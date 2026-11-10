using System;
using System.Collections.Generic;
using UnityEngine;

namespace AllowUnbindKey
{
    public enum OptionKind
    {
        Toggle,
        Slider,
        Dropdown,
        KeyBind,
        Input
    }

    /// <summary>
    /// 代表一个模组的配置与快捷键聚合组
    /// </summary>
    public class ModOptionGroup
    {
        public string ModId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PinyinSortKey { get; set; } = string.Empty;
        public bool IsExpanded { get; set; } = false;

        public List<ModHotkeyEntry> Hotkeys { get; } = new List<ModHotkeyEntry>();
        public List<ModGeneralEntry> GeneralSettings { get; } = new List<ModGeneralEntry>();

        public Action? ResetToDefaultsAction { get; set; }

        public int TotalItemCount => Hotkeys.Count + GeneralSettings.Count;

        // UI 引用，用于折叠与过滤
        public GameObject? HeaderRowObject { get; set; }
        public GameObject? ContentContainerObject { get; set; }
        public CustomUIElement? FoldArrowElement { get; set; }

        public void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;
            if (ContentContainerObject != null)
            {
                ContentContainerObject.SetActive(expanded);
            }
            if (FoldArrowElement != null)
            {
                FoldArrowElement.SetText(expanded ? "▼" : "▶");
            }
        }
    }

    /// <summary>
    /// 代表一个快捷键配置项
    /// </summary>
    public class ModHotkeyEntry
    {
        public string Id { get; set; } = string.Empty;
        public string ModId { get; set; } = string.Empty;
        public string ModDisplayName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;

        public KeyCode DefaultKey { get; set; } = KeyCode.None;
        public Func<KeyCode>? GetKey { get; set; }
        public Action<KeyCode>? SetKey { get; set; }

        public KeyCode PendingKey { get; set; } = KeyCode.None;
        public bool HasPendingChange { get; set; } = false;

        // UI 引用
        public GameObject? RowObject { get; set; }
        public CustomUIElement? ButtonElement { get; set; }
        public CustomUIElement? LabelElement { get; set; }

        public KeyCode EffectiveKey => HasPendingChange ? PendingKey : (GetKey?.Invoke() ?? KeyCode.None);
    }

    /// <summary>
    /// 代表一个通用常规配置项 (开关 / 滑块 / 下拉框 / 文本框)
    /// </summary>
    public class ModGeneralEntry
    {
        public string Id { get; set; } = string.Empty;
        public string ModId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
        public OptionKind Kind { get; set; } = OptionKind.Toggle;

        // 布尔开关
        public Func<bool>? GetBool { get; set; }
        public Action<bool>? SetBool { get; set; }
        public bool? PendingBool { get; set; }

        // 数值滑块
        public Func<float>? GetFloat { get; set; }
        public Action<float>? SetFloat { get; set; }
        public float? PendingFloat { get; set; }
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 1f;
        public bool WholeNumbers { get; set; } = false;
        public Func<float, string>? ValueFormatter { get; set; }

        // 下拉框
        public Func<int>? GetIndex { get; set; }
        public Action<int>? SetIndex { get; set; }
        public int? PendingIndex { get; set; }
        public Func<IList<string>>? GetChoices { get; set; }

        // 默认值重置
        public Action? ResetToDefault { get; set; }

        // UI 引用
        public GameObject? RowObject { get; set; }
        public CustomUIElement? ControlElement { get; set; }
        public CustomUIElement? ValueLabelElement { get; set; }

        public void ApplyPending()
        {
            try
            {
                switch (Kind)
                {
                    case OptionKind.Toggle:
                        if (PendingBool.HasValue && SetBool != null)
                        {
                            SetBool(PendingBool.Value);
                        }
                        break;
                    case OptionKind.Slider:
                        if (PendingFloat.HasValue && SetFloat != null)
                        {
                            SetFloat(PendingFloat.Value);
                        }
                        break;
                    case OptionKind.Dropdown:
                        if (PendingIndex.HasValue && SetIndex != null)
                        {
                            SetIndex(PendingIndex.Value);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AllowUnbindKey] Error applying setting '{Id}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 代表一组冲突的按键集合
    /// </summary>
    public class ConflictGroup
    {
        public KeyCode Key { get; set; } = KeyCode.None;
        public List<ModHotkeyEntry> ConflictingEntries { get; } = new List<ModHotkeyEntry>();
    }
}
