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
            UiSessionState.RemoveWindowElements(k =>
                k == "profile_hdr_self" ||
                k == "label_profile_action_status" ||
                k == "label_profile_clipboard_row" ||
                k == "btn_self_export_json" ||
                k == "btn_self_import_json" ||
                k.StartsWith("label_profile_slot_") ||
                k.StartsWith("btn_slot_"));

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
                "<b>剪贴板 JSON 备份</b>  <color=#99a3b0>导出或恢复全部快捷键、模组设置与强转表</color>",
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

            // 实时操作反馈状态行
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            UiLayoutAndFontHelper.ConfigureStretchRowObject(builder.CurrentContainer.gameObject, 28f);
            UiLayoutAndFontHelper.AddFixedHorizontalSpace(builder, 18f);
            builder.AddLabel(string.Empty, "label_profile_action_status");
            UiSessionState.ProfileActionStatusLabel = builder.Last;
            UiLayoutAndFontHelper.ConfigureSingleLineRowLabel(builder.Last, string.Empty, minWidth: 260f, minHeight: 24f);
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

        internal static void SetProfileFeedbackStatus(string statusRichText)
        {
            if (UiSessionState.ProfileActionStatusLabel != null)
            {
                UiLayoutAndFontHelper.SetTextIfChanged(UiSessionState.ProfileActionStatusLabel, statusRichText);
                I18n.ApplyFontIfRuntime(UiSessionState.ProfileActionStatusLabel.text, statusRichText);
            }
        }

        internal static void OnSaveProfileSlotClicked(int slotIndex)
        {
            UiSessionState.CancelCapture();
            if (UiSessionState.CurrentManager != null)
            {
                try
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
                catch (Exception ex)
                {
                    ModLogger.Warn($"Failed to apply settings before profile save: {ex.Message}");
                }
            }

            try
            {
                HotkeyProfileManager.SaveToSlot(slotIndex, UiSessionState.AllHotkeys, UiSessionState.ModGroups, out int hkCount, out int genCount);
                RefreshProfileSlotButtonAppearance();

                string slotName = HotkeyProfileManager.GetSlotDisplayName(slotIndex);
                string timeStr = DateTime.Now.ToString("HH:mm:ss");
                string statusMsg = I18n.Format(
                    "<color=#86efac>✓ 已将当前全部键位与设置保存至【{0}】 ({1})</color>",
                    "<color=#86efac>✓ Saved all keybinds and settings to [{0}] ({1})</color>",
                    slotName, timeStr);
                SetProfileFeedbackStatus(statusMsg);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to save profile: {ex.Message}");
                string failMsg = I18n.Format(
                    "<color=#ff8a8a>✗ 保存预设异常：{0}</color>",
                    "<color=#ff8a8a>✗ Profile save error: {0}</color>",
                    ex.Message);
                SetProfileFeedbackStatus(failMsg);
            }
        }

        internal static void OnLoadProfileSlotClicked(int slotIndex)
        {
            UiSessionState.CancelCapture();
            string slotName = HotkeyProfileManager.GetSlotDisplayName(slotIndex);
            if (!HotkeyProfileManager.TryLoadSlotSnapshot(slotIndex, out var snapshot, out _) || snapshot == null)
            {
                string warnMsg = I18n.Format(
                    "<color=#ffd27d>⚠ 【{0}】暂无存档，请先点击「保存」</color>",
                    "<color=#ffd27d>⚠ [{0}] is empty, please click 'Save' first</color>",
                    slotName);
                SetProfileFeedbackStatus(warnMsg);
                return;
            }

            try
            {
                ApplyProfileSnapshotToCurrentSession(snapshot, out int hkCount, out int genCount);
                RefreshProfileSlotButtonAppearance();
                UiSessionState.HasUserInteractedInSession = true;

                string successMsg = I18n.Format(
                    "<color=#86efac>✓ 已读取并应用【{0}】（恢复 {1} 项按键 / {2} 项设置）</color>",
                    "<color=#86efac>✓ Loaded [{0}] ({1} keys / {2} settings restored)</color>",
                    slotName, hkCount, genCount);
                SetProfileFeedbackStatus(successMsg);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to load profile snapshot: {ex.Message}");
                string failMsg = I18n.Format(
                    "<color=#ff8a8a>✗ 读取应用异常：{0}</color>",
                    "<color=#ff8a8a>✗ Load application error: {0}</color>",
                    ex.Message);
                SetProfileFeedbackStatus(failMsg);
            }
        }

        internal static void OnClearProfileSlotClicked(int slotIndex)
        {
            UiSessionState.CancelCapture();
            string slotName = HotkeyProfileManager.GetSlotDisplayName(slotIndex);
            if (!HotkeyProfileManager.HasSlotData(slotIndex))
            {
                string alreadyEmptyMsg = I18n.Format(
                    "<color=#99a3b0>【{0}】已是空槽位</color>",
                    "<color=#99a3b0>[{0}] is already empty</color>",
                    slotName);
                SetProfileFeedbackStatus(alreadyEmptyMsg);
                return;
            }

            HotkeyProfileManager.ClearSlot(slotIndex);
            RefreshProfileSlotButtonAppearance();

            string clearedMsg = I18n.Format(
                "<color=#ffd27d>✓ 已清空【{0}】存档数据</color>",
                "<color=#ffd27d>✓ Cleared [{0}] profile data</color>",
                slotName);
            SetProfileFeedbackStatus(clearedMsg);
        }

        internal static void OnExportClipboardClicked()
        {
            UiSessionState.CancelCapture();
            if (UiSessionState.CurrentManager != null)
            {
                try
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
                catch (Exception ex)
                {
                    ModLogger.Warn($"Failed to apply settings before export: {ex.Message}");
                }
            }

            try
            {
                string profileName = HotkeyProfileManager.GetSlotDisplayName(HotkeyProfileManager.ActiveSlotIndex);
                string json = HotkeyProfileManager.BuildSnapshotJson(profileName, UiSessionState.AllHotkeys, UiSessionState.ModGroups, out _, out _, out _);
                GUIUtility.systemCopyBuffer = json;

                string exportMsg = I18n.Format(
                    "<color=#86efac>✓ 已导出配置 JSON 至剪贴板（共 {0} 字符，可直接粘贴备份或分享）</color>",
                    "<color=#86efac>✓ Exported profile JSON to clipboard ({0} chars)</color>",
                    json.Length);
                SetProfileFeedbackStatus(exportMsg);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to export profile to clipboard: {ex.Message}");
                string failMsg = I18n.Format(
                    "<color=#ff8a8a>✗ 导出剪贴板异常：{0}</color>",
                    "<color=#ff8a8a>✗ Export error: {0}</color>",
                    ex.Message);
                SetProfileFeedbackStatus(failMsg);
            }
        }

        internal static void OnImportClipboardClicked()
        {
            UiSessionState.CancelCapture();
            string rawClipboard = string.Empty;
            try
            {
                rawClipboard = GUIUtility.systemCopyBuffer ?? string.Empty;
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to read systemCopyBuffer: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(rawClipboard) ||
                !HotkeyProfileManager.TryParseSnapshotJson(rawClipboard, out var snapshot, out _) ||
                snapshot == null)
            {
                string errMsg = I18n.Get(
                    "<color=#ff8a8a>✗ 导入失败：剪贴板内容不是有效的按键方案 JSON</color>",
                    "<color=#ff8a8a>✗ Import failed: Clipboard does not contain valid profile JSON</color>");
                SetProfileFeedbackStatus(errMsg);
                return;
            }

            try
            {
                ApplyProfileSnapshotToCurrentSession(snapshot, out int hkCount, out int genCount);
                RefreshProfileSlotButtonAppearance();
                UiSessionState.HasUserInteractedInSession = true;

                string successMsg = I18n.Format(
                    "<color=#86efac>✓ 已从剪贴板成功导入并立即生效（恢复 {0} 项按键 / {1} 项设置）！</color>",
                    "<color=#86efac>✓ Successfully imported from clipboard ({0} keys / {1} settings restored)!</color>",
                    hkCount, genCount);
                SetProfileFeedbackStatus(successMsg);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to apply imported snapshot: {ex.Message}");
                string failMsg = I18n.Format(
                    "<color=#ff8a8a>✗ 导入应用异常：{0}</color>",
                    "<color=#ff8a8a>✗ Import application error: {0}</color>",
                    ex.Message);
                SetProfileFeedbackStatus(failMsg);
            }
        }

        internal static void ApplyProfileSnapshotToCurrentSession(ProfileSnapshot snapshot)
        {
            ApplyProfileSnapshotToCurrentSession(snapshot, out _, out _);
        }

        internal static void ApplyProfileSnapshotToCurrentSession(ProfileSnapshot snapshot, out int appliedHkCount, out int appliedGenCount)
        {
            appliedHkCount = 0;
            appliedGenCount = 0;
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
                if (targetKey != KeyCode.None)
                {
                    ComboKeyStore.SetExplicitlyUnbound(hk.Id, false);
                }
                else
                {
                    ComboKeyStore.SetExplicitlyUnbound(hk.Id, true);
                }
                appliedHkCount++;
            }

            // 4. 逐项恢复所有模组常规配置 (开关 / 滑块 / 下拉框 / 输入框)
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

            // 4C. 恢复原版常规设置（Toggle / Slider / Dropdown）
            foreach (var sec in GameOptionRegistry.Sections)
            {
                if (sec == null || !GameOptionRegistry.IsVanillaSection(sec.id) || sec.entries == null) continue;
                for (int i = 0; i < sec.entries.Count; i++)
                {
                    var entry = sec.entries[i];
                    if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                    if (entry.id.IndexOf("resolution", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        entry.id.IndexOf("fullscreen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        entry.id.IndexOf("monitor", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    if (!snapshot.General.TryGetValue(entry.id, out string? encoded) || string.IsNullOrEmpty(encoded)) continue;

                    var activeWin = UiSessionState.CurrentWindow ?? UiSessionState.VanillaStandaloneWindow;
                    var el = activeWin?.Get(entry.id);

                    if (entry.kind == GameOptionRegistry.Kind.Toggle && entry.setBool != null && encoded.StartsWith("b:", StringComparison.OrdinalIgnoreCase))
                    {
                        string valPart = encoded.Substring(2).Trim();
                        bool bVal = valPart == "1" || string.Equals(valPart, "true", StringComparison.OrdinalIgnoreCase);
                        entry.setBool(bVal);
                        el?.SetBool(bVal);
                        appliedGenCount++;
                    }
                    else if (entry.kind == GameOptionRegistry.Kind.Slider && entry.setFloat != null && encoded.StartsWith("f:", StringComparison.OrdinalIgnoreCase))
                    {
                        string valPart = encoded.Substring(2).Trim();
                        if (float.TryParse(valPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fVal))
                        {
                            entry.setFloat(fVal);
                            el?.SetFloat(fVal);
                            appliedGenCount++;
                        }
                    }
                    else if (entry.kind == GameOptionRegistry.Kind.Dropdown && entry.setIndex != null && encoded.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
                    {
                        string valPart = encoded.Substring(2).Trim();
                        if (int.TryParse(valPart, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int iVal) && iVal >= 0)
                        {
                            entry.setIndex(iVal);
                            el?.SetIndex(iVal);
                            appliedGenCount++;
                        }
                    }
                }
            }

            // 4B. 恢复卡片排序模式与自定义排列顺序（在 ApplyAllSettings 前执行以同步刷新界面与基准快照）
            bool hasSnapOrder = snapshot.CardOrder != null && snapshot.CardOrder.Count > 0;
            if (hasSnapOrder)
            {
                ComboKeyStore.SetSavedCustomCardOrder(snapshot.CardOrder!);
            }
            if (!string.IsNullOrEmpty(snapshot.CardSortMode))
            {
                var snapSort = ModFeatureConfig.ParseCardSortMode(snapshot.CardSortMode);
                ModFeatureConfig.SetCardSortMode(snapSort);
                UiSessionState.CurrentCardSortMode = snapSort;
                UiSessionState.IsSortModeExplicitlyChosenByUser = true;
            }
            if (UiSessionState.CurrentCardSortMode == ModCardSortMode.CustomOrder && hasSnapOrder)
            {
                UiSessionState.RestoreCardVisualOrder(snapshot.CardOrder!);
            }
            else if (UiSessionState.ModGroups != null)
            {
                ComboKeyStore.SortModOptionGroups(UiSessionState.ModGroups, UiSessionState.CurrentCardSortMode);
                for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
                {
                    var grp = UiSessionState.ModGroups[i];
                    if (grp != null && UiSessionState.TryGetCardParts(grp, out var cParts) && cParts.CardRootObject != null)
                    {
                        cParts.CardRootObject.transform.SetSiblingIndex(i);
                    }
                }
            }
            if (UiSessionState.ModCardsContainer != null && UiSessionState.ModCardsContainer.transform is RectTransform cardsRt)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(cardsRt);
            }
            UnifiedOptionWindow.SyncCardSortDropdownAppearance();

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
                if (UiSessionState.ModGroups != null)
                {
                    for (int g = 0; g < UiSessionState.ModGroups.Count; g++)
                    {
                        var group = UiSessionState.ModGroups[g];
                        if (group != null && UiSessionState.TryGetCardParts(group, out var parts) && parts.IsContentBuilt)
                        {
                            UiSessionState.RefreshCardUI(group);
                        }
                    }
                }
                UiLayoutAndFontHelper.StyleVanillaKeyButtons();
                UnifiedOptionWindow.SyncVanillaKeyButtonColors();

                if (UiSessionState.VanillaCards != null && UiSessionState.CurrentWindow != null && UiSessionState.CurrentWindow.IsAlive)
                {
                    for (int c = 0; c < UiSessionState.VanillaCards.Count; c++)
                    {
                        var card = UiSessionState.VanillaCards[c];
                        if (card == null || card.Entries == null) continue;
                        for (int i = 0; i < card.Entries.Count; i++)
                        {
                            var item = card.Entries[i];
                            var entry = item?.Entry;
                            if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                            var el = UiSessionState.CurrentWindow.Get(entry.id);
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

                UiSessionState.RefreshConflicts();
                ConflictAggregationView.RefreshConflicts();
                RefreshProfileSlotButtonAppearance();
                TopSearchAndActionBar.RefreshExpandAllButtonState();
                TopSearchAndActionBar.RefreshToolbarVisibilityInPlace();
            }
        }
    }
}
