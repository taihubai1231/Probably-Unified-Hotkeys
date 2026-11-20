#nullable enable
using System;
using System.Collections.Generic;
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
        /// <summary>
        /// 装配顶部双行搜索与快捷操作栏。
        /// </summary>
        internal static void BuildTopBars(CustomUIBuilder builder, string layer, GameObject? windowRoot, bool hasThemeForLayer)
        {
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

            string topHintText = I18n.Get("组合键/右键/侧键 | Esc=无", "Combos/RClick/Side | Esc=None");
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
            OnSearchInputUpdated(text);
        }

        private static void OnSearchChanged(string? text)
        {
            string safeText = text ?? string.Empty;
            // onValueChanged (OnSearchTyping) 已即时同步过 SearchQuery；失焦触发 onEndEdit 时若文本未变直接返回，防止点击按钮前被整页清空重建吞掉 onClick
            if (string.Equals(safeText, UiSessionState.SearchQuery ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }
            OnSearchInputUpdated(safeText);
        }

        private static void OnSearchInputUpdated(string text)
        {
            UiSessionState.SearchQuery = text;
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

        /// <summary>
        /// 检查热键是否匹配当前查询关键词（复用纯内核 KeySearchMatcher 算法）。
        /// </summary>
        internal static bool IsHotkeyMatchQuery(ModHotkeyEntry hotkey, string query)
        {
            return KeySearchMatcher.MatchesHotkeyBinding(
                hotkey,
                query,
                ComboKeyStore.FormatCombo,
                ComboKeyStore.FormatPrimaryKeyName);
        }

        /// <summary>
        /// 检查常规配置项是否匹配当前查询关键词。
        /// </summary>
        internal static bool IsGeneralMatchQuery(ModGeneralEntry gen, string query)
        {
            if (PinyinEngine.Matches(gen.DisplayName, query))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(gen.SectionLabel) && PinyinEngine.Matches(gen.SectionLabel, query))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(gen.Section) && PinyinEngine.Matches(gen.Section, query))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 检查原版官方配置项是否匹配当前查询关键词。
        /// </summary>
        internal static bool IsVanillaItemMatchQuery(VanillaOptionItem item, string query)
        {
            if (item.IsKeyBind && item.LinkedHotkey != null)
            {
                return IsHotkeyMatchQuery(item.LinkedHotkey, query) ||
                       PinyinEngine.Matches(item.DisplayName, query);
            }
            return PinyinEngine.Matches(item.DisplayName, query);
        }

        private static bool IsModGroupNameMatch(ModOptionGroup group, string query, bool allowGroupName)
        {
            return allowGroupName &&
                (PinyinEngine.Matches(group.DisplayName, query) ||
                 PinyinEngine.Matches(group.ModId, query));
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

        /// <summary>
        /// 全局执行搜索与模式过滤，联动更新各卡片与行显隐。
        /// </summary>
        internal static void ApplySearchFilter()
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(UiSessionState.SearchQuery);
            bool hasDisplayFilter = UiSessionState.NormalDisplayMode != NormalDisplayMode.All;
            GetActiveDisplayPermissions(
                hasQuery,
                out bool allowGroupName,
                out bool allowHotkeys,
                out bool allowGeneral,
                out bool modeAllowsHotkeys,
                out bool modeAllowsGeneral);

            GameObject? firstMatchedHeader = null;
            int visibleVanillaCount = 0;
            int visibleModCount = 0;

            UiLayoutAndFontHelper.WithSuspendedRootLayout(() =>
            {
                using (ComboKeyStore.EnterBypass())
                {
                    // 1. 过滤原版官方设置区块（支持显示模式 + 四作用域搜索 + 繁简/拼音/按键名匹配）
                    foreach (var card in UiSessionState.VanillaCards)
                    {
                        if (!hasQuery && !hasDisplayFilter)
                        {
                            foreach (var item in card.Entries)
                            {
                                foreach (var row in item.RowObjects) SetObjectActive(row, true);
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
                            foreach (var item in card.Entries)
                            {
                                bool showRow = item.IsKeyBind ? modeAllowsHotkeys : modeAllowsGeneral;
                                foreach (var row in item.RowObjects) SetObjectActive(row, showRow);
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

                        bool sectionNameMatch = allowGroupName &&
                            (PinyinEngine.Matches(card.DisplayName, UiSessionState.SearchQuery) ||
                             PinyinEngine.Matches(card.SectionId, UiSessionState.SearchQuery));

                        bool anyItemMatch = false;
                        foreach (var item in card.Entries)
                        {
                            bool modeAllowed = item.IsKeyBind ? modeAllowsHotkeys : modeAllowsGeneral;
                            bool scopeAllowed = item.IsKeyBind ? allowHotkeys : allowGeneral;
                            bool itemMatch = modeAllowed && (sectionNameMatch || (scopeAllowed && IsVanillaItemMatchQuery(item, UiSessionState.SearchQuery)));
                            foreach (var row in item.RowObjects) SetObjectActive(row, itemMatch);
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

                        if (firstMatchedHeader == null && card.HeaderRowObject != null)
                        {
                            firstMatchedHeader = card.HeaderRowObject;
                        }
                    }

                    // 2. 过滤创意工坊模组设置卡片（挂起根布局下同步完成，绝不产生多帧依次展开过渡动画）
                    foreach (var group in UiSessionState.ModGroups)
                    {
                        UiSessionState.TryGetCardParts(group, out var parts);

                        if (!hasQuery && !hasDisplayFilter)
                        {
                            if (parts != null && parts.IsContentBuilt)
                            {
                                ApplyGroupRowVisibility(group, hasQuery: false, groupNameMatch: true, allowHotkeys: true, allowGeneral: true);
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
                                ApplyGroupRowVisibility(group, hasQuery: false, groupNameMatch: true, allowHotkeys: modeAllowsHotkeys, allowGeneral: modeAllowsGeneral);
                            }
                            CollapsibleCardView.SetGroupExpanded(group, group.IsExpanded);
                            visibleModCount++;
                            continue;
                        }

                        bool groupNameMatch = hasKindInMode && IsModGroupNameMatch(group, UiSessionState.SearchQuery, allowGroupName);
                        bool anyHotkeyMatch = allowHotkeys &&
                            (group.Hotkeys.Any(hk => IsHotkeyMatchQuery(hk, UiSessionState.SearchQuery)) ||
                             group.ReadOnlyProbedHotkeys.Any(hk => IsHotkeyMatchQuery(hk, UiSessionState.SearchQuery)));
                        bool anyGeneralMatch = allowGeneral && group.GeneralSettings.Any(gen => IsGeneralMatchQuery(gen, UiSessionState.SearchQuery));
                        bool anyProfileMatch = string.Equals(group.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase) &&
                            (allowGeneral || allowHotkeys) &&
                            ProfilePresetCardView.IsProfileSectionMatchQuery(UiSessionState.SearchQuery);

                        if (!groupNameMatch && !anyHotkeyMatch && !anyGeneralMatch && !anyProfileMatch)
                        {
                            SetCardHiddenForSearch(group);
                            continue;
                        }

                        visibleModCount++;
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
                        ApplyMatchedModGroupRows(group, groupNameMatch, allowHotkeys, allowGeneral);
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

            if (hasQuery && firstMatchedHeader != null)
            {
                UiSessionState.WasSearchNonEmpty = true;
                UiLayoutAndFontHelper.ScrollToTargetImmediate(firstMatchedHeader);
                UiSessionState.PendingScrollTarget = firstMatchedHeader;
                UiSessionState.PendingScrollMinFrame = Time.frameCount + 1;
                UiSessionState.PendingScrollRemainingPasses = 1;
            }
            else if (!hasQuery && UiSessionState.WasSearchNonEmpty)
            {
                UiSessionState.WasSearchNonEmpty = false;
                UiLayoutAndFontHelper.ClearPendingScroll();
                UiLayoutAndFontHelper.ResetScrollToTop();
            }
        }

        internal static void ApplySingleGroupVisibility(ModOptionGroup group)
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(UiSessionState.SearchQuery);
            GetActiveDisplayPermissions(
                hasQuery,
                out bool allowGroupName,
                out bool allowHotkeys,
                out bool allowGeneral,
                out bool modeAllowsHotkeys,
                out bool modeAllowsGeneral);
            bool groupNameMatch = !hasQuery || (GroupHasVisibleItemsInCurrentMode(group, modeAllowsHotkeys, modeAllowsGeneral) && IsModGroupNameMatch(group, UiSessionState.SearchQuery, allowGroupName));
            ApplyGroupRowVisibility(group, hasQuery, groupNameMatch, allowHotkeys, allowGeneral);
        }

        private static void ApplyMatchedModGroupRows(ModOptionGroup group, bool groupNameMatch, bool allowHotkeys, bool allowGeneral)
        {
            ApplyGroupRowVisibility(group, hasQuery: true, groupNameMatch, allowHotkeys, allowGeneral);
            ShowCardExpandedVisualOnly(group);
        }

        internal static void ApplyGroupRowVisibility(ModOptionGroup group, bool hasQuery, bool groupNameMatch, bool allowHotkeys, bool allowGeneral)
        {
            int visibleHotkeys = 0;
            foreach (var hotkey in group.Hotkeys)
            {
                bool match = allowHotkeys && (!hasQuery || groupNameMatch || IsHotkeyMatchQuery(hotkey, UiSessionState.SearchQuery));
                if (UiSessionState.TryGetHotkeyUi(hotkey, out var hkUi))
                {
                    SetObjectActive(hkUi.RowObject, match);
                }
                if (match) visibleHotkeys++;
            }

            int visibleGeneral = 0;
            foreach (var gen in group.GeneralSettings)
            {
                bool match = allowGeneral && (!hasQuery || groupNameMatch || IsGeneralMatchQuery(gen, UiSessionState.SearchQuery));
                if (UiSessionState.TryGetGeneralUi(gen, out var genUi))
                {
                    SetObjectActive(genUi.RowObject, match);
                }
                if (match) visibleGeneral++;
            }

            int visibleProbed = 0;
            foreach (var probed in group.ReadOnlyProbedHotkeys)
            {
                bool match = allowHotkeys && (!hasQuery || groupNameMatch || IsHotkeyMatchQuery(probed, UiSessionState.SearchQuery));
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
                bool showProfileSection = (allowGeneral || allowHotkeys) && (!hasQuery || groupNameMatch || ProfilePresetCardView.IsProfileSectionMatchQuery(UiSessionState.SearchQuery));
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
        /// 切换全部展开 / 全部收起（ADR-009 批量挂起重排优化，达成单帧瞬切）。
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

            if (UiSessionState.IsConflictViewActive)
            {
                UiSessionState.IsConflictViewActive = false;
                ModDiagnosticView.SyncViewContainersAndToggleButtons();
                ApplySearchFilter();
            }

            UiSessionState.AllExpanded = !UiSessionState.AllExpanded;
            UiLayoutAndFontHelper.SetButtonTextFast(UiSessionState.ExpandToggleBtn, UiSessionState.AllExpanded ? I18n.Get("全部收起", "Collapse All") : I18n.Get("全部展开", "Expand All"));

            bool isFiltered = !string.IsNullOrWhiteSpace(UiSessionState.SearchQuery) || UiSessionState.NormalDisplayMode != NormalDisplayMode.All;

            // 展开时，先遍历未构建的可见 ModGroups，在它们的 ContentContainerObject 仍处于 SetActive(false) 离线状态下调用 CollapsibleCardView.EnsureGroupContentBuilt(g) 批量建好内部控件
            if (UiSessionState.AllExpanded)
            {
                foreach (var g in UiSessionState.ModGroups)
                {
                    if (UiSessionState.TryGetCardParts(g, out var parts))
                    {
                        if (isFiltered && (parts.HeaderRowObject == null || !parts.HeaderRowObject.activeSelf)) continue;
                        if (!parts.IsContentBuilt)
                        {
                            CollapsibleCardView.EnsureGroupContentBuilt(g);
                        }
                    }
                }
            }

            // 随后在 UiLayoutAndFontHelper.WithSuspendedRootLayout(...) 保护下一次性切换全部原版卡与模组卡的展开态，仅触发 1 次顶层 ScrollRect.content 布局重建，达成丝滑瞬切！
            UiLayoutAndFontHelper.WithSuspendedRootLayout(() =>
            {
                using (ComboKeyStore.EnterBypass())
                {
                    foreach (var c in UiSessionState.VanillaCards)
                    {
                        if (isFiltered && (c.HeaderRowObject == null || !c.HeaderRowObject.activeSelf)) continue;
                        c.SetExpanded(UiSessionState.AllExpanded);
                        UiSessionState.VanillaExpandedStates[c.SectionId] = UiSessionState.AllExpanded;
                        if (UiSessionState.AllExpanded)
                        {
                            UiLayoutAndFontHelper.SyncCompactActionButton(c.ResetButtonElement);
                        }
                    }

                    foreach (var g in UiSessionState.ModGroups)
                    {
                        if (UiSessionState.TryGetCardParts(g, out var parts))
                        {
                            if (isFiltered && (parts.HeaderRowObject == null || !parts.HeaderRowObject.activeSelf)) continue;
                        }
                        CollapsibleCardView.SetGroupExpanded(g, UiSessionState.AllExpanded);
                        if (UiSessionState.AllExpanded && UiSessionState.TryGetCardParts(g, out var p))
                        {
                            UiLayoutAndFontHelper.SyncCompactActionButton(p.ResetButtonElement);
                        }
                    }
                }
            });
        }
    }
}
