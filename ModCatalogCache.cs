using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
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
        /// 在主菜单闲置帧预先暖机模组配置目录缓存
        /// </summary>
        internal static void Prewarm()
        {
            GetOrScanGroups(out _);
        }

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
                    // 指纹完全匹配，重置旧会话 UI 引用与未应用的待定状态后直接复用缓存
                    ResetUiSessionState(_cachedGroups, _cachedAllHotkeys);
                    allHotkeys = _cachedAllHotkeys;
                    return _cachedGroups;
                }

                // 首次运行或检测到模组变动，执行一次轻量化扫描
                _cachedGroups = ModOptionScanner.ScanAll(out _cachedAllHotkeys);
                _lastFingerprint = currentFingerprint;
                ResetUiSessionState(_cachedGroups, _cachedAllHotkeys);
                allHotkeys = _cachedAllHotkeys;

                return _cachedGroups;
            }
        }

        private static void ResetUiSessionState(List<ModOptionGroup> groups, List<ModHotkeyEntry> allHotkeys)
        {
            foreach (var g in groups)
            {
                g.IsContentBuilt = false;
                g.HeaderRowObject = null;
                g.ContentContainerObject = null;
                g.OuterSeparatorObject = null;
                g.FoldArrowElement = null;
                g.ResetButtonElement = null;

                foreach (var gen in g.GeneralSettings)
                {
                    gen.PendingBool = null;
                    gen.PendingFloat = null;
                    gen.PendingIndex = null;
                    gen.RowObject = null;
                    gen.ControlElement = null;
                    gen.ValueLabelElement = null;
                }
            }

            foreach (var hk in allHotkeys)
            {
                hk.HasPendingChange = false;
                hk.PendingModifiers = KeyModifierMask.None;
                hk.PendingKey = KeyCode.None;
                hk.RowObject = null;
                hk.ButtonElement = null;
                hk.ConflictButtonElement = null;
                hk.LabelElement = null;
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
                        string id = mod.Manifest.ID;
                        bool enabled = ModLoader.IsModEnabled(id);
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
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error computing fingerprint: {ex.Message}");
                return Guid.NewGuid().ToString(); // 出错时回退到重新扫描
            }
        }
    }
}
