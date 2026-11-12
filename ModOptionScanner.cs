using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 全覆盖模组设置与快捷键扫描聚合器
    /// </summary>
    public static class ModOptionScanner
    {
        private static readonly HashSet<string> ExactHotkeyTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Key", "Hotkey", "Hotkeys", "Shortcut", "Shortcuts", "Keybind", "Keybinds", "Keybinding",
            "Bind", "ToggleKey", "OpenKey", "TriggerKey", "KeyCode", "InputKey", "QuickKey", "FastKey",
            "Btn", "ButtonKey", "Mapping", "KeyMapping", "Accelerator", "ActivationKey", "MenuKey",
            "PanelKey", "UiKey", "ActionKey", "InteractKey", "ConfirmKey", "CancelKey", "ResetKey",
            "SortKey", "HoldKey", "PressKey"
        };

        private static readonly HashSet<string> ExcludedNonHotkeyIntTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Count", "Max", "Min", "Size", "Width", "Height", "Delay", "Interval", "Cooldown",
            "Duration", "Speed", "Scale", "Alpha", "Opacity", "Port", "Index", "Page", "Slot",
            "Limit", "Amount", "Price", "Rate", "Percent", "Level", "Day", "Days", "Step",
            "Radius", "Distance", "Font"
        };

        private static readonly string[] ChineseHotkeyKeywords =
        {
            "按键", "快捷键", "热键", "键位"
        };

        private static readonly string[] ExcludedNonHotkeyKeywords =
        {
            "keyword", "keycard", "key_card", "关键词", "关键字", "门禁卡", "房卡"
        };

        private static readonly FieldInfo? ModConfigConfigsField = AccessTools.Field(typeof(ModConfig), "configs");
        private static readonly MethodInfo? ApplyBoxedMethod = AccessTools.Method(typeof(ConfigEntry), "ApplyBoxed", new Type[] { typeof(object) });
        private static readonly Func<ConfigEntry, object, bool>? ApplyBoxedDelegate =
            ApplyBoxedMethod != null ? CreateOpenDelegate<Func<ConfigEntry, object, bool>>(ApplyBoxedMethod) : null;

        private static T? CreateOpenDelegate<T>(MethodInfo? method) where T : Delegate
        {
            if (method == null) return null;
            try { return (T)Delegate.CreateDelegate(typeof(T), method); }
            catch { return null; }
        }
        private static readonly Assembly VanillaAssembly = typeof(GameOptionRegistry).Assembly;
        private static readonly Assembly SelfAssembly = typeof(ModOptionScanner).Assembly;
        private static readonly Dictionary<Assembly, bool> SystemOrEngineAsmCache = new Dictionary<Assembly, bool>();

        // 会话级首次启动快照，确保所有无显式默认值的模组条目都能拥有稳定的“恢复”目标
        private static readonly Dictionary<string, KeyCode> InitialKeySnapshots = new Dictionary<string, KeyCode>(StringComparer.Ordinal);
        private static readonly Dictionary<string, KeyModifierMask> InitialModifierSnapshots = new Dictionary<string, KeyModifierMask>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> InitialBoolSnapshots = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Dictionary<string, float> InitialFloatSnapshots = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> InitialIndexSnapshots = new Dictionary<string, int>(StringComparer.Ordinal);

        private static bool IsSelfModId(string? modId)
        {
            return string.Equals(modId, "probably_unified_hotkeys", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(modId, "allow_unbind_key", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSystemOrEngineAssembly(Assembly asm)
        {
            if (SystemOrEngineAsmCache.TryGetValue(asm, out bool cached))
            {
                return cached;
            }

            string name = asm.GetName().Name ?? string.Empty;
            bool result = name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                          name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                          name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
                          name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
                          name.StartsWith("Mono.", StringComparison.OrdinalIgnoreCase);
            SystemOrEngineAsmCache[asm] = result;
            return result;
        }

        private static Assembly? CheckDelegateExternalAssembly(Delegate? d)
        {
            if (d == null) return null;
            var asm = d.Method?.DeclaringType?.Assembly;
            if (asm != null && asm != VanillaAssembly && !IsSystemOrEngineAssembly(asm))
            {
                return asm;
            }
            if (d.Target != null)
            {
                var targetAsm = d.Target.GetType().Assembly;
                if (targetAsm != null && targetAsm != VanillaAssembly && !IsSystemOrEngineAssembly(targetAsm))
                {
                    return targetAsm;
                }
            }
            return null;
        }

        /// <summary>
        /// 检测 GameOptionRegistry 条目委托是否归属于第三方外部程序集（防止无 source 注册的模组逃逸到原版设置页）
        /// </summary>
        public static Assembly? GetEntryExternalAssembly(GameOptionRegistry.Entry? entry)
        {
            if (entry == null) return null;

            return CheckDelegateExternalAssembly(entry.getKey)
                ?? CheckDelegateExternalAssembly(entry.setKey)
                ?? CheckDelegateExternalAssembly(entry.getBool)
                ?? CheckDelegateExternalAssembly(entry.setBool)
                ?? CheckDelegateExternalAssembly(entry.getFloat)
                ?? CheckDelegateExternalAssembly(entry.setFloat)
                ?? CheckDelegateExternalAssembly(entry.getIndex)
                ?? CheckDelegateExternalAssembly(entry.setIndex)
                ?? CheckDelegateExternalAssembly(entry.label)
                ?? CheckDelegateExternalAssembly(entry.tooltip)
                ?? CheckDelegateExternalAssembly(entry.options)
                ?? CheckDelegateExternalAssembly(entry.valueText)
                ?? CheckDelegateExternalAssembly(entry.interactable)
                ?? CheckDelegateExternalAssembly(entry.overrideOptions);
        }

        /// <summary>
        /// 判断一个 GameOptionRegistry 条目是否为纯粹的官方原版设置项
        /// </summary>
        public static bool IsPureVanillaEntry(GameOptionRegistry.Section? section, GameOptionRegistry.Entry? entry)
        {
            if (section == null || entry == null) return false;
            if (!GameOptionRegistry.IsVanillaSection(section.id)) return false;

            if (!string.IsNullOrEmpty(section.source) &&
                !string.Equals(section.source, "vanilla", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(entry.source) &&
                !string.Equals(entry.source, "vanilla", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (GetEntryExternalAssembly(entry) != null)
            {
                return false;
            }

            return true;
        }

        public static void SetEntryBoxed(ConfigEntry entry, object value)
        {
            try
            {
                if (ApplyBoxedDelegate != null) ApplyBoxedDelegate(entry, value);
                else ApplyBoxedMethod?.Invoke(entry, new object[] { value });
                entry.Config?.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error setting config entry {entry.Key}: {ex.Message}");
            }
        }

        public static void ResetEntryToDefault(ConfigEntry entry)
        {
            try
            {
                if (entry.BoxedDefault != null)
                {
                    if (ApplyBoxedDelegate != null) ApplyBoxedDelegate(entry, entry.BoxedDefault);
                    else ApplyBoxedMethod?.Invoke(entry, new object[] { entry.BoxedDefault });
                    entry.Config?.Save();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error resetting config entry {entry.Key}: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前内存中已注册的 ModConfig 数量（供轻量环境指纹感知“野 ID”或懒加载配置变动）
        /// </summary>
        internal static int GetRegisteredModConfigCount()
        {
            var dict = TryGetAllModConfigs();
            return dict != null ? dict.Count : 0;
        }

        private static Dictionary<string, ModConfig>? TryGetAllModConfigs()
        {
            try
            {
                return ModConfigConfigsField?.GetValue(null) as Dictionary<string, ModConfig>;
            }
            catch
            {
                return null;
            }
        }

        internal static string GetCanonicalConfigId(string fallbackModId, ConfigEntry entry)
        {
            string? cfgModId = entry?.Config?.ModId;
            string modPrefix = !string.IsNullOrEmpty(cfgModId) ? cfgModId! : fallbackModId;
            return $"{modPrefix}.{entry?.Key}";
        }

        /// <summary>
        /// 扫描全量已启用模组，构建设置树
        /// </summary>
        public static List<ModOptionGroup> ScanAll(out List<ModHotkeyEntry> allHotkeysForConflict)
        {
            return ScanAll(out allHotkeysForConflict, out _);
        }

        /// <summary>
        /// 扫描全量已启用模组，构建设置树并输出详尽的扫描诊断报告（0 安全警告）
        /// </summary>
        public static List<ModOptionGroup> ScanAll(out List<ModHotkeyEntry> allHotkeysForConflict, out ModScanReport scanReport)
        {
            ComboKeyStore.EnsureLoaded();
            using var _bypass = ComboKeyStore.EnterBypass();
            var groups = new List<ModOptionGroup>();
            scanReport = new ModScanReport();

            var managedStringIds = new List<string>(32);
            var managedIntIds = new List<string>(16);
            var matchedConfigsByGroupId = new Dictionary<string, List<ModConfig>>(StringComparer.OrdinalIgnoreCase);
            var candidatesByGroupId = new Dictionary<string, List<DiagnosticCandidateEntry>>(StringComparer.OrdinalIgnoreCase);
            var registryMatchedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var adapterMatchedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var consumedConfigIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unmatchedWildConfigs = new List<ModConfig>();

            // 1. 优先扫描已加载模组的标准 ModConfig（含完整 BoxedDefault 默认值与取值范围）
            List<LoadedMod>? loadedMods = null;
            try
            {
                loadedMods = ModLoader.Mods;
            }
            catch
            {
                loadedMods = null;
            }

            var enabledLoadedMods = new List<LoadedMod>();
            var enabledModById = new Dictionary<string, LoadedMod>(StringComparer.OrdinalIgnoreCase);

            if (loadedMods != null)
            {
                foreach (var mod in loadedMods)
                {
                    if (mod == null || mod.Manifest == null) continue;
                    string modId = mod.Manifest.ID;
                    if (IsSelfModId(modId)) continue;

                    bool isEnabled = IsLoadedModActive(mod, modId);
                    if (isEnabled)
                    {
                        enabledLoadedMods.Add(mod);
                        if (!string.IsNullOrEmpty(modId) && !enabledModById.ContainsKey(modId))
                        {
                            enabledModById[modId] = mod;
                        }
                    }

                    ModConfig? config = ModConfig.Find(modId);
                    if (config == null || !config.HasEntries) continue;

                    consumedConfigIds.Add(modId);
                    string displayName = string.IsNullOrEmpty(mod.Manifest.Name) ? modId : mod.Manifest.Name;
                    var group = GetOrCreateGroup(groups, modId, displayName);
                    ScanModConfigIntoGroup(
                        group,
                        config,
                        matchedConfigsByGroupId,
                        candidatesByGroupId,
                        managedStringIds,
                        managedIntIds
                    );
                }
            }

            // 1B. 打捞所有未按 Manifest.ID 命名注册的“野 ID” ModConfig（如 AI 随手写成 ModConfig.For("my_mod_cfg")）
            var allConfigs = TryGetAllModConfigs();
            if (allConfigs != null)
            {
                foreach (var kvp in allConfigs)
                {
                    string cfgId = kvp.Key;
                    ModConfig config = kvp.Value;
                    if (string.IsNullOrEmpty(cfgId) || config == null || !config.HasEntries) continue;
                    if (IsSelfModId(cfgId) || consumedConfigIds.Contains(cfgId)) continue;

                    consumedConfigIds.Add(cfgId);

                    LoadedMod? ownerMod = TryMatchWildConfigToLoadedMod(cfgId, enabledLoadedMods, matchedConfigsByGroupId);
                    if (ownerMod != null && ownerMod.Manifest != null)
                    {
                        string ownerId = ownerMod.Manifest.ID;
                        string displayName = string.IsNullOrEmpty(ownerMod.Manifest.Name) ? ownerId : ownerMod.Manifest.Name;
                        var group = GetOrCreateGroup(groups, ownerId, displayName);
                        if (!string.Equals(group.ModId, cfgId, StringComparison.OrdinalIgnoreCase) &&
                            !group.ExtraNotifyModIds.Any(id => string.Equals(id, cfgId, StringComparison.OrdinalIgnoreCase)))
                        {
                            group.ExtraNotifyModIds.Add(cfgId);
                        }
                        ScanModConfigIntoGroup(
                            group,
                            config,
                            matchedConfigsByGroupId,
                            candidatesByGroupId,
                            managedStringIds,
                            managedIntIds
                        );
                    }
                    else
                    {
                        var group = GetOrCreateGroup(groups, cfgId, cfgId);
                        unmatchedWildConfigs.Add(config);
                        ScanModConfigIntoGroup(
                            group,
                            config,
                            matchedConfigsByGroupId,
                            candidatesByGroupId,
                            managedStringIds,
                            managedIntIds
                        );
                    }
                }
            }

            // 2. 全面扫描 GameOptionRegistry 中的所有第三方模组配置与快捷键（含未指定 source 的外部程序集条目）
            ScanGameOptionRegistryModEntries(groups, registryMatchedModIds);

            // 3. 记录已被覆盖的模组（针对未走 GameOptionRegistry / ModConfig 的旧版独立适配器做兜底补充）
            var handledAdapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                if (g.Hotkeys.Count > 0 || string.Equals(g.ModId, "smart_inventory_arrange", StringComparison.OrdinalIgnoreCase))
                {
                    handledAdapterIds.Add(g.ModId);
                }
            }

            var extraAdapterGroups = new List<ModOptionGroup>();
            KnownModAdapters.TryAdaptKnownMods(extraAdapterGroups, handledAdapterIds);

            foreach (var extra in extraAdapterGroups)
            {
                adapterMatchedModIds.Add(extra.ModId);
                var existing = groups.FirstOrDefault(g => string.Equals(g.ModId, extra.ModId, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    groups.Add(extra);
                }
                else
                {
                    foreach (var hk in extra.Hotkeys)
                    {
                        if (!ContainsHotkey(existing, hk.Id, hk.DisplayName))
                        {
                            existing.Hotkeys.Add(hk);
                        }
                    }
                    foreach (var gen in extra.GeneralSettings)
                    {
                        if (!ContainsGeneral(existing, gen.Id, gen.DisplayName))
                        {
                            existing.GeneralSettings.Add(gen);
                        }
                    }
                }
            }

            // 注册运行时需要拦截组合键防误触的 string / int ConfigEntry 标识
            ComboKeyStore.RegisterManagedConfigKeyIds(managedStringIds, managedIntIds);

            // 移出无有效配置项的空组
            groups.RemoveAll(g => g.TotalItemCount == 0);

            // 4. 为所有卡片记录启动快照并绑定全覆盖“恢复”操作（确保每张模组卡片都有恢复按钮）
            foreach (var group in groups)
            {
                foreach (var hk in group.Hotkeys)
                {
                    if (!InitialKeySnapshots.TryGetValue(hk.Id, out KeyCode initKey))
                    {
                        initKey = hk.GetKey != null ? hk.GetKey() : KeyCode.None;
                        InitialKeySnapshots[hk.Id] = initKey;
                    }
                    if (!InitialModifierSnapshots.TryGetValue(hk.Id, out KeyModifierMask initMods))
                    {
                        initMods = hk.GetExternalModifiers != null
                            ? hk.GetExternalModifiers()
                            : ComboKeyStore.GetSavedModifiers(hk.Id, initKey);
                        InitialModifierSnapshots[hk.Id] = initMods;
                    }
                    hk.SnapshotKey = initKey;
                    hk.SnapshotModifiers = initMods;
                }

                foreach (var gen in group.GeneralSettings)
                {
                    if (gen.Kind == OptionKind.Toggle)
                    {
                        if (!InitialBoolSnapshots.TryGetValue(gen.Id, out bool initBool))
                        {
                            initBool = gen.GetBool != null && gen.GetBool();
                            InitialBoolSnapshots[gen.Id] = initBool;
                        }
                        gen.SnapshotBool = initBool;
                    }
                    else if (gen.Kind == OptionKind.Slider)
                    {
                        if (!InitialFloatSnapshots.TryGetValue(gen.Id, out float initFloat))
                        {
                            initFloat = gen.GetFloat != null ? gen.GetFloat() : gen.Min;
                            InitialFloatSnapshots[gen.Id] = initFloat;
                        }
                        gen.SnapshotFloat = initFloat;
                    }
                    else if (gen.Kind == OptionKind.Dropdown)
                    {
                        if (!InitialIndexSnapshots.TryGetValue(gen.Id, out int initIndex))
                        {
                            initIndex = gen.GetIndex != null ? gen.GetIndex() : 0;
                            InitialIndexSnapshots[gen.Id] = initIndex;
                        }
                        gen.SnapshotIndex = initIndex;
                    }
                }

                List<ModConfig>? groupConfigs = null;
                if (matchedConfigsByGroupId.TryGetValue(group.ModId, out var foundCfgs) && foundCfgs.Count > 0)
                {
                    groupConfigs = foundCfgs;
                }
                else
                {
                    ModConfig? fallbackCfg = ModConfig.Find(group.ModId);
                    if (fallbackCfg != null && fallbackCfg.HasEntries)
                    {
                        groupConfigs = new List<ModConfig>(1) { fallbackCfg };
                    }
                }

                var capturedGroup = group;
                capturedGroup.ResetToDefaultsAction = () =>
                {
                    if (groupConfigs != null)
                    {
                        foreach (var cfg in groupConfigs)
                        {
                            if (cfg != null && cfg.HasEntries)
                            {
                                cfg.ResetToDefaults();
                                cfg.Save();
                            }
                        }
                    }

                    foreach (var hk in capturedGroup.Hotkeys)
                    {
                        hk.HasPendingChange = false;
                        hk.PendingModifiers = hk.ResetTargetModifiers;
                        hk.PendingKey = hk.ResetTargetKey;
                        if (hk.SetCombo != null)
                        {
                            hk.SetCombo(hk.ResetTargetModifiers, hk.ResetTargetKey);
                            ComboKeyStore.SetSavedCombo(hk.Id, KeyModifierMask.None, KeyCode.None);
                        }
                        else
                        {
                            hk.SetKey?.Invoke(hk.ResetTargetKey);
                            ComboKeyStore.SetSavedCombo(hk.Id, hk.ResetTargetModifiers, hk.ResetTargetKey);
                        }
                    }
                    ComboKeyStore.Save();

                    bool hasAnyConfig = groupConfigs != null && groupConfigs.Count > 0;
                    foreach (var gen in capturedGroup.GeneralSettings)
                    {
                        gen.PendingBool = null;
                        gen.PendingFloat = null;
                        gen.PendingIndex = null;
                        if (!hasAnyConfig || gen.ResetToDefault == null)
                        {
                            gen.ResetToSnapshot();
                        }
                    }
                };
            }

            // 5. 按拼音首字母排序
            groups.Sort((a, b) =>
            {
                int cmp = string.Compare(a.PinyinSortKey, b.PinyinSortKey, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            // 6. 汇总全量快捷键用于全局冲突检测与组合键运行时路由表（原版 + 所有模组）
            allHotkeysForConflict = new List<ModHotkeyEntry>();
            allHotkeysForConflict.AddRange(GetVanillaKeybinds());
            foreach (var g in groups)
            {
                allHotkeysForConflict.AddRange(g.Hotkeys);
            }
            ComboKeyStore.RebuildRuntimeLookup(allHotkeysForConflict);

            // 7. 构建零安全警告的全量模组扫描与疑难项诊断报告
            scanReport = BuildModScanReport(
                groups,
                enabledLoadedMods,
                matchedConfigsByGroupId,
                candidatesByGroupId,
                registryMatchedModIds,
                adapterMatchedModIds,
                unmatchedWildConfigs
            );

            return groups;
        }

        private static bool IsLoadedModActive(LoadedMod mod, string modId)
        {
            if (mod.IsEnabled) return true;
            try
            {
                return !string.IsNullOrEmpty(modId) && ModLoader.IsModEnabled(modId);
            }
            catch
            {
                return mod.IsEnabled;
            }
        }

        private static void ScanModConfigIntoGroup(
            ModOptionGroup group,
            ModConfig config,
            Dictionary<string, List<ModConfig>> matchedConfigsByGroupId,
            Dictionary<string, List<DiagnosticCandidateEntry>> candidatesByGroupId,
            List<string> managedStringIds,
            List<string> managedIntIds)
        {
            if (!matchedConfigsByGroupId.TryGetValue(group.ModId, out var cfgList))
            {
                cfgList = new List<ModConfig>(1);
                matchedConfigsByGroupId[group.ModId] = cfgList;
            }
            if (!cfgList.Contains(config))
            {
                cfgList.Add(config);
            }

            if (!candidatesByGroupId.TryGetValue(group.ModId, out var candList))
            {
                candList = new List<DiagnosticCandidateEntry>();
                candidatesByGroupId[group.ModId] = candList;
            }

            foreach (var entry in config.Entries)
            {
                if (entry == null) continue;
                string entryKey = entry.Key;
                string label = entry.Label?.Invoke() ?? entryKey;
                string tooltip = entry.Tooltip?.Invoke() ?? string.Empty;
                string canonicalId = GetCanonicalConfigId(group.ModId, entry);
                bool isForced = ComboKeyStore.IsForcedHotkey(canonicalId);

                bool isHotkey = false;
                bool isGeneral = false;
                bool dropped = false;

                if (IsHotkeyEntry(entry, canonicalId))
                {
                    var hotkey = CreateHotkeyFromConfigEntry(group, entry, canonicalId, label, tooltip);
                    if (hotkey != null)
                    {
                        if (!ContainsHotkey(group, hotkey.Id, hotkey.DisplayName))
                        {
                            group.Hotkeys.Add(hotkey);
                        }
                        isHotkey = true;
                        if (entry.ValueType == typeof(string))
                        {
                            managedStringIds.Add(canonicalId);
                        }
                        else if (entry.ValueType == typeof(int))
                        {
                            managedIntIds.Add(canonicalId);
                        }
                    }
                }
                else
                {
                    var general = CreateGeneralFromConfigEntry(group, entry, label, tooltip);
                    if (general != null)
                    {
                        if (!ContainsGeneral(group, general.Id, general.DisplayName))
                        {
                            group.GeneralSettings.Add(general);
                        }
                        isGeneral = true;
                    }
                    else
                    {
                        dropped = true;
                    }
                }

                // 收集潜在可转为按键的候选/被丢弃/已强转条目（用于诊断面板人工纠偏）
                bool isPromotableType = entry.ValueType == typeof(string) ||
                                        (entry.ValueType == typeof(int) && !entry.HasRange && (entry.Choices == null || entry.Choices.Length == 0));

                if (isForced || dropped || (isPromotableType && isGeneral))
                {
                    bool suspicious = isForced || dropped;
                    if (!suspicious && entry.ValueType == typeof(int))
                    {
                        suspicious = (IsLikelyKeyCodeInt(entry.BoxedValue) || IsLikelyKeyCodeInt(entry.BoxedDefault)) &&
                                     !HasNonHotkeyIntToken(entryKey, label);
                    }

                    candList.Add(new DiagnosticCandidateEntry
                    {
                        CanonicalId = canonicalId,
                        ConfigModId = config.ModId ?? group.ModId,
                        OwnerModId = group.ModId,
                        ModDisplayName = group.DisplayName,
                        EntryKey = entryKey,
                        Label = label,
                        Tooltip = tooltip,
                        ValueTypeName = entry.ValueType?.Name ?? "Unknown",
                        CurrentValuePreview = FormatValuePreview(entry.BoxedValue),
                        DefaultValuePreview = FormatValuePreview(entry.BoxedDefault),
                        IsForcedHotkey = isForced,
                        IsCurrentlyHotkey = isHotkey,
                        IsCurrentlyGeneral = isGeneral,
                        WasDroppedFromMainUI = dropped,
                        CanPromoteToHotkey = isPromotableType || entry.ValueType == typeof(KeyCode),
                        IsSuspiciousCandidate = suspicious
                    });
                }
            }
        }

        private static LoadedMod? TryMatchWildConfigToLoadedMod(
            string cfgId,
            List<LoadedMod> enabledMods,
            Dictionary<string, List<ModConfig>> matchedConfigsByGroupId)
        {
            string normCfg = NormalizeModToken(StripCommonConfigAffixes(cfgId));
            string normCfgRaw = NormalizeModToken(cfgId);
            if (string.IsNullOrEmpty(normCfg) && string.IsNullOrEmpty(normCfgRaw)) return null;

            LoadedMod? matched = null;
            int matchCount = 0;

            foreach (var mod in enabledMods)
            {
                if (mod?.Manifest == null) continue;
                string id = mod.Manifest.ID ?? string.Empty;
                if (matchedConfigsByGroupId.ContainsKey(id)) continue; // 已有正式 ModConfig 的模组不抢占野 ID

                string name = mod.Manifest.Name ?? string.Empty;
                string idTail = StripAuthorPrefix(id);

                bool hit = string.Equals(NormalizeModToken(id), normCfgRaw, StringComparison.Ordinal) ||
                           string.Equals(NormalizeModToken(idTail), normCfgRaw, StringComparison.Ordinal) ||
                           (!string.IsNullOrEmpty(normCfg) &&
                            (string.Equals(NormalizeModToken(id), normCfg, StringComparison.Ordinal) ||
                             string.Equals(NormalizeModToken(idTail), normCfg, StringComparison.Ordinal) ||
                             string.Equals(NormalizeModToken(name), normCfg, StringComparison.Ordinal)));

                if (hit)
                {
                    matched = mod;
                    matchCount++;
                }
            }

            return matchCount == 1 ? matched : null;
        }

        private static string StripAuthorPrefix(string id)
        {
            int dot = id.IndexOf('.');
            return (dot >= 0 && dot + 1 < id.Length) ? id.Substring(dot + 1) : id;
        }

        private static string StripCommonConfigAffixes(string raw)
        {
            string s = StripAuthorPrefix(raw.Trim());
            string[] suffixes = { "_config", "-config", "config", "_cfg", "-cfg", "cfg", "_settings", "-settings", "settings", "_setting", "setting", "_options", "options" };
            foreach (string suf in suffixes)
            {
                if (s.Length > suf.Length && s.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Substring(0, s.Length - suf.Length);
                    break;
                }
            }
            return s;
        }

        private static string NormalizeModToken(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw!.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    sb.Append((char)(c - 'A' + 'a'));
                }
            }
            return sb.ToString();
        }

        private static bool IsLikelyKeyCodeInt(object? boxed)
        {
            if (boxed is not int intVal) return false;
            // 排除常见小整数配置 0..8 避免把天数/数量滑块误判，重点关注字母/数字/功能键/鼠标侧键码位
            if (intVal == (int)KeyCode.Tab || intVal == (int)KeyCode.Return || intVal == (int)KeyCode.Space || intVal == (int)KeyCode.BackQuote)
            {
                return true;
            }
            if ((intVal >= (int)KeyCode.Alpha0 && intVal <= (int)KeyCode.Alpha9) ||
                (intVal >= (int)KeyCode.A && intVal <= (int)KeyCode.Z) ||
                (intVal >= (int)KeyCode.Keypad0 && intVal <= (int)KeyCode.KeypadEquals) ||
                (intVal >= (int)KeyCode.UpArrow && intVal <= (int)KeyCode.PageDown) ||
                (intVal >= (int)KeyCode.F1 && intVal <= (int)KeyCode.F15) ||
                (intVal >= (int)KeyCode.RightShift && intVal <= (int)KeyCode.LeftAlt) ||
                (intVal >= (int)KeyCode.Mouse0 && intVal <= (int)KeyCode.Mouse6))
            {
                return true;
            }
            return false;
        }

        private static string FormatValuePreview(object? val)
        {
            if (val == null) return "null";
            string s = val.ToString() ?? string.Empty;
            if (s.Length == 0) return "<空>";
            return s.Length > 28 ? s.Substring(0, 28) + "..." : s;
        }

        private static ModScanReport BuildModScanReport(
            List<ModOptionGroup> groups,
            List<LoadedMod> enabledLoadedMods,
            Dictionary<string, List<ModConfig>> matchedConfigsByGroupId,
            Dictionary<string, List<DiagnosticCandidateEntry>> candidatesByGroupId,
            HashSet<string> registryMatchedModIds,
            HashSet<string> adapterMatchedModIds,
            List<ModConfig> unmatchedWildConfigs)
        {
            var report = new ModScanReport();
            var groupById = new Dictionary<string, ModOptionGroup>(groups.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                if (!string.IsNullOrEmpty(g.ModId) && !groupById.ContainsKey(g.ModId))
                {
                    groupById[g.ModId] = g;
                }
                report.TotalHotkeysCount += g.Hotkeys.Count;
                report.TotalGeneralsCount += g.GeneralSettings.Count;
            }

            foreach (var mod in enabledLoadedMods)
            {
                if (mod?.Manifest == null) continue;
                string modId = mod.Manifest.ID ?? string.Empty;
                string disp = string.IsNullOrEmpty(mod.Manifest.Name) ? modId : mod.Manifest.Name;
                bool hasAsm = mod.Assembly != null || (mod.Manifest.Assemblies != null && mod.Manifest.Assemblies.Count > 0);

                groupById.TryGetValue(modId, out var grp);
                int hkCount = grp?.Hotkeys.Count ?? 0;
                int genCount = grp?.GeneralSettings.Count ?? 0;
                int managedTotal = hkCount + genCount;

                var item = new ModDiagnosticItem
                {
                    ModId = modId,
                    DisplayName = disp,
                    Author = mod.Manifest.Author ?? string.Empty,
                    Version = mod.Manifest.ModVersion ?? string.Empty,
                    Description = mod.Manifest.Description ?? string.Empty,
                    WorkshopId = mod.WorkshopId,
                    HasAssembly = hasAsm,
                    ManagedHotkeyCount = hkCount,
                    ManagedGeneralCount = genCount,
                    UsedGameOptionRegistry = registryMatchedModIds.Contains(modId),
                    UsedKnownAdapter = adapterMatchedModIds.Contains(modId)
                };

                if (matchedConfigsByGroupId.TryGetValue(modId, out var cfgs))
                {
                    foreach (var c in cfgs)
                    {
                        if (c != null && !string.IsNullOrEmpty(c.ModId))
                        {
                            item.MatchedConfigIds.Add(c.ModId);
                        }
                    }
                }

                bool hasActionableCandidate = false;
                if (candidatesByGroupId.TryGetValue(modId, out var cands))
                {
                    foreach (var c in cands)
                    {
                        item.CandidateEntries.Add(c);
                        report.AllCandidates.Add(c);
                        if (c.IsSuspiciousCandidate || c.IsForcedHotkey)
                        {
                            hasActionableCandidate = true;
                        }
                    }
                }

                if (!hasAsm && managedTotal == 0 && item.CandidateEntries.Count == 0)
                {
                    item.Status = ModDiagnosticStatus.AssetOnlyMod;
                    report.AssetOnlyModCount++;
                }
                else if (hasActionableCandidate)
                {
                    item.Status = ModDiagnosticStatus.HasCandidates;
                    report.HasCandidatesModCount++;
                    if (managedTotal > 0) report.ManagedModCount++;
                }
                else if (managedTotal == 0)
                {
                    item.Status = ModDiagnosticStatus.UnmanagedCodeMod;
                    report.UnmanagedCodeModCount++;
                }
                else
                {
                    item.Status = ModDiagnosticStatus.Managed;
                    report.ManagedModCount++;
                }

                report.EnabledModCount++;
                report.Mods.Add(item);
            }

            // 额外附上无法反向绑定回 Manifest 的独立野 ModConfig
            foreach (var wildCfg in unmatchedWildConfigs)
            {
                if (wildCfg == null || string.IsNullOrEmpty(wildCfg.ModId)) continue;
                report.OrphanConfigCount++;
                groupById.TryGetValue(wildCfg.ModId, out var grp);

                var item = new ModDiagnosticItem
                {
                    ModId = wildCfg.ModId,
                    DisplayName = wildCfg.ModId,
                    HasAssembly = true,
                    IsWildOrphanConfig = true,
                    ManagedHotkeyCount = grp?.Hotkeys.Count ?? 0,
                    ManagedGeneralCount = grp?.GeneralSettings.Count ?? 0
                };
                item.MatchedConfigIds.Add(wildCfg.ModId);

                bool hasActionable = false;
                if (candidatesByGroupId.TryGetValue(wildCfg.ModId, out var cands))
                {
                    foreach (var c in cands)
                    {
                        item.CandidateEntries.Add(c);
                        report.AllCandidates.Add(c);
                        if (c.IsSuspiciousCandidate || c.IsForcedHotkey)
                        {
                            hasActionable = true;
                        }
                    }
                }

                item.Status = hasActionable ? ModDiagnosticStatus.HasCandidates : ModDiagnosticStatus.Managed;
                if (hasActionable) report.HasCandidatesModCount++;
                report.Mods.Add(item);
            }

            report.AttentionModCount = report.HasCandidatesModCount + report.UnmanagedCodeModCount;

            report.Mods.Sort((a, b) =>
            {
                int cmp = ((int)GetSortPriority(a.Status)).CompareTo((int)GetSortPriority(b.Status));
                if (cmp != 0) return cmp;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            return report;

            static int GetSortPriority(ModDiagnosticStatus s)
            {
                switch (s)
                {
                    case ModDiagnosticStatus.HasCandidates: return 0;
                    case ModDiagnosticStatus.UnmanagedCodeMod: return 1;
                    case ModDiagnosticStatus.Managed: return 2;
                    default: return 3;
                }
            }
        }

        private static ModOptionGroup GetOrCreateGroup(List<ModOptionGroup> groups, string modId, string displayName)
        {
            var existing = groups.FirstOrDefault(g => string.Equals(g.ModId, modId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!string.IsNullOrEmpty(displayName) && existing.DisplayName == existing.ModId && displayName != modId)
                {
                    existing.DisplayName = displayName;
                    existing.PinyinSortKey = PinyinEngine.ToInitials(displayName);
                }
                return existing;
            }

            var created = new ModOptionGroup
            {
                ModId = modId,
                DisplayName = string.IsNullOrEmpty(displayName) ? modId : displayName,
                PinyinSortKey = PinyinEngine.ToInitials(string.IsNullOrEmpty(displayName) ? modId : displayName)
            };
            groups.Add(created);
            return created;
        }

        private static bool ContainsHotkey(ModOptionGroup group, string entryId, string displayName)
        {
            foreach (var h in group.Hotkeys)
            {
                if (string.Equals(h.Id, entryId, StringComparison.OrdinalIgnoreCase) ||
                    h.Id.EndsWith("." + entryId, StringComparison.OrdinalIgnoreCase) ||
                    entryId.EndsWith("." + h.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(displayName) &&
                    string.Equals(h.DisplayName.Trim(), displayName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsGeneral(ModOptionGroup group, string entryId, string displayName)
        {
            foreach (var g in group.GeneralSettings)
            {
                if (string.Equals(g.Id, entryId, StringComparison.OrdinalIgnoreCase) ||
                    g.Id.EndsWith("." + entryId, StringComparison.OrdinalIgnoreCase) ||
                    entryId.EndsWith("." + g.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(displayName) &&
                    string.Equals(g.DisplayName.Trim(), displayName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 提取纯粹的原版按键绑定（严格剔除任何混入原版分区的第三方模组快捷键）
        /// </summary>
        public static List<ModHotkeyEntry> GetVanillaKeybinds()
        {
            var list = new List<ModHotkeyEntry>();
            try
            {
                var section = GameOptionRegistry.FindSection(GameOptionRegistry.SECTION_KEYBIND);
                if (section != null && section.entries != null)
                {
                    foreach (var entry in section.entries)
                    {
                        if (entry == null || entry.kind != GameOptionRegistry.Kind.KeyBind) continue;
                        if (!IsPureVanillaEntry(section, entry)) continue;

                        string label = entry.label?.Invoke() ?? entry.id;
                        string tooltip = entry.tooltip?.Invoke() ?? string.Empty;

                        list.Add(new ModHotkeyEntry
                        {
                            Id = "vanilla." + entry.id,
                            RawOptionId = entry.id,
                            ModId = "vanilla",
                            ModDisplayName = "原版官方",
                            DisplayName = label,
                            Tooltip = tooltip,
                            GetKey = entry.getKey ?? (() => KeyCode.None),
                            SetKey = entry.setKey
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error getting vanilla keybinds: {ex.Message}");
            }
            return list;
        }

        private static bool ContainsNonHotkeyExclusion(string key, string label)
        {
            for (int i = 0; i < ExcludedNonHotkeyKeywords.Length; i++)
            {
                string ex = ExcludedNonHotkeyKeywords[i];
                if (key.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasHotkeyIdentifier(string key, string label)
        {
            // 中文标签关键词检查
            for (int i = 0; i < ChineseHotkeyKeywords.Length; i++)
            {
                string kw = ChineseHotkeyKeywords[i];
                if (key.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return ContainsAnyExactToken(key, ExactHotkeyTokens) || ContainsAnyExactToken(label, ExactHotkeyTokens);
        }

        private static bool HasNonHotkeyIntToken(string key, string label)
        {
            return ContainsAnyExactToken(key, ExcludedNonHotkeyIntTokens) || ContainsAnyExactToken(label, ExcludedNonHotkeyIntTokens);
        }

        private static bool ContainsAnyExactToken(string? text, HashSet<string> tokenSet)
        {
            if (string.IsNullOrEmpty(text)) return false;

            int start = -1;
            for (int i = 0; i <= text!.Length; i++)
            {
                bool isEnd = i == text.Length;
                char c = isEnd ? '\0' : text[i];
                bool isWordChar = !isEnd && ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));

                if (start >= 0)
                {
                    bool camelBoundary = !isEnd && char.IsUpper(c) && i > start && char.IsLower(text[i - 1]);
                    if (!isWordChar || camelBoundary)
                    {
                        string token = text.Substring(start, i - start);
                        if (tokenSet.Contains(token))
                        {
                            return true;
                        }
                        start = isWordChar ? i : -1;
                    }
                }
                else if (isWordChar)
                {
                    start = i;
                }
            }

            return false;
        }

        internal static bool TryParseComboString(object? boxed, out KeyModifierMask mods, out KeyCode key)
        {
            mods = KeyModifierMask.None;
            key = KeyCode.None;
            if (boxed == null) return false;

            if (boxed is KeyCode kc)
            {
                key = kc;
                return true;
            }
            if (boxed is int intVal)
            {
                if (Enum.IsDefined(typeof(KeyCode), intVal))
                {
                    key = (KeyCode)intVal;
                    return true;
                }
                return false;
            }
            if (boxed is string raw)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0 ||
                    string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase) ||
                    trimmed == "0")
                {
                    key = KeyCode.None;
                    return true;
                }

                if (trimmed.IndexOf('+') < 0)
                {
                    if (char.IsDigit(trimmed[0])) return false;
                    if (Enum.TryParse<KeyCode>(trimmed, true, out var singleKey) &&
                        Enum.IsDefined(typeof(KeyCode), singleKey))
                    {
                        key = singleKey;
                        return true;
                    }
                    return false;
                }

                string[] parts = trimmed.Split('+');
                bool sawKey = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    string tok = parts[i].Trim();
                    if (tok.Length == 0) return false;

                    if (string.Equals(tok, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tok, "Control", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tok, "LCtrl", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tok, "RCtrl", StringComparison.OrdinalIgnoreCase))
                    {
                        mods |= KeyModifierMask.Ctrl;
                    }
                    else if (string.Equals(tok, "Shift", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(tok, "LShift", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(tok, "RShift", StringComparison.OrdinalIgnoreCase))
                    {
                        mods |= KeyModifierMask.Shift;
                    }
                    else if (string.Equals(tok, "Alt", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(tok, "LAlt", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(tok, "RAlt", StringComparison.OrdinalIgnoreCase))
                    {
                        mods |= KeyModifierMask.Alt;
                    }
                    else if (i == parts.Length - 1 &&
                             !char.IsDigit(tok[0]) &&
                             Enum.TryParse<KeyCode>(tok, true, out var parsedKey) &&
                             Enum.IsDefined(typeof(KeyCode), parsedKey))
                    {
                        key = parsedKey;
                        sawKey = true;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (!sawKey || key == KeyCode.None) return false;
                mods &= ~ComboKeyStore.GetModifierBit(key);
                return true;
            }
            return false;
        }

        internal static string FormatComboString(KeyModifierMask mods, KeyCode key)
        {
            if (key == KeyCode.None) return "None";
            mods &= ~ComboKeyStore.GetModifierBit(key);
            if (mods == KeyModifierMask.None) return key.ToString();

            var sb = new StringBuilder(24);
            if ((mods & KeyModifierMask.Ctrl) != 0) sb.Append("Ctrl+");
            if ((mods & KeyModifierMask.Shift) != 0) sb.Append("Shift+");
            if ((mods & KeyModifierMask.Alt) != 0) sb.Append("Alt+");
            sb.Append(key.ToString());
            return sb.ToString();
        }

        internal static bool IsHotkeyEntry(ConfigEntry entry)
        {
            return IsHotkeyEntry(entry, null);
        }

        internal static bool IsHotkeyEntry(ConfigEntry entry, string? canonicalId)
        {
            if (entry.ValueType == typeof(KeyCode)) return true;

            // 用户在“模组排查”视图中主动标记为快捷键的 string / int 配置项无条件接入
            if (!string.IsNullOrEmpty(canonicalId) &&
                (entry.ValueType == typeof(string) || entry.ValueType == typeof(int)) &&
                ComboKeyStore.IsForcedHotkey(canonicalId))
            {
                return true;
            }

            // 布尔开关、浮点滑块或有范围限制的数值条绝不是按键绑定
            if (entry.ValueType == typeof(bool) ||
                entry.ValueType == typeof(float) ||
                entry.ValueType == typeof(double) ||
                entry.HasRange)
            {
                return false;
            }

            if (entry.ValueType != typeof(string) && entry.ValueType != typeof(int))
            {
                return false;
            }

            string key = entry.Key ?? string.Empty;
            string label = entry.Label?.Invoke() ?? string.Empty;

            if (ContainsNonHotkeyExclusion(key, label))
            {
                return false;
            }

            if (entry.ValueType == typeof(string) && entry.Choices != null && entry.Choices.Length > 0)
            {
                if (entry.Choices.Length > 5)
                {
                    int validKeyCodes = 0;
                    foreach (var c in entry.Choices)
                    {
                        if (Enum.TryParse<KeyCode>(c, true, out _)) validKeyCodes++;
                    }
                    if (validKeyCodes >= entry.Choices.Length / 2) return true;
                }
                return false;
            }

            if (entry.ValueType == typeof(int) && entry.Choices != null && entry.Choices.Length > 0)
            {
                return false;
            }

            if (entry.ValueType == typeof(string))
            {
                string? sDef = entry.BoxedDefault as string;
                string? sVal = entry.BoxedValue as string;

                if ((sDef != null && (sDef.IndexOf(',') >= 0 || sDef.IndexOf(';') >= 0)) ||
                    (sVal != null && (sVal.IndexOf(',') >= 0 || sVal.IndexOf(';') >= 0)))
                {
                    return false;
                }

                bool hasNonEmptyDef = !string.IsNullOrWhiteSpace(sDef);
                bool hasNonEmptyVal = !string.IsNullOrWhiteSpace(sVal);
                KeyModifierMask defMods = KeyModifierMask.None;
                KeyModifierMask valMods = KeyModifierMask.None;
                KeyCode defKey = KeyCode.None;
                KeyCode valKey = KeyCode.None;
                bool defParses = hasNonEmptyDef && TryParseComboString(sDef, out defMods, out defKey);
                bool valParses = hasNonEmptyVal && TryParseComboString(sVal, out valMods, out valKey);

                if (hasNonEmptyVal && !valParses) return false;
                if (hasNonEmptyDef && !defParses) return false;

                if ((defParses && (defMods != KeyModifierMask.None || IsUnambiguousKeyCodeStringTarget(defKey))) ||
                    (valParses && (valMods != KeyModifierMask.None || IsUnambiguousKeyCodeStringTarget(valKey))))
                {
                    return true;
                }

                return HasHotkeyIdentifier(key, label);
            }

            if (HasNonHotkeyIntToken(key, label))
            {
                return false;
            }

            return HasHotkeyIdentifier(key, label);
        }

        private static bool IsUnambiguousKeyCodeStringTarget(KeyCode kc)
        {
            if (kc == KeyCode.None) return false;
            return (kc >= KeyCode.F1 && kc <= KeyCode.F15) ||
                   (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6) ||
                   (kc >= KeyCode.Keypad0 && kc <= KeyCode.KeypadEquals) ||
                   (kc >= KeyCode.RightShift && kc <= KeyCode.LeftAlt) ||
                   (kc >= KeyCode.UpArrow && kc <= KeyCode.LeftArrow) ||
                   kc == KeyCode.BackQuote ||
                   kc == KeyCode.CapsLock ||
                   kc == KeyCode.PageUp ||
                   kc == KeyCode.PageDown;
        }

        private static ModHotkeyEntry? CreateHotkeyFromConfigEntry(ModOptionGroup group, ConfigEntry entry, string canonicalId, string label, string tooltip)
        {
            bool usesComboString = entry.ValueType == typeof(string) &&
                (string.Equals(group.ModId, "nicokobo.speed-up", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(group.ModId, "smart_inventory_arrange", StringComparison.OrdinalIgnoreCase) ||
                 (entry.BoxedDefault is string defS && defS.IndexOf('+') >= 0) ||
                 (entry.BoxedValue is string valS && valS.IndexOf('+') >= 0));

            KeyCode ParseKey(object? boxed)
            {
                return TryParseComboString(boxed, out _, out var parsed) ? parsed : KeyCode.None;
            }

            KeyModifierMask ParseModifiers(object? boxed)
            {
                return TryParseComboString(boxed, out var mods, out _) ? mods : KeyModifierMask.None;
            }

            KeyCode defaultKey = ParseKey(entry.BoxedDefault);
            KeyModifierMask defaultMods = usesComboString ? ParseModifiers(entry.BoxedDefault) : KeyModifierMask.None;

            var hk = new ModHotkeyEntry
            {
                Id = canonicalId,
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = label,
                Tooltip = tooltip,
                DefaultKey = defaultKey,
                DefaultModifiers = defaultMods,
                HasExplicitDefault = entry.BoxedDefault != null,
                GetKey = () => ParseKey(entry.BoxedValue),
                SetKey = (newKey) =>
                {
                    if (entry.ValueType == typeof(KeyCode))
                    {
                        SetEntryBoxed(entry, newKey);
                    }
                    else if (entry.ValueType == typeof(int))
                    {
                        SetEntryBoxed(entry, (int)newKey);
                    }
                    else
                    {
                        SetEntryBoxed(entry, newKey.ToString());
                    }
                }
            };

            if (usesComboString)
            {
                hk.GetExternalModifiers = () => ParseModifiers(entry.BoxedValue);
                hk.SetCombo = (newMods, newKey) => SetEntryBoxed(entry, FormatComboString(newMods, newKey));
            }

            return hk;
        }

        private static ModGeneralEntry? CreateGeneralFromConfigEntry(ModOptionGroup group, ConfigEntry entry, string label, string tooltip)
        {
            var item = new ModGeneralEntry
            {
                Id = $"{group.ModId}.{entry.Key}",
                ModId = group.ModId,
                DisplayName = label,
                Tooltip = tooltip,
                ResetToDefault = () => ResetEntryToDefault(entry)
            };

            if (entry.ValueType == typeof(bool))
            {
                item.Kind = OptionKind.Toggle;
                item.GetBool = () => (bool)(entry.BoxedValue ?? false);
                item.SetBool = (val) => SetEntryBoxed(entry, val);
            }
            else if (entry.HasRange || entry.ValueType == typeof(float) || entry.ValueType == typeof(int))
            {
                item.Kind = OptionKind.Slider;
                item.Min = entry.Min;
                item.Max = entry.Max > entry.Min ? entry.Max : 100f;
                item.WholeNumbers = entry.ValueType == typeof(int);
                item.GetFloat = () => Convert.ToSingle(entry.BoxedValue ?? 0f);
                item.SetFloat = (val) =>
                {
                    if (entry.ValueType == typeof(int))
                    {
                        SetEntryBoxed(entry, Mathf.RoundToInt(val));
                    }
                    else
                    {
                        SetEntryBoxed(entry, val);
                    }
                };
            }
            else if (entry.Choices != null && entry.Choices.Length > 0)
            {
                item.Kind = OptionKind.Dropdown;
                item.GetChoices = () => entry.Choices.ToList();
                item.GetIndex = () =>
                {
                    string cur = entry.BoxedValue?.ToString() ?? string.Empty;
                    int idx = Array.IndexOf(entry.Choices, cur);
                    return idx >= 0 ? idx : 0;
                };
                item.SetIndex = (idx) =>
                {
                    if (idx >= 0 && idx < entry.Choices.Length)
                    {
                        SetEntryBoxed(entry, entry.Choices[idx]);
                    }
                };
            }
            else
            {
                return null;
            }

            return item;
        }

        /// <summary>
        /// 扫描官方 GameOptionRegistry 中由第三方模组注册的所有配置与快捷键（含直接注入原版区且未填 source 的条项）
        /// </summary>
        private static void ScanGameOptionRegistryModEntries(List<ModOptionGroup> groups, HashSet<string>? registryMatchedModIds = null)
        {
            try
            {
                var sections = GameOptionRegistry.Sections;
                if (sections == null) return;

                List<LoadedMod>? loadedMods = null;
                try { loadedMods = ModLoader.Mods; } catch { }
                Dictionary<string, LoadedMod>? modById = null;
                Dictionary<Assembly, LoadedMod>? modByAsm = null;
                if (loadedMods != null)
                {
                    modById = new Dictionary<string, LoadedMod>(loadedMods.Count, StringComparer.OrdinalIgnoreCase);
                    modByAsm = new Dictionary<Assembly, LoadedMod>(loadedMods.Count);
                    for (int i = 0; i < loadedMods.Count; i++)
                    {
                        var lm = loadedMods[i];
                        if (lm?.Manifest == null) continue;
                        if (!string.IsNullOrEmpty(lm.Manifest.ID) && !modById.ContainsKey(lm.Manifest.ID))
                        {
                            modById[lm.Manifest.ID] = lm;
                        }
                        if (lm.Assembly != null && !modByAsm.ContainsKey(lm.Assembly))
                        {
                            modByAsm[lm.Assembly] = lm;
                        }
                    }
                }

                foreach (var sec in sections)
                {
                    if (sec == null || sec.entries == null) continue;
                    bool isVanillaSec = GameOptionRegistry.IsVanillaSection(sec.id);

                    foreach (var entry in sec.entries)
                    {
                        if (entry == null) continue;
                        if (IsPureVanillaEntry(sec, entry)) continue;

                        string rawSource = !string.IsNullOrEmpty(entry.source)
                            ? entry.source
                            : (!isVanillaSec && !string.IsNullOrEmpty(sec.source) ? sec.source : string.Empty);

                        string source = rawSource;
                        string disp = string.Empty;

                        if (!string.IsNullOrEmpty(source))
                        {
                            if (IsSelfModId(source)) continue;
                            if (modById != null && modById.TryGetValue(source, out var m) && m?.Manifest != null)
                            {
                                if (!string.IsNullOrEmpty(m.Manifest.Name)) disp = m.Manifest.Name;
                            }
                            else if (modByAsm != null)
                            {
                                // 若 source 写错或与 Manifest.ID 不一致，通过入口委托归属程序集找回真实模组
                                Assembly? extAsm = GetEntryExternalAssembly(entry);
                                if (extAsm == SelfAssembly) continue;
                                if (extAsm != null && modByAsm.TryGetValue(extAsm, out var asmMod) && asmMod?.Manifest != null)
                                {
                                    source = asmMod.Manifest.ID;
                                    if (!string.IsNullOrEmpty(asmMod.Manifest.Name)) disp = asmMod.Manifest.Name;
                                }
                            }
                        }
                        else
                        {
                            Assembly? extAsm = GetEntryExternalAssembly(entry);
                            if (extAsm == SelfAssembly) continue;

                            if (extAsm != null && modByAsm != null && modByAsm.TryGetValue(extAsm, out var m) && m?.Manifest != null)
                            {
                                source = m.Manifest.ID;
                                disp = m.Manifest.Name;
                            }

                            if (string.IsNullOrEmpty(source) && extAsm != null)
                            {
                                source = extAsm.GetName().Name ?? "external_mod";
                            }
                            if (string.IsNullOrEmpty(source))
                            {
                                source = !isVanillaSec ? sec.id : "external_mod";
                            }
                        }

                        if (IsSelfModId(source)) continue;

                        if (string.IsNullOrEmpty(disp))
                        {
                            string? secTitle = !isVanillaSec ? sec.label?.Invoke() : null;
                            disp = !string.IsNullOrEmpty(secTitle) ? secTitle : source;
                        }

                        var group = GetOrCreateGroup(groups, source, disp);
                        if (!string.IsNullOrEmpty(rawSource) &&
                            !string.Equals(rawSource, group.ModId, StringComparison.OrdinalIgnoreCase) &&
                            !group.ExtraNotifyModIds.Any(id => string.Equals(id, rawSource, StringComparison.OrdinalIgnoreCase)))
                        {
                            group.ExtraNotifyModIds.Add(rawSource);
                        }
                        registryMatchedModIds?.Add(group.ModId);
                        string label = entry.label?.Invoke() ?? entry.id;
                        string tooltip = entry.tooltip?.Invoke() ?? string.Empty;

                        if (entry.kind == GameOptionRegistry.Kind.KeyBind)
                        {
                            if (ContainsHotkey(group, entry.id, label)) continue;
                            group.Hotkeys.Add(new ModHotkeyEntry
                            {
                                Id = entry.id,
                                RawOptionId = entry.id,
                                ModId = group.ModId,
                                ModDisplayName = group.DisplayName,
                                DisplayName = label,
                                Tooltip = tooltip,
                                GetKey = entry.getKey ?? (() => KeyCode.None),
                                SetKey = entry.setKey
                            });
                        }
                        else if (entry.kind == GameOptionRegistry.Kind.Toggle ||
                                 entry.kind == GameOptionRegistry.Kind.Slider ||
                                 entry.kind == GameOptionRegistry.Kind.Dropdown)
                        {
                            if (ContainsGeneral(group, entry.id, label)) continue;
                            var gen = new ModGeneralEntry
                            {
                                Id = entry.id,
                                ModId = group.ModId,
                                DisplayName = label,
                                Tooltip = tooltip
                            };

                            if (entry.kind == GameOptionRegistry.Kind.Toggle)
                            {
                                gen.Kind = OptionKind.Toggle;
                                gen.GetBool = entry.getBool;
                                gen.SetBool = entry.setBool;
                            }
                            else if (entry.kind == GameOptionRegistry.Kind.Slider)
                            {
                                gen.Kind = OptionKind.Slider;
                                gen.GetFloat = entry.getFloat;
                                gen.SetFloat = entry.setFloat;
                                gen.Min = entry.min;
                                gen.Max = entry.max;
                                gen.WholeNumbers = entry.wholeNumbers;
                                gen.ValueFormatter = entry.valueText;
                            }
                            else if (entry.kind == GameOptionRegistry.Kind.Dropdown)
                            {
                                gen.Kind = OptionKind.Dropdown;
                                gen.GetIndex = entry.getIndex;
                                gen.SetIndex = entry.setIndex;
                                gen.GetChoices = entry.options;
                            }

                            group.GeneralSettings.Add(gen);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error scanning GameOptionRegistry mod entries: {ex.Message}");
            }
        }
    }
}
