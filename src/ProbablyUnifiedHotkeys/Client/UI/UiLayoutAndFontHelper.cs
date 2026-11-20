#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// UI 表现层排版布局、单次字号测量拟合缓存（ADR-009 升级版）与滚动定位辅助工具箱。
    /// </summary>
    internal static class UiLayoutAndFontHelper
    {
        internal const float KeybindButtonWidth = 160f; // 与原版 GameOptionUIManager.KEYBIND_BUTTON_WIDTH 一致

        // =====================================================================
        // 1. 字体拟合度量规格与高频主题色彩缓存
        // =====================================================================

        internal struct ButtonFitSpec
        {
            public float AvailWidth;
            public float AvailHeight;
            public float MaxFontSize;
            public float MinFontSize;
        }

        internal static readonly Dictionary<int, ButtonTextColor?> ButtonTextColorCache =
            new Dictionary<int, ButtonTextColor?>(128);

        internal static readonly Dictionary<int, Color> AppliedButtonNormalColorCache =
            new Dictionary<int, Color>(128);

        internal static readonly Dictionary<int, ButtonFitSpec> ButtonFitSpecCache =
            new Dictionary<int, ButtonFitSpec>(128);

        // 跨打开会话可复用的单次字体缩放尺寸缓存 (fontInstanceId, availWidth*10, availHeight*10, maxFontSize*10, text) -> fittedFontSize
        // 严格保持容量上限 <= 2048，严禁开启 TMP_Text.enableAutoSizing
        private static readonly Dictionary<(int fontId, int widthTenths, int heightTenths, int maxSizeTenths, string text), float> FittedFontSizeCache
            = new Dictionary<(int, int, int, int, string), float>(256);

        internal static TMP_FontAsset? ActiveButtonFont = null;
        internal static Color DefaultButtonTextColor = Color.white;
        internal static Color DefaultHighlightColor = new Color(0.88f, 0.82f, 0.61f, 1f);
        internal static Color DefaultPressedColor = new Color(0.57f, 0.55f, 1f, 1f);
        internal static float BaseButtonFontSize = 0f;
        internal static Vector4 BaseButtonMargin = Vector4.zero;
        internal static bool HasBaseButtonMargin = false;
        internal static bool HasDefaultButtonTextColor = false;
        internal static bool HasDefaultButtonTheme = false;
        internal static string? CapturedThemeLayer = null;

        internal static void ClearButtonCaches()
        {
            ButtonTextColorCache.Clear();
            AppliedButtonNormalColorCache.Clear();
            ButtonFitSpecCache.Clear();
        }

        // 缓存 ButtonTextColor 私有色彩字段（OnPointerEnter/Exit/Up/Disable 会使用 normal/highlight/pressed 覆写 text.color）
        private static readonly FieldInfo? BtcNormalField = AccessTools.Field(typeof(ButtonTextColor), "normal");
        private static readonly FieldInfo? BtcHighlightField = AccessTools.Field(typeof(ButtonTextColor), "highlight");
        private static readonly FieldInfo? BtcPressedField = AccessTools.Field(typeof(ButtonTextColor), "pressed");
        private static readonly FieldInfo? BtcIsHoveredField = AccessTools.Field(typeof(ButtonTextColor), "isHovered");

        internal static ButtonTextColor? FindButtonTextColorComponent(CustomUIElement? btn)
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

        internal static void CaptureDefaultButtonTheme(CustomUIElement? btn, bool forceRefresh = false, string? layer = null)
        {
            if (btn == null || (!forceRefresh && HasDefaultButtonTheme && BaseButtonFontSize > 0f && HasBaseButtonMargin))
            {
                return;
            }
            if (layer != null)
            {
                CapturedThemeLayer = layer;
            }
            if (btn.text != null)
            {
                if (forceRefresh || BaseButtonFontSize <= 0f)
                {
                    BaseButtonFontSize = btn.text.fontSize;
                }
                if (forceRefresh || !HasBaseButtonMargin)
                {
                    BaseButtonMargin = btn.text.margin;
                    HasBaseButtonMargin = true;
                }
                DefaultButtonTextColor = btn.text.color;
                HasDefaultButtonTextColor = true;
            }

            var comp = FindButtonTextColorComponent(btn);
            if (comp != null)
            {
                if (BtcNormalField != null)
                {
                    DefaultButtonTextColor = (Color)BtcNormalField.GetValue(comp);
                    HasDefaultButtonTextColor = true;
                }
                if (BtcHighlightField != null)
                {
                    DefaultHighlightColor = (Color)BtcHighlightField.GetValue(comp);
                }
                if (BtcPressedField != null)
                {
                    DefaultPressedColor = (Color)BtcPressedField.GetValue(comp);
                }
                HasDefaultButtonTheme = true;
            }
        }

        internal static Color GetDefaultButtonTextColor()
        {
            return HasDefaultButtonTextColor ? DefaultButtonTextColor : Color.white;
        }

        internal static void ApplyButtonTextTheme(CustomUIElement? btn, Color normalColor, bool useDefaultTheme = false)
        {
            if (btn == null) return;

            Color targetNormal = (useDefaultTheme && HasDefaultButtonTextColor) ? DefaultButtonTextColor : normalColor;
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

            Color targetHighlight = (useDefaultTheme && HasDefaultButtonTheme)
                ? DefaultHighlightColor
                : Color.Lerp(targetNormal, Color.white, 0.35f);
            Color targetPressed = (useDefaultTheme && HasDefaultButtonTheme)
                ? DefaultPressedColor
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

        internal static void SetTextIfChanged(CustomUIElement? el, string value)
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

        internal static void SetButtonTextFast(CustomUIElement? btn, string value)
        {
            if (btn == null) return;
            if (btn.text == null)
            {
                btn.SetText(value);
                return;
            }

            if (ActiveButtonFont != null && btn.text.font != ActiveButtonFont)
            {
                btn.text.font = ActiveButtonFont;
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

        internal static void ApplySinglePassFontSize(TextMeshProUGUI tmp, string? text, ButtonFitSpec spec)
        {
            if (tmp.enableAutoSizing)
            {
                tmp.enableAutoSizing = false;
            }
            if (ActiveButtonFont != null && tmp.font != ActiveButtonFont)
            {
                tmp.font = ActiveButtonFont;
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

        internal static void SetButtonLayoutPadding(CustomUIElement? btn, int lrPad, int tbPad, bool forceExpandChild)
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

        internal static void StyleButtonTypography(CustomUIElement? btn, float lrMargin, float tbMargin, float fontScale, float buttonWidth = -1f, float buttonHeight = -1f, bool allowAutoShrink = false)
        {
            if (btn == null || btn.text == null) return;
            if (ActiveButtonFont != null && btn.text.font != ActiveButtonFont)
            {
                btn.text.font = ActiveButtonFont;
            }

            float baseSize = BaseButtonFontSize > 0f ? BaseButtonFontSize : btn.text.fontSize;
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

        internal static void StyleNativeWidthButton(CustomUIElement? btn, float width)
        {
            if (btn == null) return;
            btn.SetFixedWidth(width);
            if (btn.text != null)
            {
                if (ActiveButtonFont != null && btn.text.font != ActiveButtonFont)
                {
                    btn.text.font = ActiveButtonFont;
                }

                float targetSize = BaseButtonFontSize > 0f ? BaseButtonFontSize : btn.text.fontSize;
                Vector4 baseMargin = HasBaseButtonMargin ? BaseButtonMargin : btn.text.margin;
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

        internal static void StyleHotkeyButton(CustomUIElement? btn)
        {
            StyleNativeWidthButton(btn, KeybindButtonWidth);
        }

        internal static void StyleActionButton(CustomUIElement? btn, float width, float height, float lrMargin = 10f, float tbMargin = 2f, float fontScale = 0.88f)
        {
            if (btn == null) return;
            btn.SetFixedWidth(width);
            btn.SetPreferredSize(width, height);
            btn.SetFlexible(0f, 0f);
            SetButtonLayoutPadding(btn, Mathf.RoundToInt(lrMargin), Mathf.RoundToInt(tbMargin), forceExpandChild: true);
            StyleButtonTypography(btn, lrMargin, tbMargin, fontScale, width, height, allowAutoShrink: true);
        }

        internal static void StyleFoldButton(CustomUIElement? btn, float size)
        {
            if (btn == null) return;
            btn.SetFixedWidth(size);
            btn.SetPreferredSize(size, size);
            btn.SetFlexible(0f, 0f);
            // 清空 Meta_Button 默认 (16,16,4,4) 内边距，避免 36px 方形折叠按钮内部 TextRectWidth 被压扁
            SetButtonLayoutPadding(btn, 0, 0, forceExpandChild: true);
            if (btn.text != null)
            {
                if (ActiveButtonFont != null && btn.text.font != ActiveButtonFont)
                {
                    btn.text.font = ActiveButtonFont;
                }
                float baseSize = BaseButtonFontSize > 0f ? BaseButtonFontSize : btn.text.fontSize;
                btn.text.enableAutoSizing = false;
                btn.text.enableWordWrapping = false;
                btn.text.overflowMode = TextOverflowModes.Overflow;
                btn.text.fontSize = Mathf.Clamp(Mathf.Round(baseSize * 0.58f), 13f, 16f);
                btn.text.alignment = TextAlignmentOptions.Center;
                btn.text.margin = Vector4.zero;
            }
        }

        internal static void SyncCompactActionButton(CustomUIElement? btn)
        {
            if (btn?.text == null) return;
            if (ActiveButtonFont != null && btn.text.font != ActiveButtonFont)
            {
                btn.text.font = ActiveButtonFont;
            }
            int cacheKey = btn.root != null ? btn.root.GetInstanceID() : btn.GetHashCode();
            if (ButtonFitSpecCache.TryGetValue(cacheKey, out ButtonFitSpec spec))
            {
                ApplySinglePassFontSize(btn.text, btn.text.text, spec);
            }
        }

        internal static void SyncActionButtonsAfterShow(Action? onRefreshProfileButtons = null, Action? onUpdateConflictToggle = null, Action? onUpdateDiagnosticToggle = null)
        {
            if (I18n.IsChinese && FontLoader.Instance != null && FontLoader.Instance.simplifiedChineseFont != null)
            {
                ActiveButtonFont = FontLoader.Instance.simplifiedChineseFont;
            }
            else if (UiSessionState.ExpandToggleBtn?.text?.font != null)
            {
                ActiveButtonFont = UiSessionState.ExpandToggleBtn.text.font;
            }

            SyncCompactActionButton(UiSessionState.ConflictToggleBtn);
            SyncCompactActionButton(UiSessionState.ExpandToggleBtn);
            SyncCompactActionButton(UiSessionState.DiagnosticToggleBtn);
            SyncCompactActionButton(UiSessionState.ProfileSlotCycleBtn);
            SyncCompactActionButton(UiSessionState.ProfileSaveBtn);
            SyncCompactActionButton(UiSessionState.ProfileLoadBtn);
            SyncCompactActionButton(UiSessionState.ProfileExportBtn);
            SyncCompactActionButton(UiSessionState.ProfileImportBtn);
            onRefreshProfileButtons?.Invoke();

            for (int i = 0; i < UiSessionState.VanillaCards.Count; i++)
            {
                var vc = UiSessionState.VanillaCards[i];
                StyleFoldButton(vc.FoldArrowElement, 36f);
                if (vc.IsExpanded) SyncCompactActionButton(vc.ResetButtonElement);
            }

            for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
            {
                var mg = UiSessionState.ModGroups[i];
                if (UiSessionState.TryGetCardParts(mg, out var parts))
                {
                    StyleFoldButton(parts.FoldArrowElement, 36f);
                    if (mg.IsExpanded) SyncCompactActionButton(parts.ResetButtonElement);
                }
            }

            onUpdateConflictToggle?.Invoke();
            onUpdateDiagnosticToggle?.Invoke();
        }

        internal static void StyleVanillaKeyButtons()
        {
            if (UiSessionState.CurrentWindow == null) return;
            foreach (var hk in UiSessionState.AllHotkeys)
            {
                if (hk.ModId != "vanilla" || string.IsNullOrEmpty(hk.RawOptionId)) continue;
                var el = UiSessionState.CurrentWindow.Get(hk.RawOptionId);
                StyleHotkeyButton(el);
            }
        }

        internal static void ApplySingleButtonColor(CustomUIElement btn, KeyCombo combo, bool isReadOnlyProbed = false)
        {
            if (combo.IsEmpty)
            {
                ApplyButtonTextTheme(btn, ConflictDetector.EmptyKeyColor);
            }
            else if (UiSessionState.Conflicts.ContainsKey(combo))
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

        internal static string BuildHotkeyLabelText(ModHotkeyEntry hotkey)
        {
            string labelText = hotkey.DisplayName;
            string conflictTip = ConflictDetector.GetConflictTooltip(hotkey, UiSessionState.Conflicts);

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
                if (hotkey.ModId == "vanilla" && !string.IsNullOrEmpty(hotkey.Tooltip))
                {
                    string infoLink = "modkey:" + hotkey.Id;
                    TMPIconHover.CustomLinks[infoLink] = () => hotkey.Tooltip;
                    labelText += $" <link=\"{infoLink}\"><sprite name=\"info\" tint=1></link>";
                }
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

        internal static void UpdateHotkeyButtonVisual(ModHotkeyEntry hotkey)
        {
            var ui = UiSessionState.GetOrCreateHotkeyUi(hotkey);

            if (ui.LabelElement == null && hotkey.ModId == "vanilla" && !string.IsNullOrEmpty(hotkey.RawOptionId) && UiSessionState.CurrentWindow != null)
            {
                ui.LabelElement = UiSessionState.CurrentWindow.Get("label_" + hotkey.RawOptionId);
            }
            if (ui.LabelElement != null)
            {
                SetTextIfChanged(ui.LabelElement, BuildHotkeyLabelText(hotkey));
            }

            if (ui.ButtonElement == null && hotkey.ModId == "vanilla" && !string.IsNullOrEmpty(hotkey.RawOptionId) && UiSessionState.CurrentWindow != null)
            {
                ui.ButtonElement = UiSessionState.CurrentWindow.Get(hotkey.RawOptionId);
            }

            string formatted = FormatHotkeyComboText(hotkey);

            if (ui.ButtonElement != null && UiSessionState.CapturingEntry != hotkey)
            {
                SetButtonTextFast(ui.ButtonElement, formatted);
                ApplySingleButtonColor(ui.ButtonElement, hotkey.EffectiveCombo, hotkey.IsReadOnlyProbed);
            }

            if (ui.ConflictButtonElement != null && UiSessionState.CapturingEntry != hotkey)
            {
                SetButtonTextFast(ui.ConflictButtonElement, formatted);
                ApplySingleButtonColor(ui.ConflictButtonElement, hotkey.EffectiveCombo, hotkey.IsReadOnlyProbed);
            }
        }

        internal static string FormatHotkeyComboText(ModHotkeyEntry hotkey)
        {
            KeyCode key = hotkey.EffectiveKey;
            if (key == KeyCode.None)
            {
                return I18n.Get("无", "None");
            }
            return ComboKeyStore.FormatCombo(hotkey.EffectiveModifiers, key);
        }

        internal static string FormatSliderValue(ModGeneralEntry gen, float val)
        {
            if (gen.ValueFormatter != null) return gen.ValueFormatter(val);
            if (gen.WholeNumbers) return Mathf.RoundToInt(val).ToString();
            return val.ToString("0.0");
        }

        // =====================================================================
        // 2. UGUI 防挤压与排版辅助工具
        // =====================================================================

        internal static void ConfigureStretchColumnObject(GameObject? colObj)
        {
            if (colObj == null) return;
            var vlg = colObj.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childControlWidth = true;
                vlg.childForceExpandWidth = true;
            }
            var le = colObj.GetComponent<LayoutElement>() ?? colObj.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }

        internal static void ConfigureStretchRowObject(GameObject? rowObj, float minHeight = 32f)
        {
            if (rowObj == null) return;
            var hlg = rowObj.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childControlWidth = true;
                hlg.childForceExpandWidth = false;
            }
            var le = rowObj.GetComponent<LayoutElement>() ?? rowObj.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            if (minHeight > 0f && le.minHeight < minHeight)
            {
                le.minHeight = minHeight;
            }
        }

        internal static void ConfigureSingleLineRowLabel(CustomUIElement? el, string text, float minWidth = 180f, float minHeight = 28f)
        {
            if (el == null) return;
            if (el.root != null)
            {
                var le = el.root.GetComponent<LayoutElement>() ?? el.root.AddComponent<LayoutElement>();
                le.layoutPriority = 10;
                le.minWidth = minWidth;
                le.preferredWidth = -1f;
                le.minHeight = minHeight;
                le.flexibleWidth = 1f;
                le.flexibleHeight = 0f;
            }
            if (el.text != null)
            {
                el.text.alignment = TextAlignmentOptions.MidlineLeft;
                el.text.enableWordWrapping = false;
                el.text.overflowMode = TextOverflowModes.Ellipsis;
                I18n.ApplyFontIfRuntime(el.text, text);
            }
        }

        internal static void ConfigureFixedWidthControl(CustomUIElement? el, float width, float height = 36f)
        {
            if (el == null) return;
            el.SetFixedWidth(width);
            if (el.root != null)
            {
                var le = el.root.GetComponent<LayoutElement>() ?? el.root.AddComponent<LayoutElement>();
                // 关键：TMP_InputField 自身实现了 ILayoutElement (layoutPriority = 1)，
                // 若不提高 LayoutElement.layoutPriority，超长文本会导致 preferredWidth 暴涨至上千像素并把左侧标签挤压成单字竖排！
                le.layoutPriority = 100;
                le.minWidth = width;
                le.preferredWidth = width;
                le.flexibleWidth = 0f;
                if (height > 0f)
                {
                    le.preferredHeight = height;
                    le.flexibleHeight = 0f;
                }
            }
        }

        internal static void ConfigureSearchOrLongInputLayout(CustomUIElement? el, float fixedOrPrefWidth, bool flexible)
        {
            if (el == null) return;
            el.SetFixedWidth(fixedOrPrefWidth);
            if (el.root != null)
            {
                var le = el.root.GetComponent<LayoutElement>() ?? el.root.AddComponent<LayoutElement>();
                le.layoutPriority = 100;
                le.minWidth = flexible ? 0f : fixedOrPrefWidth;
                le.preferredWidth = fixedOrPrefWidth;
                le.flexibleWidth = flexible ? 1f : 0f;
            }
        }

        internal static void AddFixedHorizontalSpace(CustomUIBuilder builder, float pixels)
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

        internal static void ConfigureWrappedDiagnosticLabel(CustomUIElement? el, string text)
        {
            if (el == null) return;
            if (el.root != null)
            {
                var le = el.root.GetComponent<LayoutElement>() ?? el.root.AddComponent<LayoutElement>();
                le.minWidth = 0f;
                le.minHeight = 22f;
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

        internal static void AddWrappedDiagnosticLabel(CustomUIBuilder builder, float leftIndent, string text, string? id = null)
        {
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            var rowObj = builder.CurrentContainer.gameObject;
            var rowLe = rowObj.GetComponent<LayoutElement>() ?? rowObj.AddComponent<LayoutElement>();
            rowLe.flexibleWidth = 1f;
            rowLe.minHeight = 22f;

            if (leftIndent > 0f) AddFixedHorizontalSpace(builder, leftIndent);
            builder.AddLabel(text, id);
            ConfigureWrappedDiagnosticLabel(builder.Last, text);
            AddFixedHorizontalSpace(builder, 8f);
            builder.End();
        }

        // =====================================================================
        // 3. ADR-009 批量挂起布局引擎（单次顶层重建大幅削减同步开销）
        // =====================================================================

        private static int _suspendedLayoutDepth = 0;
        private static readonly List<Behaviour> SuspendedLayoutScratch = new List<Behaviour>(96);
        private static List<Behaviour>? _cachedSuspendableLayouts = null;
        internal static bool CachedSuspendableLayoutsDirty = true;

        internal static void InvalidateCachedSuspendableLayouts()
        {
            CachedSuspendableLayoutsDirty = true;
        }

        private static void EnsureCachedSuspendableLayouts()
        {
            if (!CachedSuspendableLayoutsDirty && _cachedSuspendableLayouts != null)
            {
                return;
            }

            _cachedSuspendableLayouts ??= new List<Behaviour>(96);
            _cachedSuspendableLayouts.Clear();

            CollectContainerLayouts(UiSessionState.NormalContentRoot, _cachedSuspendableLayouts);
            if (UiSessionState.ScrollRect != null)
            {
                if (UiSessionState.ScrollRect.content != null && UiSessionState.ScrollRect.content.gameObject != UiSessionState.NormalContentRoot)
                {
                    CollectContainerLayouts(UiSessionState.ScrollRect.content.gameObject, _cachedSuspendableLayouts);
                }
                if (UiSessionState.ScrollRect.viewport != null &&
                    UiSessionState.ScrollRect.viewport.gameObject != UiSessionState.NormalContentRoot &&
                    (UiSessionState.ScrollRect.content == null || UiSessionState.ScrollRect.viewport.gameObject != UiSessionState.ScrollRect.content.gameObject))
                {
                    CollectContainerLayouts(UiSessionState.ScrollRect.viewport.gameObject, _cachedSuspendableLayouts);
                }
            }

            for (int i = 0; i < UiSessionState.VanillaCards.Count; i++)
            {
                var vc = UiSessionState.VanillaCards[i];
                if (vc.ResetButtonElement != null)
                {
                    CollectContainerLayouts(vc.HeaderRowObject, _cachedSuspendableLayouts);
                }
                CollectContainerLayouts(vc.ContentContainerObject, _cachedSuspendableLayouts);
            }
            for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
            {
                var mg = UiSessionState.ModGroups[i];
                if (UiSessionState.TryGetCardParts(mg, out var parts))
                {
                    if (parts.ResetButtonElement != null)
                    {
                        CollectContainerLayouts(parts.HeaderRowObject, _cachedSuspendableLayouts);
                    }
                    CollectContainerLayouts(parts.ContentContainerObject, _cachedSuspendableLayouts);
                }
            }

            CachedSuspendableLayoutsDirty = false;
        }

        internal static void WithSuspendedRootLayout(Action action)
        {
            if (_suspendedLayoutDepth > 0)
            {
                action();
                return;
            }

            _suspendedLayoutDepth++;
            try
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

                action();
            }
            finally
            {
                _suspendedLayoutDepth--;
                if (_suspendedLayoutDepth == 0)
                {
                    for (int i = SuspendedLayoutScratch.Count - 1; i >= 0; i--)
                    {
                        var b = SuspendedLayoutScratch[i];
                        if (b != null)
                        {
                            b.enabled = true;
                        }
                    }
                    SuspendedLayoutScratch.Clear();

                    // ADR-009 升级优化：仅对顶层 ScrollRect.content 执行单次 LayoutRebuilder.ForceRebuildLayoutImmediate
                    // 削减 66% 的重复同步递归重排开销
                    if (UiSessionState.ScrollRect != null && UiSessionState.ScrollRect.content != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(UiSessionState.ScrollRect.content);
                    }
                    else if (UiSessionState.NormalContentRoot != null)
                    {
                        var normalRt = UiSessionState.NormalContentRoot.GetComponent<RectTransform>();
                        if (normalRt != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(normalRt);
                        }
                    }
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

        // =====================================================================
        // 4. 滚动定位与视图复位辅助
        // =====================================================================

        internal static void ClearPendingScroll()
        {
            UiSessionState.PendingScrollTarget = null;
            UiSessionState.PendingScrollMinFrame = -1;
            UiSessionState.PendingScrollRemainingPasses = 0;
        }

        internal static void ScrollToTargetImmediate(GameObject? targetObj)
        {
            if (UiSessionState.ScrollRect == null || targetObj == null || !targetObj.activeInHierarchy) return;
            var contentRt = UiSessionState.ScrollRect.content;
            var targetRt = targetObj.GetComponent<RectTransform>();
            var viewportRt = UiSessionState.ScrollRect.viewport != null
                ? UiSessionState.ScrollRect.viewport
                : (UiSessionState.ScrollRect.transform as RectTransform);
            if (contentRt == null || targetRt == null || viewportRt == null) return;

            UiSessionState.ScrollRect.StopMovement();

            // 精简为最多 1 次 LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt)（彻底消除逐层重复重排与双重 ForceUpdateCanvases）
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);

            float viewportHeight = viewportRt.rect.height;
            float contentHeight = contentRt.rect.height;
            float scrollableHeight = contentHeight - viewportHeight;
            if (scrollableHeight <= 1f)
            {
                UiSessionState.ScrollRect.verticalNormalizedPosition = 1f;
                UiSessionState.ScrollRect.StopMovement();
                return;
            }

            Vector3[] corners = new Vector3[4];
            targetRt.GetWorldCorners(corners); // corners[1] 为左上角
            Vector3 itemTopInContent = contentRt.InverseTransformPoint(corners[1]);
            float distanceFromTop = Mathf.Max(0f, contentRt.rect.yMax - itemTopInContent.y - 8f);
            float normalized = 1f - Mathf.Clamp01(distanceFromTop / scrollableHeight);
            UiSessionState.ScrollRect.verticalNormalizedPosition = normalized;
            UiSessionState.ScrollRect.StopMovement();
        }

        internal static void ResetScrollToTop()
        {
            if (UiSessionState.ScrollRect == null) return;
            UiSessionState.ScrollRect.StopMovement();
            Canvas.ForceUpdateCanvases();
            UiSessionState.ScrollRect.verticalNormalizedPosition = 1f;
        }

        internal static void RequestScrollToTarget(GameObject target, int minFrameDelay = 1, int passes = 1)
        {
            UiSessionState.PendingScrollTarget = target;
            UiSessionState.PendingScrollMinFrame = Time.frameCount + minFrameDelay;
            UiSessionState.PendingScrollRemainingPasses = passes;
        }

        internal static void TickPendingScroll()
        {
            if (UiSessionState.PendingScrollTarget != null && UiSessionState.CurrentWindow != null)
            {
                if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f || UiSessionState.IsConflictViewActive || UiSessionState.IsDiagnosticViewActive)
                {
                    ClearPendingScroll();
                }
                else if (Time.frameCount >= UiSessionState.PendingScrollMinFrame)
                {
                    var target = UiSessionState.PendingScrollTarget;
                    ScrollToTargetImmediate(target);
                    UiSessionState.PendingScrollRemainingPasses--;
                    if (UiSessionState.PendingScrollRemainingPasses <= 0)
                    {
                        ClearPendingScroll();
                    }
                }
            }
        }
    }
}
