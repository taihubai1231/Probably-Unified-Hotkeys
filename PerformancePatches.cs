using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// v1.4.0 全局性能与流畅度底层优化补丁：
    /// 1. 退出游戏（主菜单/暂停菜单/Alt+F4）：跳过无效 1s 分析等待，并在安全触发所有模组 OnDisable 后跳过数十次同步 Harmony wrapper 重编译。
    /// 2. 改造件面板（ModUIManager）：复用行池化对象、差分更新激活状态与文案、O(1) 依赖索引字典、空详情缓存及布局批量挂起。
    /// 3. 主菜单闲置渐进预热（零后台线程，严格遵守 ModScanner）：分帧静默预热拼音库、模组配置目录及改造件面板 UI 骨架。
    /// </summary>
    internal static class PerformancePatches
    {
        private static bool _fastQuitPrepared;

        internal static void ResetFastQuitState()
        {
            _fastQuitPrepared = false;
        }

        internal static void ResetSessionState()
        {
            _fastQuitPrepared = false;
            IdleWarmupRunner.Reset();
            ModUIManagerPerfHelper.ResetCaches();
            ConflictScannerPerfHelper.ResetSession();
        }

        /// <summary>
        /// 在游戏退出前幂等执行：安全通知各模组 OnDisable 后清空 ModLoader 持有的一次性 HarmonyInstance 引用，
        /// 避免 ModLoader.OnApplicationQuit 在进程即将销毁前逐个触发 40+ 次同步 Harmony.UnpatchAll（IL/Detour 重编译）。
        /// </summary>
        internal static void PrepareFastQuit()
        {
            if (!ModFeatureConfig.EnableFastQuit)
            {
                return;
            }
            if (_fastQuitPrepared)
            {
                return;
            }
            _fastQuitPrepared = true;
            ModEntry.MarkApplicationQuitting();

            try
            {
                List<LoadedMod>? mods = ModLoader.Mods;
                if (mods == null)
                {
                    return;
                }

                for (int i = 0; i < mods.Count; i++)
                {
                    LoadedMod mod = mods[i];
                    if (mod == null || mod.HarmonyInstance == null)
                    {
                        continue;
                    }

                    try
                    {
                        mod.Instance?.OnDisable();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ProbablyUnifiedHotkeys] Mod OnDisable warning during quit ({mod.Manifest?.ID}): {ex.Message}");
                    }

                    mod.HarmonyInstance = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] FastQuit preparation warning: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // 1. 退出游戏秒退优化补丁
    // =========================================================================

    [HarmonyPatch(typeof(MainMenuUIController), nameof(MainMenuUIController.OnExitClick))]
    public static class Patch_MainMenuUIController_OnExitClick
    {
        public static void Prefix()
        {
            if (!ModFeatureConfig.EnableFastQuit) return;
            PerformancePatches.PrepareFastQuit();
        }
    }

    [HarmonyPatch(typeof(EscapeUIManager), nameof(EscapeUIManager.OnExit))]
    public static class Patch_EscapeUIManager_OnExit
    {
        private static readonly FieldInfo? IsPendingTransitionField =
            AccessTools.Field(typeof(EscapeUIManager), "isPendingTransition");

        public static bool Prefix(EscapeUIManager __instance)
        {
            if (!ModFeatureConfig.EnableFastQuit)
            {
                return true;
            }
            if (__instance == null)
            {
                return true;
            }

            if (IsPendingTransitionField != null && (bool)(IsPendingTransitionField.GetValue(__instance) ?? false))
            {
                return false;
            }

            // 保持与原版一致的选项读取
            if (EmporiumOption.Instance != null)
            {
                _ = EmporiumOption.Instance.hasWishlistRecommended;
            }

            PerformancePatches.PrepareFastQuit();

            // 原版在 EmporiumAnalytics.OVERWRITE_DISABLE_ANALYTICS == true 时仍会对已开启遥测的玩家空等 1 秒
            bool analyticsDisabled = EmporiumAnalytics.OVERWRITE_DISABLE_ANALYTICS
                                     || EmporiumOption.Instance == null
                                     || !EmporiumOption.Instance.consentAnalytics;
            if (analyticsDisabled)
            {
                IsPendingTransitionField?.SetValue(__instance, true);
                __instance.OnAnyClick();
                Application.Quit();
                return false;
            }

            return true;
        }
    }

    // =========================================================================
    // 2. 主菜单闲置单线程分帧预热（零 Thread / Task / 外部 StartCoroutine）
    // =========================================================================

    [HarmonyPatch(typeof(MainMenuUIController), "Start")]
    public static class Patch_MainMenuUIController_Start
    {
        public static void Postfix()
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return;
            IdleWarmupRunner.Schedule();
        }
    }

    internal sealed class IdleWarmupRunner : MonoBehaviour
    {
        private static IdleWarmupRunner? _instance;
        private int _framesWaited;
        private int _stage;

        internal static void Schedule()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("PUH_IdleWarmup");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<IdleWarmupRunner>();
        }

        internal static void Reset()
        {
            if (!ReferenceEquals(_instance, null) && _instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        private void Update()
        {
            // 先让主菜单首帧及本地化协同彻底渲染落定
            if (_framesWaited < 3)
            {
                _framesWaited++;
                return;
            }

            try
            {
                switch (_stage)
                {
                    case 0:
                        // 第 1 步：预热拼音全拼与繁简查表 (~20,000 字符索引)
                        PinyinEngine.Prewarm();
                        _stage = 1;
                        return;

                    case 1:
                        // 第 2 步：预热模组设置目录缓存（含适配类型反射缓存）并同步非标准模组热键
                        KnownModAdapters.SyncExternalNonConfigHotkeysOnBoot();
                        ModCatalogCache.Prewarm();
                        _stage = 2;
                        return;

                    case 2:
                        // 第 3 步：预建未激活态下的改造件界面骨架与行对象池，消除首开创建卡顿
                        ModUIManagerPerfHelper.PrewarmPanel();
                        _stage = 3;
                        _framesWaited = 0;
                        return;

                    case 3:
                        // 短暂让出主菜单数帧，再静默预建 ConflictScanner 冲突缓存（如果玩家未先点开改造件）
                        if (_framesWaited < 12)
                        {
                            _framesWaited++;
                            return;
                        }
                        ConflictScannerPerfHelper.PrewarmConflictEngine();
                        _stage = 4;
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Idle warmup step {_stage} warning: {ex.Message}");
            }

            if (_instance == this)
            {
                _instance = null;
            }
            Destroy(gameObject);
        }
    }

    // =========================================================================
    // 3. 改造件面板（ModUIManager）行对象池化、O(1) 依赖索引与布局批量挂起
    // =========================================================================

    internal static class ModUIManagerPerfHelper
    {
        private static readonly Type? RowType;
        private static readonly FieldInfo? RowModField;
        private static readonly FieldInfo? RowElementField;
        private static readonly FieldInfo? RowWarningIconField;
        private static readonly FieldInfo? RowWarningTooltipField;

        static ModUIManagerPerfHelper()
        {
            Type? rowType = AccessTools.TypeByName("ModUIManager+Row");
            RowType = rowType;
            if (rowType != null)
            {
                RowModField = AccessTools.Field(rowType, "mod");
                RowElementField = AccessTools.Field(rowType, "element");
                RowWarningIconField = AccessTools.Field(rowType, "warningIcon");
                RowWarningTooltipField = AccessTools.Field(rowType, "warningTooltip");
            }
        }

        private static readonly FieldInfo? RowsField = AccessTools.Field(typeof(ModUIManager), "rows");
        private static readonly FieldInfo? HandleField = AccessTools.Field(typeof(ModUIManager), "handle");
        private static readonly FieldInfo? FilterField = AccessTools.Field(typeof(ModUIManager), "filter");
        private static readonly FieldInfo? DependenciesContainerField = AccessTools.Field(typeof(ModUIManager), "dependenciesContainer");

        private static readonly MethodInfo? CreateRowMethod = AccessTools.Method(typeof(ModUIManager), "CreateRow", new Type[] { typeof(LoadedMod), typeof(Transform) });
        private static readonly Action<ModUIManager>? EnsureBuiltFn = CreateMethodDelegate<Action<ModUIManager>>("EnsureBuilt");
        private static readonly Action<ModUIManager>? RefreshRowIssuesFn = CreateMethodDelegate<Action<ModUIManager>>("RefreshRowIssues");
        private static readonly Func<LoadedMod, string?>? GetModIssueFn = CreateMethodDelegate<Func<LoadedMod, string?>>("GetModIssue", new Type[] { typeof(LoadedMod) });
        private static readonly Action<ModUIManager>? ApplyFilterToRowsFn = CreateMethodDelegate<Action<ModUIManager>>("ApplyFilterToRows");
        private static readonly Action<ModUIManager, string?>? SelectModFn = CreateMethodDelegate<Action<ModUIManager, string?>>("SelectMod", new Type[] { typeof(string) });
        private static readonly Func<ModUIManager, LoadedMod, Transform, object?>? CreateRowFn = CreateMethodDelegate<Func<ModUIManager, LoadedMod, Transform, object?>>("CreateRow", new Type[] { typeof(LoadedMod), typeof(Transform) });

        // ModUIManager 私有静态纯工具方法缓存
        private static readonly Func<LoadedMod, string>? LoadErrorLineFn = CreateMethodDelegate<Func<LoadedMod, string>>("LoadErrorLine");
        private static readonly Func<LoadedMod, string?>? TrustIssueFn = CreateMethodDelegate<Func<LoadedMod, string?>>("TrustIssue");
        private static readonly Func<LoadedMod, List<string>>? DeprecatedCapabilitiesFn = CreateMethodDelegate<Func<LoadedMod, List<string>>>("DeprecatedCapabilities");
        private static readonly Func<string, bool>? VersionMatchesGameFn = CreateMethodDelegate<Func<string, bool>>("VersionMatchesGame");

        // 缓存活跃与未启用列表容器的 LayoutGroup / ContentSizeFitter，避免筛选按键与刷新时重复 GetComponents
        private static ModUIManager? _cachedLayoutManager;
        private static readonly List<Behaviour> _cachedActiveContainerLayouts = new List<Behaviour>(4);
        private static readonly List<Behaviour> _cachedInactiveContainerLayouts = new List<Behaviour>(4);
        private static readonly List<Behaviour> _suspendedLayoutsStack = new List<Behaviour>(12);

        // 每次 PopulateLayoutList / RefreshRowIssues 期间生效的零拷贝 Mod 索引字典
        private static readonly Dictionary<string, LoadedMod> ScopedModLookup = new Dictionary<string, LoadedMod>(StringComparer.Ordinal);
        private static int _scopedLookupDepth;

        // ShowMod(null) 空状态缓存：记录已显示为空面板的 ModUIManager + CustomUIWindow 组合，避免重复触发 ScriptFontSwap 与 ClearContainer
        private static ModUIManager? _lastEmptyManager;
        private static CustomUIWindow? _lastEmptyHandle;

        // ModUIManager 行预热标记（每个 ModUIManager 实例仅预建一次行池）
        private static ModUIManager? _prewarmedManager;

        private static T? CreateMethodDelegate<T>(string methodName, Type[]? paramTypes = null) where T : Delegate
        {
            try
            {
                MethodInfo? mi = paramTypes != null
                    ? AccessTools.Method(typeof(ModUIManager), methodName, paramTypes)
                    : AccessTools.Method(typeof(ModUIManager), methodName);
                if (mi != null)
                {
                    return (T)Delegate.CreateDelegate(typeof(T), mi);
                }
            }
            catch { }
            return null;
        }

        internal static void ResetCaches()
        {
            ScopedModLookup.Clear();
            _scopedLookupDepth = 0;
            _lastEmptyManager = null;
            _lastEmptyHandle = null;
            _prewarmedManager = null;
            _cachedLayoutManager = null;
            _cachedActiveContainerLayouts.Clear();
            _cachedInactiveContainerLayouts.Clear();
            _suspendedLayoutsStack.Clear();
        }

        internal static void MarkNotEmpty(ModUIManager manager)
        {
            if (_lastEmptyManager == manager)
            {
                _lastEmptyManager = null;
                _lastEmptyHandle = null;
            }
        }

        internal static bool ShouldSkipEmptyShowMod(ModUIManager manager, LoadedMod? mod)
        {
            if (mod != null)
            {
                MarkNotEmpty(manager);
                return false;
            }

            CustomUIWindow? handle = HandleField?.GetValue(manager) as CustomUIWindow;
            RectTransform? depsRt = DependenciesContainerField?.GetValue(manager) as RectTransform;
            if (handle == null || !handle.IsAlive)
            {
                return false;
            }

            if (_lastEmptyManager == manager && _lastEmptyHandle == handle && (depsRt == null || depsRt.childCount == 0))
            {
                string? activateText = handle.Get("activate")?.GetText();
                if (string.Equals(activateText, LocHelper.GetLocalizedUI("ui_mods_activate"), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void RecordEmptyShowMod(ModUIManager manager, LoadedMod? mod)
        {
            if (mod == null)
            {
                _lastEmptyManager = manager;
                _lastEmptyHandle = HandleField?.GetValue(manager) as CustomUIWindow;
            }
            else
            {
                MarkNotEmpty(manager);
            }
        }

        internal static void BeginScopedModLookup(List<LoadedMod>? mods = null)
        {
            _scopedLookupDepth++;
            if (_scopedLookupDepth == 1)
            {
                RebuildScopedLookup(mods ?? ModLoader.Mods);
            }
        }

        internal static void EndScopedModLookup()
        {
            if (_scopedLookupDepth > 0)
            {
                _scopedLookupDepth--;
                if (_scopedLookupDepth == 0)
                {
                    ScopedModLookup.Clear();
                }
            }
        }

        private static void RebuildScopedLookup(List<LoadedMod>? mods)
        {
            ScopedModLookup.Clear();
            if (mods == null)
            {
                return;
            }

            for (int i = 0; i < mods.Count; i++)
            {
                LoadedMod m = mods[i];
                string? id = m?.Manifest?.ID;
                if (!string.IsNullOrEmpty(id) && !ScopedModLookup.ContainsKey(id!))
                {
                    ScopedModLookup[id!] = m!;
                }
            }
        }

        internal static bool TryComputeModIssueFast(LoadedMod? mod, out string? issue)
        {
            issue = null;
            if (mod == null || _scopedLookupDepth <= 0)
            {
                return false;
            }

            if (LoadErrorLineFn == null || TrustIssueFn == null || DeprecatedCapabilitiesFn == null || VersionMatchesGameFn == null)
            {
                return false;
            }

            List<string>? issues = null;

            if (mod.LoadError != null)
            {
                (issues ??= new List<string>()).Add(LoadErrorLineFn(mod));
            }

            string? trustIssue = TrustIssueFn(mod);
            if (trustIssue != null)
            {
                (issues ??= new List<string>()).Add(trustIssue);
            }

            if (mod.UnlistedDlls != null && mod.UnlistedDlls.Count > 0)
            {
                for (int i = 0; i < mod.UnlistedDlls.Count; i++)
                {
                    string unlistedDll = mod.UnlistedDlls[i];
                    (issues ??= new List<string>()).Add(LocHelper.GetLocalizedUIOrDefault(
                        "ui_mods_issue_unlisted_dll",
                        "{0} is in the mod folder but not listed in its manifest and was not loaded",
                        unlistedDll));
                }
            }

            List<string> deprecatedCaps = DeprecatedCapabilitiesFn(mod);
            if (deprecatedCaps != null && deprecatedCaps.Count > 0)
            {
                string[] displayNames = new string[deprecatedCaps.Count];
                for (int i = 0; i < deprecatedCaps.Count; i++)
                {
                    displayNames[i] = ModCapability.DisplayName(deprecatedCaps[i]);
                }
                (issues ??= new List<string>()).Add(LocHelper.GetLocalizedUIOrDefault(
                    "ui_mods_issue_deprecated_capability",
                    "Uses capabilities the next game update refuses ({0}). The mod author needs to update it.",
                    string.Join(", ", displayNames)));
            }

            if (mod.Manifest != null &&
                !ModTrustList.WORKSHOP_VERIFIED_ONLY_CAPABILITIES &&
                ModTrustList.NeedsVerificationFor(mod.Source, mod.Manifest.Capabilities, mod.ListVerdict).Count > 0)
            {
                (issues ??= new List<string>()).Add(LocHelper.GetLocalizedUIOrDefault(
                    "ui_mods_issue_needs_verification",
                    "Uses files or the internet and is not verified by the developers. From the next game update such Workshop mods only load once verified; the mod author needs to request it."));
            }

            if (mod.Manifest?.Prerequisites != null)
            {
                for (int i = 0; i < mod.Manifest.Prerequisites.Count; i++)
                {
                    string prereqId = mod.Manifest.Prerequisites[i];
                    if (string.IsNullOrEmpty(prereqId) || !ScopedModLookup.TryGetValue(prereqId, out LoadedMod? dep) || dep == null)
                    {
                        (issues ??= new List<string>()).Add(LocHelper.GetLocalizedUI("ui_mods_issue_missing", prereqId));
                    }
                    else if (!ModLoader.IsModEnabled(dep.Manifest.ID))
                    {
                        (issues ??= new List<string>()).Add(LocHelper.GetLocalizedUI("ui_mods_issue_inactive", dep.Manifest.Name));
                    }
                    else if (dep.LoadError != null)
                    {
                        (issues ??= new List<string>()).Add(LoadErrorLineFn(dep));
                    }
                }
            }

            if (mod.Manifest?.GameVersions != null && mod.Manifest.GameVersions.Count > 0)
            {
                bool matchesAny = false;
                for (int i = 0; i < mod.Manifest.GameVersions.Count; i++)
                {
                    if (VersionMatchesGameFn(mod.Manifest.GameVersions[i]))
                    {
                        matchesAny = true;
                        break;
                    }
                }
                if (!matchesAny)
                {
                    (issues ??= new List<string>()).Add(LocHelper.GetLocalizedUI("ui_mods_issue_version", Application.version));
                }
            }

            issue = (issues != null && issues.Count > 0) ? string.Join("\n", issues) : null;
            return true;
        }

        /// <summary>
        /// 在主菜单闲置帧预先构造 ModUIManager UI 骨架与模组行对象池，不触发 OpenPanel 也不会错误触发第三方模组界面扫描钩子。
        /// </summary>
        internal static void PrewarmPanel()
        {
            ModUIManager? manager = ModUIManager.Instance;
            if (manager == null || manager.panel == null || manager.panel.activeSelf || manager.content == null || CustomUIManager.Instance == null)
            {
                return;
            }

            if (_prewarmedManager == manager)
            {
                return;
            }
            _prewarmedManager = manager;

            EnsureBuiltFn?.Invoke(manager);
            PopulateLayoutRowsPooled(manager, invokeFollowups: false);
        }

        internal static bool PopulateLayoutRowsPooled(ModUIManager manager, bool invokeFollowups = true)
        {
            if (manager.activeModLayoutObject == null || manager.inactiveModLayoutObject == null)
            {
                return true;
            }

            if (RowType == null || RowsField == null || RowModField == null || RowElementField == null || (CreateRowFn == null && CreateRowMethod == null))
            {
                return false;
            }

            if (!(RowsField.GetValue(manager) is IList rowList))
            {
                return false;
            }

            List<LoadedMod>? mods = ModLoader.Mods;
            if (mods == null)
            {
                return false;
            }

            BeginScopedModLookup(mods);
            int suspendedStart = SuspendManagerLayouts(manager);

            try
            {
                Transform activeTf = manager.activeModLayoutObject.transform;
                Transform inactiveTf = manager.inactiveModLayoutObject.transform;
                CustomUIManager? uiManager = CustomUIManager.Instance;

                // 1. 收集现有池化行对象
                var existingById = new Dictionary<string, object>(rowList.Count, StringComparer.Ordinal);
                var usedTransforms = new HashSet<Transform>();
                for (int i = 0; i < rowList.Count; i++)
                {
                    object? r = rowList[i];
                    if (r == null) continue;
                    LoadedMod? oldMod = RowModField.GetValue(r) as LoadedMod;
                    ModElement? oldEl = RowElementField.GetValue(r) as ModElement;
                    string? id = oldMod?.Manifest?.ID ?? oldEl?.ModID;
                    if (!string.IsNullOrEmpty(id) && oldEl != null && !existingById.ContainsKey(id!))
                    {
                        existingById[id!] = r;
                    }
                    else if (oldEl != null)
                    {
                        UnityEngine.Object.Destroy(oldEl.gameObject);
                    }
                }

                var rebuiltRows = new List<object>(mods.Count);
                int activeIndex = 0;
                int inactiveIndex = 0;

                // 2. 差分复用或按需新增行
                for (int i = 0; i < mods.Count; i++)
                {
                    LoadedMod mod = mods[i];
                    if (mod?.Manifest == null || string.IsNullOrEmpty(mod.Manifest.ID))
                    {
                        continue;
                    }

                    string modId = mod.Manifest.ID;
                    string modName = mod.Manifest.Name ?? modId;
                    bool active = ModLoader.IsModEnabled(modId);
                    Transform targetParent = active ? activeTf : inactiveTf;
                    int targetSiblingIndex = active ? activeIndex++ : inactiveIndex++;

                    object? rowObj;
                    if (existingById.TryGetValue(modId, out rowObj))
                    {
                        existingById.Remove(modId);
                        ModElement? elem = RowElementField.GetValue(rowObj) as ModElement;
                        if (elem != null)
                        {
                            RowModField.SetValue(rowObj, mod);

                            Transform tf = elem.transform;
                            if (tf.parent != targetParent)
                            {
                                tf.SetParent(targetParent, worldPositionStays: false);
                            }
                            if (tf.GetSiblingIndex() != targetSiblingIndex)
                            {
                                tf.SetSiblingIndex(targetSiblingIndex);
                            }

                            if (elem.ModID != modId ||
                                elem.ModName != modName ||
                                (elem.modNameTMP != null && elem.modNameTMP.text != modName))
                            {
                                elem.Init(modId, modName);
                            }

                            if (uiManager != null)
                            {
                                Transform? sourceIconTf = tf.Find("source_icon");
                                if (sourceIconTf != null)
                                {
                                    Image? sourceImg = sourceIconTf.GetComponent<Image>();
                                    Sprite? wantSprite = mod.Source == ModSource.Workshop ? uiManager.spriteSteam : uiManager.spriteFile;
                                    if (sourceImg != null && sourceImg.sprite != wantSprite)
                                    {
                                        sourceImg.sprite = wantSprite;
                                    }
                                }
                            }

                            usedTransforms.Add(tf);
                            rebuiltRows.Add(rowObj);
                            continue;
                        }
                    }

                    object? created = CreateRowFn != null
                        ? CreateRowFn(manager, mod, targetParent)
                        : CreateRowMethod?.Invoke(manager, new object[] { mod, targetParent });
                    if (created != null)
                    {
                        ModElement? createdEl = RowElementField.GetValue(created) as ModElement;
                        if (createdEl != null)
                        {
                            if (createdEl.transform.GetSiblingIndex() != targetSiblingIndex)
                            {
                                createdEl.transform.SetSiblingIndex(targetSiblingIndex);
                            }
                            usedTransforms.Add(createdEl.transform);
                        }
                        rebuiltRows.Add(created);
                    }
                }

                // 3. 清理已移除或残留的多余子对象
                foreach (object leftover in existingById.Values)
                {
                    ModElement? leftEl = RowElementField.GetValue(leftover) as ModElement;
                    if (leftEl != null)
                    {
                        UnityEngine.Object.Destroy(leftEl.gameObject);
                    }
                }

                CleanupStrayChildren(activeTf, usedTransforms);
                CleanupStrayChildren(inactiveTf, usedTransforms);

                rowList.Clear();
                for (int i = 0; i < rebuiltRows.Count; i++)
                {
                    rowList.Add(rebuiltRows[i]);
                }

                if (invokeFollowups)
                {
                    RefreshRowIssuesFn?.Invoke(manager);
                    ApplyFilterToRowsFn?.Invoke(manager);
                    SelectModFn?.Invoke(manager, null);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Pooled PopulateLayoutList fallback triggered: {ex.Message}");
                return false;
            }
            finally
            {
                RestoreLayoutComponentsFrom(suspendedStart);
                EndScopedModLookup();
            }
        }

        internal static bool RefreshRowIssuesFast(ModUIManager manager)
        {
            if (RowsField == null || RowModField == null || RowElementField == null || RowWarningIconField == null || RowWarningTooltipField == null)
            {
                return false;
            }

            if (!(RowsField.GetValue(manager) is IList rowList))
            {
                return false;
            }

            if (GetModIssueFn == null)
            {
                return false;
            }

            BeginScopedModLookup();
            try
            {
                for (int i = 0; i < rowList.Count; i++)
                {
                    object? row = rowList[i];
                    if (row == null) continue;
                    ModElement? el = RowElementField.GetValue(row) as ModElement;
                    if (el == null) continue;

                    LoadedMod? mod = RowModField.GetValue(row) as LoadedMod;
                    // 通过委托调用原方法使 Harmony Postfix（如 ConflictScanner）正常生效，内部由 Prefix 走 O(1) 索引字典
                    string? modIssue = mod != null ? GetModIssueFn(mod) : null;

                    GameObject? warningIcon = RowWarningIconField.GetValue(row) as GameObject;
                    bool hasIssue = modIssue != null;
                    if (warningIcon != null && warningIcon.activeSelf != hasIssue)
                    {
                        warningIcon.SetActive(hasIssue);
                    }

                    ModRowHoverTooltip? tooltip = RowWarningTooltipField.GetValue(row) as ModRowHoverTooltip;
                    if (tooltip != null && !string.Equals(tooltip.text, modIssue, StringComparison.Ordinal))
                    {
                        tooltip.text = modIssue;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                EndScopedModLookup();
            }
        }

        internal static bool ApplyFilterToRowsDiff(ModUIManager manager)
        {
            if (RowsField == null || RowElementField == null)
            {
                return false;
            }

            if (!(RowsField.GetValue(manager) is IList rowList))
            {
                return false;
            }

            int suspendedStart = SuspendManagerLayouts(manager);

            try
            {
                string filter = FilterField?.GetValue(manager) as string ?? string.Empty;
                bool hasFilter = !string.IsNullOrEmpty(filter);

                for (int i = 0; i < rowList.Count; i++)
                {
                    object? row = rowList[i];
                    if (row == null) continue;
                    ModElement? elem = RowElementField.GetValue(row) as ModElement;
                    if (elem == null) continue;

                    bool match = !hasFilter
                        || ContainsIgnoreCase(elem.ModName, filter)
                        || ContainsIgnoreCase(elem.ModID, filter);

                    GameObject go = elem.gameObject;
                    if (go.activeSelf != match)
                    {
                        go.SetActive(match);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                RestoreLayoutComponentsFrom(suspendedStart);
            }
        }

        private static bool ContainsIgnoreCase(string? haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CleanupStrayChildren(Transform container, HashSet<Transform> validChildren)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Transform child = container.GetChild(i);
                if (!validChildren.Contains(child))
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static int SuspendManagerLayouts(ModUIManager manager)
        {
            if (_cachedLayoutManager != manager)
            {
                _cachedLayoutManager = manager;
                _cachedActiveContainerLayouts.Clear();
                _cachedInactiveContainerLayouts.Clear();
                CollectLayoutComponents(manager.activeModLayoutObject, _cachedActiveContainerLayouts);
                CollectLayoutComponents(manager.inactiveModLayoutObject, _cachedInactiveContainerLayouts);
            }

            int startIndex = _suspendedLayoutsStack.Count;
            DisableCachedList(_cachedActiveContainerLayouts);
            DisableCachedList(_cachedInactiveContainerLayouts);
            return startIndex;
        }

        private static void CollectLayoutComponents(GameObject? container, List<Behaviour> target)
        {
            if (container == null) return;
            foreach (LayoutGroup lg in container.GetComponents<LayoutGroup>())
            {
                if (lg != null) target.Add(lg);
            }
            foreach (ContentSizeFitter fitter in container.GetComponents<ContentSizeFitter>())
            {
                if (fitter != null) target.Add(fitter);
            }
        }

        private static void DisableCachedList(List<Behaviour> cached)
        {
            for (int i = 0; i < cached.Count; i++)
            {
                Behaviour b = cached[i];
                if (b != null && b.enabled)
                {
                    b.enabled = false;
                    _suspendedLayoutsStack.Add(b);
                }
            }
        }

        private static void RestoreLayoutComponentsFrom(int startIndex)
        {
            for (int i = _suspendedLayoutsStack.Count - 1; i >= startIndex; i--)
            {
                Behaviour b = _suspendedLayoutsStack[i];
                if (b != null)
                {
                    b.enabled = true;
                    var rt = b.transform as RectTransform;
                    if (rt != null)
                    {
                        LayoutRebuilder.MarkLayoutForRebuild(rt);
                    }
                }
            }
            if (_suspendedLayoutsStack.Count > startIndex)
            {
                _suspendedLayoutsStack.RemoveRange(startIndex, _suspendedLayoutsStack.Count - startIndex);
            }
        }
    }

    [HarmonyPatch(typeof(ModUIManager), nameof(ModUIManager.PopulateLayoutList))]
    public static class Patch_ModUIManager_PopulateLayoutList
    {
        public static bool Prefix(ModUIManager __instance)
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return true;
            return !ModUIManagerPerfHelper.PopulateLayoutRowsPooled(__instance, invokeFollowups: true);
        }
    }

    [HarmonyPatch(typeof(ModUIManager), "RefreshRowIssues")]
    public static class Patch_ModUIManager_RefreshRowIssues
    {
        public static bool Prefix(ModUIManager __instance)
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return true;
            return !ModUIManagerPerfHelper.RefreshRowIssuesFast(__instance);
        }
    }

    [HarmonyPatch(typeof(ModUIManager), "GetModIssue")]
    public static class Patch_ModUIManager_GetModIssue
    {
        public static bool Prefix(LoadedMod mod, ref string? __result)
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return true;
            if (ModUIManagerPerfHelper.TryComputeModIssueFast(mod, out string? fastIssue))
            {
                __result = fastIssue;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ModUIManager), "ApplyFilterToRows")]
    public static class Patch_ModUIManager_ApplyFilterToRows
    {
        public static bool Prefix(ModUIManager __instance)
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return true;
            return !ModUIManagerPerfHelper.ApplyFilterToRowsDiff(__instance);
        }
    }

    [HarmonyPatch(typeof(ModUIManager), "ShowMod")]
    public static class Patch_ModUIManager_ShowMod
    {
        public static bool Prefix(ModUIManager __instance, LoadedMod? mod)
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return true;
            return !ModUIManagerPerfHelper.ShouldSkipEmptyShowMod(__instance, mod);
        }

        public static void Postfix(ModUIManager __instance, LoadedMod? mod)
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return;
            ModUIManagerPerfHelper.RecordEmptyShowMod(__instance, mod);
        }
    }

    // =========================================================================
    // 4. CustomUIWindow 缺失元素日志防护（仅在增强版游戏设置 UI 规则刷新期间接管，零日志刷屏）
    // =========================================================================

    [HarmonyPatch(typeof(CustomUIWindow), nameof(CustomUIWindow.Get), new Type[] { typeof(string) })]
    public static class Patch_CustomUIWindow_Get
    {
        public static bool Prefix(CustomUIWindow __instance, string elementId, ref CustomUIElement? __result)
        {
            if (UnifiedOptionUI.ShouldSilenceMissingElementWarning(__instance, elementId, out CustomUIElement? found))
            {
                __result = found;
                return false;
            }
            return true;
        }
    }

    // =========================================================================
    // 5. 改造件 (ModUIManager.OpenPanel) × ConflictScanner 双重阻塞扫描去重与缓存
    // =========================================================================

    [HarmonyPatch(typeof(ModUIManager), nameof(ModUIManager.OpenPanel))]
    public static class Patch_ModUIManager_OpenPanel_ConflictScope
    {
        public static void Prefix()
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return;
            ConflictScannerPerfHelper.BeginModOpenPanel();
        }

        public static void Finalizer()
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return;
            ConflictScannerPerfHelper.EndModOpenPanel();
        }
    }

    internal static class ConflictScannerPerfHelper
    {
        private static int _modOpenPanelDepth = 0;
        private static bool _bound = false;
        private static string? _lastBuiltModSignature = null;
        private static Action<bool>? _ensureBuiltFn;
        private static Func<int>? _getBuildCountFn;
        private static readonly System.Text.StringBuilder SigBuilder = new System.Text.StringBuilder(1024);

        internal static void ResetSession()
        {
            _modOpenPanelDepth = 0;
            _lastBuiltModSignature = null;
        }

        internal static void BeginModOpenPanel()
        {
            _modOpenPanelDepth++;
        }

        internal static void EndModOpenPanel()
        {
            if (_modOpenPanelDepth > 0)
            {
                _modOpenPanelDepth--;
            }
        }

        internal static void Bind(Assembly conflictScannerAssembly, Harmony harmony, ModLog? log)
        {
            if (_bound || conflictScannerAssembly == null || harmony == null)
            {
                return;
            }
            _bound = true;

            try
            {
                Type? engineType = conflictScannerAssembly.GetType("ConflictScanner.ConflictEngine", throwOnError: false);
                if (engineType == null)
                {
                    return;
                }

                MethodInfo? ensureBuiltMethod = AccessTools.Method(engineType, "EnsureBuilt", new Type[] { typeof(bool) });
                MethodInfo? invalidateMethod = AccessTools.Method(engineType, "Invalidate");
                MethodInfo? getBuildCountMethod = AccessTools.PropertyGetter(engineType, "BuildCount");

                if (ensureBuiltMethod != null)
                {
                    try { _ensureBuiltFn = Delegate.CreateDelegate(typeof(Action<bool>), ensureBuiltMethod) as Action<bool>; }
                    catch { _ensureBuiltFn = null; }
                }
                if (getBuildCountMethod != null)
                {
                    try { _getBuildCountFn = Delegate.CreateDelegate(typeof(Func<int>), getBuildCountMethod) as Func<int>; }
                    catch { _getBuildCountFn = null; }
                }

                if (invalidateMethod != null)
                {
                    MethodInfo invPrefix = AccessTools.Method(typeof(ConflictScannerPerfHelper), nameof(ConflictEngine_Invalidate_Prefix));
                    harmony.Patch(invalidateMethod, prefix: new HarmonyMethod(invPrefix));
                }
                if (ensureBuiltMethod != null)
                {
                    MethodInfo ebPrefix = AccessTools.Method(typeof(ConflictScannerPerfHelper), nameof(ConflictEngine_EnsureBuilt_Prefix));
                    MethodInfo ebPostfix = AccessTools.Method(typeof(ConflictScannerPerfHelper), nameof(ConflictEngine_EnsureBuilt_Postfix));
                    harmony.Patch(ensureBuiltMethod, prefix: new HarmonyMethod(ebPrefix), postfix: new HarmonyMethod(ebPostfix));
                }

                log?.Log("Bound ConflictScanner.ConflictEngine dedup & memoization patches");
            }
            catch (Exception ex)
            {
                log?.Warning($"[ConflictScannerPerfHelper] Failed to bind ConflictEngine: {ex.Message}");
            }
        }

        internal static void PrewarmConflictEngine()
        {
            if (!_bound || _ensureBuiltFn == null)
            {
                return;
            }
            try
            {
                int count = _getBuildCountFn != null ? _getBuildCountFn() : 0;
                if (count == 0)
                {
                    _ensureBuiltFn(false);
                }
            }
            catch { }
        }

        internal static string ComputeModScanSignature()
        {
            SigBuilder.Length = 0;
            List<LoadedMod>? mods = ModLoader.Mods;
            int asmCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            SigBuilder.Append(asmCount).Append('|').Append(mods != null ? mods.Count : 0).Append('|');

            if (mods != null)
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    LoadedMod m = mods[i];
                    if (m == null)
                    {
                        SigBuilder.Append("null;");
                        continue;
                    }
                    string id = m.Manifest?.ID ?? string.Empty;
                    string ver = m.Manifest?.ModVersion ?? string.Empty;
                    bool loadedEnabled = m.IsEnabled;
                    bool persistedEnabled = !string.IsNullOrEmpty(id) && ModLoader.IsModEnabled(id);
                    SigBuilder.Append(id)
                        .Append(':')
                        .Append(ver)
                        .Append(':')
                        .Append(loadedEnabled ? '1' : '0')
                        .Append(':')
                        .Append(persistedEnabled ? '1' : '0')
                        .Append(':')
                        .Append(m.Assembly != null ? '1' : '0')
                        .Append(':')
                        .Append(string.IsNullOrEmpty(m.LoadError) ? '0' : '1')
                        .Append(';');
                }
            }

            return SigBuilder.ToString();
        }

        private static bool HasValidCachedBuildForCurrentEnv()
        {
            if (string.IsNullOrEmpty(_lastBuiltModSignature))
            {
                return false;
            }
            int buildCount = _getBuildCountFn != null ? _getBuildCountFn() : 0;
            if (buildCount <= 0)
            {
                return false;
            }
            return string.Equals(ComputeModScanSignature(), _lastBuiltModSignature, StringComparison.Ordinal);
        }

        public static bool ConflictEngine_Invalidate_Prefix()
        {
            if (_modOpenPanelDepth > 0 && HasValidCachedBuildForCurrentEnv())
            {
                // 同一模组环境在 ModUIManager.OpenPanel 中不再重复清空已有冲突分析结果（消除两次全量 DseCecil DLL 解析）
                return false;
            }
            _lastBuiltModSignature = null;
            return true;
        }

        public static bool ConflictEngine_EnsureBuilt_Prefix()
        {
            if (_modOpenPanelDepth > 0 && HasValidCachedBuildForCurrentEnv())
            {
                return false;
            }
            return true;
        }

        public static void ConflictEngine_EnsureBuilt_Postfix()
        {
            _lastBuiltModSignature = ComputeModScanSignature();
        }
    }
}
