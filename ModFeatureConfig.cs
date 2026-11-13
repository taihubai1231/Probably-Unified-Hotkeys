using System;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 模组功能模块化配置中心：
    /// 将功能细分为基础底座与自由可选模块，通过官方标准 ModConfig.For("probably_unified_hotkeys") 持久化。
    /// 自动呈现在「改造件」面板右侧齿轮与「ESC → 选项」本模组独立卡片内。
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

#if !DISABLE_DLL_KEY_PROBE
        private static ConfigEntry<bool>? _entryDllProbe;
#endif
        private static ConfigEntry<bool>? _entryPerformancePatches;
        private static ConfigEntry<bool>? _entryFastQuit;
        private static ConfigEntry<bool>? _entryPinyinSearch;
        private static ConfigEntry<bool>? _entryKeySuppression;

        public static event Action? FeatureConfigChanged;

        /// <summary>
        /// 1. 第三方模组只读热键静态探测 (涉及外部文件只读权限，默认暂时关闭；无权限模式下恒为 false)
        /// </summary>
#if !DISABLE_DLL_KEY_PROBE
        public static bool EnableDllProbe => _entryDllProbe?.Value ?? false;
#else
        public static bool EnableDllProbe => false;
#endif

        /// <summary>
        /// 2. 界面流畅度深度优化补丁 (开窗离线预热、字号单次测量缓存、列表行对象池化复用与 ConflictScanner 双扫去重)
        /// </summary>
        public static bool EnablePerformancePatches => _entryPerformancePatches?.Value ?? true;

        /// <summary>
        /// 3. 优化退出游戏流程 (秒退，跳过进程销毁前的几十次重复卸载重编译与遥测空等)
        /// </summary>
        public static bool EnableFastQuit => _entryFastQuit?.Value ?? true;

        /// <summary>
        /// 4. 智能拼音与繁简检索增强 (支持拼音首字母模糊匹配与繁简汉字智能互查)
        /// </summary>
        public static bool EnablePinyinSearch => _entryPinyinSearch?.Value ?? true;

        /// <summary>
        /// 5. 高级防误触隔离 (按住修饰键触发组合键时抑制裸键单按与长按动作)
        /// </summary>
        public static bool EnableKeySuppression => _entryKeySuppression?.Value ?? true;

        public static void EnsureRegistered()
        {
            try
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
                    () => I18n.Get("只读热键静态探测", "Read-Only Hotkey Probing"),
                    () => I18n.Get(
                        "基于 DseCecil 与本地配置扫描未公开官方配置的代码模组按键，并入冲突排查与展示。关闭后完全不执行外部 DLL 读盘探测，界面不显示任何探测项。",
                        "Scans unmanaged code mod DLLs and local configs for hardcoded hotkeys via DseCecil. When disabled, stops file probing and hides all probe UI entirely."),
                    onChanged: OnDllProbeToggled
                );
#endif

                _entryPerformancePatches = RegisterBoolEntry(
                    config,
                    KeyPerformancePatches,
                    defaultValue: true,
                    () => I18n.Get("流畅度深度优化补丁", "Performance Optimizations"),
                    () => I18n.Get(
                        "包含设置窗口离线装配预热、卡片字号单次测量缓存、改造件列表行对象池化与 ConflictScanner 双扫去重。",
                        "Enables offline window prewarming, font size measurement caching, mod list row pooling, and ConflictScanner deduplication."),
                    onChanged: OnPerformancePatchesToggled
                );

                _entryFastQuit = RegisterBoolEntry(
                    config,
                    KeyFastQuit,
                    defaultValue: true,
                    () => I18n.Get("优化退出游戏流程 (秒退)", "Fast Game Exit"),
                    () => I18n.Get(
                        "退出游戏或返回主菜单时优先触发保存配置并跳过无意义的动态卸载重编译与遥测空等。",
                        "Saves mod configs first and skips redundant dynamic unpatch re-compilation and telemetry sleep during quit."),
                    onChanged: OnFeatureChangedNotify
                );

                _entryPinyinSearch = RegisterBoolEntry(
                    config,
                    KeyPinyinSearch,
                    defaultValue: true,
                    () => I18n.Get("智能拼音与繁简检索", "Pinyin & Traditional Search"),
                    () => I18n.Get(
                        "启用 2 万字离线拼音词库与繁简对照表，支持拼音首字母（如 sp=商品）与繁简互搜；关闭后仅保留原生精确文本匹配。",
                        "Enables 20k-character offline pinyin and Traditional/Simplified search (e.g. sp=商品); falls back to exact text matching when disabled."),
                    onChanged: OnFeatureChangedNotify
                );

                _entryKeySuppression = RegisterBoolEntry(
                    config,
                    KeyKeySuppression,
                    defaultValue: true,
                    () => I18n.Get("高级防误触隔离", "Smart Hotkey Suppression"),
                    () => I18n.Get(
                        "触发组合键时精准抑制同主键的裸键单按与长按动作，防止改键后误触背景功能。",
                        "Suppresses naked single-key actions when modifier combos trigger to prevent accidental background triggers."),
                    onChanged: OnFeatureChangedNotify
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] ModFeatureConfig registration warning: {ex.Message}");
            }
        }

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
            ModCatalogCache.Invalidate();
            FeatureConfigChanged?.Invoke();
        }
#endif

        private static void OnPerformancePatchesToggled(ConfigEntry<bool> entry)
        {
            PerformancePatches.ResetSessionState();
            FeatureConfigChanged?.Invoke();
        }

        private static void OnFeatureChangedNotify(ConfigEntry<bool> entry)
        {
            FeatureConfigChanged?.Invoke();
        }
    }
}
