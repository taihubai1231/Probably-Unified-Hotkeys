using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AllowUnbindKey
{
    public class ModEntry : IMod
    {
        private static ModLog? log;
        private static Harmony? workshopHarmony;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            log.Log($"Initialized v{manifest.ModVersion}");

            workshopHarmony = new Harmony("allow_unbind_key.workshop");
            WorkshopModPatcher.Apply(workshopHarmony, log);
        }

        public void OnEnable()
        {
            log?.Log("Enabled: Key clearing & unbind functionality is active across vanilla and workshop mods.");
        }

        public void OnDisable()
        {
            try
            {
                workshopHarmony?.UnpatchAll("allow_unbind_key.workshop");
            }
            catch (Exception ex)
            {
                log?.Warning($"Error unpatching workshop mods: {ex.Message}");
            }
            log?.Log("Disabled.");
        }
    }

    // =========================================================================
    // 1. 原版游戏设置界面按键捕获补丁
    // =========================================================================

    /// <summary>
    /// 允许捕获空按键（按下 Backspace 或 Delete 时触发 KeyCode.None，代表清空/解除绑定）
    /// </summary>
    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.PollCapture))]
    public static class Patch_KeyBindHelper_PollCapture
    {
        public static bool Prefix(ref KeyBindHelper.CaptureResult __result, ref KeyCode key)
        {
            if (!KeyBindHelper.IsCapturing || !Input.anyKeyDown)
            {
                return true;
            }

            bool isTrashItem = KeyBindHelper.CapturingOptionId == "key_trash_item" || KeyBindHelper.CapturingOptionId == "trash_item";

            // 按下 Backspace（退格键）或者非垃圾桶项按下 Delete 时，绑定为 None（空/无）
            if (Input.GetKeyDown(KeyCode.Backspace) || (!isTrashItem && Input.GetKeyDown(KeyCode.Delete)))
            {
                key = KeyCode.None;
                __result = KeyBindHelper.CaptureResult.Key;
                return false;
            }

            return true;
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
    /// 在等待按键输入时，提示用户可以通过 Backspace 设置为空按键
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "StartCapture")]
    public static class Patch_GameOptionUIManager_StartCapture
    {
        private static readonly MethodInfo? SetKeyBindStatusMethod = AccessTools.Method(typeof(GameOptionUIManager), "SetKeyBindStatus");

        public static void Postfix(GameOptionUIManager __instance)
        {
            try
            {
                string currentStatus = GameOptionUIManager.KeyBindStatus;
                string extra = " (按 Backspace 可设为空/清空)";
                SetKeyBindStatusMethod?.Invoke(__instance, new object[] { currentStatus + extra });
            }
            catch
            {
                // 忽略 UI 状态刷新中的次要异常
            }
        }
    }

    // =========================================================================
    // 2. 创意工坊主流模组的空按键兼容与支持
    // =========================================================================

    public static class WorkshopModPatcher
    {
        private static readonly HashSet<string> PatchedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Apply(Harmony harmony, ModLog? log)
        {
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

        public static bool Duoji_TickCapture_Prefix()
        {
            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
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
            if (!string.IsNullOrEmpty(__result) && !__result.Contains("Backspace"))
            {
                __result += " (按 Backspace 可设为空/清空)";
            }
        }

        public static bool Duoji_IsDefinedKeyCode_Prefix(int value, ref bool __result)
        {
            if (value == 0) // KeyCode.None
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
            }
        }

        public static bool ConflictScanner_Poll_Prefix()
        {
            Type? uiType = AccessTools.TypeByName("ConflictScanner.KeybindUi");
            if (uiType == null) return true;

            FieldInfo? capturingField = AccessTools.Field(uiType, "_capturing");
            bool isCapturing = (bool)(capturingField?.GetValue(null) ?? false);
            if (!isCapturing) return true;

            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
            {
                capturingField?.SetValue(null, false);

                Type? keybindType = AccessTools.TypeByName("ConflictScanner.Keybind");
                if (keybindType != null)
                {
                    MethodInfo? save = AccessTools.Method(keybindType, "Save", new Type[] { typeof(KeyCode) });
                    save?.Invoke(null, new object[] { KeyCode.None });
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
            }
        }

        public static bool ShowItemNames_PollCapture_Prefix(ref bool __result)
        {
            Type? windowType = AccessTools.TypeByName("ShowItemNames.SettingsWindow");
            if (windowType == null) return true;

            FieldInfo? capturingField = AccessTools.Field(windowType, "_capturing");
            bool isCapturing = (bool)(capturingField?.GetValue(null) ?? false);
            if (!isCapturing) return true;

            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
            {
                capturingField?.SetValue(null, false);

                Type? settingsType = AccessTools.TypeByName("ShowItemNames.Settings");
                if (settingsType != null)
                {
                    MethodInfo? setHotkey = AccessTools.Method(settingsType, "SetHotkey", new Type[] { typeof(KeyCode) });
                    setHotkey?.Invoke(null, new object[] { KeyCode.None });
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
            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
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

        public static bool FactionReputation_Tick_Prefix()
        {
            Type? repModType = AccessTools.TypeByName("FactionReputationExpansion.RepMod");
            if (repModType == null) return true;

            PropertyInfo? capturingProp = AccessTools.Property(repModType, "CapturingHotkey");
            bool capturing = (bool)(capturingProp?.GetValue(null) ?? false);
            if (capturing && (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)))
            {
                MethodInfo? setJournalKey = AccessTools.Method(repModType, "SetJournalKey", new Type[] { typeof(KeyCode), typeof(bool) });
                setJournalKey?.Invoke(null, new object[] { KeyCode.None, false });
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
    }
}
