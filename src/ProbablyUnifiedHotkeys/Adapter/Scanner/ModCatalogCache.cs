#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 环境指纹与轻量高速缓存管理器。
    /// 保证在模组目录未变更时 0 毫秒秒读，绝不发生重复深层扫描。
    /// 纯领域解耦：仅重置纯数据待定字段，通过 SessionResetRequested 通知 UI 层清理控件字典。
    /// </summary>
    public static class ModCatalogCache
    {
        private static string _lastFingerprint = string.Empty;
        private static List<ModOptionGroup>? _cachedGroups;
        private static List<ModHotkeyEntry>? _cachedAllHotkeys;
        private static ModScanReport? _cachedScanReport;
        private static readonly object _lock = new object();
        private static bool _eventsSubscribed = false;

        /// <summary>
        /// 当缓存重用且重置待定状态时触发，通知上层 UI 视图清理控件字典句柄。
        /// </summary>
        public static event Action? SessionResetRequested;

        static ModCatalogCache()
        {
            EnsureEventSubscriptions();
        }

        /// <summary>
        /// 幂等订阅多语言切换与 DLL 探测开关事件，变动时自动使缓存失效。
        /// </summary>
        public static void EnsureEventSubscriptions()
        {
            if (_eventsSubscribed) return;
            lock (_lock)
            {
                if (_eventsSubscribed) return;
                BilingualText.LanguageChanged += Invalidate;
                ModFeatureConfig.DllProbeToggled += Invalidate;
                _eventsSubscribed = true;
            }
        }

        /// <summary>
        /// 在主菜单闲置帧预先暖机模组配置目录缓存。
        /// </summary>
        internal static void Prewarm()
        {
            GetOrScanGroups(out _, out _);
        }

        /// <summary>
        /// 获取当前生效的模组配置组列表（自动利用缓存）。
        /// </summary>
        public static List<ModOptionGroup> GetOrScanGroups(out List<ModHotkeyEntry> allHotkeys)
        {
            return GetOrScanGroups(out allHotkeys, out _);
        }

        /// <summary>
        /// 获取当前生效的模组配置组列表及扫描诊断报告（自动利用缓存）。
        /// </summary>
        public static List<ModOptionGroup> GetOrScanGroups(out List<ModHotkeyEntry> allHotkeys, out ModScanReport scanReport)
        {
            EnsureEventSubscriptions();
            using (ComboKeyStore.EnterBypass())
            {
                lock (_lock)
                {
                    string currentFingerprint = ComputeCurrentFingerprint();

                    if (_cachedGroups != null && _cachedAllHotkeys != null && _cachedScanReport != null &&
                        string.Equals(_lastFingerprint, currentFingerprint, StringComparison.Ordinal))
                    {
                        // 指纹完全匹配，重置旧会话未应用的待定状态后直接复用缓存
                        ResetPendingSessionState(_cachedGroups, _cachedAllHotkeys);
                        allHotkeys = _cachedAllHotkeys;
                        scanReport = _cachedScanReport;
                        return _cachedGroups;
                    }

                    // 首次运行或检测到模组变动，执行一次轻量化扫描
                    _cachedGroups = ModOptionScanner.ScanAll(out _cachedAllHotkeys, out _cachedScanReport);
                    _lastFingerprint = currentFingerprint;
                    ResetPendingSessionState(_cachedGroups, _cachedAllHotkeys);
                    allHotkeys = _cachedAllHotkeys;
                    scanReport = _cachedScanReport;

                    return _cachedGroups;
                }
            }
        }

        internal static void ResetPendingSessionState(List<ModOptionGroup> groups, List<ModHotkeyEntry> allHotkeys)
        {
            foreach (var g in groups)
            {
                foreach (var gen in g.GeneralSettings)
                {
                    gen.PendingBool = null;
                    gen.PendingFloat = null;
                    gen.PendingIndex = null;
                    gen.PendingString = null;
                }
            }

            foreach (var hk in allHotkeys)
            {
                hk.HasPendingChange = false;
                hk.PendingModifiers = KeyModifierMask.None;
                hk.PendingKey = KeyCode.None;
            }

            try
            {
                SessionResetRequested?.Invoke();
            }
            catch (Exception ex)
            {
                ModLogger.Error("Exception in SessionResetRequested", ex);
            }
        }

        /// <summary>
        /// 主动使缓存失效（例如在特定重新加载时调用）。
        /// </summary>
        public static void Invalidate()
        {
            lock (_lock)
            {
                _lastFingerprint = string.Empty;
                _cachedGroups = null;
                _cachedAllHotkeys = null;
                _cachedScanReport = null;
            }

            try
            {
                SessionResetRequested?.Invoke();
            }
            catch (Exception ex)
            {
                ModLogger.Error("Exception in SessionResetRequested", ex);
            }
        }

        /// <summary>
        /// 计算当前游戏模组环境特征指纹（采用纯配置层接口，彻底消除对 Client/I18n 的逆向引用）。
        /// </summary>
        private static string ComputeCurrentFingerprint()
        {
            try
            {
                var sb = new StringBuilder(512);
                sb.Append("LANG:").Append(BilingualText.IsChinese ? "zh:" : "en:").Append(BilingualText.GetSafeGameLocaleCode()).Append(";");
                sb.Append("FORCED:").Append(ComboKeyStore.GetForcedHotkeysRevision()).Append(";");
                sb.Append("MCFG:").Append(ModOptionScanner.GetRegisteredModConfigCount()).Append(";");

                var mods = ModLoader.Mods;
                if (mods != null)
                {
                    sb.Append("COUNT:").Append(mods.Count).Append(";");
                    foreach (var mod in mods)
                    {
                        if (mod?.Manifest == null) continue;
                        string id = mod.Manifest.ID;
                        bool enabled = mod.IsEnabled || ModLoader.IsModEnabled(id);
                        ModConfig? cfg = ModConfig.Find(id);
                        int cfgCount = cfg?.Entries != null ? cfg.Entries.Count : 0;
                        sb.Append(id).Append("@").Append(mod.Manifest.ModVersion)
                          .Append(enabled ? ":1:" : ":0:")
                          .Append(cfgCount).Append(";");
                    }
                }

                var sections = GameOptionRegistry.Sections;
                if (sections != null)
                {
                    int entryCount = 0;
                    foreach (var sec in sections)
                    {
                        if (sec?.entries != null) entryCount += sec.entries.Count;
                    }
                    sb.Append("REG:").Append(sections.Count).Append(":").Append(entryCount).Append(";");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Error computing fingerprint: {ex.Message}");
                return Guid.NewGuid().ToString(); // 出错时回退到重新扫描
            }
        }
    }
}
