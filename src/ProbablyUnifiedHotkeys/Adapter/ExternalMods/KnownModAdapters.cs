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
            catch (Exception ex)
            {
                ModLogger.Debug($"Failed to create static delegate for '{method.Name}': {ex.Message}");
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
        private static PropertyInfo? _showItemEnabledProp;
        private static Action<bool>? _showItemSetEnabledFn;
        private static PropertyInfo? _showItemHideOtherProp;
        private static PropertyInfo? _showItemAutoScaleProp;
        private static Action<bool>? _showItemSetAutoScaleFn;
        private static PropertyInfo? _showItemAutoWrapProp;
        private static Action<bool>? _showItemSetAutoWrapFn;
        private static PropertyInfo? _showItemFontScaleProp;
        private static Action<float>? _showItemSetFontScaleFn;
        private static PropertyInfo? _showItemPlateAlphaProp;
        private static Action<float>? _showItemSetPlateAlphaFn;

        // 2. ConflictScanner 缓存
        private static int _csEpoch = -1;
        private static PropertyInfo? _csKeyProp;
        private static FieldInfo? _csKeyField;
        private static FieldInfo? _csLoadedField;
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
        private static readonly string[] PenPropNames = new string[]
        {
            "QuickBuy", "QuickSell", "QuickAppraisal", "QuickConfront", "QuickPin",
            "TabLeft", "TabRight", "FollowModifier", "SnapModifier", "BoxSelectModifier", "MultiSelectToggle"
        };
        private static readonly string[] PenLabelsZh = new string[]
        {
            "一键快速购买", "一键快速出售", "一键快速估价", "一键当面对质", "一键快捷锁定",
            "左切换页签", "右切换页签", "窗口跟随修饰键", "窗口边缘吸附修饰键", "框选物品修饰键", "多选开关修饰键"
        };
        private static readonly string[] PenLabelsEn = new string[]
        {
            "Quick Buy", "Quick Sell", "Quick Appraisal", "Quick Confront", "Quick Pin",
            "Previous Tab", "Next Tab", "Window Follow Modifier", "Window Snap Modifier", "Box Select Modifier", "Multi-Select Toggle"
        };
        private static readonly PropertyInfo?[] PenCachedProps = new PropertyInfo?[11];

        // 6. SmartInventoryArrange 缓存
        private static int _smartArrangeEpoch = -1;
        private static PropertyInfo? _smartArrangeEnabledProp;
        private static PropertyInfo? _smartArrangeFixedEnabledProp;
        private static PropertyInfo? _smartArrangeGuiProp;
        private static Action<bool>? _smartArrangeSetBatchFn;
        private static Action<bool>? _smartArrangeSetFixedFn;
        private static FieldInfo? _smartArrangeGuiModelField;
        private static MethodInfo? _smartArrangeGuiSaveMethod;
        private static PropertyInfo? _smartArrangeModelOpenProp;
        private static PropertyInfo? _smartArrangeModelArrangeProp;
        private static PropertyInfo? _smartArrangeModelUndoProp;
        private static ConstructorInfo? _smartArrangeHotkeyCtor;
        private static PropertyInfo? _smartArrangeHotkeyKeyProp;
        private static PropertyInfo? _smartArrangeHotkeyCtrlProp;
        private static PropertyInfo? _smartArrangeHotkeyAltProp;
        private static PropertyInfo? _smartArrangeHotkeyShiftProp;

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

        public static void EnsureLazyModConfigsInitialized()
        {
            EnsureAssemblyHook();
            try
            {
                EnsureSmartArrangeReflectionCached();
                if (_smartArrangeGuiProp != null)
                {
                    // 访问 SettingsRuntime.Gui 会触发 UnifiedSettingsGui 构造并将 OpenSettingsHotkey / ArrangeOpenContainersHotkey / UndoArrangeHotkey 注册进 ModConfig
                    _ = _smartArrangeGuiProp.GetValue(null);
                    SyncSmartArrangeRuntimeHotkeysFromStore();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"EnsureLazyModConfigsInitialized error: {ex.Message}");
            }
        }

        private static void EnsureSmartArrangeReflectionCached()
        {
            int epoch = _assemblyEpoch;
            if (_smartArrangeEpoch == epoch && _smartArrangeEnabledProp != null) return;

            Type? runtimeType = AccessTools.TypeByName("SmartInventoryArrange.SettingsRuntime");
            if (runtimeType != null)
            {
                _smartArrangeEnabledProp = AccessTools.Property(runtimeType, "ArrangeHotkeyEnabled");
                _smartArrangeFixedEnabledProp = AccessTools.Property(runtimeType, "ExperimentalAltPlacement");
                _smartArrangeGuiProp = AccessTools.Property(runtimeType, "Gui");
                _smartArrangeSetBatchFn = CreateStaticDelegate<Action<bool>>(AccessTools.Method(runtimeType, "SetBatch", new Type[] { typeof(bool) }));
                _smartArrangeSetFixedFn = CreateStaticDelegate<Action<bool>>(AccessTools.Method(runtimeType, "SetFixedPlacement", new Type[] { typeof(bool) }));
            }

            Type? guiType = AccessTools.TypeByName("SmartInventoryArrange.UnifiedSettingsGui");
            if (guiType != null)
            {
                _smartArrangeGuiModelField = AccessTools.Field(guiType, "Model");
                _smartArrangeGuiSaveMethod = AccessTools.Method(guiType, "Save");
            }

            Type? hotkeysType = AccessTools.TypeByName("SmartInventoryArrange.SettingsHotkeys");
            if (hotkeysType != null)
            {
                _smartArrangeModelOpenProp = AccessTools.Property(hotkeysType, "Open");
                _smartArrangeModelArrangeProp = AccessTools.Property(hotkeysType, "Arrange");
                _smartArrangeModelUndoProp = AccessTools.Property(hotkeysType, "Undo");
            }

            Type? hotkeyStructType = AccessTools.TypeByName("SmartInventoryArrange.SettingsHotkey");
            if (hotkeyStructType != null)
            {
                _smartArrangeHotkeyCtor = AccessTools.Constructor(hotkeyStructType, new Type[] { typeof(KeyCode), typeof(bool), typeof(bool), typeof(bool) });
                _smartArrangeHotkeyKeyProp = AccessTools.Property(hotkeyStructType, "Key");
                _smartArrangeHotkeyCtrlProp = AccessTools.Property(hotkeyStructType, "Ctrl");
                _smartArrangeHotkeyAltProp = AccessTools.Property(hotkeyStructType, "Alt");
                _smartArrangeHotkeyShiftProp = AccessTools.Property(hotkeyStructType, "Shift");
            }

            _smartArrangeEpoch = _assemblyEpoch;
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

                    _showItemEnabledProp = AccessTools.Property(settingsType, "Enabled");
                    _showItemSetEnabledFn = CreateStaticDelegate<Action<bool>>(AccessTools.Method(settingsType, "SetEnabled", new Type[] { typeof(bool) }));
                    _showItemHideOtherProp = AccessTools.Property(settingsType, "HideOtherNameLabels");
                    _showItemAutoScaleProp = AccessTools.Property(settingsType, "AutoScale");
                    _showItemSetAutoScaleFn = CreateStaticDelegate<Action<bool>>(AccessTools.Method(settingsType, "SetAutoScale", new Type[] { typeof(bool) }));
                    _showItemAutoWrapProp = AccessTools.Property(settingsType, "AutoWrap");
                    _showItemSetAutoWrapFn = CreateStaticDelegate<Action<bool>>(AccessTools.Method(settingsType, "SetAutoWrap", new Type[] { typeof(bool) }));
                    _showItemFontScaleProp = AccessTools.Property(settingsType, "FontScale");
                    _showItemSetFontScaleFn = CreateStaticDelegate<Action<float>>(AccessTools.Method(settingsType, "SetFontScale", new Type[] { typeof(float) }));
                    _showItemPlateAlphaProp = AccessTools.Property(settingsType, "PlateAlpha");
                    _showItemSetPlateAlphaFn = CreateStaticDelegate<Action<float>>(AccessTools.Method(settingsType, "SetPlateAlpha", new Type[] { typeof(float) }));
                }
                _showItemEpoch = _assemblyEpoch;
            }

            PropertyInfo? hotkeyProp = _showItemHotkeyProp;
            if (hotkeyProp == null) return;

            Func<KeyCode>? getHotkey = _showItemGetHotkeyFn;
            Action<KeyCode>? setHotkeyProp = _showItemSetHotkeyPropFn;
            Action<KeyCode>? setHotkey = _showItemSetMethodFn;
            Action? commit = _showItemCommitFn;

            string showItemName = BilingualText.Get("物品名称常显", "Show Item Names");
            var group = new ModOptionGroup
            {
                ModId = "ShowItemNames",
                DisplayName = showItemName,
                PinyinSortKey = PinyinEngine.ToInitials(showItemName)
            };

            const string entryId = "ShowItemNames.Hotkey";
            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = entryId,
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = BilingualText.Get("常显开关热键", "Toggle Item Names"),
                Tooltip = BilingualText.Get("按压或按下该快捷键以切换物品名称常显（支持单键、组合键、鼠标侧键与 Esc 清空）", "Press to toggle persistent item name labels"),
                DefaultKey = KeyCode.LeftAlt,
                HasExplicitDefault = true,
                GetKey = () =>
                {
                    KeyCode raw = getHotkey != null ? getHotkey() : (KeyCode)(hotkeyProp.GetValue(null) ?? KeyCode.None);
                    return ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, raw).Key;
                },
                GetExternalModifiers = () =>
                {
                    KeyCode raw = getHotkey != null ? getHotkey() : (KeyCode)(hotkeyProp.GetValue(null) ?? KeyCode.None);
                    return ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, raw).Modifiers;
                },
                SetKey = (key) =>
                {
                    ComboKeyStore.SetSavedCustomHotkey(entryId, KeyModifierMask.None, key, KeyModifierMask.None, KeyCode.LeftAlt);
                    if (setHotkey != null) setHotkey(key);
                    else
                    {
                        if (setHotkeyProp != null) setHotkeyProp(key);
                        else hotkeyProp.SetValue(null, key);
                        try { PrefsStorage.SetInt("snd.hotkey", (int)key); }
                        catch (Exception ex) { ModLogger.Debug($"PrefsStorage.SetInt snd.hotkey error: {ex.Message}"); }
                        commit?.Invoke();
                    }
                },
                SetCombo = (mods, key) =>
                {
                    ComboKeyStore.SetSavedCustomHotkey(entryId, mods, key, KeyModifierMask.None, KeyCode.LeftAlt);
                    if (setHotkey != null) setHotkey(key);
                    else
                    {
                        if (setHotkeyProp != null) setHotkeyProp(key);
                        else hotkeyProp.SetValue(null, key);
                        try { PrefsStorage.SetInt("snd.hotkey", (int)key); }
                        catch (Exception ex) { ModLogger.Debug($"PrefsStorage.SetInt snd.hotkey error: {ex.Message}"); }
                        commit?.Invoke();
                    }
                }
            });

            if (_showItemEnabledProp != null)
            {
                var prop = _showItemEnabledProp;
                var setter = _showItemSetEnabledFn;
                group.GeneralSettings.Add(new ModGeneralEntry
                {
                    Id = "ShowItemNames.Enabled",
                    ModId = group.ModId,
                    DisplayName = BilingualText.Get("启用物品名称常显", "Enable Item Name Labels"),
                    Tooltip = BilingualText.Get("是否当前开启物品名称常显", "Whether persistent item name labels are currently enabled"),
                    Kind = OptionKind.Toggle,
                    GetBool = () => (bool)(prop.GetValue(null) ?? true),
                    SetBool = (val) =>
                    {
                        if (setter != null) { setter(val); commit?.Invoke(); }
                        else if (prop.CanWrite) { prop.SetValue(null, val); commit?.Invoke(); }
                    }
                });
            }

            if (_showItemHideOtherProp != null && _showItemHideOtherProp.CanWrite)
            {
                var prop = _showItemHideOtherProp;
                group.GeneralSettings.Add(new ModGeneralEntry
                {
                    Id = "ShowItemNames.HideOtherNameLabels",
                    ModId = group.ModId,
                    DisplayName = BilingualText.Get("隐藏原版悬停名称标签", "Hide Vanilla Hover Name Labels"),
                    Tooltip = BilingualText.Get("开启常显时隐藏原版重复的物品名称标签", "Hide duplicate vanilla item name labels while persistent names are active"),
                    Kind = OptionKind.Toggle,
                    GetBool = () => (bool)(prop.GetValue(null) ?? true),
                    SetBool = (val) => { prop.SetValue(null, val); commit?.Invoke(); }
                });
            }

            if (_showItemAutoScaleProp != null)
            {
                var prop = _showItemAutoScaleProp;
                var setter = _showItemSetAutoScaleFn;
                group.GeneralSettings.Add(new ModGeneralEntry
                {
                    Id = "ShowItemNames.AutoScale",
                    ModId = group.ModId,
                    DisplayName = BilingualText.Get("随镜头自动缩放字号", "Auto Scale Font"),
                    Tooltip = BilingualText.Get("根据物品尺寸与视野自动调节名称标签字号", "Automatically scale label font size"),
                    Kind = OptionKind.Toggle,
                    GetBool = () => (bool)(prop.GetValue(null) ?? true),
                    SetBool = (val) =>
                    {
                        if (setter != null) { setter(val); commit?.Invoke(); }
                        else if (prop.CanWrite) { prop.SetValue(null, val); commit?.Invoke(); }
                    }
                });
            }

            if (_showItemAutoWrapProp != null)
            {
                var prop = _showItemAutoWrapProp;
                var setter = _showItemSetAutoWrapFn;
                group.GeneralSettings.Add(new ModGeneralEntry
                {
                    Id = "ShowItemNames.AutoWrap",
                    ModId = group.ModId,
                    DisplayName = BilingualText.Get("长名称自动换行", "Auto Wrap Long Names"),
                    Tooltip = BilingualText.Get("较长的物品名称自动分行显示", "Automatically wrap long item names onto multiple lines"),
                    Kind = OptionKind.Toggle,
                    GetBool = () => (bool)(prop.GetValue(null) ?? true),
                    SetBool = (val) =>
                    {
                        if (setter != null) { setter(val); commit?.Invoke(); }
                        else if (prop.CanWrite) { prop.SetValue(null, val); commit?.Invoke(); }
                    }
                });
            }

            if (_showItemFontScaleProp != null)
            {
                var prop = _showItemFontScaleProp;
                var setter = _showItemSetFontScaleFn;
                group.GeneralSettings.Add(new ModGeneralEntry
                {
                    Id = "ShowItemNames.FontScale",
                    ModId = group.ModId,
                    DisplayName = BilingualText.Get("名称字号倍率", "Font Scale"),
                    Tooltip = BilingualText.Get("调整物品名称文字缩放比例 (0.5 ~ 2.0)", "Adjust label font scale (0.5 ~ 2.0)"),
                    Kind = OptionKind.Slider,
                    Min = 0.5f,
                    Max = 2.0f,
                    GetFloat = () => (float)(prop.GetValue(null) ?? 1.0f),
                    SetFloat = (val) =>
                    {
                        if (setter != null) { setter(val); commit?.Invoke(); }
                        else if (prop.CanWrite) { prop.SetValue(null, val); commit?.Invoke(); }
                    }
                });
            }

            if (_showItemPlateAlphaProp != null)
            {
                var prop = _showItemPlateAlphaProp;
                var setter = _showItemSetPlateAlphaFn;
                group.GeneralSettings.Add(new ModGeneralEntry
                {
                    Id = "ShowItemNames.PlateAlpha",
                    ModId = group.ModId,
                    DisplayName = BilingualText.Get("名称底板不透明度", "Background Plate Opacity"),
                    Tooltip = BilingualText.Get("调整物品名称半透明背景底板的不透明度 (0.0 ~ 1.0)", "Adjust label background plate opacity (0.0 ~ 1.0)"),
                    Kind = OptionKind.Slider,
                    Min = 0.0f,
                    Max = 1.0f,
                    GetFloat = () => (float)(prop.GetValue(null) ?? 0.65f),
                    SetFloat = (val) =>
                    {
                        if (setter != null) { setter(val); commit?.Invoke(); }
                        else if (prop.CanWrite) { prop.SetValue(null, val); commit?.Invoke(); }
                    }
                });
            }

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
                    _csKeyField = AccessTools.Field(keybindType, "_key");
                    _csLoadedField = AccessTools.Field(keybindType, "_loaded");
                    _csGetKeyFn = CreateStaticDelegate<Func<KeyCode>>(_csKeyProp?.GetGetMethod(true));
                    _csSetKeyPropFn = CreateStaticDelegate<Action<KeyCode>>(_csKeyProp?.GetSetMethod(true));
                    _csSaveMethodFn = CreateStaticDelegate<Action<KeyCode>>(AccessTools.Method(keybindType, "Save", new Type[] { typeof(KeyCode) }));
                }
                _csEpoch = _assemblyEpoch;
            }

            PropertyInfo? keyProp = _csKeyProp;
            FieldInfo? keyField = _csKeyField;
            if (keyProp == null && keyField == null) return;

            Func<KeyCode>? getKey = _csGetKeyFn;
            Action<KeyCode>? setKeyProp = _csSetKeyPropFn;
            Action<KeyCode>? saveMethod = _csSaveMethodFn;

            string csName = BilingualText.Get("模组冲突检测器", "Mod Conflict Scanner");
            var group = new ModOptionGroup
            {
                ModId = "conflict_scanner",
                DisplayName = csName,
                PinyinSortKey = PinyinEngine.ToInitials(csName)
            };

            const string csEntryId = "conflict_scanner.ReportKey";
            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = csEntryId,
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = BilingualText.Get("冲突报告窗口快捷键", "Open Conflict Report"),
                Tooltip = BilingualText.Get("按下后呼出模组冲突检测器诊断面板（默认 F7；支持单键、组合键、鼠标侧键与 Esc 清空）", "Press to open the Mod Conflict Scanner diagnostic window (default F7; supports rebinding, combos & Esc)"),
                DefaultKey = KeyCode.F7,
                HasExplicitDefault = true,
                SupportsModifierCombos = true,
                GetKey = () =>
                {
                    KeyCode raw = getKey != null ? getKey() : (KeyCode)((_csKeyProp?.GetValue(null) ?? _csKeyField?.GetValue(null)) ?? KeyCode.None);
                    return ComboKeyStore.GetEffectiveCustomHotkey(csEntryId, KeyModifierMask.None, raw).Key;
                },
                GetExternalModifiers = () =>
                {
                    KeyCode raw = getKey != null ? getKey() : (KeyCode)((_csKeyProp?.GetValue(null) ?? _csKeyField?.GetValue(null)) ?? KeyCode.None);
                    return ComboKeyStore.GetEffectiveCustomHotkey(csEntryId, KeyModifierMask.None, raw).Modifiers;
                },
                SetKey = (key) =>
                {
                    ComboKeyStore.SetSavedCustomHotkey(csEntryId, KeyModifierMask.None, key, KeyModifierMask.None, KeyCode.F7);
                    _csKeyField?.SetValue(null, key);
                    _csLoadedField?.SetValue(null, true);
                    if (setKeyProp != null) setKeyProp(key);
                    else _csKeyProp?.SetValue(null, key);

                    if (key != KeyCode.None && saveMethod != null)
                    {
                        saveMethod(key);
                    }
                    else
                    {
                        try
                        {
                            PrefsStorage.SetString("conflict_scanner.report_key", key == KeyCode.None ? "None" : key.ToString());
                            PrefsStorage.Save();
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Debug($"PrefsStorage.SetString conflict_scanner.report_key error: {ex.Message}");
                        }
                    }
                },
                SetCombo = (mods, key) =>
                {
                    ComboKeyStore.SetSavedCustomHotkey(csEntryId, mods, key, KeyModifierMask.None, KeyCode.F7);
                    _csKeyField?.SetValue(null, key);
                    _csLoadedField?.SetValue(null, true);
                    if (setKeyProp != null) setKeyProp(key);
                    else _csKeyProp?.SetValue(null, key);

                    if (key != KeyCode.None && saveMethod != null)
                    {
                        saveMethod(key);
                    }
                    else
                    {
                        try
                        {
                            PrefsStorage.SetString("conflict_scanner.report_key", key == KeyCode.None ? "None" : key.ToString());
                            PrefsStorage.Save();
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Debug($"PrefsStorage.SetString conflict_scanner.report_key error: {ex.Message}");
                        }
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

            string repName = BilingualText.Get("势力声望扩展", "Faction Reputation Expansion");
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
                DisplayName = BilingualText.Get("打开势力声望手帐", "Open Faction Journal"),
                Tooltip = BilingualText.Get("按此键在游戏中打开或关闭各势力声望详情界面", "Toggle the Faction Reputation journal in-game"),
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

            string cdhName = BilingualText.Get("客户需求高亮", "Client Demand Highlight");
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
                DisplayName = BilingualText.Get("切换高亮热键", "Toggle Highlight Key"),
                Tooltip = BilingualText.Get("按此键切换是否高亮当前顾客需求的特殊物品", "Toggle highlighting items requested by the current customer"),
                DefaultKey = KeyCode.H,
                HasExplicitDefault = true,
                GetKey = () => getToggle != null ? getToggle() : (KeyCode)((toggleProp?.GetValue(null) ?? toggleField?.GetValue(null)) ?? KeyCode.None),
                SetKey = (key) =>
                {
                    if (setToggle != null) setToggle(key);
                    else if (toggleProp != null && toggleProp.CanWrite) toggleProp.SetValue(null, key);
                    else toggleField?.SetValue(null, key);
                    try
                    {
                        PrefsStorage.SetInt("clientdemandhighlight.key", (int)key);
                        PrefsStorage.Save();
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Debug($"PrefsStorage.SetInt clientdemandhighlight.key error: {ex.Message}");
                    }
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

            string penName = BilingualText.Get("Pen的便利性优化", "Pen's QoL Tweaks");
            var group = new ModOptionGroup
            {
                ModId = "Pen_mod_QoL_Tweaks",
                DisplayName = penName,
                PinyinSortKey = PinyinEngine.ToInitials(penName)
            };

            for (int i = 0; i < PenPropNames.Length; i++)
            {
                string propName = PenPropNames[i];
                string label = BilingualText.IsChinese ? PenLabelsZh[i] : PenLabelsEn[i];
                PropertyInfo? prop = PenCachedProps[i];
                if (prop != null)
                {
                    group.Hotkeys.Add(new ModHotkeyEntry
                    {
                        Id = $"pen_qol.{propName.ToLowerInvariant()}",
                        ModId = group.ModId,
                        ModDisplayName = group.DisplayName,
                        DisplayName = label,
                        Tooltip = BilingualText.Format("Pen便利性优化操作快捷键：{0}", "Pen's QoL Tweaks hotkey: {0}", label),
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
            EnsureSmartArrangeReflectionCached();
            PropertyInfo? enabledProp = _smartArrangeEnabledProp;
            if (enabledProp == null) return;

            string siaName = BilingualText.Get("智能物品整理", "Smart Inventory Arrange");
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
                DisplayName = BilingualText.Get("启用快捷键一键整理", "Enable Quick Arrange Hotkey"),
                Tooltip = BilingualText.Get("关闭后快捷键整理功能将暂停响应", "Disables quick inventory arrange hotkey when unchecked"),
                Kind = OptionKind.Toggle,
                GetBool = () => (bool)(enabledProp.GetValue(null) ?? true),
                SetBool = (val) => { if (enabledProp.CanWrite) enabledProp.SetValue(null, val); }
            });

            SupplementSmartInventoryArrangeGroup(group);

            if (group.TotalItemCount > 0)
            {
                groups.Add(group);
                handledModIds.Add(group.ModId);
            }
        }

        public static void ApplySupplementalModAdaptations(
            List<ModOptionGroup> groups,
            HashSet<string> enabledModIds,
            HashSet<string> adapterMatchedModIds)
        {
            EnsureAssemblyHook();
            if (groups == null) return;

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group == null || string.IsNullOrEmpty(group.ModId)) continue;

                if (string.Equals(group.ModId, "smart_inventory_arrange", StringComparison.OrdinalIgnoreCase))
                {
                    SupplementSmartInventoryArrangeGroup(group);
                    adapterMatchedModIds.Add(group.ModId);
                }
                else if (string.Equals(group.ModId, "chiqingsan.BetterPerformance", StringComparison.OrdinalIgnoreCase))
                {
                    SupplementBetterPerformanceGroup(group);
                    adapterMatchedModIds.Add(group.ModId);
                }
                else if (string.Equals(group.ModId, "precise_group_drag", StringComparison.OrdinalIgnoreCase))
                {
                    SupplementPreciseGroupDragGroup(group);
                    adapterMatchedModIds.Add(group.ModId);
                }
                else if (string.Equals(group.ModId, "item_details", StringComparison.OrdinalIgnoreCase))
                {
                    SupplementItemDetailsGroup(group);
                    adapterMatchedModIds.Add(group.ModId);
                }
            }
        }

        public static void SyncSmartArrangeRuntimeHotkeysFromStore()
        {
            try
            {
                EnsureSmartArrangeReflectionCached();
                object? gui = _smartArrangeGuiProp?.GetValue(null);
                if (gui == null || _smartArrangeGuiModelField == null) return;
                object? model = _smartArrangeGuiModelField.GetValue(gui);
                if (model == null) return;

                SyncSingleSmartArrangeModelProp(model, _smartArrangeModelOpenProp, "smart_inventory_arrange.OpenSettingsHotkey");
                SyncSingleSmartArrangeModelProp(model, _smartArrangeModelArrangeProp, "smart_inventory_arrange.ArrangeOpenContainersHotkey");
                SyncSingleSmartArrangeModelProp(model, _smartArrangeModelUndoProp, "smart_inventory_arrange.UndoArrangeHotkey");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"SyncSmartArrangeRuntimeHotkeysFromStore error: {ex.Message}");
            }
        }

        private static void SyncSingleSmartArrangeModelProp(object model, PropertyInfo? modelProp, string entryId)
        {
            if (modelProp == null || _smartArrangeHotkeyKeyProp == null) return;
            object? curStruct = modelProp.GetValue(model);
            if (curStruct == null) return;

            KeyCode curKey = (KeyCode)(_smartArrangeHotkeyKeyProp.GetValue(curStruct) ?? KeyCode.None);
            bool curCtrl = (bool)(_smartArrangeHotkeyCtrlProp?.GetValue(curStruct) ?? false);
            bool curAlt = (bool)(_smartArrangeHotkeyAltProp?.GetValue(curStruct) ?? false);
            bool curShift = (bool)(_smartArrangeHotkeyShiftProp?.GetValue(curStruct) ?? false);

            KeyModifierMask curMods = KeyModifierMask.None;
            if (curCtrl) curMods |= KeyModifierMask.Ctrl;
            if (curAlt) curMods |= KeyModifierMask.Alt;
            if (curShift) curMods |= KeyModifierMask.Shift;

            var effective = ComboKeyStore.GetEffectiveCustomHotkey(entryId, curMods, curKey);
            if (effective.Key != curKey || effective.Modifiers != (curMods & ~KeyCombo.GetModifierBit(curKey)))
            {
                SetSmartArrangeModelHotkey(modelProp, effective.Modifiers, effective.Key, saveGui: false);
            }
        }

        private static void SetSmartArrangeModelHotkey(PropertyInfo? modelProp, KeyModifierMask mods, KeyCode key, bool saveGui)
        {
            try
            {
                EnsureSmartArrangeReflectionCached();
                if (modelProp == null || _smartArrangeGuiProp == null || _smartArrangeGuiModelField == null || _smartArrangeHotkeyCtor == null)
                {
                    return;
                }

                object? gui = _smartArrangeGuiProp.GetValue(null);
                if (gui == null) return;
                object? model = _smartArrangeGuiModelField.GetValue(gui);
                if (model == null) return;

                var normMods = key == KeyCode.None
                    ? KeyModifierMask.None
                    : (mods & ~KeyCombo.GetModifierBit(key));

                bool ctrl = (normMods & KeyModifierMask.Ctrl) != 0;
                bool alt = (normMods & KeyModifierMask.Alt) != 0;
                bool shift = (normMods & KeyModifierMask.Shift) != 0;

                object newHk = _smartArrangeHotkeyCtor.Invoke(new object[] { key, ctrl, alt, shift });
                modelProp.SetValue(model, newHk);
                if (saveGui)
                {
                    _smartArrangeGuiSaveMethod?.Invoke(gui, null);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"SetSmartArrangeModelHotkey error: {ex.Message}");
            }
        }

        private static bool TryReadSmartArrangeModelHotkey(PropertyInfo? modelProp, out KeyModifierMask mods, out KeyCode key)
        {
            mods = KeyModifierMask.None;
            key = KeyCode.None;
            try
            {
                EnsureSmartArrangeReflectionCached();
                if (modelProp == null || _smartArrangeGuiProp == null || _smartArrangeGuiModelField == null || _smartArrangeHotkeyKeyProp == null)
                {
                    return false;
                }

                object? gui = _smartArrangeGuiProp.GetValue(null);
                if (gui == null) return false;
                object? model = _smartArrangeGuiModelField.GetValue(gui);
                if (model == null) return false;
                object? hk = modelProp.GetValue(model);
                if (hk == null) return false;

                key = (KeyCode)(_smartArrangeHotkeyKeyProp.GetValue(hk) ?? KeyCode.None);
                bool ctrl = (bool)(_smartArrangeHotkeyCtrlProp?.GetValue(hk) ?? false);
                bool alt = (bool)(_smartArrangeHotkeyAltProp?.GetValue(hk) ?? false);
                bool shift = (bool)(_smartArrangeHotkeyShiftProp?.GetValue(hk) ?? false);
                if (ctrl) mods |= KeyModifierMask.Ctrl;
                if (alt) mods |= KeyModifierMask.Alt;
                if (shift) mods |= KeyModifierMask.Shift;
                mods &= ~KeyCombo.GetModifierBit(key);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"TryReadSmartArrangeModelHotkey error: {ex.Message}");
                return false;
            }
        }

        private static void SupplementSmartInventoryArrangeGroup(ModOptionGroup group)
        {
            EnsureSmartArrangeReflectionCached();
            SyncSmartArrangeRuntimeHotkeysFromStore();

            // 1. 包装已扫描的 OpenSettingsHotkey / ArrangeOpenContainersHotkey / UndoArrangeHotkey，使其读写同步到 UnifiedSettingsGui.Model
            for (int i = 0; i < group.Hotkeys.Count; i++)
            {
                var hk = group.Hotkeys[i];
                if (hk == null) continue;

                PropertyInfo? targetModelProp = null;
                bool isBatchKey = false;
                KeyCode defaultKey = hk.DefaultKey;

                if (hk.Id.EndsWith(".OpenSettingsHotkey", StringComparison.OrdinalIgnoreCase))
                {
                    targetModelProp = _smartArrangeModelOpenProp;
                    defaultKey = KeyCode.F6;
                    hk.DefaultKey = KeyCode.F6;
                    hk.HasExplicitDefault = true;
                }
                else if (hk.Id.EndsWith(".ArrangeOpenContainersHotkey", StringComparison.OrdinalIgnoreCase))
                {
                    targetModelProp = _smartArrangeModelArrangeProp;
                    isBatchKey = true;
                }
                else if (hk.Id.EndsWith(".UndoArrangeHotkey", StringComparison.OrdinalIgnoreCase))
                {
                    targetModelProp = _smartArrangeModelUndoProp;
                    isBatchKey = true;
                }

                if (targetModelProp != null)
                {
                    var capturedProp = targetModelProp;
                    var origGetKey = hk.GetKey;
                    var origGetMods = hk.GetExternalModifiers;
                    var origSetKey = hk.SetKey;
                    var origSetCombo = hk.SetCombo;
                    string entryId = hk.Id;
                    bool autoEnableBatch = isBatchKey;
                    KeyCode capDefaultKey = defaultKey;

                    hk.GetKey = () =>
                    {
                        if (TryReadSmartArrangeModelHotkey(capturedProp, out var mMods, out var mKey))
                        {
                            return ComboKeyStore.GetEffectiveCustomHotkey(entryId, mMods, mKey).Key;
                        }
                        KeyCode raw = origGetKey != null ? origGetKey() : KeyCode.None;
                        return ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, raw).Key;
                    };
                    hk.GetExternalModifiers = () =>
                    {
                        if (TryReadSmartArrangeModelHotkey(capturedProp, out var mMods, out var mKey))
                        {
                            return ComboKeyStore.GetEffectiveCustomHotkey(entryId, mMods, mKey).Modifiers;
                        }
                        KeyModifierMask rawMods = origGetMods != null ? origGetMods() : KeyModifierMask.None;
                        KeyCode rawKey = origGetKey != null ? origGetKey() : KeyCode.None;
                        return ComboKeyStore.GetEffectiveCustomHotkey(entryId, rawMods, rawKey).Modifiers;
                    };
                    hk.SetKey = (newKey) =>
                    {
                        ComboKeyStore.SetSavedCustomHotkey(entryId, KeyModifierMask.None, newKey, KeyModifierMask.None, capDefaultKey);
                        origSetKey?.Invoke(newKey);
                        SetSmartArrangeModelHotkey(capturedProp, KeyModifierMask.None, newKey, saveGui: true);
                        if (autoEnableBatch && newKey != KeyCode.None)
                        {
                            _smartArrangeSetBatchFn?.Invoke(true);
                        }
                    };
                    hk.SetCombo = (newMods, newKey) =>
                    {
                        ComboKeyStore.SetSavedCustomHotkey(entryId, newMods, newKey, KeyModifierMask.None, capDefaultKey);
                        if (origSetCombo != null) origSetCombo(newMods, newKey);
                        else origSetKey?.Invoke(newKey);
                        SetSmartArrangeModelHotkey(capturedProp, newMods, newKey, saveGui: true);
                        if (autoEnableBatch && newKey != KeyCode.None)
                        {
                            _smartArrangeSetBatchFn?.Invoke(true);
                        }
                    };
                }
            }

            // 2. 补充第 4 个硬编码快捷键：按住强制放置修饰键 (LeftAlt / RightAlt)
            const string altPlaceId = "smart_inventory_arrange.alt_placement_key";
            bool hasAltPlace = false;
            for (int i = 0; i < group.Hotkeys.Count; i++)
            {
                if (string.Equals(group.Hotkeys[i].Id, altPlaceId, StringComparison.OrdinalIgnoreCase))
                {
                    hasAltPlace = true;
                    break;
                }
            }

            if (!hasAltPlace)
            {
                group.Hotkeys.Add(new ModHotkeyEntry
                {
                    Id = altPlaceId,
                    ModId = group.ModId,
                    ModDisplayName = group.DisplayName,
                    DisplayName = BilingualText.Get("按住强制放置修饰键", "Force Placement Hold Key"),
                    Tooltip = BilingualText.Get(
                        "拖拽物品时按住此键可进行精细强制放置（默认 LeftAlt；支持改键、组合键、鼠标侧键与 Esc 清空，绑定按键时自动启用实验性强制放置）",
                        "Hold while dragging items to force free placement (default LeftAlt; supports rebinding, combos, mouse side buttons & Esc to clear)"
                    ),
                    DefaultKey = KeyCode.LeftAlt,
                    DefaultModifiers = KeyModifierMask.None,
                    HasExplicitDefault = true,
                    SupportsModifierCombos = true,
                    GetKey = () => ComboKeyStore.GetEffectiveCustomHotkey(altPlaceId, KeyModifierMask.None, KeyCode.LeftAlt).Key,
                    GetExternalModifiers = () => ComboKeyStore.GetEffectiveCustomHotkey(altPlaceId, KeyModifierMask.None, KeyCode.LeftAlt).Modifiers,
                    SetKey = (key) =>
                    {
                        ComboKeyStore.SetSavedCustomHotkey(altPlaceId, KeyModifierMask.None, key, KeyModifierMask.None, KeyCode.LeftAlt);
                        if (key != KeyCode.None) _smartArrangeSetFixedFn?.Invoke(true);
                    },
                    SetCombo = (mods, key) =>
                    {
                        ComboKeyStore.SetSavedCustomHotkey(altPlaceId, mods, key, KeyModifierMask.None, KeyCode.LeftAlt);
                        if (key != KeyCode.None) _smartArrangeSetFixedFn?.Invoke(true);
                    }
                });
            }
        }

        private static void SupplementBetterPerformanceGroup(ModOptionGroup group)
        {
            const string entryId = "chiqingsan.BetterPerformance.hover_peek_key";
            for (int i = 0; i < group.Hotkeys.Count; i++)
            {
                var h = group.Hotkeys[i];
                if (string.Equals(h.Id, entryId, StringComparison.OrdinalIgnoreCase) ||
                    h.Id.IndexOf("peek", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.DisplayName.IndexOf("预览", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.DisplayName.IndexOf("Peek", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }
            }

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = entryId,
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = BilingualText.Get("悬停预览容器按键", "Hover Peek Container Key"),
                Tooltip = BilingualText.Get(
                    "鼠标悬停在容器/背包上时，按住此键可透视预览内部物品（默认鼠标中键；支持改键、组合键、鼠标侧键与 Esc 清空）",
                    "Hold while hovering over a container to preview its contents (default Middle Click; supports rebinding, combos, mouse side buttons & Esc to clear)"
                ),
                DefaultKey = KeyCode.Mouse2,
                DefaultModifiers = KeyModifierMask.None,
                HasExplicitDefault = true,
                SupportsModifierCombos = true,
                GetKey = () => ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, KeyCode.Mouse2).Key,
                GetExternalModifiers = () => ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, KeyCode.Mouse2).Modifiers,
                SetKey = (key) => ComboKeyStore.SetSavedCustomHotkey(entryId, KeyModifierMask.None, key, KeyModifierMask.None, KeyCode.Mouse2),
                SetCombo = (mods, key) => ComboKeyStore.SetSavedCustomHotkey(entryId, mods, key, KeyModifierMask.None, KeyCode.Mouse2)
            });
        }

        private static void SupplementPreciseGroupDragGroup(ModOptionGroup group)
        {
            const string entryId = "precise_group_drag.shift_held_key";
            for (int i = 0; i < group.Hotkeys.Count; i++)
            {
                var h = group.Hotkeys[i];
                if (string.Equals(h.Id, entryId, StringComparison.OrdinalIgnoreCase) ||
                    h.Id.IndexOf("shift", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.DisplayName.IndexOf("精准", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.DisplayName.IndexOf("紧凑", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }
            }

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = entryId,
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = BilingualText.Get("切换精准/紧凑拖拽修饰键", "Toggle Precise/Compact Drag Key"),
                Tooltip = BilingualText.Get(
                    "拖拽多件物品时按住此键，可在“保持原相对位置精准拖拽”与“紧凑聚拢拖拽”间临时切换（默认 LeftShift；支持改键、组合键、侧键与 Esc 清空）",
                    "Hold while dragging multiple items to toggle between precise relative layout and compact grouping (default LeftShift)"
                ),
                DefaultKey = KeyCode.LeftShift,
                DefaultModifiers = KeyModifierMask.None,
                HasExplicitDefault = true,
                SupportsModifierCombos = true,
                GetKey = () => ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, KeyCode.LeftShift).Key,
                GetExternalModifiers = () => ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, KeyCode.LeftShift).Modifiers,
                SetKey = (key) => ComboKeyStore.SetSavedCustomHotkey(entryId, KeyModifierMask.None, key, KeyModifierMask.None, KeyCode.LeftShift),
                SetCombo = (mods, key) => ComboKeyStore.SetSavedCustomHotkey(entryId, mods, key, KeyModifierMask.None, KeyCode.LeftShift)
            });
        }

        public static KeyCode GetItemDetailsConfigDefaultKey(ModOptionGroup? group = null)
        {
            if (group != null)
            {
                for (int i = 0; i < group.GeneralSettings.Count; i++)
                {
                    var g = group.GeneralSettings[i];
                    if (g != null && g.Id.EndsWith(".expand_key", StringComparison.OrdinalIgnoreCase) && g.GetIndex != null)
                    {
                        int idx = g.GetIndex();
                        if (idx == 1) return KeyCode.LeftShift;
                        if (idx == 2) return KeyCode.LeftAlt;
                        return KeyCode.LeftControl;
                    }
                }
            }
            return KeyCode.LeftControl;
        }

        private static void SupplementItemDetailsGroup(ModOptionGroup group)
        {
            const string entryId = "item_details.expand_key";
            for (int i = 0; i < group.Hotkeys.Count; i++)
            {
                if (string.Equals(group.Hotkeys[i].Id, entryId, StringComparison.OrdinalIgnoreCase)) return;
            }

            ModGeneralEntry? expandDropdown = null;
            for (int i = 0; i < group.GeneralSettings.Count; i++)
            {
                var g = group.GeneralSettings[i];
                if (g != null && g.Id.EndsWith(".expand_key", StringComparison.OrdinalIgnoreCase))
                {
                    expandDropdown = g;
                    break;
                }
            }

            // 如果用户更改了原下拉框，同步清除自定义覆盖使下拉框与快捷键联动
            if (expandDropdown != null && expandDropdown.SetIndex != null)
            {
                var origSetIdx = expandDropdown.SetIndex;
                expandDropdown.SetIndex = (idx) =>
                {
                    origSetIdx(idx);
                    KeyCode mapped = KeyCode.LeftControl;
                    if (idx == 1) mapped = KeyCode.LeftShift;
                    else if (idx == 2) mapped = KeyCode.LeftAlt;
                    ComboKeyStore.SetSavedCustomHotkey(entryId, KeyModifierMask.None, mapped, KeyModifierMask.None, mapped);
                };
            }

            group.Hotkeys.Add(new ModHotkeyEntry
            {
                Id = entryId,
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = BilingualText.Get("展开液体与容器详情按键", "Expand Liquid & Container Details Key"),
                Tooltip = BilingualText.Get(
                    "查看装有液体或混合物的容器时，按住此键展开详细成分列表（默认 LeftCtrl，支持任意单键、组合键、鼠标侧键与 Esc 清空）",
                    "Hold to expand detailed liquid and container composition columns (default LeftCtrl; supports any key, combo, mouse side button & Esc)"
                ),
                DefaultKey = KeyCode.LeftControl,
                DefaultModifiers = KeyModifierMask.None,
                HasExplicitDefault = true,
                SupportsModifierCombos = true,
                GetKey = () =>
                {
                    KeyCode defKey = GetItemDetailsConfigDefaultKey(group);
                    return ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, defKey).Key;
                },
                GetExternalModifiers = () =>
                {
                    KeyCode defKey = GetItemDetailsConfigDefaultKey(group);
                    return ComboKeyStore.GetEffectiveCustomHotkey(entryId, KeyModifierMask.None, defKey).Modifiers;
                },
                SetKey = (key) =>
                {
                    if (expandDropdown?.SetIndex != null)
                    {
                        if (key == KeyCode.LeftControl || key == KeyCode.RightControl) expandDropdown.SetIndex(0);
                        else if (key == KeyCode.LeftShift || key == KeyCode.RightShift) expandDropdown.SetIndex(1);
                        else if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt) expandDropdown.SetIndex(2);
                    }
                    KeyCode defKey = GetItemDetailsConfigDefaultKey(group);
                    ComboKeyStore.SetSavedCustomHotkey(entryId, KeyModifierMask.None, key, KeyModifierMask.None, defKey);
                },
                SetCombo = (mods, key) =>
                {
                    if (mods == KeyModifierMask.None && expandDropdown?.SetIndex != null)
                    {
                        if (key == KeyCode.LeftControl || key == KeyCode.RightControl) expandDropdown.SetIndex(0);
                        else if (key == KeyCode.LeftShift || key == KeyCode.RightShift) expandDropdown.SetIndex(1);
                        else if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt) expandDropdown.SetIndex(2);
                    }
                    KeyCode defKey = GetItemDetailsConfigDefaultKey(group);
                    ComboKeyStore.SetSavedCustomHotkey(entryId, mods, key, KeyModifierMask.None, defKey);
                }
            });
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
            catch (Exception ex)
            {
                ModLogger.Debug($"TryFindTrainerHost error: {ex.Message}");
                return null;
            }
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
            catch (Exception ex)
            {
                ModLogger.Debug($"TryReadTrainerSavedHotkey error: {ex.Message}");
            }
            return false;
        }

        private static bool _externalHotkeysBootSynced = false;

        internal static void ResetBootSyncForTest() => _externalHotkeysBootSynced = false;

        /// <summary>
        /// 在主菜单空闲预热或首扫时补同步“清空(None) / 鼠标侧键(Mouse3~6)”等原版 LoadHotkey 会跳过的持久键位，
        /// 同时对齐 SmartInventoryArrange, ConflictScanner, ShowItemNames 等未通过 ModConfig 初始化的已知模组运行时键位。
        /// </summary>
        public static void SyncExternalNonConfigHotkeysOnBoot()
        {
            if (_externalHotkeysBootSynced) return;
            _externalHotkeysBootSynced = true;

            SyncTrainerOnBoot();
            SyncSmartArrangeRuntimeHotkeysFromStore();
            SyncConflictScannerOnBoot();
            SyncShowItemNamesOnBoot();
        }

        private static void SyncConflictScannerOnBoot()
        {
            try
            {
                Type? keybindType = AccessTools.TypeByName("ConflictScanner.Keybind");
                if (keybindType == null) return;

                PropertyInfo? keyProp = AccessTools.Property(keybindType, "Key");
                FieldInfo? keyField = AccessTools.Field(keybindType, "_key");
                FieldInfo? loadedField = AccessTools.Field(keybindType, "_loaded");
                MethodInfo? saveMethod = AccessTools.Method(keybindType, "Save", new Type[] { typeof(KeyCode) });

                // 优先从 PrefsStorage / 原生存储解析已有键位，杜绝将旧版原生保存的配置强行重置为 F7
                KeyCode fallbackKey = KeyCode.F7;
                string rawSaved = PrefsStorage.GetString("conflict_scanner.report_key", string.Empty);
                if (string.Equals(rawSaved, "None", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackKey = KeyCode.None;
                }
                else if (!string.IsNullOrWhiteSpace(rawSaved) && Enum.TryParse<KeyCode>(rawSaved.Trim(), true, out KeyCode parsed))
                {
                    fallbackKey = parsed;
                }
                else if (keyProp != null)
                {
                    fallbackKey = (KeyCode)(keyProp.GetValue(null) ?? KeyCode.F7);
                }
                else if (keyField != null)
                {
                    fallbackKey = (KeyCode)(keyField.GetValue(null) ?? KeyCode.F7);
                }

                var effective = ComboKeyStore.GetEffectiveCustomHotkey("conflict_scanner.ReportKey", KeyModifierMask.None, fallbackKey);
                KeyCode targetKey = effective.Key;

                keyField?.SetValue(null, targetKey);
                loadedField?.SetValue(null, true);
                if (keyProp != null && keyProp.CanWrite)
                {
                    keyProp.SetValue(null, targetKey);
                }

                if (targetKey != KeyCode.None && saveMethod != null)
                {
                    saveMethod.Invoke(null, new object[] { targetKey });
                }
                else
                {
                    try
                    {
                        PrefsStorage.SetString("conflict_scanner.report_key", targetKey == KeyCode.None ? "None" : targetKey.ToString());
                        PrefsStorage.Save();
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Debug($"SyncConflictScannerOnBoot PrefsStorage error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"SyncConflictScannerOnBoot error: {ex.Message}");
            }
        }

        private static void SyncShowItemNamesOnBoot()
        {
            try
            {
                Type? settingsType = AccessTools.TypeByName("ShowItemNames.Settings");
                if (settingsType == null) return;

                PropertyInfo? hotkeyProp = AccessTools.Property(settingsType, "Hotkey");
                MethodInfo? setHotkey = AccessTools.Method(settingsType, "SetHotkey", new Type[] { typeof(KeyCode) });
                MethodInfo? commit = AccessTools.Method(settingsType, "Commit");

                // 优先从 PrefsStorage ("snd.hotkey") 或当前属性读取已有键位，杜绝误覆盖用户在独立界面配置的单键
                KeyCode fallbackKey = KeyCode.LeftAlt;
                if (PrefsStorage.HasKey("snd.hotkey"))
                {
                    int v = PrefsStorage.GetInt("snd.hotkey", (int)KeyCode.LeftAlt);
                    if (Enum.IsDefined(typeof(KeyCode), v))
                    {
                        fallbackKey = (KeyCode)v;
                    }
                }
                else if (hotkeyProp != null)
                {
                    fallbackKey = (KeyCode)(hotkeyProp.GetValue(null) ?? KeyCode.LeftAlt);
                }

                var effective = ComboKeyStore.GetEffectiveCustomHotkey("ShowItemNames.Hotkey", KeyModifierMask.None, fallbackKey);
                KeyCode targetKey = effective.Key;

                if (setHotkey != null)
                {
                    setHotkey.Invoke(null, new object[] { targetKey });
                }
                else if (hotkeyProp != null && hotkeyProp.CanWrite)
                {
                    hotkeyProp.SetValue(null, targetKey);
                }
                try
                {
                    PrefsStorage.SetInt("snd.hotkey", (int)targetKey);
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"SyncShowItemNamesOnBoot PrefsStorage error: {ex.Message}");
                }
                commit?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"SyncShowItemNamesOnBoot error: {ex.Message}");
            }
        }

        private static void SyncTrainerOnBoot()
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
            catch (Exception ex)
            {
                ModLogger.Debug($"TryAdaptProbablyStolenTrainer read mods error: {ex.Message}");
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
                catch (Exception ex)
                {
                    ModLogger.Debug($"TryAdaptProbablyStolenTrainer read enabled mods error: {ex.Message}");
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
                : BilingualText.Get("Probably Stolen 修改器", "Probably Stolen Trainer");

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
                DisplayName = BilingualText.Get("开关修改器窗口", "Toggle Trainer Overlay"),
                Tooltip = BilingualText.Get(
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
                        ModLogger.Warn($"同步修改器热键失败: {ex.Message}", ex);
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
