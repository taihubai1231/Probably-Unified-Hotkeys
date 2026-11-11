using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 全覆盖模组设置与快捷键扫描聚合器
    /// </summary>
    public static class ModOptionScanner
    {
        private static readonly HashSet<string> HotkeyKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Key", "Hotkey", "Shortcut", "Bind", "ToggleKey", "OpenKey", "TriggerKey", "按键", "快捷键", "热键"
        };

        private static readonly MethodInfo? ApplyBoxedMethod = AccessTools.Method(typeof(ConfigEntry), "ApplyBoxed", new Type[] { typeof(object) });
        private static readonly Assembly VanillaAssembly = typeof(GameOptionRegistry).Assembly;
        private static readonly Assembly SelfAssembly = typeof(ModOptionScanner).Assembly;

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
            string name = asm.GetName().Name ?? string.Empty;
            return name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Mono.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 检测 GameOptionRegistry 条目委托是否归属于第三方外部程序集（防止无 source 注册的模组逃逸到原版设置页）
        /// </summary>
        public static Assembly? GetEntryExternalAssembly(GameOptionRegistry.Entry? entry)
        {
            if (entry == null) return null;

            Delegate?[] delegates = new Delegate?[]
            {
                entry.getKey, entry.setKey,
                entry.getBool, entry.setBool,
                entry.getFloat, entry.setFloat,
                entry.getIndex, entry.setIndex,
                entry.label, entry.tooltip,
                entry.options, entry.valueText,
                entry.interactable, entry.overrideOptions
            };

            foreach (var d in delegates)
            {
                if (d == null) continue;
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
            }
            return null;
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
                        initMods = ComboKeyStore.GetSavedModifiers(hk.Id, initKey);
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
                        hk.SetKey?.Invoke(hk.ResetTargetKey);
                        ComboKeyStore.SetSavedCombo(hk.Id, hk.ResetTargetModifiers, hk.ResetTargetKey);
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

        private static bool IsHotkeyEntry(ConfigEntry entry)
        {
            if (entry.ValueType == typeof(KeyCode)) return true;

            string key = entry.Key;
            string label = entry.Label?.Invoke() ?? string.Empty;

            foreach (var kw in HotkeyKeywords)
            {
                if (key.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    label.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            if (entry.Choices != null && entry.Choices.Length > 5)
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

        private static ModHotkeyEntry? CreateHotkeyFromConfigEntry(ModOptionGroup group, ConfigEntry entry, string label, string tooltip)
        {
            KeyCode ParseKey(object? boxed)
            {
                if (boxed is KeyCode kc) return kc;
                if (boxed is int i) return (KeyCode)i;
                if (boxed is string s)
                {
                    if (string.Equals(s, "None", StringComparison.OrdinalIgnoreCase) || s == "0") return KeyCode.None;
                    if (Enum.TryParse<KeyCode>(s, true, out var parsed)) return parsed;
                }
                return KeyCode.None;
            }

            KeyCode defaultKey = ParseKey(entry.BoxedDefault);

            return new ModHotkeyEntry
            {
                Id = $"{group.ModId}.{entry.Key}",
                ModId = group.ModId,
                ModDisplayName = group.DisplayName,
                DisplayName = label,
                Tooltip = tooltip,
                DefaultKey = defaultKey,
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
                            if (loadedMods != null)
                            {
                                var m = loadedMods.FirstOrDefault(x => x?.Manifest != null &&
                                    string.Equals(x.Manifest.ID, source, StringComparison.OrdinalIgnoreCase));
                                if (m?.Manifest != null && !string.IsNullOrEmpty(m.Manifest.Name))
                                {
                                    disp = m.Manifest.Name;
                                }
                            }
                        }
                        else
                        {
                            Assembly? extAsm = GetEntryExternalAssembly(entry);
                            if (extAsm == SelfAssembly) continue;

                            if (extAsm != null && loadedMods != null)
                            {
                                var m = loadedMods.FirstOrDefault(x => x != null && x.Assembly == extAsm && x.Manifest != null);
                                if (m?.Manifest != null)
                                {
                                    source = m.Manifest.ID;
                                    disp = m.Manifest.Name;
                                }
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
