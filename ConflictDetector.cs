using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 全局按键冲突检测与分析引擎（完整支持 Shift / Ctrl / Alt 组合键与鼠标侧键精确比对）
    /// </summary>
    public static class ConflictDetector
    {
        public static readonly Color ConflictColor = new Color(1.0f, 0.70f, 0.28f, 1.0f); // 淡橙色 (#ffb347)
        public static readonly Color NormalColor = Color.white;
        public static readonly Color EmptyKeyColor = new Color(0.60f, 0.64f, 0.70f, 1.0f); // 空键统一柔和灰

        /// <summary>
        /// 重新分析所有已收集的按键条目冲突（仅当修饰键掩码与主键均相同且非空时判定为冲突）
        /// </summary>
        public static Dictionary<KeyCombo, List<ModHotkeyEntry>> Analyze(IEnumerable<ModHotkeyEntry> allEntries)
        {
            var keyMap = new Dictionary<KeyCombo, List<ModHotkeyEntry>>();

            foreach (var entry in allEntries)
            {
                var combo = entry.EffectiveCombo;
                if (combo.IsEmpty) continue; // KeyCode.None 绝不视为冲突

                if (!keyMap.TryGetValue(combo, out var list))
                {
                    list = new List<ModHotkeyEntry>();
                    keyMap[combo] = list;
                }
                list.Add(entry);
            }

            var conflicts = new Dictionary<KeyCombo, List<ModHotkeyEntry>>();
            foreach (var kvp in keyMap)
            {
                if (kvp.Value.Count > 1 && HasAnyEditableBinding(kvp.Value))
                {
                    conflicts[kvp.Key] = kvp.Value;
                }
            }

            return conflicts;
        }

        private static bool HasAnyEditableBinding(List<ModHotkeyEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].IsReadOnlyProbed) return true;
            }
            return false;
        }

        /// <summary>
        /// 获取冲突提示文字（用于鼠标悬浮浮窗）
        /// </summary>
        public static string GetConflictTooltip(ModHotkeyEntry currentEntry, Dictionary<KeyCombo, List<ModHotkeyEntry>> conflicts)
        {
            var combo = currentEntry.EffectiveCombo;
            if (combo.IsEmpty) return string.Empty;

            if (conflicts.TryGetValue(combo, out var list) && list.Count > 1)
            {
                var names = new List<string>();
                foreach (var other in list)
                {
                    if (other.Id != currentEntry.Id)
                    {
                        string suffix = other.IsReadOnlyProbed
                            ? I18n.Get(" (只读探测)", " (Probed · Read-Only)")
                            : string.Empty;
                        names.Add($"- [{other.ModDisplayName}] {other.DisplayName}{suffix}");
                    }
                }
                return I18n.Format(
                    "<color=#ffb347>[!] 按键冲突提醒（[{0}] 与以下功能重叠）：</color>\n",
                    "<color=#ffb347>[!] Keybind Conflict ([{0}] also used by):</color>\n",
                    combo
                ) + string.Join("\n", names);
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取按严重程度排序的冲突组列表
        /// </summary>
        public static List<ConflictGroup> GetSortedConflictGroups(Dictionary<KeyCombo, List<ModHotkeyEntry>> conflicts)
        {
            var result = new List<ConflictGroup>();
            foreach (var kvp in conflicts)
            {
                var group = new ConflictGroup { Combo = kvp.Key };
                group.ConflictingEntries.AddRange(kvp.Value);
                result.Add(group);
            }

            result.Sort((a, b) => b.ConflictingEntries.Count.CompareTo(a.ConflictingEntries.Count));
            return result;
        }
    }
}
