#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 统一可折叠配置卡片接口（供原版官方区块与创意工坊模组卡片共用）。
    /// </summary>
    public interface ICollapsibleOptionCard
    {
        bool IsExpanded { get; }
        Action? ResetToDefaultsAction { get; set; }
        GameObject? HeaderRowObject { get; }
        GameObject? ContentContainerObject { get; }
        GameObject? OuterSeparatorObject { get; }
        CustomUIElement? FoldArrowElement { get; }
        CustomUIElement? ResetButtonElement { get; set; }
        void SetExpanded(bool expanded);
    }

    /// <summary>
    /// 原版官方设置分组内的单条原生配置项引用（供搜索过滤与联动色彩刷新）。
    /// </summary>
    public sealed class VanillaOptionItem
    {
        public GameOptionRegistry.Entry Entry { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsKeyBind { get; set; }
        public ModHotkeyEntry? LinkedHotkey { get; set; }
        public List<GameObject> RowObjects { get; } = new List<GameObject>();
    }

    /// <summary>
    /// 代表一个可折叠的原版官方设置区块卡片。
    /// </summary>
    public sealed class VanillaSectionCard : ICollapsibleOptionCard
    {
        public string SectionId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public GameOptionRegistry.Section Section { get; set; } = null!;
        public bool IsExpanded { get; set; } = true;
        public int HotkeyCount { get; set; }
        public int GeneralCount { get; set; }

        public List<VanillaOptionItem> Entries { get; } = new List<VanillaOptionItem>();

        public Action? ResetToDefaultsAction { get; set; }

        public GameObject? HeaderRowObject { get; set; }
        public GameObject? ContentContainerObject { get; set; }
        public GameObject? OuterSeparatorObject { get; set; }
        public CustomUIElement? FoldArrowElement { get; set; }
        public CustomUIElement? ResetButtonElement { get; set; }

        public void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;
            if (ContentContainerObject != null && ContentContainerObject.activeSelf != expanded)
            {
                ContentContainerObject.SetActive(expanded);
            }
            if (FoldArrowElement != null)
            {
                string arrow = expanded ? "▼" : "▶";
                if (FoldArrowElement.text == null || FoldArrowElement.text.text != arrow)
                {
                    FoldArrowElement.SetText(arrow);
                }
            }
            if (ResetButtonElement != null)
            {
                bool shouldShowReset = expanded && ResetToDefaultsAction != null;
                if (ResetButtonElement.root == null || ResetButtonElement.root.activeSelf != shouldShowReset)
                {
                    ResetButtonElement.SetVisible(shouldShowReset);
                }
                if (shouldShowReset)
                {
                    UiLayoutAndFontHelper.SyncCompactActionButton(ResetButtonElement);
                }
            }
        }
    }

    /// <summary>
    /// 卡片内部分区头部及分隔线引用（供搜索过滤动态显示/隐藏）。
    /// </summary>
    internal sealed class GeneralSectionBlock
    {
        public GameObject? SeparatorObj;
        public GameObject? HeaderObj;
        public readonly List<ModGeneralEntry> Entries = new List<ModGeneralEntry>();
    }

    /// <summary>
    /// 模组卡片 UI 引用（从纯业务领域模型 ModOptionGroup 剥离）。
    /// </summary>
    internal sealed class ModCardUiParts
    {
        public bool IsContentBuilt { get; set; } = false;
        public GameObject? HeaderRowObject { get; set; }
        public GameObject? ContentContainerObject { get; set; }
        public GameObject? OuterSeparatorObject { get; set; }
        public CustomUIElement? FoldArrowElement { get; set; }
        public CustomUIElement? ResetButtonElement { get; set; }

        public GameObject? HkHeader;
        public GameObject? ProbedHeader;
        public GameObject? ProbedSep;
        public readonly List<GeneralSectionBlock> GeneralSections = new List<GeneralSectionBlock>();

        public void SetGroupUiExpanded(ModOptionGroup group, bool expanded)
        {
            group.IsExpanded = expanded;
            if (ContentContainerObject != null && ContentContainerObject.activeSelf != expanded)
            {
                ContentContainerObject.SetActive(expanded);
            }
            if (FoldArrowElement != null)
            {
                string arrow = expanded ? "▼" : "▶";
                if (FoldArrowElement.text == null || FoldArrowElement.text.text != arrow)
                {
                    FoldArrowElement.SetText(arrow);
                }
            }
            if (ResetButtonElement != null)
            {
                bool shouldShowReset = expanded && group.ResetToDefaultsAction != null;
                if (ResetButtonElement.root == null || ResetButtonElement.root.activeSelf != shouldShowReset)
                {
                    ResetButtonElement.SetVisible(shouldShowReset);
                }
                if (shouldShowReset)
                {
                    UiLayoutAndFontHelper.SyncCompactActionButton(ResetButtonElement);
                }
            }
        }
    }

    /// <summary>
    /// 快捷键单行控件引用（从纯业务领域模型 ModHotkeyEntry 剥离）。
    /// </summary>
    internal sealed class HotkeyRowUiRefs
    {
        public GameObject? RowObject { get; set; }
        public CustomUIElement? ButtonElement { get; set; }
        public CustomUIElement? ConflictButtonElement { get; set; }
        public CustomUIElement? LabelElement { get; set; }
    }

    /// <summary>
    /// 常规设置项单行控件引用（从纯业务领域模型 ModGeneralEntry 剥离）。
    /// </summary>
    internal sealed class GeneralRowUiRefs
    {
        public GameObject? RowObject { get; set; }
        public CustomUIElement? ControlElement { get; set; }
        public CustomUIElement? ValueLabelElement { get; set; }
    }

    /// <summary>
    /// 统一按键与模组设置中心 UI 会话状态机与运行时句柄集中管理仓。
    /// 彻底承接从 Core/Model 剥离的全部 UI 句柄，消除跨会话内存泄露。
    /// </summary>
    public static class UiSessionState
    {
        // =====================================================================
        // 1. 视图绑定模型字典映射（与 Core/Model 彻底解耦）
        // =====================================================================

        internal static readonly Dictionary<ModOptionGroup, ModCardUiParts> CardParts =
            new Dictionary<ModOptionGroup, ModCardUiParts>();

        internal static readonly Dictionary<ModHotkeyEntry, HotkeyRowUiRefs> HotkeyUiMap =
            new Dictionary<ModHotkeyEntry, HotkeyRowUiRefs>();

        internal static readonly Dictionary<ModGeneralEntry, GeneralRowUiRefs> GeneralUiMap =
            new Dictionary<ModGeneralEntry, GeneralRowUiRefs>();

        internal static ModCardUiParts GetOrCreateCardParts(ModOptionGroup group)
        {
            if (!CardParts.TryGetValue(group, out var parts))
            {
                parts = new ModCardUiParts();
                CardParts[group] = parts;
            }
            return parts;
        }

        internal static bool TryGetCardParts(ModOptionGroup group, out ModCardUiParts parts)
        {
            return CardParts.TryGetValue(group, out parts!);
        }

        internal static HotkeyRowUiRefs GetOrCreateHotkeyUi(ModHotkeyEntry hk)
        {
            if (!HotkeyUiMap.TryGetValue(hk, out var ui))
            {
                ui = new HotkeyRowUiRefs();
                HotkeyUiMap[hk] = ui;
            }
            return ui;
        }

        internal static bool TryGetHotkeyUi(ModHotkeyEntry hk, out HotkeyRowUiRefs ui)
        {
            return HotkeyUiMap.TryGetValue(hk, out ui!);
        }

        internal static GeneralRowUiRefs GetOrCreateGeneralUi(ModGeneralEntry gen)
        {
            if (!GeneralUiMap.TryGetValue(gen, out var ui))
            {
                ui = new GeneralRowUiRefs();
                GeneralUiMap[gen] = ui;
            }
            return ui;
        }

        internal static bool TryGetGeneralUi(ModGeneralEntry gen, out GeneralRowUiRefs ui)
        {
            return GeneralUiMap.TryGetValue(gen, out ui!);
        }

        /// <summary>
        /// 当缓存重置时清空所有模型绑定的 UI 句柄，避免陈旧 GameObject 残留。
        /// </summary>
        internal static void ClearModelUiBindings()
        {
            CardParts.Clear();
            HotkeyUiMap.Clear();
            GeneralUiMap.Clear();
        }

        static UiSessionState()
        {
            ModCatalogCache.SessionResetRequested += ClearModelUiBindings;
            ModFeatureConfig.FeatureConfigChanged += OnFeatureConfigChanged;
            ComboKeyStore.ExternalBypassPredicate = () => IsEnhancedWindowOpen || IsCapturingAnyKey || IsBuildingOrOpeningUI || IsApplyingSettings;
        }

        private static void OnFeatureConfigChanged()
        {
            if (CurrentWindow != null && CurrentWindow.IsAlive && CurrentWindow.IsVisible)
            {
                OnFeatureConfigChangedCallback?.Invoke();
            }
        }

        // =====================================================================
        // 2. 原版官方配置出厂默认值表
        // =====================================================================

        internal static readonly Dictionary<string, bool> VanillaDefaultBools = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["auto_call_next"] = false,
            ["data_consent"] = false,
            ["fps_cap_background"] = false,
            ["instant_dismiss"] = true,
            ["instant_security"] = false,
            ["instant_call_next"] = true,
            ["disable_sort_menu"] = false,
            ["sort_auto_rotate"] = false,
            ["single_click_interact"] = false,
            ["single_click_activate"] = false,
            ["single_click_choice"] = true,
            ["auto_open_negotiation"] = true,
            ["move_bought_to_inventory"] = true,
            ["newspaper_to_trash"] = false,
            ["disable_pop_culture_clients"] = false,
            ["show_mod_source_tooltip"] = true,
            ["show_mod_source_recorder"] = true,
            ["enable_fps_cap"] = true,
            ["restore_display_after_focus"] = true,
            ["typewriter_effect"] = true,
            ["auto_advance_dialogue"] = true
        };

        internal static readonly Dictionary<string, float> VanillaDefaultFloats = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["save_history_days"] = 3f,
            ["master_volume"] = 0.5f,
            ["music_volume"] = 0.5f,
            ["background_volume"] = 0.5f,
            ["dialogue_volume"] = 0.5f,
            ["dialogue_speed"] = 0.5f,
            ["dialogue_bubble_scale"] = 10f
        };

        // =====================================================================
        // 3. 全量窗口会话状态与句柄
        // =====================================================================

        internal static GameOptionUIManager? CurrentManager;
        internal static CustomUIWindow? CurrentWindow;
        internal static CustomUIWindow? UnsavedConfirmWindow;

        internal static List<VanillaSectionCard> VanillaCards = new List<VanillaSectionCard>();
        internal static List<ModOptionGroup> ModGroups = new List<ModOptionGroup>();
        internal static List<ModHotkeyEntry> AllHotkeys = new List<ModHotkeyEntry>();
        internal static Dictionary<string, ModHotkeyEntry> VanillaHotkeyByRawId =
            new Dictionary<string, ModHotkeyEntry>(StringComparer.OrdinalIgnoreCase);
        internal static Dictionary<KeyCombo, List<ModHotkeyEntry>> Conflicts =
            new Dictionary<KeyCombo, List<ModHotkeyEntry>>();

        internal static readonly Dictionary<string, bool> VanillaExpandedStates =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        internal static ModScanReport ScanReport = new ModScanReport();
        internal static string SearchQuery = string.Empty;
        internal static SearchScope SearchScope = SearchScope.All;
        internal static NormalDisplayMode NormalDisplayMode = NormalDisplayMode.All;
        internal static DiagnosticFilterMode DiagnosticFilterMode = DiagnosticFilterMode.All;
        public static bool IsConflictViewActive { get; internal set; } = false;
        public static bool IsDiagnosticViewActive { get; internal set; } = false;
        internal static bool AllExpanded = false;
        internal static bool WasSearchNonEmpty = false;
        internal static bool ConflictViewDirty = true;
        internal static bool DiagnosticViewDirty = true;
        internal static string? DiagnosticStatusMessage = null;
        internal static readonly HashSet<string> ExpandedDiagnosticModIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static readonly Dictionary<ModDiagnosticStatus, bool> DiagnosticSectionExpanded =
            new Dictionary<ModDiagnosticStatus, bool>
            {
                { ModDiagnosticStatus.HasCandidates, true },
                { ModDiagnosticStatus.UnmanagedCodeMod, false },
                { ModDiagnosticStatus.Managed, false },
                { ModDiagnosticStatus.AssetOnlyMod, false }
            };

        internal static readonly List<(ModDiagnosticStatus Status, GameObject BodyObj, CustomUIElement FoldBtn)> DiagnosticSectionViews =
            new List<(ModDiagnosticStatus, GameObject, CustomUIElement)>(4);

        // =====================================================================
        // 4. 按键捕获状态机与 Esc 吸收状态
        // =====================================================================

        internal static ModHotkeyEntry? CapturingEntry;
        internal static int CapturingStartedFrame = -1;
        internal static int RestoreCloseOnEscapeFrame = -1;
        internal static bool CloseOnEscapeBeforeCapture = true;
        internal static bool WaitingMouseRelease = false;
        internal static KeyCode PendingStandaloneModifier = KeyCode.None;
        internal static bool HadMultipleModifiers = false;
        internal static ModHotkeyEntry? LastEndedCaptureEntry;
        internal static bool SuppressSameEntryRestart = false;
        internal static int SuppressRestartReleaseFrame = -1;
        internal static bool IsApplyingSettings = false;
        internal static bool IsBuildingOrOpeningUI = false;
        internal static int SuppressEscapeUntilFrame = -1;
        internal static bool IsExplicitWindowClosing = false;
        internal static bool InEnhancedRefreshRules = false;
        internal static bool HasUserInteractedInSession = false;

        public static bool IsCapturingModHotkey => CapturingEntry != null;
        public static bool IsEnhancedWindowOpen => CurrentWindow != null;
        public static bool IsCapturingAnyKey => CapturingEntry != null || KeyBindHelper.IsCapturing;
        public static bool IsSuppressingEscapeAfterCapture => SuppressEscapeUntilFrame >= 0 && SafeGetCurrentFrame() <= SuppressEscapeUntilFrame;

        internal static Func<int>? FrameCountProviderForTest { get; set; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int GetUnityFrameCount() => Time.frameCount;

        internal static int SafeGetCurrentFrame()
        {
            if (FrameCountProviderForTest != null)
            {
                return FrameCountProviderForTest.Invoke();
            }
            try
            {
                return GetUnityFrameCount();
            }
            catch
            {
                return 0;
            }
        }

        // =====================================================================
        // 5. 核心容器与顶栏控件句柄
        // =====================================================================

        internal static ScrollRect? ScrollRect;
        internal static GameObject? PendingScrollTarget;
        internal static int PendingScrollMinFrame = -1;
        internal static int PendingScrollRemainingPasses = 0;
        internal static GameObject? NormalContentRoot;
        internal static GameObject? ModCardsContainer;
        internal static GameObject? ConflictContentRoot;
        internal static GameObject? DiagnosticStickyHeaderRoot;
        internal static GameObject? DiagnosticContentRoot;
        internal static GameObject? VanillaHeaderRow;
        internal static GameObject? VanillaHeaderSep;
        internal static GameObject? ModHeaderSpace;
        internal static GameObject? ModHeaderSep;
        internal static GameObject? ModHeaderRow;
        internal static GameObject? ModHeaderBottomSep;
        internal static GameObject? EmptySearchLabelRow;
        internal static CustomUIElement? ConflictToggleBtn;
        internal static CustomUIElement? ExpandToggleBtn;
        internal static CustomUIElement? DiagnosticToggleBtn;
        internal static CustomUIElement? NormalDisplayDropdown;
        internal static CustomUIElement? DiagnosticFilterDropdown;
        internal static TMP_InputField? SearchInputField;
        internal static CustomUIElement? ProfileSlotCycleBtn;
        internal static CustomUIElement? ProfileSaveBtn;
        internal static CustomUIElement? ProfileLoadBtn;
        internal static CustomUIElement? ProfileExportBtn;
        internal static CustomUIElement? ProfileImportBtn;
        internal static CustomUIElement? ProfileActionStatusLabel;
        internal static readonly Dictionary<string, bool> VanillaOpenToggleBaseline =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, float> VanillaOpenSliderBaseline =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, int> VanillaOpenDropdownBaseline =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal static GameObject? SelfProfileSectionContainer;
        internal static readonly CustomUIElement?[] SelfProfileSlotDetailLabels =
            new CustomUIElement?[HotkeyProfileManager.SlotCount];

        internal static Dictionary<string, CustomUIElement>? CurrentWindowElements;
        internal static int OpenedFrameCount = -1;
        internal static int PrebuildModIndex = 0;
        internal static string? LastConflictSignature = null;
        internal static bool SuppressRuleConflictRefresh = false;

        internal static readonly KeyCode[] BindableKeyboardKeys = BuildBindableKeyboardKeys();

        private static KeyCode[] BuildBindableKeyboardKeys()
        {
            var raw = (KeyCode[])Enum.GetValues(typeof(KeyCode));
            var list = new List<KeyCode>(128);
            var seen = new HashSet<int>();
            for (int i = 0; i < raw.Length; i++)
            {
                KeyCode kc = raw[i];
                if (kc == KeyCode.None || kc == KeyCode.Escape) continue;
                if (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6) continue;
                if ((int)kc >= (int)KeyCode.JoystickButton0) continue;
                if (ComboKeyStore.IsModifierKey(kc)) continue;
                if (kc == KeyCode.LeftWindows || kc == KeyCode.RightWindows ||
                    kc == KeyCode.LeftCommand || kc == KeyCode.RightCommand ||
                    kc == KeyCode.Menu || kc == KeyCode.SysReq || kc == KeyCode.Print) continue;
                if (seen.Add((int)kc))
                {
                    list.Add(kc);
                }
            }
            return list.ToArray();
        }

        // =====================================================================
        // 6. 原版 GameOptionUIManager 强类型委托与反射字段缓存
        // =====================================================================

        private static T? CreateMethodDelegate<T>(MethodInfo? method) where T : Delegate
        {
            if (method == null) return null;
            try
            {
                return (T)Delegate.CreateDelegate(typeof(T), method);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to create delegate for {method.Name}: {ex.Message}");
                return null;
            }
        }

        internal static readonly Action<GameOptionUIManager, CustomUIBuilder, GameOptionRegistry.Entry>? BuildEntryDelegate =
            CreateMethodDelegate<Action<GameOptionUIManager, CustomUIBuilder, GameOptionRegistry.Entry>>(
                AccessTools.Method(typeof(GameOptionUIManager), "BuildEntry"));

        internal static readonly Func<GameOptionRegistry.Section, string>? NoteIdDelegate =
            CreateMethodDelegate<Func<GameOptionRegistry.Section, string>>(
                AccessTools.Method(typeof(GameOptionUIManager), "NoteId"));

        internal static readonly Action<GameOptionUIManager>? OriginalApplySettingDelegate =
            CreateMethodDelegate<Action<GameOptionUIManager>>(
                AccessTools.Method(typeof(GameOptionUIManager), "OnApplySetting"));

        internal static readonly Action<GameOptionUIManager>? RefreshRulesDelegate =
            CreateMethodDelegate<Action<GameOptionUIManager>>(
                AccessTools.Method(typeof(GameOptionUIManager), "RefreshRules"));

        internal static readonly Action<GameOptionUIManager, string>? SetKeyBindStatusDelegate =
            CreateMethodDelegate<Action<GameOptionUIManager, string>>(
                AccessTools.Method(typeof(GameOptionUIManager), "SetKeyBindStatus", new Type[] { typeof(string) }));

        internal static readonly Action<string>? FireModConfigAppliedDelegate =
            CreateMethodDelegate<Action<string>>(
                AccessTools.Method(typeof(ModHook), "FireOnModConfigApplied", new Type[] { typeof(string) }));

        internal static readonly FieldInfo? PendingKeysField = AccessTools.Field(typeof(GameOptionUIManager), "pendingKeys");
        internal static readonly FieldInfo? OverriddenDropdownsField = AccessTools.Field(typeof(GameOptionUIManager), "overriddenDropdowns");
        internal static readonly FieldInfo? WindowField = AccessTools.Field(typeof(GameOptionUIManager), "window");
        internal static readonly FieldInfo? ConsumedEscapeFrameField = AccessTools.Field(typeof(GameOptionUIManager), "consumedEscapeFrame");
        internal static readonly FieldInfo? CustomUIConsumedEscapeFrameField = AccessTools.Field(typeof(CustomUIManager), "consumedEscapeFrame");
        internal static readonly FieldInfo? CustomWindowElementsField = AccessTools.Field(typeof(CustomUIWindow), "elements");

        // 解耦回调（供主窗口生命周期装配注入）
        internal static Action<GameOptionUIManager>? ApplyAllSettingsCallback { get; set; }
        internal static Action? RefreshConflictsCallback { get; set; }
        internal static Action? RebuildConflictViewCallback { get; set; }
        internal static Action? OnFeatureConfigChangedCallback { get; set; }

        public static void MarkEscapeConsumed()
        {
            int frame = SafeGetCurrentFrame();
            ConsumedEscapeFrameField?.SetValue(null, frame);
            CustomUIConsumedEscapeFrameField?.SetValue(null, frame);
        }

        internal static void InvokeRefreshRulesSafe(GameOptionUIManager? manager)
        {
            if (manager == null || RefreshRulesDelegate == null) return;
            InEnhancedRefreshRules = true;
            try
            {
                RefreshRulesDelegate.Invoke(manager);
            }
            finally
            {
                InEnhancedRefreshRules = false;
            }
        }

        internal static bool ShouldSilenceMissingElementWarning(CustomUIWindow? window, string id, out CustomUIElement? element)
        {
            element = null;
            if (!(InEnhancedRefreshRules || IsApplyingSettings) || window == null || window != CurrentWindow || CurrentWindowElements == null || string.IsNullOrEmpty(id))
            {
                return false;
            }
            CurrentWindowElements.TryGetValue(id, out element);
            return true;
        }

        internal static void RemoveWindowElements(Func<string, bool> predicate)
        {
            if (CurrentWindowElements == null || predicate == null) return;
            List<string>? toRemove = null;
            foreach (var kvp in CurrentWindowElements)
            {
                if (predicate(kvp.Key))
                {
                    toRemove ??= new List<string>();
                    toRemove.Add(kvp.Key);
                }
            }
            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                {
                    CurrentWindowElements.Remove(toRemove[i]);
                }
            }
        }

        // =====================================================================
        // 7. 会话重置与清理
        // =====================================================================

        internal static void ClearSessionUiCaches()
        {
            UiLayoutAndFontHelper.ClearButtonCaches();
            CardParts.Clear();
            HotkeyUiMap.Clear();
            GeneralUiMap.Clear();
            DiagnosticSectionViews.Clear();
            ProfileSlotCycleBtn = null;
            ProfileSaveBtn = null;
            ProfileLoadBtn = null;
            ProfileExportBtn = null;
            ProfileImportBtn = null;
            ProfileActionStatusLabel = null;
            SelfProfileSectionContainer = null;
            for (int i = 0; i < SelfProfileSlotDetailLabels.Length; i++)
            {
                SelfProfileSlotDetailLabels[i] = null;
            }
            VanillaOpenToggleBaseline.Clear();
            VanillaOpenSliderBaseline.Clear();
            VanillaOpenDropdownBaseline.Clear();
        }

        internal static void ResetDiagnosticSectionDefaults()
        {
            DiagnosticSectionExpanded[ModDiagnosticStatus.HasCandidates] = true;
            DiagnosticSectionExpanded[ModDiagnosticStatus.UnmanagedCodeMod] = false;
            DiagnosticSectionExpanded[ModDiagnosticStatus.Managed] = false;
            DiagnosticSectionExpanded[ModDiagnosticStatus.AssetOnlyMod] = false;
        }

        internal static void ResetSessionForOpen(GameOptionUIManager manager)
        {
            CurrentManager = manager;
            CurrentWindow = null;
            CurrentWindowElements = null;
            HasUserInteractedInSession = false;
            ComboKeyStore.ExternalBypassPredicate = () => IsEnhancedWindowOpen || IsCapturingAnyKey || IsBuildingOrOpeningUI || IsApplyingSettings;
            IsConflictViewActive = false;
            IsDiagnosticViewActive = false;
            ConflictViewDirty = true;
            DiagnosticViewDirty = true;
            DiagnosticStatusMessage = null;
            ExpandedDiagnosticModIds.Clear();
            ResetDiagnosticSectionDefaults();
            LastConflictSignature = null;
            SuppressRuleConflictRefresh = false;
            SearchQuery = string.Empty;
            NormalDisplayMode = NormalDisplayMode.All;
            DiagnosticFilterMode = DiagnosticFilterMode.All;
            WasSearchNonEmpty = false;
            UiLayoutAndFontHelper.ClearPendingScroll();
            ScrollRect = null;
            NormalContentRoot = null;
            ModCardsContainer = null;
            ConflictContentRoot = null;
            DiagnosticStickyHeaderRoot = null;
            DiagnosticContentRoot = null;
            ConflictToggleBtn = null;
            ExpandToggleBtn = null;
            DiagnosticToggleBtn = null;
            NormalDisplayDropdown = null;
            DiagnosticFilterDropdown = null;
            SearchInputField = null;
            CapturingEntry = null;
            CapturingStartedFrame = -1;
            RestoreCloseOnEscapeFrame = -1;
            WaitingMouseRelease = false;
            PendingStandaloneModifier = KeyCode.None;
            HadMultipleModifiers = false;
            VanillaCards.Clear();
            VanillaHotkeyByRawId.Clear();
            VanillaHeaderRow = null;
            VanillaHeaderSep = null;
            ModHeaderSpace = null;
            ModHeaderSep = null;
            ModHeaderRow = null;
            ModHeaderBottomSep = null;
            EmptySearchLabelRow = null;
            OpenedFrameCount = SafeGetCurrentFrame();
            PrebuildModIndex = 0;
            ClearSessionUiCaches();
            ComboKeyStore.EnsureLoaded();
        }

        internal static void ResetSessionOnClose()
        {
            UnsavedConfirmDialog.CloseUnsavedChangesConfirmDialog();
            IsExplicitWindowClosing = false;
            CurrentWindow = null;
            CurrentWindowElements = null;
            ScrollRect = null;
            NormalContentRoot = null;
            ModCardsContainer = null;
            ConflictContentRoot = null;
            DiagnosticStickyHeaderRoot = null;
            DiagnosticContentRoot = null;
            VanillaHeaderRow = null;
            VanillaHeaderSep = null;
            ModHeaderSpace = null;
            ModHeaderSep = null;
            ModHeaderRow = null;
            ModHeaderBottomSep = null;
            EmptySearchLabelRow = null;
            ConflictToggleBtn = null;
            ExpandToggleBtn = null;
            DiagnosticToggleBtn = null;
            NormalDisplayDropdown = null;
            DiagnosticFilterDropdown = null;
            SearchInputField = null;
            UiLayoutAndFontHelper.ClearPendingScroll();
            CapturingEntry = null;
            RestoreCloseOnEscapeFrame = -1;
            WaitingMouseRelease = false;
            PendingStandaloneModifier = KeyCode.None;
            HadMultipleModifiers = false;
            IsConflictViewActive = false;
            IsDiagnosticViewActive = false;
            VanillaCards.Clear();
            VanillaHotkeyByRawId.Clear();
            OpenedFrameCount = -1;
            PrebuildModIndex = 0;
            HasUserInteractedInSession = false;
            ClearSessionUiCaches();
        }

        // =====================================================================
        // 8. 基准快照、脏状态检测与放弃修改
        // =====================================================================

        public static void CaptureOpenBaselineSnapshot()
        {
            VanillaOpenToggleBaseline.Clear();
            VanillaOpenSliderBaseline.Clear();
            VanillaOpenDropdownBaseline.Clear();

            if (VanillaCards != null && CurrentWindow != null && CurrentWindow.IsAlive)
            {
                for (int c = 0; c < VanillaCards.Count; c++)
                {
                    var card = VanillaCards[c];
                    if (card == null || card.Entries == null) continue;
                    for (int i = 0; i < card.Entries.Count; i++)
                    {
                        var item = card.Entries[i];
                        var entry = item?.Entry;
                        if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                        var el = CurrentWindow.Get(entry.id);
                        if (el == null || !el.IsAlive) continue;

                        switch (entry.kind)
                        {
                            case GameOptionRegistry.Kind.Toggle:
                                VanillaOpenToggleBaseline[entry.id] = el.GetBool();
                                break;
                            case GameOptionRegistry.Kind.Slider:
                                VanillaOpenSliderBaseline[entry.id] = el.GetFloat();
                                break;
                            case GameOptionRegistry.Kind.Dropdown:
                                VanillaOpenDropdownBaseline[entry.id] = el.GetIndex();
                                break;
                        }
                    }
                }
            }

            if (CurrentManager != null)
            {
                (PendingKeysField?.GetValue(CurrentManager) as Dictionary<string, KeyCode>)?.Clear();
            }
        }

        internal static bool HasAnyPendingGeneralChanged()
        {
            if (ModGroups == null) return false;
            for (int g = 0; g < ModGroups.Count; g++)
            {
                var group = ModGroups[g];
                if (group == null) continue;
                for (int i = 0; i < group.GeneralSettings.Count; i++)
                {
                    var gen = group.GeneralSettings[i];
                    if (gen == null) continue;

                    if (gen.PendingBool.HasValue && gen.PendingBool.Value != (gen.GetBool?.Invoke() ?? false))
                    {
                        return true;
                    }
                    if (gen.PendingFloat.HasValue && Mathf.Abs(gen.PendingFloat.Value - (gen.GetFloat?.Invoke() ?? gen.Min)) > 0.0001f)
                    {
                        return true;
                    }
                    if (gen.PendingIndex.HasValue && gen.PendingIndex.Value != (gen.GetIndex?.Invoke() ?? 0))
                    {
                        return true;
                    }
                    if (gen.PendingString != null && !string.Equals(gen.PendingString, gen.GetString?.Invoke() ?? string.Empty, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static bool HasAnyVanillaBaselineChanged()
        {
            if (VanillaCards == null || CurrentWindow == null || !CurrentWindow.IsAlive) return false;
            for (int c = 0; c < VanillaCards.Count; c++)
            {
                var card = VanillaCards[c];
                if (card == null || card.Entries == null) continue;
                for (int i = 0; i < card.Entries.Count; i++)
                {
                    var item = card.Entries[i];
                    var entry = item?.Entry;
                    if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                    var el = CurrentWindow.Get(entry.id);
                    if (el == null || !el.IsAlive) continue;

                    switch (entry.kind)
                    {
                        case GameOptionRegistry.Kind.Toggle:
                            if (entry.getBool != null)
                            {
                                bool curVal = el.GetBool();
                                bool origVal = entry.getBool();
                                bool hasBase = VanillaOpenToggleBaseline.TryGetValue(entry.id, out bool baseVal);
                                if (curVal != origVal && (!hasBase || curVal != baseVal))
                                {
                                    return true;
                                }
                            }
                            break;
                        case GameOptionRegistry.Kind.Slider:
                            if (entry.getFloat != null)
                            {
                                float curVal = el.GetFloat();
                                float origVal = entry.getFloat();
                                bool hasBase = VanillaOpenSliderBaseline.TryGetValue(entry.id, out float baseVal);
                                if (Mathf.Abs(curVal - origVal) > 0.001f && (!hasBase || Mathf.Abs(curVal - baseVal) > 0.001f))
                                {
                                    return true;
                                }
                            }
                            break;
                        case GameOptionRegistry.Kind.Dropdown:
                            if (entry.getIndex != null)
                            {
                                int curVal = el.GetIndex();
                                int origVal = entry.getIndex();
                                bool hasBase = VanillaOpenDropdownBaseline.TryGetValue(entry.id, out int baseVal);
                                if (curVal != origVal && (!hasBase || curVal != baseVal))
                                {
                                    return true;
                                }
                            }
                            break;
                    }
                }
            }
            return false;
        }

        internal static bool HasAnyHotkeyChanged()
        {
            if (AllHotkeys == null) return false;
            Dictionary<string, KeyCode>? pkDict = null;
            if (CurrentManager != null && PendingKeysField != null)
            {
                pkDict = PendingKeysField.GetValue(CurrentManager) as Dictionary<string, KeyCode>;
            }

            for (int i = 0; i < AllHotkeys.Count; i++)
            {
                var hk = AllHotkeys[i];
                if (hk == null || hk.IsReadOnlyProbed) continue;

                KeyCode currentKey = hk.GetKey?.Invoke() ?? KeyCode.None;
                KeyModifierMask currentMods = currentKey == KeyCode.None
                    ? KeyModifierMask.None
                    : (hk.GetExternalModifiers != null
                        ? hk.GetExternalModifiers.Invoke()
                        : ComboKeyStore.GetSavedModifiers(hk.Id, currentKey));

                if (hk.ModId == "vanilla")
                {
                    bool inPendingDict = false;
                    KeyCode pkValue = KeyCode.None;
                    if (pkDict != null && !string.IsNullOrEmpty(hk.RawOptionId))
                    {
                        inPendingDict = pkDict.TryGetValue(hk.RawOptionId, out pkValue);
                    }

                    if (hk.HasPendingChange || inPendingDict)
                    {
                        KeyCode targetKey = hk.HasPendingChange ? hk.PendingKey : pkValue;
                        KeyModifierMask targetMods = hk.HasPendingChange ? hk.PendingModifiers : currentMods;

                        if (targetKey == KeyCode.None && currentKey == KeyCode.None)
                        {
                            continue;
                        }
                        if (targetKey != currentKey || targetMods != currentMods)
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    if (hk.HasPendingChange)
                    {
                        if (hk.PendingKey == KeyCode.None && currentKey == KeyCode.None)
                        {
                            continue;
                        }
                        if (hk.PendingKey != currentKey || hk.PendingModifiers != currentMods)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public static bool HasUnsavedChanges()
        {
            return HasAnyHotkeyChanged() || HasAnyPendingGeneralChanged() || HasAnyVanillaBaselineChanged();
        }

        public static void DiscardAllPendingChanges(Action? onRefreshConflicts = null, Action? onRebuildConflictView = null)
        {
            CancelCapture();
            HasUserInteractedInSession = false;

            // 1. 原版待提交键位清空，恢复至当前生效键位
            var pendingKeys = CurrentManager != null ? PendingKeysField?.GetValue(CurrentManager) as Dictionary<string, KeyCode> : null;
            pendingKeys?.Clear();

            for (int i = 0; i < AllHotkeys.Count; i++)
            {
                var hk = AllHotkeys[i];
                if (hk == null) continue;
                KeyCode currentKey = hk.GetKey?.Invoke() ?? KeyCode.None;
                hk.HasPendingChange = false;
                hk.PendingModifiers = hk.GetExternalModifiers != null
                    ? hk.GetExternalModifiers.Invoke()
                    : ComboKeyStore.GetSavedModifiers(hk.Id, currentKey);
                hk.PendingKey = currentKey;
                UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(hk);
            }

            // 2. 模组常规设置恢复并重置控件
            for (int g = 0; g < ModGroups.Count; g++)
            {
                var group = ModGroups[g];
                if (group == null) continue;
                for (int i = 0; i < group.GeneralSettings.Count; i++)
                {
                    var gen = group.GeneralSettings[i];
                    if (gen != null)
                    {
                        gen.PendingBool = null;
                        gen.PendingFloat = null;
                        gen.PendingIndex = null;
                        gen.PendingString = null;
                    }
                }
                if (TryGetCardParts(group, out var parts) && parts.IsContentBuilt)
                {
                    RefreshCardUI(group);
                }
            }

            // 3. 原版常规设置恢复并重置控件
            if (VanillaCards != null && CurrentWindow != null && CurrentWindow.IsAlive)
            {
                for (int c = 0; c < VanillaCards.Count; c++)
                {
                    var card = VanillaCards[c];
                    if (card == null || card.Entries == null) continue;
                    for (int i = 0; i < card.Entries.Count; i++)
                    {
                        var item = card.Entries[i];
                        var entry = item?.Entry;
                        if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                        var el = CurrentWindow.Get(entry.id);
                        if (el == null || !el.IsAlive) continue;

                        switch (entry.kind)
                        {
                            case GameOptionRegistry.Kind.Toggle:
                                if (entry.getBool != null)
                                {
                                    el.SetBool(entry.getBool(), notify: false);
                                }
                                break;
                            case GameOptionRegistry.Kind.Slider:
                                if (entry.getFloat != null)
                                {
                                    el.SetFloat(entry.getFloat(), notify: false);
                                }
                                break;
                            case GameOptionRegistry.Kind.Dropdown:
                                if (entry.getIndex != null)
                                {
                                    el.SetIndex(entry.getIndex(), notify: false);
                                }
                                break;
                        }
                    }
                }
            }

            var overriddenDropdowns = CurrentManager != null ? OverriddenDropdownsField?.GetValue(CurrentManager) as IDictionary : null;
            overriddenDropdowns?.Clear();

            RefreshConflicts();
            var refreshConflicts = onRefreshConflicts ?? RefreshConflictsCallback;
            refreshConflicts?.Invoke();

            if (IsConflictViewActive)
            {
                var rebuildConflictView = onRebuildConflictView ?? RebuildConflictViewCallback;
                rebuildConflictView?.Invoke();
            }

            CaptureOpenBaselineSnapshot();
        }

        internal static void RefreshCardUI(ModOptionGroup group)
        {
            for (int i = 0; i < group.GeneralSettings.Count; i++)
            {
                var gen = group.GeneralSettings[i];
                if (gen == null) continue;
                gen.PendingBool = null;
                gen.PendingFloat = null;
                gen.PendingIndex = null;
                gen.PendingString = null;

                if (TryGetGeneralUi(gen, out var genUi) && genUi.ControlElement != null)
                {
                    if (gen.Kind == OptionKind.Toggle)
                    {
                        genUi.ControlElement.SetBool(gen.GetBool?.Invoke() ?? false, false);
                    }
                    else if (gen.Kind == OptionKind.Slider)
                    {
                        float val = gen.GetFloat?.Invoke() ?? 0f;
                        genUi.ControlElement.SetFloat(val, false);
                        genUi.ValueLabelElement?.SetText(UiLayoutAndFontHelper.FormatSliderValue(gen, val));
                    }
                    else if (gen.Kind == OptionKind.Dropdown)
                    {
                        genUi.ControlElement.SetIndex(gen.GetIndex?.Invoke() ?? 0, false);
                    }
                    else if (gen.Kind == OptionKind.Input && !gen.IsReadOnlyInfo)
                    {
                        string val = gen.GetString?.Invoke() ?? string.Empty;
                        if (genUi.ControlElement.input != null)
                        {
                            genUi.ControlElement.input.SetTextWithoutNotify(val);
                        }
                        else
                        {
                            genUi.ControlElement.SetString(val, false);
                        }
                    }
                }
            }

            for (int i = 0; i < group.Hotkeys.Count; i++)
            {
                var hotkey = group.Hotkeys[i];
                if (hotkey == null) continue;
                hotkey.HasPendingChange = false;
                hotkey.PendingModifiers = KeyModifierMask.None;
                UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(hotkey);
            }
            for (int i = 0; i < group.ReadOnlyProbedHotkeys.Count; i++)
            {
                var probed = group.ReadOnlyProbedHotkeys[i];
                if (probed == null) continue;
                UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(probed);
            }
        }

        public static void RefreshConflicts()
        {
            Conflicts = ConflictDetector.Analyze(AllHotkeys);
            ConflictViewDirty = true;
        }

        public static void CancelCapture(bool fromMouseClick = false)
        {
            if (CapturingEntry == null) return;
            var entry = CapturingEntry;
            CapturingEntry = null;
            PendingStandaloneModifier = KeyCode.None;
            HadMultipleModifiers = false;
            int frame = SafeGetCurrentFrame();
            SuppressEscapeUntilFrame = frame + 2;
            RestoreCloseOnEscapeFrame = frame + 2;
            MarkEscapeConsumed();

            if (fromMouseClick)
            {
                LastEndedCaptureEntry = entry;
                SuppressSameEntryRestart = true;
                SuppressRestartReleaseFrame = -1;
            }

            using (ComboKeyStore.EnterBypass())
            {
                UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(entry);
                UiLayoutAndFontHelper.StyleVanillaKeyButtons();
            }
            if (CurrentManager != null)
            {
                SetKeyBindStatusDelegate?.Invoke(CurrentManager, string.Empty);
            }
        }

        public static void RefreshBaselineSnapshotsAfterApply(GameOptionUIManager? manager)
        {
            if (manager != null)
            {
                (PendingKeysField?.GetValue(manager) as Dictionary<string, KeyCode>)?.Clear();
            }

            if (AllHotkeys != null)
            {
                for (int i = 0; i < AllHotkeys.Count; i++)
                {
                    var hk = AllHotkeys[i];
                    if (hk == null) continue;

                    hk.HasPendingChange = false;
                    hk.PendingModifiers = KeyModifierMask.None;
                    hk.PendingKey = KeyCode.None;

                    KeyCode activeKey = hk.GetKey != null ? hk.GetKey() : KeyCode.None;
                    KeyModifierMask activeMods = (activeKey == KeyCode.None)
                        ? KeyModifierMask.None
                        : (hk.GetExternalModifiers != null
                            ? hk.GetExternalModifiers()
                            : ComboKeyStore.GetSavedModifiers(hk.Id, activeKey));

                    hk.SnapshotKey = activeKey;
                    hk.SnapshotModifiers = activeMods;

                    UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(hk);
                }
            }

            if (ModGroups != null)
            {
                for (int g = 0; g < ModGroups.Count; g++)
                {
                    var group = ModGroups[g];
                    if (group == null) continue;

                    for (int i = 0; i < group.GeneralSettings.Count; i++)
                    {
                        var gen = group.GeneralSettings[i];
                        if (gen == null) continue;

                        gen.PendingBool = null;
                        gen.PendingFloat = null;
                        gen.PendingIndex = null;
                        gen.PendingString = null;

                        if (gen.GetBool != null) gen.SnapshotBool = gen.GetBool();
                        if (gen.GetFloat != null) gen.SnapshotFloat = gen.GetFloat();
                        if (gen.GetIndex != null) gen.SnapshotIndex = gen.GetIndex();
                        if (gen.GetString != null) gen.SnapshotString = gen.GetString() ?? string.Empty;
                    }
                }
            }

            CaptureOpenBaselineSnapshot();
        }
    }
}
