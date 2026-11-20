using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    // =========================================================================
    // 0. 全新统一按键与模组设置中心 UI 挂接补丁
    // =========================================================================

    /// <summary>
    /// 接管原版游戏设置窗口的打开流程：
    /// 当 OptionUiHost.Active 存在时打开增强版统一设置中心；
    /// 当 OptionUiHost.Active 为 null 时安全降级放行原版选项窗口。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.OpenUI))]
    public static class Patch_GameOptionUIManager_OpenUI
    {
        public static bool Prefix(GameOptionUIManager __instance)
        {
            try
            {
                var bridge = OptionUiHost.Active;
                if (bridge != null && bridge.TryOpenEnhancedUI(__instance))
                {
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"[Patch_GameOptionUIManager_OpenUI] Error in enhanced OpenUI, falling back to vanilla", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// 拦截设置界面关闭：当处于按键录制期间或缓冲帧时压制关闭；当存在未保存更改时弹出防丢确认提示框。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.CloseUI))]
    public static class Patch_GameOptionUIManager_CloseUI
    {
        public static bool Prefix(GameOptionUIManager __instance)
        {
            try
            {
                var bridge = OptionUiHost.Active;
                if (bridge != null)
                {
                    if (bridge.IsCapturingAnyKey || bridge.IsSuppressingEscapeAfterCapture)
                    {
                        return false;
                    }

                    if (bridge.IsEnhancedWindowOpen && bridge.HasUnsavedChanges() && !bridge.IsExplicitWindowClosing)
                    {
                        bridge.PromptUnsavedChangesBeforeClosing();
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"[Patch_GameOptionUIManager_CloseUI] Error in CloseUI unsaved check", ex);
            }
            return true;
        }
    }

    /// <summary>
    /// 当处于按键录入状态或刚完成录入的缓冲帧时，告知商店 UI 本帧已消费 Esc，防止商店 UI 误触发 ResolveEscape 退出设置。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.ConsumedEscapeThisFrame))]
    public static class Patch_GameOptionUIManager_ConsumedEscapeThisFrame
    {
        public static void Postfix(ref bool __result)
        {
            if (__result) return;
            var bridge = OptionUiHost.Active;
            if (bridge != null && (bridge.IsCapturingAnyKey || bridge.IsSuppressingEscapeAfterCapture))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// 当处于按键录入状态或刚完成录入的缓冲帧时，阻止 CustomUIManager 处理 Esc，防止按 Esc 清空键位时误关闭顶层窗口；
    /// 并在选项窗口存在未保存更改时拦截 Esc，提示未保存防丢确认弹窗。
    /// </summary>
    [HarmonyPatch(typeof(CustomUIManager), "HandleEscape")]
    public static class Patch_CustomUIManager_HandleEscape
    {
        private static readonly FieldInfo? WindowOrderField = AccessTools.Field(typeof(CustomUIManager), "windowOrder");

        public static bool Prefix(CustomUIManager __instance)
        {
            var bridge = OptionUiHost.Active;
            if (bridge != null)
            {
                if (bridge.IsCapturingAnyKey || bridge.IsSuppressingEscapeAfterCapture)
                {
                    return false;
                }

                if (bridge.IsEnhancedWindowOpen && !bridge.IsExplicitWindowClosing && KeyBindHelper.GetBackDown())
                {
                    var windowOrder = WindowOrderField?.GetValue(__instance) as List<CustomUIWindow>;
                    if (windowOrder != null)
                    {
                        CustomUIWindow? topWin = null;
                        for (int i = windowOrder.Count - 1; i >= 0; i--)
                        {
                            var win = windowOrder[i];
                            if (win != null && win.IsAlive && win.IsVisible && win.closeOnEscape)
                            {
                                topWin = win;
                                break;
                            }
                        }

                        if (topWin != null && topWin == bridge.UnsavedConfirmWindow)
                        {
                            return true;
                        }

                        if (topWin != null && topWin == bridge.CurrentWindow)
                        {
                            if (bridge.HasUnsavedChanges())
                            {
                                bridge.MarkEscapeConsumed();
                                bridge.PromptUnsavedChangesBeforeClosing();
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 隔离原版捕获轮询，避免修改模组快捷键时触发原版 KeyBindHelper 干扰。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "PollKeyCapture")]
    public static class Patch_GameOptionUIManager_PollKeyCapture
    {
        public static bool Prefix()
        {
            var bridge = OptionUiHost.Active;
            return bridge == null || !bridge.IsCapturingModHotkey;
        }
    }

    /// <summary>
    /// 驱动模组按键捕获逻辑。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "Update")]
    public static class Patch_GameOptionUIManager_Update
    {
        public static void Postfix()
        {
            try
            {
                OptionUiHost.Active?.TickCapture();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_GameOptionUIManager_Update] Error ticking capture: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 同步原版按键显示色与全局冲突指示（防止原版改键/重置后颜色或冲突标记过期），并在规则刷新期间接管缺失模组备注告警。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "RefreshRules")]
    public static class Patch_GameOptionUIManager_RefreshRules
    {
        public static void Prefix()
        {
            var bridge = OptionUiHost.Active;
            if (bridge != null && bridge.IsEnhancedWindowOpen)
            {
                bridge.IsInEnhancedRefreshRules = true;
            }
        }

        public static void Postfix()
        {
            try
            {
                OptionUiHost.Active?.OnVanillaRulesRefreshed();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_GameOptionUIManager_RefreshRules] Error in OnVanillaRulesRefreshed: {ex.Message}");
            }
        }

        public static void Finalizer()
        {
            var bridge = OptionUiHost.Active;
            if (bridge != null)
            {
                bridge.IsInEnhancedRefreshRules = false;
            }
        }
    }

    [HarmonyPatch(typeof(GameOptionUIManager), "RefreshKeyBindButton")]
    public static class Patch_GameOptionUIManager_RefreshKeyBindButton
    {
        public static void Postfix()
        {
            try
            {
                OptionUiHost.Active?.OnVanillaKeyChanged();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_GameOptionUIManager_RefreshKeyBindButton] Error in OnVanillaKeyChanged: {ex.Message}");
            }
        }
    }

    internal static class ModConfigUiHelper
    {
        public static void ApplyEntryLocalization(CustomUIBuilder builder, ModConfig config, ConfigEntry entry)
        {
            if (config == null || entry == null) return;
            if (!string.Equals(config.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase)) return;
            if (!string.Equals(entry.Key, I18n.LanguageConfigKey, StringComparison.Ordinal)) return;

            var dropdownEl = builder?.Last;
            if (dropdownEl == null || !dropdownEl.IsAlive) return;

            dropdownEl.SetOptions(I18n.GetLanguageChoices());
            dropdownEl.SetIndex(I18n.LanguageToIndex(entry.BoxedValue));
            I18n.ApplyFontToDropdown(dropdownEl, forceCjkCapableList: true);
        }

        public static void ApplyWindowFonts(CustomUIWindow? window)
        {
            if (window == null || !window.IsAlive) return;

            var sectionEl = window.Get("section_" + I18n.SectionHeader);
            if (sectionEl?.text != null)
            {
                I18n.ApplyFontIfRuntime(sectionEl.text, sectionEl.text.text);
            }

            var labelEl = window.Get("label_" + I18n.LanguageConfigKey);
            if (labelEl?.text != null)
            {
                I18n.ApplyFontIfRuntime(labelEl.text, labelEl.text.text);
            }

            var dropEl = window.Get(I18n.LanguageConfigKey);
            I18n.ApplyFontToDropdown(dropEl, forceCjkCapableList: true);
        }

        public static void SyncOpenModConfigWindow(CustomUIWindow? window)
        {
            if (window == null || !window.IsAlive) return;

            string link = "modcfg:" + I18n.ModId + ":" + I18n.LanguageConfigKey;
            string labelText = $"{I18n.Get("界面语言", "UI Language")} <link=\"{link}\"><sprite name=\"info\" tint=1></link>";
            var labelEl = window.Get("label_" + I18n.LanguageConfigKey);
            labelEl?.SetText(labelText);

            var dropEl = window.Get(I18n.LanguageConfigKey);
            if (dropEl != null && dropEl.IsAlive)
            {
                dropEl.SetOptions(I18n.GetLanguageChoices());
                dropEl.SetIndex(I18n.LanguageToIndex(I18n.GetConfiguredMode()));
            }

            ApplyWindowFonts(window);
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "BuildEntry")]
    public static class Patch_ModConfigWindow_BuildEntry
    {
        public static void Postfix(CustomUIBuilder b, ModConfig config, ConfigEntry entry)
        {
            try
            {
                ModConfigUiHelper.ApplyEntryLocalization(b, config, entry);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_ModConfigWindow_BuildEntry] Error applying entry localization: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
    public static class Patch_ModConfigWindow_Open
    {
        public static void Postfix(LoadedMod mod, CustomUIWindow __result)
        {
            try
            {
                if (mod?.Manifest != null &&
                    string.Equals(mod.Manifest.ID, I18n.ModId, StringComparison.OrdinalIgnoreCase) &&
                    __result != null)
                {
                    ModConfigUiHelper.ApplyWindowFonts(__result);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_ModConfigWindow_Open] Error applying window fonts: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "Apply")]
    public static class Patch_ModConfigWindow_Apply
    {
        public static void Postfix(CustomUIWindow window, ModConfig config)
        {
            try
            {
                if (config != null &&
                    string.Equals(config.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase) &&
                    window != null && window.IsAlive)
                {
                    ModConfigUiHelper.SyncOpenModConfigWindow(window);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_ModConfigWindow_Apply] Error syncing mod config window: {ex.Message}");
            }
        }
    }

    // =========================================================================
    // 1. 原版游戏设置界面按键捕获补丁
    // =========================================================================

    /// <summary>
    /// 运行时快捷键判定共享辅助器（支持 Shift/Ctrl/Alt 组合键与鼠标侧键 Mouse3~Mouse6）。
    /// </summary>
    internal static class KeyBindRuntimeHelper
    {
        internal static bool EvaluateKeyBind(string id, bool held)
        {
            if (KeyBindHelper.SuppressHotkeys)
            {
                return false;
            }

            KeyCode key = KeyBindHelper.Get(id);
            if (key == KeyCode.None)
            {
                return false;
            }

            ComboKeyStore.EnsureLoaded();
            KeyCode effective = key;
            ComboKeyStore.FilterRuntimeKey(ref effective, "vanilla." + id, id);
            if (effective == KeyCode.None)
            {
                return false;
            }

            return held ? ComboKeyStore.IsKeyHeldRaw(effective) : ComboKeyStore.IsKeyDownRaw(effective);
        }

        internal static bool TryPollMouseButtons(out KeyCode pressed)
        {
            // Mouse1 (Right Click) -> KeyCode.Mouse1
            if (ComboKeyStore.IsKeyDownRaw(KeyCode.Mouse1))
            {
                pressed = KeyCode.Mouse1;
                return true;
            }
            // Mouse2 (Middle Click) -> KeyCode.Mouse2
            if (ComboKeyStore.IsKeyDownRaw(KeyCode.Mouse2))
            {
                pressed = KeyCode.Mouse2;
                return true;
            }
            // Mouse3 ~ Mouse6 (Side Buttons)
            for (int code = (int)KeyCode.Mouse3; code <= (int)KeyCode.Mouse6; code++)
            {
                KeyCode candidate = (KeyCode)code;
                if (ComboKeyStore.IsKeyDownRaw(candidate))
                {
                    pressed = candidate;
                    return true;
                }
            }
            pressed = KeyCode.None;
            return false;
        }

        internal static bool TryPollSideMouseButton(out KeyCode pressed)
        {
            return TryPollMouseButtons(out pressed);
        }
    }

    /// <summary>
    /// 允许捕获空按键（Esc 解除绑定）并支持直接捕获鼠标右键（Mouse1）、中键（Mouse2）、侧键（Mouse3~Mouse6）及 Backspace。
    /// 仅在无修饰键单击鼠标左键（Mouse0）时取消录入。
    /// </summary>
    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.PollCapture))]
    public static class Patch_KeyBindHelper_PollCapture
    {
        private static readonly FieldInfo? ConsumedEscapeFrameField = AccessTools.Field(typeof(GameOptionUIManager), "consumedEscapeFrame");

        public static bool Prefix(ref KeyBindHelper.CaptureResult __result, ref KeyCode key)
        {
            if (!KeyBindHelper.IsCapturing)
            {
                return true;
            }

            // 优先轮询鼠标右键、中键与侧键（Mouse1 ~ Mouse6），支持直接录制绑定
            if (KeyBindRuntimeHelper.TryPollMouseButtons(out KeyCode mouseBtn))
            {
                key = mouseBtn;
                __result = KeyBindHelper.CaptureResult.Key;
                return false;
            }

            // 无修饰键单击鼠标左键（Mouse0）：取消录入并保持原键位
            if (Input.GetMouseButtonDown(0) && ComboKeyStore.GetHeldModifiersMask() == KeyModifierMask.None)
            {
                __result = KeyBindHelper.CaptureResult.None;
                return false;
            }

            if (!Input.anyKeyDown)
            {
                return true;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ConsumedEscapeFrameField?.SetValue(null, Time.frameCount);
                key = KeyCode.None;
                __result = KeyBindHelper.CaptureResult.Key;
                return false;
            }

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                key = KeyCode.Backspace;
                __result = KeyBindHelper.CaptureResult.Key;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 增强原版 KeyName 输出，将 Mouse0~Mouse6 友好显示为“鼠标左键”、“鼠标右键”、“鼠标中键”、“鼠标侧键1~4”。
    /// </summary>
    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.KeyName))]
    public static class Patch_KeyBindHelper_KeyName
    {
        public static void Postfix(KeyCode key, ref string __result)
        {
            if (ComboKeyStore.IsMouseSideButton(key) || key == KeyCode.Mouse0 || key == KeyCode.Mouse1 || key == KeyCode.Mouse2)
            {
                __result = ComboKeyStore.FormatPrimaryKeyName(key);
            }
        }
    }

    /// <summary>
    /// 让原版 KeyBindHelper.GetKeyDown / GetKey 在游戏运行时识别组合键与鼠标侧键。
    /// </summary>
    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.GetKeyDown))]
    public static class Patch_KeyBindHelper_GetKeyDown
    {
        public static bool Prefix(string id, ref bool __result)
        {
            __result = KeyBindRuntimeHelper.EvaluateKeyBind(id, held: false);
            return false;
        }
    }

    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.GetKey))]
    public static class Patch_KeyBindHelper_GetKey
    {
        public static bool Prefix(string id, ref bool __result)
        {
            __result = KeyBindRuntimeHelper.EvaluateKeyBind(id, held: true);
            return false;
        }
    }

    /// <summary>
    /// 修复空按键的冲突判定：KeyCode.None 代表未绑定/空按键，永不与其他按键冲突。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "FindPendingKeyOwner")]
    public static class Patch_GameOptionUIManager_FindPendingKeyOwner
    {
        public static bool Prefix(KeyCode key, ref string? __result)
        {
            if (key == KeyCode.None)
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 接管原版按键录入，走增强统一捕获状态机（支持 Shift/Ctrl/Alt 组合键、Alt+左键、鼠标侧键及 Esc 解绑）。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "StartCapture", new Type[] { typeof(string) })]
    public static class Patch_GameOptionUIManager_StartCapture
    {
        public static bool Prefix(GameOptionUIManager __instance, string optionId)
        {
            try
            {
                var bridge = OptionUiHost.Active;
                if (bridge != null && bridge.StartCaptureVanillaOption(__instance, optionId))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_GameOptionUIManager_StartCapture] Error routing vanilla key capture: {ex.Message}");
            }
            return true;
        }
    }
}
