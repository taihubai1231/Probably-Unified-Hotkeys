using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 针对使用非标准配置存储的知名创意工坊模组的高速专属适配器（作为 GameOptionRegistry / ModConfig 的兜底补充）
    /// 采用按程序集加载 epoch 驱动的反射成员缓存，零重复 TypeByName 全程序集扫描且严格满足 ModScanner 静态字面量校验
    /// </summary>
    public static class KnownModAdapters
    {
        private static int _assemblyEpoch = 1;
        private static bool _assemblyHooked = false;

        private static T? CreateStaticDelegate<T>(MethodInfo? method) where T : Delegate
        {
            if (method == null) return null;
            try
            {
                return (T)Delegate.CreateDelegate(typeof(T), method);
            }
            catch
            {
                return null;
            }
        }

        // 1. ShowItemNames 缓存
        private static int _showItemEpoch = -1;
        private static PropertyInfo? _showItemHotkeyProp;
        private static Func<KeyCode>? _showItemGetHotkeyFn;
        private static Action<KeyCode>? _showItemSetHotkeyPropFn;
        private static Action<KeyCode>? _showItemSetMethodFn;
        private static Action? _showItemCommitFn;

        // 2. ConflictScanner 缓存
        private static int _csEpoch = -1;
        private static PropertyInfo? _csKeyProp;
        private static Func<KeyCode>? _csGetKeyFn;
        private static Action<KeyCode>? _csSetKeyPropFn;
        private static Action<KeyCode>? _csSaveMethodFn;

        // 3. FactionReputationExpansion 缓存
        private static int _repEpoch = -1;
        private static PropertyInfo? _repJournalProp;
        private static Func<KeyCode>? _repGetJournalFn;
        private static Action<KeyCode>? _repSetJournalPropFn;
        private static Action<KeyCode, bool>? _repSetJournalMethodFn;

        // 4. ClientDemandHighlight 缓存
        private static int _demandEpoch = -1;
        private static PropertyInfo? _demandToggleProp;
        private static Func<KeyCode>? _demandGetToggleFn;
        private static Action<KeyCode>? _demandSetToggleFn;
        private static FieldInfo? _demandToggleField;

        // 5. Pen_mod_QoL_Tweaks 缓存
        private static int _penQolEpoch = -1;
        private static readonly string[] PenPropNames = new string[] { "QuickBuy", "QuickSell", "QuickAppraisal", "QuickConfront", "QuickPin", "TabLeft", "TabRight" };
        private static readonly string[] PenLabelsZh = new string[] { "一键快速购买", "一键快速出售", "一键快速估价", "一键当面对质", "一键快捷锁定", "左切换页签", "右切换页签" };
        private static readonly string[] PenLabelsEn = new string[] { "Quick Buy", "Quick Sell", "Quick Appraisal", "Quick Confront", "Quick Pin", "Previous Tab", "Next Tab" };
        private static readonly PropertyInfo?[] PenCachedProps = new PropertyInfo?[7];

        // 6. SmartInventoryArrange 缓存
        private static int _smartArrangeEpoch = -1;
        private static PropertyInfo? _smartArrangeEnabledProp;

        // 7. Probably Stolen 修改器 (probablystolen.trainer) 缓存与持久化常量
        private const string TrainerModId = "probablystolen.trainer";
        private const string TrainerHotkeyFile = "hotkey-v1.txt";
        private const string TrainerHostName = "ProbablyStolenTrainer";
        private static bool _trainerBootSynced = false;
        private static KeyCode _trainerCachedKey = KeyCode.F7;
        private static bool _trainerHasCachedKey = false;
        private static GameObject? _trainerHostCache;

        private static void EnsureAssemblyHook()
        {
            if (!_assemblyHooked)
            {
                _assemblyHooked = true;
                AppDomain.CurrentDomain.AssemblyLoad += (_, __) => System.Threading.Interlocked.Increment(ref _assemblyEpoch);
            }
        }

        public static void TryAdaptKnownMods(List<ModOptionGroup> groups, HashSet<string> handledModIds)
        {
            EnsureAssemblyHook();
            int entryEpoch = _assemblyEpoch;
            if (!handledModIds.Contains("ShowItemNames")) TryAdaptShowItemNames(groups, handledModIds);
            if (!handledModIds.Contains("conflict_scanner")) TryAdaptConflictScanner(groups, handledModIds);
            if (!handledModIds.Contains("faction_reputation_expansion")) TryAdaptFactionReputation(groups, handledModIds);
            if (!handledModIds.Contains("hollow.clientdemandhighlight")) TryAdaptClientDemandHighlight(groups, handledModIds);
            if (!handledModIds.Contains("Pen_mod_QoL_Tweaks")) TryAdaptPenQoL(groups, handledModIds);
            if (!handledModIds.Contains("smart_inventory_arrange")) TryAdaptSmartInventoryArrange(groups, handledModIds);
            if (!handledModIds.Contains(TrainerModId)) TryAdaptProbablyStolenTrainer(groups, handledModIds);

            // 若扫描自身触发了框架程序集延迟加载导致 epoch 推进，同步本轮各适配器缓存版本号，避免下次重复扫描
            int exitEpoch = _assemblyEpoch;
            if (exitEpoch != entryEpoch)
            {
                if (_showItemEpoch >= entryEpoch) _showItemEpoch = exitEpoch;
                if (_csEpoch >= entryEpoch) _csEpoch = exitEpoch;
                if (_repEpoch >= entryEpoch) _repEpoch = exitEpoch;
                if (_demandEpoch >= entryEpoch) _demandEpoch = exitEpoch;
                if (_penQolEpoch >= entryEpoch) _penQolEpoch = exitEpoch;
                if (_smartArrangeEpoch >= entryEpoch) _smartArrangeEpoch = exitEpoch;
            }
        }

        // 1. 物品名称常显 (ShowItemNames)
        private static void TryAdaptShowItemNames(List<ModOptionGroup> groups, HashSet<string> handledModIds)
        {
            int epoch = _assemblyEpoch;
            if (_showItemEpoch != epoch && _showItemHotkeyProp == null)
            {
                Type? settingsType = AccessTools.TypeByName("ShowItemNames.Settings");
                if (settingsType != null)
                {
                    _showItemHotkeyProp = AccessTools.Property(settingsType, "Hotkey");
                    _showItemGetHotkeyFn = CreateStaticDelegate<Func<KeyCode>>(_showItemHotkeyProp?.GetGetMethod(true));
                    _showItemSetHotkeyPropFn = CreateStaticDelegate<Action<KeyCode>>(_showItemHotkeyProp?.GetSetMethod(true));
                    _showItemSetMethodFn = CreateStaticDelegate<Action<KeyCode>>(AccessTools.Method(settingsType, "SetHotkey", new Type[] { typeof(KeyCode) }));
                    _showItemCommitFn = CreateStaticDelegate<Action>(AccessTools.Method(settingsType, "Commit"));
                }
                _showItemEpoch = _assemblyEpoch;
            }

            PropertyInfo? hotkeyProp = _showItemHotkeyProp;
            if (hotkeyProp == null) return;

            Func<KeyCode>? getHotkey = _showItemGetHotkeyFn;
            Action<KeyCode>? setHotkeyProp = _showItemSetHotkeyPropFn;
            Action<KeyCode>? setHotkey = _showItemSetMethodFn;
            Action? commit = _showItemCommitFn;

            string showItemName = I18n.Get("物品名称常显", "Show Item Names");
            var group = new ModOptionGroup
            {
                ModId = "ShowItemNames",
                DisplayName = showItemName,
                PinyinSortKey = PinyinEngine.ToInitials(showItemName)
            };

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = "ShowItemNames.Hotkey",
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = I18n.Get("常显开关热键", "Toggle Item Names"),
                Tooltip = I18n.Get("按压或按下该快捷键以切换物品名称常显", "Press or hold to toggle persistent item name labels"),
                DefaultKey = KeyCode.LeftAlt,
                HasExplicitDefault = true,
                GetKey = () => getHotkey != null ? getHotkey() : (KeyCode)(hotkeyProp.GetValue(null) ?? KeyCode.None),
                SetKey = (key) =>
                {
                    if (setHotkey != null)
                    {
                        setHotkey(key);
                    }
                    else
                    {
                        if (setHotkeyProp != null) setHotkeyProp(key);
                        else hotkeyProp.SetValue(null, key);
                        PlayerPrefs.SetInt("snd.hotkey", (int)key);
                        commit?.Invoke();
                    }
                }
            });

            if (group.TotalItemCount > 0)
            {
                groups.Add(group);
                handledModIds.Add(group.ModId);
            }
        }

        // 2. 模组冲突检测器 (ConflictScanner)
        private static void TryAdaptConflictScanner(List<ModOptionGroup> groups, HashSet<string> handledModIds)
        {
            int epoch = _assemblyEpoch;
            if (_csEpoch != epoch && _csKeyProp == null)
            {
                Type? keybindType = AccessTools.TypeByName("ConflictScanner.Keybind");
                if (keybindType != null)
                {
                    _csKeyProp = AccessTools.Property(keybindType, "Key");
                    _csGetKeyFn = CreateStaticDelegate<Func<KeyCode>>(_csKeyProp?.GetGetMethod(true));
                    _csSetKeyPropFn = CreateStaticDelegate<Action<KeyCode>>(_csKeyProp?.GetSetMethod(true));
                    _csSaveMethodFn = CreateStaticDelegate<Action<KeyCode>>(AccessTools.Method(keybindType, "Save", new Type[] { typeof(KeyCode) }));
                }
                _csEpoch = _assemblyEpoch;
            }

            PropertyInfo? keyProp = _csKeyProp;
            if (keyProp == null) return;

            Func<KeyCode>? getKey = _csGetKeyFn;
            Action<KeyCode>? setKeyProp = _csSetKeyPropFn;
            Action<KeyCode>? saveMethod = _csSaveMethodFn;

            string csName = I18n.Get("模组冲突检测器", "Mod Conflict Scanner");
            var group = new ModOptionGroup
            {
                ModId = "conflict_scanner",
                DisplayName = csName,
                PinyinSortKey = PinyinEngine.ToInitials(csName)
            };

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = "conflict_scanner.ReportKey",
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = I18n.Get("冲突报告窗口快捷键", "Open Conflict Report"),
                Tooltip = I18n.Get("按下后呼出模组冲突检测器诊断面板", "Press to open the Mod Conflict Scanner diagnostic window"),
                DefaultKey = KeyCode.F8,
                HasExplicitDefault = true,
                GetKey = () => getKey != null ? getKey() : (KeyCode)(keyProp.GetValue(null) ?? KeyCode.None),
                SetKey = (key) =>
                {
                    if (saveMethod != null)
                    {
                        saveMethod(key);
                    }
                    else
                    {
                        if (setKeyProp != null) setKeyProp(key);
                        else keyProp.SetValue(null, key);
                        PlayerPrefs.SetString("conflict_scanner.report_key", key.ToString());
                        PlayerPrefs.Save();
                    }
                }
            });

            if (group.TotalItemCount > 0)
            {
                groups.Add(group);
                handledModIds.Add(group.ModId);
            }
        }

        // 3. 势力声望拓展 (FactionReputationExpansion)
        private static void TryAdaptFactionReputation(List<ModOptionGroup> groups, HashSet<string> handledModIds)
        {
            int epoch = _assemblyEpoch;
            if (_repEpoch != epoch && _repJournalProp == null)
            {
                Type? repModType = AccessTools.TypeByName("FactionReputationExpansion.RepMod");
                if (repModType != null)
                {
                    _repJournalProp = AccessTools.Property(repModType, "JournalKey");
                    _repGetJournalFn = CreateStaticDelegate<Func<KeyCode>>(_repJournalProp?.GetGetMethod(true));
                    _repSetJournalPropFn = CreateStaticDelegate<Action<KeyCode>>(_repJournalProp?.GetSetMethod(true));
                    _repSetJournalMethodFn = CreateStaticDelegate<Action<KeyCode, bool>>(AccessTools.Method(repModType, "SetJournalKey", new Type[] { typeof(KeyCode), typeof(bool) }));
                }
                _repEpoch = _assemblyEpoch;
            }

            PropertyInfo? journalProp = _repJournalProp;
            if (journalProp == null) return;

            Func<KeyCode>? getJournalKey = _repGetJournalFn;
            Action<KeyCode>? setJournalProp = _repSetJournalPropFn;
            Action<KeyCode, bool>? setJournalKey = _repSetJournalMethodFn;

            string repName = I18n.Get("势力声望扩展", "Faction Reputation Expansion");
            var group = new ModOptionGroup
            {
                ModId = "faction_reputation_expansion",
                DisplayName = repName,
                PinyinSortKey = PinyinEngine.ToInitials(repName)
            };

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = "faction_reputation.journal_key",
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = I18n.Get("打开势力声望手帐", "Open Faction Journal"),
                Tooltip = I18n.Get("按此键在游戏中打开或关闭各势力声望详情界面", "Toggle the Faction Reputation journal in-game"),
                DefaultKey = KeyCode.J,
                HasExplicitDefault = true,
                GetKey = () => getJournalKey != null ? getJournalKey() : (KeyCode)(journalProp.GetValue(null) ?? KeyCode.None),
                SetKey = (key) =>
                {
                    if (setJournalKey != null)
                    {
                        setJournalKey(key, false);
                    }
                    else if (setJournalProp != null)
                    {
                        setJournalProp(key);
                    }
                    else
                    {
                        journalProp.SetValue(null, key);
                    }
                }
            });

            if (group.TotalItemCount > 0)
            {
                groups.Add(group);
                handledModIds.Add(group.ModId);
            }
        }

        // 4. 客户需求高亮 (ClientDemandHighlight - 兜底适配，若已被 GameOptionRegistry 收编则跳过)
        private static void TryAdaptClientDemandHighlight(List<ModOptionGroup> groups, HashSet<string> handledModIds)
        {
            int epoch = _assemblyEpoch;
            if (_demandEpoch != epoch && _demandToggleProp == null && _demandToggleField == null)
            {
                Type? cfgType = AccessTools.TypeByName("ClientDemandHighlight.Config");
                if (cfgType != null)
                {
                    _demandToggleProp = AccessTools.Property(cfgType, "ToggleKey");
                    _demandGetToggleFn = CreateStaticDelegate<Func<KeyCode>>(_demandToggleProp?.GetGetMethod(true));
                    _demandSetToggleFn = CreateStaticDelegate<Action<KeyCode>>(_demandToggleProp?.GetSetMethod(true));
                    _demandToggleField = AccessTools.Field(cfgType, "_toggleKey");
                }
                _demandEpoch = _assemblyEpoch;
            }

            PropertyInfo? toggleProp = _demandToggleProp;
            Func<KeyCode>? getToggle = _demandGetToggleFn;
            Action<KeyCode>? setToggle = _demandSetToggleFn;
            FieldInfo? toggleField = _demandToggleField;
            if (toggleProp == null && toggleField == null) return;

            string cdhName = I18n.Get("客户需求高亮", "Client Demand Highlight");
            var group = new ModOptionGroup
            {
                ModId = "hollow.clientdemandhighlight",
                DisplayName = cdhName,
                PinyinSortKey = PinyinEngine.ToInitials(cdhName)
            };

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = "clientdemandhighlight.toggle_key",
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = I18n.Get("切换高亮热键", "Toggle Highlight Key"),
                Tooltip = I18n.Get("按此键切换是否高亮当前顾客需求的特殊物品", "Toggle highlighting items requested by the current customer"),
                DefaultKey = KeyCode.H,
                HasExplicitDefault = true,
                GetKey = () => getToggle != null ? getToggle() : (KeyCode)((toggleProp?.GetValue(null) ?? toggleField?.GetValue(null)) ?? KeyCode.None),
                SetKey = (key) =>
                {
                    if (setToggle != null) setToggle(key);
                    else if (toggleProp != null && toggleProp.CanWrite) toggleProp.SetValue(null, key);
                    else toggleField?.SetValue(null, key);
                    PlayerPrefs.SetInt("clientdemandhighlight.key", (int)key);
                    PlayerPrefs.Save();
                }
            });

            if (group.TotalItemCount > 0)
            {
                groups.Add(group);
                handledModIds.Add(group.ModId);
            }
        }

        // 5. Pen 便利性优化 (Pen_mod_QoL_Tweaks - 兜底适配，若已被 GameOptionRegistry 收编则跳过)
        private static void TryAdaptPenQoL(List<ModOptionGroup> groups, HashSet<string> handledModIds)
        {
            int epoch = _assemblyEpoch;
            if (_penQolEpoch != epoch && PenCachedProps[0] == null)
            {
                Type? qolKeys = AccessTools.TypeByName("PenLabelerAssist.QoLKeyBindings");
                if (qolKeys != null)
                {
                    for (int i = 0; i < PenPropNames.Length; i++)
                    {
                        PenCachedProps[i] = AccessTools.Property(qolKeys, PenPropNames[i]);
                    }
                }
                _penQolEpoch = _assemblyEpoch;
            }

            bool hasAny = false;
            for (int i = 0; i < PenCachedProps.Length; i++)
            {
                if (PenCachedProps[i] != null)
                {
                    hasAny = true;
                    break;
                }
            }
            if (!hasAny) return;

            string penName = I18n.Get("Pen的便利性优化", "Pen's QoL Tweaks");
            var group = new ModOptionGroup
            {
                ModId = "Pen_mod_QoL_Tweaks",
                DisplayName = penName,
                PinyinSortKey = PinyinEngine.ToInitials(penName)
            };

            for (int i = 0; i < PenPropNames.Length; i++)
            {
                string propName = PenPropNames[i];
                string label = I18n.IsChinese ? PenLabelsZh[i] : PenLabelsEn[i];
                PropertyInfo? prop = PenCachedProps[i];
                if (prop != null)
                {
                    group.Hotkeys.Add(new ModHotkeyEntry
                    {
                        Id = $"pen_qol.{propName.ToLowerInvariant()}",
                        ModId = group.ModId,
                        ModDisplayName = group.DisplayName,
                        DisplayName = label,
                        Tooltip = I18n.Format("Pen便利性优化操作快捷键：{0}", "Pen's QoL Tweaks hotkey: {0}", label),
                        DefaultKey = (KeyCode)(prop.GetValue(null) ?? KeyCode.None),
                        HasExplicitDefault = false,
                        GetKey = () => (KeyCode)(prop.GetValue(null) ?? KeyCode.None),
                        SetKey = (key) =>
                        {
                            if (prop.CanWrite) prop.SetValue(null, key);
                        }
                    });
                }
            }

            if (group.TotalItemCount > 0)
            {
                groups.Add(group);
                handledModIds.Add(group.ModId);
            }
        }

        // 6. 智能物品整理 (SmartInventoryArrange)
        private static void TryAdaptSmartInventoryArrange(List<ModOptionGroup> groups, HashSet<string> handledModIds)
        {
            int epoch = _assemblyEpoch;
            if (_smartArrangeEpoch != epoch && _smartArrangeEnabledProp == null)
            {
                Type? runtimeType = AccessTools.TypeByName("SmartInventoryArrange.SettingsRuntime");
                if (runtimeType != null)
                {
                    _smartArrangeEnabledProp = AccessTools.Property(runtimeType, "ArrangeHotkeyEnabled");
                }
                _smartArrangeEpoch = _assemblyEpoch;
            }

            PropertyInfo? enabledProp = _smartArrangeEnabledProp;
            if (enabledProp == null) return;

            string siaName = I18n.Get("智能物品整理", "Smart Inventory Arrange");
            var group = new ModOptionGroup
            {
                ModId = "smart_inventory_arrange",
                DisplayName = siaName,
                PinyinSortKey = PinyinEngine.ToInitials(siaName)
            };

            group.GeneralSettings.Add(new ModGeneralEntry
            {
                Id = "smart_inventory_arrange.enabled",
                ModId = group.ModId,
                DisplayName = I18n.Get("启用快捷键一键整理", "Enable Quick Arrange Hotkey"),
                Tooltip = I18n.Get("关闭后快捷键整理功能将暂停响应", "Disables quick inventory arrange hotkey when unchecked"),
                Kind = OptionKind.Toggle,
                GetBool = () => (bool)(enabledProp.GetValue(null) ?? true),
                SetBool = (val) => { if (enabledProp.CanWrite) enabledProp.SetValue(null, val); }
            });

            if (group.TotalItemCount > 0)
            {
                groups.Add(group);
                handledModIds.Add(group.ModId);
            }
        }

        // 7. Probably Stolen 修改器 (probablystolen.trainer)
        // 无命名空间、未使用 ModConfig/GameOptionRegistry；严格遵守 ModScanner 边界：
        // 只读 ModHelper.ReadModDataFileText 读取键位，运行时通过 GameObject.SendMessage("SetHotkey") 安全写入与持久化
        private static bool IsTrainerNativeKeyboardKey(KeyCode key)
        {
            int code = (int)key;
            if (code == 0 || code == 27) return false; // None 或 Escape
            if (code >= 323 && code <= 329) return false; // Mouse0..Mouse6
            return !key.ToString().StartsWith("Joystick", StringComparison.OrdinalIgnoreCase);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static GameObject? FindTrainerHostCore()
        {
            if (_trainerHostCache != null)
            {
                return _trainerHostCache;
            }
            _trainerHostCache = GameObject.Find(TrainerHostName);
            return _trainerHostCache;
        }

        private static GameObject? TryFindTrainerHost()
        {
            try { return FindTrainerHostCore(); }
            catch { return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SendTrainerHotkeyCore(GameObject host, KeyCode key)
        {
            host.SendMessage("SetHotkey", key, SendMessageOptions.DontRequireReceiver);
        }

        private static bool TrySendTrainerHotkey(GameObject host, KeyCode key, out Exception? error)
        {
            error = null;
            try
            {
                SendTrainerHotkeyCore(host, key);
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static bool TryReadTrainerSavedHotkey(out KeyCode key)
        {
            key = KeyCode.F7;
            try
            {
                string? raw = ModHelper.ReadModDataFileText(TrainerModId, TrainerHotkeyFile);
                if (!string.IsNullOrWhiteSpace(raw) && Enum.TryParse<KeyCode>(raw.Trim(), ignoreCase: true, out KeyCode parsed))
                {
                    key = parsed;
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// 在主菜单空闲预热或首扫时补同步“清空(None) / 鼠标侧键(Mouse3~6)”等原版 LoadHotkey 会跳过的持久键位。
        /// </summary>
        public static void SyncExternalNonConfigHotkeysOnBoot()
        {
            if (_trainerBootSynced) return;

            GameObject? host = TryFindTrainerHost();
            if (host == null) return;

            _trainerBootSynced = true;
            if (TryReadTrainerSavedHotkey(out KeyCode savedKey))
            {
                _trainerCachedKey = savedKey;
                _trainerHasCachedKey = true;
                if (!IsTrainerNativeKeyboardKey(savedKey))
                {
                    TrySendTrainerHotkey(host, savedKey, out _);
                }
            }
        }

        private static void TryAdaptProbablyStolenTrainer(List<ModOptionGroup> groups, HashSet<string> handledModIds)
        {
            LoadedMod? trainerMod = null;
            try
            {
                var mods = ModLoader.Mods;
                if (mods != null)
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        var lm = mods[i];
                        if (lm?.Manifest != null && string.Equals(lm.Manifest.ID, TrainerModId, StringComparison.OrdinalIgnoreCase))
                        {
                            trainerMod = lm;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            bool isEnabled = false;
            if (trainerMod != null)
            {
                try
                {
                    var enabled = ModLoader.EnabledMods;
                    if (enabled != null)
                    {
                        foreach (string id in enabled)
                        {
                            if (string.Equals(id, TrainerModId, StringComparison.OrdinalIgnoreCase))
                            {
                                isEnabled = true;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            GameObject? hostCheck = TryFindTrainerHost();
            if (!isEnabled && hostCheck == null)
            {
                return;
            }

            SyncExternalNonConfigHotkeysOnBoot();

            string displayName = trainerMod?.Manifest != null && !string.IsNullOrEmpty(trainerMod.Manifest.Name)
                ? trainerMod.Manifest.Name
                : I18n.Get("Probably Stolen 修改器", "Probably Stolen Trainer");

            if (TryReadTrainerSavedHotkey(out KeyCode diskKey))
            {
                _trainerCachedKey = diskKey;
                _trainerHasCachedKey = true;
            }
            else if (!_trainerHasCachedKey)
            {
                _trainerCachedKey = KeyCode.F7;
                _trainerHasCachedKey = true;
            }

            var group = new ModOptionGroup
            {
                ModId = TrainerModId,
                DisplayName = displayName,
                PinyinSortKey = PinyinEngine.ToInitials(displayName)
            };

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = "probablystolen.trainer.toggle",
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = I18n.Get("开关修改器窗口", "Toggle Trainer Overlay"),
                Tooltip = I18n.Get(
                    "打开/关闭修改器悬浮窗（支持单键、鼠标侧键与 Esc 清空为无；因原模组未使用官方配置与命名空间，受安全扫描限制暂不支持组合键前缀）",
                    "Toggle trainer overlay (supports single keys, mouse side buttons & Esc to clear; modifier combos unavailable under sandbox scan)"
                ),
                DefaultKey = KeyCode.F7,
                DefaultModifiers = KeyModifierMask.None,
                HasExplicitDefault = true,
                SupportsModifierCombos = false,
                GetKey = () =>
                {
                    if (TryReadTrainerSavedHotkey(out KeyCode latest))
                    {
                        _trainerCachedKey = latest;
                        _trainerHasCachedKey = true;
                        return latest;
                    }
                    return _trainerHasCachedKey ? _trainerCachedKey : KeyCode.F7;
                },
                SetKey = (val) =>
                {
                    _trainerCachedKey = val;
                    _trainerHasCachedKey = true;
                    _trainerBootSynced = true;
                    GameObject? host = TryFindTrainerHost();
                    if (host != null && !TrySendTrainerHotkey(host, val, out Exception? ex) && ex != null)
                    {
                        Debug.LogWarning($"[ProbablyUnifiedHotkeys] 同步修改器热键失败: {ex.Message}");
                    }
                }
            });

            if (group.TotalItemCount > 0)
            {
                groups.Add(group);
                handledModIds.Add(group.ModId);
            }
        }
    }
}
