using System;
using System.Runtime.CompilerServices;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 模组功能模块化配置中心：
    /// 将功能细分为基础底座与自由可选模块，通过官方标准 ModConfig.For("probably_unified_hotkeys") 持久化。
    /// 严格遵循单向解耦原则，使用事件通知替代直接反向调用上层组件。
    /// </summary>
    public static class ModFeatureConfig
    {
        public const string ModId = "probably_unified_hotkeys";
        public const string SectionFeatures = "可选功能模块 / Modular Features";

        public const string KeyDllProbe = "enable_dll_probe";
        public const string KeyPerformancePatches = "enable_performance_patches";
        public const string KeyFastQuit = "enable_fast_quit";
        public const string KeyPinyinSearch = "enable_pinyin_search";
        public const string KeyKeySuppression = "enable_key_suppression";
        public const string KeyDebugLogging = "enable_debug_logging";

#if !DISABLE_DLL_KEY_PROBE
        private static ConfigEntry<bool>? _entryDllProbe;
#endif
        private static ConfigEntry<bool>? _entryPerformancePatches;
        private static ConfigEntry<bool>? _entryFastQuit;
        private static ConfigEntry<bool>? _entryPinyinSearch;
        private static ConfigEntry<bool>? _entryKeySuppression;
        private static ConfigEntry<bool>? _entryDebugLogging;

        /// <summary>
        /// 任意功能项变动广播事件。
        /// </summary>
        public static event Action? FeatureConfigChanged;

#pragma warning disable CS0067
        /// <summary>
        /// 静态热键探测开关切换专用事件（由 Adapter/Catalog 订阅）。
        /// </summary>
        public static event Action? DllProbeToggled;
#pragma warning restore CS0067

        /// <summary>
        /// 流畅度优化补丁开关切换专用事件（由 Patches/Performance 订阅）。
        /// </summary>
        public static event Action? PerformancePatchesToggled;

        /// <summary>
        /// 供无头测试覆盖拼音检索开关，非空时覆盖真实配置值。
        /// </summary>
        internal static bool? OverridePinyinSearchForTests { get; set; }

        /// <summary>
        /// 供无头测试覆盖防误触抑键开关，非空时覆盖真实配置值。
        /// </summary>
        internal static bool? OverrideKeySuppressionForTests { get; set; }

        static ModFeatureConfig()
        {
            ModLogger.DebugEnabledProvider = () => EnableDebugLogging;
        }

        /// <summary>
        /// 1. 第三方模组只读热键静态探测 (涉及外部文件只读权限，默认暂时关闭；无权限模式下恒为 false)。
        /// </summary>
#if !DISABLE_DLL_KEY_PROBE
        public static bool EnableDllProbe => _entryDllProbe?.Value ?? false;
#else
        public static bool EnableDllProbe => false;
#endif

        /// <summary>
        /// 2. 界面流畅度深度优化补丁 (开窗离线预热、字号单次测量缓存、列表行对象池化复用与 ConflictScanner 双扫去重)。
        /// </summary>
        public static bool EnablePerformancePatches => _entryPerformancePatches?.Value ?? true;

        /// <summary>
        /// 3. 优化退出游戏流程 (退出或返回主菜单时优先保存配置并跳过无意义的重复卸载与遥测空等)。
        /// </summary>
        public static bool EnableFastQuit => _entryFastQuit?.Value ?? true;

        /// <summary>
        /// 4. 智能拼音与繁简检索增强 (支持 2 万字离线拼音首字母模糊匹配与繁简汉字智能互查)。
        /// </summary>
        public static bool EnablePinyinSearch => _entryPinyinSearch?.Value ?? true;

        /// <summary>
        /// 5. 高级防误触隔离 (按住修饰键触发组合键时抑制裸键单按与长按动作)。
        /// </summary>
        public static bool EnableKeySuppression => _entryKeySuppression?.Value ?? true;

        /// <summary>
        /// 6. 结构化调试诊断日志输出开关。
        /// </summary>
        public static bool EnableDebugLogging => _entryDebugLogging?.Value ?? false;

        /// <summary>
        /// 便捷综合属性：计算当前拼音搜索是否激活（测试覆盖优先于配置）。
        /// </summary>
        public static bool IsPinyinSearchActive => OverridePinyinSearchForTests ?? EnablePinyinSearch;

        /// <summary>
        /// 便捷综合属性：计算当前防误触抑键是否激活（测试覆盖优先于配置）。
        /// </summary>
        public static bool IsKeySuppressionActive => OverrideKeySuppressionForTests ?? EnableKeySuppression;

        public static void EnsureRegistered()
        {
            try
            {
                ModLogger.DebugEnabledProvider = () => EnableDebugLogging;
                EnsureRegisteredInternal();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"ModFeatureConfig registration warning: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsureRegisteredInternal()
        {
            ModConfig? config = ModConfig.Find(ModId) ?? ModConfig.For(ModId);
            if (config == null)
            {
                return;
            }

#if !DISABLE_DLL_KEY_PROBE
            _entryDllProbe = RegisterBoolEntry(
                config,
                KeyDllProbe,
                defaultValue: false,
                () => BilingualText.Get("静态热键探测与改键接管", "Static Hotkey Probe & Rebind"),
                () => BilingualText.Get(
                    "基于 DseCecil 静态扫描未公开官方配置的代码模组硬编码按键，直接并入主设置面板并通过底层 Input 拦截实时接管改键。关闭后不执行外部 DLL 探测。",
                    "Scans unmanaged code mod DLLs for hardcoded hotkeys via DseCecil and integrates them into the main settings panel with live Input rebind takeover."),
                onChanged: OnDllProbeToggled
            );
#endif

            _entryPerformancePatches = RegisterBoolEntry(
                config,
                KeyPerformancePatches,
                defaultValue: true,
                () => BilingualText.Get("流畅度深度优化补丁", "Performance Optimizations"),
                () => BilingualText.Get(
                    "包含设置窗口离线装配预热、卡片字号单次测量缓存、改造件列表行对象池化与 ConflictScanner 双扫去重。",
                    "Enables offline window prewarming, font size measurement caching, mod list row pooling, and ConflictScanner deduplication."),
                onChanged: OnPerformancePatchesToggled
            );

            _entryFastQuit = RegisterBoolEntry(
                config,
                KeyFastQuit,
                defaultValue: true,
                () => BilingualText.Get("优化退出游戏流程", "Fast Game Exit"),
                () => BilingualText.Get(
                    "退出游戏或返回主菜单时优先触发保存配置并跳过无意义的动态卸载重编译与遥测空等。",
                    "Saves mod configs first and skips redundant dynamic unpatch re-compilation and telemetry sleep during quit."),
                onChanged: OnFeatureChangedNotify
            );

            _entryPinyinSearch = RegisterBoolEntry(
                config,
                KeyPinyinSearch,
                defaultValue: true,
                () => BilingualText.Get("智能拼音与繁简检索", "Pinyin & Traditional Search"),
                () => BilingualText.Get(
                    "启用 2 万字离线拼音词库与繁简对照表，支持拼音首字母（如 sp=商品）与繁简互搜；关闭后仅保留原生精确文本匹配。",
                    "Enables 20k-character offline pinyin and Traditional/Simplified search (e.g. sp=商品); falls back to exact text matching when disabled."),
                onChanged: OnFeatureChangedNotify
            );

            _entryKeySuppression = RegisterBoolEntry(
                config,
                KeyKeySuppression,
                defaultValue: true,
                () => BilingualText.Get("高级防误触隔离", "Smart Hotkey Suppression"),
                () => BilingualText.Get(
                    "触发组合键时精准抑制同主键的裸键单按与长按动作，防止改键后误触背景功能。",
                    "Suppresses naked single-key actions when modifier combos trigger to prevent accidental background triggers."),
                onChanged: OnFeatureChangedNotify
            );

            _entryDebugLogging = RegisterBoolEntry(
                config,
                KeyDebugLogging,
                defaultValue: false,
                () => BilingualText.Get("详细调试诊断日志", "Debug Diagnostics Logging"),
                () => BilingualText.Get(
                    "输出模组详细适配过程、按键映射与生命周期跟踪日志至控制台，用于问题排查。",
                    "Outputs verbose mod adaptation, key mapping, and lifecycle tracking logs to console for troubleshooting."),
                onChanged: OnFeatureChangedNotify
            );
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ConfigEntry<bool> RegisterBoolEntry(
            ModConfig config,
            string key,
            bool defaultValue,
            Func<string> nameFunc,
            Func<string> descFunc,
            Action<ConfigEntry<bool>>? onChanged)
        {
            if (config.Get(key) is ConfigEntry<bool> existing)
            {
                if (onChanged != null)
                {
                    existing.OnChanged += _ => onChanged(existing);
                }
                return existing;
            }

            ConfigEntry<bool> created = config.Define(
                key,
                defaultValue,
                SectionFeatures,
                nameFunc,
                descFunc,
                requiresRestart: false
            );

            if (onChanged != null)
            {
                created.OnChanged += _ => onChanged(created);
            }

            return created;
        }

#if !DISABLE_DLL_KEY_PROBE
        private static void OnDllProbeToggled(ConfigEntry<bool> entry)
        {
            DllProbeToggled?.Invoke();
            FeatureConfigChanged?.Invoke();
        }
#endif

        private static void OnPerformancePatchesToggled(ConfigEntry<bool> entry)
        {
            PerformancePatchesToggled?.Invoke();
            FeatureConfigChanged?.Invoke();
        }

        private static void OnFeatureChangedNotify(ConfigEntry<bool> entry)
        {
            FeatureConfigChanged?.Invoke();
        }
    }
}
