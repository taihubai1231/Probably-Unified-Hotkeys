#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 模组设置排查与疑难接管视图控制器：
    /// 负责置顶摘要概况框、四分类筛选（疑难/未配官方/已纳管/免配置）、
    /// 候选条目强转按键/忽略非按键，以及就地热重扫与前去改键定位联动。
    /// </summary>
    internal static class ModDiagnosticView
    {
        private static readonly List<DiagnosticFilterMode> _activeDiagnosticFilterModes = new List<DiagnosticFilterMode>(6);

        internal static void SyncViewContainersAndToggleButtons()
        {
            bool showNormal = !UiSessionState.IsConflictViewActive && !UiSessionState.IsDiagnosticViewActive;
            if (UiSessionState.NormalContentRoot != null) UiSessionState.NormalContentRoot.SetActive(showNormal);
            if (UiSessionState.ConflictContentRoot != null) UiSessionState.ConflictContentRoot.SetActive(UiSessionState.IsConflictViewActive);
            if (UiSessionState.DiagnosticStickyHeaderRoot != null)
            {
                UiSessionState.DiagnosticStickyHeaderRoot.SetActive(UiSessionState.IsDiagnosticViewActive);
                if (!UiSessionState.IsDiagnosticViewActive && UiSessionState.DiagnosticStickyHeaderRoot.transform.parent is RectTransform parentRt)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
                }
            }
            if (UiSessionState.DiagnosticContentRoot != null) UiSessionState.DiagnosticContentRoot.SetActive(UiSessionState.IsDiagnosticViewActive);
            if (UiSessionState.NormalDisplayDropdown?.root != null) UiSessionState.NormalDisplayDropdown.root.SetActive(showNormal);
            if (UiSessionState.DiagnosticFilterDropdown?.root != null)
            {
                UiSessionState.DiagnosticFilterDropdown.root.SetActive(UiSessionState.IsDiagnosticViewActive);
                if (UiSessionState.IsDiagnosticViewActive)
                {
                    RefreshDiagnosticFilterDropdown();
                }
            }

            ConflictAggregationView.UpdateConflictToggleButtonAppearance();
            UpdateDiagnosticToggleButtonAppearance();

            if (UiSessionState.IsConflictViewActive)
            {
                UiSessionState.ExpandToggleBtn?.SetVisible(false);
            }
            else
            {
                UiSessionState.ExpandToggleBtn?.SetVisible(true);
                TopSearchAndActionBar.RefreshExpandAllButtonState();
                UiLayoutAndFontHelper.SyncCompactActionButton(UiSessionState.ExpandToggleBtn);
            }
        }

        internal static void ToggleDiagnosticMode()
        {
            KeyCaptureController.CancelCapture();
            UiLayoutAndFontHelper.ClearPendingScroll();
            bool nextDiagnostic = !UiSessionState.IsDiagnosticViewActive;
            if (nextDiagnostic)
            {
                UiSessionState.IsConflictViewActive = false;
            }
            UiSessionState.IsDiagnosticViewActive = nextDiagnostic;
            if (UiSessionState.ScrollRect != null)
            {
                UiSessionState.ScrollRect.scrollSensitivity = 60f;
            }

            SyncViewContainersAndToggleButtons();

            if (UiSessionState.IsDiagnosticViewActive)
            {
                UiSessionState.DiagnosticViewDirty = true;
                RebuildDiagnosticView();
            }
            else
            {
                TopSearchAndActionBar.ApplySearchFilter();
            }

            UiLayoutAndFontHelper.ResetScrollToTop();
        }

        internal static void UpdateDiagnosticToggleButtonAppearance()
        {
            if (UiSessionState.DiagnosticToggleBtn == null) return;

            if (UiSessionState.IsDiagnosticViewActive)
            {
                UiLayoutAndFontHelper.SetButtonTextFast(UiSessionState.DiagnosticToggleBtn, I18n.Get("返回完整设置", "Back to Settings"));
                UiLayoutAndFontHelper.ApplyButtonTextTheme(UiSessionState.DiagnosticToggleBtn, Color.white, useDefaultTheme: true);
            }
            else
            {
                int attentionCount = UiSessionState.ScanReport?.AttentionModCount ?? 0;
                string text = attentionCount > 0
                    ? I18n.Format("模组排查 ({0})", "Diagnostics ({0})", attentionCount)
                    : I18n.Get("模组排查", "Diagnostics");
                UiLayoutAndFontHelper.SetButtonTextFast(UiSessionState.DiagnosticToggleBtn, text);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(UiSessionState.DiagnosticToggleBtn, Color.white, useDefaultTheme: true);
            }
        }

        internal static List<string> BuildDiagnosticDropdownOptions()
        {
            var report = UiSessionState.ScanReport ?? new ModScanReport();
            int probedModCount = report.Mods.Count(m => m.ProbedHotkeys.Count > 0);
            int managedOnlyCount = report.Mods.Count(m => m.Status == ModDiagnosticStatus.Managed);

            _activeDiagnosticFilterModes.Clear();
            var list = new List<string>(6);

            _activeDiagnosticFilterModes.Add(DiagnosticFilterMode.All);
            list.Add(I18n.Format("全部 ({0})", "All ({0})", report.Mods.Count));

            if (ModFeatureConfig.EnableDllProbe)
            {
                _activeDiagnosticFilterModes.Add(DiagnosticFilterMode.ProbedHotkeysOnly);
                list.Add(I18n.Format("探测热键 ({0})", "Probed ({0})", probedModCount));
            }

            _activeDiagnosticFilterModes.Add(DiagnosticFilterMode.HasCandidatesOnly);
            list.Add(I18n.Format("疑难待确认 ({0})", "Needs Review ({0})", report.HasCandidatesModCount));

            _activeDiagnosticFilterModes.Add(DiagnosticFilterMode.UnmanagedOnly);
            list.Add(I18n.Format("未配官方 ({0})", "No Config ({0})", report.UnmanagedCodeModCount));

            _activeDiagnosticFilterModes.Add(DiagnosticFilterMode.ManagedOnly);
            list.Add(I18n.Format("正常纳管 ({0})", "Managed ({0})", managedOnlyCount));

            _activeDiagnosticFilterModes.Add(DiagnosticFilterMode.AssetOnly);
            list.Add(I18n.Format("免配置 ({0})", "No-Config ({0})", report.AssetOnlyModCount));

            return list;
        }

        internal static void RefreshDiagnosticFilterDropdown()
        {
            if (UiSessionState.DiagnosticFilterDropdown?.dropdown == null) return;
            var options = BuildDiagnosticDropdownOptions();
            var dd = UiSessionState.DiagnosticFilterDropdown.dropdown;
            dd.ClearOptions();
            dd.AddOptions(options);

            int targetVal = _activeDiagnosticFilterModes.IndexOf(UiSessionState.DiagnosticFilterMode);
            if (targetVal < 0)
            {
                UiSessionState.DiagnosticFilterMode = DiagnosticFilterMode.All;
                targetVal = 0;
            }
            dd.SetValueWithoutNotify(targetVal);
            dd.RefreshShownValue();
            I18n.ApplyFontToDropdown(UiSessionState.DiagnosticFilterDropdown, forceCjkCapableList: I18n.IsChinese);
        }

        internal static void OnDiagnosticFilterModeChanged(int index)
        {
            if (index >= 0 && index < _activeDiagnosticFilterModes.Count)
            {
                UiSessionState.DiagnosticFilterMode = _activeDiagnosticFilterModes[index];
                UiSessionState.DiagnosticViewDirty = true;
                RebuildDiagnosticView();
                UiLayoutAndFontHelper.ResetScrollToTop();
                TopSearchAndActionBar.RefreshExpandAllButtonState();
            }
        }

        internal static void RefreshDiagnosticStickyHeader()
        {
            if (!UiSessionState.IsDiagnosticViewActive || UiSessionState.DiagnosticStickyHeaderRoot == null || UiSessionState.CurrentWindow == null || CustomUIManager.Instance == null)
            {
                return;
            }

            RefreshDiagnosticFilterDropdown();

            var stickyRt = UiSessionState.DiagnosticStickyHeaderRoot.GetComponent<RectTransform>();
            if (stickyRt == null) return;

            UiSessionState.RemoveWindowElements(k => k == "label_diagnostic_summary");
            CustomUIBuilder.ClearContainer(stickyRt);
            using (ComboKeyStore.EnterBypass())
            {
                var stickyBuilder = new CustomUIBuilder(CustomUIManager.Instance, UiSessionState.CurrentWindow, stickyRt);
                BuildDiagnosticStickyHeader(stickyBuilder);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(stickyRt);
            if (stickyRt.parent is RectTransform parentRt)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
            }
        }

        private static void BuildDiagnosticStickyHeader(CustomUIBuilder builder)
        {
            var report = UiSessionState.ScanReport ?? new ModScanReport();

            builder.AddSpace(4f);

            // 第一行：左侧概况标题+指标标签 与 右侧操作按钮水平居中对齐
            builder.BeginRow(5f, TextAnchor.MiddleCenter);
            var stickyHeaderRowObj = builder.CurrentContainer.gameObject;
            var stickyHeaderHlg = stickyHeaderRowObj.GetComponent<HorizontalLayoutGroup>();
            if (stickyHeaderHlg != null)
            {
                stickyHeaderHlg.childForceExpandWidth = false;
                stickyHeaderHlg.childControlWidth = true;
            }
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 10f);

            string titleText = I18n.Get("<b><size=15>模组排查概况</size></b>", "<b><size=15>Diagnostics Overview</size></b>");
            string statsText = report.AttentionModCount > 0
                ? I18n.Format(
                    "  <color=#99a3b0>共 {0} 模组</color>  <color=#ffd27d><b>· {1} 待排查</b></color>",
                    "  <color=#99a3b0>{0} mods</color>  <color=#ffd27d><b>· {1} review</b></color>",
                    report.EnabledModCount, report.AttentionModCount)
                : I18n.Format(
                    "  <color=#99a3b0>共 {0} 模组</color>  <color=#7fe0a0><b>· 全部已纳管</b></color>",
                    "  <color=#99a3b0>{0} mods</color>  <color=#7fe0a0><b>· All managed</b></color>",
                    report.EnabledModCount);

            if (ModFeatureConfig.EnableDllProbe && report.ProbedHotkeysCount > 0)
            {
                statsText += I18n.Format(
                    "  <color=#d8b4fe><b>· {0} 探测键</b></color>",
                    "  <color=#d8b4fe><b>· {0} probed</b></color>",
                    report.ProbedHotkeysCount);
            }

            string summaryLine = titleText + statsText;
            builder.AddLabel(summaryLine, "label_diagnostic_summary");
            var summaryEl = builder.Last;
            if (summaryEl != null)
            {
                I18n.ApplyFontIfRuntime(summaryEl.text, summaryLine);
                if (summaryEl.root != null)
                {
                    var le = summaryEl.root.GetComponent<LayoutElement>() ?? summaryEl.root.AddComponent<LayoutElement>();
                    le.minWidth = 240f;
                    le.preferredWidth = -1f;
                    le.preferredHeight = 34f;
                    le.flexibleWidth = 1f;
                    le.flexibleHeight = 0f;
                }
                if (summaryEl.text != null)
                {
                    summaryEl.text.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                    summaryEl.text.enableWordWrapping = false;
                    summaryEl.text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
                }
            }

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 12f);

            builder.AddButton(I18n.Get("复制排查汇总", "Copy Summary"), CopyFullDiagnosticSummary, null);
            UiLayoutAndFontHelper.StyleActionButton(builder.Last, 112f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
            UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

            builder.AddButton(I18n.Get("强制全部重扫", "Rescan All"), () =>
            {
                PerformFullRescanAndRefreshInPlace(
                    forceInvalidateCache: true,
                    targetModId: null,
                    statusMessage: null);
            }, null);
            UiLayoutAndFontHelper.StyleActionButton(builder.Last, 112f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
            UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

            builder.AddButton(I18n.Get("返回设置", "Back"), ToggleDiagnosticMode, null);
            UiLayoutAndFontHelper.StyleActionButton(builder.Last, 84f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
            UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 10f);
            builder.End();

            // 第二行：精简辅助说明
            string helpDesc = ModFeatureConfig.EnableDllProbe
                ? I18n.Get(
                    "<color=#8c96a3><size=13>扫描官方 ModConfig / 注册表并执行静态热键探测与运行时改键接管。疑难项可设为按键绑定或忽略；上方下拉框可按分类筛选。</size></color>",
                    "<color=#8c96a3><size=13>Scans official ModConfig/Registry and static probe with runtime rebind takeover. Promote or ignore candidates; use top dropdown to filter.</size></color>")
                : I18n.Get(
                    "<color=#8c96a3><size=13>扫描官方 ModConfig、注册表及内置知名模组适配。疑难项可设为按键绑定或忽略；上方下拉框可快速按分类筛选。</size></color>",
                    "<color=#8c96a3><size=13>Scans official ModConfig, Registry, and built-in mod adapters. Promote or ignore candidates; use top dropdown to filter by category.</size></color>");

            UiLayoutAndFontHelper.AddWrappedDiagnosticLabel(builder, 10f, helpDesc);

            // 第三行：状态通知（如果有）
            if (!string.IsNullOrEmpty(UiSessionState.DiagnosticStatusMessage))
            {
                UiLayoutAndFontHelper.AddWrappedDiagnosticLabel(builder, 10f, $"<color=#7fe0a0><b>{UiSessionState.DiagnosticStatusMessage}</b></color>");
            }

            builder.AddSpace(2f);
            builder.AddSeparator(null, 2);
        }

        internal static void RebuildDiagnosticView()
        {
            if (!UiSessionState.IsDiagnosticViewActive || UiSessionState.DiagnosticContentRoot == null || UiSessionState.CurrentWindow == null || CustomUIManager.Instance == null)
            {
                return;
            }

            var rt = UiSessionState.DiagnosticContentRoot.GetComponent<RectTransform>();
            if (rt == null) return;

            if (!UiSessionState.DiagnosticViewDirty && rt.childCount > 0)
            {
                RefreshDiagnosticStickyHeader();
                TopSearchAndActionBar.RefreshExpandAllButtonState();
                return;
            }

            float savedNormalized = UiSessionState.ScrollRect != null ? UiSessionState.ScrollRect.verticalNormalizedPosition : 1f;
            bool hadExistingContent = rt.childCount > 0;

            RefreshDiagnosticStickyHeader();

            UiSessionState.RemoveWindowElements(k => k.StartsWith("diag_") || k == "label_diagnostic_summary");

            bool wasActive = UiSessionState.DiagnosticContentRoot.activeSelf;
            if (wasActive)
            {
                UiSessionState.DiagnosticContentRoot.SetActive(false);
            }

            CustomUIBuilder.ClearContainer(rt);
            UiSessionState.DiagnosticSectionViews.Clear();

            using (ComboKeyStore.EnterBypass())
            {
                var diagBuilder = new CustomUIBuilder(CustomUIManager.Instance, UiSessionState.CurrentWindow, rt);
                BuildDiagnosticViewContent(diagBuilder);
            }

            UiSessionState.DiagnosticViewDirty = false;

            if (wasActive)
            {
                UiSessionState.DiagnosticContentRoot.SetActive(true);
            }

            if (UiSessionState.ScrollRect != null && UiSessionState.ScrollRect.content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(UiSessionState.ScrollRect.content);
                if (hadExistingContent)
                {
                    UiSessionState.ScrollRect.verticalNormalizedPosition = Mathf.Clamp01(savedNormalized);
                    UiSessionState.ScrollRect.StopMovement();
                }
            }
            TopSearchAndActionBar.RefreshExpandAllButtonState();
        }

        private static void BuildDiagnosticViewContent(CustomUIBuilder builder)
        {
            var report = UiSessionState.ScanReport ?? new ModScanReport();

            string query = UiSessionState.SearchQuery?.Trim() ?? string.Empty;
            bool hasQuery = query.Length > 0;

            if (ModFeatureConfig.EnableDllProbe && UiSessionState.DiagnosticFilterMode == DiagnosticFilterMode.ProbedHotkeysOnly)
            {
                var probedItems = new List<ModDiagnosticItem>();
                foreach (var item in report.Mods)
                {
                    if (!MatchesDiagnosticFilterMode(item)) continue;
                    if (hasQuery && !MatchesDiagnosticItemFilter(item, query)) continue;
                    probedItems.Add(item);
                }

                if (probedItems.Count == 0)
                {
                    builder.AddSpace(8f);
                    UiLayoutAndFontHelper.AddWrappedDiagnosticLabel(
                        builder,
                        10f,
                        I18n.Get(
                            "<color=#99a3b0>未找到包含静态探测热键的模组（或不符合当前搜索词）</color>",
                            "<color=#99a3b0>No mods with probed hotkeys found matching the filter.</color>"
                        ));
                    return;
                }

                string probedTitle = I18n.Format(
                    "<color=#d8b4fe>[静态探测热键]</color> <b>含自动探测并接管改键模组</b> <color=#99a3b0>({0} 个模组 | 共 {1} 项探测热键)</color>",
                    "<color=#d8b4fe>[Probed Hotkeys]</color> <b>Mods With Probed & Rebindable Hotkeys</b> <color=#99a3b0>({0} mods | {1} probed keys)</color>",
                    probedItems.Count,
                    probedItems.Sum(x => x.ProbedHotkeys.Count));
                BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.UnmanagedCodeMod, probedItems, query, hasQuery, forceExpandSection: true, overrideTitle: probedTitle);
                return;
            }

            var reviewItems = new List<ModDiagnosticItem>();
            var unmanagedItems = new List<ModDiagnosticItem>();
            var managedItems = new List<ModDiagnosticItem>();
            var assetItems = new List<ModDiagnosticItem>();

            foreach (var item in report.Mods)
            {
                if (!MatchesDiagnosticFilterMode(item)) continue;
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
                UiLayoutAndFontHelper.AddWrappedDiagnosticLabel(
                    builder,
                    10f,
                    I18n.Get(
                        "<color=#99a3b0>未在模组排查列表中找到匹配的模组、配置 ID 或原始设置项</color>",
                        "<color=#99a3b0>No matching mods, config IDs, or raw entries found in diagnostics.</color>"
                    ));
                return;
            }

            bool singleCategoryMode = UiSessionState.DiagnosticFilterMode != DiagnosticFilterMode.All;
            BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.HasCandidates, reviewItems, query, hasQuery, singleCategoryMode);
            BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.UnmanagedCodeMod, unmanagedItems, query, hasQuery, singleCategoryMode);
            BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.Managed, managedItems, query, hasQuery, singleCategoryMode);
            BuildDiagnosticCategorySection(builder, ModDiagnosticStatus.AssetOnlyMod, assetItems, query, hasQuery, singleCategoryMode);
        }

        private static bool MatchesDiagnosticFilterMode(ModDiagnosticItem item)
        {
            switch (UiSessionState.DiagnosticFilterMode)
            {
                case DiagnosticFilterMode.ProbedHotkeysOnly:
                    return ModFeatureConfig.EnableDllProbe && item.ProbedHotkeys.Count > 0;
                case DiagnosticFilterMode.HasCandidatesOnly:
                    return item.Status == ModDiagnosticStatus.HasCandidates;
                case DiagnosticFilterMode.UnmanagedOnly:
                    return item.Status == ModDiagnosticStatus.UnmanagedCodeMod;
                case DiagnosticFilterMode.ManagedOnly:
                    return item.Status == ModDiagnosticStatus.Managed;
                case DiagnosticFilterMode.AssetOnly:
                    return item.Status == ModDiagnosticStatus.AssetOnlyMod;
                default:
                    return true;
            }
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
                   KeySearchMatcher.MatchesKeyCodeSearchToken(probed.EffectiveKey, query);
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
                    return I18n.Get("<color=#99a3b0>[免配置/纯机制]</color>", "<color=#99a3b0>[No-Config / Passive]</color>");
            }
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
                        "<color=#99a3b0>[免配置/纯机制]</color> <b>免配置 / 自动生效 / 纯资源模组</b> <color=#99a3b0>({0} 个，无全局热键或设置)</color>",
                        "<color=#99a3b0>[No-Config / Passive]</color> <b>Passive / Auto-Active / Asset Mods</b> <color=#99a3b0>({0}, no global settings required)</color>",
                        count);
            }
        }

        private static void BuildDiagnosticCategorySection(
            CustomUIBuilder builder,
            ModDiagnosticStatus status,
            List<ModDiagnosticItem> items,
            string query,
            bool hasQuery,
            bool forceExpandSection = false,
            string? overrideTitle = null)
        {
            if (items.Count == 0) return;

            bool initialExpanded = hasQuery || forceExpandSection || (UiSessionState.DiagnosticSectionExpanded.TryGetValue(status, out bool saved) && saved);

            CustomUIElement? foldBtn = null;
            GameObject? bodyObj = null;

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 6f);

            builder.AddButton(initialExpanded ? "▼" : "▶", () =>
            {
                if (bodyObj == null || foldBtn == null) return;
                bool nextExpanded = !bodyObj.activeSelf;
                bodyObj.SetActive(nextExpanded);
                foldBtn.SetText(nextExpanded ? "▼" : "▶");
                if (!hasQuery && !forceExpandSection)
                {
                    UiSessionState.DiagnosticSectionExpanded[status] = nextExpanded;
                }
                TopSearchAndActionBar.RefreshExpandAllButtonState();

                if (UiSessionState.DiagnosticContentRoot != null && UiSessionState.DiagnosticContentRoot.transform is RectTransform diagRt)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(diagRt);
                }
                if (UiSessionState.ScrollRect != null && UiSessionState.ScrollRect.content != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(UiSessionState.ScrollRect.content);
                }
            }, null);
            foldBtn = builder.Last;
            UiLayoutAndFontHelper.StyleFoldButton(foldBtn, 36f);

            string catTitle = overrideTitle ?? GetDiagnosticCategoryTitle(status, items.Count);
            builder.AddLabel(catTitle, null);
            UiLayoutAndFontHelper.ConfigureWrappedDiagnosticLabel(builder.Last, catTitle);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 6f);
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
                UiSessionState.DiagnosticSectionViews.Add((status, bodyObj, foldBtn));
            }

            builder.AddSeparator(null, 2);
        }

        private static void BuildDiagnosticModCard(CustomUIBuilder builder, ModDiagnosticItem item, string query, bool hasQuery)
        {
            bool anyCandMatchedQuery = hasQuery && item.CandidateEntries.Any(c => MatchesDiagnosticCandidateFilter(c, query));
            bool anyProbedMatchedQuery = hasQuery && item.ProbedHotkeys.Any(p => MatchesDiagnosticProbedFilter(p, query));
            bool forceProbedOnly = UiSessionState.DiagnosticFilterMode == DiagnosticFilterMode.ProbedHotkeysOnly;
            bool isExpanded = UiSessionState.ExpandedDiagnosticModIds.Contains(item.ModId) || anyCandMatchedQuery || anyProbedMatchedQuery || forceProbedOnly;

            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);

            string titleBadge = GetDiagnosticStatusBadge(item.Status);
            string idSuffix = item.IsWildOrphanConfig
                ? I18n.Get(" <color=#99a3b0>(独立 ModConfig)</color>", " <color=#99a3b0>(Standalone ModConfig)</color>")
                : string.Empty;

            int pendingSusCount = item.CandidateEntries.Count(c => c.IsSuspiciousCandidate && !c.IsIgnoredCandidate && !c.IsForcedHotkey);
            string statsPart = string.Empty;
            if (item.Status != ModDiagnosticStatus.AssetOnlyMod)
            {
                statsPart = I18n.Format(
                    "  <color=#9ec4ff>按键:{0}</color> <color=#a8d8b9>常规:{1}</color>",
                    "  <color=#9ec4ff>Keys:{0}</color> <color=#a8d8b9>Settings:{1}</color>",
                    item.ManagedHotkeyCount,
                    item.ManagedGeneralCount);
                if (ModFeatureConfig.EnableDllProbe && item.ProbedHotkeys.Count > 0)
                {
                    statsPart += I18n.Format(
                        " <color=#d8b4fe>探测:{0}</color>",
                        " <color=#d8b4fe>Probed:{0}</color>",
                        item.ProbedHotkeys.Count);
                }
                if (pendingSusCount > 0)
                {
                    statsPart += I18n.Format(
                        " <color=#ffd27d>待确认:{0}</color>",
                        " <color=#ffd27d>Pending:{0}</color>",
                        pendingSusCount);
                    if (item.CandidateEntries.Count > pendingSusCount)
                    {
                        statsPart += I18n.Format(
                            "<color=#99a3b0> (原始:{0})</color>",
                            "<color=#99a3b0> (Raw:{0})</color>",
                            item.CandidateEntries.Count);
                    }
                }
                else if (item.CandidateEntries.Count > 0)
                {
                    statsPart += I18n.Format(
                        " <color=#99a3b0>原始:{0}</color>",
                        " <color=#99a3b0>Raw:{0}</color>",
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
            UiLayoutAndFontHelper.ConfigureWrappedDiagnosticLabel(builder.Last, modTitleText);

            if (item.CandidateEntries.Count > 0 || item.ProbedHotkeys.Count > 0)
            {
                string inspectText = item.CandidateEntries.Count > 0
                    ? I18n.Get("查看原始项", "Inspect Raw")
                    : I18n.Get("查看探测项", "Inspect Probed");
                builder.AddButton(isExpanded ? I18n.Get("收起明细", "Hide Raw") : inspectText, () =>
                {
                    if (UiSessionState.ExpandedDiagnosticModIds.Contains(item.ModId))
                    {
                        UiSessionState.ExpandedDiagnosticModIds.Remove(item.ModId);
                    }
                    else
                    {
                        UiSessionState.ExpandedDiagnosticModIds.Add(item.ModId);
                    }
                    UiSessionState.DiagnosticViewDirty = true;
                    RebuildDiagnosticView();
                }, null);
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 114f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.82f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
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
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 98f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.82f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
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
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 114f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.82f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

                builder.AddButton(I18n.Get("复制信息", "Copy Info"), () =>
                {
                    CopyModDiagnosticInfo(item);
                }, null);
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 98f, 34f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.82f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 8f);
            builder.End();

            // 副标题与排查说明
            if (item.Status == ModDiagnosticStatus.UnmanagedCodeMod)
            {
                string authorVer = string.IsNullOrEmpty(item.Author) ? item.Version : $"{item.Author} | v{item.Version}";
                string meta = string.IsNullOrEmpty(authorVer) ? string.Empty : $"({authorVer}) ";
                string subDesc = (ModFeatureConfig.EnableDllProbe && item.ProbedHotkeys.Count > 0)
                    ? I18n.Format(
                        "<color=#b0b9c4>{0}未检测到官方 ModConfig/注册表配置，但已通过静态探测识别并接管 {1} 个热键（可点击「前去改键」直接修改）。</color>",
                        "<color=#b0b9c4>{0}No official ModConfig/Registry found, but {1} hotkey(s) were probed and integrated for runtime rebinding.</color>",
                        meta, item.ProbedHotkeys.Count)
                    : I18n.Format(
                        "<color=#b0b9c4>{0}未检测到官方配置（可在模组设置中开启静态热键探测，或点「复制信息」反馈作者）。</color>",
                        "<color=#b0b9c4>{0}No official config found (enable Static Hotkey Probe or click \"Copy Info\" to share).</color>",
                        meta);
                UiLayoutAndFontHelper.AddWrappedDiagnosticLabel(builder, 30f, subDesc);
            }
            else if (item.Status == ModDiagnosticStatus.AssetOnlyMod)
            {
                string assetDesc = item.HasAssembly
                    ? I18n.Get(
                        "<color=#99a3b0>该模组属于免配置自动生效或局内交互机制模组，无需全局快捷键或设置项。</color>",
                        "<color=#99a3b0>Confirmed passive / auto-active or in-game UI mod; no global hotkeys or settings required.</color>")
                    : I18n.Get(
                        "<color=#99a3b0>该模组不包含代码程序集，属于纯资源/数据模组，无需快捷键或设置。</color>",
                        "<color=#99a3b0>Contains no assembly code (asset / data-only mod); no settings or hotkeys required.</color>");
                UiLayoutAndFontHelper.AddWrappedDiagnosticLabel(builder, 30f, assetDesc);
            }
            else if (item.MatchedConfigIds.Count > 0 &&
                     (item.MatchedConfigIds.Count > 1 ||
                      !string.Equals(item.MatchedConfigIds[0], item.ModId, StringComparison.OrdinalIgnoreCase) ||
                      item.IsWildOrphanConfig))
            {
                UiLayoutAndFontHelper.AddWrappedDiagnosticLabel(
                    builder,
                    30f,
                    I18n.Format(
                        "<color=#9ec4ff>已关联 ModConfig ID: {0}</color>",
                        "<color=#9ec4ff>Linked ModConfig ID(s): {0}</color>",
                        string.Join(", ", item.MatchedConfigIds)));
            }

            // 展开后的疑难/候选原始条目列表及只读探测条目列表
            if (isExpanded && !forceProbedOnly && item.CandidateEntries.Count > 0)
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
            if (ModFeatureConfig.EnableDllProbe && isExpanded && item.ProbedHotkeys.Count > 0)
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
                            ? I18n.Get("<color=#ffb347>[原UI未显示·待确认]</color>", "<color=#ffb347>[Hidden · Pending]</color>")
                            : (cand.IsSuspiciousCandidate
                                ? I18n.Get("<color=#ffd27d>[疑难待确认]</color>", "<color=#ffd27d>[Suspicious · Pending]</color>")
                                : I18n.Get("<color=#a8d8b9>[常规选项·可强转]</color>", "<color=#a8d8b9>[General · Promotable]</color>")))));

            string detailText = I18n.Format(
                "{0} <b>{1}</b>  <color=#99a3b0>键:{2} | {3} | 当前:{4} | 默认:{5}</color>",
                "{0} <b>{1}</b>  <color=#99a3b0>Key:{2} | {3} | Current:{4} | Default:{5}</color>",
                tag, cand.Label, cand.EntryKey, cand.ValueTypeName, cand.CurrentValuePreview, cand.DefaultValuePreview);

            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 34f);

            builder.AddLabel(detailText, null);
            UiLayoutAndFontHelper.ConfigureWrappedDiagnosticLabel(builder.Last, detailText);

            if (cand.IsForcedHotkey)
            {
                builder.AddButton(I18n.Get("前去改键", "Rebind"), () =>
                {
                    JumpToModInNormalView(cand.OwnerModId, cand.Label);
                }, null);
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 98f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

                builder.AddButton(I18n.Get("还原", "Revert"), () =>
                {
                    ComboKeyStore.SetForcedHotkey(cand.CanonicalId, false);
                    UiSessionState.ExpandedDiagnosticModIds.Add(cand.OwnerModId);
                    PerformFullRescanAndRefreshInPlace(
                        forceInvalidateCache: true,
                        targetModId: null,
                        statusMessage: I18n.Format(
                            "已还原 [{0}] {1} 为默认识别类型",
                            "Restored [{0}] {1} to default type",
                            cand.ModDisplayName, cand.Label));
                }, null);
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 74f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }
            else if (cand.IsIgnoredCandidate)
            {
                builder.AddButton(I18n.Get("恢复提醒", "Restore"), () =>
                {
                    ComboKeyStore.SetIgnoredCandidate(cand.CanonicalId, false);
                    UiSessionState.ExpandedDiagnosticModIds.Add(cand.OwnerModId);
                    PerformFullRescanAndRefreshInPlace(
                        forceInvalidateCache: true,
                        targetModId: null,
                        statusMessage: I18n.Format(
                            "已恢复 [{0}] {1} 的疑难待确认提醒",
                            "Restored [{0}] {1} to review candidates",
                            cand.ModDisplayName, cand.Label));
                }, null);
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 96f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }
            else if (cand.CanPromoteToHotkey && !cand.IsCurrentlyHotkey)
            {
                builder.AddButton(I18n.Get("设为按键绑定", "Bind as Hotkey"), () =>
                {
                    ComboKeyStore.SetForcedHotkey(cand.CanonicalId, true);
                    UiSessionState.ExpandedDiagnosticModIds.Add(cand.OwnerModId);
                    PerformFullRescanAndRefreshInPlace(
                        forceInvalidateCache: true,
                        targetModId: null,
                        statusMessage: I18n.Format(
                            "已将 [{0}] {1} 设为按键绑定，可点击「前去改键」编辑组合键或空键",
                            "Promoted [{0}] {1} to hotkey; click \"Rebind\" to assign a combo or None",
                            cand.ModDisplayName, cand.Label));
                }, null);
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 134f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

                if (cand.IsSuspiciousCandidate)
                {
                    builder.AddButton(I18n.Get("忽略(非按键)", "Ignore"), () =>
                    {
                        ComboKeyStore.SetIgnoredCandidate(cand.CanonicalId, true);
                        UiSessionState.ExpandedDiagnosticModIds.Add(cand.OwnerModId);
                        PerformFullRescanAndRefreshInPlace(
                            forceInvalidateCache: true,
                            targetModId: null,
                            statusMessage: I18n.Format(
                                "已忽略 [{0}] {1}（不再归入待排查项，可随时点「恢复提醒」还原）",
                                "Ignored [{0}] {1} (excluded from review list; click Restore anytime)",
                                cand.ModDisplayName, cand.Label));
                    }, null);
                    UiLayoutAndFontHelper.StyleActionButton(builder.Last, 118f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                    UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
                }
            }
            else if (cand.IsCurrentlyHotkey)
            {
                builder.AddButton(I18n.Get("前去改键", "Rebind"), () =>
                {
                    JumpToModInNormalView(cand.OwnerModId, cand.Label);
                }, null);
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 98f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
                UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);
            }

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 8f);
            builder.End();
        }

        private static void BuildDiagnosticProbedRow(CustomUIBuilder builder, ModHotkeyEntry probed)
        {
            string sourceInfo = string.IsNullOrEmpty(probed.ProbedSourceDetail) ? probed.Tooltip : probed.ProbedSourceDetail;
            string detailText = I18n.Format(
                "<color=#d8b4fe>[探测·可改键]</color> <b>{0}</b>  <color=#9ec4ff>[{1}]</color>  <color=#99a3b0>{2}</color>",
                "<color=#d8b4fe>[Probed·Rebindable]</color> <b>{0}</b>  <color=#9ec4ff>[{1}]</color>  <color=#99a3b0>{2}</color>",
                probed.DisplayName,
                UiLayoutAndFontHelper.FormatHotkeyComboText(probed),
                sourceInfo);

            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 34f);

            builder.AddLabel(detailText, null);
            UiLayoutAndFontHelper.ConfigureWrappedDiagnosticLabel(builder.Last, detailText);

            builder.AddButton(I18n.Get("前去改键", "Rebind"), () =>
            {
                JumpToModInNormalView(probed.ModId, probed.DisplayName);
            }, null);
            UiLayoutAndFontHelper.StyleActionButton(builder.Last, 98f, 32f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.80f);
            UiLayoutAndFontHelper.ApplyButtonTextTheme(builder.Last, Color.white, useDefaultTheme: true);

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 8f);
            builder.End();
        }

        private static void CopyFullDiagnosticSummary()
        {
            var report = UiSessionState.ScanReport ?? new ModScanReport();
            string? ver = report.Mods.FirstOrDefault(m => string.Equals(m.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase))?.Version;
            if (string.IsNullOrEmpty(ver)) ver = "1.5.0";
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
                    sb.AppendFormat("  * Probed: {0} [{1}] ({2})\n", p.DisplayName, UiLayoutAndFontHelper.FormatHotkeyComboText(p), p.ProbedSourceDetail);
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
            UiSessionState.DiagnosticStatusMessage = I18n.Get(
                "已复制全部待排查/只读探测模组汇总信息到剪贴板",
                "Copied full diagnostics & probed hotkeys summary to clipboard");
            RefreshDiagnosticStickyHeader();
        }

        private static void CopyModDiagnosticInfo(ModDiagnosticItem item)
        {
            string info = $"ModName={item.DisplayName} | ModId={item.ModId} | Author={item.Author} | Version={item.Version} | WorkshopId={item.WorkshopId} | Status={item.Status}";
            GUIUtility.systemCopyBuffer = info;
            UiSessionState.DiagnosticStatusMessage = I18n.Format(
                "已复制 [{0}] 的模组排查信息到剪贴板",
                "Copied diagnostics for [{0}] to clipboard",
                item.DisplayName);
            RefreshDiagnosticStickyHeader();
        }

        internal static void JumpToModInNormalView(string ownerModId, string? hotkeyLabelHint)
        {
            KeyCaptureController.CancelCapture();
            UiLayoutAndFontHelper.ClearPendingScroll();
            if (UiSessionState.ScrollRect != null) UiSessionState.ScrollRect.StopMovement();
            UiSessionState.IsDiagnosticViewActive = false;
            UiSessionState.IsConflictViewActive = false;

            if (!string.IsNullOrEmpty(UiSessionState.SearchQuery))
            {
                UiSessionState.SearchQuery = string.Empty;
                UiSessionState.SearchInputField?.SetTextWithoutNotify(string.Empty);
            }
            UiSessionState.WasSearchNonEmpty = false;

            ModOptionGroup? targetGroup = UiSessionState.ModGroups.FirstOrDefault(g =>
                string.Equals(g.ModId, ownerModId, StringComparison.OrdinalIgnoreCase) ||
                (g.ExtraNotifyModIds != null && g.ExtraNotifyModIds.Any(id => string.Equals(id, ownerModId, StringComparison.OrdinalIgnoreCase))));

            bool needResetDisplayMode = UiSessionState.NormalDisplayMode != NormalDisplayMode.All &&
                (targetGroup == null || !GroupHasVisibleItemsInCurrentMode(
                    targetGroup,
                    UiSessionState.NormalDisplayMode != NormalDisplayMode.GeneralOnly,
                    UiSessionState.NormalDisplayMode != NormalDisplayMode.HotkeysOnly));
            if (needResetDisplayMode)
            {
                UiSessionState.NormalDisplayMode = NormalDisplayMode.All;
                UiSessionState.NormalDisplayDropdown?.SetIndex(0, false);
            }

            SyncViewContainersAndToggleButtons();
            TopSearchAndActionBar.ApplySearchFilter();

            if (targetGroup == null)
            {
                UiLayoutAndFontHelper.ResetScrollToTop();
                return;
            }

            UiLayoutAndFontHelper.WithSuspendedRootLayout(() =>
            {
                using (ComboKeyStore.EnterBypass())
                {
                    CollapsibleCardView.SetGroupExpanded(targetGroup, true);
                    if (UiSessionState.TryGetCardParts(targetGroup, out var parts))
                    {
                        UiLayoutAndFontHelper.StyleFoldButton(parts.FoldArrowElement, 36f);
                        UiLayoutAndFontHelper.SyncCompactActionButton(parts.ResetButtonElement);
                    }
                    TopSearchAndActionBar.RefreshExpandAllButtonState();
                }
            });

            GameObject? scrollTarget = null;
            if (UiSessionState.TryGetCardParts(targetGroup, out var targetParts))
            {
                scrollTarget = targetParts.HeaderRowObject;
            }

            if (!string.IsNullOrEmpty(hotkeyLabelHint))
            {
                var targetHk = targetGroup.Hotkeys.FirstOrDefault(h =>
                    string.Equals(h.DisplayName, hotkeyLabelHint, StringComparison.OrdinalIgnoreCase))
                    ?? targetGroup.ReadOnlyProbedHotkeys.FirstOrDefault(h =>
                    string.Equals(h.DisplayName, hotkeyLabelHint, StringComparison.OrdinalIgnoreCase));
                if (targetHk != null && UiSessionState.TryGetHotkeyUi(targetHk, out var hkUi) && hkUi.RowObject != null)
                {
                    scrollTarget = hkUi.RowObject;
                }
            }

            if (scrollTarget != null)
            {
                UiLayoutAndFontHelper.ScrollToTargetImmediate(scrollTarget);
                UiSessionState.PendingScrollTarget = scrollTarget;
                UiSessionState.PendingScrollMinFrame = Time.frameCount + 1;
                UiSessionState.PendingScrollRemainingPasses = 2;
            }
            else
            {
                UiLayoutAndFontHelper.ResetScrollToTop();
            }
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

        internal static void PerformFullRescanAndRefreshInPlace(bool forceInvalidateCache, string? targetModId, string? statusMessage)
        {
            KeyCaptureController.CancelCapture();

            var expandedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingHotkeys = new Dictionary<string, (KeyCode key, KeyModifierMask mods)>(StringComparer.OrdinalIgnoreCase);
            var pendingGenerals = new Dictionary<string, (bool? b, float? f, int? idx)>(StringComparer.OrdinalIgnoreCase);

            var oldGroups = UiSessionState.ModGroups;
            foreach (var g in oldGroups)
            {
                if (CollapsibleCardView.IsCardCurrentlyExpanded(g))
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

            UiSessionState.ModGroups = ModCatalogCache.GetOrScanGroups(out var scannedHotkeys, out UiSessionState.ScanReport);
            for (int i = 0; i < scannedHotkeys.Count; i++)
            {
                var hk = scannedHotkeys[i];
                if (hk.ModId == "vanilla" && !string.IsNullOrEmpty(hk.RawOptionId) &&
                    UiSessionState.VanillaHotkeyByRawId.TryGetValue(hk.RawOptionId, out var existingVanilla))
                {
                    if (UiSessionState.TryGetHotkeyUi(existingVanilla, out var exUi))
                    {
                        exUi.ConflictButtonElement = null;
                    }
                    scannedHotkeys[i] = existingVanilla;
                }
            }
            UiSessionState.AllHotkeys = scannedHotkeys;

            foreach (var g in UiSessionState.ModGroups)
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
                UiSessionState.CardParts.Remove(oldG);
            }

            UiLayoutAndFontHelper.WithSuspendedRootLayout(() =>
            {
                if (UiSessionState.ModCardsContainer != null && UiSessionState.CurrentWindow != null && CustomUIManager.Instance != null)
                {
                    var modRt = UiSessionState.ModCardsContainer.GetComponent<RectTransform>();
                    if (modRt != null)
                    {
                        CustomUIBuilder.ClearContainer(modRt);
                        using (ComboKeyStore.EnterBypass())
                        {
                            var modBuilder = new CustomUIBuilder(CustomUIManager.Instance, UiSessionState.CurrentWindow, modRt);
                            foreach (var group in UiSessionState.ModGroups)
                            {
                                CollapsibleCardView.BuildModGroupCardShell(modBuilder, group);
                            }
                        }
                        CollapsibleCardView.RestoreSavedExpandedModCards();
                        foreach (var group in UiSessionState.ModGroups)
                        {
                            if (UiSessionState.TryGetCardParts(group, out var parts))
                            {
                                UiLayoutAndFontHelper.StyleFoldButton(parts.FoldArrowElement, 36f);
                                if (group.IsExpanded)
                                {
                                    UiLayoutAndFontHelper.SyncCompactActionButton(parts.ResetButtonElement);
                                }
                            }
                        }
                    }
                }

                if (!UiSessionState.IsConflictViewActive && !UiSessionState.IsDiagnosticViewActive)
                {
                    TopSearchAndActionBar.ApplySearchFilter();
                }
            });

            UiSessionState.PrebuildModIndex = 0;
            UiLayoutAndFontHelper.CachedSuspendableLayoutsDirty = true;

            ComboKeyStore.RebuildRuntimeLookup(UiSessionState.AllHotkeys);
            ConflictAggregationView.RefreshConflicts();
            TopSearchAndActionBar.RefreshExpandAllButtonState();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                UiSessionState.DiagnosticStatusMessage = statusMessage;
            }
            else if (!string.IsNullOrEmpty(targetModId))
            {
                var found = UiSessionState.ScanReport?.Mods.FirstOrDefault(m => string.Equals(m.ModId, targetModId, StringComparison.OrdinalIgnoreCase));
                if (found == null)
                {
                    UiSessionState.DiagnosticStatusMessage = I18n.Format(
                        "已完成复查：当前已启用列表中未包含模组 {0}",
                        "Recheck complete: {0} is not in the enabled mods list.",
                        targetModId);
                }
                else if (found.Status == ModDiagnosticStatus.AssetOnlyMod)
                {
                    UiSessionState.DiagnosticStatusMessage = I18n.Format(
                        "已复查 [{0}]：已确认为免配置自动生效模组（无全局热键或设置项）",
                        "Rechecked [{0}]: Confirmed as passive / no-config mod (no global hotkeys or settings).",
                        found.DisplayName);
                }
                else if (found.Status == ModDiagnosticStatus.UnmanagedCodeMod)
                {
                    UiSessionState.DiagnosticStatusMessage = I18n.Format(
                        "已复查 [{0}]：未检测到官方配置项（可在设置中开启「静态热键探测与改键」自动探查并接管硬编码热键）",
                        "Rechecked [{0}]: No official config found (enable \"Static Hotkey Probe & Rebind\" to detect and rebind hardcoded keys).",
                        found.DisplayName);
                }
                else
                {
                    UiSessionState.DiagnosticStatusMessage = I18n.Format(
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
                var rpt = UiSessionState.ScanReport;
                UiSessionState.DiagnosticStatusMessage = rpt != null
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
            UiSessionState.ConflictViewDirty = true;
            if (UiSessionState.IsConflictViewActive)
            {
                ConflictAggregationView.RebuildConflictView();
            }
            UiSessionState.DiagnosticViewDirty = true;
            if (UiSessionState.IsDiagnosticViewActive)
            {
                RebuildDiagnosticView();
            }
        }

        internal static bool AllVisibleDiagnosticSectionsExpanded()
        {
            if (UiSessionState.DiagnosticSectionViews.Count == 0)
            {
                return false;
            }

            foreach (var section in UiSessionState.DiagnosticSectionViews)
            {
                if (section.BodyObj != null && !section.BodyObj.activeSelf)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
