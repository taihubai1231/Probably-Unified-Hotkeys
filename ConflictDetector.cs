using System;
using System.Collections.Generic;
using UnityEngine;

namespace AllowUnbindKey
{
    /// <summary>
    /// 全局按键冲突检测与分析引擎
    /// </summary>
    public static class ConflictDetector
    {
        public static readonly Color ConflictColor = new Color(1.0f, 0.70f, 0.28f, 1.0f); // 淡橙色
        public static readonly Color NormalColor = Color.white;
        public static readonly Color EmptyKeyColor = new Color(0.65f, 0.65f, 0.65f, 1.0f); // 空键淡灰

        /// <summary>
        /// 重新分析所有已收集的按键条目冲突
        /// </summary>
        public static Dictionary<KeyCode, List<ModHotkeyEntry>> Analyze(IEnumerable<ModHotkeyEntry> allEntries)
        {
            var keyMap = new Dictionary<KeyCode, List<ModHotkeyEntry>>();

            foreach (var entry in allEntries)
            {
                KeyCode key = entry.EffectiveKey;
                if (key == KeyCode.None) continue; // KeyCode.None 绝不视为冲突

                if (!keyMap.TryGetValue(key, out var list))
                {
                    list = new List<ModHotkeyEntry>();
                    keyMap[key] = list;
                }
                list.Add(entry);
            }

            // 只保留数量大于 1 的真冲突项
            var conflicts = new Dictionary<KeyCode, List<ModHotkeyEntry>>();
            foreach (var kvp in keyMap)
            {
                if (kvp.Value.Count > 1)
                {
                    conflicts[kvp.Key] = kvp.Value;
                }
            }

            return conflicts;
        }

        /// <summary>
        /// 获取冲突提示文字（用于鼠标悬浮浮窗）
        /// </summary>
        public static string GetConflictTooltip(ModHotkeyEntry currentEntry, Dictionary<KeyCode, List<ModHotkeyEntry>> conflicts)
        {
            KeyCode key = currentEntry.EffectiveKey;
            if (key == KeyCode.None) return string.Empty;

            if (conflicts.TryGetValue(key, out var list) && list.Count > 1)
            {
                var names = new List<string>();
                foreach (var other in list)
                {
                    if (other.Id != currentEntry.Id)
                    {
                        names.Add($"[{other.ModDisplayName}: {other.DisplayName}]");
                    }
                }
                return $"<color=#ffb347>⚠ 按键冲突！与以下功能重叠：</color>\n" + string.Join("\n", names);
            }

            return string.Empty;
        }

        /// <summary>
        /// 获取按严重程度排序的冲突组列表
        /// </summary>
        public static List<ConflictGroup> GetSortedConflictGroups(Dictionary<KeyCode, List<ModHotkeyEntry>> conflicts)
        {
            var result = new List<ConflictGroup>();
            foreach (var kvp in conflicts)
            {
                var group = new ConflictGroup { Key = kvp.Key };
                group.ConflictingEntries.AddRange(kvp.Value);
                result.Add(group);
            }

            // 按冲突项数量降序排列（冲突越多的排在前面）
            result.Sort((a, b) => b.ConflictingEntries.Count.CompareTo(a.ConflictingEntries.Count));
            return result;
        }
    }
}
