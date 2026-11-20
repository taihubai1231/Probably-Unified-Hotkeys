#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// [!] 仅看冲突聚合视图控制器：
    /// 集中分析并聚合展现全量原版与模组按键冲突，支持直接原位改绑与动态样式刷新。
    /// </summary>
    internal static class ConflictAggregationView
    {
        internal static void ToggleConflictMode()
        {
            KeyCaptureController.CancelCapture();
            UiLayoutAndFontHelper.ClearPendingScroll();
            bool nextConflict = !UiSessionState.IsConflictViewActive;
            if (nextConflict)
            {
                UiSessionState.IsDiagnosticViewActive = false;
            }
            UiSessionState.IsConflictViewActive = nextConflict;

            if (UiSessionState.IsConflictViewActive)
            {
                RefreshConflicts();
                RebuildConflictView();
            }
            else if (!string.IsNullOrWhiteSpace(UiSessionState.SearchQuery) || UiSessionState.WasSearchNonEmpty)
            {
                TopSearchAndActionBar.ApplySearchFilter();
            }

            ModDiagnosticView.SyncViewContainersAndToggleButtons();
            UiLayoutAndFontHelper.ResetScrollToTop();
        }

        internal static void UpdateConflictToggleButtonAppearance()
        {
            if (UiSessionState.ConflictToggleBtn == null) return;

            if (UiSessionState.IsConflictViewActive)
            {
                UiLayoutAndFontHelper.SetButtonTextFast(UiSessionState.ConflictToggleBtn, I18n.Get("返回完整设置", "Back to Settings"));
                UiLayoutAndFontHelper.ApplyButtonTextTheme(UiSessionState.ConflictToggleBtn, Color.white, useDefaultTheme: true);
            }
            else
            {
                string text = UiSessionState.Conflicts.Count > 0
                    ? I18n.Format("[!] 仅看冲突 ({0} 组)", "[!] Conflicts ({0})", UiSessionState.Conflicts.Count)
                    : I18n.Get("[!] 仅看冲突", "[!] Conflicts");
                UiLayoutAndFontHelper.SetButtonTextFast(UiSessionState.ConflictToggleBtn, text);
                if (UiSessionState.Conflicts.Count > 0)
                {
                    UiLayoutAndFontHelper.ApplyButtonTextTheme(UiSessionState.ConflictToggleBtn, ConflictDetector.ConflictColor);
                }
                else
                {
                    UiLayoutAndFontHelper.ApplyButtonTextTheme(UiSessionState.ConflictToggleBtn, Color.white, useDefaultTheme: true);
                }
            }
        }

        internal static void RebuildConflictView()
        {
            if (!UiSessionState.IsConflictViewActive || UiSessionState.ConflictContentRoot == null || UiSessionState.CurrentWindow == null || CustomUIManager.Instance == null) return;
            var rt = UiSessionState.ConflictContentRoot.GetComponent<RectTransform>();
            if (rt == null) return;

            if (!UiSessionState.ConflictViewDirty && rt.childCount > 0)
            {
                return;
            }

            foreach (var hk in UiSessionState.AllHotkeys)
            {
                if (UiSessionState.TryGetHotkeyUi(hk, out var ui))
                {
                    ui.ConflictButtonElement = null;
                }
            }

            UiSessionState.RemoveWindowElements(k => k.StartsWith("cg_") || k == "header_conflict_status");

            bool wasActive = UiSessionState.ConflictContentRoot.activeSelf;
            if (wasActive)
            {
                UiSessionState.ConflictContentRoot.SetActive(false);
            }

            CustomUIBuilder.ClearContainer(rt);

            using (ComboKeyStore.EnterBypass())
            {
                var conflictBuilder = new CustomUIBuilder(CustomUIManager.Instance, UiSessionState.CurrentWindow, rt);
                BuildConflictViewContent(conflictBuilder);
            }
            UiSessionState.ConflictViewDirty = false;

            if (wasActive)
            {
                UiSessionState.ConflictContentRoot.SetActive(true);
            }

            if (UiSessionState.ScrollRect != null && UiSessionState.ScrollRect.content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(UiSessionState.ScrollRect.content);
            }
        }

        private static void BuildConflictViewContent(CustomUIBuilder builder)
        {
            var sortedConflicts = ConflictDetector.GetSortedConflictGroups(UiSessionState.Conflicts);

            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            UiLayoutAndFontHelper.ConfigureStretchRowObject(builder.CurrentContainer.gameObject, 28f);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 8f);
            string headerText = sortedConflicts.Count > 0
                ? I18n.Format(
                    "<color=#ffb347><b>当前共有 {0} 组按键发生冲突！</b></color> (点击下方按键可直接改绑，按 Esc 设为空)",
                    "<color=#ffb347><b>{0} hotkey conflict group(s) detected!</b></color> (Click any key below to rebind, Esc to clear)",
                    sortedConflicts.Count)
                : I18n.Get(
                    "<color=#55dd88><b>太棒了！当前没有任何按键冲突。</b></color>",
                    "<color=#55dd88><b>Great! No hotkey conflicts found.</b></color>");
            builder.AddHeader(headerText, "header_conflict_status");
            UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, headerText, minWidth: 240f, minHeight: 28f);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 8f);
            builder.End();
            builder.AddSeparator(null, 2);

            foreach (var cg in sortedConflicts)
            {
                string keyName = ComboKeyStore.FormatCombo(cg.Combo.Modifiers, cg.Combo.Key);
                builder.BeginRow(4f, TextAnchor.MiddleLeft);
                UiLayoutAndFontHelper.ConfigureStretchRowObject(builder.CurrentContainer.gameObject, 26f);
                UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 8f);
                string cgHdrText = I18n.Format(
                    "<b>【 冲突按键: <color=#ffb347>{0}</color> 】</b> (共 {1} 个功能重叠)",
                    "<b>[ Conflict Key: <color=#ffb347>{0}</color> ]</b> ({1} bindings overlap)",
                    keyName,
                    cg.ConflictingEntries.Count);
                builder.AddLabel(cgHdrText, $"cg_header_{(int)cg.Combo.Modifiers}_{(int)cg.Combo.Key}");
                UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, cgHdrText, minWidth: 200f, minHeight: 24f);
                builder.End();

                foreach (var entry in cg.ConflictingEntries)
                {
                    builder.BeginRow(6f, TextAnchor.MiddleLeft);
                    UiLayoutAndFontHelper.ConfigureStretchRowObject(builder.CurrentContainer.gameObject, 36f);
                    UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 20f);
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
                    UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, rowLabel, minWidth: 200f, minHeight: 30f);
                    builder.AddFlexibleSpace();

                    builder.AddButton(UiLayoutAndFontHelper.FormatHotkeyComboText(entry), () =>
                    {
                        if (!entry.IsReadOnlyProbed)
                        {
                            KeyCaptureController.StartCaptureHotkey(entry);
                        }
                    }, "cg_btn_" + entry.Id);

                    var btn = builder.Last;
                    UiLayoutAndFontHelper.StyleHotkeyButton(btn);
                    if (entry.IsReadOnlyProbed && btn?.button != null)
                    {
                        btn.button.interactable = false;
                    }
                    var hotkeyUi = UiSessionState.GetOrCreateHotkeyUi(entry);
                    hotkeyUi.ConflictButtonElement = btn;
                    if (btn != null) UiLayoutAndFontHelper.ApplySingleButtonColor(btn, entry.EffectiveCombo, entry.IsReadOnlyProbed);

                    UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 10f);
                    builder.End();
                }

                builder.AddSpace(6f);
                builder.AddSeparator(null, 1);
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

        internal static void RefreshConflicts()
        {
            using (ComboKeyStore.EnterBypass())
            {
                UiSessionState.Conflicts = ConflictDetector.Analyze(UiSessionState.AllHotkeys);
                string sig = ComputeConflictSignature(UiSessionState.Conflicts);
                if (!string.Equals(UiSessionState.LastConflictSignature, sig, StringComparison.Ordinal))
                {
                    UiSessionState.LastConflictSignature = sig;
                    UiSessionState.ConflictViewDirty = true;
                }

                // 1. 刷新所有模组卡片按键按钮与冲突标签（含只读探测热键）
                foreach (var g in UiSessionState.ModGroups)
                {
                    foreach (var h in g.Hotkeys)
                    {
                        UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(h);
                    }
                    foreach (var ph in g.ReadOnlyProbedHotkeys)
                    {
                        UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(ph);
                    }
                }

                // 2. 同步刷新上方原版按键文本、组合键格式与显示色（消除“蓝/灰不统一”）
                UnifiedOptionWindow.SyncVanillaKeyButtonColors();

                // 3. 刷新顶部冲突按钮状态
                UpdateConflictToggleButtonAppearance();
            }
        }
    }
}
