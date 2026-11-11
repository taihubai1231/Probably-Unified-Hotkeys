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
            "Key", "Hotkey", "Shortcut", "Keybind", "Bind", "ToggleKey", "OpenKey", "TriggerKey"
        };

        private static readonly string[] ChineseHotkeyKeywords =
        {
            "按键", "快捷键", "热键", "键位"
        };

        private static readonly string[] ExcludedNonHotkeyKeywords =
        {
            "keyword", "keycard", "key_card", "关键词", "关键字", "门禁卡", "房卡"
        };

        private static readonly MethodInfo? ApplyBoxedMethod = AccessTools.Method(typeof(ConfigEntry), "ApplyBoxed", new Type[] { typeof(object) });
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
                ApplyBoxedMethod?.Invoke(entry, new object[] { value });
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
                    ApplyBoxedMethod?.Invoke(entry, new object[] { entry.BoxedDefault });
                    entry.Config?.Save();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error resetting config entry {entry.Key}: {ex.Message}");
            }
        }

        /// <summary>
        /// 扫描全量已启用模组，构建设置树
        /// </summary>
        public static List<ModOptionGroup> ScanAll(out List<ModHotkeyEntry> allHotkeysForConflict)
        {
            ComboKeyStore.EnsureLoaded();
            using var _bypass = ComboKeyStore.EnterBypass();
            var groups = new List<ModOptionGroup>();

            // 1. 优先扫描官方 ModConfig（含完整 BoxedDefault 默认值与取值范围）
            var loadedMods = ModLoader.Mods;
            if (loadedMods != null)
            {
                foreach (var mod in loadedMods)
                {
                    if (mod == null || mod.Manifest == null) continue;
                    string modId = mod.Manifest.ID;
                    if (IsSelfModId(modId)) continue;

                    ModConfig? config = ModConfig.Find(modId);
                    if (config == null || !config.HasEntries) continue;

                    string displayName = string.IsNullOrEmpty(mod.Manifest.Name) ? modId : mod.Manifest.Name;
                    var group = GetOrCreateGroup(groups, modId, displayName);

                    foreach (var entry in config.Entries)
                    {
                        if (entry == null) continue;
                        string entryKey = entry.Key;
                        string label = entry.Label?.Invoke() ?? entryKey;
                        string tooltip = entry.Tooltip?.Invoke() ?? string.Empty;

                        if (IsHotkeyEntry(entry))
                        {
                            var hotkey = CreateHotkeyFromConfigEntry(group, entry, label, tooltip);
                            if (hotkey != null && !ContainsHotkey(group, hotkey.Id, hotkey.DisplayName))
                            {
                                group.Hotkeys.Add(hotkey);
                            }
                        }
                        else
                        {
                            var general = CreateGeneralFromConfigEntry(group, entry, label, tooltip);
                            if (general != null && !ContainsGeneral(group, general.Id, general.DisplayName))
                            {
                                group.GeneralSettings.Add(general);
                            }
                        }
                    }
                }
            }

            // 2. 全面扫描 GameOptionRegistry 中的所有第三方模组配置与快捷键（含未指定 source 的外部程序集条目）
            ScanGameOptionRegistryModEntries(groups);

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

                ModConfig? modCfg = ModConfig.Find(group.ModId);
                var capturedGroup = group;
                capturedGroup.ResetToDefaultsAction = () =>
                {
                    if (modCfg != null && modCfg.HasEntries)
                    {
                        modCfg.ResetToDefaults();
                        modCfg.Save();
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

                    foreach (var gen in capturedGroup.GeneralSettings)
                    {
                        gen.PendingBool = null;
                        gen.PendingFloat = null;
                        gen.PendingIndex = null;
                        if (modCfg == null || gen.ResetToDefault == null)
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

            return groups;
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

            return ContainsExactHotkeyToken(key) || ContainsExactHotkeyToken(label);
        }

        private static bool ContainsExactHotkeyToken(string? text)
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
                        if (ExactHotkeyTokens.Contains(token))
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
            if (entry.ValueType == typeof(KeyCode)) return true;

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
                bool defParses = hasNonEmptyDef && TryParseComboString(sDef, out defMods, out _);
                bool valParses = hasNonEmptyVal && TryParseComboString(sVal, out valMods, out _);

                if (hasNonEmptyVal && !valParses) return false;
                if (hasNonEmptyDef && !defParses) return false;

                if ((defParses && defMods != KeyModifierMask.None) || (valParses && valMods != KeyModifierMask.None))
                {
                    return true;
                }

                return HasHotkeyIdentifier(key, label);
            }

            return HasHotkeyIdentifier(key, label);
        }

        private static ModHotkeyEntry? CreateHotkeyFromConfigEntry(ModOptionGroup group, ConfigEntry entry, string label, string tooltip)
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
                Id = $"{group.ModId}.{entry.Key}",
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
        private static void ScanGameOptionRegistryModEntries(List<ModOptionGroup> groups)
        {
            try
            {
                var sections = GameOptionRegistry.Sections;
                if (sections == null) return;

                var loadedMods = ModLoader.Mods;
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

                        string source = !string.IsNullOrEmpty(entry.source)
                            ? entry.source
                            : (!isVanillaSec && !string.IsNullOrEmpty(sec.source) ? sec.source : string.Empty);

                        string disp = string.Empty;

                        if (!string.IsNullOrEmpty(source))
                        {
                            if (IsSelfModId(source)) continue;
                            if (modById != null && modById.TryGetValue(source, out var m) && m?.Manifest != null && !string.IsNullOrEmpty(m.Manifest.Name))
                            {
                                disp = m.Manifest.Name;
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
