#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 顶部双行搜索栏与全局操作控制台（作用域筛选、显示模式筛选、冲突/排查/全部展开切换）。
    /// </summary>
    internal static class TopSearchAndActionBar
    {
        private static string? s_pendingSearchText;
        private static float s_searchDebounceTime;
        private static bool s_hasPendingSearchDebounce;

        /// <summary>
        /// 兼容旧版调用；新版已彻底废除分帧展开队列，单帧瞬开无排队。
        /// </summary>
        internal static bool HasPendingProgressiveExpand => false;

        internal static void ClearSearchDebounce()
        {
            s_hasPendingSearchDebounce = false;
            s_pendingSearchText = null;
        }

        internal static void ClearProgressiveExpandQueue()
        {
        }

        internal static void TickSearchDebounce()
        {
            if (!s_hasPendingSearchDebounce) return;

            // 智能识别输入法合成态（IME Composition）：当正在输入法拼音候选串合成中时，推迟防抖计时器，绝不提前触发重度全量过滤
            if (!string.IsNullOrEmpty(Input.compositionString))
            {
                s_searchDebounceTime = Time.unscaledTime + 0.30f;
                return;
            }

            if (Time.unscaledTime >= s_searchDebounceTime)
            {
                string textToApply = s_pendingSearchText ?? string.Empty;
                s_hasPendingSearchDebounce = false;
                s_pendingSearchText = null;
                ExecuteSearchFilter(textToApply);
            }
        }

        /// <summary>
        /// 兼容旧版调用；新版已彻底废除分帧展开队列，单帧瞬开无排队。
        /// </summary>
        internal static void TickProgressiveExpand()
        {
        }

        /// <summary>
        /// 装配顶部双行搜索与快捷操作栏。
        /// </summary>
        internal static void BuildTopBars(CustomUIBuilder builder, string layer, GameObject? windowRoot, bool hasThemeForLayer)
        {
            ClearSearchIndex();
            ClearSearchDebounce();
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
                (int)UiSessionState.SearchScope,
                OnSearchScopeChanged,
                "unified_search_scope"
            );
            var scopeEl = builder.Last;
            scopeEl.SetFixedWidth(158f);
            scopeEl.SetPreferredSize(158f, 36f);
            I18n.ApplyFontToDropdown(scopeEl, forceCjkCapableList: I18n.IsChinese);

            builder.AddInput(
                I18n.Get(
                    "搜索设置项或按键 (支持繁简中文、英文、拼音如 sp=商品)...",
                    "Search settings, hotkeys or mods (EN / CN / Pinyin)..."
                ),
                UiSessionState.SearchQuery,
                OnSearchChanged,
                "unified_search_input",
                OnSearchTyping
            );
            var searchEl = builder.Last;
            searchEl.SetFlexible(1f, 0f);
            if (searchEl.root != null)
            {
                var searchLe = searchEl.root.GetComponent<LayoutElement>() ?? searchEl.root.AddComponent<LayoutElement>();
                searchLe.layoutPriority = 100;
                searchLe.minWidth = 180f;
                searchLe.preferredWidth = 240f;
                searchLe.flexibleWidth = 1f;
                searchLe.flexibleHeight = 0f;
            }
            if (searchEl.input != null)
            {
                UiSessionState.SearchInputField = searchEl.input;
                I18n.ApplyFontIfRuntime(searchEl.input.textComponent, null);
                I18n.ApplyFontIfRuntime(searchEl.input.placeholder as TextMeshProUGUI, searchEl.input.placeholder is TextMeshProUGUI phTmp ? phTmp.text : null);
            }

            builder.AddSpace(10f);
            builder.End();

            builder.AddSpace(6f);

            // 第二行：选项(显示模式/排查分类) + 仅看冲突 + 模组排查 + 全部展开/收起 + 快捷提示（紧凑单行排版）
            builder.BeginRow(6f, TextAnchor.MiddleCenter);
            builder.AddSpace(10f);

            // 1. 完整设置展示筛选下拉选项 (显示: 全部 / 仅展示按键 / 仅展示常规)
            builder.AddDropdown(
                new List<string>
                {
                    I18n.Get("显示: 全部", "Show: All"),
                    I18n.Get("仅展示按键", "Keys Only"),
                    I18n.Get("仅展示常规", "Settings Only")
                },
                (int)UiSessionState.NormalDisplayMode,
                OnNormalDisplayModeChanged,
                "unified_display_filter"
            );
            UiSessionState.NormalDisplayDropdown = builder.Last;
            UiSessionState.NormalDisplayDropdown.SetFixedWidth(158f);
            UiSessionState.NormalDisplayDropdown.SetPreferredSize(158f, 36f);
            I18n.ApplyFontToDropdown(UiSessionState.NormalDisplayDropdown, forceCjkCapableList: I18n.IsChinese);

            // 1-alt. 模组排查分类筛选下拉选项 (全部 / 探测热键 / 疑难待确认 / 未配官方 / 正常纳管 / 纯资源)
            builder.AddDropdown(
                ModDiagnosticView.BuildDiagnosticDropdownOptions(),
                (int)UiSessionState.DiagnosticFilterMode,
                ModDiagnosticView.OnDiagnosticFilterModeChanged,
                "unified_diagnostic_filter"
            );
            UiSessionState.DiagnosticFilterDropdown = builder.Last;
            UiSessionState.DiagnosticFilterDropdown.SetFixedWidth(158f);
            UiSessionState.DiagnosticFilterDropdown.SetPreferredSize(158f, 36f);
            I18n.ApplyFontToDropdown(UiSessionState.DiagnosticFilterDropdown, forceCjkCapableList: I18n.IsChinese);
            if (UiSessionState.DiagnosticFilterDropdown?.root != null)
            {
                UiSessionState.DiagnosticFilterDropdown.root.SetActive(false);
            }

            // 2. 仅看冲突按钮
            string conflictBtnText = UiSessionState.Conflicts.Count > 0
                ? I18n.Format("[!] 仅看冲突 ({0} 组)", "[!] Conflicts ({0})", UiSessionState.Conflicts.Count)
                : I18n.Get("[!] 仅看冲突", "[!] Conflicts");
            builder.AddButton(conflictBtnText, ConflictAggregationView.ToggleConflictMode, "btn_conflict_toggle");
            UiSessionState.ConflictToggleBtn = builder.Last;

            // 3. 模组排查按钮
            int diagCount = UiSessionState.ScanReport?.AttentionModCount ?? 0;
            string diagBtnText = diagCount > 0
                ? I18n.Format("模组排查 ({0})", "Diagnostics ({0})", diagCount)
                : I18n.Get("模组排查", "Diagnostics");
            builder.AddButton(diagBtnText, ModDiagnosticView.ToggleDiagnosticMode, "btn_diagnostic_toggle");
            UiSessionState.DiagnosticToggleBtn = builder.Last;

            // 4. 全部展开 / 全部收起按钮
            builder.AddButton(UiSessionState.AllExpanded ? I18n.Get("全部收起", "Collapse All") : I18n.Get("全部展开", "Expand All"), ToggleExpandAll, "btn_expand_all");
            UiSessionState.ExpandToggleBtn = builder.Last;

            // 优先抓取原生 ButtonTextColor 默认字色、高亮色、按下色、激活字体与原始字号（每个层级 menu/meta 仅需首开抓取一次）
            if (!hasThemeForLayer)
            {
                UiLayoutAndFontHelper.CaptureDefaultButtonTheme(UiSessionState.ExpandToggleBtn, forceRefresh: true, layer: layer);
            }
            if (I18n.IsChinese && FontLoader.Instance != null && FontLoader.Instance.simplifiedChineseFont != null)
            {
                UiLayoutAndFontHelper.ActiveButtonFont = FontLoader.Instance.simplifiedChineseFont;
            }
            else if (UiSessionState.ExpandToggleBtn?.text?.font != null)
            {
                UiLayoutAndFontHelper.ActiveButtonFont = UiSessionState.ExpandToggleBtn.text.font;
            }

            // 抓取完成后即关闭根节点激活态，确保整页百余个原版与模组控件在离线非激活状态一次性装配完毕
            if (windowRoot != null && windowRoot.activeSelf)
            {
                windowRoot.SetActive(false);
            }

            UiLayoutAndFontHelper.StyleActionButton(UiSessionState.ConflictToggleBtn, 168f, 36f, lrMargin: 8f, tbMargin: 3f, fontScale: 0.85f);
            UiLayoutAndFontHelper.StyleActionButton(UiSessionState.DiagnosticToggleBtn, 140f, 36f, lrMargin: 8f, tbMargin: 3f, fontScale: 0.85f);
            UiLayoutAndFontHelper.StyleActionButton(UiSessionState.ExpandToggleBtn, 112f, 36f, lrMargin: 8f, tbMargin: 3f, fontScale: 0.85f);

            ConflictAggregationView.UpdateConflictToggleButtonAppearance();
            ModDiagnosticView.UpdateDiagnosticToggleButtonAppearance();

            string topHintText = I18n.Get("Ctrl/Shift/Alt+侧键 | Esc=无", "Combos/Side | Esc=None");
            builder.AddLabel(topHintText, "label_top_hint");
            var hintEl = builder.Last;
            if (hintEl != null)
            {
                I18n.ApplyFontIfRuntime(hintEl.text, topHintText);
                if (hintEl.root != null)
                {
                    var hintLe = hintEl.root.GetComponent<LayoutElement>() ?? hintEl.root.AddComponent<LayoutElement>();
                    hintLe.minWidth = 0f;
                    hintLe.preferredWidth = 220f;
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
                    float baseFontSize = UiLayoutAndFontHelper.BaseButtonFontSize > 0f ? UiLayoutAndFontHelper.BaseButtonFontSize : 20f;
                    float maxHintSize = Mathf.Clamp(baseFontSize * 0.78f, 12f, 17f);
                    UiLayoutAndFontHelper.ApplySinglePassFontSize(hintEl.text, hintEl.text.text, new UiLayoutAndFontHelper.ButtonFitSpec
                    {
                        AvailWidth = 220f,
                        AvailHeight = 30f,
                        MaxFontSize = maxHintSize,
                        MinFontSize = 11f
                    });
                }
            }
            builder.AddSpace(10f);
            builder.End();

            builder.AddSeparator(null, 2);
        }

        private static void OnSearchScopeChanged(int index)
        {
            if (index >= 0 && index <= (int)SearchScope.GeneralOnly)
            {
                ClearSearchDebounce();
                ClearProgressiveExpandQueue();
                UiSessionState.SearchScope = (SearchScope)index;
                if (UiSessionState.IsConflictViewActive || UiSessionState.IsDiagnosticViewActive)
                {
                    UiSessionState.IsConflictViewActive = false;
                    UiSessionState.IsDiagnosticViewActive = false;
                    ModDiagnosticView.SyncViewContainersAndToggleButtons();
                }
                ApplySearchFilter();
            }
        }

        private static void OnNormalDisplayModeChanged(int index)
        {
            if (index >= 0 && index <= (int)NormalDisplayMode.GeneralOnly)
            {
                ClearSearchDebounce();
                ClearProgressiveExpandQueue();
                UiSessionState.NormalDisplayMode = (NormalDisplayMode)index;
                if (UiSessionState.IsConflictViewActive || UiSessionState.IsDiagnosticViewActive)
                {
                    UiSessionState.IsConflictViewActive = false;
                    UiSessionState.IsDiagnosticViewActive = false;
                    ModDiagnosticView.SyncViewContainersAndToggleButtons();
                }
                ApplySearchFilter();
                RefreshExpandAllButtonState();
                UiLayoutAndFontHelper.ResetScrollToTop();
            }
        }

        private static void OnSearchTyping(string text)
        {
            // 清空搜索框时 0 延迟立即生效
            if (string.IsNullOrEmpty(text))
            {
                ClearSearchDebounce();
                ExecuteSearchFilter(string.Empty);
                return;
            }

            // 300ms 轻量输入防抖（人类正常敲两个字母间隔约 150~250ms，300ms 保证连续逐字敲击期间 0 界面重排）
            s_pendingSearchText = text;
            s_searchDebounceTime = Time.unscaledTime + 0.30f;
            s_hasPendingSearchDebounce = true;
        }

        private static void OnSearchChanged(string? text)
        {
            string safeText = text ?? string.Empty;

            // 清空搜索框时 0 延迟立即生效
            if (string.IsNullOrEmpty(safeText))
            {
                ClearSearchDebounce();
                ExecuteSearchFilter(string.Empty);
                return;
            }

            // 按回车提交时 0 延迟立即生效
            bool isEnterPressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (isEnterPressed)
            {
                ClearSearchDebounce();
                ExecuteSearchFilter(safeText);
                return;
            }

            // 防失焦竞态：失焦点击下方按钮（如展开/收起、按键绑定等）时，
            // 绝不同步执行整页重新过滤与布局重建，避免吞掉当前帧待分发的 onClick 事件；
            // 保持由 TickSearchDebounce 在到期或后续帧中安全驱动。
            if (string.Equals(safeText, UiSessionState.SearchQuery ?? string.Empty, StringComparison.Ordinal))
            {
                ClearSearchDebounce();
                return;
            }

            // 若失焦时文本发生变更（如外部粘贴但未触发回车），确保防抖队列记录最新文本并在到期时驱动
            s_pendingSearchText = safeText;
            s_searchDebounceTime = Mathf.Max(s_searchDebounceTime, Time.unscaledTime + 0.15f);
            s_hasPendingSearchDebounce = true;
        }

        private static void ExecuteSearchFilter(string text)
        {
            UiSessionState.SearchQuery = text;
            ClearProgressiveExpandQueue();
            if (UiSessionState.IsDiagnosticViewActive)
            {
                UiSessionState.DiagnosticViewDirty = true;
                ModDiagnosticView.RebuildDiagnosticView();
                UiLayoutAndFontHelper.ResetScrollToTop();
                return;
            }
            if (UiSessionState.IsConflictViewActive && !string.IsNullOrWhiteSpace(text))
            {
                ConflictAggregationView.ToggleConflictMode();
            }
            ApplySearchFilter();
        }

        #region 搜索预索引与零分配极速匹配缓存

        private static string StripSeparators(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]) || char.IsPunctuation(text[i]))
                {
                    var sb = new System.Text.StringBuilder(text.Length);
                    sb.Append(text, 0, i);
                    for (int j = i + 1; j < text.Length; j++)
                    {
                        char c = text[j];
                        if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c))
                        {
                            sb.Append(c);
                        }
                    }
                    return sb.ToString();
                }
            }
            return text;
        }

        private static void IndexString(string? text, out string lower, out string initials, out string fullPy, out string simplified)
        {
            if (string.IsNullOrEmpty(text))
            {
                lower = string.Empty;
                initials = string.Empty;
                fullPy = string.Empty;
                simplified = string.Empty;
                return;
            }

            simplified = PinyinEngine.ToSimplified(text!);
            lower = simplified.ToLowerInvariant();
            initials = PinyinEngine.ToInitials(simplified);
            fullPy = PinyinEngine.ToFullPinyin(simplified);
        }

        private sealed class GroupSearchData
        {
            public string DisplayNameLower = string.Empty;
            public string DisplayNameInitials = string.Empty;
            public string DisplayNameFullPy = string.Empty;
            public string DisplayNameSimplified = string.Empty;
            public string ModIdLower = string.Empty;
            public string ModIdInitials = string.Empty;
            public string ModIdFullPy = string.Empty;
            public string ModIdSimplified = string.Empty;

            public bool Matches(string normQuery, string compactQuery)
            {
                if (DisplayNameLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0 ||
                    ModIdLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
                if (compactQuery.Length > 0)
                {
                    if (DisplayNameInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0 ||
                        ModIdInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }

                    if (PinyinEngine.MatchesPolyphoneTokens(DisplayNameSimplified, compactQuery) ||
                        PinyinEngine.MatchesPolyphoneTokens(ModIdSimplified, compactQuery))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class GeneralSearchData
        {
            public string DisplayNameLower = string.Empty;
            public string DisplayNameInitials = string.Empty;
            public string DisplayNameFullPy = string.Empty;
            public string DisplayNameSimplified = string.Empty;
            public string SectionLabelLower = string.Empty;
            public string SectionLabelInitials = string.Empty;
            public string SectionLabelFullPy = string.Empty;
            public string SectionLabelSimplified = string.Empty;
            public string SectionLower = string.Empty;
            public string SectionInitials = string.Empty;
            public string SectionFullPy = string.Empty;
            public string SectionSimplified = string.Empty;

            public bool Matches(string normQuery, string compactQuery)
            {
                if (DisplayNameLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0 ||
                    (!string.IsNullOrEmpty(SectionLabelLower) && SectionLabelLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0) ||
                    (!string.IsNullOrEmpty(SectionLower) && SectionLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0))
                {
                    return true;
                }
                if (compactQuery.Length > 0)
                {
                    if (DisplayNameInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0 ||
                        (!string.IsNullOrEmpty(SectionLabelInitials) && SectionLabelInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0) ||
                        (!string.IsNullOrEmpty(SectionInitials) && SectionInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0))
                    {
                        return true;
                    }

                    if (PinyinEngine.MatchesPolyphoneTokens(DisplayNameSimplified, compactQuery) ||
                        (!string.IsNullOrEmpty(SectionLabelSimplified) && PinyinEngine.MatchesPolyphoneTokens(SectionLabelSimplified, compactQuery)) ||
                        (!string.IsNullOrEmpty(SectionSimplified) && PinyinEngine.MatchesPolyphoneTokens(SectionSimplified, compactQuery)))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class HotkeySearchData
        {
            public string DisplayNameLower = string.Empty;
            public string DisplayNameInitials = string.Empty;
            public string DisplayNameFullPy = string.Empty;
            public string DisplayNameSimplified = string.Empty;
            public string SectionLabelLower = string.Empty;
            public string SectionLabelInitials = string.Empty;
            public string SectionLabelFullPy = string.Empty;
            public string SectionLabelSimplified = string.Empty;
            public string SectionLower = string.Empty;
            public string SectionInitials = string.Empty;
            public string SectionFullPy = string.Empty;
            public string SectionSimplified = string.Empty;
            public string ProbedLower = string.Empty;
            public string ProbedInitials = string.Empty;
            public string ProbedFullPy = string.Empty;
            public string ProbedSimplified = string.Empty;

            public KeyCombo CachedCombo;
            public string CachedComboText = string.Empty;
            public string CachedPrimaryKeyText = string.Empty;

            public bool Matches(ModHotkeyEntry hotkey, string query, string normQuery, string compactQuery)
            {
                if (DisplayNameLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0 ||
                    (!string.IsNullOrEmpty(SectionLabelLower) && SectionLabelLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0) ||
                    (!string.IsNullOrEmpty(SectionLower) && SectionLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0) ||
                    (!string.IsNullOrEmpty(ProbedLower) && ProbedLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0))
                {
                    return true;
                }

                if (compactQuery.Length > 0)
                {
                    if (DisplayNameInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0 ||
                        (!string.IsNullOrEmpty(SectionLabelInitials) && SectionLabelInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0) ||
                        (!string.IsNullOrEmpty(SectionInitials) && SectionInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0) ||
                        (!string.IsNullOrEmpty(ProbedInitials) && ProbedInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0))
                    {
                        return true;
                    }

                    if (PinyinEngine.MatchesPolyphoneTokens(DisplayNameSimplified, compactQuery) ||
                        (!string.IsNullOrEmpty(SectionLabelSimplified) && PinyinEngine.MatchesPolyphoneTokens(SectionLabelSimplified, compactQuery)) ||
                        (!string.IsNullOrEmpty(SectionSimplified) && PinyinEngine.MatchesPolyphoneTokens(SectionSimplified, compactQuery)) ||
                        (!string.IsNullOrEmpty(ProbedSimplified) && PinyinEngine.MatchesPolyphoneTokens(ProbedSimplified, compactQuery)))
                    {
                        return true;
                    }
                }

                KeyCombo effCombo = hotkey.EffectiveCombo;
                if (effCombo != CachedCombo || string.IsNullOrEmpty(CachedComboText))
                {
                    CachedCombo = effCombo;
                    if (effCombo.Key == KeyCode.None)
                    {
                        CachedComboText = ComboKeyStore.FormatCombo(effCombo.Modifiers, effCombo.Key);
                        CachedPrimaryKeyText = string.Empty;
                    }
                    else
                    {
                        CachedComboText = ComboKeyStore.FormatCombo(effCombo.Modifiers, effCombo.Key);
                        CachedPrimaryKeyText = effCombo.Modifiers != KeyModifierMask.None
                            ? ComboKeyStore.FormatPrimaryKeyName(effCombo.Key)
                            : string.Empty;
                    }
                }

                KeyCode effKey = effCombo.Key;
                if (effKey == KeyCode.None)
                {
                    return KeySearchMatcher.MatchesFormattedKeyText(CachedComboText, query);
                }

                if (KeySearchMatcher.MatchesKeyCodeSearchToken(effKey, query))
                {
                    return true;
                }

                if (KeySearchMatcher.MatchesFormattedKeyText(CachedComboText, query))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(CachedPrimaryKeyText) && KeySearchMatcher.MatchesFormattedKeyText(CachedPrimaryKeyText, query))
                {
                    return true;
                }

                return false;
            }
        }

        private sealed class VanillaItemSearchData
        {
            public string DisplayNameLower = string.Empty;
            public string DisplayNameInitials = string.Empty;
            public string DisplayNameFullPy = string.Empty;
            public string DisplayNameSimplified = string.Empty;

            public bool Matches(VanillaOptionItem item, string query, string normQuery, string compactQuery)
            {
                if (item.IsKeyBind && item.LinkedHotkey != null)
                {
                    if (MatchesHotkey(item.LinkedHotkey, query, normQuery, compactQuery))
                    {
                        return true;
                    }
                }

                if (DisplayNameLower.IndexOf(normQuery, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }

                if (compactQuery.Length > 0)
                {
                    if (DisplayNameInitials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }

                    if (PinyinEngine.MatchesPolyphoneTokens(DisplayNameSimplified, compactQuery))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private static readonly Dictionary<ModOptionGroup, GroupSearchData> s_groupSearchIndex = new Dictionary<ModOptionGroup, GroupSearchData>();
        private static readonly Dictionary<ModGeneralEntry, GeneralSearchData> s_generalSearchIndex = new Dictionary<ModGeneralEntry, GeneralSearchData>();
        private static readonly Dictionary<ModHotkeyEntry, HotkeySearchData> s_hotkeySearchIndex = new Dictionary<ModHotkeyEntry, HotkeySearchData>();
        private static readonly Dictionary<VanillaOptionItem, VanillaItemSearchData> s_vanillaItemSearchIndex = new Dictionary<VanillaOptionItem, VanillaItemSearchData>();
        private static readonly Dictionary<VanillaSectionCard, (string lower, string initials, string fullPy, string simplified)> s_vanillaCardSearchIndex = new Dictionary<VanillaSectionCard, (string, string, string, string)>();

        private static GroupSearchData GetOrCreateGroupIndex(ModOptionGroup group)
        {
            if (s_groupSearchIndex.TryGetValue(group, out var data)) return data;
            data = new GroupSearchData();
            IndexString(group.DisplayName, out data.DisplayNameLower, out data.DisplayNameInitials, out data.DisplayNameFullPy, out data.DisplayNameSimplified);
            IndexString(group.ModId, out data.ModIdLower, out data.ModIdInitials, out data.ModIdFullPy, out data.ModIdSimplified);
            s_groupSearchIndex[group] = data;
            return data;
        }

        private static GeneralSearchData GetOrCreateGeneralIndex(ModGeneralEntry gen)
        {
            if (s_generalSearchIndex.TryGetValue(gen, out var data)) return data;
            data = new GeneralSearchData();
            IndexString(gen.DisplayName, out data.DisplayNameLower, out data.DisplayNameInitials, out data.DisplayNameFullPy, out data.DisplayNameSimplified);
            IndexString(gen.SectionLabel, out data.SectionLabelLower, out data.SectionLabelInitials, out data.SectionLabelFullPy, out data.SectionLabelSimplified);
            IndexString(gen.Section, out data.SectionLower, out data.SectionInitials, out data.SectionFullPy, out data.SectionSimplified);
            s_generalSearchIndex[gen] = data;
            return data;
        }

        private static HotkeySearchData GetOrCreateHotkeyIndex(ModHotkeyEntry hotkey)
        {
            if (s_hotkeySearchIndex.TryGetValue(hotkey, out var data)) return data;
            data = new HotkeySearchData();
            IndexString(hotkey.DisplayName, out data.DisplayNameLower, out data.DisplayNameInitials, out data.DisplayNameFullPy, out data.DisplayNameSimplified);
            IndexString(hotkey.SectionLabel, out data.SectionLabelLower, out data.SectionLabelInitials, out data.SectionLabelFullPy, out data.SectionLabelSimplified);
            IndexString(hotkey.Section, out data.SectionLower, out data.SectionInitials, out data.SectionFullPy, out data.SectionSimplified);
            if (hotkey.IsReadOnlyProbed)
            {
                IndexString(hotkey.ProbedSourceDetail, out data.ProbedLower, out data.ProbedInitials, out data.ProbedFullPy, out data.ProbedSimplified);
            }
            s_hotkeySearchIndex[hotkey] = data;
            return data;
        }

        private static VanillaItemSearchData GetOrCreateVanillaItemIndex(VanillaOptionItem item)
        {
            if (s_vanillaItemSearchIndex.TryGetValue(item, out var data)) return data;
            data = new VanillaItemSearchData();
            IndexString(item.DisplayName, out data.DisplayNameLower, out data.DisplayNameInitials, out data.DisplayNameFullPy, out data.DisplayNameSimplified);
            s_vanillaItemSearchIndex[item] = data;
            return data;
        }

        private static (string lower, string initials, string fullPy, string simplified) GetOrCreateVanillaCardIndex(VanillaSectionCard card)
        {
            if (s_vanillaCardSearchIndex.TryGetValue(card, out var data)) return data;
            IndexString(card.DisplayName + " " + card.SectionId, out data.lower, out data.initials, out data.fullPy, out data.simplified);
            s_vanillaCardSearchIndex[card] = data;
            return data;
        }

        internal static void ClearSearchIndex()
        {
            s_groupSearchIndex.Clear();
            s_generalSearchIndex.Clear();
            s_hotkeySearchIndex.Clear();
            s_vanillaItemSearchIndex.Clear();
            s_vanillaCardSearchIndex.Clear();
            s_lastMatchedCards.Clear();
            s_lastSearchQuery = string.Empty;
        }

        internal static bool MatchesHotkey(ModHotkeyEntry hotkey, string query, string normQuery, string compactQuery)
        {
            var idx = GetOrCreateHotkeyIndex(hotkey);
            return idx.Matches(hotkey, query, normQuery, compactQuery);
        }

        internal static bool MatchesGeneral(ModGeneralEntry gen, string normQuery, string compactQuery)
        {
            var idx = GetOrCreateGeneralIndex(gen);
            return idx.Matches(normQuery, compactQuery);
        }

        private static bool IsVanillaSectionMatch(VanillaSectionCard card, string normQuery, string compactQuery)
        {
            var data = GetOrCreateVanillaCardIndex(card);
            if (data.lower.IndexOf(normQuery, StringComparison.Ordinal) >= 0) return true;
            if (compactQuery.Length > 0)
            {
                if (data.initials.IndexOf(compactQuery, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }

                if (PinyinEngine.MatchesPolyphoneTokens(data.simplified, compactQuery))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        /// <summary>
        /// 检查热键是否匹配当前查询关键词（复用纯内核 KeySearchMatcher 算法与预索引加速）。
        /// </summary>
        internal static bool IsHotkeyMatchQuery(ModHotkeyEntry hotkey, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string normQuery = PinyinEngine.ToSimplified(query.Trim()).ToLowerInvariant();
            string compactQuery = StripSeparators(normQuery);
            return MatchesHotkey(hotkey, query, normQuery, compactQuery);
        }

        /// <summary>
        /// 检查常规配置项是否匹配当前查询关键词。
        /// </summary>
        internal static bool IsGeneralMatchQuery(ModGeneralEntry gen, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string normQuery = PinyinEngine.ToSimplified(query.Trim()).ToLowerInvariant();
            string compactQuery = StripSeparators(normQuery);
            return MatchesGeneral(gen, normQuery, compactQuery);
        }

        /// <summary>
        /// 检查原版官方配置项是否匹配当前查询关键词。
        /// </summary>
        internal static bool IsVanillaItemMatchQuery(VanillaOptionItem item, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string normQuery = PinyinEngine.ToSimplified(query.Trim()).ToLowerInvariant();
            string compactQuery = StripSeparators(normQuery);
            return GetOrCreateVanillaItemIndex(item).Matches(item, query, normQuery, compactQuery);
        }

        private static bool IsModGroupNameMatch(ModOptionGroup group, string query, bool allowGroupName)
        {
            if (!allowGroupName) return false;
            string normQuery = PinyinEngine.ToSimplified(query.Trim()).ToLowerInvariant();
            string compactQuery = StripSeparators(normQuery);
            return GetOrCreateGroupIndex(group).Matches(normQuery, compactQuery);
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

        private static void SetCardHiddenForSearch(ModOptionGroup group)
        {
            if (UiSessionState.TryGetCardParts(group, out var parts))
            {
                SetObjectActive(parts.HeaderRowObject, false);
                SetObjectActive(parts.ContentContainerObject, false);
                SetObjectActive(parts.OuterSeparatorObject, false);
            }
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

        private static void ShowCardExpandedVisualOnly(ModOptionGroup group)
        {
            if (UiSessionState.TryGetCardParts(group, out var parts))
            {
                SetObjectActive(parts.ContentContainerObject, true);
                if (parts.FoldArrowElement != null && (parts.FoldArrowElement.text == null || parts.FoldArrowElement.text.text != "▼"))
                {
                    parts.FoldArrowElement.SetText("▼");
                }
                if (parts.ResetButtonElement != null)
                {
                    bool showReset = group.ResetToDefaultsAction != null;
                    if (parts.ResetButtonElement.root == null || parts.ResetButtonElement.root.activeSelf != showReset)
                    {
                        parts.ResetButtonElement.SetVisible(showReset);
                    }
                }
            }
        }

        private static void GetActiveDisplayPermissions(
            bool hasQuery,
            out bool allowGroupName,
            out bool allowHotkeys,
            out bool allowGeneral,
            out bool modeAllowsHotkeys,
            out bool modeAllowsGeneral)
        {
            modeAllowsHotkeys = UiSessionState.NormalDisplayMode != NormalDisplayMode.GeneralOnly;
            modeAllowsGeneral = UiSessionState.NormalDisplayMode != NormalDisplayMode.HotkeysOnly;
            allowGroupName = UiSessionState.SearchScope == SearchScope.All;
            bool scopeHotkeys = !hasQuery || UiSessionState.SearchScope == SearchScope.All || UiSessionState.SearchScope == SearchScope.HotkeysAndGeneral || UiSessionState.SearchScope == SearchScope.HotkeysOnly;
            bool scopeGeneral = !hasQuery || UiSessionState.SearchScope == SearchScope.All || UiSessionState.SearchScope == SearchScope.HotkeysAndGeneral || UiSessionState.SearchScope == SearchScope.GeneralOnly;
            allowHotkeys = modeAllowsHotkeys && scopeHotkeys;
            allowGeneral = modeAllowsGeneral && scopeGeneral;
        }

        private static bool GroupHasVisibleItemsInCurrentMode(ModOptionGroup group, bool modeAllowsHotkeys, bool modeAllowsGeneral)
        {
            if (string.Equals(group.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase))
            {
                return modeAllowsHotkeys || modeAllowsGeneral;
            }
            return (modeAllowsHotkeys && (group.Hotkeys.Count > 0 || group.ReadOnlyProbedHotkeys.Count > 0)) ||
                   (modeAllowsGeneral && group.GeneralSettings.Count > 0);
        }

        internal static bool IsApplyingSearchFilter { get; private set; }

        private static string s_lastSearchQuery = string.Empty;
        private static SearchScope s_lastSearchScope = (SearchScope)(-1);
        private static NormalDisplayMode s_lastDisplayMode = (NormalDisplayMode)(-1);
        private static readonly HashSet<object> s_lastMatchedCards = new HashSet<object>();

        /// <summary>
        /// 全局执行搜索与模式过滤，联动更新各卡片与行显隐（零分配预索引 + 前缀收窄极速匹配）。
        /// </summary>
        internal static void ApplySearchFilter()
        {
            if (IsApplyingSearchFilter) return;
            IsApplyingSearchFilter = true;

            try
            {
                string query = (UiSessionState.SearchQuery ?? string.Empty).Trim();
                bool hasQuery = query.Length > 0;
                bool hasDisplayFilter = UiSessionState.NormalDisplayMode != NormalDisplayMode.All;
                GetActiveDisplayPermissions(
                    hasQuery,
                    out bool allowGroupName,
                    out bool allowHotkeys,
                    out bool allowGeneral,
                    out bool modeAllowsHotkeys,
                    out bool modeAllowsGeneral);

                string normQuery = hasQuery ? PinyinEngine.ToSimplified(query).ToLowerInvariant() : string.Empty;
                string compactQuery = hasQuery ? StripSeparators(normQuery) : string.Empty;

                bool isNarrowing = hasQuery &&
                    s_lastSearchQuery.Length > 0 &&
                    query.Length > s_lastSearchQuery.Length &&
                    query.StartsWith(s_lastSearchQuery, StringComparison.OrdinalIgnoreCase) &&
                    s_lastSearchScope == UiSessionState.SearchScope &&
                    s_lastDisplayMode == UiSessionState.NormalDisplayMode;

                var nextMatchedCards = new HashSet<object>();
                GameObject? firstMatchedHeader = null;
                int visibleVanillaCount = 0;
                int visibleModCount = 0;

                UiLayoutAndFontHelper.WithSuspendedRootLayout(() =>
                {
                    using (ComboKeyStore.EnterBypass())
                    {
                        // 1. 过滤原版官方设置区块（支持显示模式 + 四作用域搜索 + 繁简/拼音/按键名匹配）
                        for (int cIdx = 0; cIdx < UiSessionState.VanillaCards.Count; cIdx++)
                        {
                            var card = UiSessionState.VanillaCards[cIdx];

                            if (!hasQuery && !hasDisplayFilter)
                            {
                                for (int i = 0; i < card.Entries.Count; i++)
                                {
                                    var item = card.Entries[i];
                                    for (int r = 0; r < item.RowObjects.Count; r++) SetObjectActive(item.RowObjects[r], true);
                                }
                                SetObjectActive(card.HeaderRowObject, true);
                                SetObjectActive(card.OuterSeparatorObject, true);
                                bool savedExpanded = UiSessionState.VanillaExpandedStates.TryGetValue(card.SectionId, out bool saved) ? saved : false;
                                card.SetExpanded(savedExpanded);
                                visibleVanillaCount++;
                                continue;
                            }

                            if (!hasQuery && hasDisplayFilter)
                            {
                                int visibleRows = 0;
                                for (int i = 0; i < card.Entries.Count; i++)
                                {
                                    var item = card.Entries[i];
                                    bool showRow = item.IsKeyBind ? modeAllowsHotkeys : modeAllowsGeneral;
                                    for (int r = 0; r < item.RowObjects.Count; r++) SetObjectActive(item.RowObjects[r], showRow);
                                    if (showRow) visibleRows++;
                                }
                                if (visibleRows == 0)
                                {
                                    SetCardHiddenForSearch(card);
                                    continue;
                                }
                                SetObjectActive(card.HeaderRowObject, true);
                                SetObjectActive(card.OuterSeparatorObject, true);
                                bool savedExpanded = UiSessionState.VanillaExpandedStates.TryGetValue(card.SectionId, out bool saved) ? saved : false;
                                card.SetExpanded(savedExpanded);
                                visibleVanillaCount++;
                                continue;
                            }

                            // hasQuery == true: 前缀收窄优化（上轮就不匹配且作用域未变时，该卡片必不匹配，保持隐藏）
                            if (isNarrowing && !s_lastMatchedCards.Contains(card))
                            {
                                continue;
                            }

                            bool sectionNameMatch = allowGroupName && IsVanillaSectionMatch(card, normQuery, compactQuery);

                            bool anyItemMatch = false;
                            for (int i = 0; i < card.Entries.Count; i++)
                            {
                                var item = card.Entries[i];
                                bool modeAllowed = item.IsKeyBind ? modeAllowsHotkeys : modeAllowsGeneral;
                                bool scopeAllowed = item.IsKeyBind ? allowHotkeys : allowGeneral;
                                bool itemMatch = modeAllowed && (sectionNameMatch || (scopeAllowed && GetOrCreateVanillaItemIndex(item).Matches(item, query, normQuery, compactQuery)));
                                for (int r = 0; r < item.RowObjects.Count; r++) SetObjectActive(item.RowObjects[r], itemMatch);
                                if (itemMatch) anyItemMatch = true;
                            }

                            if (!anyItemMatch)
                            {
                                SetCardHiddenForSearch(card);
                                continue;
                            }

                            SetObjectActive(card.HeaderRowObject, true);
                            SetObjectActive(card.OuterSeparatorObject, true);
                            ShowCardExpandedVisualOnly(card);
                            visibleVanillaCount++;
                            nextMatchedCards.Add(card);

                            if (firstMatchedHeader == null && card.HeaderRowObject != null)
                            {
                                firstMatchedHeader = card.HeaderRowObject;
                            }
                        }

                        // 2. 过滤创意工坊模组设置卡片（挂起根布局下同步完成，绝不产生多帧依次展开过渡动画）
                        for (int gIdx = 0; gIdx < UiSessionState.ModGroups.Count; gIdx++)
                        {
                            var group = UiSessionState.ModGroups[gIdx];
                            UiSessionState.TryGetCardParts(group, out var parts);

                            if (!hasQuery && !hasDisplayFilter)
                            {
                                if (parts != null && parts.IsContentBuilt)
                                {
                                    ApplyGroupRowVisibility(group, hasQuery: false, groupNameMatch: true, allowHotkeys: true, allowGeneral: true, query: query, normQuery: normQuery, compactQuery: compactQuery);
                                }

                                if (parts != null)
                                {
                                    SetObjectActive(parts.HeaderRowObject, true);
                                    SetObjectActive(parts.OuterSeparatorObject, true);
                                }
                                CollapsibleCardView.SetGroupExpanded(group, group.IsExpanded);
                                visibleModCount++;
                                continue;
                            }

                            bool hasKindInMode = GroupHasVisibleItemsInCurrentMode(group, modeAllowsHotkeys, modeAllowsGeneral);
                            if (!hasQuery && hasDisplayFilter)
                            {
                                if (!hasKindInMode)
                                {
                                    SetCardHiddenForSearch(group);
                                    continue;
                                }
                                if (parts != null)
                                {
                                    SetObjectActive(parts.HeaderRowObject, true);
                                    SetObjectActive(parts.OuterSeparatorObject, true);
                                }
                                if (parts != null && parts.IsContentBuilt)
                                {
                                    ApplyGroupRowVisibility(group, hasQuery: false, groupNameMatch: true, allowHotkeys: modeAllowsHotkeys, allowGeneral: modeAllowsGeneral, query: query, normQuery: normQuery, compactQuery: compactQuery);
                                }
                                CollapsibleCardView.SetGroupExpanded(group, group.IsExpanded);
                                visibleModCount++;
                                continue;
                            }

                            // hasQuery == true: 前缀收窄优化（上轮就不匹配且作用域未变时，该模组卡片必不匹配，保持隐藏）
                            if (isNarrowing && !s_lastMatchedCards.Contains(group))
                            {
                                continue;
                            }

                            bool groupNameMatch = hasKindInMode && allowGroupName && GetOrCreateGroupIndex(group).Matches(normQuery, compactQuery);
                            bool anyHotkeyMatch = false;
                            bool anyGeneralMatch = false;

                            if (!groupNameMatch)
                            {
                                if (allowHotkeys)
                                {
                                    for (int i = 0; i < group.Hotkeys.Count; i++)
                                    {
                                        if (MatchesHotkey(group.Hotkeys[i], query, normQuery, compactQuery))
                                        {
                                            anyHotkeyMatch = true;
                                            break;
                                        }
                                    }
                                    if (!anyHotkeyMatch)
                                    {
                                        for (int i = 0; i < group.ReadOnlyProbedHotkeys.Count; i++)
                                        {
                                            if (MatchesHotkey(group.ReadOnlyProbedHotkeys[i], query, normQuery, compactQuery))
                                            {
                                                anyHotkeyMatch = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                                if (allowGeneral)
                                {
                                    for (int i = 0; i < group.GeneralSettings.Count; i++)
                                    {
                                        if (MatchesGeneral(group.GeneralSettings[i], normQuery, compactQuery))
                                        {
                                            anyGeneralMatch = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            bool anyProfileMatch = string.Equals(group.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase) &&
                                (allowGeneral || allowHotkeys) &&
                                ProfilePresetCardView.IsProfileSectionMatchQuery(query);

                            if (!groupNameMatch && !anyHotkeyMatch && !anyGeneralMatch && !anyProfileMatch)
                            {
                                SetCardHiddenForSearch(group);
                                continue;
                            }

                            visibleModCount++;
                            nextMatchedCards.Add(group);

                            if (parts != null)
                            {
                                SetObjectActive(parts.HeaderRowObject, true);
                                SetObjectActive(parts.OuterSeparatorObject, true);
                                if (firstMatchedHeader == null && parts.HeaderRowObject != null)
                                {
                                    firstMatchedHeader = parts.HeaderRowObject;
                                }

                                if (!parts.IsContentBuilt)
                                {
                                    CollapsibleCardView.EnsureGroupContentBuilt(group);
                                }
                            }
                            ApplyMatchedModGroupRows(group, groupNameMatch, allowHotkeys, allowGeneral, query, normQuery, compactQuery);
                        }
                    }

                    // 3. 联动显隐区域标题栏与空结果提示
                    bool showVanillaBanner = (!hasQuery && !hasDisplayFilter) ? UiSessionState.VanillaCards.Count > 0 : visibleVanillaCount > 0;
                    bool showModBanner = (!hasQuery && !hasDisplayFilter) ? UiSessionState.ModGroups.Count > 0 : visibleModCount > 0;
                    SetObjectActive(UiSessionState.VanillaHeaderRow, showVanillaBanner);
                    SetObjectActive(UiSessionState.VanillaHeaderSep, showVanillaBanner);
                    SetObjectActive(UiSessionState.ModHeaderSpace, showVanillaBanner && showModBanner);
                    SetObjectActive(UiSessionState.ModHeaderSep, showModBanner);
                    SetObjectActive(UiSessionState.ModHeaderRow, showModBanner);
                    SetObjectActive(UiSessionState.ModHeaderBottomSep, showModBanner);
                    SetObjectActive(UiSessionState.EmptySearchLabelRow, (hasQuery || hasDisplayFilter) && visibleVanillaCount == 0 && visibleModCount == 0);

                    if (!hasQuery)
                    {
                        RefreshExpandAllButtonState();
                    }
                });

                if (hasQuery)
                {
                    s_lastMatchedCards.Clear();
                    s_lastMatchedCards.UnionWith(nextMatchedCards);
                    s_lastSearchQuery = query;
                    s_lastSearchScope = UiSessionState.SearchScope;
                    s_lastDisplayMode = UiSessionState.NormalDisplayMode;
                }
                else
                {
                    s_lastMatchedCards.Clear();
                    s_lastSearchQuery = string.Empty;
                    s_lastSearchScope = UiSessionState.SearchScope;
                    s_lastDisplayMode = UiSessionState.NormalDisplayMode;
                }

                if (hasQuery && firstMatchedHeader != null)
                {
                    UiSessionState.WasSearchNonEmpty = true;
                    UiLayoutAndFontHelper.ScrollToTargetImmediate(firstMatchedHeader);
                    UiLayoutAndFontHelper.ClearPendingScroll();
                }
                else if (!hasQuery && UiSessionState.WasSearchNonEmpty)
                {
                    UiSessionState.WasSearchNonEmpty = false;
                    UiLayoutAndFontHelper.ClearPendingScroll();
                    UiLayoutAndFontHelper.ResetScrollToTop();
                }
            }
            finally
            {
                IsApplyingSearchFilter = false;
            }
        }

        internal static void ApplySingleGroupVisibility(ModOptionGroup group)
        {
            string query = (UiSessionState.SearchQuery ?? string.Empty).Trim();
            bool hasQuery = query.Length > 0;
            GetActiveDisplayPermissions(
                hasQuery,
                out bool allowGroupName,
                out bool allowHotkeys,
                out bool allowGeneral,
                out bool modeAllowsHotkeys,
                out bool modeAllowsGeneral);

            string normQuery = hasQuery ? PinyinEngine.ToSimplified(query).ToLowerInvariant() : string.Empty;
            string compactQuery = hasQuery ? StripSeparators(normQuery) : string.Empty;

            bool groupNameMatch = !hasQuery || (GroupHasVisibleItemsInCurrentMode(group, modeAllowsHotkeys, modeAllowsGeneral) && allowGroupName && GetOrCreateGroupIndex(group).Matches(normQuery, compactQuery));
            ApplyGroupRowVisibility(group, hasQuery, groupNameMatch, allowHotkeys, allowGeneral, query, normQuery, compactQuery);
        }

        private static void ApplyMatchedModGroupRows(
            ModOptionGroup group,
            bool groupNameMatch,
            bool allowHotkeys,
            bool allowGeneral,
            string query,
            string normQuery,
            string compactQuery)
        {
            ApplyGroupRowVisibility(group, hasQuery: true, groupNameMatch, allowHotkeys, allowGeneral, query, normQuery, compactQuery);
            ShowCardExpandedVisualOnly(group);
        }

        internal static void ApplyGroupRowVisibility(
            ModOptionGroup group,
            bool hasQuery,
            bool groupNameMatch,
            bool allowHotkeys,
            bool allowGeneral,
            string? query = null,
            string? normQuery = null,
            string? compactQuery = null)
        {
            string actualQuery = query ?? (UiSessionState.SearchQuery ?? string.Empty).Trim();
            string nq = normQuery ?? (hasQuery ? PinyinEngine.ToSimplified(actualQuery).ToLowerInvariant() : string.Empty);
            string cq = compactQuery ?? (hasQuery ? StripSeparators(nq) : string.Empty);

            int visibleHotkeys = 0;
            for (int i = 0; i < group.Hotkeys.Count; i++)
            {
                var hotkey = group.Hotkeys[i];
                bool match = allowHotkeys && (!hasQuery || groupNameMatch || MatchesHotkey(hotkey, actualQuery, nq, cq));
                if (UiSessionState.TryGetHotkeyUi(hotkey, out var hkUi))
                {
                    SetObjectActive(hkUi.RowObject, match);
                }
                if (match) visibleHotkeys++;
            }

            int visibleGeneral = 0;
            for (int i = 0; i < group.GeneralSettings.Count; i++)
            {
                var gen = group.GeneralSettings[i];
                bool match = allowGeneral && (!hasQuery || groupNameMatch || MatchesGeneral(gen, nq, cq));
                if (UiSessionState.TryGetGeneralUi(gen, out var genUi))
                {
                    SetObjectActive(genUi.RowObject, match);
                }
                if (match) visibleGeneral++;
            }

            int visibleProbed = 0;
            for (int i = 0; i < group.ReadOnlyProbedHotkeys.Count; i++)
            {
                var probed = group.ReadOnlyProbedHotkeys[i];
                bool match = allowHotkeys && (!hasQuery || groupNameMatch || MatchesHotkey(probed, actualQuery, nq, cq));
                if (UiSessionState.TryGetHotkeyUi(probed, out var probedUi))
                {
                    SetObjectActive(probedUi.RowObject, match);
                }
                if (match) visibleProbed++;
            }

            if (UiSessionState.TryGetCardParts(group, out var matchedParts))
            {
                SetObjectActive(matchedParts.HkHeader, visibleHotkeys > 0);
                bool hasVisibleContentAbove = visibleHotkeys > 0;
                for (int s = 0; s < matchedParts.GeneralSections.Count; s++)
                {
                    var secBlock = matchedParts.GeneralSections[s];
                    int secVisible = 0;
                    for (int e = 0; e < secBlock.Entries.Count; e++)
                    {
                        var entry = secBlock.Entries[e];
                        if (UiSessionState.TryGetGeneralUi(entry, out var entryUi) && entryUi.RowObject != null && entryUi.RowObject.activeSelf)
                        {
                            secVisible++;
                        }
                    }
                    bool showSec = secVisible > 0;
                    SetObjectActive(secBlock.HeaderObj, showSec);
                    SetObjectActive(secBlock.SeparatorObj, showSec && hasVisibleContentAbove);
                    if (showSec)
                    {
                        hasVisibleContentAbove = true;
                    }
                }
                SetObjectActive(matchedParts.ProbedHeader, visibleProbed > 0);
                SetObjectActive(matchedParts.ProbedSep, hasVisibleContentAbove && visibleProbed > 0);
            }

            if (string.Equals(group.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase) && UiSessionState.SelfProfileSectionContainer != null)
            {
                bool showProfileSection = (allowGeneral || allowHotkeys) && (!hasQuery || groupNameMatch || ProfilePresetCardView.IsProfileSectionMatchQuery(actualQuery));
                SetObjectActive(UiSessionState.SelfProfileSectionContainer, showProfileSection);
            }
        }

        private static bool AllVisibleCardsExpanded()
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(UiSessionState.SearchQuery);
            bool isFiltered = hasQuery || UiSessionState.NormalDisplayMode != NormalDisplayMode.All;
            int counted = 0;

            foreach (var c in UiSessionState.VanillaCards)
            {
                if (isFiltered && (c.HeaderRowObject == null || !c.HeaderRowObject.activeSelf)) continue;
                counted++;
                if (hasQuery ? !CollapsibleCardView.IsCardCurrentlyExpanded(c) : !c.IsExpanded) return false;
            }
            foreach (var g in UiSessionState.ModGroups)
            {
                if (UiSessionState.TryGetCardParts(g, out var parts))
                {
                    if (isFiltered && (parts.HeaderRowObject == null || !parts.HeaderRowObject.activeSelf)) continue;
                    counted++;
                    bool isExpanded = parts.ContentContainerObject != null ? parts.ContentContainerObject.activeSelf : g.IsExpanded;
                    if (hasQuery ? !isExpanded : !g.IsExpanded) return false;
                }
                else
                {
                    if (isFiltered) continue;
                    counted++;
                    if (!g.IsExpanded) return false;
                }
            }

            return counted > 0;
        }

        /// <summary>
        /// 刷新“全部展开 / 全部收起”按钮的文本与状态。
        /// </summary>
        internal static void RefreshExpandAllButtonState()
        {
            UiSessionState.AllExpanded = UiSessionState.IsDiagnosticViewActive
                ? ModDiagnosticView.AllVisibleDiagnosticSectionsExpanded()
                : AllVisibleCardsExpanded();
            UiLayoutAndFontHelper.SetButtonTextFast(UiSessionState.ExpandToggleBtn, UiSessionState.AllExpanded ? I18n.Get("全部收起", "Collapse All") : I18n.Get("全部展开", "Expand All"));
        }

        /// <summary>
        /// 切换全部展开 / 全部收起（脱机批量极速切换法，彻底消除分帧与滚动条跳动，实现单帧无动画瞬开/瞬收）。
        /// </summary>
        internal static void ToggleExpandAll()
        {
            if (UiSessionState.IsDiagnosticViewActive)
            {
                bool nextExpanded = !ModDiagnosticView.AllVisibleDiagnosticSectionsExpanded();
                bool hasDiagnosticQuery = !string.IsNullOrWhiteSpace(UiSessionState.SearchQuery);
                bool singleSectionFilter = UiSessionState.DiagnosticFilterMode != DiagnosticFilterMode.All;

                for (int i = 0; i < UiSessionState.DiagnosticSectionViews.Count; i++)
                {
                    var sec = UiSessionState.DiagnosticSectionViews[i];
                    if (!hasDiagnosticQuery && !singleSectionFilter)
                    {
                        UiSessionState.DiagnosticSectionExpanded[sec.Status] = nextExpanded;
                    }
                    sec.BodyObj?.SetActive(nextExpanded);
                    sec.FoldBtn?.SetText(nextExpanded ? "▼" : "▶");
                }

                RefreshExpandAllButtonState();
                if (UiSessionState.DiagnosticContentRoot != null)
                {
                    var diagRt = UiSessionState.DiagnosticContentRoot.GetComponent<RectTransform>();
                    if (diagRt != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(diagRt);
                    }
                }
                if (UiSessionState.ScrollRect != null && UiSessionState.ScrollRect.content != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(UiSessionState.ScrollRect.content);
                }
                return;
            }

            // Bug #9：在「仅看冲突」页点击「全部展开」直接返回，绝不误跳回完整设置
            if (UiSessionState.IsConflictViewActive)
            {
                return;
            }

            UiSessionState.AllExpanded = !UiSessionState.AllExpanded;
            bool targetExpanded = UiSessionState.AllExpanded;
            UiLayoutAndFontHelper.SetButtonTextFast(UiSessionState.ExpandToggleBtn, targetExpanded ? I18n.Get("全部收起", "Collapse All") : I18n.Get("全部展开", "Expand All"));

            bool isFiltered = !string.IsNullOrWhiteSpace(UiSessionState.SearchQuery) || UiSessionState.NormalDisplayMode != NormalDisplayMode.All;

            // 脱机批量切换法（Offline Batch Switch）：
            // 1. 先将外层根容器 NormalContentRoot 临时设为 false（整棵子树瞬间脱离 Canvas 激活层级，消除全部子对象 SetActive 触发的 Canvas 注册与脏标记！）
            GameObject? rootToDeactivate = UiSessionState.NormalContentRoot ?? UiSessionState.ModCardsContainer;
            bool rootWasActive = rootToDeactivate != null && rootToDeactivate.activeSelf;
            if (rootWasActive)
            {
                rootToDeactivate!.SetActive(false);
            }

            try
            {
                using (ComboKeyStore.EnterBypass())
                {
                    // 2. 在脱机状态下批量处理原版卡片
                    for (int i = 0; i < UiSessionState.VanillaCards.Count; i++)
                    {
                        var c = UiSessionState.VanillaCards[i];
                        if (isFiltered && (c.HeaderRowObject == null || !c.HeaderRowObject.activeSelf)) continue;

                        c.IsExpanded = targetExpanded;
                        UiSessionState.VanillaExpandedStates[c.SectionId] = targetExpanded;
                        if (c.ContentContainerObject != null && c.ContentContainerObject.activeSelf != targetExpanded)
                        {
                            c.ContentContainerObject.SetActive(targetExpanded);
                        }
                        if (c.FoldArrowElement != null)
                        {
                            c.FoldArrowElement.SetText(targetExpanded ? "▼" : "▶");
                        }
                        if (c.ResetButtonElement != null)
                        {
                            bool showReset = targetExpanded && c.ResetToDefaultsAction != null;
                            if (c.ResetButtonElement.root != null && c.ResetButtonElement.root.activeSelf != showReset)
                            {
                                c.ResetButtonElement.SetVisible(showReset);
                            }
                        }
                    }

                    // 3. 在脱机状态下，若为展开，先一次性建齐任何未建卡片（此时子树脱机，零 Canvas 注册开销！）
                    if (targetExpanded)
                    {
                        for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
                        {
                            var g = UiSessionState.ModGroups[i];
                            if (isFiltered && UiSessionState.TryGetCardParts(g, out var parts) && (parts.HeaderRowObject == null || !parts.HeaderRowObject.activeSelf))
                            {
                                continue;
                            }
                            CollapsibleCardView.EnsureGroupContentBuilt(g);
                        }
                    }

                    // 4. 在脱机状态下批量切换所有模组卡片的折叠状态、箭头文本与重置按钮
                    for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
                    {
                        var g = UiSessionState.ModGroups[i];
                        if (isFiltered && UiSessionState.TryGetCardParts(g, out var parts) && (parts.HeaderRowObject == null || !parts.HeaderRowObject.activeSelf))
                        {
                            continue;
                        }

                        g.IsExpanded = targetExpanded;
                        if (UiSessionState.TryGetCardParts(g, out var p))
                        {
                            if (p.ContentContainerObject != null && p.ContentContainerObject.activeSelf != targetExpanded)
                            {
                                p.ContentContainerObject.SetActive(targetExpanded);
                            }
                            if (p.FoldArrowElement != null)
                            {
                                p.FoldArrowElement.SetText(targetExpanded ? "▼" : "▶");
                            }
                            if (p.ResetButtonElement != null)
                            {
                                bool showReset = targetExpanded && g.ResetToDefaultsAction != null;
                                if (p.ResetButtonElement.root != null && p.ResetButtonElement.root.activeSelf != showReset)
                                {
                                    p.ResetButtonElement.SetVisible(showReset);
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                // 5. 单次恢复外层根容器激活态
                if (rootWasActive && rootToDeactivate != null)
                {
                    rootToDeactivate.SetActive(true);
                }
            }

            // 6. 仅对 ScrollRect.content 触发 1 次单帧集中排布计算，右侧滑块一步到位，绝无多帧跳动或渐变动画！
            if (UiSessionState.ScrollRect != null && UiSessionState.ScrollRect.content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(UiSessionState.ScrollRect.content);
            }
        }
    }
}
