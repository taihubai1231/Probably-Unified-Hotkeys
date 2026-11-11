using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    public enum OptionKind
    {
        Toggle,
        Slider,
        Dropdown,
        KeyBind,
        Input
    }

    public enum SearchScope
    {
        All = 0,               // 整体搜索（模组名 + 按键 + 常规设置）
        HotkeysAndGeneral = 1, // 搜索按键和常规设置（不匹配模组名）
        HotkeysOnly = 2,       // 仅搜索按键
        GeneralOnly = 3        // 仅搜索常规设置
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
        public bool IsContentBuilt { get; set; } = false;
        public GameObject? HeaderRowObject { get; set; }
        public GameObject? ContentContainerObject { get; set; }
        public GameObject? OuterSeparatorObject { get; set; }
        public CustomUIElement? FoldArrowElement { get; set; }
        public CustomUIElement? ResetButtonElement { get; set; }

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
            if (ResetButtonElement != null)
            {
                ResetButtonElement.SetVisible(expanded && ResetToDefaultsAction != null);
            }
        }
    }

    /// <summary>
    /// 代表一个快捷键配置项
    /// </summary>
    public class ModHotkeyEntry
    {
        public string Id { get; set; } = string.Empty;
        public string RawOptionId { get; set; } = string.Empty; // 原版 UI 中的元素 ID（如 keybind_rotate_cw）
        public string ModId { get; set; } = string.Empty;
        public string ModDisplayName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;

        public KeyCode DefaultKey { get; set; } = KeyCode.None;
        public KeyModifierMask DefaultModifiers { get; set; } = KeyModifierMask.None;
        public KeyCode SnapshotKey { get; set; } = KeyCode.None;
        public KeyModifierMask SnapshotModifiers { get; set; } = KeyModifierMask.None;
        public bool HasExplicitDefault { get; set; } = false;

        public Func<KeyCode>? GetKey { get; set; }
        public Action<KeyCode>? SetKey { get; set; }

        public KeyCode PendingKey { get; set; } = KeyCode.None;
        public KeyModifierMask PendingModifiers { get; set; } = KeyModifierMask.None;
        public bool HasPendingChange { get; set; } = false;

        // UI 引用
        public GameObject? RowObject { get; set; }
        public CustomUIElement? ButtonElement { get; set; }
        public CustomUIElement? ConflictButtonElement { get; set; }
        public CustomUIElement? LabelElement { get; set; }

        public KeyCode EffectiveKey => HasPendingChange ? PendingKey : (GetKey?.Invoke() ?? KeyCode.None);
        public KeyModifierMask EffectiveModifiers
        {
            get
            {
                KeyCode key = EffectiveKey;
                if (key == KeyCode.None) return KeyModifierMask.None;
                return (HasPendingChange ? PendingModifiers : ComboKeyStore.GetSavedModifiers(Id, key)) & ~ComboKeyStore.GetModifierBit(key);
            }
        }
        public KeyCombo EffectiveCombo => new KeyCombo(EffectiveModifiers, EffectiveKey);

        public KeyCode ResetTargetKey => HasExplicitDefault ? DefaultKey : SnapshotKey;
        public KeyModifierMask ResetTargetModifiers => HasExplicitDefault ? DefaultModifiers : SnapshotModifiers;
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
        public bool SnapshotBool { get; set; }

        // 数值滑块
        public Func<float>? GetFloat { get; set; }
        public Action<float>? SetFloat { get; set; }
        public float? PendingFloat { get; set; }
        public float SnapshotFloat { get; set; }
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 1f;
        public bool WholeNumbers { get; set; } = false;
        public Func<float, string>? ValueFormatter { get; set; }

        // 下拉框
        public Func<int>? GetIndex { get; set; }
        public Action<int>? SetIndex { get; set; }
        public int? PendingIndex { get; set; }
        public int SnapshotIndex { get; set; }
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
                            PendingBool = null;
                        }
                        break;
                    case OptionKind.Slider:
                        if (PendingFloat.HasValue && SetFloat != null)
                        {
                            SetFloat(PendingFloat.Value);
                            PendingFloat = null;
                        }
                        break;
                    case OptionKind.Dropdown:
                        if (PendingIndex.HasValue && SetIndex != null)
                        {
                            SetIndex(PendingIndex.Value);
                            PendingIndex = null;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error applying setting '{Id}': {ex.Message}");
            }
        }

        public void ResetToSnapshot()
        {
            try
            {
                if (ResetToDefault != null)
                {
                    ResetToDefault.Invoke();
                    return;
                }

                switch (Kind)
                {
                    case OptionKind.Toggle:
                        PendingBool = SnapshotBool;
                        SetBool?.Invoke(SnapshotBool);
                        PendingBool = null;
                        break;
                    case OptionKind.Slider:
                        PendingFloat = SnapshotFloat;
                        SetFloat?.Invoke(SnapshotFloat);
                        PendingFloat = null;
                        break;
                    case OptionKind.Dropdown:
                        PendingIndex = SnapshotIndex;
                        SetIndex?.Invoke(SnapshotIndex);
                        PendingIndex = null;
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error resetting setting '{Id}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 代表一组冲突的按键集合
    /// </summary>
    public class ConflictGroup
    {
        public KeyCombo Combo { get; set; }
        public KeyCode Key => Combo.Key;
        public List<ModHotkeyEntry> ConflictingEntries { get; } = new List<ModHotkeyEntry>();
    }
}
