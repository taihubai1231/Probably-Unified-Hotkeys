#nullable enable
using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 统一折叠卡片视图控制器：原版官方设置分区卡片与创意工坊模组折叠卡片的懒加载构建与交互管理。
    /// </summary>
    internal static class CollapsibleCardView
    {
        internal static GameObject? GetLastChildGameObject(RectTransform? container)
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
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to get section text: {ex.Message}");
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
            UiLayoutAndFontHelper.ConfigureStretchRowObject(headerRow, 38f);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 6f);

            builder.AddButton(initialExpanded ? "▼" : "▶", onFoldClicked, foldButtonId);
            foldBtn = builder.Last;
            UiLayoutAndFontHelper.StyleFoldButton(foldBtn, 36f);

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
            UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, cardHeaderText, minWidth: 200f, minHeight: 32f);

            builder.AddFlexibleSpace();
            buildRightContent?.Invoke(builder);

            if (onResetClicked != null)
            {
                if (buildRightContent != null)
                {
                    UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 6f);
                }
                builder.AddButton(I18n.Get("恢复默认", "Reset"), () =>
                {
                    UiSessionState.CancelCapture();
                    onResetClicked();
                }, resetButtonId);
                card.ResetButtonElement = builder.Last;
                UiLayoutAndFontHelper.StyleActionButton(card.ResetButtonElement, 112f, 36f, lrMargin: 8f, tbMargin: 2f, fontScale: 0.86f);
                card.ResetButtonElement?.SetVisible(initialExpanded && card.ResetToDefaultsAction != null);
            }

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 6f);
            builder.End();
        }

        /// <summary>
        /// 构建可折叠且支持过滤的纯原版设置区块卡片（严格过滤第三方模组条目，保留全部原版交互规则与 Apply 读写语义）。
        /// </summary>
        internal static void BuildVanillaSectionCard(CustomUIBuilder builder, GameOptionUIManager manager, GameOptionRegistry.Section section)
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
            for (int i = 0; i < section.entries.Count; i++)
            {
                var entry = section.entries[i];
                if (!ModOptionScanner.IsPureVanillaEntry(section, entry)) continue;
                filteredEntries.Add(entry);
                if (entry.kind == GameOptionRegistry.Kind.KeyBind) hkCount++;
                else genCount++;
            }

            if (filteredEntries.Count == 0) return;

            bool isExpanded = UiSessionState.VanillaExpandedStates.TryGetValue(section.id, out bool savedState) ? savedState : false;
            UiSessionState.VanillaExpandedStates[section.id] = isExpanded;

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

            string noteId = UiSessionState.NoteIdDelegate != null ? UiSessionState.NoteIdDelegate(section) : ("note_" + section.id);
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

            // 构建折叠内容容器：顶部细分隔线 + 与模组卡片完全一致的 24px/16px 内缩进列
            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            card.ContentContainerObject = builder.CurrentContainer.gameObject;
            UiLayoutAndFontHelper.ConfigureStretchColumnObject(card.ContentContainerObject);
            builder.AddSeparator(null, 1);

            builder.BeginColumn(4f, TextAnchor.UpperLeft);
            var entriesColumnRt = builder.CurrentContainer;
            UiLayoutAndFontHelper.ConfigureStretchColumnObject(entriesColumnRt.gameObject);
            var entriesLayout = entriesColumnRt.GetComponent<VerticalLayoutGroup>();
            if (entriesLayout != null)
            {
                entriesLayout.padding = new RectOffset(24, 16, 2, 6);
            }

            for (int e = 0; e < filteredEntries.Count; e++)
            {
                var entry = filteredEntries[e];
                int childBefore = entriesColumnRt.childCount;
                UiSessionState.BuildEntryDelegate?.Invoke(manager, builder, entry);

                var vanillaLabelEl = builder.Handle?.Get("label_" + entry.id) ?? UiSessionState.CurrentWindow?.Get("label_" + entry.id);
                if (vanillaLabelEl != null)
                {
                    UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(vanillaLabelEl, vanillaLabelEl.text != null ? vanillaLabelEl.text.text : SafeSectionText(entry.label, entry.id), minWidth: 180f);
                }
                var vanillaCtrlEl = builder.Handle?.Get(entry.id) ?? UiSessionState.CurrentWindow?.Get(entry.id);
                if (vanillaCtrlEl?.input != null && vanillaCtrlEl.root != null)
                {
                    var vLe = vanillaCtrlEl.root.GetComponent<LayoutElement>() ?? vanillaCtrlEl.root.AddComponent<LayoutElement>();
                    vLe.layoutPriority = 100;
                }

                ModHotkeyEntry? linked = null;
                if (entry.kind == GameOptionRegistry.Kind.KeyBind)
                {
                    if (UiSessionState.VanillaHotkeyByRawId.TryGetValue(entry.id, out linked) && linked != null)
                    {
                        var hkUi = UiSessionState.GetOrCreateHotkeyUi(linked);
                        hkUi.LabelElement = vanillaLabelEl;
                        hkUi.ButtonElement = vanillaCtrlEl;
                    }
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
                    var rowGo = entriesColumnRt.GetChild(i).gameObject;
                    UiLayoutAndFontHelper.ConfigureStretchRowObject(rowGo, 32f);
                    item.RowObjects.Add(rowGo);
                }
                card.Entries.Add(item);
            }

            builder.End(); // entriesColumnRt
            builder.End(); // ContentContainerObject

            builder.AddSeparator(null, 2);
            card.OuterSeparatorObject = GetLastChildGameObject(builder.CurrentContainer);

            card.SetExpanded(isExpanded);
            UiSessionState.VanillaCards.Add(card);
        }

        /// <summary>
        /// 构建模组卡片折叠外壳（ADR-009 开窗极速优化：首帧仅构建头部与空容器，绝不实例化内部几百行控件）。
        /// </summary>
        internal static void BuildModGroupCardShell(CustomUIBuilder builder, ModOptionGroup group)
        {
            var parts = UiSessionState.GetOrCreateCardParts(group);
            parts.IsContentBuilt = false;

            Action? onModReset = group.ResetToDefaultsAction != null
                ? () =>
                {
                    bool wasChinese = I18n.IsChinese;
                    using (ComboKeyStore.EnterBypass())
                    {
                        group.ResetToDefaultsAction?.Invoke();
                        UiSessionState.RefreshCardUI(group);
                        ComboKeyStore.RebuildRuntimeLookup(UiSessionState.AllHotkeys);
                    }
                    NotifyGroupConfigsApplied(group);
                    if (wasChinese != I18n.IsChinese && UiSessionState.CurrentManager != null)
                    {
                        UnifiedOptionWindow.ReopenEnhancedUIForLanguageSwitch(UiSessionState.CurrentManager, group.ModId);
                    }
                }
                : null;

            var cardAdapter = new ModOptionCard(group, parts);
            BuildCardHeaderRow(
                builder,
                cardAdapter,
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
            parts.HeaderRowObject = headerRow;
            parts.FoldArrowElement = foldBtn;

            // 2. 卡片展开内容占位容器（折叠态初始为空，由窗口开启后的静默后台预建或按需触发填充）
            builder.BeginColumn(3f, TextAnchor.UpperLeft);
            parts.ContentContainerObject = builder.CurrentContainer.gameObject;
            UiLayoutAndFontHelper.ConfigureStretchColumnObject(parts.ContentContainerObject);
            parts.ContentContainerObject.SetActive(false);
            builder.End();

            // 3. 卡片外部分界横线（无论折叠还是展开都清晰划分模组卡片边界）
            builder.AddSeparator(null, 2);
            parts.OuterSeparatorObject = GetLastChildGameObject(builder.CurrentContainer);
        }

        internal static void RestoreSavedExpandedModCards()
        {
            for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
            {
                var group = UiSessionState.ModGroups[i];
                if (group.IsExpanded)
                {
                    SetGroupExpanded(group, true);
                }
            }
        }

        /// <summary>
        /// 确保模组卡片内容已完成离线批量构建（ADR-009：离线非激活态生成全部子控件，避免逐控件重建 CanvasRegistry）。
        /// </summary>
        internal static void EnsureGroupContentBuilt(ModOptionGroup group)
        {
            var parts = UiSessionState.GetOrCreateCardParts(group);
            if (parts.IsContentBuilt || parts.ContentContainerObject == null || UiSessionState.CurrentWindow == null || CustomUIManager.Instance == null)
            {
                return;
            }

            var contentRt = parts.ContentContainerObject.GetComponent<RectTransform>();
            if (contentRt == null) return;

            bool wasActive = parts.ContentContainerObject.activeSelf;
            if (wasActive)
            {
                parts.ContentContainerObject.SetActive(false);
            }

            parts.IsContentBuilt = true;

            try
            {
                using (ComboKeyStore.EnterBypass())
                {
                    var builder = new CustomUIBuilder(CustomUIManager.Instance, UiSessionState.CurrentWindow, contentRt);

                    // 头部与内容区的细横线分隔
                    builder.AddSeparator(null, 1);

                    GameObject? hkHeaderObj = null;
                    GameObject? probedHeaderObj = null;
                    GameObject? probedSepObj = null;

                    // --- 上半截：快捷键绑定 ---
                    if (group.Hotkeys.Count > 0)
                    {
                        builder.BeginRow(4f, TextAnchor.MiddleLeft);
                        hkHeaderObj = builder.CurrentContainer.gameObject;
                        UiLayoutAndFontHelper.ConfigureStretchRowObject(hkHeaderObj, 26f);
                        UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);
                        string hkHdrText = I18n.Get("<color=#9ec4ff><b>快捷键</b></color>", "<color=#9ec4ff><b>Hotkeys</b></color>");
                        builder.AddLabel(hkHdrText, "hk_hdr_" + group.ModId);
                        UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, hkHdrText, minWidth: 160f, minHeight: 24f);
                        builder.End();

                        for (int i = 0; i < group.Hotkeys.Count; i++)
                        {
                            BuildHotkeyRow(builder, group.Hotkeys[i]);
                        }
                    }

                    // --- 下半截：常规通用配置 (按原模组 Section 分组展示：开关 / 滑块 / 下拉框 / 文本输入框) ---
                    if (group.GeneralSettings.Count > 0)
                    {
                        var orderedSections = GroupGeneralSettingsBySection(group);
                        for (int sIdx = 0; sIdx < orderedSections.Count; sIdx++)
                        {
                            var (secTitle, secEntries) = orderedSections[sIdx];
                            var secBlock = new GeneralSectionBlock();
                            secBlock.Entries.AddRange(secEntries);

                            // 分隔线（首个常规分区仅在上方有可见快捷键时显示；后续分区在前序有可见内容时显示）
                            builder.AddSeparator(null, 1);
                            if (contentRt.childCount > 0)
                            {
                                secBlock.SeparatorObj = contentRt.GetChild(contentRt.childCount - 1).gameObject;
                                if (sIdx == 0 && group.Hotkeys.Count == 0)
                                {
                                    secBlock.SeparatorObj.SetActive(false);
                                }
                            }

                            builder.BeginRow(4f, TextAnchor.MiddleLeft);
                            secBlock.HeaderObj = builder.CurrentContainer.gameObject;
                            UiLayoutAndFontHelper.ConfigureStretchRowObject(secBlock.HeaderObj, 26f);
                            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);
                            string genHdrText = $"<color=#a8d8b9><b>{secTitle}</b></color>";
                            builder.AddLabel(genHdrText, $"gen_hdr_{group.ModId}_{sIdx}");
                            UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, genHdrText, minWidth: 160f, minHeight: 24f);
                            builder.End();

                            for (int eIdx = 0; eIdx < secEntries.Count; eIdx++)
                            {
                                BuildGeneralSettingRow(builder, secEntries[eIdx]);
                            }

                            parts.GeneralSections.Add(secBlock);
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
                        UiLayoutAndFontHelper.ConfigureStretchRowObject(probedHeaderObj, 26f);
                        UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);
                        string probedHdrText = I18n.Get(
                            "<color=#d8b4fe><b>自动探测热键 (只读 · 仅供冲突排查)</b></color>",
                            "<color=#d8b4fe><b>Probed Hotkeys (Read-Only · Conflict Check)</b></color>");
                        builder.AddLabel(probedHdrText, "probed_hdr_" + group.ModId);
                        UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, probedHdrText, minWidth: 200f, minHeight: 24f);
                        builder.End();

                        for (int i = 0; i < group.ReadOnlyProbedHotkeys.Count; i++)
                        {
                            BuildHotkeyRow(builder, group.ReadOnlyProbedHotkeys[i]);
                        }
                    }

                    // --- 本模组专属：键位与设置方案快照管理面板（3 套场景预设槽位 + 剪贴板 JSON 导入/导出） ---
                    if (string.Equals(group.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase))
                    {
                        ProfilePresetCardView.BuildSelfModProfileSection(builder);
                    }

                    builder.AddSpace(4f);
                    parts.HkHeader = hkHeaderObj;
                    parts.ProbedHeader = probedHeaderObj;
                    parts.ProbedSep = probedSepObj;

                    if (UiSessionState.NormalDisplayMode != NormalDisplayMode.All || !string.IsNullOrWhiteSpace(UiSessionState.SearchQuery))
                    {
                        TopSearchAndActionBar.ApplySearchFilter();
                    }
                }
            }
            finally
            {
                if (parts.ContentContainerObject != null)
                {
                    parts.ContentContainerObject.SetActive(group.IsExpanded);
                }
            }
        }

        internal static void SetGroupExpanded(ModOptionGroup group, bool expanded)
        {
            if (expanded)
            {
                EnsureGroupContentBuilt(group);
            }
            var parts = UiSessionState.GetOrCreateCardParts(group);
            parts.SetGroupUiExpanded(group, expanded);
        }

        internal static bool IsCardCurrentlyExpanded(ICollapsibleOptionCard card)
        {
            return card.ContentContainerObject != null ? card.ContentContainerObject.activeSelf : card.IsExpanded;
        }

        internal static bool IsCardCurrentlyExpanded(ModOptionGroup group)
        {
            if (UiSessionState.TryGetCardParts(group, out var parts) && parts.ContentContainerObject != null)
            {
                return parts.ContentContainerObject.activeSelf;
            }
            return group.IsExpanded;
        }

        private static void OnManualFoldVanillaCard(VanillaSectionCard card)
        {
            bool nextExpanded = !IsCardCurrentlyExpanded(card);
            card.SetExpanded(nextExpanded);
            UiSessionState.VanillaExpandedStates[card.SectionId] = nextExpanded;
            TopSearchAndActionBar.RefreshExpandAllButtonState();
        }

        private static void OnManualFoldModGroup(ModOptionGroup group)
        {
            bool nextExpanded = !IsCardCurrentlyExpanded(group);
            SetGroupExpanded(group, nextExpanded);
            TopSearchAndActionBar.RefreshExpandAllButtonState();
        }

        private static List<(string SectionTitle, List<ModGeneralEntry> Entries)> GroupGeneralSettingsBySection(ModOptionGroup group)
        {
            string defaultHeader = I18n.Get("常规设置", "General Settings");
            var buckets = new List<(string RawTitle, List<ModGeneralEntry> Entries)>();
            var indexByTitle = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < group.GeneralSettings.Count; i++)
            {
                var gen = group.GeneralSettings[i];
                if (gen == null) continue;

                string rawTitle = !string.IsNullOrWhiteSpace(gen.SectionLabel)
                    ? gen.SectionLabel.Trim()
                    : (!string.IsNullOrWhiteSpace(gen.Section)
                        ? ModOptionScanner.FormatModSectionLabel(gen.Section, null, group.ModId)
                        : string.Empty);

                if (IsDefaultOrSelfModSectionTitle(rawTitle, group) ||
                    IsDefaultOrSelfModSectionTitle(gen.Section, group))
                {
                    rawTitle = string.Empty;
                }

                if (!indexByTitle.TryGetValue(rawTitle, out int idx))
                {
                    idx = buckets.Count;
                    indexByTitle[rawTitle] = idx;
                    buckets.Add((rawTitle, new List<ModGeneralEntry>()));
                }
                buckets[idx].Entries.Add(gen);
            }

            var result = new List<(string SectionTitle, List<ModGeneralEntry> Entries)>(buckets.Count);
            for (int i = 0; i < buckets.Count; i++)
            {
                string title = string.IsNullOrEmpty(buckets[i].RawTitle) ? defaultHeader : buckets[i].RawTitle;
                result.Add((title, buckets[i].Entries));
            }
            return result;
        }

        private static bool IsDefaultOrSelfModSectionTitle(string? title, ModOptionGroup group)
        {
            if (string.IsNullOrWhiteSpace(title)) return true;
            string t = title!.Trim();
            if (string.Equals(t, "常规", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "常规设置", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "General", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "General Settings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Settings", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "Config", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(t, group.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, group.ModId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(group.ModId))
            {
                int dot = group.ModId.IndexOf('.');
                if (dot >= 0 && dot + 1 < group.ModId.Length)
                {
                    string modIdTail = group.ModId.Substring(dot + 1);
                    if (string.Equals(t, modIdTail, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(group.DisplayName) && group.DisplayName.IndexOf('/') >= 0)
            {
                string[] dispParts = group.DisplayName.Split('/');
                for (int i = 0; i < dispParts.Length; i++)
                {
                    string dp = dispParts[i].Trim();
                    if (dp.Length > 0 && string.Equals(t, dp, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                if (dispParts.Length == 2 && t.IndexOf('/') >= 0)
                {
                    string[] secParts = t.Split('/');
                    if (secParts.Length == 2 &&
                        string.Equals(dispParts[0].Trim(), secParts[1].Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(dispParts[1].Trim(), secParts[0].Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void BuildGeneralSettingRow(CustomUIBuilder builder, ModGeneralEntry gen)
        {
            var genUi = UiSessionState.GetOrCreateGeneralUi(gen);
            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            genUi.RowObject = builder.CurrentContainer.gameObject;
            UiLayoutAndFontHelper.ConfigureStretchRowObject(genUi.RowObject, 34f);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);

            string labelText = gen.DisplayName;
            if (gen.IsReadOnlyInfo)
            {
                labelText = $"<color=#99a3b0>{labelText}</color>";
            }
            if (!string.IsNullOrEmpty(gen.Tooltip))
            {
                string link = "modopt:" + gen.Id;
                TMPIconHover.CustomLinks[link] = () => gen.Tooltip;
                labelText += $" <link=\"{link}\"><sprite name=\"info\" tint=1></link>";
            }

            builder.AddLabel(labelText, "label_" + gen.Id);
            if (gen.IsReadOnlyInfo)
            {
                UiLayoutAndFontHelper.ConfigureWrappedDiagnosticLabel(builder.Last, labelText);
                UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 10f);
                builder.End();
                return;
            }

            UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, labelText, minWidth: 200f, minHeight: 30f);
            builder.AddFlexibleSpace();

            switch (gen.Kind)
            {
                case OptionKind.Toggle:
                    bool currentBool = gen.GetBool?.Invoke() ?? false;
                    builder.AddToggle(string.Empty, currentBool, (val) =>
                    {
                        gen.PendingBool = val;
                    }, "toggle_" + gen.Id);
                    genUi.ControlElement = builder.Last;
                    break;

                case OptionKind.Slider:
                    float currentFloat = gen.GetFloat?.Invoke() ?? gen.Min;
                    string sliderValText = UiLayoutAndFontHelper.FormatSliderValue(gen, currentFloat);
                    builder.AddLabel(sliderValText, "val_" + gen.Id);
                    genUi.ValueLabelElement = builder.Last;
                    UiLayoutAndFontHelper.ConfigureFixedWidthControl(genUi.ValueLabelElement, 58f, 30f);
                    if (genUi.ValueLabelElement?.text != null)
                    {
                        genUi.ValueLabelElement.text.enableWordWrapping = false;
                        genUi.ValueLabelElement.text.overflowMode = TextOverflowModes.Overflow;
                        genUi.ValueLabelElement.text.alignment = TextAlignmentOptions.MidlineRight;
                        I18n.ApplyFontIfRuntime(genUi.ValueLabelElement.text, sliderValText);
                    }

                    builder.AddSlider(gen.Min, gen.Max, currentFloat, (val) =>
                    {
                        gen.PendingFloat = val;
                        genUi.ValueLabelElement?.SetText(UiLayoutAndFontHelper.FormatSliderValue(gen, val));
                    }, gen.WholeNumbers, "slider_" + gen.Id);
                    genUi.ControlElement = builder.Last;
                    UiLayoutAndFontHelper.ConfigureFixedWidthControl(genUi.ControlElement, 242f, 30f);
                    break;

                case OptionKind.Dropdown:
                    var choices = gen.GetChoices?.Invoke() ?? new List<string>();
                    int currentIdx = gen.GetIndex?.Invoke() ?? 0;
                    builder.AddDropdown(choices, currentIdx, (idx) =>
                    {
                        gen.PendingIndex = idx;
                    }, "drop_" + gen.Id);
                    genUi.ControlElement = builder.Last;
                    UiLayoutAndFontHelper.ConfigureFixedWidthControl(genUi.ControlElement, 306f, 36f);
                    bool isLangDrop = string.Equals(gen.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase);
                    I18n.ApplyFontToDropdown(genUi.ControlElement, forceCjkCapableList: isLangDrop || I18n.IsChinese);
                    break;

                case OptionKind.Input:
                    string currentStr = gen.PendingString ?? (gen.GetString?.Invoke() ?? string.Empty);
                    builder.AddInput(
                        I18n.Get("输入内容...", "Enter text..."),
                        currentStr,
                        (val) =>
                        {
                            gen.PendingString = val ?? string.Empty;
                        },
                        "input_" + gen.Id,
                        (val) =>
                        {
                            gen.PendingString = val ?? string.Empty;
                        }
                    );
                    genUi.ControlElement = builder.Last;
                    UiLayoutAndFontHelper.ConfigureSearchOrLongInputLayout(genUi.ControlElement, 306f, false);
                    if (genUi.ControlElement?.input != null)
                    {
                        I18n.ApplyFontIfRuntime(genUi.ControlElement.input.textComponent, null);
                        if (genUi.ControlElement.input.placeholder is TextMeshProUGUI phTmp)
                        {
                            I18n.ApplyFontIfRuntime(phTmp, phTmp.text);
                        }
                    }
                    break;
            }

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 10f);
            builder.End();
        }

        internal static void BuildHotkeyRow(CustomUIBuilder builder, ModHotkeyEntry hotkey, bool isConflictRow = false)
        {
            var ui = UiSessionState.GetOrCreateHotkeyUi(hotkey);
            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            var rowObj = builder.CurrentContainer.gameObject;
            if (!isConflictRow)
            {
                ui.RowObject = rowObj;
            }
            UiLayoutAndFontHelper.ConfigureStretchRowObject(rowObj, 36f);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, isConflictRow ? 20f : 18f);

            if (isConflictRow)
            {
                string rowLabel = hotkey.IsReadOnlyProbed
                    ? I18n.Get("<color=#d8b4fe>[探测·只读]</color> ", "<color=#d8b4fe>[Probed·Read-Only]</color> ") + $"[{hotkey.ModDisplayName}]  {hotkey.DisplayName}"
                    : $"[{hotkey.ModDisplayName}]  {hotkey.DisplayName}";
                if (hotkey.IsReadOnlyProbed && !string.IsNullOrEmpty(hotkey.Tooltip))
                {
                    string infoLink = "cg_modkey:" + hotkey.Id;
                    TMPIconHover.CustomLinks[infoLink] = () => hotkey.Tooltip;
                    rowLabel += $" <link=\"{infoLink}\"><sprite name=\"info\" tint=1></link>";
                }
                builder.AddLabel(rowLabel, "cg_label_" + hotkey.Id);
                UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, rowLabel, minWidth: 200f, minHeight: 30f);

                builder.AddFlexibleSpace();

                string keyText = UiLayoutAndFontHelper.FormatHotkeyComboText(hotkey);
                builder.AddButton(keyText, () =>
                {
                    if (!hotkey.IsReadOnlyProbed)
                    {
                        KeyCaptureController.StartCaptureHotkey(hotkey);
                    }
                }, "cg_btn_" + hotkey.Id);

                var btn = builder.Last;
                ui.ConflictButtonElement = btn;
                UiLayoutAndFontHelper.StyleHotkeyButton(btn);
                if (hotkey.IsReadOnlyProbed && btn?.button != null)
                {
                    btn.button.interactable = false;
                }
                if (btn != null && UiSessionState.CapturingEntry != hotkey)
                {
                    UiLayoutAndFontHelper.ApplySingleButtonColor(btn, hotkey.EffectiveCombo, hotkey.IsReadOnlyProbed);
                }
            }
            else
            {
                string hkLabel = UiLayoutAndFontHelper.BuildHotkeyLabelText(hotkey);
                builder.AddLabel(hkLabel, "label_" + hotkey.Id);
                ui.LabelElement = builder.Last;
                UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(ui.LabelElement, hkLabel, minWidth: 200f, minHeight: 30f);

                builder.AddFlexibleSpace();

                string keyText = UiLayoutAndFontHelper.FormatHotkeyComboText(hotkey);
                builder.AddButton(keyText, () =>
                {
                    if (!hotkey.IsReadOnlyProbed)
                    {
                        KeyCaptureController.StartCaptureHotkey(hotkey);
                    }
                }, "btn_key_" + hotkey.Id);

                ui.ButtonElement = builder.Last;
                UiLayoutAndFontHelper.StyleHotkeyButton(ui.ButtonElement);
                if (hotkey.IsReadOnlyProbed && ui.ButtonElement?.button != null)
                {
                    ui.ButtonElement.button.interactable = false;
                }

                if (ui.ButtonElement != null && UiSessionState.CapturingEntry != hotkey)
                {
                    UiLayoutAndFontHelper.ApplySingleButtonColor(ui.ButtonElement, hotkey.EffectiveCombo, hotkey.IsReadOnlyProbed);
                }
            }

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 10f);
            builder.End();
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

        internal static void ResetVanillaSectionToDefaults(GameOptionUIManager manager, VanillaSectionCard card)
        {
            if (UiSessionState.CurrentWindow == null || manager == null || card == null) return;

            UiSessionState.CancelCapture();

            var pendingKeys = UiSessionState.PendingKeysField?.GetValue(manager) as Dictionary<string, KeyCode>;
            bool hadKeyBind = false;
            bool hasDisplayResolution = false;

            using (ComboKeyStore.EnterBypass())
            {
                for (int i = 0; i < card.Entries.Count; i++)
                {
                    var item = card.Entries[i];
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
                            UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(item.LinkedHotkey);
                        }
                        hadKeyBind = true;
                        continue;
                    }

                    if (string.Equals(entry.id, "resolution", StringComparison.Ordinal))
                    {
                        hasDisplayResolution = true;
                        continue;
                    }

                    var el = UiSessionState.CurrentWindow.Get(entry.id);
                    if (el == null) continue;

                    switch (entry.kind)
                    {
                        case GameOptionRegistry.Kind.Toggle:
                            if (UiSessionState.VanillaDefaultBools.TryGetValue(entry.id, out bool defaultBool))
                            {
                                el.SetBool(defaultBool, true);
                            }
                            break;

                        case GameOptionRegistry.Kind.Slider:
                            if (UiSessionState.VanillaDefaultFloats.TryGetValue(entry.id, out float defaultFloat))
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
                UiSessionState.InvokeRefreshRulesSafe(manager);

                if (hasDisplayResolution)
                {
                    var resEl = UiSessionState.CurrentWindow.Get("resolution");
                    if (resEl != null)
                    {
                        var overridden = UiSessionState.OverriddenDropdownsField?.GetValue(manager) as System.Collections.IDictionary;
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
                        UiSessionState.InvokeRefreshRulesSafe(manager);
                    }
                }

                if (hadKeyBind)
                {
                    UiSessionState.SetKeyBindStatusDelegate?.Invoke(manager, string.Empty);
                    UiSessionState.RefreshConflicts();
                    ConflictAggregationView.RefreshConflicts();
                    if (UiSessionState.IsConflictViewActive)
                    {
                        UiSessionState.RebuildConflictViewCallback?.Invoke();
                    }
                }
            }
        }

        private static void NotifyGroupConfigsApplied(ModOptionGroup group)
        {
            if (group == null) return;
            NotifyModConfigApplied(group.ModId);
            if (group.ExtraNotifyModIds != null && group.ExtraNotifyModIds.Count > 0)
            {
                for (int i = 0; i < group.ExtraNotifyModIds.Count; i++)
                {
                    string extraId = group.ExtraNotifyModIds[i];
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
                if (ModHook.Instance != null && UiSessionState.FireModConfigAppliedDelegate != null)
                {
                    UiSessionState.FireModConfigAppliedDelegate.Invoke(modId);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to notify mod config applied for {modId}: {ex.Message}");
            }
        }

        internal sealed class ModOptionCard : ICollapsibleOptionCard
        {
            public ModOptionGroup Group { get; }
            public ModCardUiParts Parts { get; }

            public ModOptionCard(ModOptionGroup group, ModCardUiParts parts)
            {
                Group = group;
                Parts = parts;
            }

            public bool IsExpanded => Group.IsExpanded;

            public Action? ResetToDefaultsAction
            {
                get => Group.ResetToDefaultsAction;
                set => Group.ResetToDefaultsAction = value;
            }

            public GameObject? HeaderRowObject => Parts.HeaderRowObject;
            public GameObject? ContentContainerObject => Parts.ContentContainerObject;
            public GameObject? OuterSeparatorObject => Parts.OuterSeparatorObject;
            public CustomUIElement? FoldArrowElement => Parts.FoldArrowElement;

            public CustomUIElement? ResetButtonElement
            {
                get => Parts.ResetButtonElement;
                set => Parts.ResetButtonElement = value;
            }

            public void SetExpanded(bool expanded)
            {
                Parts.SetGroupUiExpanded(Group, expanded);
            }
        }
    }
}
