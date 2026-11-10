using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AllowUnbindKey
{
    /// <summary>
    /// 环境指纹与轻量高速缓存管理器
    /// 保证在模组目录未变更时 0 毫秒秒读，绝不发生重复深层扫描
    /// </summary>
    public static class ModCatalogCache
    {
        private static string _lastFingerprint = string.Empty;
        private static List<ModOptionGroup>? _cachedGroups;
        private static List<ModHotkeyEntry>? _cachedAllHotkeys;
        private static readonly object _lock = new object();

        /// <summary>
        /// 获取当前生效的模组配置组列表（自动利用缓存）
        /// </summary>
        public static List<ModOptionGroup> GetOrScanGroups(out List<ModHotkeyEntry> allHotkeys)
        {
            lock (_lock)
            {
                string currentFingerprint = ComputeCurrentFingerprint();

                if (_cachedGroups != null && _cachedAllHotkeys != null && string.Equals(_lastFingerprint, currentFingerprint, StringComparison.Ordinal))
                {
                    // 指纹完全匹配，模组环境没有任何变动，直接返回内存高速缓存
                    allHotkeys = _cachedAllHotkeys;
                    return _cachedGroups;
                }

                // 首次运行或检测到模组变动，执行一次轻量化扫描
                _cachedGroups = ModOptionScanner.ScanAll(out _cachedAllHotkeys);
                _lastFingerprint = currentFingerprint;
                allHotkeys = _cachedAllHotkeys;

                return _cachedGroups;
            }
        }

        /// <summary>
        /// 主动使缓存失效（例如在特定重新加载时调用）
        /// </summary>
        public static void Invalidate()
        {
            lock (_lock)
            {
                _lastFingerprint = string.Empty;
                _cachedGroups = null;
                _cachedAllHotkeys = null;
            }
        }

        /// <summary>
        /// 计算当前游戏模组环境特征指纹
        /// </summary>
        private static string ComputeCurrentFingerprint()
        {
            try
            {
                var sb = new StringBuilder(512);
                var mods = ModLoader.Mods;
                if (mods != null)
                {
                    sb.Append("COUNT:").Append(mods.Count).Append(";");
                    foreach (var mod in mods)
                    {
                        if (mod?.Manifest == null) continue;
                        sb.Append(mod.Manifest.ID).Append("@").Append(mod.Manifest.ModVersion).Append(";");
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AllowUnbindKey] Error computing fingerprint: {ex.Message}");
                return Guid.NewGuid().ToString(); // 出错时回退到重新扫描
            }
        }
    }
}
