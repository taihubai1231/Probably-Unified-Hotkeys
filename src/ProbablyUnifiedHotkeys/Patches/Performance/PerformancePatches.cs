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
    /// 全局性能与流畅度底层优化补丁：
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
        /// 在游戏退出前幂等执行：安全刷新落盘配置并将各模组的 HarmonyInstance 置空，
        /// 从而使官方 ModLoader.OnApplicationQuit 自动跳过耗时数秒的数十次 UnpatchAll 重编译，
        /// 同时避免手动调用各模组 OnDisable 触发模组内部的 UnpatchAll，达成真正的极速秒退。
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
                PrefsStorage.Save();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[PerformancePatches] PlayerPrefs.Save failed: {ex.Message}");
            }

            try
            {
                List<LoadedMod>? mods = ModLoader.Mods;
                if (mods != null)
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        LoadedMod mod = mods[i];
                        if (mod != null)
                        {
                            mod.HarmonyInstance = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[PerformancePatches] FastQuit clear HarmonyInstance warning: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // 1. 退出游戏秒退优化补丁
    // =========================================================================

    [HarmonyPatch(typeof(MainMenuUIController), nameof(MainMenuUIController.OnExitClick))]
    public static class Patch_MainMenuUIController_OnExitClick
    {
        public static bool Prefix()
        {
            if (!ModFeatureConfig.EnableFastQuit) return true;
            PerformancePatches.PrepareFastQuit();
            Application.Quit();
            return false;
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

            PerformancePatches.PrepareFastQuit();

            IsPendingTransitionField?.SetValue(__instance, true);
            __instance.OnAnyClick();
            Application.Quit();
            return false;
        }
    }

    [HarmonyPatch(typeof(EscapeUIManager), nameof(EscapeUIManager.OnMainMenu))]
    public static class Patch_EscapeUIManager_OnMainMenu
    {
        private static readonly FieldInfo? IsPendingTransitionField =
            AccessTools.Field(typeof(EscapeUIManager), "isPendingTransition");

        public static bool Prefix(EscapeUIManager __instance)
        {
            if (!ModFeatureConfig.EnableFastQuit && !ModFeatureConfig.EnablePerformancePatches)
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
            IsPendingTransitionField?.SetValue(__instance, true);

            __instance.OnAnyClick();

            try
            {
                EmporiumAnalytics.TrackLeaveToMainMenu();
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[Patch_EscapeUIManager_OnMainMenu] TrackLeaveToMainMenu warning: {ex.Message}");
            }

            // 零等待执行原版重置与场景切换，消除原版 MainMenuWithAnalytics 中 1.0s 无效 WaitForSecondsRealtime 阻塞！
            try
            {
                if (NewGameData.Instance != null)
                {
                    NewGameData.Instance.isLoadFromMainMenu = true;
                }
                PlayerStore.Instance?.Reset();
                EmporiumEntry.ResetData();
                PlayerStore.ResetData();
                StoreStation.ResetData();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_EscapeUIManager_OnMainMenu] Reset game state warning: {ex.Message}");
            }

            UnityEngine.SceneManagement.SceneManager.LoadScene("EmporiumMenu", UnityEngine.SceneManagement.LoadSceneMode.Single);
            return false;
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
            try
            {
                KnownModAdapters.SyncExternalNonConfigHotkeysOnBoot();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_MainMenuUIController_Start] Failed to sync external hotkeys on boot: {ex.Message}");
            }

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
                ModLogger.Warn($"[PerformancePatches] Idle warmup step {_stage} warning: {ex.Message}");
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
            catch (Exception ex)
            {
                ModLogger.Debug($"[ModUIManagerPerfHelper] Failed to bind delegate for {methodName}: {ex.Message}");
            }
            return null;
        }

        private static readonly FieldInfo? PreviewCacheField = AccessTools.Field(typeof(ModUIManager), "previewCache");
        private static readonly Dictionary<string, Sprite> PersistentPreviewCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Func<ModUIManager, LoadedMod, Sprite?>? GetLocalPreviewFn =
            CreateMethodDelegate<Func<ModUIManager, LoadedMod, Sprite?>>("GetLocalPreview", new Type[] { typeof(LoadedMod) });
        private static int _warmupModIndex = 0;
        private static ModUIManager? _lastPopulatedManager;
        private static string? _lastPopulatedEnvSignature;
        private static bool _hasCompletedFullPopulate = false;
        private static readonly System.Text.StringBuilder PopulateSigBuilder = new System.Text.StringBuilder(1024);

        internal static bool IsPreviewWarmupComplete(int totalCount) => _warmupModIndex >= totalCount;

        internal static void WarmupNextLocalPreviews(ModUIManager manager, int count = 2)
        {
            if (manager == null || GetLocalPreviewFn == null || ModLoader.Mods == null) return;
            List<LoadedMod> mods = ModLoader.Mods;
            int processed = 0;
            while (_warmupModIndex < mods.Count && processed < count)
            {
                LoadedMod m = mods[_warmupModIndex++];
                if (m?.Manifest != null && !string.IsNullOrEmpty(m.Manifest.ID))
                {
                    string id = m.Manifest.ID;
                    if (!PersistentPreviewCache.ContainsKey(id))
                    {
                        try
                        {
                            Sprite? local = GetLocalPreviewFn(manager, m);
                            if (local != null)
                            {
                                PersistentPreviewCache[id] = local;
                            }
                        }
                        catch
                        {
                            // ignore preview warmup errors
                        }
                    }
                }
                processed++;
            }
        }

        internal static bool TryGetFastPreview(ModUIManager manager, LoadedMod? mod, out Sprite? sprite)
        {
            sprite = null;
            if (mod?.Manifest == null || string.IsNullOrEmpty(mod.Manifest.ID)) return false;
            string id = mod.Manifest.ID;

            // 1. 优先从跨场景/会话全局持久缓存中命中（0ms 内存秒出）
            if (PersistentPreviewCache.TryGetValue(id, out Sprite? cached) && cached != null)
            {
                sprite = cached;
                return true;
            }

            // 2. 查原生 ModUIManager.previewCache 字典
            Dictionary<string, Sprite>? nativeCache = PreviewCacheField?.GetValue(manager) as Dictionary<string, Sprite>;
            if (nativeCache != null && nativeCache.TryGetValue(id, out Sprite? nativeCached) && nativeCached != null)
            {
                PersistentPreviewCache[id] = nativeCached;
                sprite = nativeCached;
                return true;
            }

            // 3. 极速从本地 Mod 目录 preview.png 载入（零网络请求、零挂起，1-2ms 快速解码）
            if (GetLocalPreviewFn != null)
            {
                try
                {
                    Sprite? local = GetLocalPreviewFn(manager, mod);
                    if (local != null)
                    {
                        PersistentPreviewCache[id] = local;
                        if (nativeCache != null)
                        {
                            nativeCache[id] = local;
                        }
                        sprite = local;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"[ModUIManagerPerfHelper] GetLocalPreview failed for {id}: {ex.Message}");
                }
            }

            return false;
        }

        internal static void EnsurePreviewCached(ModUIManager manager, LoadedMod? mod)
        {
            if (mod?.Manifest == null || string.IsNullOrEmpty(mod.Manifest.ID)) return;
            string id = mod.Manifest.ID;
            if (PreviewCacheField != null && PreviewCacheField.GetValue(manager) is Dictionary<string, Sprite> cache)
            {
                if (cache.TryGetValue(id, out Sprite? sprite) && sprite != null)
                {
                    PersistentPreviewCache[id] = sprite;
                }
                else if (PersistentPreviewCache.TryGetValue(id, out Sprite? saved) && saved != null)
                {
                    cache[id] = saved;
                }
            }
        }

        internal static CustomUIWindow? GetHandle(ModUIManager manager)
        {
            return HandleField?.GetValue(manager) as CustomUIWindow;
        }

        internal static void LockDetailButtonSizes(ModUIManager? manager)
        {
            if (manager == null) return;
            CustomUIWindow? handle = GetHandle(manager);
            if (handle == null || !handle.IsAlive) return;

            CustomUIElement? activate = handle.Get("activate");
            CustomUIElement? options = handle.Get("settings") ?? handle.Get("options");
            CustomUIElement? steam = handle.Get("workshop_page");
            CustomUIElement? review = handle.Get("review");

            // 1. 锁定按钮自身控件尺寸（preferredHeight 42px、Square 图标 42x42px、flexibleHeight = 0f），杜绝自身被拉伸
            if (activate?.Rect != null)
            {
                activate.Rect.sizeDelta = new Vector2(activate.Rect.sizeDelta.x, 42f);
                LayoutElement le = activate.Rect.GetComponent<LayoutElement>() ?? activate.Rect.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 42f;
                le.preferredHeight = 42f;
                le.flexibleHeight = 0f;
            }
            if (options?.Rect != null)
            {
                options.Rect.sizeDelta = new Vector2(42f, 42f);
                LayoutElement le = options.Rect.GetComponent<LayoutElement>() ?? options.Rect.gameObject.AddComponent<LayoutElement>();
                le.minWidth = 42f;
                le.preferredWidth = 42f;
                le.minHeight = 42f;
                le.preferredHeight = 42f;
                le.flexibleWidth = 0f;
                le.flexibleHeight = 0f;
            }
            if (steam?.Rect != null)
            {
                steam.Rect.sizeDelta = new Vector2(42f, 42f);
                LayoutElement le = steam.Rect.GetComponent<LayoutElement>() ?? steam.Rect.gameObject.AddComponent<LayoutElement>();
                le.minWidth = 42f;
                le.preferredWidth = 42f;
                le.minHeight = 42f;
                le.preferredHeight = 42f;
                le.flexibleWidth = 0f;
                le.flexibleHeight = 0f;
            }
            if (review?.Rect != null)
            {
                review.Rect.sizeDelta = new Vector2(review.Rect.sizeDelta.x, 42f);
                LayoutElement le = review.Rect.GetComponent<LayoutElement>() ?? review.Rect.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 42f;
                le.preferredHeight = 42f;
                le.flexibleHeight = 0f;
            }

            // 2. 保持父 row 原版 childControlHeight = true，并将 childForceExpandHeight = false，杜绝按钮被拉伸；
            // 若父 row 挂有 LayoutElement，仅设 flexibleHeight = 0f，绝不触碰它的 preferredHeight 和 minHeight！
            RectTransform? rowRt = (activate?.Rect?.parent ?? options?.Rect?.parent ?? steam?.Rect?.parent) as RectTransform;
            if (rowRt != null)
            {
                HorizontalLayoutGroup? hlg = rowRt.GetComponent<HorizontalLayoutGroup>();
                if (hlg != null)
                {
                    hlg.childControlHeight = true;
                    hlg.childForceExpandHeight = false;
                    hlg.childAlignment = TextAnchor.MiddleLeft;
                }

                Transform? metaColTf = rowRt.childCount > 0 ? rowRt.GetChild(0) : null;
                if (metaColTf is RectTransform metaRt)
                {
                    ContentSizeFitter? csf = metaRt.GetComponent<ContentSizeFitter>();
                    if (csf != null)
                    {
                        UnityEngine.Object.Destroy(csf);
                    }
                }

                LayoutElement? rowLe = rowRt.GetComponent<LayoutElement>();
                if (rowLe != null)
                {
                    rowLe.flexibleHeight = 0f;
                    rowLe.minHeight = -1f;
                    rowLe.preferredHeight = -1f;
                }

                LayoutRebuilder.MarkLayoutForRebuild(rowRt);

                if (rowRt.parent is RectTransform detailColRt)
                {
                    LayoutRebuilder.MarkLayoutForRebuild(detailColRt);
                }
            }

            // 3. 锁定下方描述滚动区 ScrollRect 的 flexibleHeight = 1f，让其完整吸收隐藏封面释放出来的 345px 空间
            CustomUIElement? desc = handle.Get("detail_description");
            if (desc?.Rect != null)
            {
                ScrollRect? scrollRect = desc.Rect.GetComponentInParent<ScrollRect>();
                if (scrollRect != null)
                {
                    LayoutElement scrollLe = scrollRect.GetComponent<LayoutElement>() ?? scrollRect.gameObject.AddComponent<LayoutElement>();
                    scrollLe.flexibleHeight = 1f;
                    scrollLe.flexibleWidth = 1f;
                }
            }
        }

        private static string ComputePopulateEnvironmentSignature(ModUIManager manager, List<LoadedMod> mods)
        {
            PopulateSigBuilder.Length = 0;
            string locale = LocHelper.GetCurrentLocaleCode() ?? string.Empty;
            string filter = FilterField?.GetValue(manager) as string ?? string.Empty;
            PopulateSigBuilder.Append(locale).Append('|').Append(filter).Append('|').Append(mods.Count).Append('|');

            for (int i = 0; i < mods.Count; i++)
            {
                LoadedMod m = mods[i];
                if (m == null)
                {
                    PopulateSigBuilder.Append("null;");
                    continue;
                }
                string id = m.Manifest?.ID ?? string.Empty;
                bool active = !string.IsNullOrEmpty(id) && ModLoader.IsModEnabled(id);
                PopulateSigBuilder.Append(id)
                    .Append(':')
                    .Append(active ? '1' : '0')
                    .Append(':')
                    .Append(string.IsNullOrEmpty(m.LoadError) ? '0' : '1')
                    .Append(';');
            }
            return PopulateSigBuilder.ToString();
        }

        private static bool IsPopulateEnvironmentUnchanged(ModUIManager manager, List<LoadedMod> mods, IList rowList)
        {
            if (!_hasCompletedFullPopulate || string.IsNullOrEmpty(_lastPopulatedEnvSignature) || _lastPopulatedManager != manager)
            {
                return false;
            }
            if (rowList.Count == 0)
            {
                return false;
            }
            if (manager.activeModLayoutObject == null || manager.inactiveModLayoutObject == null)
            {
                return false;
            }
            string currentSig = ComputePopulateEnvironmentSignature(manager, mods);
            return string.Equals(currentSig, _lastPopulatedEnvSignature, StringComparison.Ordinal);
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
            _lastPopulatedManager = null;
            _lastPopulatedEnvSignature = null;
            _hasCompletedFullPopulate = false;
            _warmupModIndex = 0;
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

        internal static void PrewarmPanelInstance(ModUIManager? manager)
        {
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
            LockDetailButtonSizes(manager);
            PopulateLayoutRowsPooled(manager, invokeFollowups: false);
            RefreshRowIssuesFast(manager);
            ApplyFilterToRowsDiff(manager);
            _hasCompletedFullPopulate = true;
            _lastPopulatedManager = manager;
            if (ModLoader.Mods != null)
            {
                _lastPopulatedEnvSignature = ComputePopulateEnvironmentSignature(manager, ModLoader.Mods);
            }
        }

        /// <summary>
        /// 在主菜单闲置帧预先构造 ModUIManager UI 骨架与模组行对象池，不触发 OpenPanel 也不会错误触发第三方模组界面扫描钩子。
        /// </summary>
        internal static void PrewarmPanel()
        {
            PrewarmPanelInstance(ModUIManager.Instance);
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

            if (IsPopulateEnvironmentUnchanged(manager, mods, rowList))
            {
                return true;
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
                    _hasCompletedFullPopulate = true;
                }

                _lastPopulatedManager = manager;
                _lastPopulatedEnvSignature = ComputePopulateEnvironmentSignature(manager, mods);
                LockDetailButtonSizes(manager);

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[ModUIManagerPerfHelper] Pooled PopulateLayoutList fallback triggered: {ex.Message}");
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
            catch (Exception ex)
            {
                ModLogger.Warn($"[ModUIManagerPerfHelper] RefreshRowIssuesFast failed: {ex.Message}");
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
            catch (Exception ex)
            {
                ModLogger.Warn($"[ModUIManagerPerfHelper] ApplyFilterToRowsDiff failed: {ex.Message}");
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

    [HarmonyPatch(typeof(ModUIManager), "Awake")]
    public static class Patch_ModUIManager_Awake
    {
        public static void Postfix(ModUIManager __instance)
        {
            if (!ModFeatureConfig.EnablePerformancePatches || __instance == null) return;
            try
            {
                // 统一交由 ModUIPrewarmRunner 延后至主菜单渲染完毕后的空闲帧分步执行，杜绝 Awake 首帧卡顿
                ModUIPrewarmRunner.Attach(__instance);
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[Patch_ModUIManager_Awake] Prewarm on Awake warning: {ex.Message}");
            }
        }
    }

    internal sealed class ModUIPrewarmRunner : MonoBehaviour
    {
        private ModUIManager? _manager;
        private int _framesWaited = 0;
        private bool _prewarmed = false;

        internal static void Attach(ModUIManager manager)
        {
            if (manager == null || manager.gameObject == null) return;
            var runner = manager.gameObject.GetComponent<ModUIPrewarmRunner>();
            if (runner == null)
            {
                runner = manager.gameObject.AddComponent<ModUIPrewarmRunner>();
            }
            runner._manager = manager;
            runner._framesWaited = 0;
            runner._prewarmed = false;
        }

        private void Update()
        {
            _framesWaited++;
            // 闲置等待：让出主菜单前 5 帧，让场景和首屏 UI 丝滑呈现
            if (_framesWaited < 5)
            {
                return;
            }

            if (_manager == null || _manager.panel == null || _manager.content == null || CustomUIManager.Instance == null)
            {
                if (_framesWaited > 60 || (_manager != null && _manager.panel != null && _manager.panel.activeSelf))
                {
                    Destroy(this);
                }
                return;
            }

            if (!_prewarmed)
            {
                _prewarmed = true;
                try
                {
                    ModUIManagerPerfHelper.PrewarmPanelInstance(_manager);
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"[ModUIPrewarmRunner] Prewarm deferred warning: {ex.Message}");
                }
                return;
            }

            // 预建 UI 骨架后，每空闲帧轻量解码 2 个未缓存模组的本地封面进 PersistentPreviewCache
            try
            {
                ModUIManagerPerfHelper.WarmupNextLocalPreviews(_manager, 2);
            }
            catch
            {
                // ignore preview warmup errors
            }

            if (ModLoader.Mods != null && ModUIManagerPerfHelper.IsPreviewWarmupComplete(ModLoader.Mods.Count))
            {
                Destroy(this);
            }
            else if (_framesWaited > 120)
            {
                Destroy(this);
            }
        }
    }

    [HarmonyPatch(typeof(CustomUIManager), "Start")]
    public static class Patch_CustomUIManager_Start
    {
        public static void Postfix()
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return;
            try
            {
                ModUIManager? mgr = ModUIManager.Instance;
                if (mgr != null && mgr.panel != null && !mgr.panel.activeSelf && mgr.content != null)
                {
                    ModUIPrewarmRunner.Attach(mgr);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[Patch_CustomUIManager_Start] Prewarm warning: {ex.Message}");
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

    [HarmonyPatch(typeof(ModUIManager), "GetPreview")]
    public static class Patch_ModUIManager_GetPreview
    {
        public static bool Prefix(ModUIManager __instance, LoadedMod? mod, ref Sprite? __result)
        {
            if (!ModFeatureConfig.EnablePerformancePatches || __instance == null || mod?.Manifest == null || string.IsNullOrEmpty(mod.Manifest.ID))
            {
                return true;
            }

            if (ModUIManagerPerfHelper.TryGetFastPreview(__instance, mod, out Sprite? sprite))
            {
                __result = sprite;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ModUIManager), "ApplyPreview")]
    public static class Patch_ModUIManager_ApplyPreview
    {
        public static bool Prefix(ModUIManager __instance, LoadedMod? mod)
        {
            if (ModFeatureConfig.EnablePerformancePatches)
            {
                ModUIManagerPerfHelper.EnsurePreviewCached(__instance, mod);
                ModUIManagerPerfHelper.LockDetailButtonSizes(__instance);
            }

            CustomUIWindow? handle = ModUIManagerPerfHelper.GetHandle(__instance);
            CustomUIElement? previewPanel = handle?.Get("preview_panel");
            CustomUIElement? previewElem = handle?.Get("preview");

            // 本模组专属去封面优化（老板诉求 #3）：隐藏封面预览面板 preview_panel，玩家无需滚动即可直接清晰通读顶部文字介绍
            if (mod?.Manifest != null && string.Equals(mod.Manifest.ID, ModFeatureConfig.ModId, StringComparison.Ordinal))
            {
                if (previewElem != null)
                {
                    previewElem.SetSprite((Sprite?)null);
                }
                if (previewPanel != null)
                {
                    previewPanel.SetVisible(false);
                    if (previewPanel.Rect?.parent is RectTransform parentRt)
                    {
                        LayoutRebuilder.MarkLayoutForRebuild(parentRt);
                    }
                }
                ModUIManagerPerfHelper.LockDetailButtonSizes(__instance);
                return false; // 跳过原版 ApplyPreview，保持 preview_panel 隐藏且按钮零变形
            }
            else
            {
                // 重要防串图：在加载/显示新模组封面之前，先清空旧模组残留在 preview 控件上的 Sprite！
                // 严禁在此处无脑 SetVisible(true)！应由后续原版 ApplyPreview 根据 GetPreview 结果自主决定显隐！
                if (previewElem != null)
                {
                    previewElem.SetSprite((Sprite?)null);
                }
            }

            return true;
        }

        public static void Postfix(ModUIManager __instance)
        {
            if (ModFeatureConfig.EnablePerformancePatches)
            {
                ModUIManagerPerfHelper.LockDetailButtonSizes(__instance);
            }
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
            if (!ModFeatureConfig.EnablePerformancePatches && (mod?.Manifest == null || !string.Equals(mod.Manifest.ID, ModFeatureConfig.ModId, StringComparison.Ordinal))) return;
            ModUIManagerPerfHelper.RecordEmptyShowMod(__instance, mod);

            if (mod?.Manifest != null && string.Equals(mod.Manifest.ID, ModFeatureConfig.ModId, StringComparison.Ordinal))
            {
                CustomUIWindow? handle = ModUIManagerPerfHelper.GetHandle(__instance);
                CustomUIElement? previewPanel = handle?.Get("preview_panel");
                if (previewPanel != null && previewPanel.Rect != null && previewPanel.Rect.gameObject.activeSelf)
                {
                    previewPanel.SetVisible(false);
                    if (previewPanel.Rect.parent is RectTransform parentRt)
                    {
                        LayoutRebuilder.MarkLayoutForRebuild(parentRt);
                    }
                }
            }
            // 切换到其他模组时，绝对不要在此处强行 previewPanel.SetVisible(true)！
            // 原版 ApplyPreview 会根据 GetPreview 结果正确调用 SetVisible(true) 或 SetVisible(false)。

            ModUIManagerPerfHelper.LockDetailButtonSizes(__instance);
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
            var bridge = OptionUiHost.Active;
            if (bridge != null && bridge.ShouldSilenceMissingElementWarning(__instance, elementId, out CustomUIElement? found))
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
        public static void Prefix(ModUIManager __instance)
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return;
            ConflictScannerPerfHelper.BeginModOpenPanel();
        }

        public static void Postfix(ModUIManager __instance)
        {
            if (!ModFeatureConfig.EnablePerformancePatches) return;
            ModUIManagerPerfHelper.LockDetailButtonSizes(__instance);
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
        private static bool _hasCompletedAtLeastOneBuild = false;
        private static bool _hasReportedForCurrentEnv = false;
        private static string? _lastBuiltModSignature = null;
        private static Action<bool>? _ensureBuiltFn;
        private static readonly System.Text.StringBuilder SigBuilder = new System.Text.StringBuilder(1024);

        internal static void ResetSession()
        {
            _modOpenPanelDepth = 0;
            _hasCompletedAtLeastOneBuild = false;
            _hasReportedForCurrentEnv = false;
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

                if (ensureBuiltMethod != null)
                {
                    try { _ensureBuiltFn = Delegate.CreateDelegate(typeof(Action<bool>), ensureBuiltMethod) as Action<bool>; }
                    catch (Exception ex) { ModLogger.Debug($"[ConflictScannerPerfHelper] Failed to bind _ensureBuiltFn: {ex.Message}"); _ensureBuiltFn = null; }
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

                Type? modType = conflictScannerAssembly.GetType("ConflictScanner.ConflictScannerMod", throwOnError: false);
                if (modType != null)
                {
                    MethodInfo? reportMethod = AccessTools.Method(modType, "Report");
                    if (reportMethod != null)
                    {
                        MethodInfo repPrefix = AccessTools.Method(typeof(ConflictScannerPerfHelper), nameof(ConflictScannerMod_Report_Prefix));
                        harmony.Patch(reportMethod, prefix: new HarmonyMethod(repPrefix));
                    }
                }

                log?.Log("Bound ConflictScanner.ConflictEngine dedup & memoization patches");
            }
            catch (Exception ex)
            {
                log?.Warning($"[ConflictScannerPerfHelper] Failed to bind ConflictEngine: {ex.Message}");
                ModLogger.Warn($"[ConflictScannerPerfHelper] Failed to bind ConflictEngine: {ex.Message}");
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
                if (!_hasCompletedAtLeastOneBuild || !HasValidCachedBuildForCurrentEnv())
                {
                    _ensureBuiltFn(false);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[ConflictScannerPerfHelper] PrewarmConflictEngine warning: {ex.Message}");
            }
        }

        internal static string ComputeModScanSignature()
        {
            SigBuilder.Length = 0;
            List<LoadedMod>? mods = ModLoader.Mods;
            SigBuilder.Append(mods != null ? mods.Count : 0).Append('|');

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
            if (!_hasCompletedAtLeastOneBuild || string.IsNullOrEmpty(_lastBuiltModSignature))
            {
                return false;
            }
            return string.Equals(ComputeModScanSignature(), _lastBuiltModSignature, StringComparison.Ordinal);
        }

        public static bool ConflictScannerMod_Report_Prefix()
        {
            if (_hasReportedForCurrentEnv && HasValidCachedBuildForCurrentEnv())
            {
                // 当前模组环境签名已上报且无变动，跳过重复打印 "Scan: 53 DLL(s) read"
                return false;
            }
            _hasReportedForCurrentEnv = true;
            return true;
        }

        public static bool ConflictEngine_Invalidate_Prefix()
        {
            if (HasValidCachedBuildForCurrentEnv())
            {
                // 当前模组环境签名未变（启用状态与版本未变），直接跳过清空已有冲突分析结果，杜绝重复扫描！
                return false;
            }
            _hasCompletedAtLeastOneBuild = false;
            _hasReportedForCurrentEnv = false;
            _lastBuiltModSignature = null;
            return true;
        }

        public static bool ConflictEngine_EnsureBuilt_Prefix()
        {
            if (HasValidCachedBuildForCurrentEnv())
            {
                // 当前模组环境签名未变，直接命中缓存跳过 53-DLL 耗时重扫！
                return false;
            }
            return true;
        }

        public static void ConflictEngine_EnsureBuilt_Postfix()
        {
            _hasCompletedAtLeastOneBuild = true;
            _lastBuiltModSignature = ComputeModScanSignature();
        }
    }
}
