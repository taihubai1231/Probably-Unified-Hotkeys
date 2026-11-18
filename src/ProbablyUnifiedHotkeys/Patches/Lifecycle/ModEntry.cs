using System;
using HarmonyLib;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 模组主入口生命周期管理器：
    /// 实现官方原生 IMod 契约（Init, OnEnable, OnDisable），
    /// 统一按序装配底座服务、组合键监听、工坊模组 Harmony 补丁与极速秒退监听。
    /// </summary>
    public class ModEntry : IMod
    {
        private static ModLog? log;
        private static Harmony? workshopHarmony;
        private static volatile bool _isApplicationQuitting = false;
        private static bool _quitHookRegistered = false;
        private static bool _perfPatchesHookRegistered = false;
        private static bool _disabledDuringQuit = false;

        public void Init(ModManifest manifest)
        {
            _isApplicationQuitting = false;
            _disabledDuringQuit = false;
            PerformancePatches.ResetSessionState();

            if (!_quitHookRegistered)
            {
                _quitHookRegistered = true;
                Application.wantsToQuit += OnApplicationWantsToQuit;
                Application.quitting += OnApplicationQuitting;
            }

            log = new ModLog(manifest);
            log.Log($"Initialized v{manifest.ModVersion}");

            ComboKeyStore.ExternalBypassPredicate = () =>
                (OptionUiHost.Active?.IsEnhancedWindowOpen ?? false) ||
                (OptionUiHost.Active?.IsCapturingModHotkey ?? false);

            ComboKeyStore.EnsureLoaded();
            I18n.EnsureRegistered();
            ModFeatureConfig.EnsureRegistered();
            ModCatalogCache.EnsureEventSubscriptions();

            if (!_perfPatchesHookRegistered)
            {
                _perfPatchesHookRegistered = true;
                ModFeatureConfig.PerformancePatchesToggled += PerformancePatches.ResetSessionState;
            }

            workshopHarmony = new Harmony("probably_unified_hotkeys.workshop");
            WorkshopModPatcher.Apply(workshopHarmony, log);
        }

        internal static bool IsApplicationQuitting => _isApplicationQuitting;

        internal static void MarkApplicationQuitting()
        {
            _isApplicationQuitting = true;
        }

        internal static void ResetApplicationQuittingForTest()
        {
            _isApplicationQuitting = false;
        }

        private static bool OnApplicationWantsToQuit()
        {
            _isApplicationQuitting = true;
            PerformancePatches.PrepareFastQuit();
            return true;
        }

        private static void OnApplicationQuitting()
        {
            _isApplicationQuitting = true;
            PerformancePatches.PrepareFastQuit();
        }

        public void OnEnable()
        {
            log?.Log("Enabled: Unified hotkey combos, mouse side buttons & unbinding active across vanilla and workshop mods.");
        }

        public void OnDisable()
        {
            if (_isApplicationQuitting && _disabledDuringQuit)
            {
                return;
            }

            try
            {
                if (_isApplicationQuitting)
                {
                    _disabledDuringQuit = true;
                    return;
                }

                if (_quitHookRegistered)
                {
                    _quitHookRegistered = false;
                    Application.wantsToQuit -= OnApplicationWantsToQuit;
                    Application.quitting -= OnApplicationQuitting;
                }

                if (_perfPatchesHookRegistered)
                {
                    _perfPatchesHookRegistered = false;
                    ModFeatureConfig.PerformancePatchesToggled -= PerformancePatches.ResetSessionState;
                }

                OptionUiHost.Active?.ResetSessionState();
                PerformancePatches.ResetSessionState();
                ModCatalogCache.Invalidate();
                GameOptionRegistry.RemoveBySource("probably_unified_hotkeys");
                GameOptionRegistry.RemoveBySource("allow_unbind_key");
                workshopHarmony?.UnpatchAll("probably_unified_hotkeys.workshop");
            }
            catch (Exception ex)
            {
                log?.Warning($"Error unpatching workshop mods: {ex.Message}");
                ModLogger.Warn($"[ModEntry] Error unpatching workshop mods: {ex.Message}");
            }
            log?.Log("Disabled.");
        }
    }
}
