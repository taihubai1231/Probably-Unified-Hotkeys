using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProbablyUnifiedHotkeys
{
    public enum UiLanguageMode
    {
        FollowGame = 0,
        SimplifiedChinese = 1,
        English = 2
    }

    /// <summary>
    /// 纯文本双语与本地化核心：提供语言模式配置绑定、动态热切换事件与无头单测覆盖。
    /// 严禁引入任何 UGUI / TextMeshPro 控件或上层业务缓存。
    /// </summary>
    public static class BilingualText
    {
        public const string ModId = "probably_unified_hotkeys";
        public const string LanguageConfigKey = "ui_language";
        public const string SectionHeader = "语言设置 / Language";
        public const string OwnDisplayName = "可能是按键统一优化 / Probably Unified Hotkeys";

        public const string ChoiceAuto = "auto";
        public const string ChoiceZh = "zh";
        public const string ChoiceEn = "en";

        private static readonly string[] AllChoiceValues = new[] { ChoiceAuto, ChoiceZh, ChoiceEn };

        private static ConfigEntry<string>? _languageEntry;

        /// <summary>
        /// 语言变动事件，供上层 UI 视图与选项卡片监听重绘。
        /// </summary>
        public static event Action? LanguageChanged;

        /// <summary>
        /// 仅用于无头单元测试覆盖当前游戏语言代码，运行时为 null。
        /// </summary>
        internal static Func<string>? LocaleCodeOverrideForTest { get; set; }

        /// <summary>
        /// 仅用于无头单元测试覆盖界面语言模式，运行时为 null。
        /// </summary>
        internal static UiLanguageMode? ModeOverrideForTest { get; set; }

        public static bool IsChinese
        {
            get
            {
                if (ModeOverrideForTest.HasValue)
                {
                    var testMode = ModeOverrideForTest.Value;
                    if (testMode == UiLanguageMode.SimplifiedChinese) return true;
                    if (testMode == UiLanguageMode.English) return false;
                    // FollowGame 模式继续向下判定区域代码
                }

                if (LocaleCodeOverrideForTest != null)
                {
                    string testLocale = LocaleCodeOverrideForTest() ?? string.Empty;
                    return IsLocaleChinese(testLocale);
                }

                if (!ModeOverrideForTest.HasValue)
                {
                    var mode = GetConfiguredMode();
                    if (mode == UiLanguageMode.SimplifiedChinese) return true;
                    if (mode == UiLanguageMode.English) return false;
                }

                string locale = GetSafeGameLocaleCode();
                if (string.IsNullOrEmpty(locale))
                {
                    // 非主菜单/未初始化时安全降级中文
                    return true;
                }
                return IsLocaleChinese(locale);
            }
        }

        public static string Get(string zh, string en)
        {
            return IsChinese ? zh : en;
        }

        public static string Format(string zhFormat, string enFormat, params object[] args)
        {
            return string.Format(IsChinese ? zhFormat : enFormat, args);
        }

        public static ConfigEntry<string>? EnsureRegistered()
        {
            try
            {
                return EnsureRegisteredInternal();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"BilingualText EnsureRegistered warning: {ex.Message}");
                UnbindEntry();
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ConfigEntry<string>? EnsureRegisteredInternal()
        {
            ModConfig? config = ModConfig.Find(ModId) ?? ModConfig.For(ModId);
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

        public static void NotifyLanguageChanged()
        {
            try
            {
                LanguageChanged?.Invoke();
            }
            catch (Exception ex)
            {
                ModLogger.Error("Exception in LanguageChanged event handler", ex);
            }
        }

        public static UiLanguageMode GetConfiguredMode()
        {
            if (ModeOverrideForTest.HasValue)
            {
                return ModeOverrideForTest.Value;
            }

            try
            {
                var entry = EnsureRegistered();
                return ParseMode(entry?.Value);
            }
            catch
            {
                return UiLanguageMode.FollowGame;
            }
        }

        public static void SetConfiguredMode(UiLanguageMode mode)
        {
            if (ModeOverrideForTest.HasValue)
            {
                ModeOverrideForTest = mode;
            }

            try
            {
                var entry = EnsureRegistered();
                entry?.Set(ModeToChoiceValue(mode));
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to set configured language mode: {ex.Message}");
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
                return GetGameLocaleCodeInternal();
            }
            catch
            {
                return string.Empty;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetGameLocaleCodeInternal()
        {
            return LocHelper.GetCurrentLocaleCode() ?? string.Empty;
        }

        public static bool IsLocaleChinese(string? code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            return code!.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        public static UiLanguageMode ParseMode(string? raw)
        {
            string s = (raw ?? string.Empty).Trim();
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

        public static UiLanguageMode ParseMode(object? raw)
        {
            return ParseMode(raw?.ToString());
        }

        public static string ModeToChoiceValue(UiLanguageMode mode)
        {
            switch (mode)
            {
                case UiLanguageMode.SimplifiedChinese: return ChoiceZh;
                case UiLanguageMode.English: return ChoiceEn;
                default: return ChoiceAuto;
            }
        }

        public static List<string> GetLanguageChoices()
        {
            return new List<string>(3)
            {
                Get("跟随游戏语言", "Follow Game"),
                "简体中文",
                "English"
            };
        }

        public static string GetLanguageTooltip()
        {
            return Get(
                "设置本模组界面的显示语言。",
                "Sets the display language of this mod's interface."
            );
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
    }
}
