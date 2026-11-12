using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    public enum UiLanguageMode
    {
        FollowGame = 0,
        SimplifiedChinese = 1,
        English = 2
    }

    public static class I18n
    {
        public const string ModId = "probably_unified_hotkeys";
        public const string LanguageConfigKey = "ui_language";
        public const string SectionHeader = "界面语言 / UI Language";
        public const string OwnDisplayName = "可能是按键统一优化 / Probably Unified Hotkeys";

        public const string ChoiceAuto = "auto";
        public const string ChoiceZh = "zh";
        public const string ChoiceEn = "en";

        private static readonly string[] AllChoiceValues = new[] { ChoiceAuto, ChoiceZh, ChoiceEn };

        private static ConfigEntry<string>? _languageEntry;

        /// <summary>
        /// 仅用于无头单元测试覆盖当前游戏语言代码，运行时为 null
        /// </summary>
        internal static Func<string>? LocaleCodeOverrideForTest = null;

        public static event Action? LanguageChanged;

        public static ConfigEntry<string>? EnsureRegistered()
        {
            try
            {
                ModConfig? config = ModConfig.Find(ModId);
                if (config == null)
                {
                    config = ModConfig.For(ModId);
                }

                if (config == null)
                {
                    UnbindEntry();
                    return null;
                }

                if (config.Get(LanguageConfigKey) is ConfigEntry<string> existing)
                {
                    BindEntry(existing);
                    return existing;
                }

                ConfigEntry<string> created = config.Define(
                    LanguageConfigKey,
                    ChoiceAuto,
                    SectionHeader,
                    () => Get("界面语言", "UI Language"),
                    AllChoiceValues,
                    GetLanguageTooltip,
                    requiresRestart: false
                );
                BindEntry(created);
                return created;
            }
            catch
            {
                UnbindEntry();
                return null;
            }
        }

        private static void BindEntry(ConfigEntry<string> entry)
        {
            if (ReferenceEquals(_languageEntry, entry)) return;
            UnbindEntry();
            _languageEntry = entry;
            _languageEntry.OnChanged += OnConfigEntryValueChanged;
        }

        private static void UnbindEntry()
        {
            if (_languageEntry == null) return;
            _languageEntry.OnChanged -= OnConfigEntryValueChanged;
            _languageEntry = null;
        }

        private static void OnConfigEntryValueChanged(string _)
        {
            NotifyLanguageChanged();
        }

        internal static void NotifyLanguageChanged()
        {
            ModCatalogCache.Invalidate();
            try
            {
                LanguageChanged?.Invoke();
            }
            catch { }
        }

        public static UiLanguageMode GetConfiguredMode()
        {
            var entry = EnsureRegistered();
            return ParseMode(entry?.Value);
        }

        public static void SetConfiguredMode(UiLanguageMode mode)
        {
            var entry = EnsureRegistered();
            entry?.Set(ModeToChoiceValue(mode));
        }

        public static bool IsChinese
        {
            get
            {
                var mode = GetConfiguredMode();
                if (mode == UiLanguageMode.SimplifiedChinese) return true;
                if (mode == UiLanguageMode.English) return false;
                return IsLocaleChinese(GetSafeGameLocaleCode());
            }
        }

        public static string GetSafeGameLocaleCode()
        {
            if (LocaleCodeOverrideForTest != null)
            {
                try
                {
                    return LocaleCodeOverrideForTest() ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            try
            {
                return LocHelper.GetCurrentLocaleCode() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool IsLocaleChinese(string? code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            return code!.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        public static UiLanguageMode ParseMode(object? raw)
        {
            string s = (raw?.ToString() ?? string.Empty).Trim();
            if (string.Equals(s, ChoiceZh, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "zh-Hans", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "SimplifiedChinese", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "简体中文", StringComparison.OrdinalIgnoreCase))
            {
                return UiLanguageMode.SimplifiedChinese;
            }

            if (string.Equals(s, ChoiceEn, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "English", StringComparison.OrdinalIgnoreCase))
            {
                return UiLanguageMode.English;
            }

            return UiLanguageMode.FollowGame;
        }

        public static int LanguageToIndex(object? raw)
        {
            return (int)ParseMode(raw);
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
            return IndexToChoiceValue((int)mode);
        }

        public static List<string> GetLanguageChoices()
        {
            return new List<string>(3)
            {
                Get("跟随游戏语言", "Follow Game Language"),
                "简体中文",
                "English"
            };
        }

        public static string GetLanguageTooltip()
        {
            return Get(
                "选择本模组界面显示语言：\n" +
                "• 跟随游戏语言（默认）：自动随游戏语言切换简体中文或英文\n" +
                "• 简体中文 / English：强制使用对应语言显示\n" +
                "修改后点击「应用设置」立即生效。",
                "Choose the display language for this mod's interface:\n" +
                "• Follow Game Language (Default): Automatically follows the game's language (CN / EN)\n" +
                "• 简体中文 / English: Force the selected display language\n" +
                "Click Apply to take effect immediately."
            );
        }

        public static string Get(string zh, string en)
        {
            return IsChinese ? zh : en;
        }

        public static string Format(string zhFormat, string enFormat, params object[] args)
        {
            return string.Format(IsChinese ? zhFormat : enFormat, args);
        }

        public static bool ContainsCjk(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text!.Length; i++)
            {
                char c = text[i];
                if ((c >= '\u4e00' && c <= '\u9fff') ||
                    (c >= '\u3400' && c <= '\u4dbf') ||
                    (c >= '\u3000' && c <= '\u303f') ||
                    (c >= '\uff00' && c <= '\uffef'))
                {
                    return true;
                }
            }
            return false;
        }

        public static void ApplyFontIfRuntime(TMP_Text? tmp, string? text)
        {
            if (tmp == null || FontLoader.Instance == null) return;
            try
            {
                if (ContainsCjk(text) && !IsLocaleChinese(GetSafeGameLocaleCode()))
                {
                    var chineseFont = FontLoader.Instance.simplifiedChineseFont;
                    if (chineseFont != null && tmp.font != chineseFont)
                    {
                        tmp.font = chineseFont;
                    }
                }
            }
            catch { }
        }

        public static void ApplyFontToDropdown(CustomUIElement? el, bool forceCjkCapableList = false)
        {
            if (el?.dropdown == null || FontLoader.Instance == null) return;
            try
            {
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
            catch { }
        }
    }
}
