#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 本模组专属预设卡片视图：3 槽位方案预设与剪贴板紧凑 JSON 导出/导入管理面板。
    /// </summary>
    internal static class ProfilePresetCardView
    {
        internal static bool IsProfileSectionMatchQuery(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return PinyinEngine.Matches("键位与设置方案快照 预设 方案一 方案二 方案三 保存方案 读取方案 清空 导出剪贴板 导入剪贴板 剪贴板 JSON 备份 恢复 快捷键 强转表", query!) ||
                   PinyinEngine.Matches("Keybind Preset Profile Snapshot Export Import Clipboard JSON Backup Restore", query!);
        }

        internal static void BuildSelfModProfileSection(CustomUIBuilder builder)
        {
            builder.BeginColumn(4f, TextAnchor.UpperLeft);
            UiSessionState.SelfProfileSectionContainer = builder.CurrentContainer.gameObject;
            var vlg = UiSessionState.SelfProfileSectionContainer.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) vlg.childForceExpandWidth = true;
            var le = UiSessionState.SelfProfileSectionContainer.GetComponent<LayoutElement>() ?? UiSessionState.SelfProfileSectionContainer.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            builder.AddSeparator(null, 1);

            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            UiLayoutAndFontHelper.ConfigureStretchRowObject(builder.CurrentContainer.gameObject, 26f);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);
            string hdrText = I18n.Get(
                "<color=#a8d8b9><b>键位与设置方案快照</b></color>",
                "<color=#a8d8b9><b>Keybind & Settings Profiles</b></color>");
            builder.AddLabel(hdrText, "profile_hdr_self");
            UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, hdrText, minWidth: 180f, minHeight: 24f);
            builder.End();

            for (int slot = 0; slot < HotkeyProfileManager.SlotCount; slot++)
            {
                int capturedSlot = slot;
                builder.BeginRow(6f, TextAnchor.MiddleLeft);
                UiLayoutAndFontHelper.ConfigureStretchRowObject(builder.CurrentContainer.gameObject, 36f);
                UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);

                string rowLabelText = BuildSelfModSlotRowLabelText(capturedSlot);
                builder.AddLabel(rowLabelText, $"label_profile_slot_{capturedSlot}");
                UiSessionState.SelfProfileSlotDetailLabels[capturedSlot] = builder.Last;
                UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, rowLabelText, minWidth: 220f, minHeight: 30f);

                builder.AddFlexibleSpace();

                builder.AddButton(I18n.Get("保存", "Save"), () =>
                {
                    HotkeyProfileManager.ActiveSlotIndex = capturedSlot;
                    OnSaveProfileSlotClicked(capturedSlot);
                }, $"btn_slot_save_{capturedSlot}");
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 78f, 36f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.84f);
                UiLayoutAndFontHelper.SyncCompactActionButton(builder.Last);

                builder.AddButton(I18n.Get("读取", "Load"), () =>
                {
                    HotkeyProfileManager.ActiveSlotIndex = capturedSlot;
                    OnLoadProfileSlotClicked(capturedSlot);
                }, $"btn_slot_load_{capturedSlot}");
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 78f, 36f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.84f);
                UiLayoutAndFontHelper.SyncCompactActionButton(builder.Last);

                builder.AddButton(I18n.Get("清空", "Clear"), () =>
                {
                    OnClearProfileSlotClicked(capturedSlot);
                }, $"btn_slot_clear_{capturedSlot}");
                UiLayoutAndFontHelper.StyleActionButton(builder.Last, 78f, 36f, lrMargin: 6f, tbMargin: 2f, fontScale: 0.84f);
                UiLayoutAndFontHelper.SyncCompactActionButton(builder.Last);

                UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 10f);
                builder.End();
            }

            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            UiLayoutAndFontHelper.ConfigureStretchRowObject(builder.CurrentContainer.gameObject, 36f);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);
            string clipDesc = I18n.Get(
                "<b>剪贴板 JSON 备份</b>  <color=#99a3b0>一键导出或恢复全部快捷键、模组设置与强转表</color>",
                "<b>Clipboard JSON Backup</b>  <color=#99a3b0>Export or restore all hotkeys, mod settings & forced tables</color>");
            builder.AddLabel(clipDesc, "label_profile_clipboard_row");
            UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, clipDesc, minWidth: 220f, minHeight: 30f);

            builder.AddFlexibleSpace();

            builder.AddButton(I18n.Get("导出剪贴板", "Export JSON"), OnExportClipboardClicked, "btn_self_export_json");
            UiLayoutAndFontHelper.StyleActionButton(builder.Last, 112f, 36f, lrMargin: 8f, tbMargin: 2f, fontScale: 0.84f);
            UiLayoutAndFontHelper.SyncCompactActionButton(builder.Last);

            builder.AddButton(I18n.Get("导入剪贴板", "Import JSON"), OnImportClipboardClicked, "btn_self_import_json");
            UiLayoutAndFontHelper.StyleActionButton(builder.Last, 112f, 36f, lrMargin: 8f, tbMargin: 2f, fontScale: 0.84f);
            UiLayoutAndFontHelper.SyncCompactActionButton(builder.Last);

            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 10f);
            builder.End();

            builder.End(); // End SelfProfileSectionContainer
        }

        internal static string BuildSelfModSlotRowLabelText(int slotIndex)
        {
            string slotTitle = HotkeyProfileManager.GetSlotDisplayName(slotIndex);
            string detail = HotkeyProfileManager.GetSlotDetailSummary(slotIndex);
            bool isSelected = HotkeyProfileManager.ActiveSlotIndex == slotIndex;
            string prefix = isSelected ? "<color=#a8d8b9>▶ </color>" : "    ";
            return $"{prefix}<b>{slotTitle}</b>   {detail}";
        }

        internal static void RefreshProfileSlotButtonAppearance()
        {
            if (UiSessionState.ProfileSlotCycleBtn != null)
            {
                UiLayoutAndFontHelper.SetButtonTextFast(UiSessionState.ProfileSlotCycleBtn, HotkeyProfileManager.GetBottomCycleButtonLabel());
                UiLayoutAndFontHelper.ApplyButtonTextTheme(UiSessionState.ProfileSlotCycleBtn, UiLayoutAndFontHelper.GetDefaultButtonTextColor(), useDefaultTheme: true);
            }

            for (int i = 0; i < HotkeyProfileManager.SlotCount; i++)
            {
                var lbl = UiSessionState.SelfProfileSlotDetailLabels[i];
                if (lbl != null)
                {
                    string txt = BuildSelfModSlotRowLabelText(i);
                    UiLayoutAndFontHelper.SetTextIfChanged(lbl, txt);
                    I18n.ApplyFontIfRuntime(lbl.text, txt);
                }
            }
        }

        internal static void OnCycleProfileSlotClicked()
        {
            UiSessionState.CancelCapture();
            HotkeyProfileManager.CycleNextSlot();
            RefreshProfileSlotButtonAppearance();
        }

        internal static void OnSaveProfileSlotClicked(int slotIndex)
        {
            UiSessionState.CancelCapture();
            if (UiSessionState.CurrentManager != null)
            {
                if (UiSessionState.ApplyAllSettingsCallback != null)
                {
                    UiSessionState.ApplyAllSettingsCallback.Invoke(UiSessionState.CurrentManager);
                }
                else
                {
                    UnifiedOptionWindow.ApplyAllSettings(UiSessionState.CurrentManager);
                }
            }

            HotkeyProfileManager.SaveToSlot(slotIndex, UiSessionState.AllHotkeys, UiSessionState.ModGroups, out _, out _);
            RefreshProfileSlotButtonAppearance();
        }

        internal static void OnLoadProfileSlotClicked(int slotIndex)
        {
            UiSessionState.CancelCapture();
            if (!HotkeyProfileManager.TryLoadSlotSnapshot(slotIndex, out var snapshot, out _) || snapshot == null)
            {
                return;
            }

            ApplyProfileSnapshotToCurrentSession(snapshot);
            RefreshProfileSlotButtonAppearance();
        }

        internal static void OnClearProfileSlotClicked(int slotIndex)
        {
            UiSessionState.CancelCapture();
            HotkeyProfileManager.ClearSlot(slotIndex);
            RefreshProfileSlotButtonAppearance();
        }

        internal static void OnExportClipboardClicked()
        {
            UiSessionState.CancelCapture();
            if (UiSessionState.CurrentManager != null)
            {
                if (UiSessionState.ApplyAllSettingsCallback != null)
                {
                    UiSessionState.ApplyAllSettingsCallback.Invoke(UiSessionState.CurrentManager);
                }
                else
                {
                    UnifiedOptionWindow.ApplyAllSettings(UiSessionState.CurrentManager);
                }
            }

            string profileName = HotkeyProfileManager.GetSlotDisplayName(HotkeyProfileManager.ActiveSlotIndex);
            string json = HotkeyProfileManager.BuildSnapshotJson(profileName, UiSessionState.AllHotkeys, UiSessionState.ModGroups, out _, out _, out _);
            GUIUtility.systemCopyBuffer = json;
        }

        internal static void OnImportClipboardClicked()
        {
            UiSessionState.CancelCapture();
            string rawClipboard = GUIUtility.systemCopyBuffer ?? string.Empty;
            if (!HotkeyProfileManager.TryParseSnapshotJson(rawClipboard, out var snapshot, out _) || snapshot == null)
            {
                return;
            }

            ApplyProfileSnapshotToCurrentSession(snapshot);
            RefreshProfileSlotButtonAppearance();
        }

        internal static void ApplyProfileSnapshotToCurrentSession(ProfileSnapshot snapshot)
        {
            if (UiSessionState.CurrentManager == null) return;

            bool wasChinese = I18n.IsChinese;

            // 1. 恢复强制纳管表与忽略表（若强制纳管列表变化，先原位重扫以便生成对应的热键条目）
            ComboKeyStore.ReplaceForcedAndIgnoredSets(snapshot.Forced, snapshot.Ignored, out bool forcedChanged);
            if (forcedChanged)
            {
                ModDiagnosticView.PerformFullRescanAndRefreshInPlace(forceInvalidateCache: false, targetModId: null, statusMessage: null);
            }

            // 2. 恢复自定义/探测热键映射表
            if (snapshot.CustomHotkeys.Count > 0)
            {
                var customMap = new Dictionary<string, KeyCombo>(snapshot.CustomHotkeys.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in snapshot.CustomHotkeys)
                {
                    if (ModOptionScanner.TryParseComboString(kvp.Value, out var cMods, out var cKey))
                    {
                        customMap[kvp.Key] = new KeyCombo(cMods, cKey);
                    }
                }
                ComboKeyStore.ApplyCustomHotkeysMap(customMap);
            }

            // 3. 逐项恢复原版与所有模组快捷键
            int appliedHkCount = 0;
            for (int i = 0; i < UiSessionState.AllHotkeys.Count; i++)
            {
                var hk = UiSessionState.AllHotkeys[i];
                if (hk == null || string.IsNullOrEmpty(hk.Id)) continue;
                if (!snapshot.Hotkeys.TryGetValue(hk.Id, out string? comboStr) || string.IsNullOrEmpty(comboStr)) continue;
                if (!ModOptionScanner.TryParseComboString(comboStr, out var targetMods, out var targetKey)) continue;

                KeyModifierMask finalMods = (hk.SupportsModifierCombos && targetKey != KeyCode.None)
                    ? targetMods
                    : KeyModifierMask.None;
                hk.PendingModifiers = finalMods;
                hk.PendingKey = targetKey;
                hk.HasPendingChange = true;
                appliedHkCount++;
            }

            // 4. 逐项恢复所有模组常规配置 (开关 / 滑块 / 下拉框 / 输入框)
            int appliedGenCount = 0;
            for (int g = 0; g < UiSessionState.ModGroups.Count; g++)
            {
                var group = UiSessionState.ModGroups[g];
                if (group == null) continue;
                for (int i = 0; i < group.GeneralSettings.Count; i++)
                {
                    var gen = group.GeneralSettings[i];
                    if (gen == null || string.IsNullOrEmpty(gen.Id)) continue;
                    if (!snapshot.General.TryGetValue(gen.Id, out string? encoded) || string.IsNullOrEmpty(encoded)) continue;

                    if (gen.Kind == OptionKind.Toggle && encoded.StartsWith("b:", StringComparison.OrdinalIgnoreCase))
                    {
                        string valPart = encoded.Substring(2).Trim();
                        bool bVal = valPart == "1" || string.Equals(valPart, "true", StringComparison.OrdinalIgnoreCase);
                        gen.PendingBool = bVal;
                        appliedGenCount++;
                    }
                    else if (gen.Kind == OptionKind.Slider && encoded.StartsWith("f:", StringComparison.OrdinalIgnoreCase))
                    {
                        string valPart = encoded.Substring(2).Trim();
                        if (float.TryParse(valPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fVal))
                        {
                            float clamped = Mathf.Clamp(fVal, gen.Min, gen.Max > gen.Min ? gen.Max : fVal);
                            gen.PendingFloat = gen.WholeNumbers ? Mathf.Round(clamped) : clamped;
                            appliedGenCount++;
                        }
                    }
                    else if (gen.Kind == OptionKind.Dropdown && encoded.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
                    {
                        string valPart = encoded.Substring(2).Trim();
                        if (int.TryParse(valPart, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int iVal) && iVal >= 0)
                        {
                            gen.PendingIndex = iVal;
                            appliedGenCount++;
                        }
                    }
                    else if (gen.Kind == OptionKind.Input && !gen.IsReadOnlyInfo && encoded.StartsWith("s:", StringComparison.OrdinalIgnoreCase))
                    {
                        gen.PendingString = encoded.Substring(2);
                        appliedGenCount++;
                    }
                }
            }

            // 5. 提交并立即生效
            if (UiSessionState.ApplyAllSettingsCallback != null)
            {
                UiSessionState.ApplyAllSettingsCallback.Invoke(UiSessionState.CurrentManager);
            }
            else
            {
                UnifiedOptionWindow.ApplyAllSettings(UiSessionState.CurrentManager);
            }

            if (wasChinese != I18n.IsChinese)
            {
                return;
            }

            // 6. 同步刷新当前已构建的所有卡片控件显示
            using (ComboKeyStore.EnterBypass())
            {
                for (int g = 0; g < UiSessionState.ModGroups.Count; g++)
                {
                    var group = UiSessionState.ModGroups[g];
                    if (group != null && UiSessionState.TryGetCardParts(group, out var parts) && parts.IsContentBuilt)
                    {
                        UiSessionState.RefreshCardUI(group);
                    }
                }
                UiLayoutAndFontHelper.StyleVanillaKeyButtons();
                UiSessionState.RefreshConflicts();
                ConflictAggregationView.RefreshConflicts();
                RefreshProfileSlotButtonAppearance();
                TopSearchAndActionBar.RefreshExpandAllButtonState();
            }
        }
    }
}
