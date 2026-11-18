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

    public enum NormalDisplayMode
    {
        All = 0,         // 全部展示
        HotkeysOnly = 1, // 仅展示按键
        GeneralOnly = 2  // 仅展示常规
    }

    public enum DiagnosticFilterMode
    {
        All = 0,               // 全部模组
        ProbedHotkeysOnly = 1, // 仅含只读探测热键
        HasCandidatesOnly = 2, // 仅待确认/疑难模组
        UnmanagedOnly = 3,     // 仅未检测到官方配置
        ManagedOnly = 4,       // 仅已正常纳管
        AssetOnly = 5          // 仅纯资源/无代码
    }

    public enum ModDiagnosticStatus
    {
        Managed = 0,
        HasCandidates = 1,
        UnmanagedCodeMod = 2,
        AssetOnlyMod = 3
    }

    /// <summary>
    /// 代表一个模组的配置与快捷键纯领域聚合组（零 UI 控件依赖）。
    /// </summary>
    public class ModOptionGroup
    {
        public string ModId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PinyinSortKey { get; set; } = string.Empty;
        public bool IsExpanded { get; set; } = false;

        public List<ModHotkeyEntry> Hotkeys { get; } = new List<ModHotkeyEntry>();
        public List<ModHotkeyEntry> ReadOnlyProbedHotkeys { get; } = new List<ModHotkeyEntry>();
        public List<ModGeneralEntry> GeneralSettings { get; } = new List<ModGeneralEntry>();
        public List<string> ExtraNotifyModIds { get; } = new List<string>();

        public Action? ResetToDefaultsAction { get; set; }

        public int TotalItemCount => Hotkeys.Count + ReadOnlyProbedHotkeys.Count + GeneralSettings.Count;
    }

    /// <summary>
    /// 代表一个快捷键配置项纯领域模型（零 UI 句柄引用）。
    /// </summary>
    public class ModHotkeyEntry
    {
        public string Id { get; set; } = string.Empty;
        public string RawOptionId { get; set; } = string.Empty; // 原版 UI 中的元素 ID（如 keybind_rotate_cw）
        public string ModId { get; set; } = string.Empty;
        public string ModDisplayName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string SectionLabel { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;

        public KeyCode DefaultKey { get; set; } = KeyCode.None;
        public KeyModifierMask DefaultModifiers { get; set; } = KeyModifierMask.None;
        public KeyCode SnapshotKey { get; set; } = KeyCode.None;
        public KeyModifierMask SnapshotModifiers { get; set; } = KeyModifierMask.None;
        public bool HasExplicitDefault { get; set; } = false;
        public bool IsReadOnlyProbed { get; set; } = false;
        public string ProbedSourceDetail { get; set; } = string.Empty;

        private bool _disableModifierCombos;
        /// <summary>是否允许在按键前叠加 Ctrl/Shift/Alt 组合键前缀（部分无官方配置的独立模组仅支持单键/侧键；默认开启）。</summary>
        public bool SupportsModifierCombos
        {
            get => !_disableModifierCombos;
            set => _disableModifierCombos = !value;
        }

        public Func<KeyCode>? GetKey { get; set; }
        public Action<KeyCode>? SetKey { get; set; }
        public Func<KeyModifierMask>? GetExternalModifiers { get; set; }
        public Action<KeyModifierMask, KeyCode>? SetCombo { get; set; }

        public KeyCode PendingKey { get; set; } = KeyCode.None;
        public KeyModifierMask PendingModifiers { get; set; } = KeyModifierMask.None;
        public bool HasPendingChange { get; set; } = false;

        /// <summary>
        /// 解耦委托：由持久化层在初始化时注入 ComboKeyStore.GetSavedModifiers，避免 Core 逆向依赖 Persistence。
        /// </summary>
        public static Func<string, KeyCode, KeyModifierMask>? SavedModifierResolver { get; set; }

        public KeyCode EffectiveKey => HasPendingChange ? PendingKey : (GetKey?.Invoke() ?? KeyCode.None);

        public KeyModifierMask EffectiveModifiers
        {
            get
            {
                if (!SupportsModifierCombos) return KeyModifierMask.None;
                KeyCode key = EffectiveKey;
                if (key == KeyCode.None) return KeyModifierMask.None;
                KeyModifierMask mask = HasPendingChange
                    ? PendingModifiers
                    : (GetExternalModifiers != null
                        ? GetExternalModifiers.Invoke()
                        : (SavedModifierResolver?.Invoke(Id, key) ?? KeyModifierMask.None));
                return mask & ~KeyCombo.GetModifierBit(key);
            }
        }

        public KeyCombo EffectiveCombo => new KeyCombo(EffectiveModifiers, EffectiveKey);

        public KeyCode ResetTargetKey => HasExplicitDefault ? DefaultKey : SnapshotKey;
        public KeyModifierMask ResetTargetModifiers => SupportsModifierCombos ? (HasExplicitDefault ? DefaultModifiers : SnapshotModifiers) : KeyModifierMask.None;
    }

    /// <summary>
    /// 代表一个通用常规配置项 (开关 / 滑块 / 下拉框 / 文本框) 纯领域模型。
    /// </summary>
    public class ModGeneralEntry
    {
        public string Id { get; set; } = string.Empty;
        public string ModId { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string SectionLabel { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
        public OptionKind Kind { get; set; } = OptionKind.Toggle;
        public bool IsReadOnlyInfo { get; set; } = false;

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

        // 文本输入框
        public Func<string>? GetString { get; set; }
        public Action<string>? SetString { get; set; }
        public string? PendingString { get; set; }
        public string SnapshotString { get; set; } = string.Empty;

        // 默认值重置
        public Action? ResetToDefault { get; set; }

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
                    case OptionKind.Input:
                        if (PendingString != null && SetString != null && !IsReadOnlyInfo)
                        {
                            SetString(PendingString);
                            PendingString = null;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Error applying setting '{Id}': {ex.Message}", ex);
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
                    case OptionKind.Input:
                        if (!IsReadOnlyInfo)
                        {
                            PendingString = SnapshotString;
                            SetString?.Invoke(SnapshotString);
                            PendingString = null;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Error resetting setting '{Id}': {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// 代表一组冲突的按键集合纯领域模型。
    /// </summary>
    public class ConflictGroup
    {
        public KeyCombo Combo { get; set; }
        public KeyCode Key => Combo.Key;
        public List<ModHotkeyEntry> ConflictingEntries { get; } = new List<ModHotkeyEntry>();
    }

    /// <summary>
    /// 模组排查视图中发现的疑难 / 被跳过 / 用户强转的 ModConfig 候选条目纯领域模型。
    /// </summary>
    public class DiagnosticCandidateEntry
    {
        public string CanonicalId { get; set; } = string.Empty; // "{configModId}.{entryKey}"
        public string ConfigModId { get; set; } = string.Empty;
        public string OwnerModId { get; set; } = string.Empty;
        public string ModDisplayName { get; set; } = string.Empty;
        public string EntryKey { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
        public string ValueTypeName { get; set; } = string.Empty;
        public string CurrentValuePreview { get; set; } = string.Empty;
        public string DefaultValuePreview { get; set; } = string.Empty;
        public bool IsForcedHotkey { get; set; }
        public bool IsCurrentlyHotkey { get; set; }
        public bool IsCurrentlyGeneral { get; set; }
        public bool WasDroppedFromMainUI { get; set; }
        public bool CanPromoteToHotkey { get; set; }
        public bool IsSuspiciousCandidate { get; set; }
        public bool IsIgnoredCandidate { get; set; }
    }

    /// <summary>
    /// 单个已启用模组的扫描诊断快照纯领域模型。
    /// </summary>
    public class ModDiagnosticItem
    {
        public string ModId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ulong WorkshopId { get; set; }
        public bool HasAssembly { get; set; }
        public bool IsWildOrphanConfig { get; set; }
        public ModDiagnosticStatus Status { get; set; }
        public int ManagedHotkeyCount { get; set; }
        public int ManagedGeneralCount { get; set; }
        public int IgnoredCandidateCount { get; set; }
        public List<string> MatchedConfigIds { get; } = new List<string>();
        public bool UsedGameOptionRegistry { get; set; }
        public bool UsedKnownAdapter { get; set; }
        public List<DiagnosticCandidateEntry> CandidateEntries { get; } = new List<DiagnosticCandidateEntry>();
        public List<ModHotkeyEntry> ProbedHotkeys { get; } = new List<ModHotkeyEntry>();
    }

    /// <summary>
    /// 全量模组扫描与排查报告纯领域模型。
    /// </summary>
    public class ModScanReport
    {
        public int EnabledModCount { get; set; }
        public int ManagedModCount { get; set; }
        public int AttentionModCount { get; set; }
        public int HasCandidatesModCount { get; set; }
        public int UnmanagedCodeModCount { get; set; }
        public int AssetOnlyModCount { get; set; }
        public int TotalHotkeysCount { get; set; }
        public int TotalGeneralsCount { get; set; }
        public int ProbedHotkeysCount { get; set; }
        public int OrphanConfigCount { get; set; }
        public List<ModDiagnosticItem> Mods { get; } = new List<ModDiagnosticItem>();
        public List<DiagnosticCandidateEntry> AllCandidates { get; } = new List<DiagnosticCandidateEntry>();
    }
}
