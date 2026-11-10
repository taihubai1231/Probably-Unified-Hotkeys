using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AllowUnbindKey
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

        public static void SetEntryBoxed(ConfigEntry entry, object value)
        {
            try
            {
                ApplyBoxedMethod?.Invoke(entry, new object[] { value });
                entry.Config?.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AllowUnbindKey] Error setting config entry {entry.Key}: {ex.Message}");
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
                Debug.LogWarning($"[AllowUnbindKey] Error resetting config entry {entry.Key}: {ex.Message}");
            }
        }

        /// <summary>
        /// 扫描全量已启用模组，构建设置树
        /// </summary>
        public static List<ModOptionGroup> ScanAll(out List<ModHotkeyEntry> allHotkeysForConflict)
        {
            var groups = new List<ModOptionGroup>();
            var knownGroups = KnownModAdapters.TryAdaptKnownMods();
            var handledModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kg in knownGroups)
            {
                groups.Add(kg);
                handledModIds.Add(kg.ModId);
            }

            // 扫描所有 ModLoader 中的已加载模组
            var loadedMods = ModLoader.Mods;
            if (loadedMods != null)
            {
                foreach (var mod in loadedMods)
                {
                    if (mod == null || mod.Manifest == null) continue;
                    string modId = mod.Manifest.ID;
                    if (string.Equals(modId, "allow_unbind_key", StringComparison.OrdinalIgnoreCase)) continue;

                    string displayName = mod.Manifest.Name;
                    if (string.IsNullOrEmpty(displayName)) displayName = modId;

                    // 检查是否已有适配器处理该模组
                    ModOptionGroup? group = groups.FirstOrDefault(g => string.Equals(g.ModId, modId, StringComparison.OrdinalIgnoreCase));
                    if (group == null)
                    {
                        group = new ModOptionGroup
                        {
                            ModId = modId,
                            DisplayName = displayName,
                            PinyinSortKey = PinyinEngine.ToInitials(displayName)
                        };
                    }

                    // 扫描 ModConfig
                    ModConfig? config = ModConfig.Find(modId);
                    if (config != null && config.HasEntries)
                    {
                        group.ResetToDefaultsAction = () =>
                        {
                            config.ResetToDefaults();
                            config.Save();
                        };

                        foreach (var entry in config.Entries)
                        {
                            if (entry == null) continue;
                            string entryKey = entry.Key;

                            // 避免与已知适配器中已定义的项重复
                            if (group.Hotkeys.Any(h => h.Id.EndsWith(entryKey, StringComparison.OrdinalIgnoreCase)) ||
                                group.GeneralSettings.Any(g => g.Id.EndsWith(entryKey, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            string label = entry.Label?.Invoke() ?? entryKey;
                            string tooltip = entry.Tooltip?.Invoke() ?? string.Empty;

                            if (IsHotkeyEntry(entry))
                            {
                                // 识别为快捷键
                                var hotkey = CreateHotkeyFromConfigEntry(group, entry, label, tooltip);
                                if (hotkey != null)
                                {
                                    group.Hotkeys.Add(hotkey);
                                }
                            }
                            else
                            {
                                // 识别为常规通用配置
                                var general = CreateGeneralFromConfigEntry(group, entry, label, tooltip);
                                if (general != null)
                                {
                                    group.GeneralSettings.Add(general);
                                }
                            }
                        }
                    }

                    if (group.TotalItemCount > 0 && !groups.Contains(group))
                    {
                        groups.Add(group);
                    }
                }
            }

            // 扫描官方 GameOptionRegistry 中由第三方模组注册的所有配置与快捷键
            ScanGameOptionRegistryModEntries(groups);

            // 按拼音首字母排序
            groups.Sort((a, b) =>
            {
                int cmp = string.Compare(a.PinyinSortKey, b.PinyinSortKey, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            // 汇总全量快捷键用于冲突检测（包含原版与所有模组）
            allHotkeysForConflict = new List<ModHotkeyEntry>();

            // 1. 原版快捷键
            allHotkeysForConflict.AddRange(GetVanillaKeybinds());

            // 2. 模组快捷键
            foreach (var g in groups)
            {
                allHotkeysForConflict.AddRange(g.Hotkeys);
            }

            return groups;
        }

        /// <summary>
        /// 提取原版所有按键绑定
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
                        if (entry.kind == GameOptionRegistry.Kind.KeyBind)
                        {
                            string label = entry.label?.Invoke() ?? entry.id;
                            string tooltip = entry.tooltip?.Invoke() ?? string.Empty;

                            list.Add(new ModHotkeyEntry
                            {
                                Id = "vanilla." + entry.id,
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
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AllowUnbindKey] Error getting vanilla keybinds: {ex.Message}");
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

            // 检查 Choices 是否全部是 KeyCode 名称
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
        /// 扫描官方 GameOptionRegistry 中由第三方模组注册的所有配置与快捷键
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
                        string source = entry.source ?? (isVanillaSec ? "vanilla" : (sec.source ?? sec.id));
                        if (string.IsNullOrEmpty(source) ||
                            string.Equals(source, "vanilla", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(source, "allow_unbind_key", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var group = groups.FirstOrDefault(g => string.Equals(g.ModId, source, StringComparison.OrdinalIgnoreCase));
                        if (group == null)
                        {
                            string disp = source;
                            if (loadedMods != null)
                            {
                                var m = loadedMods.FirstOrDefault(x => x?.Manifest != null && string.Equals(x.Manifest.ID, source, StringComparison.OrdinalIgnoreCase));
                                if (m?.Manifest != null && !string.IsNullOrEmpty(m.Manifest.Name))
                                {
                                    disp = m.Manifest.Name;
                                }
                            }

                            group = new ModOptionGroup
                            {
                                ModId = source,
                                DisplayName = disp,
                                PinyinSortKey = PinyinEngine.ToInitials(disp)
                            };
                            groups.Add(group);
                        }

                        string label = entry.label?.Invoke() ?? entry.id;
                        string tooltip = entry.tooltip?.Invoke() ?? string.Empty;

                        if (entry.kind == GameOptionRegistry.Kind.KeyBind)
                        {
                            if (group.Hotkeys.Any(h => h.Id == entry.id)) continue;
                            group.Hotkeys.Add(new ModHotkeyEntry
                            {
                                Id = entry.id,
                                ModId = group.ModId,
                                ModDisplayName = group.DisplayName,
                                DisplayName = label,
                                Tooltip = tooltip,
                                GetKey = entry.getKey ?? (() => KeyCode.None),
                                SetKey = entry.setKey
                            });
                        }
                        else
                        {
                            if (group.GeneralSettings.Any(g => g.Id == entry.id)) continue;
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
                Debug.LogWarning($"[AllowUnbindKey] Error scanning GameOptionRegistry mod entries: {ex.Message}");
            }
        }
    }
}
