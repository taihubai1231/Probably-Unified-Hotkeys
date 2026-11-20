using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    // =========================================================================
    // 2. 创意工坊主流模组的空按键兼容与支持
    // =========================================================================

    public static class WorkshopModPatcher
    {
        private static readonly HashSet<string> PatchedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<MethodBase, string[]> PenQoLGetterCandidates = new Dictionary<MethodBase, string[]>();

        public static void Apply(Harmony harmony, ModLog? log)
        {
            // 对官方泛型 ModConfig 热键（KeyCode / string / int）挂接运行时组合键过滤
            PatchConfigEntryHotkeys(harmony, log);

            // 挂接 UnityEngine.Input 底层按键重定向与作用域接管补丁
            PatchUnityInputRedirection(harmony, log);

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

        private static void PatchUnityInputRedirection(Harmony harmony, ModLog? log)
        {
            int patchedCount = 0;

            try
            {
                MethodInfo? getKey = AccessTools.Method(typeof(Input), "GetKey", new Type[] { typeof(KeyCode) });
                if (getKey != null)
                {
                    harmony.Patch(getKey, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(Input_GetKey_Prefix))));
                    patchedCount++;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[WorkshopModPatcher] Failed to patch Input.GetKey: {ex.Message}");
            }

            try
            {
                MethodInfo? getKeyDown = AccessTools.Method(typeof(Input), "GetKeyDown", new Type[] { typeof(KeyCode) });
                if (getKeyDown != null)
                {
                    harmony.Patch(getKeyDown, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(Input_GetKeyDown_Prefix))));
                    patchedCount++;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[WorkshopModPatcher] Failed to patch Input.GetKeyDown: {ex.Message}");
            }

            try
            {
                MethodInfo? getKeyUp = AccessTools.Method(typeof(Input), "GetKeyUp", new Type[] { typeof(KeyCode) });
                if (getKeyUp != null)
                {
                    harmony.Patch(getKeyUp, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(Input_GetKeyUp_Prefix))));
                    patchedCount++;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[WorkshopModPatcher] Failed to patch Input.GetKeyUp: {ex.Message}");
            }

            try
            {
                MethodInfo? getMouse = AccessTools.Method(typeof(Input), "GetMouseButton", new Type[] { typeof(int) });
                if (getMouse != null)
                {
                    harmony.Patch(getMouse, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(Input_GetMouseButton_Prefix))));
                    patchedCount++;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[WorkshopModPatcher] Input.GetMouseButton is an InternalCall and skipped: {ex.Message}");
            }

            try
            {
                MethodInfo? getMouseDown = AccessTools.Method(typeof(Input), "GetMouseButtonDown", new Type[] { typeof(int) });
                if (getMouseDown != null)
                {
                    harmony.Patch(getMouseDown, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(Input_GetMouseButtonDown_Prefix))));
                    patchedCount++;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[WorkshopModPatcher] Input.GetMouseButtonDown is an InternalCall and skipped: {ex.Message}");
            }

            try
            {
                MethodInfo? getMouseUp = AccessTools.Method(typeof(Input), "GetMouseButtonUp", new Type[] { typeof(int) });
                if (getMouseUp != null)
                {
                    harmony.Patch(getMouseUp, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(Input_GetMouseButtonUp_Prefix))));
                    patchedCount++;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[WorkshopModPatcher] Input.GetMouseButtonUp is an InternalCall and skipped: {ex.Message}");
            }

            log?.Log($"Patched UnityEngine.Input key & mouse redirection hooks ({patchedCount} methods)");
        }

        public static bool Input_GetKey_Prefix(KeyCode key, ref bool __result)
        {
            if (ComboKeyStore.HasAnyActiveInputRemapOrScope &&
                ComboKeyStore.TryEvaluateInputRemap(key, ComboKeyStore.InputQueryKind.Held, out bool remapped))
            {
                __result = remapped;
                return false;
            }
            return true;
        }

        public static bool Input_GetKeyDown_Prefix(KeyCode key, ref bool __result)
        {
            if (ComboKeyStore.HasAnyActiveInputRemapOrScope &&
                ComboKeyStore.TryEvaluateInputRemap(key, ComboKeyStore.InputQueryKind.Down, out bool remapped))
            {
                __result = remapped;
                return false;
            }
            return true;
        }

        public static bool Input_GetKeyUp_Prefix(KeyCode key, ref bool __result)
        {
            if (ComboKeyStore.HasAnyActiveInputRemapOrScope &&
                ComboKeyStore.TryEvaluateInputRemap(key, ComboKeyStore.InputQueryKind.Up, out bool remapped))
            {
                __result = remapped;
                return false;
            }
            return true;
        }

        public static bool Input_GetMouseButton_Prefix(int button, ref bool __result)
        {
            if (button >= 0 && button <= 6 && ComboKeyStore.HasAnyActiveInputRemapOrScope)
            {
                KeyCode key = (KeyCode)((int)KeyCode.Mouse0 + button);
                if (ComboKeyStore.TryEvaluateInputRemap(key, ComboKeyStore.InputQueryKind.Held, out bool remapped))
                {
                    __result = remapped;
                    return false;
                }
            }
            return true;
        }

        public static bool Input_GetMouseButtonDown_Prefix(int button, ref bool __result)
        {
            if (button >= 0 && button <= 6 && ComboKeyStore.HasAnyActiveInputRemapOrScope)
            {
                KeyCode key = (KeyCode)((int)KeyCode.Mouse0 + button);
                if (ComboKeyStore.TryEvaluateInputRemap(key, ComboKeyStore.InputQueryKind.Down, out bool remapped))
                {
                    __result = remapped;
                    return false;
                }
            }
            return true;
        }

        public static bool Input_GetMouseButtonUp_Prefix(int button, ref bool __result)
        {
            if (button >= 0 && button <= 6 && ComboKeyStore.HasAnyActiveInputRemapOrScope)
            {
                KeyCode key = (KeyCode)((int)KeyCode.Mouse0 + button);
                if (ComboKeyStore.TryEvaluateInputRemap(key, ComboKeyStore.InputQueryKind.Up, out bool remapped))
                {
                    __result = remapped;
                    return false;
                }
            }
            return true;
        }

        private static void PatchConfigEntryHotkeys(Harmony harmony, ModLog? log)
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
                ModLogger.Warn($"[WorkshopModPatcher] Failed to patch ConfigEntry<KeyCode>.Value: {ex.Message}");
            }

            try
            {
                PropertyInfo? strProp = AccessTools.Property(typeof(ConfigEntry<string>), nameof(ConfigEntry<string>.Value));
                MethodInfo? strGetter = strProp?.GetGetMethod(true);
                if (strGetter != null)
                {
                    MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ConfigEntryString_GetValue_Postfix));
                    harmony.Patch(strGetter, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                log?.Warning($"[WorkshopModPatcher] Failed to patch ConfigEntry<string>.Value: {ex.Message}");
                ModLogger.Warn($"[WorkshopModPatcher] Failed to patch ConfigEntry<string>.Value: {ex.Message}");
            }

            try
            {
                PropertyInfo? intProp = AccessTools.Property(typeof(ConfigEntry<int>), nameof(ConfigEntry<int>.Value));
                MethodInfo? intGetter = intProp?.GetGetMethod(true);
                if (intGetter != null)
                {
                    MethodInfo postfix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(ConfigEntryInt_GetValue_Postfix));
                    harmony.Patch(intGetter, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                log?.Warning($"[WorkshopModPatcher] Failed to patch ConfigEntry<int>.Value: {ex.Message}");
                ModLogger.Warn($"[WorkshopModPatcher] Failed to patch ConfigEntry<int>.Value: {ex.Message}");
            }
        }

        public static void ConfigEntryKeyCode_GetValue_Postfix(ConfigEntry<KeyCode> __instance, ref KeyCode __result)
        {
            if (__instance == null || ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            string? modId = __instance.Config?.ModId;
            string fullId = string.IsNullOrEmpty(modId) ? __instance.Key : $"{modId}.{__instance.Key}";
            if (ComboKeyStore.IsExplicitlyUnbound(fullId))
            {
                __result = KeyCode.None;
                return;
            }
            if (__result == KeyCode.None) return;
            ComboKeyStore.FilterRuntimeKey(ref __result, fullId, __instance.Key);
        }

        public static void ConfigEntryString_GetValue_Postfix(ConfigEntry<string> __instance, ref string __result)
        {
            if (__instance == null || ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            string? modId = __instance.Config?.ModId;
            string fullId = string.IsNullOrEmpty(modId) ? __instance.Key : $"{modId}.{__instance.Key}";
            if (ComboKeyStore.IsExplicitlyUnbound(fullId))
            {
                __result = "None";
                return;
            }
            if (string.IsNullOrEmpty(__result)) return;
            if (!ComboKeyStore.IsManagedStringHotkey(fullId, __instance.Key)) return;

            if (!ModOptionScanner.TryParseComboString(__result, out KeyModifierMask embeddedMods, out KeyCode parsedKey) ||
                parsedKey == KeyCode.None)
            {
                return;
            }

            if (embeddedMods != KeyModifierMask.None)
            {
                if (!ComboKeyStore.AreModifiersExactMatch(embeddedMods, parsedKey))
                {
                    __result = "None";
                }
            }
            else
            {
                KeyCode filtered = parsedKey;
                ComboKeyStore.FilterRuntimeKey(ref filtered, fullId, __instance.Key);
                if (filtered == KeyCode.None)
                {
                    __result = "None";
                }
            }
        }

        public static void ConfigEntryInt_GetValue_Postfix(ConfigEntry<int> __instance, ref int __result)
        {
            if (__instance == null || ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            string? modId = __instance.Config?.ModId;
            string fullId = string.IsNullOrEmpty(modId) ? __instance.Key : $"{modId}.{__instance.Key}";
            if (ComboKeyStore.IsExplicitlyUnbound(fullId))
            {
                __result = 0;
                return;
            }
            if (__result == 0) return;
            if (!ComboKeyStore.IsManagedIntHotkey(fullId, __instance.Key)) return;
            if (!Enum.IsDefined(typeof(KeyCode), __result)) return;

            KeyCode filtered = (KeyCode)__result;
            ComboKeyStore.FilterRuntimeKey(ref filtered, fullId, __instance.Key);
            if (filtered == KeyCode.None)
            {
                __result = 0;
            }
        }

        private static readonly HashSet<string> SupportedWorkshopAssemblies = new HashSet<string>(StringComparer.Ordinal)
        {
            "DuojiHotkeyManager",
            "ConflictScanner",
            "ShowItemNames",
            "SmartInventoryArrange.Mono",
            "FactionReputationExpansion",
            "Pen_mod_QoL_Tweaks",
            "ClientDemandHighlight",
            "PreciseGroupDrag",
            "BetterPerformance",
            "ItemDetails",
            "RealTimeClock"
        };

        private static void TryPatch(Assembly assembly, Harmony harmony, ModLog? log)
        {
            string? name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name) || !SupportedWorkshopAssemblies.Contains(name)) return;

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
                    ConflictScannerPerfHelper.Bind(assembly, harmony, log);
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
                else if (name == "PreciseGroupDrag")
                {
                    PatchPreciseGroupDrag(harmony, log);
                }
                else if (name == "BetterPerformance")
                {
                    PatchBetterPerformance(harmony, log);
                }
                else if (name == "ItemDetails")
                {
                    PatchItemDetails(harmony, log);
                }
            }
            catch (Exception ex)
            {
                log?.Warning($"[WorkshopModPatcher] Failed to patch mod '{name}': {ex.Message}");
                ModLogger.Warn($"[WorkshopModPatcher] Failed to patch mod '{name}': {ex.Message}");
            }
        }

        private static T? CreateDelegateSafe<T>(MethodInfo? method) where T : Delegate
        {
            if (method == null) return null;
            try
            {
                return (T)Delegate.CreateDelegate(typeof(T), method);
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[WorkshopModPatcher] CreateDelegateSafe<{typeof(T).Name}> failed: {ex.Message}");
                return null;
            }
        }

        // --- 多吉改键器 (DuojiHotkeyManager) ---
        private static Func<List<KeyCode>?>? _duojiGetToggleKeysFn;
        private static Action<string, KeyCode>? _duojiFinishCaptureFn;

        private static void PatchDuojiHotkeyManager(Harmony harmony, ModLog? log)
        {
            Type? hotkeyMgrType = AccessTools.TypeByName("DuojiHotkeyManager.HotkeyManager");
            if (hotkeyMgrType != null)
            {
                _duojiFinishCaptureFn = CreateDelegateSafe<Action<string, KeyCode>>(
                    AccessTools.Method(hotkeyMgrType, "FinishCapture", new Type[] { typeof(string), typeof(KeyCode) }));

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

                _duojiGetToggleKeysFn = CreateDelegateSafe<Func<List<KeyCode>?>>(
                    AccessTools.PropertyGetter(hotkeyMgrType, "ToggleKeys"));
                MethodInfo? toggleAnyDown = AccessTools.Method(
                    hotkeyMgrType,
                    "ToggleAnyDown",
                    new Type[] { typeof(KeyCode).MakeByRefType(), typeof(bool).MakeByRefType() });
                if (toggleAnyDown != null && _duojiGetToggleKeysFn != null)
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

        public static bool Duoji_ToggleAnyDown_Prefix(ref KeyCode which, ref bool isFallback, ref bool __result)
        {
            which = KeyCode.None;
            isFallback = false;

            if (_duojiGetToggleKeysFn == null)
            {
                return true;
            }

            List<KeyCode>? keys = _duojiGetToggleKeysFn();
            if (keys != null && keys.Count > 0)
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
            if (Input.GetKeyDown(KeyCode.Escape) && _duojiFinishCaptureFn != null)
            {
                _duojiFinishCaptureFn("None", KeyCode.None);
                return false;
            }
            return true;
        }

        public static void Duoji_GetCaptureTargetText_Postfix(ref string __result)
        {
            if (!string.IsNullOrEmpty(__result) && !__result.Contains("Esc"))
            {
                __result += BilingualText.Get(" (按 Esc 可设为空)", " (Esc to clear)");
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
        private static FieldInfo? _csUiCapturingField;
        private static Action<KeyCode>? _csKeybindSaveFn;
        private static Action? _csReportWindowShowFn;
        private static FieldInfo? _csKeybindKeyField;
        private static FieldInfo? _csKeybindLoadedField;

        private static void PatchConflictScanner(Harmony harmony, ModLog? log)
        {
            Type? uiType = AccessTools.TypeByName("ConflictScanner.KeybindUi");
            if (uiType != null)
            {
                _csUiCapturingField = AccessTools.Field(uiType, "_capturing");

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
                _csKeybindKeyField = AccessTools.Field(keybindType, "_key");
                _csKeybindLoadedField = AccessTools.Field(keybindType, "_loaded");

                MethodInfo? save = AccessTools.Method(keybindType, "Save", new Type[] { typeof(KeyCode) });
                _csKeybindSaveFn = CreateDelegateSafe<Action<KeyCode>>(save);
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

            Type? reportWindowType = AccessTools.TypeByName("ConflictScanner.ReportWindow");
            if (reportWindowType != null)
            {
                _csReportWindowShowFn = CreateDelegateSafe<Action>(AccessTools.Method(reportWindowType, "Show"));
            }
        }

        public static void ConflictScanner_GetKey_Postfix(ref KeyCode __result)
        {
            if (ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            if (ComboKeyStore.IsExplicitlyUnbound("conflict_scanner.ReportKey"))
            {
                __result = KeyCode.None;
                return;
            }
            if (__result == KeyCode.None) return;
            ComboKeyStore.FilterRuntimeKey(ref __result, "conflict_scanner.ReportKey");
        }

        public static bool ConflictScanner_Poll_Prefix()
        {
            if (_csUiCapturingField == null) return true;

            bool isCapturing = (bool)(_csUiCapturingField.GetValue(null) ?? false);
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
                _csUiCapturingField.SetValue(null, false);
                _csKeybindSaveFn?.Invoke(targetKey.Value);
                CustomUIManager.Instance?.GetWindow("conflict_scanner.rebind")?.Close();
                _csReportWindowShowFn?.Invoke();

                return false;
            }
            return true;
        }

        public static bool ConflictScanner_Save_Prefix(KeyCode key)
        {
            try
            {
                ComboKeyStore.SetSavedCustomHotkey("conflict_scanner.ReportKey", KeyModifierMask.None, key, KeyModifierMask.None, KeyCode.F7);
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[WorkshopModPatcher] Failed to sync ConflictScanner key to store: {ex.Message}");
            }

            if (key == KeyCode.None)
            {
                _csKeybindKeyField?.SetValue(null, KeyCode.None);
                _csKeybindLoadedField?.SetValue(null, true);
                try
                {
                    PlayerPrefs.SetString("conflict_scanner.report_key", "None");
                    PlayerPrefs.Save();
                }
                catch (Exception ex)
                {
                    ModLogger.Warn($"[WorkshopModPatcher] Failed to save conflict_scanner report_key: {ex.Message}");
                }
                return false;
            }
            return true;
        }

        public static bool ConflictScanner_DisplayName_Prefix(KeyCode key, ref string __result)
        {
            if (key == KeyCode.None)
            {
                __result = BilingualText.Get("（无）", "None");
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
        private static FieldInfo? _sinWindowCapturingField;
        private static Action? _sinWindowRequestRebuildFn;
        private static Action<KeyCode>? _sinSettingsSetHotkeyFn;
        private static PropertyInfo? _sinSettingsHotkeyProp;
        private static Action<KeyCode>? _sinSettingsSetHotkeyPropFn;
        private static Action? _sinSettingsCommitFn;

        private static void PatchShowItemNames(Harmony harmony, ModLog? log)
        {
            Type? windowType = AccessTools.TypeByName("ShowItemNames.SettingsWindow");
            if (windowType != null)
            {
                _sinWindowCapturingField = AccessTools.Field(windowType, "_capturing");
                _sinWindowRequestRebuildFn = CreateDelegateSafe<Action>(AccessTools.Method(windowType, "RequestRebuild"));

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
                _sinSettingsHotkeyProp = AccessTools.Property(settingsType, "Hotkey");
                _sinSettingsSetHotkeyPropFn = CreateDelegateSafe<Action<KeyCode>>(_sinSettingsHotkeyProp?.GetSetMethod(true));
                _sinSettingsCommitFn = CreateDelegateSafe<Action>(AccessTools.Method(settingsType, "Commit"));

                MethodInfo? setHotkey = AccessTools.Method(settingsType, "SetHotkey", new Type[] { typeof(KeyCode) });
                _sinSettingsSetHotkeyFn = CreateDelegateSafe<Action<KeyCode>>(setHotkey);
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
            if (ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            if (ComboKeyStore.IsExplicitlyUnbound("ShowItemNames.Hotkey") || ComboKeyStore.IsExplicitlyUnbound("snd.hotkey"))
            {
                __result = KeyCode.None;
                return;
            }
            if (__result == KeyCode.None) return;
            ComboKeyStore.FilterRuntimeKey(ref __result, "ShowItemNames.Hotkey");
        }

        public static bool ShowItemNames_PollCapture_Prefix(ref bool __result)
        {
            if (_sinWindowCapturingField == null) return true;

            bool isCapturing = (bool)(_sinWindowCapturingField.GetValue(null) ?? false);
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
                _sinWindowCapturingField.SetValue(null, false);
                _sinSettingsSetHotkeyFn?.Invoke(captured.Value);
                _sinWindowRequestRebuildFn?.Invoke();

                __result = true;
                return false;
            }
            return true;
        }

        public static bool ShowItemNames_SetHotkey_Prefix(KeyCode key)
        {
            if (key == KeyCode.None)
            {
                try
                {
                    if (_sinSettingsSetHotkeyPropFn != null) _sinSettingsSetHotkeyPropFn(KeyCode.None);
                    else if (_sinSettingsHotkeyProp != null && _sinSettingsHotkeyProp.CanWrite && _sinSettingsHotkeyProp.GetSetMethod(true) != null) _sinSettingsHotkeyProp.SetValue(null, KeyCode.None);
                    PlayerPrefs.SetInt("snd.hotkey", 0);
                    _sinSettingsCommitFn?.Invoke();
                }
                catch (Exception ex)
                {
                    ModLogger.Warn($"ShowItemNames_SetHotkey_Prefix error: {ex.Message}");
                }
                return false;
            }
            return true;
        }

        // --- 智能物品整理 (SmartInventoryArrange) ---
        private static FieldInfo? _siaModelField;
        private static PropertyInfo? _siaCaptureProp;
        private static object? _siaNoneKeyValue;
        private static MethodInfo? _siaBindMethod;
        private static PropertyInfo? _siaHkKeyProp;
        private static PropertyInfo? _siaHkCtrlProp;
        private static PropertyInfo? _siaHkAltProp;
        private static PropertyInfo? _siaHkShiftProp;
        private static MethodInfo? _siaOpenSetter;
        private static PropertyInfo? _siaBoundProp;
        private static bool _siaBootSynced = false;

        private static void PatchSmartInventoryArrange(Harmony harmony, ModLog? log)
        {
            Type? guiType = AccessTools.TypeByName("SmartInventoryArrange.UnifiedSettingsGui");
            Type? modelType = AccessTools.TypeByName("SmartInventoryArrange.SettingsHotkeys");
            Type? settingsBindingType = AccessTools.TypeByName("SmartInventoryArrange.SettingsBinding");
            Type? settingsHotkeyType = AccessTools.TypeByName("SmartInventoryArrange.SettingsHotkey");

            if (settingsHotkeyType != null)
            {
                _siaHkKeyProp = AccessTools.Property(settingsHotkeyType, "Key");
                _siaHkCtrlProp = AccessTools.Property(settingsHotkeyType, "Ctrl");
                _siaHkAltProp = AccessTools.Property(settingsHotkeyType, "Alt");
                _siaHkShiftProp = AccessTools.Property(settingsHotkeyType, "Shift");
            }

            if (modelType != null && settingsBindingType != null && settingsHotkeyType != null)
            {
                _siaOpenSetter = AccessTools.PropertySetter(modelType, "Open");
                _siaBoundProp = AccessTools.Property(settingsHotkeyType, "Bound");

                MethodInfo? trySet = AccessTools.Method(modelType, "TrySet", new Type[] { settingsBindingType, settingsHotkeyType, typeof(string).MakeByRefType() });
                if (trySet != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(SmartInventoryArrange_TrySet_Prefix));
                    harmony.Patch(trySet, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched SmartInventoryArrange.SettingsHotkeys.TrySet (allows unbinding Open shortcut to None)");
                }
            }

            if (guiType != null)
            {
                _siaModelField = AccessTools.Field(guiType, "Model");
                if (modelType != null)
                {
                    _siaCaptureProp = AccessTools.Property(modelType, "Capture");
                }
                if (settingsHotkeyType != null)
                {
                    _siaNoneKeyValue = AccessTools.Field(settingsHotkeyType, "None")?.GetValue(null);
                }
                if (settingsBindingType != null && settingsHotkeyType != null)
                {
                    _siaBindMethod = AccessTools.Method(guiType, "Bind", new Type[] { settingsBindingType, settingsHotkeyType });
                }

                MethodInfo? captureKey = AccessTools.Method(guiType, "CaptureKey");
                if (captureKey != null)
                {
                    MethodInfo prefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(SmartInventoryArrange_CaptureKey_Prefix));
                    harmony.Patch(captureKey, prefix: new HarmonyMethod(prefix));
                    log?.Log("Patched SmartInventoryArrange.UnifiedSettingsGui.CaptureKey");
                }

                MethodInfo? downHelper = AccessTools.Method(guiType, "<Update>g__Down|167_0");
                if (downHelper != null && _siaHkKeyProp != null)
                {
                    MethodInfo downPrefix = AccessTools.Method(typeof(WorkshopModPatcher), nameof(SmartInventoryArrange_Down_Prefix));
                    harmony.Patch(downHelper, prefix: new HarmonyMethod(downPrefix));
                    log?.Log("Patched SmartInventoryArrange.UnifiedSettingsGui.<Update>g__Down|167_0 (combo & mouse side button support)");
                }
            }

            Type? fixedDropType = AccessTools.TypeByName("SmartInventoryArrange.FixedPlacementDrop");
            if (fixedDropType != null)
            {
                MethodInfo? fixedPrefix = AccessTools.Method(fixedDropType, "Prefix");
                if (fixedPrefix != null)
                {
                    harmony.Patch(
                        fixedPrefix,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(SmartInventoryArrange_FixedDrop_Prefix))),
                        finalizer: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(SmartInventoryArrange_FixedDrop_Finalizer)))
                    );
                    log?.Log("Patched SmartInventoryArrange.FixedPlacementDrop.Prefix (alt placement key redirection)");
                }
            }
        }

        public static bool SmartInventoryArrange_TrySet_Prefix(object __instance, object target, object key, ref string? error, ref bool __result)
        {
            try
            {
                if (target != null && string.Equals(target.ToString(), "Open", StringComparison.OrdinalIgnoreCase))
                {
                    bool isBound = true;
                    if (_siaBoundProp != null)
                    {
                        isBound = (bool)(_siaBoundProp.GetValue(key) ?? true);
                    }
                    else if (_siaHkKeyProp != null)
                    {
                        KeyCode k = (KeyCode)(_siaHkKeyProp.GetValue(key) ?? KeyCode.None);
                        isBound = (k != KeyCode.None);
                    }

                    if (!isBound)
                    {
                        if (_siaOpenSetter != null)
                        {
                            _siaOpenSetter.Invoke(__instance, new object[] { key });
                            error = null;
                            __result = true;
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[WorkshopModPatcher] SmartInventoryArrange TrySet prefix warning: {ex.Message}");
            }
            return true;
        }

        public static bool SmartInventoryArrange_Down_Prefix(object hk, ref bool __result)
        {
            if (!_siaBootSynced)
            {
                _siaBootSynced = true;
                KnownModAdapters.SyncSmartArrangeRuntimeHotkeysFromStore();
            }

            if (hk == null || _siaHkKeyProp == null) return true;

            KeyCode key = (KeyCode)(_siaHkKeyProp.GetValue(hk) ?? KeyCode.None);
            if (key == KeyCode.None)
            {
                __result = false;
                return false;
            }

            bool ctrl = (bool)(_siaHkCtrlProp?.GetValue(hk) ?? false);
            bool alt = (bool)(_siaHkAltProp?.GetValue(hk) ?? false);
            bool shift = (bool)(_siaHkShiftProp?.GetValue(hk) ?? false);

            KeyModifierMask mods = KeyModifierMask.None;
            if (ctrl) mods |= KeyModifierMask.Ctrl;
            if (alt) mods |= KeyModifierMask.Alt;
            if (shift) mods |= KeyModifierMask.Shift;

            __result = ComboKeyStore.EvaluateTargetComboByKind(mods, key, ComboKeyStore.InputQueryKind.Down);
            return false;
        }

        public static void SmartInventoryArrange_FixedDrop_Prefix()
        {
            ComboKeyStore.EnterMethodInputRemapScope(
                "smart_inventory_arrange.alt_placement_key",
                KeyCode.LeftAlt,
                KeyCode.RightAlt,
                KeyModifierMask.None,
                KeyCode.LeftAlt);
        }

        public static void SmartInventoryArrange_FixedDrop_Finalizer()
        {
            ComboKeyStore.ExitMethodInputRemapScope();
        }

        public static bool SmartInventoryArrange_CaptureKey_Prefix(object __instance)
        {
            if (Input.GetKeyDown(KeyCode.Escape) && _siaModelField != null && _siaCaptureProp != null && _siaBindMethod != null && _siaNoneKeyValue != null)
            {
                object? model = _siaModelField.GetValue(__instance);
                if (model != null)
                {
                    object? currentBinding = _siaCaptureProp.GetValue(model);
                    if (currentBinding != null)
                    {
                        _siaBindMethod.Invoke(__instance, new object[] { currentBinding, _siaNoneKeyValue });
                        return false;
                    }
                }
            }
            return true;
        }

        // --- 势力声望拓展 (FactionReputationExpansion) ---
        private static Func<bool>? _repCapturingFn;
        private static Action<KeyCode, bool>? _repSetJournalKeyFn;

        private static void PatchFactionReputationExpansion(Harmony harmony, ModLog? log)
        {
            Type? repModType = AccessTools.TypeByName("FactionReputationExpansion.RepMod");
            if (repModType != null)
            {
                _repCapturingFn = CreateDelegateSafe<Func<bool>>(AccessTools.PropertyGetter(repModType, "CapturingHotkey"));
                _repSetJournalKeyFn = CreateDelegateSafe<Action<KeyCode, bool>>(
                    AccessTools.Method(repModType, "SetJournalKey", new Type[] { typeof(KeyCode), typeof(bool) }));

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
            if (ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            if (ComboKeyStore.IsExplicitlyUnbound("faction_reputation.journal_key"))
            {
                __result = KeyCode.None;
                return;
            }
            if (__result == KeyCode.None) return;
            ComboKeyStore.FilterRuntimeKey(ref __result, "faction_reputation.journal_key");
        }

        public static bool FactionReputation_Tick_Prefix()
        {
            if (_repCapturingFn == null) return true;

            if (!_repCapturingFn()) return true;

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
                _repSetJournalKeyFn?.Invoke(captured.Value, false);
                return false;
            }
            return true;
        }

        public static bool FactionReputation_IsBindableName_Prefix(string? name, ref bool __result)
        {
            if (string.Equals(name, "None", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Mouse1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Mouse2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Mouse3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Mouse4", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Mouse5", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Mouse6", StringComparison.OrdinalIgnoreCase))
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
                        "key_" + optionId,
                        optionId,
                        "Pen_mod_QoL_Tweaks:" + optionId,
                        "Pen_mod_QoL_Tweaks:key_" + optionId,
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

            _penBoxSelectGetterFn = CreateDelegateSafe<Func<KeyCode>>(AccessTools.PropertyGetter(qolType, "BoxSelectModifier"));
            MethodInfo? isBoxDown = AccessTools.Method(qolType, "IsBoxSelectModifierDown");
            if (isBoxDown != null && _penBoxSelectGetterFn != null)
            {
                harmony.Patch(isBoxDown, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(PenQoL_IsBoxSelectModifierDown_Prefix))));
            }

            log?.Log($"Patched PenLabelerAssist.QoLKeyBindings ({patchedGetters} properties + input helpers)");
        }

        private static Func<KeyCode>? _penBoxSelectGetterFn;

        public static bool PenQoL_IsBoxSelectModifierDown_Prefix(ref bool __result)
        {
            if (_penBoxSelectGetterFn == null) return true;
            KeyCode key = _penBoxSelectGetterFn();
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
            __result = ComboKeyStore.IsKeyHeldOrModifierPairRaw(key);
            return false;
        }

        public static void PenQoL_GetKey_Postfix(MethodBase __originalMethod, ref KeyCode __result)
        {
            if (ComboKeyStore.IsBypassed) return;
            if (!PenQoLGetterCandidates.TryGetValue(__originalMethod, out string[]? ids) || ids == null) return;

            ComboKeyStore.EnsureLoaded();
            foreach (var id in ids)
            {
                if (ComboKeyStore.IsExplicitlyUnbound(id))
                {
                    __result = KeyCode.None;
                    return;
                }
            }
            if (__result == KeyCode.None) return;
            ComboKeyStore.FilterRuntimeKey(ref __result, ids);
        }

        public static bool PenQoL_IsBoundKeyDown_Prefix(KeyCode key, ref bool __result)
        {
            if (key == KeyCode.None)
            {
                __result = false;
                return false;
            }
            __result = ComboKeyStore.IsKeyDownOrModifierPairRaw(key);
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
            __result = ComboKeyStore.IsKeyHeldOrModifierPairRaw(key);
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
            if (ComboKeyStore.IsBypassed) return;
            ComboKeyStore.EnsureLoaded();
            if (ComboKeyStore.IsExplicitlyUnbound("clientdemandhighlight.toggle_key") ||
                ComboKeyStore.IsExplicitlyUnbound("hollow.clientdemandhighlight.toggle_key") ||
                ComboKeyStore.IsExplicitlyUnbound("hollow.clientdemandhighlight:toggleKey") ||
                ComboKeyStore.IsExplicitlyUnbound("toggleKey"))
            {
                __result = KeyCode.None;
                return;
            }
            if (__result == KeyCode.None) return;
            ComboKeyStore.FilterRuntimeKeyWithPrefix(
                ref __result,
                "hollow.clientdemandhighlight:",
                "hollow.clientdemandhighlight:toggleKey",
                "hollow.clientdemandhighlight.toggleKey",
                "clientdemandhighlight.toggle_key",
                "toggleKey"
            );
        }

        // --- 精准拖拽分组 (PreciseGroupDrag) ---
        private static void PatchPreciseGroupDrag(Harmony harmony, ModLog? log)
        {
            Type? quickMoveType = AccessTools.TypeByName("PreciseGroupDragMod.GroupQuickMove");
            if (quickMoveType != null)
            {
                MethodInfo? anyHeld = AccessTools.Method(quickMoveType, "AnyQuickKeyHeld");
                if (anyHeld != null)
                {
                    harmony.Patch(anyHeld, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(PreciseGroupDrag_AnyQuickKeyHeld_Prefix))));
                    log?.Log("Patched PreciseGroupDragMod.GroupQuickMove.AnyQuickKeyHeld (held combo & mouse side button support)");
                }
            }

            Type? gateType = AccessTools.TypeByName("PreciseGroupDragMod.PreciseGroupDragGate");
            if (gateType != null)
            {
                MethodInfo? shiftHeld = AccessTools.Method(gateType, "ShiftHeld");
                if (shiftHeld != null)
                {
                    harmony.Patch(shiftHeld, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(PreciseGroupDrag_ShiftHeld_Prefix))));
                    log?.Log("Patched PreciseGroupDragMod.PreciseGroupDragGate.ShiftHeld (custom modifier redirection)");
                }
            }
        }

        public static bool PreciseGroupDrag_AnyQuickKeyHeld_Prefix(ref bool __result)
        {
            __result = KeyBindHelper.GetKey("quick_move_inventory") ||
                       KeyBindHelper.GetKey("quick_move_showcase") ||
                       KeyBindHelper.GetKey("quick_move_storage");
            return false;
        }

        public static bool PreciseGroupDrag_ShiftHeld_Prefix(ref bool __result)
        {
            __result = ComboKeyStore.IsCustomHotkeyHeld("precise_group_drag.shift_held_key", KeyModifierMask.None, KeyCode.LeftShift);
            return false;
        }

        // --- 更好的性能优化 (BetterPerformance) ---
        private static void PatchBetterPerformance(Harmony harmony, ModLog? log)
        {
            Type? hoverPeekType = AccessTools.TypeByName("BetterPerformance.HoverPeek");
            if (hoverPeekType == null) return;

            var prefix = new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(BetterPerformance_HoverPeek_Prefix)));
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(BetterPerformance_HoverPeek_Finalizer)));

            MethodInfo? tick = AccessTools.Method(hoverPeekType, "Tick");
            if (tick != null)
            {
                harmony.Patch(tick, prefix: prefix, finalizer: finalizer);
            }

            MethodInfo? blockTooltip = AccessTools.Method(hoverPeekType, "BlockTooltipPrefix");
            if (blockTooltip != null)
            {
                harmony.Patch(blockTooltip, prefix: prefix, finalizer: finalizer);
            }

            log?.Log("Patched BetterPerformance.HoverPeek (hover peek key redirection)");
        }

        public static void BetterPerformance_HoverPeek_Prefix()
        {
            ComboKeyStore.EnterMethodInputRemapScope(
                "chiqingsan.BetterPerformance.hover_peek_key",
                KeyCode.Mouse2,
                KeyCode.None,
                KeyModifierMask.None,
                KeyCode.Mouse2);
        }

        public static void BetterPerformance_HoverPeek_Finalizer()
        {
            ComboKeyStore.ExitMethodInputRemapScope();
        }

        // --- 物品详情 (ItemDetails) ---
        private static MethodInfo? _itemDetailsExpandKeyGetter;

        private static void PatchItemDetails(Harmony harmony, ModLog? log)
        {
            Type? modSettingsType = AccessTools.TypeByName("ItemDetails.ModSettings");
            if (modSettingsType != null)
            {
                _itemDetailsExpandKeyGetter = AccessTools.PropertyGetter(modSettingsType, "ExpandKey");
            }

            Type? waterColsType = AccessTools.TypeByName("ItemDetails.WaterColumns");
            if (waterColsType != null)
            {
                MethodInfo? expanded = AccessTools.Method(waterColsType, "Expanded");
                if (expanded != null)
                {
                    harmony.Patch(expanded, prefix: new HarmonyMethod(AccessTools.Method(typeof(WorkshopModPatcher), nameof(ItemDetails_WaterColumnsExpanded_Prefix))));
                    log?.Log("Patched ItemDetails.WaterColumns.Expanded (expand key redirection)");
                }
            }
        }

        public static bool ItemDetails_WaterColumnsExpanded_Prefix(ref bool __result)
        {
            KeyCode defaultKey = KeyCode.LeftControl;
            try
            {
                if (_itemDetailsExpandKeyGetter != null)
                {
                    object? rawEnum = _itemDetailsExpandKeyGetter.Invoke(null, null);
                    if (rawEnum != null)
                    {
                        int code = Convert.ToInt32(rawEnum);
                        if (code == 0) defaultKey = KeyCode.LeftShift;
                        else if (code == 2) defaultKey = KeyCode.LeftAlt;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[WorkshopModPatcher] ItemDetails ExpandKey read failed: {ex.Message}");
            }

            __result = ComboKeyStore.IsCustomHotkeyHeld("item_details.expand_key", KeyModifierMask.None, defaultKey);
            return false;
        }
    }
}
