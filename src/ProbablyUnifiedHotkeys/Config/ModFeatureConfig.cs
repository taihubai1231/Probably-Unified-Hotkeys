using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 模组功能模块化配置中心：
    /// 管理 7 大分区配置项，通过官方标准 ModConfig.For("probably_unified_hotkeys") 持久化。
    /// 严格遵循单向解耦原则，使用事件通知替代直接反向调用上层组件。
    /// </summary>
    public static class ModFeatureConfig
    {
        public const string ModId = "probably_unified_hotkeys";

        // 7 大分区标题常量
        public const string SectionLanguage = "语言设置 / Language";
        public const string SectionOptionsLayout = "选项页面布局 / Options Page Layout";
        public const string SectionTopToolbar = "选项页顶部工具栏 / Options Top Toolbar";
        public const string SectionKeybindRules = "快捷键绑定规则 / Keybinding Rules";
        public const string SectionModsPage = "改造件页面设置 / Mods Page";
        public const string SectionPreloadAndExit = "预载及退出加速 / Preload & Fast Exit";
        public const string SectionDebugLogging = "调试日志 / Debug Logging";

        // 分组 2: 选项页面布局 Keys
        public const string KeyOptionDisplayLocation = "option_display_location";
        public const string KeyPinOwnCardToTop = "pin_own_card_to_top";
        public const string KeyCardSortMode = "mod_card_sort_mode";
        public const string KeyAllowCardDragReorder = "allow_card_drag_reorder";
        public const string KeyShowCardSortDropdown = "show_card_sort_dropdown";
        public const string KeyShowProfilePresetCard = "enable_profile_preset_card";
        public const string KeyEnableUnsavedConfirmDialog = "enable_unsaved_confirm_dialog";

        // 分组 3: 选项页顶部工具栏 Keys
        public const string KeyShowTopSearchBar = "show_top_search_bar";
        public const string KeyShowSearchScopeDropdown = "show_search_scope_dropdown";
        public const string KeyEnableSearchDebounce = "enable_search_debounce";
        public const string KeySearchDebounceMs = "search_debounce_ms";
        public const string KeyShowDisplayFilterDropdown = "show_display_filter_dropdown";
        public const string KeyShowConflictViewButton = "show_conflict_view_button";
        public const string KeyShowModDiagnosticButton = "enable_mod_diagnostic_button";
        public const string KeyShowExpandAllButton = "show_expand_all_button";
        public const string KeyShowTopKeybindHint = "show_top_keybind_hint";

        // 分组 4: 快捷键绑定规则 Keys
        public const string KeyEnableComboKeyBind = "enable_combo_key_bind";
        public const string KeyEnableMouseButtonRebind = "enable_mouse_button_rebind";
        public const string KeyEnableEmptyKeyUnbind = "enable_empty_key_unbind";
        public const string KeyAllowConflictingKeys = "allow_conflicting_keys";

        // 分组 5: 改造件页面设置 Keys
        public const string KeyHideOwnCoverInModsPanel = "hide_own_cover_in_mods_panel";
        public const string KeyEnableModsRowPool = "enable_mods_row_pool";
        public const string KeyEnableModsPreviewCache = "enable_mods_preview_cache";
        public const string KeyEnableConflictScannerCache = "enable_conflict_scanner_cache";

        // 分组 6: 预载及退出加速 Keys
        public const string KeyEnableIdleWarmup = "enable_idle_warmup";
        public const string KeyEnableBackgroundCardPrebuild = "enable_background_card_prebuild";
        public const string KeyEnableFastMainMenu = "enable_fast_main_menu";
        public const string KeyEnableFastQuit = "enable_fast_quit";

        // 分组 7: 调试日志 Keys
        public const string KeyDebugLogging = "enable_debug_logging";

        // 兼容旧版常量定义
        public const string KeyDllProbe = "enable_dll_probe";
        public const string KeyPerformancePatches = "enable_performance_patches";
        public const string KeyPinyinSearch = "enable_pinyin_search";
        public const string KeyKeySuppression = "enable_key_suppression";

        // OptionDisplayLocation 枚举值常量
        public const string DisplayLocationMerged = "merged";
        public const string DisplayLocationStandalone = "standalone";
        public const string DisplayLocationGearOnly = "gear_only";
        private static readonly string[] AllDisplayLocationValues = new[] { DisplayLocationMerged, DisplayLocationStandalone, DisplayLocationGearOnly };

        // ModCardSortMode 枚举值常量
        public const string SortPinyinAsc = "pinyin_asc";
        public const string SortPinyinDesc = "pinyin_desc";
        public const string SortCustom = "custom";
        public const string SortRecentEnabled = "recent_enabled";
        public const string SortHotkeyCount = "hotkey_count";
        public const string SortLoadOrder = "load_order";
        private static readonly string[] AllCardSortModeValues = new[] { SortPinyinAsc, SortPinyinDesc, SortCustom, SortRecentEnabled, SortHotkeyCount, SortLoadOrder };

        // ConfigEntry 缓存引用
        private static ConfigEntry<string>? _entryDisplayLocation;
        private static ConfigEntry<bool>? _entryAllowCardDragReorder;
        private static ConfigEntry<bool>? _entryShowCardSortDropdown;
        private static ConfigEntry<bool>? _entryShowProfilePresetCard;
        private static ConfigEntry<bool>? _entryEnableUnsavedConfirmDialog;

        private static ConfigEntry<bool>? _entryShowTopSearchBar;
        private static ConfigEntry<bool>? _entryShowSearchScopeDropdown;
        private static ConfigEntry<bool>? _entryEnableSearchDebounce;
        private static ConfigEntry<string>? _entrySearchDebounceMs;
        private static ConfigEntry<bool>? _entryShowDisplayFilterDropdown;
        private static ConfigEntry<bool>? _entryShowConflictViewButton;
        private static ConfigEntry<bool>? _entryShowModDiagnosticButton;
        private static ConfigEntry<bool>? _entryShowExpandAllButton;
        private static ConfigEntry<bool>? _entryShowTopKeybindHint;

        private static ConfigEntry<bool>? _entryEnableComboKeyBind;
        private static ConfigEntry<bool>? _entryEnableMouseButtonRebind;
        private static ConfigEntry<bool>? _entryEnableEmptyKeyUnbind;
        private static ConfigEntry<bool>? _entryAllowConflictingKeys;

        private static ConfigEntry<bool>? _entryHideOwnCoverInModsPanel;
        private static ConfigEntry<bool>? _entryEnableModsRowPool;
        private static ConfigEntry<bool>? _entryEnableModsPreviewCache;
        private static ConfigEntry<bool>? _entryEnableConflictScannerCache;

        private static ConfigEntry<bool>? _entryEnableIdleWarmup;
        private static ConfigEntry<bool>? _entryEnableBackgroundCardPrebuild;
        private static ConfigEntry<bool>? _entryEnableFastMainMenu;
        private static ConfigEntry<bool>? _entryEnableFastQuit;

        private static ConfigEntry<bool>? _entryDebugLogging;

        /// <summary>
        /// 任意配置项变动广播事件。
        /// </summary>
        public static event Action? FeatureConfigChanged;

        /// <summary>
        /// 选项页显示位置模式切换事件。
        /// </summary>
        public static event Action? DisplayLocationChanged;

        /// <summary>
        /// 模组卡片排序模式切换事件。
        /// </summary>
        public static event Action? CardSortModeChanged;

        /// <summary>
        /// 改造件隐藏封面开关变动事件。
        /// </summary>
        public static event Action? HideOwnCoverToggled;

        /// <summary>
        /// 性能补丁重置广播事件（兼容保留）。
        /// </summary>
        public static event Action? PerformancePatchesToggled;

#pragma warning disable CS0067
        /// <summary>
        /// 静态探测开关切换事件（兼容保留）。
        /// </summary>
        public static event Action? DllProbeToggled;
#pragma warning restore CS0067

        // 无头单测覆盖钩子
        internal static bool? OverridePinyinSearchForTests { get; set; }
        internal static bool? OverrideKeySuppressionForTests { get; set; }
        internal static OptionDisplayLocation? OverrideDisplayLocationForTests { get; set; }
        internal static ModCardSortMode? OverrideCardSortModeForTests { get; set; }
        internal static bool? OverrideConflictScannerModEnabledForTests { get; set; }
        internal static bool? OverrideAllowConflictingKeysForTests { get; set; }
        internal static bool? OverrideEnableComboKeyBindForTests { get; set; }
        internal static bool? OverrideEnableMouseButtonRebindForTests { get; set; }
        internal static bool? OverrideEnableEmptyKeyUnbindForTests { get; set; }
        internal static bool? OverrideShowTopSearchBarForTests { get; set; }
        internal static bool? OverrideShowDisplayFilterDropdownForTests { get; set; }
        internal static bool? OverrideShowConflictViewButtonForTests { get; set; }
        internal static bool? OverrideShowExpandAllButtonForTests { get; set; }

        static ModFeatureConfig()
        {
            ModLogger.DebugEnabledProvider = () => EnableDebugLogging;
        }

        // =========================================================================
        // 分组 2: 选项页面布局属性与方法
        // =========================================================================

        public static OptionDisplayLocation DisplayLocation
        {
            get
            {
                if (OverrideDisplayLocationForTests.HasValue) return OverrideDisplayLocationForTests.Value;
                return ParseDisplayLocation(_entryDisplayLocation?.Value);
            }
        }

        public static void SetDisplayLocation(OptionDisplayLocation value)
        {
            if (OverrideDisplayLocationForTests.HasValue || PrefsStorage.UseInMemoryPrefsForTests)
            {
                OverrideDisplayLocationForTests = value;
            }
            EnsureRegistered();
            if (!PrefsStorage.UseInMemoryPrefsForTests)
            {
                _entryDisplayLocation?.Set(DisplayLocationToChoiceValue(value));
            }
            else
            {
                DisplayLocationChanged?.Invoke();
                FeatureConfigChanged?.Invoke();
            }
        }

        public static List<string> GetDisplayLocationChoices()
        {
            return new List<string>(3)
            {
                BilingualText.Get("模组界面合并至原版界面", "Merge Mod UI into Vanilla"),
                BilingualText.Get("模组独立界面", "Standalone Mod Window"),
                BilingualText.Get("原版设置界面", "Vanilla Options Only")
            };
        }

        public static int DisplayLocationToIndex(object? raw)
        {
            var loc = ParseDisplayLocation(raw?.ToString());
            return (int)loc;
        }

        public static string IndexToDisplayLocationValue(int idx)
        {
            switch (idx)
            {
                case 1: return DisplayLocationStandalone;
                case 2: return DisplayLocationGearOnly;
                default: return DisplayLocationMerged;
            }
        }

        public static OptionDisplayLocation ParseDisplayLocation(string? raw)
        {
            string s = (raw ?? string.Empty).Trim();
            if (string.Equals(s, DisplayLocationStandalone, StringComparison.OrdinalIgnoreCase) || s == "1")
                return OptionDisplayLocation.StandaloneBottomButton;
            if (string.Equals(s, DisplayLocationGearOnly, StringComparison.OrdinalIgnoreCase) || s == "2")
                return OptionDisplayLocation.GearOnly;
            return OptionDisplayLocation.MergedInOptions;
        }

        public static string DisplayLocationToChoiceValue(OptionDisplayLocation loc)
        {
            switch (loc)
            {
                case OptionDisplayLocation.StandaloneBottomButton: return DisplayLocationStandalone;
                case OptionDisplayLocation.GearOnly: return DisplayLocationGearOnly;
                default: return DisplayLocationMerged;
            }
        }

        public static bool PinOwnCardToTop => false;

        public static ModCardSortMode CardSortMode
        {
            get
            {
                if (OverrideCardSortModeForTests.HasValue) return OverrideCardSortModeForTests.Value;
                return ComboKeyStore.GetSavedCardSortMode();
            }
        }

        public static void SetCardSortMode(ModCardSortMode value)
        {
            if (OverrideCardSortModeForTests.HasValue || PrefsStorage.UseInMemoryPrefsForTests)
            {
                OverrideCardSortModeForTests = value;
            }
            ComboKeyStore.SetSavedCardSortMode(value);
            CardSortModeChanged?.Invoke();
            FeatureConfigChanged?.Invoke();
        }

        public static List<string> GetCardSortModeChoices()
        {
            return new List<string>(6)
            {
                BilingualText.Get("按名称升序", "Name (A-Z)"),
                BilingualText.Get("按名称降序", "Name (Z-A)"),
                BilingualText.Get("自由拖动排列", "Custom Drag Order"),
                BilingualText.Get("最近启用优先", "Recently Enabled"),
                BilingualText.Get("按键数量优先", "Most Hotkeys First"),
                BilingualText.Get("游戏加载顺序", "Game Load Order")
            };
        }

        public static int CardSortModeToIndex(object? raw)
        {
            var mode = ParseCardSortMode(raw?.ToString());
            return (int)mode;
        }

        public static string IndexToCardSortModeValue(int idx)
        {
            switch (idx)
            {
                case 1: return SortPinyinDesc;
                case 2: return SortCustom;
                case 3: return SortRecentEnabled;
                case 4: return SortHotkeyCount;
                case 5: return SortLoadOrder;
                default: return SortPinyinAsc;
            }
        }

        public static ModCardSortMode ParseCardSortMode(string? raw)
        {
            string s = (raw ?? string.Empty).Trim();
            if (string.Equals(s, SortPinyinDesc, StringComparison.OrdinalIgnoreCase) || s == "1")
                return ModCardSortMode.PinyinDesc;
            if (string.Equals(s, SortCustom, StringComparison.OrdinalIgnoreCase) || s == "2")
                return ModCardSortMode.CustomOrder;
            if (string.Equals(s, SortRecentEnabled, StringComparison.OrdinalIgnoreCase) || s == "3")
                return ModCardSortMode.RecentEnabled;
            if (string.Equals(s, SortHotkeyCount, StringComparison.OrdinalIgnoreCase) || s == "4")
                return ModCardSortMode.HotkeyCountDesc;
            if (string.Equals(s, SortLoadOrder, StringComparison.OrdinalIgnoreCase) || s == "5")
                return ModCardSortMode.GameLoadOrder;
            return ModCardSortMode.PinyinAsc;
        }

        public static string CardSortModeToChoiceValue(ModCardSortMode mode)
        {
            switch (mode)
            {
                case ModCardSortMode.PinyinDesc: return SortPinyinDesc;
                case ModCardSortMode.CustomOrder: return SortCustom;
                case ModCardSortMode.RecentEnabled: return SortRecentEnabled;
                case ModCardSortMode.HotkeyCountDesc: return SortHotkeyCount;
                case ModCardSortMode.GameLoadOrder: return SortLoadOrder;
                default: return SortPinyinAsc;
            }
        }

        public static bool AllowCardDragReorder => _entryAllowCardDragReorder?.Value ?? true;
        public static bool ShowCardSortDropdown => _entryShowCardSortDropdown?.Value ?? true;
        public static bool ShowProfilePresetCard => _entryShowProfilePresetCard?.Value ?? true;
        public static bool EnableUnsavedConfirmDialog => _entryEnableUnsavedConfirmDialog?.Value ?? true;

        // =========================================================================
        // 分组 3: 选项页顶部工具栏属性与方法
        // =========================================================================

        public static bool ShowTopSearchBar => OverrideShowTopSearchBarForTests ?? (_entryShowTopSearchBar?.Value ?? true);
        public static bool ShowSearchScopeDropdown => _entryShowSearchScopeDropdown?.Value ?? true;
        public static bool EnableSearchDebounce => _entryEnableSearchDebounce?.Value ?? true;

        public static int SearchDebounceMs
        {
            get
            {
                string? raw = _entrySearchDebounceMs?.Value;
                return ParseAndClampDebounceMs(raw);
            }
        }

        public static int ParseAndClampDebounceMs(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 300;
            if (int.TryParse(raw.Trim(), out int parsed))
            {
                if (parsed < 0) return 0;
                return Math.Max(0, Math.Min(2000, parsed));
            }
            return 300;
        }

        public static void SetSearchDebounceMs(int ms)
        {
            int clamped = Math.Max(0, Math.Min(2000, ms));
            EnsureRegistered();
            if (!PrefsStorage.UseInMemoryPrefsForTests)
            {
                _entrySearchDebounceMs?.Set(clamped.ToString());
            }
        }

        public static bool ShowDisplayFilterDropdown => OverrideShowDisplayFilterDropdownForTests ?? (_entryShowDisplayFilterDropdown?.Value ?? true);
        public static bool ShowConflictViewButton => OverrideShowConflictViewButtonForTests ?? (_entryShowConflictViewButton?.Value ?? true);
        public static bool ShowModDiagnosticButton => _entryShowModDiagnosticButton?.Value ?? true;
        public static bool ShowExpandAllButton => OverrideShowExpandAllButtonForTests ?? (_entryShowExpandAllButton?.Value ?? true);
        public static bool ShowTopKeybindHint => _entryShowTopKeybindHint?.Value ?? true;

        // =========================================================================
        // 分组 4: 快捷键绑定规则属性
        // =========================================================================

        public static bool EnableComboKeyBind => OverrideEnableComboKeyBindForTests ?? (_entryEnableComboKeyBind?.Value ?? true);
        public static bool EnableMouseButtonRebind => OverrideEnableMouseButtonRebindForTests ?? (_entryEnableMouseButtonRebind?.Value ?? true);
        public static bool EnableEmptyKeyUnbind => OverrideEnableEmptyKeyUnbindForTests ?? (_entryEnableEmptyKeyUnbind?.Value ?? true);
        public static bool AllowConflictingKeys => OverrideAllowConflictingKeysForTests ?? (_entryAllowConflictingKeys?.Value ?? true);

        // =========================================================================
        // 分组 5: 改造件页面设置属性
        // =========================================================================

        public static bool HideOwnCoverInModsPanel => _entryHideOwnCoverInModsPanel?.Value ?? true;
        public static bool EnableModsRowPool => _entryEnableModsRowPool?.Value ?? true;
        public static bool EnableModsPreviewCache => _entryEnableModsPreviewCache?.Value ?? true;
        public static bool EnableConflictScannerCache => _entryEnableConflictScannerCache?.Value ?? true;

        public static bool IsConflictScannerModEnabled()
        {
            if (OverrideConflictScannerModEnabledForTests.HasValue)
            {
                return OverrideConflictScannerModEnabledForTests.Value;
            }

            try
            {
                if (ModLoader.IsModEnabled("conflict_scanner")) return true;
                var mods = ModLoader.Mods;
                if (mods != null)
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        var m = mods[i];
                        if (m?.Manifest != null)
                        {
                            if (string.Equals(m.Manifest.ID, "conflict_scanner", StringComparison.OrdinalIgnoreCase) &&
                                (m.IsEnabled || ModLoader.IsModEnabled(m.Manifest.ID)))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略非 Unity 环境异常
            }
            return false;
        }

        // =========================================================================
        // 分组 6: 预载及退出加速属性
        // =========================================================================

        public static bool EnableIdleWarmup => _entryEnableIdleWarmup?.Value ?? true;
        public static bool EnableBackgroundCardPrebuild => _entryEnableBackgroundCardPrebuild?.Value ?? true;
        public static bool EnableFastMainMenu => _entryEnableFastMainMenu?.Value ?? true;
        public static bool EnableFastQuit => _entryEnableFastQuit?.Value ?? true;

        // =========================================================================
        // 分组 7: 调试日志属性
        // =========================================================================

        public static bool EnableDebugLogging => _entryDebugLogging?.Value ?? false;

        // =========================================================================
        // 向后兼容属性（确保现有 59 项单元测试 100% 编译与通过）
        // =========================================================================

        public static bool EnableDllProbe => false;
        public static bool EnablePerformancePatches => EnableModsRowPool && EnableModsPreviewCache && EnableIdleWarmup;
        public static bool EnablePinyinSearch => true;
        public static bool EnableKeySuppression => true;

        public static bool IsPinyinSearchActive => OverridePinyinSearchForTests ?? EnablePinyinSearch;
        public static bool IsKeySuppressionActive => OverrideKeySuppressionForTests ?? EnableKeySuppression;

        public static void ResetAllTestOverrides()
        {
            OverridePinyinSearchForTests = null;
            OverrideKeySuppressionForTests = null;
            OverrideDisplayLocationForTests = null;
            OverrideCardSortModeForTests = null;
            OverrideConflictScannerModEnabledForTests = null;
            OverrideAllowConflictingKeysForTests = null;
            OverrideEnableComboKeyBindForTests = null;
            OverrideEnableMouseButtonRebindForTests = null;
            OverrideEnableEmptyKeyUnbindForTests = null;
            OverrideShowTopSearchBarForTests = null;
            OverrideShowDisplayFilterDropdownForTests = null;
            OverrideShowConflictViewButtonForTests = null;
            OverrideShowExpandAllButtonForTests = null;
        }

        public static void EnsureRegistered()
        {
            try
            {
                ModLogger.DebugEnabledProvider = () => EnableDebugLogging;
                EnsureRegisteredInternal();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"ModFeatureConfig registration warning: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsureRegisteredInternal()
        {
            ModConfig? config = ModConfig.Find(ModId) ?? ModConfig.For(ModId);
            if (config == null)
            {
                return;
            }

            // -----------------------------------------------------------------
            // 分组 2: 选项页面布局 (SectionOptionsLayout)
            // -----------------------------------------------------------------
            _entryDisplayLocation = RegisterStringChoiceEntry(
                config,
                KeyOptionDisplayLocation,
                defaultValue: DisplayLocationMerged,
                SectionOptionsLayout,
                () => BilingualText.Get("选项页面显示位置", "Options Page Location"),
                AllDisplayLocationValues,
                () => BilingualText.Get(
                    "设置模组选项与快捷键界面的打开入口：合并展示、在原版选项页左下角放置独立按钮，或仅通过改造件列表右侧齿轮打开。",
                    "Sets where mod options and hotkeys appear: merged, a bottom-left button in vanilla options, or only via the Mods gear button."),
                onChanged: _ =>
                {
                    DisplayLocationChanged?.Invoke();
                    FeatureConfigChanged?.Invoke();
                }
            );

            _entryAllowCardDragReorder = RegisterBoolEntry(
                config,
                KeyAllowCardDragReorder,
                defaultValue: true,
                SectionOptionsLayout,
                () => BilingualText.Get("允许拖动卡片自由排序", "Allow Drag-to-Reorder Cards"),
                () => BilingualText.Get(
                    "允许按住模组卡片顶部标题栏自由拖动排列顺序，并在卡片标题栏最右侧显示拖动提示符。",
                    "Allows dragging mod card headers to reorder cards, and shows a drag indicator on the right of the card header."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryShowCardSortDropdown = RegisterBoolEntry(
                config,
                KeyShowCardSortDropdown,
                defaultValue: true,
                SectionOptionsLayout,
                () => BilingualText.Get("显示卡片排序下拉框", "Show Card Sort Dropdown"),
                () => BilingualText.Get(
                    "在模组卡片列表顶部分区栏右侧显示快速切换排序方式的下拉框。",
                    "Shows a sort mode dropdown on the right side of the mod cards header."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryShowProfilePresetCard = RegisterBoolEntry(
                config,
                KeyShowProfilePresetCard,
                defaultValue: true,
                SectionOptionsLayout,
                () => BilingualText.Get("显示方案预设卡片", "Show Profile Preset Card"),
                () => BilingualText.Get(
                    "在本模组设置卡片底部显示 3 套快捷键方案切换与剪贴板导入导出面板。",
                    "Shows a 3-slot profile preset switcher and clipboard import/export panel at the bottom of this mod's card."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryEnableUnsavedConfirmDialog = RegisterBoolEntry(
                config,
                KeyEnableUnsavedConfirmDialog,
                defaultValue: true,
                SectionOptionsLayout,
                () => BilingualText.Get("未保存修改退出确认弹窗", "Unsaved Changes Confirmation Dialog"),
                () => BilingualText.Get(
                    "在选项页存在未保存的修改时按下 Esc 或关闭窗口，弹出确认提示，防止误操作丢失改动。",
                    "Prompts a confirmation dialog when closing with unsaved changes to prevent accidental loss."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            // -----------------------------------------------------------------
            // 分组 3: 选项页顶部工具栏 (SectionTopToolbar)
            // -----------------------------------------------------------------
            _entryShowTopSearchBar = RegisterBoolEntry(
                config,
                KeyShowTopSearchBar,
                defaultValue: true,
                SectionTopToolbar,
                () => BilingualText.Get("显示顶部搜索框", "Show Top Search Bar"),
                () => BilingualText.Get(
                    "在选项界面顶部第一行显示搜索输入框。",
                    "Shows the search input bar on the first row of the top toolbar."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryShowSearchScopeDropdown = RegisterBoolEntry(
                config,
                KeyShowSearchScopeDropdown,
                defaultValue: true,
                SectionTopToolbar,
                () => BilingualText.Get("显示搜索范围下拉框", "Show Search Scope Dropdown"),
                () => BilingualText.Get(
                    "在搜索框左侧显示搜索范围选择框（整体搜索 / 仅按键 / 仅常规）。",
                    "Shows the search scope dropdown to the left of the search bar."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryEnableSearchDebounce = RegisterBoolEntry(
                config,
                KeyEnableSearchDebounce,
                defaultValue: true,
                SectionTopToolbar,
                () => BilingualText.Get("打字停顿后再触发搜索", "Debounce Search Input"),
                () => BilingualText.Get(
                    "输入搜索词停顿指定时间后再执行过滤，避免连续打字时频繁刷新列表导致卡顿。",
                    "Waits for typing to pause before filtering to prevent lag during rapid input."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entrySearchDebounceMs = RegisterStringEntry(
                config,
                KeySearchDebounceMs,
                defaultValue: "300",
                SectionTopToolbar,
                () => BilingualText.Get("打字停顿等待毫秒数", "Search Debounce Delay (ms)"),
                () => BilingualText.Get(
                    "设置停止输入后触发搜索的等待时间（毫秒），建议 100~500 毫秒。",
                    "Sets the delay in milliseconds before triggering search after typing stops (recommended: 100-500 ms)."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryShowDisplayFilterDropdown = RegisterBoolEntry(
                config,
                KeyShowDisplayFilterDropdown,
                defaultValue: true,
                SectionTopToolbar,
                () => BilingualText.Get("显示内容筛选下拉框", "Show Display Filter Dropdown"),
                () => BilingualText.Get(
                    "在顶部工具栏第二行显示全部 / 仅按键 / 仅常规筛选下拉框。",
                    "Shows the filter dropdown (All / Hotkeys / General) on the second row."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryShowConflictViewButton = RegisterBoolEntry(
                config,
                KeyShowConflictViewButton,
                defaultValue: true,
                SectionTopToolbar,
                () => BilingualText.Get("显示仅看冲突按钮", "Show Conflict View Button"),
                () => BilingualText.Get(
                    "在顶部工具栏第二行显示「[!] 仅看冲突」快速排查按钮。",
                    "Shows the '[!] Conflicts' button on the second row."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryShowModDiagnosticButton = RegisterBoolEntry(
                config,
                KeyShowModDiagnosticButton,
                defaultValue: true,
                SectionTopToolbar,
                () => BilingualText.Get("显示模组排查按钮", "Show Mod Diagnostics Button"),
                () => BilingualText.Get(
                    "在顶部工具栏第二行显示「模组排查」诊断分类视图按钮。",
                    "Shows the 'Mod Diagnostics' button on the second row."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryShowExpandAllButton = RegisterBoolEntry(
                config,
                KeyShowExpandAllButton,
                defaultValue: true,
                SectionTopToolbar,
                () => BilingualText.Get("显示全部展开与收起按钮", "Show Expand/Collapse All Button"),
                () => BilingualText.Get(
                    "在顶部工具栏第二行显示一键展开或折叠全部模组卡片的按钮。",
                    "Shows the button to expand or collapse all mod cards with one click."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryShowTopKeybindHint = RegisterBoolEntry(
                config,
                KeyShowTopKeybindHint,
                defaultValue: true,
                SectionTopToolbar,
                () => BilingualText.Get("显示顶栏按键提示", "Show Top Keybind Hint"),
                () => BilingualText.Get(
                    "在顶部工具栏最右侧显示快捷操作说明文字。",
                    "Shows quick operation hints on the far right of the top toolbar."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            // -----------------------------------------------------------------
            // 分组 4: 快捷键绑定规则 (SectionKeybindRules)
            // -----------------------------------------------------------------
            _entryEnableComboKeyBind = RegisterBoolEntry(
                config,
                KeyEnableComboKeyBind,
                defaultValue: true,
                SectionKeybindRules,
                () => BilingualText.Get("允许绑定组合键", "Allow Modifier Combos"),
                () => BilingualText.Get(
                    "允许按下 Ctrl、Shift、Alt 叠加按键绑定组合键；关闭后录键仅允许绑定单个按键（单个 Ctrl/Shift/Alt 可作为普通单键录入），已绑定的组合键继续有效。",
                    "Allows Ctrl, Shift, and Alt combos; when disabled, key recording only accepts single keys (Ctrl/Shift/Alt can be bound as standalone keys)."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryEnableMouseButtonRebind = RegisterBoolEntry(
                config,
                KeyEnableMouseButtonRebind,
                defaultValue: true,
                SectionKeybindRules,
                () => BilingualText.Get("允许绑定鼠标键", "Allow Mouse Button Binding"),
                () => BilingualText.Get(
                    "允许将功能直接绑定至鼠标右键、中键及侧键（Mouse1~Mouse6）；关闭后鼠标右键恢复原版取消录键行为。",
                    "Allows binding to right click, middle click, and side buttons (Mouse1-Mouse6); when disabled, right click cancels recording."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryEnableEmptyKeyUnbind = RegisterBoolEntry(
                config,
                KeyEnableEmptyKeyUnbind,
                defaultValue: true,
                SectionKeybindRules,
                () => BilingualText.Get("允许绑定空键", "Allow Unbinding Hotkeys"),
                () => BilingualText.Get(
                    "允许录键时按 Esc 将快捷键清空解绑为「无」；关闭后按 Esc 仅取消本次录入并保留原按键。",
                    "Allows pressing Esc to unbind hotkeys; when disabled, pressing Esc cancels recording and preserves the current key."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryAllowConflictingKeys = RegisterBoolEntry(
                config,
                KeyAllowConflictingKeys,
                defaultValue: true,
                SectionKeybindRules,
                () => BilingualText.Get("允许冲突按键同时生效", "Allow Conflicting Hotkeys"),
                () => BilingualText.Get(
                    "允许多个功能共用同一按键并在按下时同时触发；关闭后同一按键仅首个功能生效，其余重复项在游戏内被抑制。",
                    "Allows multiple actions to share the same key; when disabled, only the first action triggers and duplicate hotkeys are suppressed."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            // -----------------------------------------------------------------
            // 分组 5: 改造件页面设置 (SectionModsPage)
            // -----------------------------------------------------------------
            _entryHideOwnCoverInModsPanel = RegisterBoolEntry(
                config,
                KeyHideOwnCoverInModsPanel,
                defaultValue: true,
                SectionModsPage,
                () => BilingualText.Get("隐藏本模组封面图", "Hide This Mod's Cover Image"),
                () => BilingualText.Get(
                    "在原版改造件面板中查看本模组时隐藏封面预览图，释放空间以便直接通读说明文字。",
                    "Hides this mod's preview cover in the Mods panel to show descriptions without scrolling."),
                onChanged: _ =>
                {
                    HideOwnCoverToggled?.Invoke();
                    FeatureConfigChanged?.Invoke();
                }
            );

            _entryEnableModsRowPool = RegisterBoolEntry(
                config,
                KeyEnableModsRowPool,
                defaultValue: true,
                SectionModsPage,
                () => BilingualText.Get("加快改造件列表加载", "Fast Mods List Loading"),
                () => BilingualText.Get(
                    "通过复用行列表对象与差分筛选，减少打开和搜索改造件面板时的卡顿。",
                    "Reuses mod list row objects and uses diff filtering to reduce stutter in the Mods panel."),
                onChanged: _ =>
                {
                    PerformancePatchesToggled?.Invoke();
                    FeatureConfigChanged?.Invoke();
                }
            );

            _entryEnableModsPreviewCache = RegisterBoolEntry(
                config,
                KeyEnableModsPreviewCache,
                defaultValue: true,
                SectionModsPage,
                () => BilingualText.Get("优先加载本地模组封面图", "Fast Local Mod Preview Loading"),
                () => BilingualText.Get(
                    "跨界面缓存模组封面并优先读取本地图片，减少封面加载等待。",
                    "Caches mod covers and prefers local images to speed up preview loading."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            if (IsConflictScannerModEnabled())
            {
                _entryEnableConflictScannerCache = RegisterBoolEntry(
                    config,
                    KeyEnableConflictScannerCache,
                    defaultValue: true,
                    SectionModsPage,
                    () => BilingualText.Get("跳过冲突检测器重复扫描", "Cache Conflict Scanner Results"),
                    () => BilingualText.Get(
                        "检测到模组环境未发生变动时，跳过创意工坊「模组冲突检测器」的重复扫描与日志打印。",
                        "Reuses cached scan results when mods haven't changed to skip duplicate DLL scanning."),
                    onChanged: _ => FeatureConfigChanged?.Invoke()
                );
            }

            // -----------------------------------------------------------------
            // 分组 6: 预载及退出加速 (SectionPreloadAndExit)
            // -----------------------------------------------------------------
            _entryEnableIdleWarmup = RegisterBoolEntry(
                config,
                KeyEnableIdleWarmup,
                defaultValue: true,
                SectionPreloadAndExit,
                () => BilingualText.Get("在主菜单提前加载设置数据", "Preload Settings Data on Main Menu"),
                () => BilingualText.Get(
                    "利用主菜单闲置时间提前构建拼音词库与设置数据缓存，消除首次打开设置时的停顿。",
                    "Preloads search indexes and settings cache during main menu idle frames."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryEnableBackgroundCardPrebuild = RegisterBoolEntry(
                config,
                KeyEnableBackgroundCardPrebuild,
                defaultValue: true,
                SectionPreloadAndExit,
                () => BilingualText.Get("后台提前生成折叠卡片内容", "Prebuild Collapsed Cards in Background"),
                () => BilingualText.Get(
                    "选项界面打开后在后台分帧静默生成折叠卡片，使展开卡片与搜索时更顺畅。",
                    "Silently constructs collapsed cards across frames to make expanding cards smooth."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryEnableFastMainMenu = RegisterBoolEntry(
                config,
                KeyEnableFastMainMenu,
                defaultValue: true,
                SectionPreloadAndExit,
                () => BilingualText.Get("跳过返回主菜单等待时间", "Skip Delay when Returning to Main Menu"),
                () => BilingualText.Get(
                    "在暂停菜单点击返回主菜单时跳过原版 1 秒的等待停顿，立即切换场景。",
                    "Skips the 1-second pause when returning to the main menu from the pause menu."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            _entryEnableFastQuit = RegisterBoolEntry(
                config,
                KeyEnableFastQuit,
                defaultValue: true,
                SectionPreloadAndExit,
                () => BilingualText.Get("加快退出游戏关闭速度", "Fast Game Exit"),
                () => BilingualText.Get(
                    "退出游戏时保存配置并跳过重复的卸载重编译流程，使游戏立即退出。",
                    "Saves configs and skips redundant unpatch steps during exit to close the game instantly."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );

            // -----------------------------------------------------------------
            // 分组 7: 调试日志 (SectionDebugLogging)
            // -----------------------------------------------------------------
            _entryDebugLogging = RegisterBoolEntry(
                config,
                KeyDebugLogging,
                defaultValue: false,
                SectionDebugLogging,
                () => BilingualText.Get("输出控制台调试日志", "Output Console Debug Logs"),
                () => BilingualText.Get(
                    "输出模组详细适配过程、按键映射与生命周期跟踪日志至控制台，用于排查问题。",
                    "Outputs mod adaptation, key mapping, and lifecycle tracking logs to console."),
                onChanged: _ => FeatureConfigChanged?.Invoke()
            );
        }

        private static readonly HashSet<object> BoundEntries = new HashSet<object>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ConfigEntry<bool> RegisterBoolEntry(
            ModConfig config,
            string key,
            bool defaultValue,
            string section,
            Func<string> nameFunc,
            Func<string> descFunc,
            Action<ConfigEntry<bool>>? onChanged)
        {
            if (config.Get(key) is ConfigEntry<bool> existing)
            {
                if (onChanged != null && BoundEntries.Add(existing))
                {
                    existing.OnChanged += _ => onChanged(existing);
                }
                return existing;
            }

            ConfigEntry<bool> created = config.Define(
                key,
                defaultValue,
                section,
                nameFunc,
                descFunc,
                requiresRestart: false
            );

            if (onChanged != null && BoundEntries.Add(created))
            {
                created.OnChanged += _ => onChanged(created);
            }

            return created;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ConfigEntry<string> RegisterStringChoiceEntry(
            ModConfig config,
            string key,
            string defaultValue,
            string section,
            Func<string> nameFunc,
            string[] choices,
            Func<string> descFunc,
            Action<ConfigEntry<string>>? onChanged)
        {
            if (config.Get(key) is ConfigEntry<string> existing)
            {
                if (onChanged != null && BoundEntries.Add(existing))
                {
                    existing.OnChanged += _ => onChanged(existing);
                }
                return existing;
            }

            ConfigEntry<string> created = config.Define(
                key,
                defaultValue,
                section,
                nameFunc,
                choices,
                descFunc,
                requiresRestart: false
            );

            if (onChanged != null && BoundEntries.Add(created))
            {
                created.OnChanged += _ => onChanged(created);
            }

            return created;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ConfigEntry<string> RegisterStringEntry(
            ModConfig config,
            string key,
            string defaultValue,
            string section,
            Func<string> nameFunc,
            Func<string> descFunc,
            Action<ConfigEntry<string>>? onChanged)
        {
            if (config.Get(key) is ConfigEntry<string> existing)
            {
                if (onChanged != null && BoundEntries.Add(existing))
                {
                    existing.OnChanged += _ => onChanged(existing);
                }
                return existing;
            }

            // 注册为普通 string 配置项，双端均以文本输入框渲染
            ConfigEntry<string> created = config.Define(
                key,
                defaultValue,
                section,
                nameFunc,
                Array.Empty<string>(),
                descFunc,
                requiresRestart: false
            );

            if (onChanged != null && BoundEntries.Add(created))
            {
                created.OnChanged += _ => onChanged(created);
            }

            return created;
        }
    }
}
