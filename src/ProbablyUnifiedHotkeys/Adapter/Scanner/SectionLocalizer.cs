#nullable enable
using System;
using System.Collections.Generic;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 三层根治式模组分区标题本地化引擎：
    /// 1. 优先查询游戏原生 LoadedMod.Localization 多语言表（支持原始本地化键名直查 + 启动期语言错位反向对齐）
    /// 2. 命中内置创意工坊全生态分类词典（覆盖 nicokobo.speed-up 等内嵌字典模组及纯英文分类模组）
    /// 3. 对未知未来模组的 xxx.config.yyy / settings_section_zzz 点分或下划线键名自动剥离前缀并人性化翻译
    /// 严禁引用 TMPro 或 UnityEngine.UI，保证单向依赖且支持 .NET 8 离线单测。
    /// </summary>
    public static class SectionLocalizer
    {
        public static readonly string[] ChineseLocaleCandidates = new string[]
        {
            "zh", "zh-CN", "zh-Hans", "zh_CN", "zh-TW", "zh-Hant", "zh_TW", "cn", "chs"
        };

        public static readonly string[] EnglishLocaleCandidates = new string[]
        {
            "en", "en-US", "en_US", "en-GB", "eng"
        };

        public static readonly Dictionary<string, (string Zh, string En)> KnownSectionTitleTable =
            new Dictionary<string, (string Zh, string En)>(StringComparer.OrdinalIgnoreCase)
            {
                // 本模组 7 大分区标题
                { "语言设置 / Language", ("语言设置", "Language") },
                { "界面语言 / UI Language", ("界面语言", "UI Language") },
                { "选项页面布局 / Options Page Layout", ("选项页面布局", "Options Page Layout") },
                { "选项页顶部工具栏 / Options Top Toolbar", ("选项页顶部工具栏", "Options Top Toolbar") },
                { "快捷键绑定规则 / Keybinding Rules", ("快捷键绑定规则", "Keybinding Rules") },
                { "改造件页面设置 / Mods Page", ("改造件页面设置", "Mods Page") },
                { "预载及退出加速 / Preload & Fast Exit", ("预载及退出加速", "Preload & Fast Exit") },
                { "调试日志 / Debug Logging", ("调试日志", "Debug Logging") },
                { "可选功能模块 / Modular Features", ("可选功能模块", "Modular Features") },

                // 通用默认分类
                { "General", ("常规", "General") },
                { "Default", ("常规", "General") },
                { "General Settings", ("常规", "General") },
                { "Settings", ("常规", "General") },
                { "Config", ("常规", "General") },
                { "Configuration", ("常规", "General") },
                { "Options", ("常规", "General") },
                { "Main", ("常规", "General") },

                // nicokobo.speed-up (Nicokobo.ModUtilities ModText.AuthoredTables 静态字典键)
                { "speed.config.dialogue", ("对话", "Dialogue") },
                { "speed.config.animation", ("动画", "Animation") },
                { "speed.config.global", ("全局", "Global") },
                { "speed.config.display", ("显示", "Display") },
                { "speed.config.hotkeys", ("快捷键", "Hotkeys") },

                // item_details (启动阶段未打开 ModConfigWindow 时残留的英文分类或键名)
                { "config.section.info", ("附加信息", "Extra Info") },
                { "Extra Info", ("附加信息", "Extra Info") },
                { "config.section.reveal", ("透视与揭露", "Reveal") },
                { "Reveal", ("透视与揭露", "Reveal") },
                { "config.section.layout", ("布局与排版", "Layout") },
                { "Layout", ("布局与排版", "Layout") },
                { "config.section.interaction", ("控制与交互", "Controls") },

                // smart_inventory_arrange
                { "Arrangement", ("整理规则", "Arrangement") },
                { "Shortcuts", ("快捷操作", "Shortcuts") },
                { "Diagnostics", ("诊断与日志", "Diagnostics") },

                // item_durability_bars (物品耐久显示)
                { "Item display", ("物品显示", "Item Display") },
                { "Other settings", ("其他设置", "Other Settings") },

                // better_liquid_scanner (更好的液体扫描仪)
                { "Display", ("显示设置", "Display") },

                // deep_space_trainer
                { "QoL", ("便利性功能", "QoL") },
                { "Quality of Life", ("便利性功能", "Quality of Life") },

                // item_spawner
                { "spawn", ("生成设置", "Spawn") },

                // faction_reputation_expansion 及常见模组分类
                { "Controls", ("控制与交互", "Controls") },
                { "Control", ("控制与交互", "Control") },
                { "Interaction", ("控制与交互", "Interaction") },
                { "Presentation", ("界面与表现", "Presentation") },
                { "Content", ("内容设置", "Content") },

                // local.no-lost-items (NoF**kingDropItems)
                { "counter", ("柜台防丢", "Counter") },
                { "storage", ("仓储防丢", "Storage") },
                { "suppliers", ("供货商防丢", "Suppliers") },

                // 创意工坊常见通用分类词元
                { "Debug", ("调试", "Debug") },
                { "Debugging", ("调试", "Debugging") },
                { "Hotkeys", ("快捷键", "Hotkeys") },
                { "Hotkey", ("快捷键", "Hotkey") },
                { "Keybinds", ("快捷键", "Keybinds") },
                { "Keybindings", ("快捷键", "Keybindings") },
                { "Dialogue", ("对话", "Dialogue") },
                { "Dialog", ("对话", "Dialog") },
                { "Animation", ("动画", "Animation") },
                { "Animations", ("动画", "Animations") },
                { "Global", ("全局", "Global") },
                { "Audio", ("音频与音效", "Audio") },
                { "Sound", ("音频与音效", "Sound") },
                { "Sounds", ("音频与音效", "Sounds") },
                { "UI", ("界面设置", "UI") },
                { "Interface", ("界面设置", "Interface") },
                { "Visual", ("外观与视觉", "Visual") },
                { "Visuals", ("外观与视觉", "Visuals") },
                { "Appearance", ("外观与视觉", "Appearance") },
                { "Gameplay", ("玩法机制", "Gameplay") },
                { "Mechanics", ("玩法机制", "Mechanics") },
                { "Performance", ("性能与优化", "Performance") },
                { "Optimization", ("性能与优化", "Optimization") },
                { "Advanced", ("高级设置", "Advanced") },
                { "Filter", ("过滤与筛选", "Filter") },
                { "Filters", ("过滤与筛选", "Filters") },
                { "Filtering", ("过滤与筛选", "Filtering") },
                { "Notification", ("通知与提示", "Notification") },
                { "Notifications", ("通知与提示", "Notifications") },
                { "Experimental", ("实验性功能", "Experimental") },
                { "Compatibility", ("兼容性", "Compatibility") },
                { "Misc", ("其他设置", "Misc") },
                { "Miscellaneous", ("其他设置", "Miscellaneous") },
                { "Other", ("其他设置", "Other") },
                { "Others", ("其他设置", "Others") },
                { "Info", ("附加信息", "Info") },
                { "Information", ("附加信息", "Information") },
                { "Trade", ("交易设置", "Trade") },
                { "Trading", ("交易设置", "Trading") },
                { "Shop", ("店铺设置", "Shop") },
                { "Inventory", ("背包与库存", "Inventory") },
                { "Items", ("物品设置", "Items") },
                { "Item", ("物品设置", "Item") },
                { "Highlight", ("高亮设置", "Highlight") },
                { "Tooltip", ("提示框设置", "Tooltip") },
                { "Tooltips", ("提示框设置", "Tooltips") }
            };

        /// <summary>
        /// 三层根治式模组分区标题本地化引擎：
        /// 1. 优先查询游戏原生 LoadedMod.Localization 多语言表（支持原始本地化键名直查 + 启动期语言错位反向对齐）
        /// 2. 命中内置创意工坊全生态分类词典（覆盖 nicokobo.speed-up 等内嵌字典模组及纯英文分类模组）
        /// 3. 对未知未来模组的 xxx.config.yyy / settings_section_zzz 点分或下划线键名自动剥离前缀并人性化翻译
        /// </summary>
        public static string FormatModSectionLabel(string? rawSection, LoadedMod? mod = null, string? modId = null)
        {
            if (string.IsNullOrWhiteSpace(rawSection))
            {
                return string.Empty;
            }

            string trimmed = rawSection!.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            // 第 1 层：通过官方原生 LoadedMod.Localization 表解析（零反射、零权限）
            LoadedMod? targetMod = mod ?? FindLoadedModById(modId);
            if (targetMod != null && TryResolveFromModLocalization(targetMod, trimmed, out string localizedFromMod))
            {
                return localizedFromMod;
            }

            // 第 2 层：精确命中全生态分类词典
            if (KnownSectionTitleTable.TryGetValue(trimmed, out var pair))
            {
                return BilingualText.Get(pair.Zh, pair.En);
            }

            // 第 3 层：针对 xxx.config.yyy / config.section.zzz / settings_section_xxx 等结构化原始键名的通用兜底净化
            if (TryHumanizeStructuredSectionKey(trimmed, out string humanized))
            {
                return humanized;
            }

            return trimmed;
        }

        private static LoadedMod? FindLoadedModById(string? modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return null;
            try
            {
                var mods = ModLoader.Mods;
                if (mods == null) return null;
                for (int i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    if (m?.Manifest != null && string.Equals(m.Manifest.ID, modId, StringComparison.OrdinalIgnoreCase))
                    {
                        return m;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"FindLoadedModById exception for '{modId}': {ex.Message}");
            }
            return null;
        }

        public static bool TryResolveFromModLocalization(LoadedMod mod, string rawSection, out string resolved)
        {
            resolved = string.Empty;
            try
            {
                var loc = mod.Localization;
                if (loc == null || loc.Count == 0) return false;

                bool wantZh = BilingualText.IsChinese;
                string[] primaryCandidates = wantZh ? ChineseLocaleCandidates : EnglishLocaleCandidates;
                string primaryPrefix = wantZh ? "zh" : "en";

                // 1a. 直接将 rawSection 作为本地化 Key 查询目标语言表（如 "speed.config.dialogue" / "config.section.info"）
                if (TryLookupKeyInLocaleTables(loc, primaryCandidates, primaryPrefix, rawSection, out resolved))
                {
                    return true;
                }

                // 1a-2. 若直查未命中且不以 "section_" 开头，补充尝试查询 "section_" + rawSection（精准覆盖 item_stacking 等模组的分类键）
                if (!rawSection.StartsWith("section_", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryLookupKeyInLocaleTables(loc, primaryCandidates, primaryPrefix, "section_" + rawSection, out resolved))
                    {
                        return true;
                    }
                }

                // 1b. 反向对齐：若模组在 OnLoad 启动期（LocalizationSettings 尚未就绪或语言切换前）将英文/旧语言值写死进了 Section，
                // 在所有语言表中反查 Value == rawSection 的对应 Key，再取出当前目标语言下的译文
                string? matchedKey = null;
                foreach (var tableKvp in loc)
                {
                    var table = tableKvp.Value;
                    if (table == null) continue;
                    foreach (var entryKvp in table)
                    {
                        if (!string.IsNullOrWhiteSpace(entryKvp.Value) &&
                            string.Equals(entryKvp.Value.Trim(), rawSection, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedKey = entryKvp.Key;
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(matchedKey)) break;
                }

                if (!string.IsNullOrEmpty(matchedKey) &&
                    TryLookupKeyInLocaleTables(loc, primaryCandidates, primaryPrefix, matchedKey!, out resolved))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"TryResolveFromModLocalization exception for '{rawSection}': {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 从模组 Localization 表中按当前语言依次查找配置项显示名称：
        /// 依次查找 cfg_<entryKey>、config.<entryKey>、config_<entryKey>、<modId>.<entryKey>、<entryKey>
        /// </summary>
        public static bool TryResolveConfigLabelFromModLocalization(LoadedMod? mod, string? entryKey, out string resolved)
        {
            resolved = string.Empty;
            if (mod == null || string.IsNullOrWhiteSpace(entryKey)) return false;
            try
            {
                var loc = mod.Localization;
                if (loc == null || loc.Count == 0) return false;

                bool wantZh = BilingualText.IsChinese;
                string[] primaryCandidates = wantZh ? ChineseLocaleCandidates : EnglishLocaleCandidates;
                string primaryPrefix = wantZh ? "zh" : "en";
                string modId = mod.Manifest?.ID ?? string.Empty;

                string trimmedKey = entryKey!.Trim();
                var candidateKeys = new List<string>(5)
                {
                    "cfg_" + trimmedKey,
                    "config." + trimmedKey,
                    "config_" + trimmedKey
                };
                if (!string.IsNullOrEmpty(modId))
                {
                    candidateKeys.Add(modId + "." + trimmedKey);
                }
                candidateKeys.Add(trimmedKey);

                for (int i = 0; i < candidateKeys.Count; i++)
                {
                    if (TryLookupKeyInLocaleTables(loc, primaryCandidates, primaryPrefix, candidateKeys[i], out resolved))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"TryResolveConfigLabelFromModLocalization exception for '{entryKey}': {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// 从模组 Localization 表中按当前语言依次查找配置项提示说明：
        /// 依次查找 cfg_<entryKey>.tip、cfg_<entryKey>.tooltip、config.<entryKey>.tip、config.<entryKey>.tooltip、<entryKey>.tip、<entryKey>.tooltip、<entryKey>.desc
        /// </summary>
        public static bool TryResolveConfigTooltipFromModLocalization(LoadedMod? mod, string? entryKey, out string resolved)
        {
            resolved = string.Empty;
            if (mod == null || string.IsNullOrWhiteSpace(entryKey)) return false;
            try
            {
                var loc = mod.Localization;
                if (loc == null || loc.Count == 0) return false;

                bool wantZh = BilingualText.IsChinese;
                string[] primaryCandidates = wantZh ? ChineseLocaleCandidates : EnglishLocaleCandidates;
                string primaryPrefix = wantZh ? "zh" : "en";

                string trimmedKey = entryKey!.Trim();
                var candidateKeys = new string[]
                {
                    "cfg_" + trimmedKey + ".tip",
                    "cfg_" + trimmedKey + ".tooltip",
                    "config." + trimmedKey + ".tip",
                    "config." + trimmedKey + ".tooltip",
                    trimmedKey + ".tip",
                    trimmedKey + ".tooltip",
                    trimmedKey + ".desc"
                };

                for (int i = 0; i < candidateKeys.Length; i++)
                {
                    if (TryLookupKeyInLocaleTables(loc, primaryCandidates, primaryPrefix, candidateKeys[i], out resolved))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"TryResolveConfigTooltipFromModLocalization exception for '{entryKey}': {ex.Message}");
            }
            return false;
        }

        private static bool TryLookupKeyInLocaleTables(
            Dictionary<string, Dictionary<string, string>> loc,
            string[] candidates,
            string localePrefix,
            string key,
            out string value)
        {
            value = string.Empty;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (loc.TryGetValue(candidates[i], out var table) &&
                    table != null &&
                    TryGetNonEmptyIgnoreCase(table, key, out value))
                {
                    return true;
                }
            }

            foreach (var kvp in loc)
            {
                if (kvp.Key != null &&
                    kvp.Key.StartsWith(localePrefix, StringComparison.OrdinalIgnoreCase) &&
                    kvp.Value != null &&
                    TryGetNonEmptyIgnoreCase(kvp.Value, key, out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetNonEmptyIgnoreCase(Dictionary<string, string> table, string key, out string value)
        {
            if (table.TryGetValue(key, out string? exact) && !string.IsNullOrWhiteSpace(exact))
            {
                value = exact.Trim();
                return true;
            }

            foreach (var kvp in table)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    value = kvp.Value.Trim();
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        public static bool TryHumanizeStructuredSectionKey(string trimmed, out string result)
        {
            result = string.Empty;

            // 含有空格或中文字符的已经是自然语言标题，不做标识符拆解
            if (trimmed.IndexOf(' ') >= 0) return false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] > 127) return false;
            }

            string tail = trimmed;
            bool isStructuredKey = false;

            string[] underscorePrefixes = new[]
            {
                "settings_section_",
                "setting_section_",
                "config_section_",
                "cfg_section_",
                "section_"
            };

            // 处理点分路径：如 "speed.config.dialogue", "config.section.info", "mod.settings.audio", "some_mod.settings_section_combat"
            if (trimmed.IndexOf('.') >= 0)
            {
                string[] parts = trimmed.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        string seg = parts[i];
                        if (string.Equals(seg, "config", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(seg, "section", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(seg, "sections", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(seg, "settings", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(seg, "setting", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(seg, "options", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(seg, "option", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(seg, "ui", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(seg, "category", StringComparison.OrdinalIgnoreCase))
                        {
                            isStructuredKey = true;
                            break;
                        }
                    }

                    string lastPart = parts[parts.Length - 1];
                    for (int i = 0; i < underscorePrefixes.Length; i++)
                    {
                        if (lastPart.StartsWith(underscorePrefixes[i], StringComparison.OrdinalIgnoreCase))
                        {
                            isStructuredKey = true;
                            break;
                        }
                    }

                    if (isStructuredKey || parts.Length >= 3)
                    {
                        isStructuredKey = true;
                        tail = lastPart;
                    }
                }
            }

            // 处理下划线前缀：如 "settings_section_general", "config_section_display", "section_audio"
            for (int i = 0; i < underscorePrefixes.Length; i++)
            {
                string pfx = underscorePrefixes[i];
                if (tail.StartsWith(pfx, StringComparison.OrdinalIgnoreCase) && tail.Length > pfx.Length)
                {
                    tail = tail.Substring(pfx.Length);
                    isStructuredKey = true;
                    break;
                }
            }

            if (!isStructuredKey || string.IsNullOrWhiteSpace(tail))
            {
                return false;
            }

            // 剥离出尾部语义词元后，优先再次查询内置词典（例如 tail == "dialogue" -> "对话"）
            if (KnownSectionTitleTable.TryGetValue(tail, out var pair))
            {
                result = BilingualText.Get(pair.Zh, pair.En);
                return true;
            }

            string tailSpaced = tail.Replace('_', ' ').Replace('-', ' ').Trim();
            if (KnownSectionTitleTable.TryGetValue(tailSpaced, out var spacedPair))
            {
                result = BilingualText.Get(spacedPair.Zh, spacedPair.En);
                return true;
            }

            // 将下划线/连字符拆分为首字母大写的整洁词组
            string[] words = tailSpaced.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return false;
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                words[i] = w.Length == 1
                    ? w.ToUpperInvariant()
                    : char.ToUpperInvariant(w[0]) + w.Substring(1);
            }
            result = string.Join(" ", words);
            return true;
        }
    }
}
