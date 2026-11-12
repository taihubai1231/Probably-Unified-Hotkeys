using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 全新统一按键与模组设置中心 UI 注入与控制器
    /// </summary>
    public static class UnifiedOptionUI
    {
        private const float KeybindButtonWidth = 160f; // 与原版 GameOptionUIManager.KEYBIND_BUTTON_WIDTH 一致

        // 原版官方配置出厂默认值表（与游戏原生 EmporiumOption.SetDefault + GameOptionRegistry.RegisterVanilla 一一对应）
        private static readonly Dictionary<string, bool> VanillaDefaultBools = new Dictionary<string, bool>(StringComparer.Ordinal)
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

        private static readonly Dictionary<string, float> VanillaDefaultFloats = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["save_history_days"] = 3f,
            ["master_volume"] = 0.5f,
            ["music_volume"] = 0.5f,
            ["background_volume"] = 0.5f,
            ["dialogue_volume"] = 0.5f,
            ["dialogue_speed"] = 0.5f,
            ["dialogue_bubble_scale"] = 10f
        };

        private static GameOptionUIManager? _currentManager;
        private static CustomUIWindow? _currentWindow;
        private static List<VanillaSectionCard> _vanillaCards = new List<VanillaSectionCard>();
        private static List<ModOptionGroup> _modGroups = new List<ModOptionGroup>();
        private static List<ModHotkeyEntry> _allHotkeys = new List<ModHotkeyEntry>();
        private static Dictionary<string, ModHotkeyEntry> _vanillaHotkeyByRawId = new Dictionary<string, ModHotkeyEntry>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<KeyCombo, List<ModHotkeyEntry>> _conflicts = new Dictionary<KeyCombo, List<ModHotkeyEntry>>();

        // 会话内记住原版分区折叠状态（默认展开，支持随时折叠）
        private static readonly Dictionary<string, bool> VanillaExpandedStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // 卡片内部分区头部及分隔线引用（供搜索过滤动态显示/隐藏）
        private static readonly Dictionary<ModOptionGroup, (GameObject? hkHeader, GameObject? genHeader, GameObject? probedHeader, GameObject? midSep, GameObject? probedSep)> CardParts
            = new Dictionary<ModOptionGroup, (GameObject?, GameObject?, GameObject?, GameObject?, GameObject?)>();

        // UI 元素级高性能缓存（窗口关闭时清理）
        private struct ButtonFitSpec
        {
            public float AvailWidth;
            public float AvailHeight;
            public float MaxFontSize;
            public float MinFontSize;
        }

        private static readonly Dictionary<int, ButtonTextColor?> ButtonTextColorCache = new Dictionary<int, ButtonTextColor?>(128);
        private static readonly Dictionary<int, Color> AppliedButtonNormalColorCache = new Dictionary<int, Color>(128);
        private static readonly Dictionary<int, ButtonFitSpec> ButtonFitSpecCache = new Dictionary<int, ButtonFitSpec>(128);

        // 跨打开会话可复用的单次字体缩放尺寸缓存 (fontInstanceId, availWidth*10, availHeight*10, maxFontSize*10, text) -> fittedFontSize
        private static readonly Dictionary<(int fontId, int widthTenths, int heightTenths, int maxSizeTenths, string text), float> FittedFontSizeCache
            = new Dictionary<(int, int, int, int, string), float>(256);

        private static TMP_FontAsset? _activeButtonFont = null;
        private static List<Behaviour>? _cachedSuspendableLayouts = null;
        private static bool _cachedSuspendableLayoutsDirty = true;
        private static Dictionary<string, CustomUIElement>? _currentWindowElements = null;
        internal static bool _inEnhancedRefreshRules = false;
        private static readonly FieldInfo? CustomWindowElementsField = AccessTools.Field(typeof(CustomUIWindow), "elements");

        private static readonly Stopwatch PrebuildStopwatch = new Stopwatch();
        private static bool _suppressRuleConflictRefresh = false;
        private static bool _conflictViewDirty = true;
        private static string? _lastConflictSignature = null;
        private static readonly KeyCode[] BindableKeyboardKeys = BuildBindableKeyboardKeys();

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

        // 窗口开启后后台静默预建游标：按帧耗时预算(<=3.5ms)在折叠态(inactive)容器内批量静默预建，消除"全部展开"首帧卡顿
        private static int _openedFrameCount = -1;
        private static int _prebuildModIndex = 0;

        // 视图与搜索状态
        public static bool IsConflictViewActive { get; private set; } = false;
        public static bool IsDiagnosticViewActive { get; private set; } = false;
        private static ModScanReport _scanReport = new ModScanReport();
        private static string _searchQuery = string.Empty;
        private static SearchScope _searchScope = SearchScope.All;
        private static bool _allExpanded = false;
        private static bool _wasSearchNonEmpty = false;
        private static bool _diagnosticViewDirty = true;
        private static string? _diagnosticStatusMessage = null;
        private static readonly HashSet<string> _expandedDiagnosticModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<ModDiagnosticStatus, bool> _diagnosticSectionExpanded = new Dictionary<ModDiagnosticStatus, bool>
        {
            { ModDiagnosticStatus.HasCandidates, true },
            { ModDiagnosticStatus.UnmanagedCodeMod, false },
            { ModDiagnosticStatus.Managed, false },
            { ModDiagnosticStatus.AssetOnlyMod, false }
        };
        private static readonly List<(ModDiagnosticStatus Status, GameObject BodyObj, CustomUIElement FoldBtn)> _diagnosticSectionViews =
            new List<(ModDiagnosticStatus, GameObject, CustomUIElement)>(4);

        // 统一增强按键捕获状态机（同时服务上方原版设置项与下方模组卡片）
        private static ModHotkeyEntry? _capturingEntry;
        private static int _capturingStartedFrame = -1;
        private static int _restoreCloseOnEscapeFrame = -1;
        private static bool _closeOnEscapeBeforeCapture = true;
        private static bool _waitingMouseRelease = false;
        private static KeyCode _pendingStandaloneModifier = KeyCode.None;
        private static bool _hadMultipleModifiers = false;
        private static ModHotkeyEntry? _lastEndedCaptureEntry;
        private static bool _suppressSameEntryRestart = false;
        private static int _suppressRestartReleaseFrame = -1;
        private static bool _isApplyingSettings = false;
        public static bool IsCapturingModHotkey => _capturingEntry != null;
        public static bool IsEnhancedWindowOpen => _currentWindow != null;

        // 按钮原生默认主题色彩、字号与边距（启动打开时从原生未缩放 Button 提取，保证卡片按钮与官方原生完全一致）
        private static Color _defaultButtonTextColor = Color.white;
        private static Color _defaultHighlightColor = new Color(0.88f, 0.82f, 0.61f, 1f);
        private static Color _defaultPressedColor = new Color(0.57f, 0.55f, 1f, 1f);
        private static float _baseButtonFontSize = 0f;
        private static Vector4 _baseButtonMargin = Vector4.zero;
        private static bool _hasBaseButtonMargin = false;
        private static bool _hasDefaultButtonTextColor = false;
        private static bool _hasDefaultButtonTheme = false;
        private static string? _capturedThemeLayer = null;

        // 各种容器与控件引用
        private static ScrollRect? _scrollRect;
        private static GameObject? _pendingScrollTarget;
        private static int _pendingScrollMinFrame = -1;
        private static int _pendingScrollRemainingPasses = 0;
        private static GameObject? _normalContentRoot;
        private static GameObject? _modCardsContainer;
        private static GameObject? _conflictContentRoot;
        private static GameObject? _diagnosticStickyHeaderRoot;
        private static GameObject? _diagnosticContentRoot;
        private static GameObject? _vanillaHeaderRow;
        private static GameObject? _vanillaHeaderSep;
        private static GameObject? _modHeaderSpace;
        private static GameObject? _modHeaderSep;
        private static GameObject? _modHeaderRow;
        private static GameObject? _modHeaderBottomSep;
        private static GameObject? _emptySearchLabelRow;
        private static CustomUIElement? _conflictToggleBtn;
        private static CustomUIElement? _expandToggleBtn;
        private static CustomUIElement? _diagnosticToggleBtn;
        private static TMP_InputField? _searchInputField;

        // 缓存 ButtonTextColor 私有色彩字段（OnPointerEnter/Exit/Up/Disable 会使用 normal/highlight/pressed 覆写 text.color）
        private static readonly FieldInfo? BtcNormalField = AccessTools.Field(typeof(ButtonTextColor), "normal");
        private static readonly FieldInfo? BtcHighlightField = AccessTools.Field(typeof(ButtonTextColor), "highlight");
        private static readonly FieldInfo? BtcPressedField = AccessTools.Field(typeof(ButtonTextColor), "pressed");
        private static readonly FieldInfo? BtcIsHoveredField = AccessTools.Field(typeof(ButtonTextColor), "isHovered");

        // 反射缓存原版 GameOptionUIManager 私有成员
        private static T? CreateMethodDelegate<T>(MethodInfo? method) where T : Delegate
        {
            if (method == null) return null;
            try
            {
                return (T)Delegate.CreateDelegate(typeof(T), method);
            }
            catch
            {
                return null;
            }
        }

        private static readonly Action<GameOptionUIManager, CustomUIBuilder, GameOptionRegistry.Entry>? BuildEntryDelegate =
            CreateMethodDelegate<Action<GameOptionUIManager, CustomUIBuilder, GameOptionRegistry.Entry>>(AccessTools.Method(typeof(GameOptionUIManager), "BuildEntry"));
        private static readonly Func<GameOptionRegistry.Section, string>? NoteIdDelegate =
            CreateMethodDelegate<Func<GameOptionRegistry.Section, string>>(AccessTools.Method(typeof(GameOptionUIManager), "NoteId"));
        private static readonly Action<GameOptionUIManager>? OriginalApplySettingDelegate =
            CreateMethodDelegate<Action<GameOptionUIManager>>(AccessTools.Method(typeof(GameOptionUIManager), "OnApplySetting"));
        private static readonly Action<GameOptionUIManager>? RefreshRulesDelegate =
            CreateMethodDelegate<Action<GameOptionUIManager>>(AccessTools.Method(typeof(GameOptionUIManager), "RefreshRules"));
        private static readonly Action<GameOptionUIManager, string>? SetKeyBindStatusDelegate =
            CreateMethodDelegate<Action<GameOptionUIManager, string>>(AccessTools.Method(typeof(GameOptionUIManager), "SetKeyBindStatus", new Type[] { typeof(string) }));
        private static readonly Action<string>? FireModConfigAppliedDelegate =
            CreateMethodDelegate<Action<string>>(AccessTools.Method(typeof(ModHook), "FireOnModConfigApplied", new Type[] { typeof(string) }));
        private static readonly FieldInfo? PendingKeysField = AccessTools.Field(typeof(GameOptionUIManager), "pendingKeys");
        private static readonly FieldInfo? OverriddenDropdownsField = AccessTools.Field(typeof(GameOptionUIManager), "overriddenDropdowns");
        private static readonly FieldInfo? WindowField = AccessTools.Field(typeof(GameOptionUIManager), "window");
        private static readonly FieldInfo? ConsumedEscapeFrameField = AccessTools.Field(typeof(GameOptionUIManager), "consumedEscapeFrame");

        internal static void InvokeRefreshRulesSafe(GameOptionUIManager? manager)
        {
            if (manager == null || RefreshRulesDelegate == null) return;
            _inEnhancedRefreshRules = true;
            try
            {
                RefreshRulesDelegate.Invoke(manager);
            }
            finally
            {
                _inEnhancedRefreshRules = false;
            }
        }

        internal static bool ShouldSilenceMissingElementWarning(CustomUIWindow? window, string id, out CustomUIElement? element)
        {
            element = null;
            if (!_inEnhancedRefreshRules || window == null || window != _currentWindow || _currentWindowElements == null || string.IsNullOrEmpty(id))
            {
                return false;
            }
            _currentWindowElements.TryGetValue(id, out element);
            return true;
        }

        /// <summary>
        /// 打开全新统一设置面板
        /// </summary>
        private static void ClearSessionUiCaches()
        {
            ButtonTextColorCache.Clear();
            AppliedButtonNormalColorCache.Clear();
            ButtonFitSpecCache.Clear();
            CardParts.Clear();
            _diagnosticSectionViews.Clear();
        }

        private static void ResetDiagnosticSectionDefaults()
        {
            _diagnosticSectionExpanded[ModDiagnosticStatus.HasCandidates] = true;
            _diagnosticSectionExpanded[ModDiagnosticStatus.UnmanagedCodeMod] = false;
            _diagnosticSectionExpanded[ModDiagnosticStatus.Managed] = false;
            _diagnosticSectionExpanded[ModDiagnosticStatus.AssetOnlyMod] = false;
        }

        public static void OpenEnhancedUI(GameOptionUIManager manager)
        {
            _currentManager = manager;
            _currentWindow = null;
            IsConflictViewActive = false;
            IsDiagnosticViewActive = false;
            _conflictViewDirty = true;
            _diagnosticViewDirty = true;
            _diagnosticStatusMessage = null;
            _expandedDiagnosticModIds.Clear();
            ResetDiagnosticSectionDefaults();
            _lastConflictSignature = null;
            _suppressRuleConflictRefresh = false;
            _searchQuery = string.Empty;
            _wasSearchNonEmpty = false;
            ClearPendingScroll();
            _scrollRect = null;
            _normalContentRoot = null;
            _modCardsContainer = null;
            _conflictContentRoot = null;
            _diagnosticStickyHeaderRoot = null;
            _diagnosticContentRoot = null;
            _conflictToggleBtn = null;
            _expandToggleBtn = null;
            _diagnosticToggleBtn = null;
            _capturingEntry = null;
            _capturingStartedFrame = -1;
            _restoreCloseOnEscapeFrame = -1;
            _waitingMouseRelease = false;
            _pendingStandaloneModifier = KeyCode.None;
            _hadMultipleModifiers = false;
            _vanillaCards.Clear();
            _vanillaHotkeyByRawId.Clear();
            _vanillaHeaderRow = null;
            _vanillaHeaderSep = null;
            _modHeaderSpace = null;
            _modHeaderSep = null;
            _modHeaderRow = null;
            _modHeaderBottomSep = null;
            _emptySearchLabelRow = null;
            _openedFrameCount = Time.frameCount;
            _prebuildModIndex = 0;
            ClearSessionUiCaches();
            ComboKeyStore.EnsureLoaded();

            // 1. 初始化原版环境
            if (EmporiumOption.Instance != null)
            {
                EmporiumOption.Instance.SyncDisplayStateFromScreen();
            }
            GameOptionRegistry.EnsureVanilla();
            GameOptionRegistry.RefreshMonitorInfo();
            KeyBindHelper.StopCapture();

            var pendingKeys = PendingKeysField?.GetValue(manager) as Dictionary<string, KeyCode>;
            pendingKeys?.Clear();
            var overriddenDropdowns = OverriddenDropdownsField?.GetValue(manager) as System.Collections.IDictionary;
            overriddenDropdowns?.Clear();
            SetKeyBindStatusDelegate?.Invoke(manager, string.Empty);

            // 2. 加载全量模组配置并分析按键冲突
            _modGroups = ModCatalogCache.GetOrScanGroups(out _allHotkeys, out _scanReport);
            foreach (var hk in _allHotkeys)
            {
                if (hk.ModId == "vanilla" && !string.IsNullOrEmpty(hk.RawOptionId))
                {
                    _vanillaHotkeyByRawId[hk.RawOptionId] = hk;
                }
            }
            using (ComboKeyStore.EnterBypass())
            {
                _conflicts = ConflictDetector.Analyze(_allHotkeys);
            }

            // 3. 构建原生窗口
            bool isStoreUi = StoreUIManager.Instance != null;
            string layer = isStoreUi ? "meta" : "menu";

            var builder = CustomUIManager.Instance.CreateWindow(
                GameOptionUIManager.WINDOW_ID,
                null,
                layer,
                CustomUIManager.Instance.optionUIPrefab,
                CustomUIManager.Style.Auto
            );

            // 若已完成当前视图层级(menu/meta)的原生按钮主题抓取，立即隐藏根对象完成离线构建，避免每生成一个控件就触发 Canvas/Layout 重算
            bool hasThemeForLayer = _hasDefaultButtonTheme && string.Equals(_capturedThemeLayer, layer, StringComparison.Ordinal);
            GameObject? windowRoot = builder.Handle?.root;
            if (hasThemeForLayer && windowRoot != null && windowRoot.activeSelf)
            {
                windowRoot.SetActive(false);
            }

            builder.SetCloseOnEscape(!isStoreUi);
            builder.OnClose(() =>
            {
                _currentWindow = null;
                _scrollRect = null;
                _normalContentRoot = null;
                _modCardsContainer = null;
                _conflictContentRoot = null;
                _diagnosticStickyHeaderRoot = null;
                _diagnosticContentRoot = null;
                _conflictToggleBtn = null;
                _expandToggleBtn = null;
                _diagnosticToggleBtn = null;
                ClearPendingScroll();
                _capturingEntry = null;
                _restoreCloseOnEscapeFrame = -1;
                _waitingMouseRelease = false;
                _pendingStandaloneModifier = KeyCode.None;
                _hadMultipleModifiers = false;
                IsConflictViewActive = false;
                IsDiagnosticViewActive = false;
                _vanillaCards.Clear();
                _vanillaHotkeyByRawId.Clear();
                _openedFrameCount = -1;
                _prebuildModIndex = 0;
                ClearSessionUiCaches();
            });

            // =================================================================
            // 4. 顶部快捷操作栏（两行舒展排版，留足边缘间距与控件宽度）
            // =================================================================
            builder.AddSpace(4f);

            // 第一行：搜索范围下拉框 + 宽幅搜索框
            builder.BeginRow(8f, TextAnchor.MiddleCenter);
            builder.AddSpace(10f);

            builder.AddDropdown(
                new List<string>
                {
                    I18n.Get("整体搜索", "Search All"),
                    I18n.Get("按键+常规", "Keys + Settings"),
                    I18n.Get("仅搜按键", "Keys Only"),
                    I18n.Get("仅搜常规", "Settings Only")
                },
                (int)_searchScope,
                OnSearchScopeChanged,
                "unified_search_scope"
            );
            var scopeEl = builder.Last;
            scopeEl.SetFixedWidth(162f);
            scopeEl.SetPreferredSize(162f, 36f);
            I18n.ApplyFontToDropdown(scopeEl, forceCjkCapableList: I18n.IsChinese);

            builder.AddInput(
                I18n.Get(
                    "搜索设置项或按键 (支持繁简中文、英文、拼音如 sp=商品)...",
                    "Search settings, hotkeys or mods (EN / CN / Pinyin)..."
                ),
                _searchQuery,
                OnSearchChanged,
                "unified_search_input",
                OnSearchTyping
            );
            var searchEl = builder.Last;
            searchEl.SetFlexible(1f, 0f);
            if (searchEl.input != null)
            {
                _searchInputField = searchEl.input;
                I18n.ApplyFontIfRuntime(searchEl.input.textComponent, null);
                I18n.ApplyFontIfRuntime(searchEl.input.placeholder as TextMeshProUGUI, searchEl.input.placeholder is TextMeshProUGUI phTmp ? phTmp.text : null);
            }

            builder.AddSpace(10f);
            builder.End();

            builder.AddSpace(6f);

            // 第二行：仅看冲突按钮 + 全部展开/收起按钮 + 模组排查按钮 + 快捷提示（单行紧凑排版，避免右侧提示折行拉高表头）
            builder.BeginRow(6f, TextAnchor.MiddleCenter);
            builder.AddSpace(10f);

            string conflictBtnText = _conflicts.Count > 0
                ? I18n.Format("[!] 仅看冲突 ({0} 组)", "[!] Conflicts ({0})", _conflicts.Count)
                : I18n.Get("[!] 仅看冲突", "[!] Conflicts");
            builder.AddButton(conflictBtnText, ToggleConflictMode, "btn_conflict_toggle");
            _conflictToggleBtn = builder.Last;

            builder.AddButton(_allExpanded ? I18n.Get("全部收起", "Collapse All") : I18n.Get("全部展开", "Expand All"), ToggleExpandAll, "btn_expand_all");
            _expandToggleBtn = builder.Last;

            int diagCount = _scanReport?.AttentionModCount ?? 0;
            string diagBtnText = diagCount > 0
                ? I18n.Format("模组排查 ({0})", "Diagnostics ({0})", diagCount)
                : I18n.Get("模组排查", "Diagnostics");
            builder.AddButton(diagBtnText, ToggleDiagnosticMode, "btn_diagnostic_toggle");
            _diagnosticToggleBtn = builder.Last;

            // 优先抓取原生 ButtonTextColor 默认字色、高亮色、按下色、激活字体与原始字号（每个层级 menu/meta 仅需首开抓取一次）
            if (!hasThemeForLayer)
            {
                CaptureDefaultButtonTheme(_expandToggleBtn, forceRefresh: true);
                _capturedThemeLayer = layer;
            }
            if (I18n.IsChinese && FontLoader.Instance != null && FontLoader.Instance.simplifiedChineseFont != null)
            {
                _activeButtonFont = FontLoader.Instance.simplifiedChineseFont;
            }
            else if (_expandToggleBtn?.text?.font != null)
            {
                _activeButtonFont = _expandToggleBtn.text.font;
            }

            // 抓取完成后即关闭根节点激活态，确保整页百余个原版与模组控件在离线非激活状态一次性装配完毕
            if (windowRoot != null && windowRoot.activeSelf)
            {
                windowRoot.SetActive(false);
            }

            StyleActionButton(_conflictToggleBtn, 180f, 38f, lrMargin: 8f, tbMargin: 3f, fontScale: 0.85f);
            StyleActionButton(_expandToggleBtn, 116f, 38f, lrMargin: 8f, tbMargin: 3f, fontScale: 0.85f);
            StyleActionButton(_diagnosticToggleBtn, 148f, 38f, lrMargin: 8f, tbMargin: 3f, fontScale: 0.85f);

            UpdateConflictToggleButtonAppearance();
            UpdateDiagnosticToggleButtonAppearance();

            string topHintText = I18n.Get("Ctrl/Shift/Alt组合键与侧键 | Esc=无", "Ctrl/Shift/Alt Combos & Side | Esc=None");
            builder.AddLabel(topHintText, "label_top_hint");
            var hintEl = builder.Last;
            if (hintEl != null)
            {
                I18n.ApplyFontIfRuntime(hintEl.text, topHintText);
                if (hintEl.root != null)
                {
                    var hintLe = hintEl.root.GetComponent<LayoutElement>() ?? hintEl.root.AddComponent<LayoutElement>();
                    hintLe.minWidth = 0f;
                    hintLe.preferredWidth = 265f;
                    hintLe.preferredHeight = 34f;
                    hintLe.flexibleWidth = 1f;
                    hintLe.flexibleHeight = 0f;
                }
                if (hintEl.text != null)
                {
                    hintEl.text.enableWordWrapping = false;
                    hintEl.text.overflowMode = TextOverflowModes.Ellipsis;
                    hintEl.text.alignment = TextAlignmentOptions.MidlineRight;
                    hintEl.text.color = new Color(0.60f, 0.64f, 0.69f, 1f);
                    float maxHintSize = Mathf.Clamp((_baseButtonFontSize > 0f ? _baseButtonFontSize : 20f) * 0.78f, 12f, 17f);
                    ApplySinglePassFontSize(hintEl.text, hintEl.text.text, new ButtonFitSpec
                    {
                        AvailWidth = 265f,
                        AvailHeight = 30f,
                        MaxFontSize = maxHintSize,
                        MinFontSize = 11f
                    });
                }
            }
            builder.AddSpace(10f);
            builder.End();

            builder.AddSeparator(null, 2);

            // =================================================================
            // 4.5 模组排查置顶摘要框（位于顶部工具栏之下、ScrollRect 之上；浏览排查列表时固定可见）
            // =================================================================
            builder.BeginColumn(4f, TextAnchor.UpperLeft);
            _diagnosticStickyHeaderRoot = builder.CurrentContainer.gameObject;
            var diagStickyVlg = _diagnosticStickyHeaderRoot.GetComponent<VerticalLayoutGroup>();
            if (diagStickyVlg != null) diagStickyVlg.childForceExpandWidth = true;
            var diagStickyLe = _diagnosticStickyHeaderRoot.GetComponent<LayoutElement>() ?? _diagnosticStickyHeaderRoot.AddComponent<LayoutElement>();
            diagStickyLe.flexibleWidth = 1f;
            diagStickyLe.flexibleHeight = 0f;
            _diagnosticStickyHeaderRoot.SetActive(false);
            builder.End();

            // =================================================================
            // 5. 滚动视窗核心区域（折叠卡片懒加载，消除打开选项页卡顿）
            // =================================================================
            builder.BeginScrollFlexible(0f);
            // OpenEnhancedUI 在此之前已调用 windowRoot.SetActive(false) 做离线批量装配，必须传 includeInactive: true 才能获取到 ScrollRect
            _scrollRect = builder.CurrentContainer.GetComponentInParent<ScrollRect>(true);
            if (_scrollRect == null && windowRoot != null)
            {
                _scrollRect = windowRoot.GetComponentInChildren<ScrollRect>(true);
            }

            // --- A. 常规完整设置视图容器 ---
            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            _normalContentRoot = builder.CurrentContainer.gameObject;
            var normalColumnRt = builder.CurrentContainer;

            // (1) 原版官方设置与快捷键（统一可折叠卡片 + 支持全范围搜索）
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            _vanillaHeaderRow = builder.CurrentContainer.gameObject;
            builder.AddSpace(6f);
            string vanillaHdrText = I18n.Get("游戏原生设置与快捷键", "Vanilla Settings & Hotkeys");
            builder.AddHeader(vanillaHdrText, "header_vanilla_sections");
            I18n.ApplyFontIfRuntime(builder.Last?.text, vanillaHdrText);
            builder.End();
            builder.AddSeparator(null, 2);
            _vanillaHeaderSep = GetLastChildGameObject(normalColumnRt);

            foreach (var section in GameOptionRegistry.Sections)
            {
                if (section == null) continue;
                BuildVanillaSectionCard(builder, manager, section);
            }

            // (2) 模组区块分割标题
            builder.AddSpace(12f);
            _modHeaderSpace = GetLastChildGameObject(normalColumnRt);
            builder.AddSeparator(null, 3);
            _modHeaderSep = GetLastChildGameObject(normalColumnRt);
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            _modHeaderRow = builder.CurrentContainer.gameObject;
            builder.AddSpace(6f);
            string modHdrText = I18n.Get("创意工坊模组设置与快捷键", "Mod Settings & Hotkeys");
            builder.AddHeader(modHdrText, "header_mod_sections");
            I18n.ApplyFontIfRuntime(builder.Last?.text, modHdrText);
            builder.End();
            builder.AddSeparator(null, 2);
            _modHeaderBottomSep = GetLastChildGameObject(normalColumnRt);

            // (3) 创意工坊模组折叠卡片列表（置于独立子容器以支持热重扫原地重建，初始仅构建头部外壳）
            using (ComboKeyStore.EnterBypass())
            {
                builder.BeginColumn(2f, TextAnchor.UpperLeft);
                _modCardsContainer = builder.CurrentContainer.gameObject;
                foreach (var group in _modGroups)
                {
                    BuildModGroupCardShell(builder, group);
                }
                builder.End(); // 结束 _modCardsContainer Column

                // 空搜索结果提示（默认隐藏）
                builder.BeginRow(4f, TextAnchor.MiddleCenter);
                _emptySearchLabelRow = builder.CurrentContainer.gameObject;
                builder.AddSpace(10f);
                builder.AddFlexibleSpace();
                string emptySearchText = I18n.Get(
                    "<color=#99a3b0>未找到匹配的设置项或按键，可尝试更换关键词或切换左侧搜索范围</color>",
                    "<color=#99a3b0>No matching settings or hotkeys found. Try another keyword or search scope.</color>"
                );
                builder.AddLabel(emptySearchText, "label_empty_search");
                I18n.ApplyFontIfRuntime(builder.Last?.text, emptySearchText);
                builder.AddFlexibleSpace();
                builder.AddSpace(10f);
                builder.End();
                _emptySearchLabelRow.SetActive(false);

                builder.End(); // 结束 _normalContentRoot Column

                // --- B. 冲突专属速览视图容器（初始为空并隐藏，点击“仅看冲突”时按需懒构建） ---
                builder.BeginColumn(2f, TextAnchor.UpperLeft);
                _conflictContentRoot = builder.CurrentContainer.gameObject;
                _conflictContentRoot.SetActive(false);
                builder.End(); // 结束 _conflictContentRoot Column

                // --- C. 模组排查与疑难接管视图容器（初始为空并隐藏，点击“模组排查”时按需懒构建） ---
                builder.BeginColumn(4f, TextAnchor.UpperLeft);
                _diagnosticContentRoot = builder.CurrentContainer.gameObject;
                var diagContentVlg = _diagnosticContentRoot.GetComponent<VerticalLayoutGroup>();
                if (diagContentVlg != null) diagContentVlg.childForceExpandWidth = true;
                var diagContentLe = _diagnosticContentRoot.GetComponent<LayoutElement>() ?? _diagnosticContentRoot.AddComponent<LayoutElement>();
                diagContentLe.flexibleWidth = 1f;
                _diagnosticContentRoot.SetActive(false);
            }

            builder.End(); // 结束 _diagnosticContentRoot Column
            builder.End(); // 结束 BeginScrollFlexible

            // =================================================================
            // 6. 底部操作栏：原生“应用设置”按钮
            // =================================================================
            builder.AddSeparator(null, 2);
            builder.BeginRow(4f, TextAnchor.MiddleCenter);
            builder.AddSpace(10f);
            builder.AddFlexibleSpace();

            builder.AddButton(LocHelper.GetLocalizedMainMenu("option_apply"), () =>
            {
                ApplyAllSettings(manager);
            }, "apply");
            StyleNativeWidthButton(builder.Last, 180f);

            builder.AddFlexibleSpace();
            builder.AddSpace(10f);
            builder.End();

            // 7. 仍在离线非激活态下同步记忆展开态、样式、原版待定键值与冲突色彩，再一次性激活展示并仅做单次布局刷新
            _currentWindow = builder.Handle;
            RestoreSavedExpandedModCards();
            RefreshExpandAllButtonState();
            StyleVanillaKeyButtons();
            SyncVanillaPendingFromManager();
            RefreshConflicts();

            _currentWindow = builder.Show();
            _currentWindowElements = CustomWindowElementsField?.GetValue(_currentWindow) as Dictionary<string, CustomUIElement>;
            _cachedSuspendableLayouts = null;
            _cachedSuspendableLayoutsDirty = true;
            WindowField?.SetValue(manager, _currentWindow);
            SyncActionButtonsAfterShow();
            _suppressRuleConflictRefresh = true;
            try
            {
                InvokeRefreshRulesSafe(manager);
            }
            finally
            {
                _suppressRuleConflictRefresh = false;
            }
            _openedFrameCount = Time.frameCount;
        }

        private static GameObject? GetLastChildGameObject(RectTransform? container)
        {
            if (container == null || container.childCount <= 0) return null;
            return container.GetChild(container.childCount - 1).gameObject;
        }

        private static string SafeSectionText(Func<string>? getter, string fallback)
        {
            if (getter == null) return fallback;
            try
            {
                string val = getter();
                return string.IsNullOrEmpty(val) ? fallback : val;
            }
            catch
            {
                return fallback;
            }
        }

        private static void BuildCardHeaderRow(
            CustomUIBuilder builder,
            ICollapsibleOptionCard card,
            string foldButtonId,
            string resetButtonId,
            string labelId,
            string title,
            int hotkeyCount,
            int generalCount,
            bool initialExpanded,
            Action onFoldClicked,
            Action? onResetClicked,
            Action<CustomUIBuilder>? buildRightContent,
            out GameObject headerRow,
            out CustomUIElement foldBtn,
            int probedCount = 0)
        {
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            headerRow = builder.CurrentContainer.gameObject;
            builder.AddSpace(6f);

            builder.AddButton(initialExpanded ? "▼" : "▶", onFoldClicked, foldButtonId);
            foldBtn = builder.Last;
            StyleFoldButton(foldBtn, 36f);

            string badge = probedCount > 0
                ? I18n.Format(
                    "<color=#99a3b0>(按键: {0} | 常规: {1} | </color><color=#d8b4fe>探测: {2}</color><color=#99a3b0>)</color>",
                    "<color=#99a3b0>(Keys: {0} | Settings: {1} | </color><color=#d8b4fe>Probed: {2}</color><color=#99a3b0>)</color>",
                    hotkeyCount,
                    generalCount,
                    probedCount
                )
                : I18n.Format(
                    "<color=#99a3b0>(按键: {0} | 常规: {1})</color>",
                    "<color=#99a3b0>(Keys: {0} | Settings: {1})</color>",
                    hotkeyCount,
                    generalCount
                );
            string cardHeaderText = $"<b>{title}</b> {badge}";
            builder.AddLabel(cardHeaderText, labelId);
            I18n.ApplyFontIfRuntime(builder.Last?.text, cardHeaderText);

            builder.AddFlexibleSpace();
            buildRightContent?.Invoke(builder);

            if (onResetClicked != null)
            {
                if (buildRightContent != null)
                {
                    builder.AddSpace(6f);
                }
                builder.AddButton(I18n.Get("恢复默认", "Reset"), () =>
                {
                    CancelCapture();
                    onResetClicked();
                }, resetButtonId);
                card.ResetButtonElement = builder.Last;
                StyleActionButton(card.ResetButtonElement, 112f, 36f, lrMargin: 8f, tbMargin: 2f, fontScale: 0.86f);
                card.ResetButtonElement?.SetVisible(initialExpanded && card.ResetToDefaultsAction != null);
            }

            builder.AddSpace(6f);
            builder.End();
        }

        /// <summary>
        /// 构建可折叠且支持过滤的纯原版设置区块卡片（严格过滤第三方模组条目，保留全部原版交互规则与 Apply 读写语义）
        /// </summary>
        private static void BuildVanillaSectionCard(CustomUIBuilder builder, GameOptionUIManager manager, GameOptionRegistry.Section section)
        {
            if (!GameOptionRegistry.IsVanillaSection(section.id)) return;
            if (!string.IsNullOrEmpty(section.source) &&
                !string.Equals(section.source, "vanilla", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (section.entries == null || section.entries.Count == 0) return;

            var filteredEntries = new List<GameOptionRegistry.Entry>();
            int hkCount = 0;
            int genCount = 0;
            foreach (var entry in section.entries)
            {
                if (!ModOptionScanner.IsPureVanillaEntry(section, entry)) continue;
                filteredEntries.Add(entry);
                if (entry.kind == GameOptionRegistry.Kind.KeyBind) hkCount++;
                else genCount++;
            }

            if (filteredEntries.Count == 0) return;

            bool isExpanded = VanillaExpandedStates.TryGetValue(section.id, out bool savedState) ? savedState : true;
            VanillaExpandedStates[section.id] = isExpanded;

            var card = new VanillaSectionCard
            {
                SectionId = section.id,
                DisplayName = SafeSectionText(section.label, section.id),
                Section = section,
                IsExpanded = isExpanded,
                HotkeyCount = hkCount,
                GeneralCount = genCount
            };
            card.ResetToDefaultsAction = () => ResetVanillaSectionToDefaults(manager, card);

            string noteId = NoteIdDelegate != null ? NoteIdDelegate(section) : ("note_" + section.id);
            BuildCardHeaderRow(
                builder,
                card,
                "fold_vanilla_" + section.id,
                "reset_vanilla_" + section.id,
                "section_" + section.id,
                card.DisplayName,
                hkCount,
                genCount,
                isExpanded,
                () => OnManualFoldVanillaCard(card),
                card.ResetToDefaultsAction,
                section.note != null
                    ? (Action<CustomUIBuilder>)(b => b.AddWarning(SafeSectionText(section.note, string.Empty), noteId))
                    : null,
                out GameObject headerRow,
                out CustomUIElement foldBtn
            );
            card.HeaderRowObject = headerRow;
            card.FoldArrowElement = foldBtn;

            // 构建折叠内容容器：顶部细分隔线 + 与模组卡片完全一致的 18px/10px 内缩进列
            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            card.ContentContainerObject = builder.CurrentContainer.gameObject;
            builder.AddSeparator(null, 1);

            builder.BeginColumn(4f, TextAnchor.UpperLeft);
            var entriesColumnRt = builder.CurrentContainer;
            var entriesLayout = entriesColumnRt.GetComponent<VerticalLayoutGroup>();
            if (entriesLayout != null)
            {
                // 与模组卡片内 AddSpace(18)+6 间距及右侧 AddSpace(10)+6 保持像素级对齐 (24px / 16px)
                entriesLayout.padding = new RectOffset(24, 16, 2, 6);
            }

            foreach (var entry in filteredEntries)
            {
                int childBefore = entriesColumnRt.childCount;
                BuildEntryDelegate?.Invoke(manager, builder, entry);

                ModHotkeyEntry? linked = null;
                if (entry.kind == GameOptionRegistry.Kind.KeyBind)
                {
                    _vanillaHotkeyByRawId.TryGetValue(entry.id, out linked);
                }

                var item = new VanillaOptionItem
                {
                    Entry = entry,
                    DisplayName = SafeSectionText(entry.label, entry.id),
                    IsKeyBind = entry.kind == GameOptionRegistry.Kind.KeyBind,
                    LinkedHotkey = linked
                };
                for (int i = childBefore; i < entriesColumnRt.childCount; i++)
                {
                    item.RowObjects.Add(entriesColumnRt.GetChild(i).gameObject);
                }
                card.Entries.Add(item);
            }

            builder.End(); // entriesColumnRt
            builder.End(); // ContentContainerObject

            builder.AddSeparator(null, 2);
            card.OuterSeparatorObject = GetLastChildGameObject(builder.CurrentContainer);

            card.SetExpanded(isExpanded);
            _vanillaCards.Add(card);
        }

        // =====================================================================
        // 模组卡片构建 (懒加载内容区 + 垂直分区：上半截快捷键，下半截常规设置)
        // =====================================================================
        private static void BuildModGroupCardShell(CustomUIBuilder builder, ModOptionGroup group)
        {
            Action? onModReset = group.ResetToDefaultsAction != null
                ? () =>
                {
                    bool wasChinese = I18n.IsChinese;
                    using (ComboKeyStore.EnterBypass())
                    {
                        group.ResetToDefaultsAction?.Invoke();
                        RefreshCardUI(group);
                        ComboKeyStore.RebuildRuntimeLookup(_allHotkeys);
                    }
                    NotifyGroupConfigsApplied(group);
                    if (wasChinese != I18n.IsChinese && _currentManager != null)
                    {
                        ReopenEnhancedUIForLanguageSwitch(_currentManager, group.ModId);
                    }
                }
                : null;

            BuildCardHeaderRow(
                builder,
                group,
                "fold_" + group.ModId,
                "reset_" + group.ModId,
                "label_group_" + group.ModId,
                group.DisplayName,
                group.Hotkeys.Count,
                group.GeneralSettings.Count,
                group.IsExpanded,
                () => OnManualFoldModGroup(group),
                onModReset,
                null,
                out GameObject headerRow,
                out CustomUIElement foldBtn,
                group.ReadOnlyProbedHotkeys.Count
            );
            group.HeaderRowObject = headerRow;
            group.FoldArrowElement = foldBtn;

            // 2. 卡片展开内容占位容器（折叠态初始为空，由窗口开启后的静默后台预建或按需触发填充）
            builder.BeginColumn(3f, TextAnchor.UpperLeft);
            group.ContentContainerObject = builder.CurrentContainer.gameObject;
            group.ContentContainerObject.SetActive(false);
            builder.End();

            // 3. 卡片外部分界横线（无论折叠还是展开都清晰划分模组卡片边界）
            builder.AddSeparator(null, 2);
            group.OuterSeparatorObject = GetLastChildGameObject(builder.CurrentContainer);
        }

        private static void RestoreSavedExpandedModCards()
        {
            foreach (var group in _modGroups)
            {
                if (group.IsExpanded)
                {
                    SetGroupExpanded(group, true);
                }
            }
        }

        private static void EnsureGroupContentBuilt(ModOptionGroup group)
        {
            if (group.IsContentBuilt || group.ContentContainerObject == null || _currentWindow == null || CustomUIManager.Instance == null)
            {
                return;
            }

            var contentRt = group.ContentContainerObject.GetComponent<RectTransform>();
            if (contentRt == null) return;

            group.IsContentBuilt = true;

            using (ComboKeyStore.EnterBypass())
            {
                var builder = new CustomUIBuilder(CustomUIManager.Instance, _currentWindow, contentRt);

                // 头部与内容区的细横线分隔
                builder.AddSeparator(null, 1);

                GameObject? hkHeaderObj = null;
                GameObject? genHeaderObj = null;
                GameObject? probedHeaderObj = null;
                GameObject? midSepObj = null;
                GameObject? probedSepObj = null;

                // --- 上半截：快捷键绑定 ---
                if (group.Hotkeys.Count > 0)
                {
                    builder.BeginRow(4f, TextAnchor.MiddleLeft);
                    hkHeaderObj = builder.CurrentContainer.gameObject;
                    builder.AddSpace(18f);
                    string hkHdrText = I18n.Get("<color=#9ec4ff><b>快捷键</b></color>", "<color=#9ec4ff><b>Hotkeys</b></color>");
                    builder.AddLabel(hkHdrText, "hk_hdr_" + group.ModId);
                    I18n.ApplyFontIfRuntime(builder.Last?.text, hkHdrText);
                    builder.End();

                    foreach (var hotkey in group.Hotkeys)
                    {
                        BuildHotkeyRow(builder, hotkey);
                    }
                }

                // --- 中间横线分隔（上半快捷键与下半常规同时存在时显示） ---
                if (group.Hotkeys.Count > 0 && group.GeneralSettings.Count > 0)
                {
                    builder.AddSeparator(null, 1);
                    if (contentRt.childCount > 0)
                    {
                        midSepObj = contentRt.GetChild(contentRt.childCount - 1).gameObject;
                    }
                }

                // --- 下半截：常规通用配置 (开关 / 滑块 / 下拉框) ---
                if (group.GeneralSettings.Count > 0)
                {
                    builder.BeginRow(4f, TextAnchor.MiddleLeft);
                    genHeaderObj = builder.CurrentContainer.gameObject;
                    builder.AddSpace(18f);
                    string genHdrText = I18n.Get("<color=#a8d8b9><b>常规设置</b></color>", "<color=#a8d8b9><b>General Settings</b></color>");
                    builder.AddLabel(genHdrText, "gen_hdr_" + group.ModId);
                    I18n.ApplyFontIfRuntime(builder.Last?.text, genHdrText);
                    builder.End();

                    foreach (var gen in group.GeneralSettings)
                    {
                        BuildGeneralSettingRow(builder, gen);
                    }
                }

                // --- 只读探测热键（静态 IL / 本地配置文件自动识别，仅供冲突排查） ---
                if (group.ReadOnlyProbedHotkeys.Count > 0)
                {
                    if (group.Hotkeys.Count > 0 || group.GeneralSettings.Count > 0)
                    {
                        builder.AddSeparator(null, 1);
                        if (contentRt.childCount > 0)
                        {
                            probedSepObj = contentRt.GetChild(contentRt.childCount - 1).gameObject;
                        }
                    }

                    builder.BeginRow(4f, TextAnchor.MiddleLeft);
                    probedHeaderObj = builder.CurrentContainer.gameObject;
                    builder.AddSpace(18f);
                    string probedHdrText = I18n.Get(
                        "<color=#d8b4fe><b>自动探测热键 (只读 · 仅供冲突排查)</b></color>",
                        "<color=#d8b4fe><b>Probed Hotkeys (Read-Only · Conflict Check)</b></color>");
                    builder.AddLabel(probedHdrText, "probed_hdr_" + group.ModId);
                    I18n.ApplyFontIfRuntime(builder.Last?.text, probedHdrText);
                    builder.End();

                    foreach (var probed in group.ReadOnlyProbedHotkeys)
                    {
                        BuildHotkeyRow(builder, probed);
                    }
                }

                builder.AddSpace(4f);
                CardParts[group] = (hkHeaderObj, genHeaderObj, probedHeaderObj, midSepObj, probedSepObj);
            }
        }

        private static bool IsCardCurrentlyExpanded(ICollapsibleOptionCard card)
        {
            return card.ContentContainerObject != null ? card.ContentContainerObject.activeSelf : card.IsExpanded;
        }

        private static void OnManualFoldVanillaCard(VanillaSectionCard card)
        {
            bool nextExpanded = !IsCardCurrentlyExpanded(card);
            card.SetExpanded(nextExpanded);
            VanillaExpandedStates[card.SectionId] = nextExpanded;
            RefreshExpandAllButtonState();
        }

        private static void OnManualFoldModGroup(ModOptionGroup group)
        {
            bool nextExpanded = !IsCardCurrentlyExpanded(group);
            SetGroupExpanded(group, nextExpanded);
            RefreshExpandAllButtonState();
        }

        private static void SetGroupExpanded(ModOptionGroup group, bool expanded)
        {
            if (expanded)
            {
                EnsureGroupContentBuilt(group);
            }
            group.SetExpanded(expanded);
        }

        private static void BuildGeneralSettingRow(CustomUIBuilder builder, ModGeneralEntry gen)
        {
            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            gen.RowObject = builder.CurrentContainer.gameObject;
            builder.AddSpace(18f);

            string labelText = gen.DisplayName;
            if (!string.IsNullOrEmpty(gen.Tooltip))
            {
                string link = "modopt:" + gen.Id;
                TMPIconHover.CustomLinks[link] = () => gen.Tooltip;
                labelText += $" <link=\"{link}\"><sprite name=\"info\" tint=1></link>";
            }

            builder.AddLabel(labelText, "label_" + gen.Id);
            I18n.ApplyFontIfRuntime(builder.Last?.text, labelText);
            builder.AddFlexibleSpace();

            switch (gen.Kind)
            {
                case OptionKind.Toggle:
                    bool currentBool = gen.GetBool?.Invoke() ?? false;
                    builder.AddToggle(string.Empty, currentBool, (val) =>
                    {
                        gen.PendingBool = val;
                    }, "toggle_" + gen.Id);
                    gen.ControlElement = builder.Last;
                    break;

                case OptionKind.Slider:
                    float currentFloat = gen.GetFloat?.Invoke() ?? gen.Min;
                    builder.AddLabel(FormatSliderValue(gen, currentFloat), "val_" + gen.Id);
                    gen.ValueLabelElement = builder.Last;
                    gen.ValueLabelElement?.SetFixedWidth(58f);

                    builder.AddSlider(gen.Min, gen.Max, currentFloat, (val) =>
                    {
                        gen.PendingFloat = val;
                        gen.ValueLabelElement?.SetText(FormatSliderValue(gen, val));
                    }, gen.WholeNumbers, "slider_" + gen.Id);
                    gen.ControlElement = builder.Last;
                    gen.ControlElement?.SetFixedWidth(242f);
                    break;

                case OptionKind.Dropdown:
                    var choices = gen.GetChoices?.Invoke() ?? new List<string>();
                    int currentIdx = gen.GetIndex?.Invoke() ?? 0;
                    builder.AddDropdown(choices, currentIdx, (idx) =>
                    {
                        gen.PendingIndex = idx;
                    }, "drop_" + gen.Id);
                    gen.ControlElement = builder.Last;
                    gen.ControlElement?.SetFixedWidth(306f);
                    bool isLangDrop = string.Equals(gen.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase);
                    I18n.ApplyFontToDropdown(gen.ControlElement, forceCjkCapableList: isLangDrop || I18n.IsChinese);
                    break;
            }

            builder.AddSpace(10f);
            builder.End();
        }

        private static void BuildHotkeyRow(CustomUIBuilder builder, ModHotkeyEntry hotkey)
        {
            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            hotkey.RowObject = builder.CurrentContainer.gameObject;
            builder.AddSpace(18f);

            string hkLabel = BuildHotkeyLabelText(hotkey);
            builder.AddLabel(hkLabel, "label_" + hotkey.Id);
            hotkey.LabelElement = builder.Last;
            I18n.ApplyFontIfRuntime(hotkey.LabelElement?.text, hkLabel);

            builder.AddFlexibleSpace();

            string keyText = FormatHotkeyComboText(hotkey);
            builder.AddButton(keyText, () =>
            {
                if (!hotkey.IsReadOnlyProbed)
                {
                    StartCaptureHotkey(hotkey);
                }
            }, "btn_key_" + hotkey.Id);

            hotkey.ButtonElement = builder.Last;
            StyleHotkeyButton(hotkey.ButtonElement);
            if (hotkey.IsReadOnlyProbed && hotkey.ButtonElement?.button != null)
            {
                hotkey.ButtonElement.button.interactable = false;
            }

            if (hotkey.ButtonElement != null && _capturingEntry != hotkey)
            {
                ApplySingleButtonColor(hotkey.ButtonElement, hotkey.EffectiveCombo, hotkey.IsReadOnlyProbed);
            }

            builder.AddSpace(10f);
            builder.End();
        }

        private static string BuildHotkeyLabelText(ModHotkeyEntry hotkey)
        {
            string labelText = hotkey.DisplayName;
            string conflictTip = ConflictDetector.GetConflictTooltip(hotkey, _conflicts);

            if (hotkey.IsReadOnlyProbed)
            {
                string roTag = I18n.Get("<color=#d8b4fe>[探测·只读]</color> ", "<color=#d8b4fe>[Probed·Read-Only]</color> ");
                labelText = roTag + labelText;
                if (!string.IsNullOrEmpty(hotkey.Tooltip))
                {
                    string infoLink = "modkey:" + hotkey.Id;
                    TMPIconHover.CustomLinks[infoLink] = () => hotkey.Tooltip;
                    labelText += $" <link=\"{infoLink}\"><sprite name=\"info\" tint=1></link>";
                }
                if (!string.IsNullOrEmpty(conflictTip))
                {
                    string link = "conflict:" + hotkey.Id;
                    TMPIconHover.CustomLinks[link] = () => conflictTip;
                    labelText += $" <link=\"{link}\"><color=#ffb347><b>[!]</b></color></link>";
                }
                return labelText;
            }

            if (!string.IsNullOrEmpty(conflictTip))
            {
                string link = "conflict:" + hotkey.Id;
                TMPIconHover.CustomLinks[link] = () => conflictTip;
                labelText += $" <link=\"{link}\"><color=#ffb347><b>[!]</b></color></link>";
            }
            else if (!string.IsNullOrEmpty(hotkey.Tooltip))
            {
                string link = "modkey:" + hotkey.Id;
                TMPIconHover.CustomLinks[link] = () => hotkey.Tooltip;
                labelText += $" <link=\"{link}\"><sprite name=\"info\" tint=1></link>";
            }
            return labelText;
        }

        // =====================================================================
        // 冲突专属速览视图构建与动态重建
        // =====================================================================
        private static void RebuildConflictView()
        {
            if (!IsConflictViewActive || _conflictContentRoot == null || _currentWindow == null || CustomUIManager.Instance == null) return;
            var rt = _conflictContentRoot.GetComponent<RectTransform>();
            if (rt == null) return;

            if (!_conflictViewDirty && rt.childCount > 0)
            {
                return;
            }

            foreach (var hk in _allHotkeys)
            {
                hk.ConflictButtonElement = null;
            }

            for (int i = rt.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(rt.GetChild(i).gameObject);
            }

            using (ComboKeyStore.EnterBypass())
            {
                var conflictBuilder = new CustomUIBuilder(CustomUIManager.Instance, _currentWindow, rt);
                BuildConflictViewContent(conflictBuilder);
            }
            _conflictViewDirty = false;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        private static void BuildConflictViewContent(CustomUIBuilder builder)
        {
            var sortedConflicts = ConflictDetector.GetSortedConflictGroups(_conflicts);

            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            builder.AddSpace(8f);
            string headerText = sortedConflicts.Count > 0
                ? I18n.Format(
                    "<color=#ffb347><b>当前共有 {0} 组按键发生冲突！</b></color> (点击下方按键可直接改绑，按 Esc 设为空)",
                    "<color=#ffb347><b>{0} hotkey conflict group(s) detected!</b></color> (Click any key below to rebind, Esc to clear)",
                    sortedConflicts.Count)
                : I18n.Get(
                    "<color=#55dd88><b>太棒了！当前没有任何按键冲突。</b></color>",
                    "<color=#55dd88><b>Great! No hotkey conflicts found.</b></color>");
            builder.AddHeader(headerText, "header_conflict_status");
            I18n.ApplyFontIfRuntime(builder.Last?.text, headerText);
            builder.AddSpace(8f);
            builder.End();
            builder.AddSeparator(null, 2);

            foreach (var cg in sortedConflicts)
            {
                string keyName = ComboKeyStore.FormatCombo(cg.Combo.Modifiers, cg.Combo.Key);
                builder.BeginRow(4f, TextAnchor.MiddleLeft);
                builder.AddSpace(8f);
                string cgHdrText = I18n.Format(
                    "<b>【 冲突按键: <color=#ffb347>{0}</color> 】</b> (共 {1} 个功能重叠)",
                    "<b>[ Conflict Key: <color=#ffb347>{0}</color> ]</b> ({1} bindings overlap)",
                    keyName,
                    cg.ConflictingEntries.Count);
                builder.AddLabel(cgHdrText, $"cg_header_{(int)cg.Combo.Modifiers}_{(int)cg.Combo.Key}");
                I18n.ApplyFontIfRuntime(builder.Last?.text, cgHdrText);
                builder.End();

                foreach (var entry in cg.ConflictingEntries)
                {
                    builder.BeginRow(6f, TextAnchor.MiddleLeft);
                    builder.AddSpace(20f);
                    string rowLabel = entry.IsReadOnlyProbed
                        ? I18n.Get("<color=#d8b4fe>[探测·只读]</color> ", "<color=#d8b4fe>[Probed·Read-Only]</color> ") + $"[{entry.ModDisplayName}]  {entry.DisplayName}"
                        : $"[{entry.ModDisplayName}]  {entry.DisplayName}";
                    if (entry.IsReadOnlyProbed && !string.IsNullOrEmpty(entry.Tooltip))
                    {
                        string infoLink = "cg_modkey:" + entry.Id;
                        TMPIconHover.CustomLinks[infoLink] = () => entry.Tooltip;
                        rowLabel += $" <link=\"{infoLink}\"><sprite name=\"info\" tint=1></link>";
                    }
                    builder.AddLabel(rowLabel, "cg_label_" + entry.Id);
                    I18n.ApplyFontIfRuntime(builder.Last?.text, rowLabel);
                    builder.AddFlexibleSpace();

                    builder.AddButton(FormatHotkeyComboText(entry), () =>
                    {
                        if (!entry.IsReadOnlyProbed)
                        {
                            StartCaptureHotkey(entry);
                        }
                    }, "cg_btn_" + entry.Id);

                    var btn = builder.Last;
                    StyleHotkeyButton(btn);
                    if (entry.IsReadOnlyProbed && btn?.button != null)
                    {
                        btn.button.interactable = false;
                    }
                    entry.ConflictButtonElement = btn;
                    if (btn != null) ApplySingleButtonColor(btn, entry.EffectiveCombo, entry.IsReadOnlyProbed);

                    builder.AddSpace(10f);
                    builder.End();
                }

                builder.AddSpace(6f);
                builder.AddSeparator(null, 1);
            }
        }

        // =====================================================================
        // 统一增强按键捕获状态机（原版 + 模组共用，支持组合键 / 鼠标侧键 / Esc解绑）
        // =====================================================================
        private static readonly KeyCode[] TrackedModifierKeys = new KeyCode[]
        {
            KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt
        };

        public static bool StartCaptureVanillaOption(GameOptionUIManager manager, string optionId)
        {
            if (_currentWindow == null || _currentManager != manager || string.IsNullOrEmpty(optionId))
            {
                return false;
            }

            var vanillaEntry = _allHotkeys.FirstOrDefault(h =>
                h.ModId == "vanilla" && string.Equals(h.RawOptionId, optionId, StringComparison.Ordinal));
            if (vanillaEntry == null)
            {
                return false;
            }

            StartCaptureHotkey(vanillaEntry);
            return true;
        }

        private static string CapturePrompt => I18n.Get("请按键... (Esc=无)", "Press key... (Esc=None)");

        private static void StartCaptureHotkey(ModHotkeyEntry entry)
        {
            if (_suppressSameEntryRestart && _lastEndedCaptureEntry == entry)
            {
                return;
            }

            if (_capturingEntry != null)
            {
                UpdateHotkeyButtonVisual(_capturingEntry);
            }

            KeyBindHelper.StopCapture();

            if (_capturingEntry == null && _currentWindow != null)
            {
                _closeOnEscapeBeforeCapture = _currentWindow.closeOnEscape;
                _currentWindow.closeOnEscape = false;
            }

            _capturingEntry = entry;
            _capturingStartedFrame = Time.frameCount;
            _restoreCloseOnEscapeFrame = -1;
            _waitingMouseRelease = Input.GetMouseButton(0) || Input.GetMouseButton(1);
            _pendingStandaloneModifier = KeyCode.None;
            _hadMultipleModifiers = false;

            SetCapturingEntryDisplayText(entry, CapturePrompt);

            if (_currentManager != null)
            {
                string statusPrompt = entry.SupportsModifierCombos
                    ? I18n.Format(
                        "正在为 [{0}: {1}] 绑定按键 (支持组合键与鼠标侧键；按 Esc 设为空，左键取消)",
                        "Binding [{0}: {1}] (combos & side buttons supported; Esc to clear, Left click to cancel)",
                        entry.ModDisplayName, entry.DisplayName)
                    : I18n.Format(
                        "正在为 [{0}: {1}] 绑定按键 (支持单键与鼠标侧键；按 Esc 设为空，左键取消；此模组暂不支持组合键前缀)",
                        "Binding [{0}: {1}] (single key & side buttons; Esc to clear, Left click to cancel; no modifier combos)",
                        entry.ModDisplayName, entry.DisplayName);
                SetKeyBindStatusDelegate?.Invoke(_currentManager, statusPrompt);
            }
        }

        private static readonly Color CapturePromptColor = new Color(0.4f, 0.8f, 1f, 1f);

        private static void SetCapturingEntryDisplayText(ModHotkeyEntry entry, string text)
        {
            if (entry.ButtonElement != null)
            {
                SetButtonTextFast(entry.ButtonElement, text);
                ApplyButtonTextTheme(entry.ButtonElement, CapturePromptColor);
            }
            if (entry.ConflictButtonElement != null)
            {
                SetButtonTextFast(entry.ConflictButtonElement, text);
                ApplyButtonTextTheme(entry.ConflictButtonElement, CapturePromptColor);
            }
            if (entry.ModId == "vanilla" && !string.IsNullOrEmpty(entry.RawOptionId))
            {
                var vanillaEl = _currentWindow?.Get(entry.RawOptionId);
                if (vanillaEl != null)
                {
                    SetButtonTextFast(vanillaEl, text);
                    ApplyButtonTextTheme(vanillaEl, CapturePromptColor);
                }
            }
        }

        /// <summary>
        /// 窗口开启后的按帧预算后台静默预建：在约 7.5ms/帧 的平滑预算内于折叠态(inactive)容器连续构建多张卡片，
        /// 既不在开启首帧产生压力，又能更快预热全量卡片，使“全部展开”与搜索秒开且完全无过渡动画。
        /// </summary>
        private static void PrebuildNextCollapsedModCard()
        {
            if (_currentWindow == null || IsConflictViewActive || IsDiagnosticViewActive || _capturingEntry != null || _pendingScrollTarget != null || !string.IsNullOrWhiteSpace(_searchQuery))
            {
                return;
            }
            if (_openedFrameCount < 0 || Time.frameCount <= _openedFrameCount)
            {
                return;
            }

            PrebuildStopwatch.Restart();
            while (_prebuildModIndex < _modGroups.Count)
            {
                var group = _modGroups[_prebuildModIndex++];
                if (group.IsContentBuilt || group.ContentContainerObject == null || group.ContentContainerObject.activeSelf)
                {
                    continue;
                }

                EnsureGroupContentBuilt(group);

                if (PrebuildStopwatch.Elapsed.TotalMilliseconds >= 7.5)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 每帧由 Patch_GameOptionUIManager_Update 驱动模组/原版快捷键捕获、静默预建、安全 Esc 恢复与搜索延迟滚动
        /// </summary>
        public static void TickCapture()
        {
            // 搜索 / 跨视图跳转后跨帧滚动校准：等待至下一帧 Canvas 提交后再执行多次重排定位，防止点击当帧布局尚未刷新而提前清空
            if (_pendingScrollTarget != null && _currentWindow != null)
            {
                if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f || IsConflictViewActive || IsDiagnosticViewActive)
                {
                    ClearPendingScroll();
                }
                else if (Time.frameCount >= _pendingScrollMinFrame)
                {
                    var target = _pendingScrollTarget;
                    ScrollToTargetImmediate(target);
                    _pendingScrollRemainingPasses--;
                    if (_pendingScrollRemainingPasses <= 0)
                    {
                        ClearPendingScroll();
                    }
                }
            }

            // 闲置时每帧在折叠容器内悄然预建 1 张未建卡片（无动画、零重建开销）
            PrebuildNextCollapsedModCard();

            // 鼠标组合键完成/取消后的同键防重触发窗口释放检测
            if (_suppressSameEntryRestart)
            {
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
                {
                    if (_suppressRestartReleaseFrame < 0)
                    {
                        _suppressRestartReleaseFrame = Time.frameCount + 2;
                    }
                    else if (Time.frameCount >= _suppressRestartReleaseFrame)
                    {
                        _suppressSameEntryRestart = false;
                        _lastEndedCaptureEntry = null;
                        _suppressRestartReleaseFrame = -1;
                    }
                }
            }

            // 延迟恢复关闭窗口的 Esc 权限，防止解绑当帧窗口意外关闭
            if (_capturingEntry == null && _restoreCloseOnEscapeFrame >= 0 && Time.frameCount >= _restoreCloseOnEscapeFrame)
            {
                if (_currentWindow != null)
                {
                    _currentWindow.closeOnEscape = _closeOnEscapeBeforeCapture;
                }
                _restoreCloseOnEscapeFrame = -1;
            }

            if (_capturingEntry == null) return;
            if (Time.frameCount <= _capturingStartedFrame) return;

            if (_waitingMouseRelease)
            {
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) return;
                _waitingMouseRelease = false;
            }

            bool allowCombos = _capturingEntry.SupportsModifierCombos;
            KeyModifierMask heldModifiers = allowCombos ? ComboKeyStore.GetHeldModifiersMask() : KeyModifierMask.None;

            // 1. 仅 Esc 用于解除绑定（清空为无），Backspace 与 Delete 均作为普通可绑定按键
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ConsumedEscapeFrameField?.SetValue(null, Time.frameCount);
                FinishCapture(KeyModifierMask.None, KeyCode.None, fromMouseClick: false);
                return;
            }

            // 2. 鼠标按键优先轮询（绕过 Input.anyKeyDown 遗漏侧键问题，并支持 Alt+左键 等鼠标组合键）
            if (Input.GetMouseButtonDown(0))
            {
                if (heldModifiers == KeyModifierMask.None)
                {
                    CancelCapture(fromMouseClick: true);
                }
                else
                {
                    FinishCapture(heldModifiers, KeyCode.Mouse0, fromMouseClick: true);
                }
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                if (heldModifiers == KeyModifierMask.None)
                {
                    CancelCapture(fromMouseClick: true);
                }
                else
                {
                    FinishCapture(heldModifiers, KeyCode.Mouse1, fromMouseClick: true);
                }
                return;
            }

            for (int btn = 2; btn <= 6; btn++)
            {
                KeyCode mk = KeyCode.Mouse0 + btn;
                if (ComboKeyStore.IsKeyDownRaw(mk))
                {
                    FinishCapture(heldModifiers, mk, fromMouseClick: false);
                    return;
                }
            }

            // 3. 修饰键追踪（允许按住 Shift/Ctrl/Alt 等等待主键，或单击松开绑为独立修饰键）
            foreach (KeyCode modKey in TrackedModifierKeys)
            {
                if (Input.GetKeyDown(modKey))
                {
                    if (_pendingStandaloneModifier == KeyCode.None)
                    {
                        _pendingStandaloneModifier = modKey;
                        _hadMultipleModifiers = false;
                    }
                    else if (_pendingStandaloneModifier != modKey)
                    {
                        _hadMultipleModifiers = true;
                    }
                }
            }

            if (((int)heldModifiers & ((int)heldModifiers - 1)) != 0)
            {
                _hadMultipleModifiers = true;
            }

            // 4. 常规非修饰键盘主键轮询（使用静态预筛选键值表，零 GC 分配）
            if (Input.anyKeyDown)
            {
                for (int i = 0; i < BindableKeyboardKeys.Length; i++)
                {
                    KeyCode kc = BindableKeyboardKeys[i];
                    if (Input.GetKeyDown(kc))
                    {
                        KeyModifierMask finalMods = allowCombos ? ComboKeyStore.GetHeldModifiersMask(kc) : KeyModifierMask.None;
                        FinishCapture(finalMods, kc, fromMouseClick: false);
                        return;
                    }
                }
            }

            // 5. 若按住修饰键后未按主键而松开：仅单修饰键点按才回落绑定该修饰键自身（保留如 LeftAlt 单独快移）
            if (_pendingStandaloneModifier != KeyCode.None && Input.GetKeyUp(_pendingStandaloneModifier))
            {
                KeyCode standalone = _pendingStandaloneModifier;
                _pendingStandaloneModifier = KeyCode.None;

                KeyModifierMask afterMods = allowCombos ? ComboKeyStore.GetHeldModifiersMask() : KeyModifierMask.None;
                if (!_hadMultipleModifiers && afterMods == KeyModifierMask.None)
                {
                    FinishCapture(KeyModifierMask.None, standalone, fromMouseClick: false);
                    return;
                }

                if (afterMods == KeyModifierMask.None)
                {
                    _hadMultipleModifiers = false;
                    SetCapturingEntryDisplayText(_capturingEntry, CapturePrompt);
                }
                else
                {
                    SetCapturingEntryDisplayText(_capturingEntry, ComboKeyStore.FormatModifiers(afterMods) + "...");
                }
                return;
            }

            // 实时显示当前按住的修饰键前缀
            if (heldModifiers != KeyModifierMask.None)
            {
                SetCapturingEntryDisplayText(_capturingEntry, ComboKeyStore.FormatModifiers(heldModifiers) + "...");
            }
            else if (_pendingStandaloneModifier == KeyCode.None)
            {
                SetCapturingEntryDisplayText(_capturingEntry, CapturePrompt);
            }
        }

        private static void CancelCapture(bool fromMouseClick = false)
        {
            if (_capturingEntry == null) return;
            var entry = _capturingEntry;
            _capturingEntry = null;
            _pendingStandaloneModifier = KeyCode.None;
            _hadMultipleModifiers = false;
            _restoreCloseOnEscapeFrame = Time.frameCount + 2;

            if (fromMouseClick)
            {
                _lastEndedCaptureEntry = entry;
                _suppressSameEntryRestart = true;
                _suppressRestartReleaseFrame = -1;
            }

            using (ComboKeyStore.EnterBypass())
            {
                UpdateHotkeyButtonVisual(entry);
                SyncVanillaKeyButtonColors();
            }
            if (_currentManager != null)
            {
                SetKeyBindStatusDelegate?.Invoke(_currentManager, string.Empty);
            }
        }

        private static void FinishCapture(KeyModifierMask newMods, KeyCode newKey, bool fromMouseClick)
        {
            if (_capturingEntry == null) return;

            var entry = _capturingEntry;
            if (!entry.SupportsModifierCombos) newMods = KeyModifierMask.None;
            entry.PendingModifiers = newKey == KeyCode.None ? KeyModifierMask.None : newMods;
            entry.PendingKey = newKey;
            entry.HasPendingChange = true;

            // 若捕获的是原版官方按键，立即同步主键到原版挂起字典
            if (entry.ModId == "vanilla" && _currentManager != null && !string.IsNullOrEmpty(entry.RawOptionId))
            {
                if (PendingKeysField?.GetValue(_currentManager) is Dictionary<string, KeyCode> pendingKeys)
                {
                    pendingKeys[entry.RawOptionId] = newKey;
                }
            }

            _capturingEntry = null;
            _pendingStandaloneModifier = KeyCode.None;
            _hadMultipleModifiers = false;
            _restoreCloseOnEscapeFrame = Time.frameCount + 2;

            if (fromMouseClick)
            {
                _lastEndedCaptureEntry = entry;
                _suppressSameEntryRestart = true;
                _suppressRestartReleaseFrame = -1;
            }

            RefreshConflicts();
            UpdateHotkeyButtonVisual(entry);
            if (_currentManager != null)
            {
                SetKeyBindStatusDelegate?.Invoke(_currentManager, string.Empty);
            }
        }

        // =====================================================================
        // 搜索过滤与作用域切换 (支持 整体 / 按键+常规 / 仅按键 / 仅常规)
        // =====================================================================
        private static void OnSearchScopeChanged(int index)
        {
            if (index >= 0 && index <= (int)SearchScope.GeneralOnly)
            {
                _searchScope = (SearchScope)index;
                if (IsConflictViewActive || IsDiagnosticViewActive)
                {
                    IsConflictViewActive = false;
                    IsDiagnosticViewActive = false;
                    SyncViewContainersAndToggleButtons();
                }
                ApplySearchFilter();
            }
        }

        private static void OnSearchTyping(string text)
        {
            OnSearchInputUpdated(text);
        }

        private static void OnSearchChanged(string? text)
        {
            string safeText = text ?? string.Empty;
            // onValueChanged (OnSearchTyping) 已即时同步过 _searchQuery；失焦触发 onEndEdit 时若文本未变直接返回，防止点击按钮前被整页清空重建吞掉 onClick
            if (string.Equals(safeText, _searchQuery ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }
            OnSearchInputUpdated(safeText);
        }

        private static void OnSearchInputUpdated(string text)
        {
            _searchQuery = text;
            if (IsDiagnosticViewActive)
            {
                _diagnosticViewDirty = true;
                RebuildDiagnosticView();
                ResetScrollToTop();
                return;
            }
            if (IsConflictViewActive && !string.IsNullOrWhiteSpace(text))
            {
                ToggleConflictMode();
            }
            ApplySearchFilter();
        }

        private static GameObject? _lastSearchScrollTarget;

        private static readonly List<string> _searchKeyTokensScratch = new List<string>(8);
        private static readonly List<string> _searchQueryTokensScratch = new List<string>(8);
        private static readonly System.Text.StringBuilder _searchConcatScratch = new System.Text.StringBuilder(32);

        private static bool IsHotkeyMatchQuery(ModHotkeyEntry hotkey, string query)
        {
            if (PinyinEngine.Matches(hotkey.DisplayName, query))
            {
                return true;
            }
            if (hotkey.IsReadOnlyProbed && !string.IsNullOrEmpty(hotkey.ProbedSourceDetail) &&
                PinyinEngine.Matches(hotkey.ProbedSourceDetail, query))
            {
                return true;
            }

            KeyCode effKey = hotkey.EffectiveKey;
            if (effKey == KeyCode.None)
            {
                return MatchesFormattedKeyText(FormatHotkeyComboText(hotkey), query);
            }

            if (MatchesKeyCodeSearchToken(effKey, query))
            {
                return true;
            }

            string comboText = FormatHotkeyComboText(hotkey);
            if (MatchesFormattedKeyText(comboText, query))
            {
                return true;
            }

            if (hotkey.EffectiveModifiers != KeyModifierMask.None &&
                MatchesFormattedKeyText(ComboKeyStore.FormatPrimaryKeyName(effKey), query))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 对格式化按键文本执行词元边界前缀/完整组合匹配（中文走拼音匹配，英文/数字禁止词中子串如 "ow"->"Arrow" / "on"->"None" 误命中）
        /// </summary>
        private static bool MatchesFormattedKeyText(string? keyText, string query)
        {
            if (string.IsNullOrWhiteSpace(keyText))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            string qTrim = query.Trim();
            bool keyHasNonAscii = false;
            System.Text.StringBuilder? cjkOnly = null;
            for (int i = 0; i < keyText.Length; i++)
            {
                char ch = keyText[i];
                if (ch > 127)
                {
                    keyHasNonAscii = true;
                    cjkOnly ??= new System.Text.StringBuilder(keyText.Length);
                    cjkOnly.Append(ch);
                }
            }

            if (keyHasNonAscii)
            {
                // 仅将非 ASCII/中文部分送入拼音子串匹配，防止 "Ctrl + 小键盘6" 中的 ASCII 词中子串误触发
                if (cjkOnly != null && PinyinEngine.Matches(cjkOnly.ToString(), qTrim))
                {
                    return true;
                }
                if (PinyinEngine.Matches(keyText, qTrim) && qTrim.IndexOf(' ') >= 0)
                {
                    return true;
                }
            }

            TokenizeAsciiSearchTokens(keyText, _searchKeyTokensScratch);
            TokenizeAsciiSearchTokens(qTrim, _searchQueryTokensScratch);

            if (_searchKeyTokensScratch.Count == 0 || _searchQueryTokensScratch.Count == 0)
            {
                return string.Equals(keyText.Trim(), qTrim, StringComparison.OrdinalIgnoreCase);
            }

            if (_searchQueryTokensScratch.Count == 1)
            {
                string qt = _searchQueryTokensScratch[0];
                for (int i = 0; i < _searchKeyTokensScratch.Count; i++)
                {
                    string kt = _searchKeyTokensScratch[i];
                    if (qt.Length == 1)
                    {
                        if (string.Equals(kt, qt, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                    else if (kt.StartsWith(qt, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                // 允许如 "numpad6" / "downarrow" / "mouse1" 从词元起点连写命中 "Numpad 6" / "Down Arrow" / "Mouse 1"
                if (qt.Length >= 2)
                {
                    return MatchesConcatenatedFromTokenBoundary(_searchKeyTokensScratch, qt);
                }
                return false;
            }

            // 多词元组合查询（如 "ctrl+c" / "numpad 6" / "left ctrl"）
            int qCount = _searchQueryTokensScratch.Count;
            int kCount = _searchKeyTokensScratch.Count;
            for (int start = 0; start <= kCount - qCount; start++)
            {
                bool matched = true;
                for (int j = 0; j < qCount; j++)
                {
                    string kt = _searchKeyTokensScratch[start + j];
                    string qt = _searchQueryTokensScratch[j];
                    bool isLast = j == qCount - 1;
                    bool ok = isLast && qt.Length >= 2
                        ? kt.StartsWith(qt, StringComparison.Ordinal)
                        : string.Equals(kt, qt, StringComparison.Ordinal);
                    if (!ok)
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                {
                    return true;
                }
            }

            string compactQuery = string.Concat(_searchQueryTokensScratch);
            return MatchesConcatenatedFromTokenBoundary(_searchKeyTokensScratch, compactQuery);
        }

        private static bool MatchesConcatenatedFromTokenBoundary(List<string> kTokens, string compactQuery)
        {
            if (string.IsNullOrEmpty(compactQuery))
            {
                return false;
            }
            var sb = _searchConcatScratch;
            for (int start = 0; start < kTokens.Count; start++)
            {
                sb.Length = 0;
                for (int end = start; end < kTokens.Count; end++)
                {
                    sb.Append(kTokens[end]);
                    if (sb.Length >= compactQuery.Length)
                    {
                        break;
                    }
                }
                if (sb.Length >= compactQuery.Length)
                {
                    string joined = sb.ToString();
                    if (compactQuery.Length == 1
                        ? string.Equals(joined, compactQuery, StringComparison.Ordinal)
                        : joined.StartsWith(compactQuery, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void TokenizeAsciiSearchTokens(string text, List<string> output)
        {
            output.Clear();
            int len = text.Length;
            int tokenStart = -1;

            for (int i = 0; i < len; i++)
            {
                char ch = text[i];
                bool isAlpha = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
                bool isDigit = ch >= '0' && ch <= '9';
                bool isWordChar = isAlpha || isDigit;

                if (!isWordChar)
                {
                    if (tokenStart >= 0)
                    {
                        output.Add(text.Substring(tokenStart, i - tokenStart).ToLowerInvariant());
                        tokenStart = -1;
                    }
                    // 对孤立运算符号（如 "Numpad +" 的末尾 '+'），保留为词元；跳过组合键中的 " + "
                    if ((ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '.') &&
                        (i == 0 || char.IsWhiteSpace(text[i - 1])) &&
                        i == len - 1)
                    {
                        output.Add(ch.ToString());
                    }
                    continue;
                }

                if (tokenStart < 0)
                {
                    tokenStart = i;
                    continue;
                }

                char prev = text[i - 1];
                bool prevLower = prev >= 'a' && prev <= 'z';
                bool curUpper = ch >= 'A' && ch <= 'Z';
                if (prevLower && curUpper)
                {
                    output.Add(text.Substring(tokenStart, i - tokenStart).ToLowerInvariant());
                    tokenStart = i;
                }
            }

            if (tokenStart >= 0)
            {
                output.Add(text.Substring(tokenStart, len - tokenStart).ToLowerInvariant());
            }
        }

        /// <summary>
        /// 按键枚举名词根前缀 / 常用别名精确匹配，避免 KeyCode.Keypad6 等原始枚举名被 "yp" 等词中子串误命中
        /// </summary>
        private static bool MatchesKeyCodeSearchToken(KeyCode key, string query)
        {
            if (key == KeyCode.None || string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            string q = query.Trim();

            // 1. 常用玩家可读别名（小键盘 / 侧键 / 修饰键简称）
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                int digit = (int)key - (int)KeyCode.Keypad0;
                if (IsQueryPrefixOrAlias(q, "keypad" + digit) ||
                    IsQueryPrefixOrAlias(q, "kp" + digit) ||
                    IsQueryPrefixOrAlias(q, "numpad" + digit) ||
                    IsQueryPrefixOrAlias(q, "numpad " + digit) ||
                    IsQueryPrefixOrAlias(q, "num" + digit) ||
                    IsQueryPrefixOrAlias(q, "小键盘" + digit) ||
                    IsQueryPrefixOrAlias(q, "xjp" + digit))
                {
                    return true;
                }
            }
            else if (key >= KeyCode.KeypadPeriod && key <= KeyCode.KeypadEquals)
            {
                if (IsQueryPrefixOrAlias(q, "keypad") ||
                    IsQueryPrefixOrAlias(q, "kp") ||
                    IsQueryPrefixOrAlias(q, "numpad") ||
                    IsQueryPrefixOrAlias(q, "小键盘") ||
                    IsQueryPrefixOrAlias(q, "xjp"))
                {
                    return true;
                }
            }
            else if (ComboKeyStore.IsMouseSideButton(key))
            {
                int sideIndex = (int)key - (int)KeyCode.Mouse3 + 1;
                if (IsQueryPrefixOrAlias(q, "mouse" + ((int)key - (int)KeyCode.Mouse0)) ||
                    IsQueryPrefixOrAlias(q, "mouse side " + sideIndex) ||
                    IsQueryPrefixOrAlias(q, "side " + sideIndex) ||
                    IsQueryPrefixOrAlias(q, "鼠标侧键" + sideIndex) ||
                    IsQueryPrefixOrAlias(q, "侧键" + sideIndex))
                {
                    return true;
                }
            }
            else if (key == KeyCode.LeftControl || key == KeyCode.RightControl)
            {
                if (IsQueryPrefixOrAlias(q, "ctrl") ||
                    IsQueryPrefixOrAlias(q, key == KeyCode.LeftControl ? "lctrl" : "rctrl"))
                {
                    return true;
                }
            }
            else if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
            {
                if (IsQueryPrefixOrAlias(q, "shift") ||
                    IsQueryPrefixOrAlias(q, key == KeyCode.LeftShift ? "lshift" : "rshift"))
                {
                    return true;
                }
            }
            else if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt)
            {
                if (IsQueryPrefixOrAlias(q, "alt") ||
                    IsQueryPrefixOrAlias(q, key == KeyCode.LeftAlt ? "lalt" : "ralt"))
                {
                    return true;
                }
            }

            // 2. 原始枚举名仅允许从开头或 PascalCase/数字词元起点匹配（>=2 字符允许前缀，单字符需精准匹配完整词元）
            string raw = key.ToString();
            if (q.Length >= 2 && raw.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int tokenStart = 0;
            for (int i = 1; i <= raw.Length; i++)
            {
                bool atBoundary = i == raw.Length ||
                                  char.IsUpper(raw[i]) ||
                                  (char.IsDigit(raw[i]) != char.IsDigit(raw[i - 1]));
                if (!atBoundary) continue;

                int tokenLen = i - tokenStart;
                if (tokenLen > 0)
                {
                    bool matched = q.Length >= 2
                        ? (tokenLen >= q.Length && string.Compare(raw, tokenStart, q, 0, q.Length, StringComparison.OrdinalIgnoreCase) == 0)
                        : (tokenLen == 1 && char.ToUpperInvariant(raw[tokenStart]) == char.ToUpperInvariant(q[0]));
                    if (matched)
                    {
                        return true;
                    }
                }
                tokenStart = i;
            }

            return false;
        }

        private static bool IsQueryPrefixOrAlias(string query, string alias)
        {
            if (query.Length >= 2)
            {
                return alias.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(query, alias, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(query, alias, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGeneralMatchQuery(ModGeneralEntry gen, string query)
        {
            return PinyinEngine.Matches(gen.DisplayName, query);
        }

        private static bool IsVanillaItemMatchQuery(VanillaOptionItem item, string query)
        {
            if (item.IsKeyBind && item.LinkedHotkey != null)
            {
                return IsHotkeyMatchQuery(item.LinkedHotkey, query) ||
                       PinyinEngine.Matches(item.DisplayName, query);
            }
            return PinyinEngine.Matches(item.DisplayName, query);
        }

        private static void SetObjectActive(GameObject? obj, bool active)
        {
            if (obj != null && obj.activeSelf != active)
            {
                obj.SetActive(active);
            }
        }

        private static void SetCardHiddenForSearch(ICollapsibleOptionCard card)
        {
            SetObjectActive(card.HeaderRowObject, false);
            SetObjectActive(card.ContentContainerObject, false);
            SetObjectActive(card.OuterSeparatorObject, false);
        }

        private static void ShowCardExpandedVisualOnly(ICollapsibleOptionCard card)
        {
            SetObjectActive(card.ContentContainerObject, true);
            if (card.FoldArrowElement != null && (card.FoldArrowElement.text == null || card.FoldArrowElement.text.text != "▼"))
            {
                card.FoldArrowElement.SetText("▼");
            }
            if (card.ResetButtonElement != null)
            {
                bool showReset = card.ResetToDefaultsAction != null;
                if (card.ResetButtonElement.root == null || card.ResetButtonElement.root.activeSelf != showReset)
                {
                    card.ResetButtonElement.SetVisible(showReset);
                }
            }
        }

        private static void ApplySearchFilter()
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(_searchQuery);
            bool allowGroupName = _searchScope == SearchScope.All;
            bool allowHotkeys = _searchScope == SearchScope.All || _searchScope == SearchScope.HotkeysAndGeneral || _searchScope == SearchScope.HotkeysOnly;
            bool allowGeneral = _searchScope == SearchScope.All || _searchScope == SearchScope.HotkeysAndGeneral || _searchScope == SearchScope.GeneralOnly;

            _lastSearchScrollTarget = null;

            GameObject? firstMatchedHeader = null;
            int visibleVanillaCount = 0;
            int visibleModCount = 0;

            WithSuspendedRootLayout(() =>
            {
                using (ComboKeyStore.EnterBypass())
                {
                    // 1. 过滤原版官方设置区块（支持四作用域搜索、繁简/拼音/按键名匹配与自动展开）
                    foreach (var card in _vanillaCards)
                    {
                        if (!hasQuery)
                        {
                            foreach (var item in card.Entries)
                            {
                                foreach (var row in item.RowObjects) SetObjectActive(row, true);
                            }
                            SetObjectActive(card.HeaderRowObject, true);
                            SetObjectActive(card.OuterSeparatorObject, true);
                            bool savedExpanded = VanillaExpandedStates.TryGetValue(card.SectionId, out bool saved) ? saved : true;
                            card.SetExpanded(savedExpanded);
                            visibleVanillaCount++;
                            continue;
                        }

                        bool sectionNameMatch = allowGroupName &&
                            (PinyinEngine.Matches(card.DisplayName, _searchQuery) ||
                             PinyinEngine.Matches(card.SectionId, _searchQuery));

                        bool anyItemMatch = false;
                        foreach (var item in card.Entries)
                        {
                            bool scopeAllowed = item.IsKeyBind ? allowHotkeys : allowGeneral;
                            bool itemMatch = sectionNameMatch || (scopeAllowed && IsVanillaItemMatchQuery(item, _searchQuery));
                            foreach (var row in item.RowObjects) SetObjectActive(row, itemMatch);
                            if (itemMatch) anyItemMatch = true;
                        }

                        bool cardVisible = sectionNameMatch || anyItemMatch;
                        if (!cardVisible)
                        {
                            SetCardHiddenForSearch(card);
                            continue;
                        }

                        SetObjectActive(card.HeaderRowObject, true);
                        SetObjectActive(card.OuterSeparatorObject, true);
                        ShowCardExpandedVisualOnly(card);
                        visibleVanillaCount++;

                        if (firstMatchedHeader == null && card.HeaderRowObject != null)
                        {
                            firstMatchedHeader = card.HeaderRowObject;
                        }
                    }

                    // 2. 过滤创意工坊模组设置卡片（挂起根布局下同步完成，绝不产生多帧依次展开过渡动画）
                    foreach (var group in _modGroups)
                    {
                        if (!hasQuery)
                        {
                            if (group.IsContentBuilt)
                            {
                                foreach (var hotkey in group.Hotkeys) SetObjectActive(hotkey.RowObject, true);
                                foreach (var gen in group.GeneralSettings) SetObjectActive(gen.RowObject, true);
                                foreach (var probed in group.ReadOnlyProbedHotkeys) SetObjectActive(probed.RowObject, true);
                                if (CardParts.TryGetValue(group, out var parts))
                                {
                                    SetObjectActive(parts.hkHeader, group.Hotkeys.Count > 0);
                                    SetObjectActive(parts.genHeader, group.GeneralSettings.Count > 0);
                                    SetObjectActive(parts.probedHeader, group.ReadOnlyProbedHotkeys.Count > 0);
                                    SetObjectActive(parts.midSep, group.Hotkeys.Count > 0 && group.GeneralSettings.Count > 0);
                                    SetObjectActive(parts.probedSep, (group.Hotkeys.Count > 0 || group.GeneralSettings.Count > 0) && group.ReadOnlyProbedHotkeys.Count > 0);
                                }
                            }

                            SetObjectActive(group.HeaderRowObject, true);
                            SetObjectActive(group.OuterSeparatorObject, true);
                            group.SetExpanded(group.IsExpanded);
                            visibleModCount++;
                            continue;
                        }

                        bool groupNameMatch = IsModGroupNameMatch(group, _searchQuery, allowGroupName);
                        bool anyHotkeyMatch = allowHotkeys &&
                            (group.Hotkeys.Any(hk => IsHotkeyMatchQuery(hk, _searchQuery)) ||
                             group.ReadOnlyProbedHotkeys.Any(hk => IsHotkeyMatchQuery(hk, _searchQuery)));
                        bool anyGeneralMatch = allowGeneral && group.GeneralSettings.Any(gen => IsGeneralMatchQuery(gen, _searchQuery));

                        if (!groupNameMatch && !anyHotkeyMatch && !anyGeneralMatch)
                        {
                            SetCardHiddenForSearch(group);
                            continue;
                        }

                        visibleModCount++;
                        SetObjectActive(group.HeaderRowObject, true);
                        SetObjectActive(group.OuterSeparatorObject, true);

                        if (firstMatchedHeader == null && group.HeaderRowObject != null)
                        {
                            firstMatchedHeader = group.HeaderRowObject;
                        }

                        if (!group.IsContentBuilt)
                        {
                            EnsureGroupContentBuilt(group);
                        }
                        ApplyMatchedModGroupRows(group, groupNameMatch, allowHotkeys, allowGeneral);
                    }
                }

                // 3. 联动显隐区域标题栏与空结果提示
                bool showVanillaBanner = !hasQuery ? _vanillaCards.Count > 0 : visibleVanillaCount > 0;
                bool showModBanner = !hasQuery ? _modGroups.Count > 0 : visibleModCount > 0;
                SetObjectActive(_vanillaHeaderRow, showVanillaBanner);
                SetObjectActive(_vanillaHeaderSep, showVanillaBanner);
                SetObjectActive(_modHeaderSpace, showVanillaBanner && showModBanner);
                SetObjectActive(_modHeaderSep, showModBanner);
                SetObjectActive(_modHeaderRow, showModBanner);
                SetObjectActive(_modHeaderBottomSep, showModBanner);
                SetObjectActive(_emptySearchLabelRow, hasQuery && visibleVanillaCount == 0 && visibleModCount == 0);

                if (!hasQuery)
                {
                    RefreshExpandAllButtonState();
                }
            });

            if (hasQuery && firstMatchedHeader != null)
            {
                _wasSearchNonEmpty = true;
                _lastSearchScrollTarget = firstMatchedHeader;
                ScrollToTargetImmediate(firstMatchedHeader);
                _pendingScrollTarget = firstMatchedHeader;
                _pendingScrollMinFrame = Time.frameCount + 1;
                _pendingScrollRemainingPasses = 1;
            }
            else if (!hasQuery && _wasSearchNonEmpty)
            {
                _wasSearchNonEmpty = false;
                ClearPendingScroll();
                ResetScrollToTop();
            }
        }

        private static bool IsModGroupNameMatch(ModOptionGroup group, string query, bool allowGroupName)
        {
            return allowGroupName &&
                (PinyinEngine.Matches(group.DisplayName, query) ||
                 PinyinEngine.Matches(group.ModId, query));
        }

        private static void ApplyMatchedModGroupRows(ModOptionGroup group, bool groupNameMatch, bool allowHotkeys, bool allowGeneral)
        {
            int visibleHotkeys = 0;
            foreach (var hotkey in group.Hotkeys)
            {
                bool match = groupNameMatch || (allowHotkeys && IsHotkeyMatchQuery(hotkey, _searchQuery));
                SetObjectActive(hotkey.RowObject, match);
                if (match) visibleHotkeys++;
            }

            int visibleGeneral = 0;
            foreach (var gen in group.GeneralSettings)
            {
                bool match = groupNameMatch || (allowGeneral && IsGeneralMatchQuery(gen, _searchQuery));
                SetObjectActive(gen.RowObject, match);
                if (match) visibleGeneral++;
            }

            int visibleProbed = 0;
            foreach (var probed in group.ReadOnlyProbedHotkeys)
            {
                bool match = groupNameMatch || (allowHotkeys && IsHotkeyMatchQuery(probed, _searchQuery));
                SetObjectActive(probed.RowObject, match);
                if (match) visibleProbed++;
            }

            if (CardParts.TryGetValue(group, out var matchedParts))
            {
                SetObjectActive(matchedParts.hkHeader, visibleHotkeys > 0);
                SetObjectActive(matchedParts.genHeader, visibleGeneral > 0);
                SetObjectActive(matchedParts.probedHeader, visibleProbed > 0);
                SetObjectActive(matchedParts.midSep, visibleHotkeys > 0 && visibleGeneral > 0);
                SetObjectActive(matchedParts.probedSep, (visibleHotkeys > 0 || visibleGeneral > 0) && visibleProbed > 0);
            }

            ShowCardExpandedVisualOnly(group);
        }

        private static void ClearPendingScroll()
        {
            _pendingScrollTarget = null;
            _pendingScrollMinFrame = -1;
            _pendingScrollRemainingPasses = 0;
        }

        private static void ScrollToTargetImmediate(GameObject targetObj)
        {
            if (_scrollRect == null || targetObj == null || !targetObj.activeInHierarchy) return;
            var contentRt = _scrollRect.content;
            var targetRt = targetObj.GetComponent<RectTransform>();
            var viewportRt = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : (_scrollRect.transform as RectTransform);
            if (contentRt == null || targetRt == null || viewportRt == null) return;

            _scrollRect.StopMovement();

            // 跨视图切换/展开懒加载卡片后，需先刷新画布(含 TMP 首帧度量)，再自底向上逐层重排，最后再次同步画布尺寸
            Canvas.ForceUpdateCanvases();

            var parentRt = targetRt.parent as RectTransform;
            int guard = 0;
            while (parentRt != null && guard < 12)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
                if (parentRt == contentRt) break;
                parentRt = parentRt.parent as RectTransform;
                guard++;
            }

            if (_modCardsContainer != null && _modCardsContainer.activeInHierarchy)
            {
                var cardsRt = _modCardsContainer.GetComponent<RectTransform>();
                if (cardsRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(cardsRt);
            }
            if (_normalContentRoot != null && _normalContentRoot.activeInHierarchy)
            {
                var normalRt = _normalContentRoot.GetComponent<RectTransform>();
                if (normalRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(normalRt);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);

            float viewportHeight = viewportRt.rect.height;
            float contentHeight = contentRt.rect.height;
            float scrollableHeight = contentHeight - viewportHeight;
            if (scrollableHeight <= 1f)
            {
                _scrollRect.verticalNormalizedPosition = 1f;
                _scrollRect.StopMovement();
                return;
            }

            Vector3[] corners = new Vector3[4];
            targetRt.GetWorldCorners(corners); // corners[1] 为左上角
            Vector3 itemTopInContent = contentRt.InverseTransformPoint(corners[1]);
            float distanceFromTop = Mathf.Max(0f, contentRt.rect.yMax - itemTopInContent.y - 8f);
            float normalized = 1f - Mathf.Clamp01(distanceFromTop / scrollableHeight);
            _scrollRect.verticalNormalizedPosition = normalized;
            _scrollRect.StopMovement();
        }

        private static void ResetScrollToTop()
        {
            if (_scrollRect == null) return;
            _scrollRect.StopMovement();
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 1f;
        }

        // =====================================================================
        // 辅助切换与全量色彩 / 冲突同步 + 按钮字体样式与悬停持久染色
        // =====================================================================
        private static void SyncViewContainersAndToggleButtons()
        {
            bool showNormal = !IsConflictViewActive && !IsDiagnosticViewActive;
            if (_normalContentRoot != null) _normalContentRoot.SetActive(showNormal);
            if (_conflictContentRoot != null) _conflictContentRoot.SetActive(IsConflictViewActive);
            if (_diagnosticStickyHeaderRoot != null)
            {
                _diagnosticStickyHeaderRoot.SetActive(IsDiagnosticViewActive);
                if (!IsDiagnosticViewActive && _diagnosticStickyHeaderRoot.transform.parent is RectTransform parentRt)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
                }
            }
            if (_diagnosticContentRoot != null) _diagnosticContentRoot.SetActive(IsDiagnosticViewActive);

            UpdateConflictToggleButtonAppearance();
            UpdateDiagnosticToggleButtonAppearance();
            RefreshExpandAllButtonState();
        }

        private static void ToggleConflictMode()
        {
            CancelCapture();
            ClearPendingScroll();
            bool nextConflict = !IsConflictViewActive;
            if (nextConflict)
            {
                IsDiagnosticViewActive = false;
            }
            IsConflictViewActive = nextConflict;

            if (IsConflictViewActive)
            {
                RefreshConflicts();
                RebuildConflictView();
            }
            else if (!string.IsNullOrWhiteSpace(_searchQuery) || _wasSearchNonEmpty)
            {
                ApplySearchFilter();
            }

            SyncViewContainersAndToggleButtons();
            ResetScrollToTop();
        }

        private static void ToggleDiagnosticMode()
        {
            CancelCapture();
            ClearPendingScroll();
            bool nextDiagnostic = !IsDiagnosticViewActive;
            if (nextDiagnostic)
            {
                IsConflictViewActive = false;
            }
            IsDiagnosticViewActive = nextDiagnostic;

            SyncViewContainersAndToggleButtons();

            if (IsDiagnosticViewActive)
            {
                _diagnosticViewDirty = true;
                RebuildDiagnosticView();
            }
            else
            {
                ApplySearchFilter();
            }

            ResetScrollToTop();
        }

        private static void UpdateConflictToggleButtonAppearance()
        {
            if (_conflictToggleBtn == null) return;

            if (IsConflictViewActive)
            {
                SetButtonTextFast(_conflictToggleBtn, I18n.Get("返回完整设置", "Back to Settings"));
                ApplyButtonTextTheme(_conflictToggleBtn, Color.white, useDefaultTheme: true);
            }
            else
            {
                string text = _conflicts.Count > 0
                    ? I18n.Format("[!] 仅看冲突 ({0} 组)", "[!] Conflicts ({0})", _conflicts.Count)
                    : I18n.Get("[!] 仅看冲突", "[!] Conflicts");
                SetButtonTextFast(_conflictToggleBtn, text);
                if (_conflicts.Count > 0)
                {
                    ApplyButtonTextTheme(_conflictToggleBtn, ConflictDetector.ConflictColor);
                }
                else
                {
                    ApplyButtonTextTheme(_conflictToggleBtn, Color.white, useDefaultTheme: true);
                }
            }
        }

        private static void UpdateDiagnosticToggleButtonAppearance()
        {
            if (_diagnosticToggleBtn == null) return;

            if (IsDiagnosticViewActive)
            {
                SetButtonTextFast(_diagnosticToggleBtn, I18n.Get("返回完整设置", "Back to Settings"));
                ApplyButtonTextTheme(_diagnosticToggleBtn, Color.white, useDefaultTheme: true);
            }
            else
            {
                int attentionCount = _scanReport?.AttentionModCount ?? 0;
                string text = attentionCount > 0
                    ? I18n.Format("模组排查 ({0})", "Diagnostics ({0})", attentionCount)
                    : I18n.Get("模组排查", "Diagnostics");
                SetButtonTextFast(_diagnosticToggleBtn, text);
                ApplyButtonTextTheme(_diagnosticToggleBtn, Color.white, useDefaultTheme: true);
            }
        }

        private static void RefreshDiagnosticStickyHeader()
        {
            if (!IsDiagnosticViewActive || _diagnosticStickyHeaderRoot == null || _currentWindow == null || CustomUIManager.Instance == null)
            {
                return;
            }

            var stickyRt = _diagnosticStickyHeaderRoot.GetComponent<RectTransform>();
            if (stickyRt == null) return;

            CustomUIBuilder.ClearContainer(stickyRt);
            using (ComboKeyStore.EnterBypass())
            {
                var stickyBuilder = new CustomUIBuilder(CustomUIManager.Instance, _currentWindow, stickyRt);
                BuildDiagnosticStickyHeader(stickyBuilder);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(stickyRt);
            if (stickyRt.parent is RectTransform parentRt)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
            }
        }

        private static void RebuildDiagnosticView()
        {
            if (!IsDiagnosticViewActive || _diagnosticContentRoot == null || _currentWindow == null || CustomUIManager.Instance == null)
            {
                return;
            }

            var rt = _diagnosticContentRoot.GetComponent<RectTransform>();
            if (rt == null) return;

            if (!_diagnosticViewDirty && rt.childCount > 0)
            {
                RefreshDiagnosticStickyHeader();
                RefreshExpandAllButtonState();
                return;
            }

            float savedNormalized = _scrollRect != null ? _scrollRect.verticalNormalizedPosition : 1f;
            bool hadExistingContent = rt.childCount > 0;

            RefreshDiagnosticStickyHeader();

            CustomUIBuilder.ClearContainer(rt);
            _diagnosticSectionViews.Clear();

            using (ComboKeyStore.EnterBypass())
            {
                var diagBuilder = new CustomUIBuilder(CustomUIManager.Instance, _currentWindow, rt);
                BuildDiagnosticViewContent(diagBuilder);
            }

            _diagnosticViewDirty = false;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            if (_scrollRect != null && _scrollRect.content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);
                if (hadExistingContent)
                {
                    _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(savedNormalized);
                    _scrollRect.StopMovement();
                }
            }
            RefreshExpandAllButtonState();
        }

        private static void AddFixedHorizontalSpace(CustomUIBuilder builder, float pixels)
        {
            builder.AddSpace(pixels);
            if (builder.CurrentContainer == null || builder.CurrentContainer.childCount <= 0) return;
            var spaceTransform = builder.CurrentContainer.GetChild(builder.CurrentContainer.childCount - 1);
            var le = spaceTransform.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = pixels;
                le.preferredWidth = pixels;
                le.flexibleWidth = 0f;
            }
        }

        private static void ConfigureWrappedDiagnosticLabel(CustomUIElement? el, string text)
        {
            if (el == null) return;
            if (el.root != null)
            {
                var le = el.root.GetComponent<LayoutElement>() ?? el.root.AddComponent<LayoutElement>();
                // 置零 min/preferredWidth 并赋予 flexibleWidth=1，避免长文本在 HorizontalLayoutGroup 换行前挤压同行的左侧缩进 Spacer
                le.minWidth = 0f;
                le.preferredWidth = 0f;
                le.flexibleWidth = 1f;
                le.flexibleHeight = 0f;
            }
            if (el.text != null)
            {
                el.text.alignment = TextAlignmentOptions.MidlineLeft;
                el.text.enableWordWrapping = true;
                el.text.overflowMode = TextOverflowModes.Overflow;
                I18n.ApplyFontIfRuntime(el.text, text);
            }
        }

        private static void AddWrappedDiagnosticLabel(CustomUIBuilder builder, float leftIndent, string text, string? id = null)
        {
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            if (leftIndent > 0f) AddFixedHorizontalSpace(builder, leftIndent);
            builder.AddLabel(text, id);
            ConfigureWrappedDiagnosticLabel(builder.Last, text);
            AddFixedHorizontalSpace(builder, 8f);
            builder.End();
        }

        private static void BuildDiagnosticStickyHeader(CustomUIBuilder builder)
        {
            var report = _scanReport ?? new ModScanReport();

            builder.AddSpace(4f);
            builder.BeginRow(6f, TextAnchor.MiddleCenter);
            AddFixedHorizontalSpace(builder, 8f);
            string summaryHeader = I18n.Format(
                "<b>已启用模组排查</b> <color=#99a3b0>(共 {0} 个 | 正常纳管 {1} | </color>" +
                "<color=#ffd27d>待排查 {2}</color><color=#99a3b0> | 纯资源 {3} | </color><color=#d8b4fe>探测热键 {4}</color><color=#99a3b0>)</color>",
                "<b>Enabled Mod Diagnostics</b> <color=#99a3b0>({0} total | Managed {1} | </color>" +
                "<color=#ffd27d>Attention {2}</color><color=#99a3b0> | Assets {3} | </color><color=#d8b4fe>Probed {4}</color><color=#99a3b0>)</color>",
                report.EnabledModCount,
                report.ManagedModCount,
                report.AttentionModCount,
                report.AssetOnlyModCount,
                report.ProbedHotkeysCount
            );
            builder.AddHeader(summaryHeader, null);
            I18n.ApplyFontIfRuntime(builder.Last?.text, summaryHeader);
            builder.AddFlexibleSpace();

            builder.AddButton(I18n.Get("复制排查汇总", "Copy Summary"), CopyFullDiagnosticSummary, null);
            StyleActionButton(builder.Last, 134f, 36f, lrMargin: 8f, tbMargin: 2f, fontScale: 0.84f);
            ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

            builder.AddButton(I18n.Get("强制全部重扫", "Rescan All"), () =>
            {
                PerformFullRescanAndRefreshInPlace(
                    forceInvalidateCache: true,
                    targetModId: null,
                    statusMessage: null);
            }, null);
            StyleActionButton(builder.Last, 134f, 36f, lrMargin: 8f, tbMargin: 2f, fontScale: 0.84f);
            ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

            builder.AddButton(I18n.Get("返回设置", "Back"), ToggleDiagnosticMode, null);
            StyleActionButton(builder.Last, 102f, 36f, lrMargin: 8f, tbMargin: 2f, fontScale: 0.84f);
            ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

            AddFixedHorizontalSpace(builder, 8f);
            builder.End();

            AddWrappedDiagnosticLabel(
                builder,
                8f,
                I18n.Get(
                    "<color=#99a3b0>扫描官方 ModConfig / 注册表，并对无公开热键的代码模组执行静态只读 IL / 本地配置探测。疑难项可「设为按键绑定」或「忽略(非按键)」；顶部搜索栏支持实时过滤本页。</color>",
                    "<color=#99a3b0>Scans official ModConfig/Registry plus read-only static IL & config probing for code mods without public hotkeys. Promote or ignore raw candidates below; top search filters live.</color>"
                ));

            if (!string.IsNullOrEmpty(_diagnosticStatusMessage))
            {
                AddWrappedDiagnosticLabel(builder, 8f, $"<color=#7fe0a0><b>{_diagnosticStatusMessage}</b></color>");
            }

            builder.AddSeparator(null, 2);
        }

        private static string GetDiagnosticCategoryTitle(ModDiagnosticStatus status, int count)
        {
            switch (status)
            {
                case ModDiagnosticStatus.HasCandidates:
                    return I18n.Format(
                        "<color=#ffd27d>[有疑难/待确认项]</color> <b>疑难 / 待确认设置模组</b> <color=#99a3b0>({0} 个)</color>",
                        "<color=#ffd27d>[Needs Review]</color> <b>Mods Requiring Review</b> <color=#99a3b0>({0})</color>",
                        count);
                case ModDiagnosticStatus.UnmanagedCodeMod:
                    return I18n.Format(
                        "<color=#ffb347>[未检测到官方配置]</color> <b>未检测到官方配置模组</b> <color=#99a3b0>({0} 个)</color>",
                        "<color=#ffb347>[No Official Config]</color> <b>Mods Without Official Config</b> <color=#99a3b0>({0})</color>",
                        count);
                case ModDiagnosticStatus.Managed:
                    return I18n.Format(
                        "<color=#7fe0a0>[已正常纳管]</color> <b>已正常纳管模组</b> <color=#99a3b0>({0} 个)</color>",
                        "<color=#7fe0a0>[Managed]</color> <b>Managed Mods</b> <color=#99a3b0>({0})</color>",
                        count);
                default:
                    return I18n.Format(
                        "<color=#99a3b0>[纯资源/无代码]</color> <b>纯资源 / 无代码模组</b> <color=#99a3b0>({0} 个，无需排查设置)</color>",
                        "<color=#99a3b0>[Asset Only]</color> <b>Asset / Data-Only Mods</b> <color=#99a3b0>({0}, no settings required)</color>",
                        count);
            }
        }

        private static void BuildDiagnosticCategorySection(
            CustomUIBuilder builder,
            ModDiagnosticStatus status,
            List<ModDiagnosticItem> items,
            string query,
            bool hasQuery)
        {
            if (items.Count == 0) return;

            bool initialExpanded = hasQuery || (_diagnosticSectionExpanded.TryGetValue(status, out bool saved) && saved);

            CustomUIElement? foldBtn = null;
            GameObject? bodyObj = null;

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            AddFixedHorizontalSpace(builder, 6f);

            builder.AddButton(initialExpanded ? "▼" : "▶", () =>
            {
                if (bodyObj == null || foldBtn == null) return;
                bool nextExpanded = !bodyObj.activeSelf;
                bodyObj.SetActive(nextExpanded);
                foldBtn.SetText(nextExpanded ? "▼" : "▶");
                if (!hasQuery)
                {
                    _diagnosticSectionExpanded[status] = nextExpanded;
                }
                RefreshExpandAllButtonState();

                if (_diagnosticContentRoot != null && _diagnosticContentRoot.transform is RectTransform diagRt)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(diagRt);
                }
                if (_scrollRect != null && _scrollRect.content != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);
                }
            }, null);
            foldBtn = builder.Last;
            StyleFoldButton(foldBtn, 36f);

            string catTitle = GetDiagnosticCategoryTitle(status, items.Count);
            builder.AddLabel(catTitle, null);
            ConfigureWrappedDiagnosticLabel(builder.Last, catTitle);
            AddFixedHorizontalSpace(builder, 6f);
            builder.End();

            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            bodyObj = builder.CurrentContainer.gameObject;
            var bodyLayout = bodyObj.GetComponent<VerticalLayoutGroup>();
            if (bodyLayout != null)
            {
                bodyLayout.childForceExpandWidth = true;
            }
            foreach (var item in items)
            {
                BuildDiagnosticModCard(builder, item, query, hasQuery);
            }
            builder.End();
            bodyObj.SetActive(initialExpanded);

            if (foldBtn != null)
            {
                _diagnosticSectionViews.Add((status, bodyObj, foldBtn));
            }

            builder.AddSeparator(null, 2);
        }

        private static void BuildDiagnosticViewContent(CustomUIBuilder builder)
        {
            var report = _scanReport ?? new ModScanReport();

            string query = _searchQuery?.Trim() ?? string.Empty;
            bool hasQuery = query.Length > 0;

            var reviewItems = new List<ModDiagnosticItem>();
            var unmanagedItems = new List<ModDiagnosticItem>();
            var managedItems = new List<ModDiagnosticItem>();
            var assetItems = new List<ModDiagnosticItem>();

            foreach (var item in report.Mods)
            {
                if (hasQuery && !MatchesDiagnosticItemFilter(item, query))
                {
                    continue;
                }

                switch (item.Status)
                {
                    case ModDiagnosticStatus.HasCandidates:
                        reviewItems.Add(item);
                        break;
                    case ModDiagnosticStatus.UnmanagedCodeMod:
                        unmanagedItems.Add(item);
                        break;
                    case ModDiagnosticStatus.Managed:
                        managedItems.Add(item);
                        break;
                    default:
                        assetItems.Add(item);
                        break;
                }
            }

            if (reviewItems.Count == 0 && unmanagedItems.Count == 0 && managedItems.Count == 0 && assetItems.Count == 0)
            {
                builder.AddSpace(8f);
                AddWrappedDiagnosticLabel(
                    builder,
                    10f,
                    I18n.Get(
                        "<color=#99a3b0>未在模组排查列表中找到匹配的模组、配置 ID 或原始设置项</color>",
                        "<color=#99a3b0>No matching mods, config IDs, or raw entries found in diagnostics.</color>"
                    ));
                return;
            }

            // 按 4 个同类分组依次渲染可折叠卡片，减少同屏杂乱并保留折叠记忆
            BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.HasCandidates, reviewItems, query, hasQuery);
            BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.UnmanagedCodeMod, unmanagedItems, query, hasQuery);
            BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.Managed, managedItems, query, hasQuery);
            BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.AssetOnlyMod, assetItems, query, hasQuery);
        }

        private static bool MatchesDiagnosticItemFilter(ModDiagnosticItem item, string query)
        {
            if (PinyinEngine.Matches(item.DisplayName, query) ||
                PinyinEngine.Matches(item.ModId, query) ||
                PinyinEngine.Matches(item.Author, query))
            {
                return true;
            }

            foreach (var cfgId in item.MatchedConfigIds)
            {
                if (PinyinEngine.Matches(cfgId, query))
                {
                    return true;
                }
            }

            foreach (var cand in item.CandidateEntries)
            {
                if (MatchesDiagnosticCandidateFilter(cand, query))
                {
                    return true;
                }
            }

            foreach (var probed in item.ProbedHotkeys)
            {
                if (MatchesDiagnosticProbedFilter(probed, query))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesDiagnosticCandidateFilter(DiagnosticCandidateEntry cand, string query)
        {
            return PinyinEngine.Matches(cand.Label, query) ||
                   PinyinEngine.Matches(cand.EntryKey, query) ||
                   PinyinEngine.Matches(cand.ConfigModId, query) ||
                   PinyinEngine.Matches(cand.ValueTypeName, query);
        }

        private static bool MatchesDiagnosticProbedFilter(ModHotkeyEntry probed, string query)
        {
            return PinyinEngine.Matches(probed.DisplayName, query) ||
                   (!string.IsNullOrEmpty(probed.ProbedSourceDetail) && PinyinEngine.Matches(probed.ProbedSourceDetail, query)) ||
                   MatchesKeyCodeSearchToken(probed.EffectiveKey, query);
        }

        private static string GetDiagnosticStatusBadge(ModDiagnosticStatus status)
        {
            switch (status)
            {
                case ModDiagnosticStatus.HasCandidates:
                    return I18n.Get("<color=#ffd27d>[有疑难/待确认项]</color>", "<color=#ffd27d>[Needs Review]</color>");
                case ModDiagnosticStatus.UnmanagedCodeMod:
                    return I18n.Get("<color=#ffb347>[未检测到官方配置]</color>", "<color=#ffb347>[No Official Config]</color>");
                case ModDiagnosticStatus.Managed:
                    return I18n.Get("<color=#7fe0a0>[已正常纳管]</color>", "<color=#7fe0a0>[Managed]</color>");
                default:
                    return I18n.Get("<color=#99a3b0>[纯资源/无代码]</color>", "<color=#99a3b0>[Asset Only]</color>");
            }
        }

        private static void BuildDiagnosticModCard(CustomUIBuilder builder, ModDiagnosticItem item, string query, bool hasQuery)
        {
            bool anyCandMatchedQuery = hasQuery && item.CandidateEntries.Any(c => MatchesDiagnosticCandidateFilter(c, query));
            bool anyProbedMatchedQuery = hasQuery && item.ProbedHotkeys.Any(p => MatchesDiagnosticProbedFilter(p, query));
            bool isExpanded = _expandedDiagnosticModIds.Contains(item.ModId) || anyCandMatchedQuery || anyProbedMatchedQuery;

            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            AddFixedHorizontalSpace(builder, 18f);

            string titleBadge = GetDiagnosticStatusBadge(item.Status);
            string idSuffix = item.IsWildOrphanConfig
                ? I18n.Get(" <color=#99a3b0>(独立 ModConfig)</color>", " <color=#99a3b0>(Standalone ModConfig)</color>")
                : string.Empty;

            string statsPart = string.Empty;
            if (item.Status != ModDiagnosticStatus.AssetOnlyMod)
            {
                statsPart = I18n.Format(
                    "  <color=#9ec4ff>按键:{0}</color> <color=#a8d8b9>常规:{1}</color>",
                    "  <color=#9ec4ff>Keys:{0}</color> <color=#a8d8b9>Settings:{1}</color>",
                    item.ManagedHotkeyCount,
                    item.ManagedGeneralCount);
                if (item.ProbedHotkeys.Count > 0)
                {
                    statsPart += I18n.Format(
                        " <color=#d8b4fe>探测:{0}</color>",
                        " <color=#d8b4fe>Probed:{0}</color>",
                        item.ProbedHotkeys.Count);
                }
                if (item.CandidateEntries.Count > 0)
                {
                    statsPart += I18n.Format(
                        " <color=#ffd27d>候选:{0}</color>",
                        " <color=#ffd27d>Raw:{0}</color>",
                        item.CandidateEntries.Count);
                }
                if (item.IgnoredCandidateCount > 0)
                {
                    statsPart += I18n.Format(
                        " <color=#99a3b0>已忽略:{0}</color>",
                        " <color=#99a3b0>Ignored:{0}</color>",
                        item.IgnoredCandidateCount);
                }
            }

            string modTitleText = $"{titleBadge} <b>{item.DisplayName}</b>{idSuffix}{statsPart}";
            builder.AddLabel(modTitleText, null);
            ConfigureWrappedDiagnosticLabel(builder.Last, modTitleText);

            if (item.CandidateEntries.Count > 0 || item.ProbedHotkeys.Count > 0)
            {
                string inspectText = item.CandidateEntries.Count > 0
                    ? I18n.Get("查看原始项", "Inspect Raw")
                    : I18n.Get("查看探测项", "Inspect Probed");
                builder.AddButton(isExpanded ? I18n.Get("收起明细", "Hide Raw") : inspectText, () =>
                {
                    if (_expandedDiagnosticModIds.Contains(item.ModId))
                    {
                        _expandedDiagnosticModIds.Remove(item.ModId);
                    }
                    else
                    {
                        _expandedDiagnosticModIds.Add(item.ModId);
                    }
                    _diagnosticViewDirty = true;
                    RebuildDiagnosticView();
                }, null);
                StyleActionButton(builder.Last, 114f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.82f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }

            if (item.ManagedHotkeyCount > 0 || item.ManagedGeneralCount > 0 || item.ProbedHotkeys.Count > 0)
            {
                string jumpText = (item.ManagedHotkeyCount > 0 || item.ProbedHotkeys.Count > 0)
                    ? I18n.Get("前去查看", "View Keys")
                    : I18n.Get("查看设置", "View");
                if (item.ManagedHotkeyCount > 0)
                {
                    jumpText = I18n.Get("前去改键", "Rebind");
                }
                builder.AddButton(jumpText, () =>
                {
                    JumpToModInNormalView(item.ModId, null);
                }, null);
                StyleActionButton(builder.Last, 98f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.82f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }

            if (item.Status == ModDiagnosticStatus.UnmanagedCodeMod || item.ProbedHotkeys.Count > 0)
            {
                builder.AddButton(I18n.Get("复查此模组", "Recheck Mod"), () =>
                {
                    PerformFullRescanAndRefreshInPlace(
                        forceInvalidateCache: true,
                        targetModId: item.ModId,
                        statusMessage: null);
                }, null);
                StyleActionButton(builder.Last, 114f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.82f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

                builder.AddButton(I18n.Get("复制信息", "Copy Info"), () =>
                {
                    CopyModDiagnosticInfo(item);
                }, null);
                StyleActionButton(builder.Last, 98f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.82f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }

            AddFixedHorizontalSpace(builder, 8f);
            builder.End();

            // 副标题与排查说明
            if (item.Status == ModDiagnosticStatus.UnmanagedCodeMod)
            {
                string authorVer = string.IsNullOrEmpty(item.Author) ? item.Version : $"{item.Author} | v{item.Version}";
                string meta = string.IsNullOrEmpty(authorVer) ? string.Empty : $"({authorVer}) ";
                string subDesc = item.ProbedHotkeys.Count > 0
                    ? I18n.Format(
                        "<color=#b0b9c4>{0}未检测到官方 ModConfig/注册表配置，但已通过静态只读探测识别 {1} 个疑似按键绑定（仅供只读冲突排查，无法直接代改原模组逻辑；可点「复制信息」反馈作者）。</color>",
                        "<color=#b0b9c4>{0}No official ModConfig/Registry found, but {1} read-only keybind(s) were probed for conflict checking (cannot overwrite hardcoded bindings; click \"Copy Info\" to share).</color>",
                        meta, item.ProbedHotkeys.Count)
                    : I18n.Format(
                        "<color=#b0b9c4>{0}未检测到官方配置，且静态只读探测暂未发现明确的静态按键字段（可能为鼠标右键/HUD点击/动态绑定），可点「复制信息」反馈作者补充专项适配。</color>",
                        "<color=#b0b9c4>{0}No official config found and static probing detected no explicit keybinds (may use HUD clicks or dynamic input). Click \"Copy Info\" to share details.</color>",
                        meta);
                AddWrappedDiagnosticLabel(builder, 30f, subDesc);
            }
            else if (item.Status == ModDiagnosticStatus.AssetOnlyMod)
            {
                AddWrappedDiagnosticLabel(
                    builder,
                    30f,
                    I18n.Get(
                        "<color=#99a3b0>该模组不包含代码程序集，属于纯资源/数据模组，无需快捷键或设置。</color>",
                        "<color=#99a3b0>Contains no assembly code (asset / data-only mod); no settings or hotkeys required.</color>"));
            }
            else if (item.MatchedConfigIds.Count > 0 &&
                     (item.MatchedConfigIds.Count > 1 ||
                      !string.Equals(item.MatchedConfigIds[0], item.ModId, StringComparison.OrdinalIgnoreCase) ||
                      item.IsWildOrphanConfig))
            {
                AddWrappedDiagnosticLabel(
                    builder,
                    30f,
                    I18n.Format(
                        "<color=#9ec4ff>已关联 ModConfig ID: {0}</color>",
                        "<color=#9ec4ff>Linked ModConfig ID(s): {0}</color>",
                        string.Join(", ", item.MatchedConfigIds)));
            }

            // 展开后的疑难/候选原始条目列表及只读探测条目列表
            if (isExpanded && item.CandidateEntries.Count > 0)
            {
                foreach (var cand in item.CandidateEntries)
                {
                    if (hasQuery && anyCandMatchedQuery && !MatchesDiagnosticCandidateFilter(cand, query))
                    {
                        continue;
                    }
                    BuildDiagnosticCandidateRow(builder, cand);
                }
            }
            if (isExpanded && item.ProbedHotkeys.Count > 0)
            {
                foreach (var probed in item.ProbedHotkeys)
                {
                    if (hasQuery && anyProbedMatchedQuery && !MatchesDiagnosticProbedFilter(probed, query))
                    {
                        continue;
                    }
                    BuildDiagnosticProbedRow(builder, probed);
                }
            }

            builder.AddSeparator(null, 1);
        }

        private static void BuildDiagnosticCandidateRow(CustomUIBuilder builder, DiagnosticCandidateEntry cand)
        {
            string tag = cand.IsForcedHotkey
                ? I18n.Get("<color=#ffd27d>[已强转按键]</color>", "<color=#ffd27d>[Forced Keybind]</color>")
                : (cand.IsIgnoredCandidate
                    ? I18n.Get("<color=#99a3b0>[已忽略非按键]</color>", "<color=#99a3b0>[Ignored · Non-Hotkey]</color>")
                    : (cand.IsCurrentlyHotkey
                        ? I18n.Get("<color=#9ec4ff>[已纳管按键]</color>", "<color=#9ec4ff>[Managed Key]</color>")
                        : (cand.WasDroppedFromMainUI
                            ? I18n.Get("<color=#ffb347>[原UI未显示]</color>", "<color=#ffb347>[Hidden in Main UI]</color>")
                            : I18n.Get("<color=#a8d8b9>[常规选项]</color>", "<color=#a8d8b9>[General Setting]</color>"))));

            string detailText = I18n.Format(
                "{0} <b>{1}</b>  <color=#99a3b0>键:{2} | {3} | 当前:{4} | 默认:{5}</color>",
                "{0} <b>{1}</b>  <color=#99a3b0>Key:{2} | {3} | Current:{4} | Default:{5}</color>",
                tag, cand.Label, cand.EntryKey, cand.ValueTypeName, cand.CurrentValuePreview, cand.DefaultValuePreview);

            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            AddFixedHorizontalSpace(builder, 34f);

            builder.AddLabel(detailText, null);
            ConfigureWrappedDiagnosticLabel(builder.Last, detailText);

            if (cand.IsForcedHotkey)
            {
                builder.AddButton(I18n.Get("前去改键", "Rebind"), () =>
                {
                    JumpToModInNormalView(cand.OwnerModId, cand.Label);
                }, null);
                StyleActionButton(builder.Last, 98f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

                builder.AddButton(I18n.Get("还原", "Revert"), () =>
                {
                    ComboKeyStore.SetForcedHotkey(cand.CanonicalId, false);
                    _expandedDiagnosticModIds.Add(cand.OwnerModId);
                    PerformFullRescanAndRefreshInPlace(
                        forceInvalidateCache: true,
                        targetModId: null,
                        statusMessage: I18n.Format(
                            "已还原 [{0}] {1} 为默认识别类型",
                            "Restored [{0}] {1} to default type",
                            cand.ModDisplayName, cand.Label));
                }, null);
                StyleActionButton(builder.Last, 74f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }
            else if (cand.IsIgnoredCandidate)
            {
                builder.AddButton(I18n.Get("恢复提醒", "Restore"), () =>
                {
                    ComboKeyStore.SetIgnoredCandidate(cand.CanonicalId, false);
                    _expandedDiagnosticModIds.Add(cand.OwnerModId);
                    PerformFullRescanAndRefreshInPlace(
                        forceInvalidateCache: true,
                        targetModId: null,
                        statusMessage: I18n.Format(
                            "已恢复 [{0}] {1} 的疑难待确认提醒",
                            "Restored [{0}] {1} to review candidates",
                            cand.ModDisplayName, cand.Label));
                }, null);
                StyleActionButton(builder.Last, 96f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }
            else if (cand.CanPromoteToHotkey && !cand.IsCurrentlyHotkey)
            {
                builder.AddButton(I18n.Get("设为按键绑定", "Bind as Hotkey"), () =>
                {
                    ComboKeyStore.SetForcedHotkey(cand.CanonicalId, true);
                    _expandedDiagnosticModIds.Add(cand.OwnerModId);
                    PerformFullRescanAndRefreshInPlace(
                        forceInvalidateCache: true,
                        targetModId: null,
                        statusMessage: I18n.Format(
                            "已将 [{0}] {1} 设为按键绑定，可点击「前去改键」编辑组合键或空键",
                            "Promoted [{0}] {1} to hotkey; click \"Rebind\" to assign a combo or None",
                            cand.ModDisplayName, cand.Label));
                }, null);
                StyleActionButton(builder.Last, 134f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

                builder.AddButton(I18n.Get("忽略(非按键)", "Ignore"), () =>
                {
                    ComboKeyStore.SetIgnoredCandidate(cand.CanonicalId, true);
                    _expandedDiagnosticModIds.Add(cand.OwnerModId);
                    PerformFullRescanAndRefreshInPlace(
                        forceInvalidateCache: true,
                        targetModId: null,
                        statusMessage: I18n.Format(
                            "已忽略 [{0}] {1}（不再归入待排查项，可随时点「恢复提醒」还原）",
                            "Ignored [{0}] {1} (excluded from review list; click Restore anytime)",
                            cand.ModDisplayName, cand.Label));
                }, null);
                StyleActionButton(builder.Last, 118f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }
            else if (cand.IsCurrentlyHotkey)
            {
                builder.AddButton(I18n.Get("前去改键", "Rebind"), () =>
                {
                    JumpToModInNormalView(cand.OwnerModId, cand.Label);
                }, null);
                StyleActionButton(builder.Last, 98f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }

            AddFixedHorizontalSpace(builder, 8f);
            builder.End();
        }

        private static void BuildDiagnosticProbedRow(CustomUIBuilder builder, ModHotkeyEntry probed)
        {
            string sourceInfo = string.IsNullOrEmpty(probed.ProbedSourceDetail) ? probed.Tooltip : probed.ProbedSourceDetail;
            string detailText = I18n.Format(
                "<color=#d8b4fe>[只读探测]</color> <b>{0}</b>  <color=#9ec4ff>[{1}]</color>  <color=#99a3b0>{2}</color>",
                "<color=#d8b4fe>[Probed]</color> <b>{0}</b>  <color=#9ec4ff>[{1}]</color>  <color=#99a3b0>{2}</color>",
                probed.DisplayName,
                FormatHotkeyComboText(probed),
                sourceInfo);

            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            AddFixedHorizontalSpace(builder, 34f);

            builder.AddLabel(detailText, null);
            ConfigureWrappedDiagnosticLabel(builder.Last, detailText);

            builder.AddButton(I18n.Get("定位", "Locate"), () =>
            {
                JumpToModInNormalView(probed.ModId, probed.DisplayName);
            }, null);
            StyleActionButton(builder.Last, 82f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
            ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

            AddFixedHorizontalSpace(builder, 8f);
            builder.End();
        }

        private static void CopyFullDiagnosticSummary()
        {
            var report = _scanReport ?? new ModScanReport();
            string? ver = report.Mods.FirstOrDefault(m => string.Equals(m.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase))?.Version;
            if (string.IsNullOrEmpty(ver)) ver = "1.4.4";
            var sb = new System.Text.StringBuilder(1024);
            sb.AppendFormat(
                "[ProbablyUnifiedHotkeys v{0} Diagnostics] Total={1} | Managed={2} | Attention={3} | AssetOnly={4} | ProbedHotkeys={5}\n",
                ver,
                report.EnabledModCount,
                report.ManagedModCount,
                report.AttentionModCount,
                report.AssetOnlyModCount,
                report.ProbedHotkeysCount);

            foreach (var item in report.Mods)
            {
                if (item.Status == ModDiagnosticStatus.AssetOnlyMod) continue;
                bool interesting = item.Status == ModDiagnosticStatus.HasCandidates ||
                                   item.Status == ModDiagnosticStatus.UnmanagedCodeMod ||
                                   item.ProbedHotkeys.Count > 0;
                if (!interesting) continue;

                sb.AppendFormat(
                    "- [{0}] {1} (Id={2}, Author={3}, Ver={4}, WorkshopId={5}) | Keys={6}, Settings={7}, Probed={8}, Raw={9}, Ignored={10}\n",
                    item.Status,
                    item.DisplayName,
                    item.ModId,
                    item.Author,
                    item.Version,
                    item.WorkshopId,
                    item.ManagedHotkeyCount,
                    item.ManagedGeneralCount,
                    item.ProbedHotkeys.Count,
                    item.CandidateEntries.Count,
                    item.IgnoredCandidateCount);

                foreach (var p in item.ProbedHotkeys)
                {
                    sb.AppendFormat("  * Probed: {0} [{1}] ({2})\n", p.DisplayName, FormatHotkeyComboText(p), p.ProbedSourceDetail);
                }
                foreach (var c in item.CandidateEntries)
                {
                    if (c.IsIgnoredCandidate) continue;
                    if (!c.CanPromoteToHotkey && !c.IsForcedHotkey) continue;
                    sb.AppendFormat("  * Candidate: {0} (Key={1}, Type={2}, Cur={3}, Def={4})\n",
                        c.Label, c.EntryKey, c.ValueTypeName, c.CurrentValuePreview, c.DefaultValuePreview);
                }
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            _diagnosticStatusMessage = I18n.Get(
                "已复制全部待排查/只读探测模组汇总信息到剪贴板",
                "Copied full diagnostics & probed hotkeys summary to clipboard");
            RefreshDiagnosticStickyHeader();
        }

        private static void CopyModDiagnosticInfo(ModDiagnosticItem item)
        {
            string info = $"ModName={item.DisplayName} | ModId={item.ModId} | Author={item.Author} | Version={item.Version} | WorkshopId={item.WorkshopId} | Status={item.Status}";
            GUIUtility.systemCopyBuffer = info;
            _diagnosticStatusMessage = I18n.Format(
                "已复制 [{0}] 的模组排查信息到剪贴板",
                "Copied diagnostics for [{0}] to clipboard",
                item.DisplayName);
            RefreshDiagnosticStickyHeader();
        }

        private static void JumpToModInNormalView(string ownerModId, string? hotkeyLabelHint)
        {
            CancelCapture();
            ClearPendingScroll();
            if (_scrollRect != null) _scrollRect.StopMovement();
            IsDiagnosticViewActive = false;
            IsConflictViewActive = false;

            if (!string.IsNullOrEmpty(_searchQuery))
            {
                _searchQuery = string.Empty;
                _searchInputField?.SetTextWithoutNotify(string.Empty);
            }
            _wasSearchNonEmpty = false;

            SyncViewContainersAndToggleButtons();
            ApplySearchFilter();

            ModOptionGroup? targetGroup = _modGroups.FirstOrDefault(g =>
                string.Equals(g.ModId, ownerModId, StringComparison.OrdinalIgnoreCase) ||
                (g.ExtraNotifyModIds != null && g.ExtraNotifyModIds.Any(id => string.Equals(id, ownerModId, StringComparison.OrdinalIgnoreCase))));

            if (targetGroup == null)
            {
                ResetScrollToTop();
                return;
            }

            WithSuspendedRootLayout(() =>
            {
                using (ComboKeyStore.EnterBypass())
                {
                    SetGroupExpanded(targetGroup, true);
                    StyleFoldButton(targetGroup.FoldArrowElement, 36f);
                    SyncCompactActionButton(targetGroup.ResetButtonElement);
                    RefreshExpandAllButtonState();
                }
            });

            GameObject? scrollTarget = targetGroup.HeaderRowObject;
            if (!string.IsNullOrEmpty(hotkeyLabelHint))
            {
                var targetHk = targetGroup.Hotkeys.FirstOrDefault(h =>
                    string.Equals(h.DisplayName, hotkeyLabelHint, StringComparison.OrdinalIgnoreCase))
                    ?? targetGroup.ReadOnlyProbedHotkeys.FirstOrDefault(h =>
                    string.Equals(h.DisplayName, hotkeyLabelHint, StringComparison.OrdinalIgnoreCase));
                if (targetHk?.RowObject != null)
                {
                    scrollTarget = targetHk.RowObject;
                }
            }

            if (scrollTarget != null)
            {
                ScrollToTargetImmediate(scrollTarget);
                _pendingScrollTarget = scrollTarget;
                _pendingScrollMinFrame = Time.frameCount + 1;
                _pendingScrollRemainingPasses = 2;
            }
            else
            {
                ResetScrollToTop();
            }
        }

        private static void PerformFullRescanAndRefreshInPlace(bool forceInvalidateCache, string? targetModId, string? statusMessage)
        {
            CancelCapture();

            var expandedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingHotkeys = new Dictionary<string, (KeyCode key, KeyModifierMask mods)>(StringComparer.OrdinalIgnoreCase);
            var pendingGenerals = new Dictionary<string, (bool? b, float? f, int? idx)>(StringComparer.OrdinalIgnoreCase);

            var oldGroups = _modGroups;
            foreach (var g in oldGroups)
            {
                if (IsCardCurrentlyExpanded(g))
                {
                    expandedModIds.Add(g.ModId);
                }
                foreach (var hk in g.Hotkeys)
                {
                    if (hk.HasPendingChange)
                    {
                        pendingHotkeys[hk.Id] = (hk.PendingKey, hk.PendingModifiers);
                    }
                }
                foreach (var gen in g.GeneralSettings)
                {
                    if (gen.PendingBool.HasValue || gen.PendingFloat.HasValue || gen.PendingIndex.HasValue)
                    {
                        pendingGenerals[gen.Id] = (gen.PendingBool, gen.PendingFloat, gen.PendingIndex);
                    }
                }
            }

            if (forceInvalidateCache)
            {
                if (!string.IsNullOrEmpty(targetModId))
                {
                    DllKeyProbe.InvalidateMod(targetModId);
                }
                else
                {
                    DllKeyProbe.InvalidateAll();
                }
                ModCatalogCache.Invalidate();
            }

            _modGroups = ModCatalogCache.GetOrScanGroups(out var scannedHotkeys, out _scanReport);
            for (int i = 0; i < scannedHotkeys.Count; i++)
            {
                var hk = scannedHotkeys[i];
                if (hk.ModId == "vanilla" && !string.IsNullOrEmpty(hk.RawOptionId) &&
                    _vanillaHotkeyByRawId.TryGetValue(hk.RawOptionId, out var existingVanilla))
                {
                    existingVanilla.ConflictButtonElement = null;
                    scannedHotkeys[i] = existingVanilla;
                }
            }
            _allHotkeys = scannedHotkeys;

            foreach (var g in _modGroups)
            {
                g.IsExpanded = expandedModIds.Contains(g.ModId);
                foreach (var hk in g.Hotkeys)
                {
                    if (pendingHotkeys.TryGetValue(hk.Id, out var savedPending))
                    {
                        hk.PendingKey = savedPending.key;
                        hk.PendingModifiers = savedPending.mods;
                        hk.HasPendingChange = true;
                    }
                }
                foreach (var gen in g.GeneralSettings)
                {
                    if (pendingGenerals.TryGetValue(gen.Id, out var savedGen))
                    {
                        gen.PendingBool = savedGen.b;
                        gen.PendingFloat = savedGen.f;
                        gen.PendingIndex = savedGen.idx;
                    }
                }
            }

            foreach (var oldG in oldGroups)
            {
                CardParts.Remove(oldG);
            }

            WithSuspendedRootLayout(() =>
            {
                if (_modCardsContainer != null && _currentWindow != null && CustomUIManager.Instance != null)
                {
                    var modRt = _modCardsContainer.GetComponent<RectTransform>();
                    if (modRt != null)
                    {
                        CustomUIBuilder.ClearContainer(modRt);
                        using (ComboKeyStore.EnterBypass())
                        {
                            var modBuilder = new CustomUIBuilder(CustomUIManager.Instance, _currentWindow, modRt);
                            foreach (var group in _modGroups)
                            {
                                BuildModGroupCardShell(modBuilder, group);
                            }
                        }
                        RestoreSavedExpandedModCards();
                        foreach (var group in _modGroups)
                        {
                            StyleFoldButton(group.FoldArrowElement, 36f);
                            if (group.IsExpanded)
                            {
                                SyncCompactActionButton(group.ResetButtonElement);
                            }
                        }
                    }
                }

                if (!IsConflictViewActive && !IsDiagnosticViewActive)
                {
                    ApplySearchFilter();
                }
            });

            _prebuildModIndex = 0;
            _cachedSuspendableLayoutsDirty = true;

            ComboKeyStore.RebuildRuntimeLookup(_allHotkeys);
            RefreshConflicts();
            RefreshExpandAllButtonState();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                _diagnosticStatusMessage = statusMessage;
            }
            else if (!string.IsNullOrEmpty(targetModId))
            {
                var found = _scanReport?.Mods.FirstOrDefault(m => string.Equals(m.ModId, targetModId, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    _diagnosticStatusMessage = I18n.Format(
                        "已完成复查：当前已启用列表中未包含模组 {0}",
                        "Recheck complete: {0} is not in the enabled mods list.",
                        targetModId);
                }
                else if (found.Status == ModDiagnosticStatus.UnmanagedCodeMod)
                {
                    _diagnosticStatusMessage = I18n.Format(
                        "已复查 [{0}]：仍未检测到官方配置项（可能为自定义按键或私藏写法）",
                        "Rechecked [{0}]: Still no official config entries found (likely custom input/UI).",
                        found.DisplayName);
                }
                else
                {
                    _diagnosticStatusMessage = I18n.Format(
                        "已复查 [{0}]：检测到 按键:{1} / 常规:{2} / 候选:{3}",
                        "Rechecked [{0}]: Found Keys:{1} / Settings:{2} / Raw:{3}",
                        found.DisplayName,
                        found.ManagedHotkeyCount,
                        found.ManagedGeneralCount,
                        found.CandidateEntries.Count);
                }
            }
            else
            {
                var rpt = _scanReport;
                _diagnosticStatusMessage = rpt != null
                    ? I18n.Format(
                        "已完成全量重扫：共 {0} 个模组，捕获 按键:{1} / 常规:{2}，待排查:{3}",
                        "Rescan complete: {0} mods, Keys:{1} / Settings:{2}, Attention:{3}",
                        rpt.EnabledModCount,
                        rpt.TotalHotkeysCount,
                        rpt.TotalGeneralsCount,
                        rpt.AttentionModCount)
                    : I18n.Get("已完成全量重扫", "Rescan complete");
            }

            UpdateDiagnosticToggleButtonAppearance();
            _diagnosticViewDirty = true;
            if (IsDiagnosticViewActive)
            {
                RebuildDiagnosticView();
            }
        }

        private static bool AllVisibleDiagnosticSectionsExpanded()
        {
            if (_diagnosticSectionViews.Count == 0)
            {
                return false;
            }

            foreach (var section in _diagnosticSectionViews)
            {
                if (section.BodyObj != null && !section.BodyObj.activeSelf)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AllVisibleCardsExpanded()
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(_searchQuery);
            int counted = 0;

            foreach (var c in _vanillaCards)
            {
                if (hasQuery && (c.HeaderRowObject == null || !c.HeaderRowObject.activeSelf)) continue;
                counted++;
                if (hasQuery ? !IsCardCurrentlyExpanded(c) : !c.IsExpanded) return false;
            }
            foreach (var g in _modGroups)
            {
                if (hasQuery && (g.HeaderRowObject == null || !g.HeaderRowObject.activeSelf)) continue;
                counted++;
                if (hasQuery ? !IsCardCurrentlyExpanded(g) : !g.IsExpanded) return false;
            }

            return counted > 0;
        }

        private static void RefreshExpandAllButtonState()
        {
            _allExpanded = IsDiagnosticViewActive
                ? AllVisibleDiagnosticSectionsExpanded()
                : AllVisibleCardsExpanded();
            SetButtonTextFast(_expandToggleBtn, _allExpanded ? I18n.Get("全部收起", "Collapse All") : I18n.Get("全部展开", "Expand All"));
        }

        private static void ToggleExpandAll()
        {
            if (IsDiagnosticViewActive)
            {
                bool nextExpanded = !AllVisibleDiagnosticSectionsExpanded();
                bool hasDiagnosticQuery = !string.IsNullOrWhiteSpace(_searchQuery);

                for (int i = 0; i < _diagnosticSectionViews.Count; i++)
                {
                    var sec = _diagnosticSectionViews[i];
                    if (!hasDiagnosticQuery)
                    {
                        _diagnosticSectionExpanded[sec.Status] = nextExpanded;
                    }
                    sec.BodyObj?.SetActive(nextExpanded);
                    sec.FoldBtn?.SetText(nextExpanded ? "▼" : "▶");
                }

                RefreshExpandAllButtonState();
                if (_diagnosticContentRoot != null)
                {
                    var diagRt = _diagnosticContentRoot.GetComponent<RectTransform>();
                    if (diagRt != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(diagRt);
                    }
                }
                if (_scrollRect != null && _scrollRect.content != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);
                }
                return;
            }

            if (IsConflictViewActive)
            {
                IsConflictViewActive = false;
                SyncViewContainersAndToggleButtons();
                ApplySearchFilter();
            }

            _allExpanded = !_allExpanded;
            SetButtonTextFast(_expandToggleBtn, _allExpanded ? I18n.Get("全部收起", "Collapse All") : I18n.Get("全部展开", "Expand All"));

            bool hasQuery = !string.IsNullOrWhiteSpace(_searchQuery);

            // 临时挂起外层容器、Viewport 及卡片标题栏 LayoutGroup，确保全部卡片在单帧内零过渡动画瞬间切换完毕，仅触发 1 次集中排布
            WithSuspendedRootLayout(() =>
            {
                using (ComboKeyStore.EnterBypass())
                {
                    foreach (var c in _vanillaCards)
                    {
                        if (hasQuery && (c.HeaderRowObject == null || !c.HeaderRowObject.activeSelf)) continue;
                        c.SetExpanded(_allExpanded);
                        VanillaExpandedStates[c.SectionId] = _allExpanded;
                        if (_allExpanded) SyncCompactActionButton(c.ResetButtonElement);
                    }

                    foreach (var g in _modGroups)
                    {
                        if (hasQuery && (g.HeaderRowObject == null || !g.HeaderRowObject.activeSelf)) continue;
                        SetGroupExpanded(g, _allExpanded);
                        if (_allExpanded) SyncCompactActionButton(g.ResetButtonElement);
                    }
                }
            });
        }

        private static readonly List<Behaviour> SuspendedLayoutScratch = new List<Behaviour>(96);

        private static void EnsureCachedSuspendableLayouts()
        {
            if (!_cachedSuspendableLayoutsDirty && _cachedSuspendableLayouts != null)
            {
                return;
            }

            _cachedSuspendableLayouts ??= new List<Behaviour>(96);
            _cachedSuspendableLayouts.Clear();

            CollectContainerLayouts(_normalContentRoot, _cachedSuspendableLayouts);
            if (_scrollRect != null)
            {
                if (_scrollRect.content != null && _scrollRect.content.gameObject != _normalContentRoot)
                {
                    CollectContainerLayouts(_scrollRect.content.gameObject, _cachedSuspendableLayouts);
                }
                if (_scrollRect.viewport != null &&
                    _scrollRect.viewport.gameObject != _normalContentRoot &&
                    (_scrollRect.content == null || _scrollRect.viewport.gameObject != _scrollRect.content.gameObject))
                {
                    CollectContainerLayouts(_scrollRect.viewport.gameObject, _cachedSuspendableLayouts);
                }
            }

            for (int i = 0; i < _vanillaCards.Count; i++)
            {
                var vc = _vanillaCards[i];
                if (vc.ResetButtonElement != null)
                {
                    CollectContainerLayouts(vc.HeaderRowObject, _cachedSuspendableLayouts);
                }
                CollectContainerLayouts(vc.ContentContainerObject, _cachedSuspendableLayouts);
            }
            for (int i = 0; i < _modGroups.Count; i++)
            {
                var mg = _modGroups[i];
                if (mg.ResetButtonElement != null)
                {
                    CollectContainerLayouts(mg.HeaderRowObject, _cachedSuspendableLayouts);
                }
                CollectContainerLayouts(mg.ContentContainerObject, _cachedSuspendableLayouts);
            }

            _cachedSuspendableLayoutsDirty = false;
        }

        private static void WithSuspendedRootLayout(Action action)
        {
            EnsureCachedSuspendableLayouts();
            SuspendedLayoutScratch.Clear();

            var cached = _cachedSuspendableLayouts!;
            for (int i = 0; i < cached.Count; i++)
            {
                var b = cached[i];
                if (b != null && b.enabled)
                {
                    b.enabled = false;
                    SuspendedLayoutScratch.Add(b);
                }
            }

            try
            {
                action();
            }
            finally
            {
                for (int i = SuspendedLayoutScratch.Count - 1; i >= 0; i--)
                {
                    if (SuspendedLayoutScratch[i] != null)
                    {
                        SuspendedLayoutScratch[i].enabled = true;
                    }
                }
                SuspendedLayoutScratch.Clear();

                if (_normalContentRoot != null)
                {
                    var normalRt = _normalContentRoot.GetComponent<RectTransform>();
                    if (normalRt != null) LayoutRebuilder.MarkLayoutForRebuild(normalRt);
                }
                if (_scrollRect != null && _scrollRect.content != null)
                {
                    LayoutRebuilder.MarkLayoutForRebuild(_scrollRect.content);
                }
            }
        }

        private static void CollectContainerLayouts(GameObject? container, List<Behaviour> targetList)
        {
            if (container == null) return;
            foreach (var lg in container.GetComponents<LayoutGroup>())
            {
                if (lg != null)
                {
                    targetList.Add(lg);
                }
            }
            foreach (var fitter in container.GetComponents<ContentSizeFitter>())
            {
                if (fitter != null)
                {
                    targetList.Add(fitter);
                }
            }
        }

        private static KeyCode GetVanillaDefaultKey(GameOptionRegistry.Entry entry, ModHotkeyEntry? linked)
        {
            if (entry != null && !string.IsNullOrEmpty(entry.id) &&
                entry.id.StartsWith("key_", StringComparison.Ordinal))
            {
                string bindingId = entry.id.Substring(4);
                if (KeyBindHelper.Find(bindingId) != null)
                {
                    return KeyBindHelper.GetDefault(bindingId);
                }
            }
            return linked != null ? linked.DefaultKey : KeyCode.None;
        }

        private static void ResetVanillaSectionToDefaults(GameOptionUIManager manager, VanillaSectionCard card)
        {
            if (_currentWindow == null || manager == null || card == null) return;

            CancelCapture();

            var pendingKeys = PendingKeysField?.GetValue(manager) as Dictionary<string, KeyCode>;
            bool hadKeyBind = false;
            bool hasDisplayResolution = false;

            using (ComboKeyStore.EnterBypass())
            {
                foreach (var item in card.Entries)
                {
                    var entry = item.Entry;
                    if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                    if (entry.kind == GameOptionRegistry.Kind.KeyBind)
                    {
                        KeyCode defaultKey = GetVanillaDefaultKey(entry, item.LinkedHotkey);
                        if (pendingKeys != null)
                        {
                            pendingKeys[entry.id] = defaultKey;
                        }
                        if (item.LinkedHotkey != null)
                        {
                            item.LinkedHotkey.PendingKey = defaultKey;
                            item.LinkedHotkey.PendingModifiers = KeyModifierMask.None;
                            item.LinkedHotkey.HasPendingChange = true;
                        }
                        hadKeyBind = true;
                        continue;
                    }

                    if (string.Equals(entry.id, "resolution", StringComparison.Ordinal))
                    {
                        hasDisplayResolution = true;
                        continue;
                    }

                    var el = _currentWindow.Get(entry.id);
                    if (el == null) continue;

                    switch (entry.kind)
                    {
                        case GameOptionRegistry.Kind.Toggle:
                            if (VanillaDefaultBools.TryGetValue(entry.id, out bool defaultBool))
                            {
                                el.SetBool(defaultBool, true);
                            }
                            break;

                        case GameOptionRegistry.Kind.Slider:
                            if (VanillaDefaultFloats.TryGetValue(entry.id, out float defaultFloat))
                            {
                                el.SetFloat(defaultFloat, true);
                            }
                            break;

                        case GameOptionRegistry.Kind.Dropdown:
                            if (string.Equals(entry.id, "fullscreen", StringComparison.Ordinal))
                            {
                                // EmporiumOption.SetDefault(): fullscreen = true, windowed = false => index 1 (无边框全屏)
                                el.SetIndex(1, true);
                            }
                            break;
                    }
                }

                // 先刷新原版规则链，确保非 16:9 显示器下的 overriddenDropdowns 与分辨率列表在恢复全屏模式后正确重算
                InvokeRefreshRulesSafe(manager);

                if (hasDisplayResolution)
                {
                    var resEl = _currentWindow.Get("resolution");
                    if (resEl != null)
                    {
                        var overridden = OverriddenDropdownsField?.GetValue(manager) as System.Collections.IDictionary;
                        int targetResIdx = (overridden != null && overridden.Contains("resolution"))
                            ? 0
                            : GameOptionUIManager.MapResolution();

                        if (resEl.dropdown != null && resEl.dropdown.options != null && resEl.dropdown.options.Count > 0)
                        {
                            targetResIdx = Mathf.Clamp(targetResIdx, 0, resEl.dropdown.options.Count - 1);
                        }
                        else if (targetResIdx < 0)
                        {
                            targetResIdx = 0;
                        }

                        resEl.SetIndex(targetResIdx, true);
                        InvokeRefreshRulesSafe(manager);
                    }
                }

                if (hadKeyBind)
                {
                    SetKeyBindStatusDelegate?.Invoke(manager, string.Empty);
                    RefreshConflicts();
                    if (IsConflictViewActive)
                    {
                        RebuildConflictView();
                    }
                }
            }
        }

        public static void OnVanillaRulesRefreshed()
        {
            if (_currentWindow == null || _isApplyingSettings || _suppressRuleConflictRefresh) return;
            SyncVanillaPendingFromManager();
            RefreshConflicts();
        }

        public static void OnVanillaKeyChanged()
        {
            if (_currentWindow == null || _isApplyingSettings) return;
            SyncVanillaPendingFromManager();
            RefreshConflicts();
        }

        private static void SyncVanillaPendingFromManager()
        {
            if (_currentManager == null) return;
            var pendingKeys = PendingKeysField?.GetValue(_currentManager) as Dictionary<string, KeyCode>;
            if (pendingKeys == null) return;

            foreach (var hk in _allHotkeys)
            {
                if (hk.ModId != "vanilla" || string.IsNullOrEmpty(hk.RawOptionId)) continue;
                if (pendingKeys.TryGetValue(hk.RawOptionId, out KeyCode pk))
                {
                    if (!hk.HasPendingChange || hk.PendingKey != pk)
                    {
                        hk.PendingModifiers = KeyModifierMask.None;
                    }
                    hk.PendingKey = pk;
                    hk.HasPendingChange = true;
                }
                else
                {
                    hk.HasPendingChange = false;
                    hk.PendingModifiers = KeyModifierMask.None;
                }
            }
        }

        private static string ComputeConflictSignature(Dictionary<KeyCombo, List<ModHotkeyEntry>> conflicts)
        {
            if (conflicts.Count == 0) return string.Empty;

            var keys = new List<KeyCombo>(conflicts.Keys);
            keys.Sort((a, b) =>
            {
                int m = ((int)a.Modifiers).CompareTo((int)b.Modifiers);
                return m != 0 ? m : ((int)a.Key).CompareTo((int)b.Key);
            });

            var sb = new System.Text.StringBuilder(conflicts.Count * 48);
            for (int i = 0; i < keys.Count; i++)
            {
                KeyCombo combo = keys[i];
                sb.Append((int)combo.Modifiers).Append(':').Append((int)combo.Key).Append('=');
                List<ModHotkeyEntry> list = conflicts[combo];
                for (int j = 0; j < list.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(list[j].Id);
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        private static void RefreshConflicts()
        {
            using (ComboKeyStore.EnterBypass())
            {
                _conflicts = ConflictDetector.Analyze(_allHotkeys);
                string sig = ComputeConflictSignature(_conflicts);
                if (!string.Equals(_lastConflictSignature, sig, StringComparison.Ordinal))
                {
                    _lastConflictSignature = sig;
                    _conflictViewDirty = true;
                }

                // 1. 刷新所有模组卡片按键按钮与冲突标签（含只读探测热键）
                foreach (var g in _modGroups)
                {
                    foreach (var h in g.Hotkeys)
                    {
                        UpdateHotkeyButtonVisual(h);
                    }
                    foreach (var ph in g.ReadOnlyProbedHotkeys)
                    {
                        UpdateHotkeyButtonVisual(ph);
                    }
                }

                // 2. 同步刷新上方原版按键文本、组合键格式与显示色（消除“蓝/灰不统一”）
                SyncVanillaKeyButtonColors();

                // 3. 刷新顶部冲突按钮状态
                UpdateConflictToggleButtonAppearance();
            }
        }

        private static void SyncVanillaKeyButtonColors()
        {
            if (_currentWindow == null) return;

            foreach (var hk in _allHotkeys)
            {
                if (hk.ModId != "vanilla" || string.IsNullOrEmpty(hk.RawOptionId)) continue;

                var uiEl = _currentWindow.Get(hk.RawOptionId);
                if (uiEl != null && _capturingEntry != hk)
                {
                    SetButtonTextFast(uiEl, FormatHotkeyComboText(hk));
                    ApplySingleButtonColor(uiEl, hk.EffectiveCombo);
                }

                if (hk.ConflictButtonElement != null && _capturingEntry != hk)
                {
                    SetButtonTextFast(hk.ConflictButtonElement, FormatHotkeyComboText(hk));
                    ApplySingleButtonColor(hk.ConflictButtonElement, hk.EffectiveCombo);
                }
            }
        }

        private static ButtonTextColor? FindButtonTextColorComponent(CustomUIElement? btn)
        {
            if (btn == null) return null;
            int cacheKey = btn.root != null ? btn.root.GetInstanceID() : btn.GetHashCode();
            if (ButtonTextColorCache.TryGetValue(cacheKey, out ButtonTextColor? cached))
            {
                return cached;
            }

            ButtonTextColor? comp = null;
            if (btn.root != null)
            {
                comp = btn.root.GetComponent<ButtonTextColor>();
            }
            if (comp == null && btn.button != null)
            {
                comp = btn.button.GetComponent<ButtonTextColor>();
            }
            if (comp == null && btn.root != null)
            {
                comp = btn.root.GetComponentInChildren<ButtonTextColor>(true);
            }
            if (comp == null && btn.text != null)
            {
                comp = btn.text.GetComponentInParent<ButtonTextColor>(true);
            }

            ButtonTextColorCache[cacheKey] = comp;
            return comp;
        }

        private static void CaptureDefaultButtonTheme(CustomUIElement? btn, bool forceRefresh = false)
        {
            if (btn == null || (!forceRefresh && _hasDefaultButtonTheme && _baseButtonFontSize > 0f && _hasBaseButtonMargin))
            {
                return;
            }
            if (btn.text != null)
            {
                if (forceRefresh || _baseButtonFontSize <= 0f)
                {
                    _baseButtonFontSize = btn.text.fontSize;
                }
                if (forceRefresh || !_hasBaseButtonMargin)
                {
                    _baseButtonMargin = btn.text.margin;
                    _hasBaseButtonMargin = true;
                }
                _defaultButtonTextColor = btn.text.color;
                _hasDefaultButtonTextColor = true;
            }

            var comp = FindButtonTextColorComponent(btn);
            if (comp != null)
            {
                if (BtcNormalField != null)
                {
                    _defaultButtonTextColor = (Color)BtcNormalField.GetValue(comp);
                    _hasDefaultButtonTextColor = true;
                }
                if (BtcHighlightField != null)
                {
                    _defaultHighlightColor = (Color)BtcHighlightField.GetValue(comp);
                }
                if (BtcPressedField != null)
                {
                    _defaultPressedColor = (Color)BtcPressedField.GetValue(comp);
                }
                _hasDefaultButtonTheme = true;
            }
        }

        private static Color GetDefaultButtonTextColor()
        {
            return _hasDefaultButtonTextColor ? _defaultButtonTextColor : Color.white;
        }

        private static void ApplyButtonTextTheme(CustomUIElement? btn, Color normalColor, bool useDefaultTheme = false)
        {
            if (btn == null) return;

            Color targetNormal = (useDefaultTheme && _hasDefaultButtonTextColor) ? _defaultButtonTextColor : normalColor;
            int cacheKey = btn.root != null ? btn.root.GetInstanceID() : btn.GetHashCode();

            var comp = FindButtonTextColorComponent(btn);
            if (AppliedButtonNormalColorCache.TryGetValue(cacheKey, out Color lastApplied) && lastApplied == targetNormal)
            {
                bool compMatches = comp == null || BtcIsHoveredField == null || !(bool)BtcIsHoveredField.GetValue(comp);
                if (compMatches && (btn.text == null || btn.text.color == targetNormal))
                {
                    return;
                }
            }

            Color targetHighlight = (useDefaultTheme && _hasDefaultButtonTheme)
                ? _defaultHighlightColor
                : Color.Lerp(targetNormal, Color.white, 0.35f);
            Color targetPressed = (useDefaultTheme && _hasDefaultButtonTheme)
                ? _defaultPressedColor
                : Color.Lerp(targetNormal, Color.black, 0.25f);

            bool isHovered = false;
            if (comp != null)
            {
                BtcNormalField?.SetValue(comp, targetNormal);
                BtcHighlightField?.SetValue(comp, targetHighlight);
                BtcPressedField?.SetValue(comp, targetPressed);
                if (BtcIsHoveredField != null)
                {
                    isHovered = (bool)BtcIsHoveredField.GetValue(comp);
                }
            }

            AppliedButtonNormalColorCache[cacheKey] = targetNormal;

            Color wantTextColor = isHovered ? targetHighlight : targetNormal;
            if (btn.text != null && btn.text.color != wantTextColor)
            {
                btn.text.color = wantTextColor;
            }
        }

        private static void SetTextIfChanged(CustomUIElement? el, string value)
        {
            if (el == null) return;
            if (el.text != null)
            {
                if (!string.Equals(el.text.text, value, StringComparison.Ordinal))
                {
                    el.text.text = value;
                }
            }
            else
            {
                el.SetText(value);
            }
        }

        private static void SetButtonTextFast(CustomUIElement? btn, string value)
        {
            if (btn == null) return;
            if (btn.text == null)
            {
                btn.SetText(value);
                return;
            }

            if (_activeButtonFont != null && btn.text.font != _activeButtonFont)
            {
                btn.text.font = _activeButtonFont;
            }

            if (btn.text.enableAutoSizing)
            {
                btn.text.enableAutoSizing = false;
            }

            bool textChanged = !string.Equals(btn.text.text, value, StringComparison.Ordinal);
            if (textChanged)
            {
                btn.text.text = value;
            }

            int cacheKey = btn.root != null ? btn.root.GetInstanceID() : btn.GetHashCode();
            if (ButtonFitSpecCache.TryGetValue(cacheKey, out ButtonFitSpec spec))
            {
                ApplySinglePassFontSize(btn.text, value, spec);
            }
        }

        private static void ApplySinglePassFontSize(TextMeshProUGUI tmp, string? text, ButtonFitSpec spec)
        {
            if (tmp.enableAutoSizing)
            {
                tmp.enableAutoSizing = false;
            }
            if (_activeButtonFont != null && tmp.font != _activeButtonFont)
            {
                tmp.font = _activeButtonFont;
            }

            if (string.IsNullOrEmpty(text))
            {
                if (!Mathf.Approximately(tmp.fontSize, spec.MaxFontSize))
                {
                    tmp.fontSize = spec.MaxFontSize;
                }
                return;
            }

            int fontId = tmp.font != null ? tmp.font.GetInstanceID() : 0;
            if (fontId == 0)
            {
                if (!Mathf.Approximately(tmp.fontSize, spec.MaxFontSize))
                {
                    tmp.fontSize = spec.MaxFontSize;
                }
                return;
            }

            int widthTenths = Mathf.RoundToInt(spec.AvailWidth * 10f);
            int heightTenths = Mathf.RoundToInt(spec.AvailHeight * 10f);
            int maxSizeTenths = Mathf.RoundToInt(spec.MaxFontSize * 10f);
            var key = (fontId, widthTenths, heightTenths, maxSizeTenths, text!);

            if (!FittedFontSizeCache.TryGetValue(key, out float fitted))
            {
                float safeWidth = spec.AvailWidth > 0f ? spec.AvailWidth * 0.96f : float.PositiveInfinity;
                float safeHeight = spec.AvailHeight > 0f ? spec.AvailHeight * 0.88f : float.PositiveInfinity;

                tmp.fontSize = spec.MaxFontSize;
                Vector2 pref = tmp.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity);
                fitted = spec.MaxFontSize;

                float scaleX = (safeWidth < float.PositiveInfinity && pref.x > safeWidth && pref.x > 0.01f) ? (safeWidth / pref.x) : 1f;
                float scaleY = (safeHeight < float.PositiveInfinity && pref.y > safeHeight && pref.y > 0.01f) ? (safeHeight / pref.y) : 1f;
                float scale = Mathf.Min(scaleX, scaleY);

                if (scale < 1f)
                {
                    float scaled = Mathf.Floor((spec.MaxFontSize * scale) * 10f) / 10f;
                    fitted = Mathf.Max(spec.MinFontSize, Mathf.Min(spec.MaxFontSize, scaled));

                    tmp.fontSize = fitted;
                    Vector2 verify = tmp.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity);
                    if ((safeWidth < float.PositiveInfinity && verify.x > safeWidth) ||
                        (safeHeight < float.PositiveInfinity && verify.y > safeHeight))
                    {
                        float rx = (safeWidth < float.PositiveInfinity && verify.x > safeWidth && verify.x > 0.01f) ? (safeWidth / verify.x) : 1f;
                        float ry = (safeHeight < float.PositiveInfinity && verify.y > safeHeight && verify.y > 0.01f) ? (safeHeight / verify.y) : 1f;
                        float refined = Mathf.Floor((fitted * Mathf.Min(rx, ry) * 0.98f) * 10f) / 10f;
                        fitted = Mathf.Max(spec.MinFontSize, Mathf.Min(fitted, refined));
                    }
                }
                if (FittedFontSizeCache.Count >= 2048)
                {
                    FittedFontSizeCache.Clear();
                }
                FittedFontSizeCache[key] = fitted;
            }

            if (!Mathf.Approximately(tmp.fontSize, fitted))
            {
                tmp.fontSize = fitted;
            }
        }

        private static void SetButtonLayoutPadding(CustomUIElement? btn, int lrPad, int tbPad, bool forceExpandChild)
        {
            if (btn?.root == null) return;
            var hlg = btn.root.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.padding = new RectOffset(lrPad, lrPad, tbPad, tbPad);
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                if (forceExpandChild)
                {
                    hlg.childForceExpandWidth = true;
                    hlg.childForceExpandHeight = true;
                }
            }
        }

        private static void StyleButtonTypography(CustomUIElement? btn, float lrMargin, float tbMargin, float fontScale, float buttonWidth = -1f, float buttonHeight = -1f, bool allowAutoShrink = false)
        {
            if (btn == null || btn.text == null) return;
            if (_activeButtonFont != null && btn.text.font != _activeButtonFont)
            {
                btn.text.font = _activeButtonFont;
            }

            float baseSize = _baseButtonFontSize > 0f ? _baseButtonFontSize : btn.text.fontSize;
            float targetSize = Mathf.Max(11f, Mathf.Round(baseSize * fontScale * 10f) / 10f);

            // 若显式指定了按钮高度（由 SetButtonLayoutPadding 负责外框内边距），避免叠加 TMP margin 导致二次压缩
            if (buttonHeight > 0f)
            {
                btn.text.margin = Vector4.zero;
            }
            else
            {
                btn.text.margin = new Vector4(lrMargin, tbMargin, lrMargin, tbMargin);
            }

            btn.text.alignment = TextAlignmentOptions.Center;
            btn.text.enableAutoSizing = false;
            btn.text.enableWordWrapping = false;
            btn.text.overflowMode = (buttonHeight > 0f && buttonHeight <= 42f)
                ? TextOverflowModes.Truncate
                : TextOverflowModes.Ellipsis;

            if (allowAutoShrink && buttonWidth > 0f)
            {
                float availHeight = buttonHeight > 0f
                    ? Mathf.Max(14f, buttonHeight - tbMargin * 2f - 2f)
                    : 40f;
                var spec = new ButtonFitSpec
                {
                    AvailWidth = Mathf.Max(20f, buttonWidth - lrMargin * 2f - 4f),
                    AvailHeight = availHeight,
                    MaxFontSize = targetSize,
                    MinFontSize = (buttonHeight > 0f && buttonHeight <= 42f) ? 9.5f : 10.5f
                };
                int cacheKey = btn.root != null ? btn.root.GetInstanceID() : btn.GetHashCode();
                ButtonFitSpecCache[cacheKey] = spec;
                ApplySinglePassFontSize(btn.text, btn.text.text, spec);
            }
            else
            {
                btn.text.fontSize = targetSize;
            }
        }

        private static void StyleNativeWidthButton(CustomUIElement? btn, float width)
        {
            if (btn == null) return;
            btn.SetFixedWidth(width);
            if (btn.text != null)
            {
                if (_activeButtonFont != null && btn.text.font != _activeButtonFont)
                {
                    btn.text.font = _activeButtonFont;
                }

                float targetSize = _baseButtonFontSize > 0f ? _baseButtonFontSize : btn.text.fontSize;
                Vector4 baseMargin = _hasBaseButtonMargin ? _baseButtonMargin : btn.text.margin;
                float leftMargin = Mathf.Max(baseMargin.x, 8f);
                float rightMargin = Mathf.Max(baseMargin.z, 8f);
                btn.text.margin = new Vector4(leftMargin, baseMargin.y, rightMargin, baseMargin.w);
                btn.text.enableWordWrapping = false;
                btn.text.overflowMode = TextOverflowModes.Ellipsis;
                btn.text.enableAutoSizing = false;

                var hlg = btn.root != null ? btn.root.GetComponent<HorizontalLayoutGroup>() : null;
                var le = btn.root != null ? btn.root.GetComponent<LayoutElement>() : null;
                float btnHeight = (le != null && le.preferredHeight > 0f) ? le.preferredHeight : 48f;
                int vPad = hlg != null ? hlg.padding.vertical : 8;
                float availHeight = Mathf.Max(18f, btnHeight - vPad - baseMargin.y - baseMargin.w);

                var spec = new ButtonFitSpec
                {
                    AvailWidth = Mathf.Max(20f, width - leftMargin - rightMargin - 4f),
                    AvailHeight = availHeight,
                    MaxFontSize = targetSize,
                    MinFontSize = 10.5f
                };
                int cacheKey = btn.root != null ? btn.root.GetInstanceID() : btn.GetHashCode();
                ButtonFitSpecCache[cacheKey] = spec;
                ApplySinglePassFontSize(btn.text, btn.text.text, spec);
            }
        }

        private static void StyleHotkeyButton(CustomUIElement? btn)
        {
            StyleNativeWidthButton(btn, KeybindButtonWidth);
        }

        private static void StyleActionButton(CustomUIElement? btn, float width, float height, float lrMargin = 10f, float tbMargin = 2f, float fontScale = 0.88f)
        {
            if (btn == null) return;
            btn.SetFixedWidth(width);
            btn.SetPreferredSize(width, height);
            btn.SetFlexible(0f, 0f);
            SetButtonLayoutPadding(btn, Mathf.RoundToInt(lrMargin), Mathf.RoundToInt(tbMargin), forceExpandChild: true);
            StyleButtonTypography(btn, lrMargin, tbMargin, fontScale, width, height, allowAutoShrink: true);
        }

        private static void StyleFoldButton(CustomUIElement? btn, float size)
        {
            if (btn == null) return;
            btn.SetFixedWidth(size);
            btn.SetPreferredSize(size, size);
            btn.SetFlexible(0f, 0f);
            // 清空 Meta_Button 默认 (16,16,4,4) 内边距，避免 36px 方形折叠按钮内部 TextRectWidth 被压扁
            SetButtonLayoutPadding(btn, 0, 0, forceExpandChild: true);
            if (btn.text != null)
            {
                if (_activeButtonFont != null && btn.text.font != _activeButtonFont)
                {
                    btn.text.font = _activeButtonFont;
                }
                float baseSize = _baseButtonFontSize > 0f ? _baseButtonFontSize : btn.text.fontSize;
                btn.text.enableAutoSizing = false;
                btn.text.enableWordWrapping = false;
                btn.text.overflowMode = TextOverflowModes.Overflow;
                btn.text.fontSize = Mathf.Clamp(Mathf.Round(baseSize * 0.58f), 13f, 16f);
                btn.text.alignment = TextAlignmentOptions.Center;
                btn.text.margin = Vector4.zero;
            }
        }

        private static void SyncActionButtonsAfterShow()
        {
            if (I18n.IsChinese && FontLoader.Instance != null && FontLoader.Instance.simplifiedChineseFont != null)
            {
                _activeButtonFont = FontLoader.Instance.simplifiedChineseFont;
            }
            else if (_expandToggleBtn?.text?.font != null)
            {
                _activeButtonFont = _expandToggleBtn.text.font;
            }

            SyncCompactActionButton(_conflictToggleBtn);
            SyncCompactActionButton(_expandToggleBtn);
            SyncCompactActionButton(_diagnosticToggleBtn);

            for (int i = 0; i < _vanillaCards.Count; i++)
            {
                var vc = _vanillaCards[i];
                StyleFoldButton(vc.FoldArrowElement, 36f);
                if (vc.IsExpanded) SyncCompactActionButton(vc.ResetButtonElement);
            }

            for (int i = 0; i < _modGroups.Count; i++)
            {
                var mg = _modGroups[i];
                StyleFoldButton(mg.FoldArrowElement, 36f);
                if (mg.IsExpanded) SyncCompactActionButton(mg.ResetButtonElement);
            }

            UpdateConflictToggleButtonAppearance();
            UpdateDiagnosticToggleButtonAppearance();
        }

        internal static void SyncCompactActionButton(CustomUIElement? btn)
        {
            if (btn?.text == null) return;
            if (_activeButtonFont != null && btn.text.font != _activeButtonFont)
            {
                btn.text.font = _activeButtonFont;
            }
            int cacheKey = btn.root != null ? btn.root.GetInstanceID() : btn.GetHashCode();
            if (ButtonFitSpecCache.TryGetValue(cacheKey, out ButtonFitSpec spec))
            {
                ApplySinglePassFontSize(btn.text, btn.text.text, spec);
            }
        }

        private static void StyleVanillaKeyButtons()
        {
            if (_currentWindow == null) return;
            foreach (var hk in _allHotkeys)
            {
                if (hk.ModId != "vanilla" || string.IsNullOrEmpty(hk.RawOptionId)) continue;
                var el = _currentWindow.Get(hk.RawOptionId);
                StyleHotkeyButton(el);
            }
        }

        private static void ApplySingleButtonColor(CustomUIElement btn, KeyCombo combo, bool isReadOnlyProbed = false)
        {
            if (combo.IsEmpty)
            {
                ApplyButtonTextTheme(btn, ConflictDetector.EmptyKeyColor);
            }
            else if (_conflicts.ContainsKey(combo))
            {
                ApplyButtonTextTheme(btn, ConflictDetector.ConflictColor);
            }
            else if (isReadOnlyProbed)
            {
                ApplyButtonTextTheme(btn, new Color(0.76f, 0.72f, 0.92f, 1f));
            }
            else
            {
                ApplyButtonTextTheme(btn, GetDefaultButtonTextColor(), useDefaultTheme: true);
            }
        }

        private static void UpdateHotkeyButtonVisual(ModHotkeyEntry hotkey)
        {
            if (hotkey.LabelElement != null)
            {
                SetTextIfChanged(hotkey.LabelElement, BuildHotkeyLabelText(hotkey));
            }

            string formatted = FormatHotkeyComboText(hotkey);

            if (hotkey.ButtonElement != null && _capturingEntry != hotkey)
            {
                SetButtonTextFast(hotkey.ButtonElement, formatted);
                ApplySingleButtonColor(hotkey.ButtonElement, hotkey.EffectiveCombo, hotkey.IsReadOnlyProbed);
            }

            if (hotkey.ConflictButtonElement != null && _capturingEntry != hotkey)
            {
                SetButtonTextFast(hotkey.ConflictButtonElement, formatted);
                ApplySingleButtonColor(hotkey.ConflictButtonElement, hotkey.EffectiveCombo, hotkey.IsReadOnlyProbed);
            }
        }

        private static void RefreshCardUI(ModOptionGroup group)
        {
            foreach (var gen in group.GeneralSettings)
            {
                gen.PendingBool = null;
                gen.PendingFloat = null;
                gen.PendingIndex = null;

                if (gen.Kind == OptionKind.Toggle && gen.ControlElement != null)
                {
                    gen.ControlElement.SetBool(gen.GetBool?.Invoke() ?? false, false);
                }
                else if (gen.Kind == OptionKind.Slider && gen.ControlElement != null)
                {
                    float val = gen.GetFloat?.Invoke() ?? 0f;
                    gen.ControlElement.SetFloat(val, false);
                    gen.ValueLabelElement?.SetText(FormatSliderValue(gen, val));
                }
                else if (gen.Kind == OptionKind.Dropdown && gen.ControlElement != null)
                {
                    gen.ControlElement.SetIndex(gen.GetIndex?.Invoke() ?? 0, false);
                }
            }

            foreach (var hotkey in group.Hotkeys)
            {
                hotkey.HasPendingChange = false;
                hotkey.PendingModifiers = KeyModifierMask.None;
                UpdateHotkeyButtonVisual(hotkey);
            }
            foreach (var probed in group.ReadOnlyProbedHotkeys)
            {
                UpdateHotkeyButtonVisual(probed);
            }

            RefreshConflicts();
            if (IsConflictViewActive)
            {
                RebuildConflictView();
            }
        }

        private static string FormatHotkeyComboText(ModHotkeyEntry hotkey)
        {
            KeyCode key = hotkey.EffectiveKey;
            if (key == KeyCode.None)
            {
                return I18n.Get("无", "None");
            }
            return ComboKeyStore.FormatCombo(hotkey.EffectiveModifiers, key);
        }

        private static string FormatSliderValue(ModGeneralEntry gen, float val)
        {
            if (gen.ValueFormatter != null) return gen.ValueFormatter(val);
            if (gen.WholeNumbers) return Mathf.RoundToInt(val).ToString();
            return val.ToString("0.0");
        }

        private static void NotifyGroupConfigsApplied(ModOptionGroup group)
        {
            if (group == null) return;
            NotifyModConfigApplied(group.ModId);
            if (group.ExtraNotifyModIds != null && group.ExtraNotifyModIds.Count > 0)
            {
                foreach (var extraId in group.ExtraNotifyModIds)
                {
                    if (!string.IsNullOrEmpty(extraId) && !string.Equals(extraId, group.ModId, StringComparison.OrdinalIgnoreCase))
                    {
                        NotifyModConfigApplied(extraId);
                    }
                }
            }
        }

        private static void NotifyModConfigApplied(string modId)
        {
            try
            {
                if (ModHook.Instance != null && FireModConfigAppliedDelegate != null)
                {
                    FireModConfigAppliedDelegate.Invoke(modId);
                }
            }
            catch { }
        }

        private static void ReopenEnhancedUIForLanguageSwitch(GameOptionUIManager manager, string? focusModId)
        {
            if (manager == null) return;

            string prevQuery = _searchQuery;
            SearchScope prevScope = _searchScope;

            ModCatalogCache.Invalidate();
            OpenEnhancedUI(manager);

            if (prevScope != SearchScope.All)
            {
                _searchScope = prevScope;
                _currentWindow?.Get("unified_search_scope")?.SetIndex((int)prevScope, false);
            }
            if (!string.IsNullOrEmpty(prevQuery))
            {
                _searchQuery = prevQuery;
                if (_searchInputField != null)
                {
                    _searchInputField.SetTextWithoutNotify(prevQuery);
                }
                ApplySearchFilter();
            }
            else if (!string.IsNullOrEmpty(focusModId))
            {
                JumpToModInNormalView(focusModId!, null);
            }
        }

        // =====================================================================
        // 保存与持久化 (OnApplySetting 挂钩)
        // =====================================================================
        private static void ApplyAllSettings(GameOptionUIManager manager)
        {
            CancelCapture();
            bool wasChinese = I18n.IsChinese;
            bool selfModLanguageChanged = false;

            // 1. 同步原版 PendingKeys 并写入组合键修饰位
            var pendingKeys = PendingKeysField?.GetValue(manager) as Dictionary<string, KeyCode>;
            bool comboChanged = false;
            foreach (var hk in _allHotkeys)
            {
                if (hk.ModId == "vanilla" && hk.HasPendingChange && !string.IsNullOrEmpty(hk.RawOptionId))
                {
                    if (pendingKeys != null)
                    {
                        pendingKeys[hk.RawOptionId] = hk.PendingKey;
                    }
                    ComboKeyStore.SetSavedCombo(hk.Id, hk.PendingModifiers, hk.PendingKey);
                    comboChanged = true;
                    hk.PendingModifiers = KeyModifierMask.None;
                    hk.HasPendingChange = false;
                }
            }

            // 2. 执行原版原生保存逻辑（屏蔽期间触发的 RefreshRules 回调，并清空已提交的原版 pendingKeys）
            _isApplyingSettings = true;
            try
            {
                using (ComboKeyStore.EnterBypass())
                {
                    OriginalApplySettingDelegate?.Invoke(manager);
                }
                pendingKeys?.Clear();
            }
            finally
            {
                _isApplyingSettings = false;
            }

            // 3. 保存所有发生变动的模组常规配置与模组快捷键（含组合键）
            var touchedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in _modGroups)
            {
                bool groupChanged = false;

                foreach (var gen in group.GeneralSettings)
                {
                    if (gen.PendingBool.HasValue || gen.PendingFloat.HasValue || gen.PendingIndex.HasValue)
                    {
                        if (string.Equals(group.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase))
                        {
                            selfModLanguageChanged = true;
                        }
                        gen.ApplyPending();
                        groupChanged = true;
                    }
                }

                foreach (var hotkey in group.Hotkeys)
                {
                    if (hotkey.HasPendingChange)
                    {
                        if (hotkey.SetCombo != null)
                        {
                            hotkey.SetCombo(hotkey.PendingModifiers, hotkey.PendingKey);
                            ComboKeyStore.SetSavedCombo(hotkey.Id, KeyModifierMask.None, KeyCode.None);
                            comboChanged = true;
                        }
                        else
                        {
                            hotkey.SetKey?.Invoke(hotkey.PendingKey);
                            ComboKeyStore.SetSavedCombo(hotkey.Id, hotkey.PendingModifiers, hotkey.PendingKey);
                            comboChanged = true;
                        }
                        hotkey.PendingModifiers = KeyModifierMask.None;
                        hotkey.PendingKey = KeyCode.None;
                        hotkey.HasPendingChange = false;
                        groupChanged = true;
                    }
                }

                if (groupChanged)
                {
                    touchedModIds.Add(group.ModId);
                    if (group.ExtraNotifyModIds != null)
                    {
                        foreach (var extraId in group.ExtraNotifyModIds)
                        {
                            if (!string.IsNullOrEmpty(extraId))
                            {
                                touchedModIds.Add(extraId);
                            }
                        }
                    }
                }
            }

            if (comboChanged)
            {
                ComboKeyStore.Save();
            }
            ComboKeyStore.RebuildRuntimeLookup(_allHotkeys);

            // 4. 触发通知链，使第三方模组即时热生效
            foreach (var modId in touchedModIds)
            {
                NotifyModConfigApplied(modId);
            }

            if (wasChinese != I18n.IsChinese)
            {
                ReopenEnhancedUIForLanguageSwitch(manager, selfModLanguageChanged ? I18n.ModId : null);
                return;
            }

            // 5. 重新刷新冲突与颜色状态
            RefreshConflicts();
            if (IsConflictViewActive)
            {
                RebuildConflictView();
            }
            if (touchedModIds.Count > 0 || comboChanged)
            {
                _diagnosticViewDirty = true;
                if (IsDiagnosticViewActive)
                {
                    RebuildDiagnosticView();
                }
            }
        }
    }
}
