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
        private static readonly Dictionary<ModOptionGroup, (GameObject? hkHeader, GameObject? genHeader, GameObject? midSep)> CardParts
            = new Dictionary<ModOptionGroup, (GameObject?, GameObject?, GameObject?)>();

        // UI 元素级高性能缓存（窗口关闭时清理）
        private struct ButtonFitSpec
        {
            public float AvailWidth;
            public float MaxFontSize;
            public float MinFontSize;
        }

        private static readonly Dictionary<int, ButtonTextColor?> ButtonTextColorCache = new Dictionary<int, ButtonTextColor?>(128);
        private static readonly Dictionary<int, Color> AppliedButtonNormalColorCache = new Dictionary<int, Color>(128);
        private static readonly Dictionary<int, ButtonFitSpec> ButtonFitSpecCache = new Dictionary<int, ButtonFitSpec>(128);

        // 跨打开会话可复用的单次字体缩放尺寸缓存 (fontInstanceId, availWidth*10, maxFontSize*10, text) -> fittedFontSize
        private static readonly Dictionary<(int fontId, int widthTenths, int maxSizeTenths, string text), float> FittedFontSizeCache
            = new Dictionary<(int, int, int, string), float>(256);

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
        private static string _searchQuery = string.Empty;
        private static SearchScope _searchScope = SearchScope.All;
        private static bool _allExpanded = false;
        private static bool _wasSearchNonEmpty = false;

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
        private static GameObject? _normalContentRoot;
        private static GameObject? _conflictContentRoot;
        private static GameObject? _vanillaHeaderRow;
        private static GameObject? _vanillaHeaderSep;
        private static GameObject? _modHeaderSpace;
        private static GameObject? _modHeaderSep;
        private static GameObject? _modHeaderRow;
        private static GameObject? _modHeaderBottomSep;
        private static GameObject? _emptySearchLabelRow;
        private static CustomUIElement? _conflictToggleBtn;
        private static CustomUIElement? _expandToggleBtn;
        private static TMP_InputField? _searchInputField;

        // 缓存 ButtonTextColor 私有色彩字段（OnPointerEnter/Exit/Up/Disable 会使用 normal/highlight/pressed 覆写 text.color）
        private static readonly FieldInfo? BtcNormalField = AccessTools.Field(typeof(ButtonTextColor), "normal");
        private static readonly FieldInfo? BtcHighlightField = AccessTools.Field(typeof(ButtonTextColor), "highlight");
        private static readonly FieldInfo? BtcPressedField = AccessTools.Field(typeof(ButtonTextColor), "pressed");
        private static readonly FieldInfo? BtcIsHoveredField = AccessTools.Field(typeof(ButtonTextColor), "isHovered");

        // 反射缓存原版 GameOptionUIManager 私有成员
        private static readonly MethodInfo? BuildEntryMethod = AccessTools.Method(typeof(GameOptionUIManager), "BuildEntry");
        private static readonly MethodInfo? NoteIdMethod = AccessTools.Method(typeof(GameOptionUIManager), "NoteId");
        private static readonly MethodInfo? OriginalApplySettingMethod = AccessTools.Method(typeof(GameOptionUIManager), "OnApplySetting");
        private static readonly MethodInfo? RefreshRulesMethod = AccessTools.Method(typeof(GameOptionUIManager), "RefreshRules");
        private static readonly MethodInfo? SetKeyBindStatusMethod = AccessTools.Method(typeof(GameOptionUIManager), "SetKeyBindStatus");
        private static readonly MethodInfo? FireModConfigAppliedMethod = AccessTools.Method(typeof(ModHook), "FireOnModConfigApplied", new Type[] { typeof(string) });
        private static readonly FieldInfo? PendingKeysField = AccessTools.Field(typeof(GameOptionUIManager), "pendingKeys");
        private static readonly FieldInfo? OverriddenDropdownsField = AccessTools.Field(typeof(GameOptionUIManager), "overriddenDropdowns");
        private static readonly FieldInfo? WindowField = AccessTools.Field(typeof(GameOptionUIManager), "window");
        private static readonly FieldInfo? ConsumedEscapeFrameField = AccessTools.Field(typeof(GameOptionUIManager), "consumedEscapeFrame");

        /// <summary>
        /// 打开全新统一设置面板
        /// </summary>
        private static void ClearSessionUiCaches()
        {
            ButtonTextColorCache.Clear();
            AppliedButtonNormalColorCache.Clear();
            ButtonFitSpecCache.Clear();
            CardParts.Clear();
        }

        public static void OpenEnhancedUI(GameOptionUIManager manager)
        {
            _currentManager = manager;
            _currentWindow = null;
            IsConflictViewActive = false;
            _conflictViewDirty = true;
            _lastConflictSignature = null;
            _suppressRuleConflictRefresh = false;
            _searchQuery = string.Empty;
            _wasSearchNonEmpty = false;
            _pendingScrollTarget = null;
            _scrollRect = null;
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
            SetKeyBindStatusMethod?.Invoke(manager, new object[] { string.Empty });

            // 2. 加载全量模组配置并分析按键冲突
            _modGroups = ModCatalogCache.GetOrScanGroups(out _allHotkeys);
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
                _pendingScrollTarget = null;
                _capturingEntry = null;
                _restoreCloseOnEscapeFrame = -1;
                _waitingMouseRelease = false;
                _pendingStandaloneModifier = KeyCode.None;
                _hadMultipleModifiers = false;
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
                new List<string> { "整体搜索", "按键+常规", "仅搜按键", "仅搜常规" },
                (int)_searchScope,
                OnSearchScopeChanged,
                "unified_search_scope"
            );
            builder.Last.SetFixedWidth(148f);
            builder.Last.SetPreferredSize(148f, 36f);

            builder.AddInput(
                "搜索设置项或按键 (支持繁简中文、英文、拼音如 sp=商品)...",
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
            }

            builder.AddSpace(10f);
            builder.End();

            builder.AddSpace(6f);

            // 第二行：仅看冲突按钮 + 全部展开/收起按钮 + 快捷提示
            builder.BeginRow(12f, TextAnchor.MiddleCenter);
            builder.AddSpace(10f);

            string conflictBtnText = _conflicts.Count > 0
                ? $"[!] 仅看冲突 ({_conflicts.Count} 组)"
                : "[!] 仅看冲突";
            builder.AddButton(conflictBtnText, ToggleConflictMode, "btn_conflict_toggle");
            _conflictToggleBtn = builder.Last;

            builder.AddButton(_allExpanded ? "全部收起" : "全部展开", ToggleExpandAll, "btn_expand_all");
            _expandToggleBtn = builder.Last;

            // 优先抓取原生 ButtonTextColor 默认字色、高亮色、按下色与原始字号（每个层级 menu/meta 仅需首开抓取一次）
            if (!hasThemeForLayer)
            {
                CaptureDefaultButtonTheme(_expandToggleBtn, forceRefresh: true);
                _capturedThemeLayer = layer;
            }

            // 抓取完成后即关闭根节点激活态，确保整页百余个原版与模组控件在离线非激活状态一次性装配完毕
            if (windowRoot != null && windowRoot.activeSelf)
            {
                windowRoot.SetActive(false);
            }

            StyleActionButton(_conflictToggleBtn, 226f, 38f, lrMargin: 14f, tbMargin: 4f, fontScale: 0.88f);
            StyleActionButton(_expandToggleBtn, 144f, 38f, lrMargin: 14f, tbMargin: 4f, fontScale: 0.88f);

            UpdateConflictToggleButtonAppearance();

            builder.AddFlexibleSpace();
            builder.AddLabel("<color=#99a3b0>支持 Shift/Ctrl/Alt 组合键与鼠标侧键 | Esc=无</color>", "label_top_hint");
            builder.AddSpace(10f);
            builder.End();

            builder.AddSeparator(null, 2);

            // =================================================================
            // 5. 滚动视窗核心区域（折叠卡片懒加载，消除打开选项页卡顿）
            // =================================================================
            builder.BeginScrollFlexible(0f);
            _scrollRect = builder.CurrentContainer.GetComponentInParent<ScrollRect>();

            // --- A. 常规完整设置视图容器 ---
            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            _normalContentRoot = builder.CurrentContainer.gameObject;
            var normalColumnRt = builder.CurrentContainer;

            // (1) 原版官方设置与快捷键（统一可折叠卡片 + 支持全范围搜索）
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            _vanillaHeaderRow = builder.CurrentContainer.gameObject;
            builder.AddSpace(6f);
            builder.AddHeader("游戏原生设置与快捷键", "header_vanilla_sections");
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
            builder.AddHeader("创意工坊模组设置与快捷键", "header_mod_sections");
            builder.End();
            builder.AddSeparator(null, 2);
            _modHeaderBottomSep = GetLastChildGameObject(normalColumnRt);

            // (3) 创意工坊模组折叠卡片列表（初始仅构建卡片头部外壳，展开/搜索时按需懒构建内部控件）
            using (ComboKeyStore.EnterBypass())
            {
                foreach (var group in _modGroups)
                {
                    BuildModGroupCardShell(builder, group);
                }

                // 空搜索结果提示（默认隐藏）
                builder.BeginRow(4f, TextAnchor.MiddleCenter);
                _emptySearchLabelRow = builder.CurrentContainer.gameObject;
                builder.AddSpace(10f);
                builder.AddFlexibleSpace();
                builder.AddLabel("<color=#99a3b0>未找到匹配的设置项或按键，可尝试更换关键词或切换左侧搜索范围</color>", "label_empty_search");
                builder.AddFlexibleSpace();
                builder.AddSpace(10f);
                builder.End();
                _emptySearchLabelRow.SetActive(false);

                builder.End(); // 结束 _normalContentRoot Column

                // --- B. 冲突专属速览视图容器（初始为空并隐藏，点击“仅看冲突”时按需懒构建） ---
                builder.BeginColumn(2f, TextAnchor.UpperLeft);
                _conflictContentRoot = builder.CurrentContainer.gameObject;
                _conflictContentRoot.SetActive(false);
            }

            builder.End(); // 结束 _conflictContentRoot Column
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
            WindowField?.SetValue(manager, _currentWindow);
            _suppressRuleConflictRefresh = true;
            try
            {
                RefreshRulesMethod?.Invoke(manager, null);
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
            out CustomUIElement foldBtn)
        {
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            headerRow = builder.CurrentContainer.gameObject;
            builder.AddSpace(6f);

            builder.AddButton(initialExpanded ? "▼" : "▶", onFoldClicked, foldButtonId);
            foldBtn = builder.Last;
            StyleFoldButton(foldBtn, 34f);

            string badge = $"<color=#99a3b0>(按键: {hotkeyCount} | 常规: {generalCount})</color>";
            builder.AddLabel($"<b>{title}</b> {badge}", labelId);

            builder.AddFlexibleSpace();
            buildRightContent?.Invoke(builder);

            if (onResetClicked != null)
            {
                if (buildRightContent != null)
                {
                    builder.AddSpace(6f);
                }
                builder.AddButton("恢复默认", () =>
                {
                    CancelCapture();
                    onResetClicked();
                }, resetButtonId);
                card.ResetButtonElement = builder.Last;
                StyleActionButton(card.ResetButtonElement, 112f, 34f, lrMargin: 10f, tbMargin: 3f, fontScale: 0.86f);
                card.ResetButtonElement.SetVisible(initialExpanded && card.ResetToDefaultsAction != null);
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

            string noteId = NoteIdMethod?.Invoke(null, new object[] { section }) as string ?? ("note_" + section.id);
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
                BuildEntryMethod?.Invoke(manager, new object[] { builder, entry });

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
                    using (ComboKeyStore.EnterBypass())
                    {
                        group.ResetToDefaultsAction?.Invoke();
                        RefreshCardUI(group);
                        ComboKeyStore.RebuildRuntimeLookup(_allHotkeys);
                    }
                    NotifyModConfigApplied(group.ModId);
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
                out CustomUIElement foldBtn
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
                GameObject? midSepObj = null;

                // --- 上半截：快捷键绑定 ---
                if (group.Hotkeys.Count > 0)
                {
                    builder.BeginRow(4f, TextAnchor.MiddleLeft);
                    hkHeaderObj = builder.CurrentContainer.gameObject;
                    builder.AddSpace(18f);
                    builder.AddLabel("<color=#9ec4ff><b>快捷键</b></color>", "hk_hdr_" + group.ModId);
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
                    builder.AddLabel("<color=#a8d8b9><b>常规设置</b></color>", "gen_hdr_" + group.ModId);
                    builder.End();

                    foreach (var gen in group.GeneralSettings)
                    {
                        BuildGeneralSettingRow(builder, gen);
                    }
                }

                builder.AddSpace(4f);
                CardParts[group] = (hkHeaderObj, genHeaderObj, midSepObj);
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
                    gen.ValueLabelElement.SetFixedWidth(58f);

                    builder.AddSlider(gen.Min, gen.Max, currentFloat, (val) =>
                    {
                        gen.PendingFloat = val;
                        gen.ValueLabelElement?.SetText(FormatSliderValue(gen, val));
                    }, gen.WholeNumbers, "slider_" + gen.Id);
                    gen.ControlElement = builder.Last;
                    gen.ControlElement.SetFixedWidth(242f);
                    break;

                case OptionKind.Dropdown:
                    var choices = gen.GetChoices?.Invoke() ?? new List<string>();
                    int currentIdx = gen.GetIndex?.Invoke() ?? 0;
                    builder.AddDropdown(choices, currentIdx, (idx) =>
                    {
                        gen.PendingIndex = idx;
                    }, "drop_" + gen.Id);
                    gen.ControlElement = builder.Last;
                    gen.ControlElement.SetFixedWidth(306f);
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

            builder.AddLabel(BuildHotkeyLabelText(hotkey), "label_" + hotkey.Id);
            hotkey.LabelElement = builder.Last;

            builder.AddFlexibleSpace();

            string keyText = FormatHotkeyComboText(hotkey);
            builder.AddButton(keyText, () =>
            {
                StartCaptureHotkey(hotkey);
            }, "btn_key_" + hotkey.Id);

            hotkey.ButtonElement = builder.Last;
            StyleHotkeyButton(hotkey.ButtonElement);

            if (_capturingEntry != hotkey)
            {
                ApplySingleButtonColor(hotkey.ButtonElement, hotkey.EffectiveCombo);
            }

            builder.AddSpace(10f);
            builder.End();
        }

        private static string BuildHotkeyLabelText(ModHotkeyEntry hotkey)
        {
            string labelText = hotkey.DisplayName;
            string conflictTip = ConflictDetector.GetConflictTooltip(hotkey, _conflicts);
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
                ? $"<color=#ffb347><b>当前共有 {sortedConflicts.Count} 组按键发生冲突！</b></color> (点击下方按键可直接改绑，按 Esc 设为空)"
                : "<color=#55dd88><b>太棒了！当前没有任何按键冲突。</b></color>";
            builder.AddHeader(headerText, "header_conflict_status");
            builder.AddSpace(8f);
            builder.End();
            builder.AddSeparator(null, 2);

            foreach (var cg in sortedConflicts)
            {
                string keyName = ComboKeyStore.FormatCombo(cg.Combo.Modifiers, cg.Combo.Key);
                builder.BeginRow(4f, TextAnchor.MiddleLeft);
                builder.AddSpace(8f);
                builder.AddLabel($"<b>【 冲突按键: <color=#ffb347>{keyName}</color> 】</b> (共 {cg.ConflictingEntries.Count} 个功能重叠)", $"cg_header_{(int)cg.Combo.Modifiers}_{(int)cg.Combo.Key}");
                builder.End();

                foreach (var entry in cg.ConflictingEntries)
                {
                    builder.BeginRow(6f, TextAnchor.MiddleLeft);
                    builder.AddSpace(20f);
                    builder.AddLabel($"[{entry.ModDisplayName}]  {entry.DisplayName}", "cg_label_" + entry.Id);
                    builder.AddFlexibleSpace();

                    builder.AddButton(FormatHotkeyComboText(entry), () =>
                    {
                        StartCaptureHotkey(entry);
                    }, "cg_btn_" + entry.Id);

                    var btn = builder.Last;
                    StyleHotkeyButton(btn);
                    entry.ConflictButtonElement = btn;
                    ApplySingleButtonColor(btn, entry.EffectiveCombo);

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

        private const string CapturePrompt = "请按键... (Esc=无)";

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

            SetKeyBindStatusMethod?.Invoke(
                _currentManager,
                new object[] { $"正在为 [{entry.ModDisplayName}: {entry.DisplayName}] 绑定按键 (支持组合键与鼠标侧键；按 Esc 设为空，左键取消)" }
            );
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
        /// 窗口开启后的按帧预算后台静默预建：在约 3.5ms/帧 的平滑预算内于折叠态(inactive)容器连续构建多张卡片，
        /// 既不在开启首帧产生压力，又能极快地预热全量卡片，使“全部展开”与搜索秒开且完全无过渡动画。
        /// </summary>
        private static void PrebuildNextCollapsedModCard()
        {
            if (_currentWindow == null || IsConflictViewActive || _capturingEntry != null || !string.IsNullOrWhiteSpace(_searchQuery))
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

                if (PrebuildStopwatch.Elapsed.TotalMilliseconds >= 3.5)
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
            // 搜索后延迟 1 帧滚动校准，确保 ContentSizeFitter 与 VerticalLayoutGroup 已更新最新高度
            if (_pendingScrollTarget != null && _currentWindow != null)
            {
                var target = _pendingScrollTarget;
                _pendingScrollTarget = null;
                ScrollToTargetImmediate(target);
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

            KeyModifierMask heldModifiers = ComboKeyStore.GetHeldModifiersMask();

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
                        KeyModifierMask finalMods = ComboKeyStore.GetHeldModifiersMask(kc);
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

                KeyModifierMask afterMods = ComboKeyStore.GetHeldModifiersMask();
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
            SetKeyBindStatusMethod?.Invoke(_currentManager, new object[] { string.Empty });
        }

        private static void FinishCapture(KeyModifierMask newMods, KeyCode newKey, bool fromMouseClick)
        {
            if (_capturingEntry == null) return;

            var entry = _capturingEntry;
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
            SetKeyBindStatusMethod?.Invoke(_currentManager, new object[] { string.Empty });
        }

        // =====================================================================
        // 搜索过滤与作用域切换 (支持 整体 / 按键+常规 / 仅按键 / 仅常规)
        // =====================================================================
        private static void OnSearchScopeChanged(int index)
        {
            if (index >= 0 && index <= (int)SearchScope.GeneralOnly)
            {
                _searchScope = (SearchScope)index;
                ApplySearchFilter();
            }
        }

        private static void OnSearchTyping(string text)
        {
            _searchQuery = text;
            if (IsConflictViewActive && !string.IsNullOrWhiteSpace(text))
            {
                ToggleConflictMode();
            }
            ApplySearchFilter();
        }

        private static void OnSearchChanged(string text)
        {
            _searchQuery = text;
            if (IsConflictViewActive && !string.IsNullOrWhiteSpace(text))
            {
                ToggleConflictMode();
            }
            ApplySearchFilter();
        }

        private static GameObject? _lastSearchScrollTarget;

        private static bool IsHotkeyMatchQuery(ModHotkeyEntry hotkey, string query)
        {
            return PinyinEngine.Matches(hotkey.DisplayName, query) ||
                   PinyinEngine.Matches(FormatHotkeyComboText(hotkey), query) ||
                   PinyinEngine.Matches(KeyBindHelper.KeyName(hotkey.EffectiveKey), query) ||
                   PinyinEngine.Matches(hotkey.EffectiveKey.ToString(), query);
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
                                if (CardParts.TryGetValue(group, out var parts))
                                {
                                    SetObjectActive(parts.hkHeader, group.Hotkeys.Count > 0);
                                    SetObjectActive(parts.genHeader, group.GeneralSettings.Count > 0);
                                    SetObjectActive(parts.midSep, group.Hotkeys.Count > 0 && group.GeneralSettings.Count > 0);
                                }
                            }

                            SetObjectActive(group.HeaderRowObject, true);
                            SetObjectActive(group.OuterSeparatorObject, true);
                            group.SetExpanded(group.IsExpanded);
                            visibleModCount++;
                            continue;
                        }

                        bool groupNameMatch = IsModGroupNameMatch(group, _searchQuery, allowGroupName);
                        bool anyHotkeyMatch = allowHotkeys && group.Hotkeys.Any(hk => IsHotkeyMatchQuery(hk, _searchQuery));
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
            }
            else if (!hasQuery && _wasSearchNonEmpty)
            {
                _wasSearchNonEmpty = false;
                _pendingScrollTarget = null;
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

            if (CardParts.TryGetValue(group, out var matchedParts))
            {
                SetObjectActive(matchedParts.hkHeader, visibleHotkeys > 0);
                SetObjectActive(matchedParts.genHeader, visibleGeneral > 0);
                SetObjectActive(matchedParts.midSep, visibleHotkeys > 0 && visibleGeneral > 0);
            }

            ShowCardExpandedVisualOnly(group);
        }

        private static void ScrollToTargetImmediate(GameObject targetObj)
        {
            if (_scrollRect == null || targetObj == null) return;
            var contentRt = _scrollRect.content;
            var targetRt = targetObj.GetComponent<RectTransform>();
            var viewportRt = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : (_scrollRect.transform as RectTransform);
            if (contentRt == null || targetRt == null || viewportRt == null) return;

            if (_normalContentRoot != null)
            {
                var normalRt = _normalContentRoot.GetComponent<RectTransform>();
                if (normalRt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(normalRt);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
            Canvas.ForceUpdateCanvases();

            float viewportHeight = viewportRt.rect.height;
            float contentHeight = contentRt.rect.height;
            float scrollableHeight = contentHeight - viewportHeight;
            if (scrollableHeight <= 1f)
            {
                _scrollRect.verticalNormalizedPosition = 1f;
                return;
            }

            Vector3[] corners = new Vector3[4];
            targetRt.GetWorldCorners(corners); // corners[1] 为左上角
            Vector3 itemTopInContent = contentRt.InverseTransformPoint(corners[1]);
            float distanceFromTop = Mathf.Max(0f, contentRt.rect.yMax - itemTopInContent.y - 8f);
            float normalized = 1f - Mathf.Clamp01(distanceFromTop / scrollableHeight);
            _scrollRect.verticalNormalizedPosition = normalized;
        }

        private static void ResetScrollToTop()
        {
            if (_scrollRect == null) return;
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 1f;
        }

        // =====================================================================
        // 辅助切换与全量色彩 / 冲突同步 + 按钮字体样式与悬停持久染色
        // =====================================================================
        private static void ToggleConflictMode()
        {
            CancelCapture();
            IsConflictViewActive = !IsConflictViewActive;

            if (IsConflictViewActive)
            {
                RefreshConflicts();
                RebuildConflictView();
            }

            if (_normalContentRoot != null) _normalContentRoot.SetActive(!IsConflictViewActive);
            if (_conflictContentRoot != null) _conflictContentRoot.SetActive(IsConflictViewActive);

            UpdateConflictToggleButtonAppearance();
            ResetScrollToTop();
        }

        private static void UpdateConflictToggleButtonAppearance()
        {
            if (_conflictToggleBtn == null) return;

            if (IsConflictViewActive)
            {
                SetButtonTextFast(_conflictToggleBtn, "返回完整设置");
                ApplyButtonTextTheme(_conflictToggleBtn, Color.white, useDefaultTheme: true);
            }
            else
            {
                string text = _conflicts.Count > 0 ? $"[!] 仅看冲突 ({_conflicts.Count} 组)" : "[!] 仅看冲突";
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
            _allExpanded = AllVisibleCardsExpanded();
            SetButtonTextFast(_expandToggleBtn, _allExpanded ? "全部收起" : "全部展开");
        }

        private static void ToggleExpandAll()
        {
            if (IsConflictViewActive)
            {
                ToggleConflictMode();
            }

            _allExpanded = !_allExpanded;
            SetButtonTextFast(_expandToggleBtn, _allExpanded ? "全部收起" : "全部展开");

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
                    }

                    foreach (var g in _modGroups)
                    {
                        if (hasQuery && (g.HeaderRowObject == null || !g.HeaderRowObject.activeSelf)) continue;
                        SetGroupExpanded(g, _allExpanded);
                    }
                }
            });
        }

        private static void WithSuspendedRootLayout(Action action)
        {
            var disabledLayouts = new List<Behaviour>(64);
            DisableContainerLayouts(_normalContentRoot, disabledLayouts);
            if (_scrollRect != null)
            {
                if (_scrollRect.content != null && _scrollRect.content.gameObject != _normalContentRoot)
                {
                    DisableContainerLayouts(_scrollRect.content.gameObject, disabledLayouts);
                }
                if (_scrollRect.viewport != null &&
                    _scrollRect.viewport.gameObject != _normalContentRoot &&
                    (_scrollRect.content == null || _scrollRect.viewport.gameObject != _scrollRect.content.gameObject))
                {
                    DisableContainerLayouts(_scrollRect.viewport.gameObject, disabledLayouts);
                }
            }

            // 挂起各卡片标题行 HorizontalLayoutGroup，防止“恢复默认”按钮批量显隐期间逐行触发水平排版
            for (int i = 0; i < _vanillaCards.Count; i++)
            {
                var vc = _vanillaCards[i];
                if (vc.ResetButtonElement != null)
                {
                    DisableContainerLayouts(vc.HeaderRowObject, disabledLayouts);
                }
            }
            for (int i = 0; i < _modGroups.Count; i++)
            {
                var mg = _modGroups[i];
                if (mg.ResetButtonElement != null)
                {
                    DisableContainerLayouts(mg.HeaderRowObject, disabledLayouts);
                }
            }

            try
            {
                action();
            }
            finally
            {
                for (int i = disabledLayouts.Count - 1; i >= 0; i--)
                {
                    if (disabledLayouts[i] != null)
                    {
                        disabledLayouts[i].enabled = true;
                    }
                }

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

        private static void DisableContainerLayouts(GameObject? container, List<Behaviour> disabledLayouts)
        {
            if (container == null) return;
            foreach (var lg in container.GetComponents<LayoutGroup>())
            {
                if (lg != null && lg.enabled)
                {
                    lg.enabled = false;
                    disabledLayouts.Add(lg);
                }
            }
            foreach (var fitter in container.GetComponents<ContentSizeFitter>())
            {
                if (fitter != null && fitter.enabled)
                {
                    fitter.enabled = false;
                    disabledLayouts.Add(fitter);
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
                RefreshRulesMethod?.Invoke(manager, null);

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
                        RefreshRulesMethod?.Invoke(manager, null);
                    }
                }

                if (hadKeyBind)
                {
                    SetKeyBindStatusMethod?.Invoke(manager, new object[] { string.Empty });
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

                // 1. 刷新所有模组卡片按键按钮与冲突标签
                foreach (var g in _modGroups)
                {
                    foreach (var h in g.Hotkeys)
                    {
                        UpdateHotkeyButtonVisual(h);
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
                comp = btn.text.GetComponentInParent<ButtonTextColor>();
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
            int maxSizeTenths = Mathf.RoundToInt(spec.MaxFontSize * 10f);
            var key = (fontId, widthTenths, maxSizeTenths, text!);

            if (!FittedFontSizeCache.TryGetValue(key, out float fitted))
            {
                tmp.fontSize = spec.MaxFontSize;
                Vector2 pref = tmp.GetPreferredValues(text, float.PositiveInfinity, 0f);
                fitted = spec.MaxFontSize;
                if (pref.x > spec.AvailWidth && pref.x > 0.01f)
                {
                    float scaled = Mathf.Floor((spec.MaxFontSize * (spec.AvailWidth / pref.x)) * 10f) / 10f;
                    fitted = Mathf.Max(spec.MinFontSize, Mathf.Min(spec.MaxFontSize, scaled));
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

        private static void StyleButtonTypography(CustomUIElement? btn, float lrMargin, float tbMargin, float fontScale, float buttonWidth = -1f, bool allowAutoShrink = false)
        {
            if (btn == null || btn.text == null) return;
            float baseSize = _baseButtonFontSize > 0f ? _baseButtonFontSize : btn.text.fontSize;
            float targetSize = Mathf.Max(11f, Mathf.Round(baseSize * fontScale * 10f) / 10f);
            btn.text.margin = new Vector4(lrMargin, tbMargin, lrMargin, tbMargin);
            btn.text.enableAutoSizing = false;
            btn.text.enableWordWrapping = false;
            btn.text.overflowMode = TextOverflowModes.Ellipsis;

            if (allowAutoShrink && buttonWidth > 0f)
            {
                var spec = new ButtonFitSpec
                {
                    AvailWidth = Mathf.Max(20f, buttonWidth - lrMargin * 2f - 4f),
                    MaxFontSize = targetSize,
                    MinFontSize = 10.5f
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
                float targetSize = _baseButtonFontSize > 0f ? _baseButtonFontSize : btn.text.fontSize;
                Vector4 baseMargin = _hasBaseButtonMargin ? _baseButtonMargin : btn.text.margin;
                float leftMargin = Mathf.Max(baseMargin.x, 8f);
                float rightMargin = Mathf.Max(baseMargin.z, 8f);
                btn.text.margin = new Vector4(leftMargin, baseMargin.y, rightMargin, baseMargin.w);
                btn.text.enableWordWrapping = false;
                btn.text.overflowMode = TextOverflowModes.Ellipsis;
                btn.text.enableAutoSizing = false;

                var spec = new ButtonFitSpec
                {
                    AvailWidth = Mathf.Max(20f, width - leftMargin - rightMargin - 4f),
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

        private static void StyleActionButton(CustomUIElement? btn, float width, float height, float lrMargin = 12f, float tbMargin = 3f, float fontScale = 0.88f)
        {
            if (btn == null) return;
            btn.SetFixedWidth(width);
            btn.SetPreferredSize(width, height);
            btn.SetFlexible(0f, 0f);
            StyleButtonTypography(btn, lrMargin, tbMargin, fontScale, width, allowAutoShrink: true);
        }

        private static void StyleFoldButton(CustomUIElement? btn, float size)
        {
            if (btn == null) return;
            btn.SetFixedWidth(size);
            btn.SetPreferredSize(size, size);
            btn.SetFlexible(0f, 0f);
            if (btn.text != null)
            {
                float baseSize = _baseButtonFontSize > 0f ? _baseButtonFontSize : btn.text.fontSize;
                btn.text.enableAutoSizing = false;
                btn.text.fontSize = Mathf.Max(10f, Mathf.Round(baseSize * 0.50f));
                btn.text.alignment = TextAlignmentOptions.Center;
                btn.text.margin = Vector4.zero;
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

        private static void ApplySingleButtonColor(CustomUIElement btn, KeyCombo combo)
        {
            if (combo.IsEmpty)
            {
                ApplyButtonTextTheme(btn, ConflictDetector.EmptyKeyColor);
            }
            else if (_conflicts.ContainsKey(combo))
            {
                ApplyButtonTextTheme(btn, ConflictDetector.ConflictColor);
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
                ApplySingleButtonColor(hotkey.ButtonElement, hotkey.EffectiveCombo);
            }

            if (hotkey.ConflictButtonElement != null && _capturingEntry != hotkey)
            {
                SetButtonTextFast(hotkey.ConflictButtonElement, formatted);
                ApplySingleButtonColor(hotkey.ConflictButtonElement, hotkey.EffectiveCombo);
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
                return KeyBindHelper.KeyName(KeyCode.None);
            }
            return ComboKeyStore.FormatCombo(hotkey.EffectiveModifiers, key);
        }

        private static string FormatSliderValue(ModGeneralEntry gen, float val)
        {
            if (gen.ValueFormatter != null) return gen.ValueFormatter(val);
            if (gen.WholeNumbers) return Mathf.RoundToInt(val).ToString();
            return val.ToString("0.0");
        }

        private static void NotifyModConfigApplied(string modId)
        {
            try
            {
                if (ModHook.Instance != null && FireModConfigAppliedMethod != null)
                {
                    FireModConfigAppliedMethod.Invoke(ModHook.Instance, new object[] { modId });
                }
            }
            catch { }
        }

        // =====================================================================
        // 保存与持久化 (OnApplySetting 挂钩)
        // =====================================================================
        private static void ApplyAllSettings(GameOptionUIManager manager)
        {
            CancelCapture();

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
                    OriginalApplySettingMethod?.Invoke(manager, null);
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

            // 5. 重新刷新冲突与颜色状态
            RefreshConflicts();
            if (IsConflictViewActive)
            {
                RebuildConflictView();
            }
        }
    }
}
