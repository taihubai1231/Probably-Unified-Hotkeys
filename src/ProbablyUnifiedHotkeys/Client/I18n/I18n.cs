#nullable enable
using System;
using System.Collections.Generic;
using TMPro;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 双语与本地化门面（BilingualText 的完整超集兼容层）。
    /// 保留 v1 全部公开常量、事件与静态方法签名，并提供 UGUI/TMP 字体回退辅助。
    /// </summary>
    public static class I18n
    {
        public const string ModId = BilingualText.ModId;
        public const string LanguageConfigKey = BilingualText.LanguageConfigKey;
        public const string SectionHeader = BilingualText.SectionHeader;
        public const string OwnDisplayName = BilingualText.OwnDisplayName;

        public const string ChoiceAuto = BilingualText.ChoiceAuto;
        public const string ChoiceZh = BilingualText.ChoiceZh;
        public const string ChoiceEn = BilingualText.ChoiceEn;

        /// <summary>
        /// 仅用于无头单元测试覆盖当前游戏语言代码，运行时为 null。
        /// </summary>
        internal static Func<string>? LocaleCodeOverrideForTest
        {
            get => BilingualText.LocaleCodeOverrideForTest;
            set => BilingualText.LocaleCodeOverrideForTest = value;
        }

        /// <summary>
        /// 语言变动事件，供上层 UI 视图与选项卡片监听重绘。
        /// </summary>
        public static event Action? LanguageChanged
        {
            add => BilingualText.LanguageChanged += value;
            remove => BilingualText.LanguageChanged -= value;
        }

        public static ConfigEntry<string>? EnsureRegistered()
        {
            return BilingualText.EnsureRegistered();
        }

        internal static void NotifyLanguageChanged()
        {
            ModCatalogCache.Invalidate();
            BilingualText.NotifyLanguageChanged();
        }

        public static UiLanguageMode GetConfiguredMode()
        {
            return BilingualText.GetConfiguredMode();
        }

        public static void SetConfiguredMode(UiLanguageMode mode)
        {
            BilingualText.SetConfiguredMode(mode);
        }

        public static bool IsChinese => BilingualText.IsChinese;

        public static string GetSafeGameLocaleCode()
        {
            return BilingualText.GetSafeGameLocaleCode();
        }

        public static bool IsLocaleChinese(string? code)
        {
            return BilingualText.IsLocaleChinese(code);
        }

        public static UiLanguageMode ParseMode(object? raw)
        {
            return BilingualText.ParseMode(raw);
        }

        public static int LanguageToIndex(object? raw)
        {
            return (int)BilingualText.ParseMode(raw);
        }

        public static string IndexToChoiceValue(int index)
        {
            switch (index)
            {
                case 1: return ChoiceZh;
                case 2: return ChoiceEn;
                default: return ChoiceAuto;
            }
        }

        public static string ModeToChoiceValue(UiLanguageMode mode)
        {
            return BilingualText.ModeToChoiceValue(mode);
        }

        public static List<string> GetLanguageChoices()
        {
            return BilingualText.GetLanguageChoices();
        }

        public static string GetLanguageTooltip()
        {
            return BilingualText.GetLanguageTooltip();
        }

        public static string Get(string zh, string en)
        {
            return BilingualText.Get(zh, en);
        }

        public static string Format(string zhFormat, string enFormat, params object[] args)
        {
            return BilingualText.Format(zhFormat, enFormat, args);
        }

        public static bool ContainsCjk(string? text)
        {
            return BilingualText.ContainsCjk(text);
        }

        public static void ApplyFontIfRuntime(TMP_Text? tmp, string? text)
        {
            if (tmp == null) return;
            try
            {
                if (FontLoader.Instance == null) return;
                if (ContainsCjk(text) && !IsLocaleChinese(GetSafeGameLocaleCode()))
                {
                    var chineseFont = FontLoader.Instance.simplifiedChineseFont;
                    if (chineseFont != null && tmp.font != chineseFont)
                    {
                        tmp.font = chineseFont;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"ApplyFontIfRuntime exception: {ex.Message}");
            }
        }

        public static void ApplyFontToDropdown(CustomUIElement? el, bool forceCjkCapableList = false)
        {
            if (el?.dropdown == null) return;
            try
            {
                // 统一优化所有下拉框列表的滚轮滚动灵敏度至 45f
                if (el.dropdown.template != null)
                {
                    var scroll = el.dropdown.template.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
                    if (scroll != null)
                    {
                        scroll.scrollSensitivity = 45f;
                    }
                }

                if (FontLoader.Instance == null) return;
                var chineseFont = FontLoader.Instance.simplifiedChineseFont;
                if (chineseFont == null) return;

                if (forceCjkCapableList || ContainsCjk(el.dropdown.captionText?.text))
                {
                    if (el.dropdown.itemText != null && forceCjkCapableList)
                    {
                        el.dropdown.itemText.font = chineseFont;
                    }
                    if (el.dropdown.captionText != null && (forceCjkCapableList || ContainsCjk(el.dropdown.captionText.text)))
                    {
                        el.dropdown.captionText.font = chineseFont;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"ApplyFontToDropdown exception: {ex.Message}");
            }
        }
    }
}
