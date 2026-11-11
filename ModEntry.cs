using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    public class ModEntry : IMod
    {
        private static ModLog? log;
        private static Harmony? workshopHarmony;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            log.Log($"Initialized v{manifest.ModVersion}");

            ComboKeyStore.EnsureLoaded();
            workshopHarmony = new Harmony("probably_unified_hotkeys.workshop");
            WorkshopModPatcher.Apply(workshopHarmony, log);
        }

        public void OnEnable()
        {
            log?.Log("Enabled: Unified hotkey combos, mouse side buttons & unbinding active across vanilla and workshop mods.");
        }

        public void OnDisable()
        {
            try
            {
                GameOptionRegistry.RemoveBySource("probably_unified_hotkeys");
                GameOptionRegistry.RemoveBySource("allow_unbind_key");
                ModCatalogCache.Invalidate();
                workshopHarmony?.UnpatchAll("probably_unified_hotkeys.workshop");
            }
            catch (Exception ex)
            {
                log?.Warning($"Error unpatching workshop mods: {ex.Message}");
            }
            log?.Log("Disabled.");
        }
    }

    // =========================================================================
    // 0. 全新统一按键与模组设置中心 UI 挂接补丁
    // =========================================================================

    /// <summary>
    /// 接管原版游戏设置窗口的打开流程，注入统一设置与改键中心
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.OpenUI))]
    public static class Patch_GameOptionUIManager_OpenUI
    {
        public static bool Prefix(GameOptionUIManager __instance)
        {
            try
            {
                UnifiedOptionUI.OpenEnhancedUI(__instance);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProbablyUnifiedHotkeys] Error in enhanced OpenUI, falling back to vanilla: {ex}");
                return true;
            }
        }
    }

    /// <summary>
    /// 隔离原版捕获轮询，避免修改模组快捷键时触发原版 KeyBindHelper 干扰
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "PollKeyCapture")]
    public static class Patch_GameOptionUIManager_PollKeyCapture
    {
        public static bool Prefix()
        {
            return !UnifiedOptionUI.IsCapturingModHotkey;
        }
    }

    /// <summary>
    /// 驱动模组按键捕获逻辑
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "Update")]
    public static class Patch_GameOptionUIManager_Update
    {
        public static void Postfix()
        {
            try
            {
                UnifiedOptionUI.TickCapture();
            }
            catch { }
        }
    }

    /// <summary>
    /// 同步原版按键显示色与全局冲突指示（防止原版改键/重置后颜色或冲突标记过期）
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "RefreshRules")]
    public static class Patch_GameOptionUIManager_RefreshRules
    {
        public static void Postfix()
        {
            try
            {
                UnifiedOptionUI.OnVanillaRulesRefreshed();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GameOptionUIManager), "RefreshKeyBindButton")]
    public static class Patch_GameOptionUIManager_RefreshKeyBindButton
    {
        public static void Postfix()
        {
            try
            {
                UnifiedOptionUI.OnVanillaKeyChanged();
            }
            catch { }
        }
    }

    // =========================================================================
    // 1. 原版游戏设置界面按键捕获补丁
    // =========================================================================

    /// <summary>
    /// 运行时快捷键判定共享辅助器（支持 Shift/Ctrl/Alt 组合键与鼠标侧键 Mouse3~Mouse6）
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

        internal static bool TryPollSideMouseButton(out KeyCode pressed)
        {
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
    }

    /// <summary>
    /// 允许捕获空按键（Esc 解除绑定）并支持直接捕获鼠标侧键（Mouse3~Mouse6）及 Backspace
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

            // Unity 的 Input.anyKeyDown 有时会遗漏鼠标侧键，优先轮询 Mouse3~Mouse6
            if (KeyBindRuntimeHelper.TryPollSideMouseButton(out KeyCode sideBtn))
            {
                key = sideBtn;
                __result = KeyBindHelper.CaptureResult.Key;
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
    /// 增强原版 KeyName 输出，将 Mouse3~Mouse6 友好显示为“鼠标侧键1~4”
    /// </summary>
    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.KeyName))]
    public static class Patch_KeyBindHelper_KeyName
    {
        public static void Postfix(KeyCode key, ref string __result)
        {
            if (ComboKeyStore.IsMouseSideButton(key))
            {
                __result = ComboKeyStore.FormatPrimaryKeyName(key);
            }
        }
    }

    /// <summary>
    /// 让原版 KeyBindHelper.GetKeyDown / GetKey 在游戏运行时识别组合键与鼠标侧键
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
    /// 修复空按键的冲突判定：KeyCode.None 代表未绑定/空按键，永不与其他按键冲突
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
    /// 接管原版按键录入，走增强统一捕获状态机（支持 Shift/Ctrl/Alt 组合键、Alt+左键、鼠标侧键及 Esc 解绑）
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "StartCapture", new Type[] { typeof(string) })]
    public static class Patch_GameOptionUIManager_StartCapture
    {
        public static bool Prefix(GameOptionUIManager __instance, string optionId)
        {
            try
            {
                if (UnifiedOptionUI.StartCaptureVanillaOption(__instance, optionId))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error routing vanilla key capture: {ex.Message}");
            }
            return true;
        }
    }

    // =========================================================================
    // 2. 创意工坊主流模组的空按键兼容与支持
    // =========================================================================

    public static class WorkshopModPatcher
    {
        private static readonly HashSet<string> PatchedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<MethodBase, string[]> PenQoLGetterCandidates = new Dictionary<MethodBase, string[]>();

        public static void Apply(Harmony harmony, ModLog? log)
        {
            // 对官方泛型 ModConfig 热键（ConfigEntry<KeyCode>.Value）挂接运行时组合键过滤
            PatchConfigEntryKeyCode(harmony, log);

            // 扫描当前已加载的程序集
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                TryPatch(assembly, harmony, log);
            }

            // 监听后续动态加载的模组程序集
            AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
            {
                TryPatch(args.LoadedAssembly, harmony, log);
            };
        }

        private static void PatchConfigEntryKeyCode(Harmony harmony, ModLog? log)
        {
            try
            {
                PropertyInfo? valueProp = AccessTools.Property(typeof(ConfigEntry<KeyCode>), nameof(ConfigEntry<KeyCode>.Value));
                MethodInfo? getter = valueProp?.GetGetMethod(true);
                if (getter != null)
                {
                    MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ConfigEntryKeyCode_GetValue_Postfix));
                    harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                    log?.Log("Patched ConfigEntry<KeyCode>.Value getter (combo key runtime filter)");
                }
            }
            catch (Exception ex)
            {
                log?.Warning($"[WorkshopModPatcher] Failed to patch ConfigEntry<KeyCode>.Value: {ex.Message}");
            }
        }

        public static void ConfigEntryKeyCode_GetValue_Postfix(ConfigEntry<KeyCode> __instance, ref KeyCode __result)
        {
            if (__result == KeyCode.None || ComboKeyStore.IsBypassed || __instance == null) return;
            ComboKeyStore.EnsureLoaded();
            string? modId = __instance.Config?.ModId;
            string fullId = string.IsNullOrEmpty(modId) ? __instance.Key : $"{modId}.{__instance.Key}";
            ComboKeyStore.FilterRuntimeKey(ref __result, fullId, __instance.Key);
        }

        private static void TryPatch(Assembly assembly, Harmony harmony, ModLog? log)
        {
            string? name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name)) return;

            lock (PatchedAssemblies)
            {
                if (PatchedAssemblies.Contains(name)) return;
                PatchedAssemblies.Add(name);
            }

            try
            {
                if (name == "DuojiHotkeyManager")
                {
                    PatchDuojiHotkeyManager(harmony, log);
                }
                else if (name == "ConflictScanner")
                {
                    PatchConflictScanner(harmony, log);
                }
                else if (name == "ShowItemNames")
                {
                    PatchShowItemNames(harmony, log);
                }
                else if (name == "SmartInventoryArrange.Mono")
                {
                    PatchSmartInventoryArrange(harmony, log);
                }
                else if (name == "FactionReputationExpansion")
                {
                    PatchFactionReputationExpansion(harmony, log);
                }
                else if (name == "Pen_mod_QoL_Tweaks")
                {
                    PatchPenQoL(harmony, log);
                }
                else if (name == "ClientDemandHighlight")
                {
                    PatchClientDemandHighlight(harmony, log);
                }
            }
            catch (Exception ex)
            {
                log?.Warning($"[WorkshopModPatcher] Failed to patch mod '{name}': {ex.Message}");
            }
        }

        // --- 多吉改键器 (DuojiHotkeyManager) ---
        private static void PatchDuojiHotkeyManager(Harmony harmony, ModLog? log)
        {
            Type? hotkeyMgrType = AccessTools.TypeByName("DuojiHotkeyManager.HotkeyManager");
            if (hotkeyMgrType != null)
            {
                MethodInfo? tickCapture = AccessTools.Method(hotkeyMgrType, "TickCapture");
                if (tickCapture != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(Duoji_TickCapture_Prefix));
                    harmony.Patch(tickCapture, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched DuojiHotkeyManager.HotkeyManager.TickCapture");
                }

                MethodInfo? getCaptureText = AccessTools.Method(hotkeyMgrType, "get_CaptureTargetText");
                if (getCaptureText != null)
                {
                    MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(Duoji_GetCaptureTargetText_Postfix));
                    harmony.Patch(getCaptureText, postfix: new HarmonyMethod(postfix));
                }

                _duojiGetToggleKeysMethod = AccessTools.PropertyGetter(hotkeyMgrType, "ToggleKeys");
                MethodInfo? toggleAnyDown = AccessTools.Method(
                    hotkeyMgrType,
                    "ToggleAnyDown",
                    new Type[] { typeof(KeyCode).MakeByRefType(), typeof(bool).MakeByRefType() });
                if (toggleAnyDown != null && _duojiGetToggleKeysMethod != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(Duoji_ToggleAnyDown_Prefix));
                    harmony.Patch(toggleAnyDown, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched DuojiHotkeyManager.HotkeyManager.ToggleAnyDown (combo & mouse side button support)");
                }
            }

            Type? keyTextType = AccessTools.TypeByName("DuojiHotkeyManager.KeyText");
            if (keyTextType != null)
            {
                MethodInfo? isDefined = AccessTools.Method(keyTextType, "IsDefinedKeyCode", new Type[] { typeof(int) });
                if (isDefined != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(Duoji_IsDefinedKeyCode_Prefix));
                    harmony.Patch(isDefined, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched DuojiHotkeyManager.KeyText.IsDefinedKeyCode (allows None/0)");
                }
            }
        }

        private static MethodInfo? _duojiGetToggleKeysMethod;

        public static bool Duoji_ToggleAnyDown_Prefix(ref KeyCode which, ref bool isFallback, ref bool __result)
        {
            which = KeyCode.None;
            isFallback = false;

            if (_duojiGetToggleKeysMethod == null)
            {
                return true;
            }

            if (_duojiGetToggleKeysMethod.Invoke(null, null) is List<KeyCode> keys && keys.Count > 0)
            {
                ComboKeyStore.EnsureLoaded();
                for (int i = 0; i < keys.Count; i++)
                {
                    KeyCode rawKey = keys[i];
                    KeyCode filtered = rawKey;
                    if (i == 0)
                    {
                        ComboKeyStore.FilterRuntimeKey(
                            ref filtered,
                            "duoji_hotkey_rebind:open_panel",
                            "duoji_hotkey_rebind.PanelHotkey",
                            "open_panel");
                    }

                    if (filtered != KeyCode.None && ComboKeyStore.IsKeyDownRaw(filtered))
                    {
                        which = rawKey;
                        isFallback = i > 0;
                        __result = true;
                        return false;
                    }
                }

                __result = false;
                return false;
            }

            return true;
        }

        public static bool Duoji_TickCapture_Prefix()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Type? hotkeyMgrType = AccessTools.TypeByName("DuojiHotkeyManager.HotkeyManager");
                if (hotkeyMgrType != null)
                {
                    MethodInfo? finishCapture = AccessTools.Method(hotkeyMgrType, "FinishCapture", new Type[] { typeof(string), typeof(KeyCode) });
                    finishCapture?.Invoke(null, new object[] { "None", KeyCode.None });
                    return false;
                }
            }
            return true;
        }

        public static void Duoji_GetCaptureTargetText_Postfix(ref string __result)
        {
            if (!string.IsNullOrEmpty(__result) && !__result.Contains("Esc"))
            {
                __result += " (按 Esc 可设为空)";
            }
        }

        public static bool Duoji_IsDefinedKeyCode_Prefix(int value, ref bool __result)
        {
            if (value == 0 || (value >= (int)KeyCode.Mouse3 && value <= (int)KeyCode.Mouse6))
            {
                __result = true;
                return false;
            }
            return true;
        }

        // --- 快捷键冲突扫描 (ConflictScanner) ---
        private static void PatchConflictScanner(Harmony harmony, ModLog? log)
        {
            Type? uiType = AccessTools.TypeByName("ConflictScanner.KeybindUi");
            if (uiType != null)
            {
                MethodInfo? poll = AccessTools.Method(uiType, "Poll");
                if (poll != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ConflictScanner_Poll_Prefix));
                    harmony.Patch(poll, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched ConflictScanner.KeybindUi.Poll");
                }
            }

            Type? keybindType = AccessTools.TypeByName("ConflictScanner.Keybind");
            if (keybindType != null)
            {
                MethodInfo? save = AccessTools.Method(keybindType, "Save", new Type[] { typeof(KeyCode) });
                if (save != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ConflictScanner_Save_Prefix));
                    harmony.Patch(save, prefix: new HarmonyMethod(prefix));
                }

                MethodInfo? disp = AccessTools.Method(keybindType, "DisplayName", new Type[] { typeof(KeyCode) });
                if (disp != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ConflictScanner_DisplayName_Prefix));
                    harmony.Patch(disp, prefix: new HarmonyMethod(prefix));
                }

                MethodInfo? getKey = AccessTools.PropertyGetter(keybindType, "Key");
                if (getKey != null)
                {
                    MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ConflictScanner_GetKey_Postfix));
                    harmony.Patch(getKey, postfix: new HarmonyMethod(postfix));
                }
            }
        }

        public static void ConflictScanner_GetKey_Postfix(ref KeyCode __result)
        {
            if (__result == KeyCode.None || ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            ComboKeyStore.FilterRuntimeKey(ref __result, "conflict_scanner.ReportKey");
        }

        public static bool ConflictScanner_Poll_Prefix()
        {
            Type? uiType = AccessTools.TypeByName("ConflictScanner.KeybindUi");
            if (uiType == null) return true;

            FieldInfo? capturingField = AccessTools.Field(uiType, "_capturing");
            bool isCapturing = (bool)(capturingField?.GetValue(null) ?? false);
            if (!isCapturing) return true;

            KeyCode? targetKey = null;
            if (KeyBindRuntimeHelper.TryPollSideMouseButton(out KeyCode sideBtn))
            {
                targetKey = sideBtn;
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                targetKey = KeyCode.None;
            }

            if (targetKey.HasValue)
            {
                capturingField?.SetValue(null, false);

                Type? keybindType = AccessTools.TypeByName("ConflictScanner.Keybind");
                if (keybindType != null)
                {
                    MethodInfo? save = AccessTools.Method(keybindType, "Save", new Type[] { typeof(KeyCode) });
                    save?.Invoke(null, new object[] { targetKey.Value });
                }

                CustomUIManager.Instance?.GetWindow("conflict_scanner.rebind")?.Close();

                Type? reportWindowType = AccessTools.TypeByName("ConflictScanner.ReportWindow");
                MethodInfo? show = AccessTools.Method(reportWindowType, "Show");
                show?.Invoke(null, null);

                return false;
            }
            return true;
        }

        public static bool ConflictScanner_Save_Prefix(KeyCode key)
        {
            if (key == KeyCode.None)
            {
                Type? keybindType = AccessTools.TypeByName("ConflictScanner.Keybind");
                if (keybindType != null)
                {
                    AccessTools.Field(keybindType, "_key")?.SetValue(null, KeyCode.None);
                    AccessTools.Field(keybindType, "_loaded")?.SetValue(null, true);
                    try
                    {
                        PlayerPrefs.SetString("conflict_scanner.report_key", "None");
                        PlayerPrefs.Save();
                    }
                    catch { }
                }
                return false;
            }
            return true;
        }

        public static bool ConflictScanner_DisplayName_Prefix(KeyCode key, ref string __result)
        {
            if (key == KeyCode.None)
            {
                __result = "（无）";
                return false;
            }
            if (ComboKeyStore.IsMouseSideButton(key))
            {
                __result = ComboKeyStore.FormatPrimaryKeyName(key);
                return false;
            }
            return true;
        }

        // --- 物品名称常显 (ShowItemNames) ---
        private static void PatchShowItemNames(Harmony harmony, ModLog? log)
        {
            Type? windowType = AccessTools.TypeByName("ShowItemNames.SettingsWindow");
            if (windowType != null)
            {
                MethodInfo? poll = AccessTools.Method(windowType, "PollCapture");
                if (poll != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ShowItemNames_PollCapture_Prefix));
                    harmony.Patch(poll, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched ShowItemNames.SettingsWindow.PollCapture");
                }
            }

            Type? settingsType = AccessTools.TypeByName("ShowItemNames.Settings");
            if (settingsType != null)
            {
                MethodInfo? setHotkey = AccessTools.Method(settingsType, "SetHotkey", new Type[] { typeof(KeyCode) });
                if (setHotkey != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ShowItemNames_SetHotkey_Prefix));
                    harmony.Patch(setHotkey, prefix: new HarmonyMethod(prefix));
                }

                MethodInfo? getHotkey = AccessTools.PropertyGetter(settingsType, "Hotkey");
                if (getHotkey != null)
                {
                    MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ShowItemNames_GetHotkey_Postfix));
                    harmony.Patch(getHotkey, postfix: new HarmonyMethod(postfix));
                }
            }
        }

        public static void ShowItemNames_GetHotkey_Postfix(ref KeyCode __result)
        {
            if (__result == KeyCode.None || ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            ComboKeyStore.FilterRuntimeKey(ref __result, "ShowItemNames.Hotkey");
        }

        public static bool ShowItemNames_PollCapture_Prefix(ref bool __result)
        {
            Type? windowType = AccessTools.TypeByName("ShowItemNames.SettingsWindow");
            if (windowType == null) return true;

            FieldInfo? capturingField = AccessTools.Field(windowType, "_capturing");
            bool isCapturing = (bool)(capturingField?.GetValue(null) ?? false);
            if (!isCapturing) return true;

            KeyCode? captured = null;
            if (KeyBindRuntimeHelper.TryPollSideMouseButton(out KeyCode sideBtn))
            {
                captured = sideBtn;
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                captured = KeyCode.None;
            }

            if (captured.HasValue)
            {
                capturingField?.SetValue(null, false);

                Type? settingsType = AccessTools.TypeByName("ShowItemNames.Settings");
                if (settingsType != null)
                {
                    MethodInfo? setHotkey = AccessTools.Method(settingsType, "SetHotkey", new Type[] { typeof(KeyCode) });
                    setHotkey?.Invoke(null, new object[] { captured.Value });
                }

                MethodInfo? requestRebuild = AccessTools.Method(windowType, "RequestRebuild");
                requestRebuild?.Invoke(null, null);

                __result = true;
                return false;
            }
            return true;
        }

        public static bool ShowItemNames_SetHotkey_Prefix(KeyCode key)
        {
            if (key == KeyCode.None)
            {
                Type? settingsType = AccessTools.TypeByName("ShowItemNames.Settings");
                if (settingsType != null)
                {
                    PropertyInfo? hotkeyProp = AccessTools.Property(settingsType, "Hotkey");
                    hotkeyProp?.SetValue(null, KeyCode.None);
                    PlayerPrefs.SetInt("snd.hotkey", 0);
                    MethodInfo? commitMethod = AccessTools.Method(settingsType, "Commit");
                    commitMethod?.Invoke(null, null);
                }
                return false;
            }
            return true;
        }

        // --- 智能物品整理 (SmartInventoryArrange) ---
        private static void PatchSmartInventoryArrange(Harmony harmony, ModLog? log)
        {
            Type? guiType = AccessTools.TypeByName("SmartInventoryArrange.UnifiedSettingsGui");
            if (guiType != null)
            {
                MethodInfo? captureKey = AccessTools.Method(guiType, "CaptureKey");
                if (captureKey != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(SmartInventoryArrange_CaptureKey_Prefix));
                    harmony.Patch(captureKey, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched SmartInventoryArrange.UnifiedSettingsGui.CaptureKey");
                }
            }
        }

        public static bool SmartInventoryArrange_CaptureKey_Prefix(object __instance)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Type? guiType = AccessTools.TypeByName("SmartInventoryArrange.UnifiedSettingsGui");
                Type? modelType = AccessTools.TypeByName("SmartInventoryArrange.SettingsHotkeys");
                Type? settingsBindingType = AccessTools.TypeByName("SmartInventoryArrange.SettingsBinding");
                Type? settingsHotkeyType = AccessTools.TypeByName("SmartInventoryArrange.SettingsHotkey");

                if (guiType != null && modelType != null && settingsBindingType != null && settingsHotkeyType != null)
                {
                    FieldInfo? modelField = AccessTools.Field(guiType, "Model");
                    object? model = modelField?.GetValue(__instance);
                    if (model != null)
                    {
                        PropertyInfo? captureProp = AccessTools.Property(modelType, "Capture");
                        object? currentBinding = captureProp?.GetValue(model);
                        if (currentBinding != null)
                        {
                            FieldInfo? noneField = AccessTools.Field(settingsHotkeyType, "None");
                            object? noneKey = noneField?.GetValue(null);

                            MethodInfo? bindMethod = AccessTools.Method(guiType, "Bind", new Type[] { settingsBindingType, settingsHotkeyType });
                            bindMethod?.Invoke(__instance, new object[] { currentBinding, noneKey! });
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        // --- 势力声望拓展 (FactionReputationExpansion) ---
        private static void PatchFactionReputationExpansion(Harmony harmony, ModLog? log)
        {
            Type? repModType = AccessTools.TypeByName("FactionReputationExpansion.RepMod");
            if (repModType != null)
            {
                MethodInfo? tick = AccessTools.Method(repModType, "Tick");
                if (tick != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(FactionReputation_Tick_Prefix));
                    harmony.Patch(tick, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched FactionReputationExpansion.RepMod.Tick");
                }

                MethodInfo? getKey = AccessTools.PropertyGetter(repModType, "JournalKey");
                if (getKey != null)
                {
                    MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(FactionReputation_GetJournalKey_Postfix));
                    harmony.Patch(getKey, postfix: new HarmonyMethod(postfix));
                }
            }

            Type? rulesType = AccessTools.TypeByName("FactionReputationExpansion.HotkeyRules");
            if (rulesType != null)
            {
                MethodInfo? isBindable = AccessTools.Method(rulesType, "IsBindableName", new Type[] { typeof(string) });
                if (isBindable != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(FactionReputation_IsBindableName_Prefix));
                    harmony.Patch(isBindable, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched FactionReputationExpansion.HotkeyRules.IsBindableName (allows None)");
                }
            }
        }

        public static void FactionReputation_GetJournalKey_Postfix(ref KeyCode __result)
        {
            if (__result == KeyCode.None || ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            ComboKeyStore.FilterRuntimeKey(ref __result, "faction_reputation.journal_key");
        }

        public static bool FactionReputation_Tick_Prefix()
        {
            Type? repModType = AccessTools.TypeByName("FactionReputationExpansion.RepMod");
            if (repModType == null) return true;

            PropertyInfo? capturingProp = AccessTools.Property(repModType, "CapturingHotkey");
            bool capturing = (bool)(capturingProp?.GetValue(null) ?? false);
            if (!capturing) return true;

            KeyCode? captured = null;
            if (KeyBindRuntimeHelper.TryPollSideMouseButton(out KeyCode sideBtn))
            {
                captured = sideBtn;
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                captured = KeyCode.None;
            }

            if (captured.HasValue)
            {
                MethodInfo? setJournalKey = AccessTools.Method(repModType, "SetJournalKey", new Type[] { typeof(KeyCode), typeof(bool) });
                setJournalKey?.Invoke(null, new object[] { captured.Value, false });
                return false;
            }
            return true;
        }

        public static bool FactionReputation_IsBindableName_Prefix(string? name, ref bool __result)
        {
            if (string.Equals(name, "None", StringComparison.OrdinalIgnoreCase))
            {
                __result = true;
                return false;
            }
            return true;
        }

        // --- Pen 的便利性优化 (Pen_mod_QoL_Tweaks) ---
        private static void PatchPenQoL(Harmony harmony, ModLog? log)
        {
            Type? qolType = AccessTools.TypeByName("PenLabelerAssist.QoLKeyBindings");
            if (qolType == null) return;

            var bindings = new (string PropName, string OptionId)[]
            {
                ("QuickBuy", "pen_qol_quick_buy"),
                ("QuickSell", "pen_qol_quick_sell"),
                ("QuickAppraisal", "pen_qol_quick_appraisal"),
                ("QuickConfront", "pen_qol_quick_confront"),
                ("QuickPin", "pen_qol_quick_pin"),
                ("TabLeft", "pen_qol_tab_left"),
                ("TabRight", "pen_qol_tab_right"),
                ("FollowModifier", "pen_qol_window_follow_modifier"),
                ("SnapModifier", "pen_qol_window_snap_modifier"),
                ("BoxSelectModifier", "pen_qol_boxselect_modifier"),
                ("MultiSelectToggle", "pen_qol_multi_select_toggle")
            };

            MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(PenQoL_GetKey_Postfix));
            int patchedGetters = 0;
            foreach (var (propName, optionId) in bindings)
            {
                MethodInfo? getter = AccessTools.PropertyGetter(qolType, propName);
                if (getter == null) continue;

                lock (PenQoLGetterCandidates)
                {
                    PenQoLGetterCandidates[getter] = new[]
                    {
                        optionId,
                        "pen_qol." + propName.ToLowerInvariant()
                    };
                }
                harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                patchedGetters++;
            }

            MethodInfo? isBoundDown = AccessTools.Method(qolType, "IsBoundKeyDown", new Type[] { typeof(KeyCode) });
            if (isBoundDown != null)
            {
                harmony.Patch(isBoundDown, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(PenQoL_IsBoundKeyDown_Prefix))));
            }

            MethodInfo? isDown = AccessTools.Method(qolType, "IsDown", new Type[] { typeof(KeyCode) });
            if (isDown != null)
            {
                harmony.Patch(isDown, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(PenQoL_IsDown_Prefix))));
            }

            log?.Log($"Patched PenLabelerAssist.QoLKeyBindings ({patchedGetters} properties + input helpers)");
        }

        public static void PenQoL_GetKey_Postfix(MethodBase __originalMethod, ref KeyCode __result)
        {
            if (__result == KeyCode.None || ComboKeyStore.IsBypassed) return;
            if (!PenQoLGetterCandidates.TryGetValue(__originalMethod, out string[]? ids) || ids == null) return;

            ComboKeyStore.EnsureLoaded();
            ComboKeyStore.FilterRuntimeKey(ref __result, ids);
        }

        public static bool PenQoL_IsBoundKeyDown_Prefix(KeyCode key, ref bool __result)
        {
            if (key == KeyCode.None)
            {
                __result = false;
                return false;
            }
            __result = ComboKeyStore.IsKeyDownRaw(key);
            return false;
        }

        public static bool PenQoL_IsDown_Prefix(KeyCode key, ref bool __result)
        {
            if (key == KeyCode.None)
            {
                __result = false;
                return false;
            }
            if (key == KeyCode.Mouse0 && ComboKeyStore.GetHeldModifiersMask() == KeyModifierMask.None)
            {
                __result = false;
                return false;
            }
            __result = ComboKeyStore.IsKeyHeldRaw(key);
            return false;
        }

        // --- 客户需求高亮 (ClientDemandHighlight) ---
        private static void PatchClientDemandHighlight(Harmony harmony, ModLog? log)
        {
            Type? cfgType = AccessTools.TypeByName("ClientDemandHighlight.Config");
            if (cfgType == null) return;

            MethodInfo? getToggle = AccessTools.PropertyGetter(cfgType, "ToggleKey");
            if (getToggle != null)
            {
                MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ClientDemandHighlight_GetToggleKey_Postfix));
                harmony.Patch(getToggle, postfix: new HarmonyMethod(postfix));
                log?.Log("Patched ClientDemandHighlight.Config.ToggleKey");
            }
        }

        public static void ClientDemandHighlight_GetToggleKey_Postfix(ref KeyCode __result)
        {
            if (__result == KeyCode.None || ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            ComboKeyStore.FilterRuntimeKey(
                ref __result,
                "toggleKey",
                "hollow.clientdemandhighlight.toggleKey",
                "clientdemandhighlight.toggle_key"
            );
        }
    }
}
