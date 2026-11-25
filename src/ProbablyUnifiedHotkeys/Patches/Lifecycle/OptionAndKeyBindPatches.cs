using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    // =========================================================================
    // 0. 全新统一按键与模组设置中心 UI 挂接补丁
    // =========================================================================

    internal static class VanillaOptionProfileHelper
    {
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            HotkeyProfileManager.ExtraGeneralSettingsProvider = GetVanillaGeneralSettingsSnapshot;
            HotkeyProfileManager.ExtraGeneralSettingApplier = ApplyVanillaGeneralSetting;
        }

        private static IEnumerable<KeyValuePair<string, string>> GetVanillaGeneralSettingsSnapshot()
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                var sections = GameOptionRegistry.Sections;
                if (sections != null)
                {
                    foreach (var sec in sections)
                    {
                        if (sec == null || !GameOptionRegistry.IsVanillaSection(sec.id) || sec.entries == null) continue;
                        if (string.Equals(sec.id, GameOptionRegistry.SECTION_KEYBIND, StringComparison.OrdinalIgnoreCase)) continue;

                        for (int i = 0; i < sec.entries.Count; i++)
                        {
                            var entry = sec.entries[i];
                            if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                            if (entry.id.IndexOf("resolution", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                entry.id.IndexOf("fullscreen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                entry.id.IndexOf("monitor", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                            string? encoded = null;
                            if (entry.kind == GameOptionRegistry.Kind.Toggle && entry.getBool != null)
                            {
                                encoded = entry.getBool() ? "b:1" : "b:0";
                            }
                            else if (entry.kind == GameOptionRegistry.Kind.Slider && entry.getFloat != null)
                            {
                                encoded = "f:" + entry.getFloat().ToString("G7", System.Globalization.CultureInfo.InvariantCulture);
                            }
                            else if (entry.kind == GameOptionRegistry.Kind.Dropdown && entry.getIndex != null)
                            {
                                encoded = "i:" + entry.getIndex().ToString(System.Globalization.CultureInfo.InvariantCulture);
                            }

                            if (encoded != null)
                            {
                                list.Add(new KeyValuePair<string, string>(entry.id, encoded));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"Failed to get vanilla general settings: {ex.Message}");
            }
            return list;
        }

        private static void ApplyVanillaGeneralSetting(string id, string encoded, bool applyToUnderlying)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(encoded)) return;
            try
            {
                var entry = GameOptionRegistry.Find(id);
                if (entry == null) return;

                if (entry.kind == GameOptionRegistry.Kind.Toggle && entry.setBool != null && encoded.StartsWith("b:", StringComparison.OrdinalIgnoreCase))
                {
                    string valPart = encoded.Substring(2).Trim();
                    bool bVal = valPart == "1" || string.Equals(valPart, "true", StringComparison.OrdinalIgnoreCase);
                    if (applyToUnderlying) entry.setBool(bVal);
                }
                else if (entry.kind == GameOptionRegistry.Kind.Slider && entry.setFloat != null && encoded.StartsWith("f:", StringComparison.OrdinalIgnoreCase))
                {
                    string valPart = encoded.Substring(2).Trim();
                    if (float.TryParse(valPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fVal))
                    {
                        if (applyToUnderlying) entry.setFloat(fVal);
                    }
                }
                else if (entry.kind == GameOptionRegistry.Kind.Dropdown && entry.setIndex != null && encoded.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
                {
                    string valPart = encoded.Substring(2).Trim();
                    if (int.TryParse(valPart, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int iVal) && iVal >= 0)
                    {
                        if (applyToUnderlying) entry.setIndex(iVal);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"Failed to apply vanilla setting '{id}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 接管原版游戏设置窗口的打开流程：
    /// 当 OptionUiHost.Active 存在时打开增强版统一设置中心；
    /// 当 OptionUiHost.Active 为 null 时安全降级放行原版选项窗口。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.OpenUI))]
    [HarmonyPriority(Priority.First)]
    public static class Patch_GameOptionUIManager_OpenUI
    {
        static Patch_GameOptionUIManager_OpenUI()
        {
            VanillaOptionProfileHelper.EnsureInitialized();
        }

        public static bool Prefix(GameOptionUIManager __instance)
        {
            try
            {
                if (ModFeatureConfig.DisplayLocation == OptionDisplayLocation.MergedInOptions)
                {
                    var bridge = OptionUiHost.Active;
                    if (bridge != null && bridge.TryOpenEnhancedUI(__instance))
                    {
                        return false;
                    }
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

    [HarmonyPatch(typeof(GameOptionUIManager), "BuildSection", new Type[] { typeof(CustomUIBuilder), typeof(GameOptionRegistry.Section) })]
    public static class Patch_GameOptionUIManager_BuildSection
    {
        public static bool Prefix(CustomUIBuilder builder, GameOptionRegistry.Section section)
        {
            if (ModFeatureConfig.DisplayLocation == OptionDisplayLocation.StandaloneBottomButton &&
                !(OptionUiHost.Active?.IsEnhancedWindowOpen ?? false) &&
                !(OptionUiHost.Active?.IsStandaloneModWindowOpen ?? false))
            {
                if (section == null || !GameOptionRegistry.IsVanillaSection(section.id) ||
                    (!string.IsNullOrEmpty(section.source) && !string.Equals(section.source, "vanilla", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameOptionUIManager), "BuildEntry", new Type[] { typeof(CustomUIBuilder), typeof(GameOptionRegistry.Entry) })]
    public static class Patch_GameOptionUIManager_BuildEntry
    {
        private static readonly HashSet<string> VanillaKeybindWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rotate_cw", "rotate_ccw", "flip_item", "reset_orientation", "trash_item",
            "quick_move_inventory", "quick_move_showcase", "quick_move_storage",
            "open_options", "control_hints", "advance_dialogue", "pin_mode",
            "sort_menu", "call_next_customer", "menu_back", "secondary_click",
            "flip_item_alt"
        };

        internal static bool IsVanillaKeybindId(string? keyId)
        {
            if (string.IsNullOrEmpty(keyId)) return false;
            if (keyId!.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
            {
                keyId = keyId.Substring(4);
            }
            return VanillaKeybindWhitelist.Contains(keyId) || keyId.StartsWith("toolbox_slot_", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Prefix(CustomUIBuilder builder, GameOptionRegistry.Entry entry)
        {
            if (ModFeatureConfig.DisplayLocation == OptionDisplayLocation.StandaloneBottomButton &&
                !(OptionUiHost.Active?.IsEnhancedWindowOpen ?? false) &&
                !(OptionUiHost.Active?.IsStandaloneModWindowOpen ?? false))
            {
                if (entry == null) return false;

                if (!string.IsNullOrEmpty(entry.source) && !string.Equals(entry.source, "vanilla", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (ModOptionScanner.GetEntryExternalAssembly(entry) != null)
                {
                    return false;
                }

                if (entry.kind == GameOptionRegistry.Kind.KeyBind)
                {
                    if (!IsVanillaKeybindId(entry.id))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.OpenUI))]
    public static class Patch_GameOptionUIManager_OpenUI_StandaloneSnapshot
    {
        internal static readonly HashSet<GameObject> PureVanillaContentNodes = new HashSet<GameObject>();
        internal static readonly FieldInfo? WindowField = AccessTools.Field(typeof(GameOptionUIManager), "window");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(GameOptionUIManager __instance)
        {
            if (ModFeatureConfig.DisplayLocation != OptionDisplayLocation.StandaloneBottomButton)
            {
                return;
            }

            if (!PrefsStorage.UseInMemoryPrefsForTests)
            {
                SnapshotVanillaNodesSafe(__instance);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SnapshotVanillaNodesSafe(GameOptionUIManager instance)
        {
            try
            {
                PureVanillaContentNodes.Clear();
                CustomUIWindow? window = WindowField?.GetValue(instance) as CustomUIWindow;
                if (window != null && window.IsAlive)
                {
                    var scrollRect = window.root?.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
                    if (scrollRect != null && scrollRect.content != null)
                    {
                        Transform contentTf = scrollRect.content;
                        for (int i = 0; i < contentTf.childCount; i++)
                        {
                            Transform child = contentTf.GetChild(i);
                            if (child != null)
                            {
                                PureVanillaContentNodes.Add(child.gameObject);
                                for (int j = 0; j < child.childCount; j++)
                                {
                                    Transform sub = child.GetChild(j);
                                    if (sub != null)
                                    {
                                        PureVanillaContentNodes.Add(sub.gameObject);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[StandaloneSnapshot Postfix] Snapshot vanilla nodes warning: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.OpenUI))]
    public static class Patch_GameOptionUIManager_OpenUI_StandalonePost
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(GameOptionUIManager __instance)
        {
            if (ModFeatureConfig.DisplayLocation != OptionDisplayLocation.StandaloneBottomButton)
            {
                return;
            }

            CustomUIWindow? window = null;
            try
            {
                if (!PrefsStorage.UseInMemoryPrefsForTests)
                {
                    window = Patch_GameOptionUIManager_OpenUI_StandaloneSnapshot.WindowField?.GetValue(__instance) as CustomUIWindow;
                    HideForeignNodesSafe(window);
                }

                OptionUiHost.Active?.OnVanillaOptionsWindowOpened(__instance, window);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[StandalonePost Postfix] Error hiding foreign injected nodes or injecting button: {ex.Message}");
            }
            finally
            {
                Patch_GameOptionUIManager_OpenUI_StandaloneSnapshot.PureVanillaContentNodes.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void HideForeignNodesSafe(CustomUIWindow? window)
        {
            if (window == null || !window.IsAlive) return;
            var scrollRect = window.root?.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            if (scrollRect != null && scrollRect.content != null && Patch_GameOptionUIManager_OpenUI_StandaloneSnapshot.PureVanillaContentNodes.Count > 0)
            {
                Transform contentTf = scrollRect.content;
                for (int i = 0; i < contentTf.childCount; i++)
                {
                    Transform child = contentTf.GetChild(i);
                    if (child != null && !Patch_GameOptionUIManager_OpenUI_StandaloneSnapshot.PureVanillaContentNodes.Contains(child.gameObject))
                    {
                        string goName = child.gameObject.name ?? string.Empty;
                        if (!goName.StartsWith("puh_", StringComparison.OrdinalIgnoreCase))
                        {
                            child.gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            Patch_GameOptionUIManager_OpenUI_StandaloneSnapshot.PureVanillaContentNodes.Clear();
        }
    }

    [HarmonyPatch(typeof(GameOptionUIManager), "OnApplySetting")]
    public static class Patch_GameOptionUIManager_OnApplySetting
    {
        private static IDisposable? _bypass;

        [HarmonyPrefix]
        public static void Prefix(GameOptionUIManager __instance)
        {
            try
            {
                var bridge = OptionUiHost.Active;
                if (bridge != null && bridge.IsVanillaStandaloneWindowOpen && __instance != null)
                {
                    _bypass = ComboKeyStore.EnterBypass();
                    bridge.OnBeforeVanillaStandaloneApply(__instance);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_GameOptionUIManager_OnApplySetting Prefix] Error: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(GameOptionUIManager __instance)
        {
            try
            {
                var bridge = OptionUiHost.Active;
                if (bridge != null && bridge.IsVanillaStandaloneWindowOpen && __instance != null)
                {
                    bridge.OnAfterVanillaStandaloneApply(__instance);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_GameOptionUIManager_OnApplySetting Postfix] Error: {ex.Message}");
            }
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            _bypass?.Dispose();
            _bypass = null;
        }
    }

    /// <summary>
    /// 拦截设置界面关闭：当处于按键录制期间或缓冲帧时压制关闭；当存在未保存更改时弹出防丢确认提示框。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.CloseUI))]
    [HarmonyPriority(Priority.First)]
    public static class Patch_GameOptionUIManager_CloseUI
    {
        public static bool Prefix(GameOptionUIManager __instance)
        {
            try
            {
                var bridge = OptionUiHost.Active;
                if (bridge != null)
                {
                    if (bridge.IsCapturingModHotkey || bridge.IsSuppressingEscapeAfterCapture)
                    {
                        return false;
                    }

                    if (bridge.IsEnhancedWindowOpen && bridge.HasUnsavedChanges() && !bridge.IsExplicitWindowClosing)
                    {
                        if (ModFeatureConfig.EnableUnsavedConfirmDialog)
                        {
                            bridge.PromptUnsavedChangesBeforeClosing();
                            return false;
                        }
                        else
                        {
                            bridge.DiscardUnsavedChangesOnDirectClose();
                        }
                    }

                    if (!bridge.IsEnhancedWindowOpen && bridge.IsVanillaStandaloneWindowOpen)
                    {
                        if (bridge.HasVanillaStandaloneUnsavedChanges())
                        {
                            bridge.MarkEscapeConsumed();
                            if (ModFeatureConfig.EnableUnsavedConfirmDialog)
                            {
                                bridge.PromptVanillaStandaloneUnsavedChanges();
                                return false;
                            }
                            else
                            {
                                bridge.DiscardVanillaStandaloneUnsavedChanges();
                            }
                        }
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
            if (bridge != null && (bridge.IsCapturingModHotkey || bridge.IsSuppressingEscapeAfterCapture))
            {
                __result = true;
                return;
            }
            int curFrame = UiSessionState.SafeGetCurrentFrame();
            if (UiSessionState.ConsumedEscapeFrame >= 0 && Math.Abs(curFrame - UiSessionState.ConsumedEscapeFrame) <= 2)
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
    [HarmonyPriority(Priority.First)]
    public static class Patch_CustomUIManager_HandleEscape
    {
        private static readonly FieldInfo? WindowOrderField = AccessTools.Field(typeof(CustomUIManager), "windowOrder");

        public static bool Prefix(CustomUIManager __instance)
        {
            var bridge = OptionUiHost.Active;
            if (bridge != null)
            {
                if (bridge.IsCapturingModHotkey || bridge.IsSuppressingEscapeAfterCapture)
                {
                    return false;
                }

                if (bridge.IsEnhancedWindowOpen && bridge.IsSearchInputFocused && KeyBindHelper.GetBackDown())
                {
                    bridge.ReleaseSearchInputFocus();
                    bridge.MarkEscapeConsumed();
                    return false;
                }

                if (bridge.IsStandaloneModWindowOpen && KeyBindHelper.GetBackDown())
                {
                    var windowOrder = WindowOrderField?.GetValue(__instance) as List<CustomUIWindow>;
                    CustomUIWindow? topWin = null;
                    if (windowOrder != null)
                    {
                        for (int i = windowOrder.Count - 1; i >= 0; i--)
                        {
                            var win = windowOrder[i];
                            if (win != null && win.IsAlive && win.IsVisible)
                            {
                                topWin = win;
                                break;
                            }
                        }
                    }

                    if (topWin != null && topWin == bridge.UnsavedConfirmWindow)
                    {
                        bridge.MarkEscapeConsumed();
                        return true;
                    }

                    if (topWin != null && topWin == bridge.CurrentWindow)
                    {
                        bridge.MarkEscapeConsumed();
                        if (bridge.HasUnsavedChanges())
                        {
                            if (ModFeatureConfig.EnableUnsavedConfirmDialog)
                            {
                                bridge.PromptUnsavedChangesBeforeClosing();
                                return false;
                            }
                        }
                        bridge.DiscardUnsavedChangesOnDirectClose();
                        topWin.Close();
                        return false;
                    }
                }

                if (!bridge.IsEnhancedWindowOpen && bridge.IsVanillaStandaloneWindowOpen && KeyBindHelper.GetBackDown())
                {
                    if (bridge.HasVanillaStandaloneUnsavedChanges())
                    {
                        bridge.MarkEscapeConsumed();
                        if (ModFeatureConfig.EnableUnsavedConfirmDialog)
                        {
                            bridge.PromptVanillaStandaloneUnsavedChanges();
                            return false;
                        }
                        else
                        {
                            bridge.DiscardVanillaStandaloneUnsavedChanges();
                        }
                    }
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
                                if (ModFeatureConfig.EnableUnsavedConfirmDialog)
                                {
                                    bridge.MarkEscapeConsumed();
                                    bridge.PromptUnsavedChangesBeforeClosing();
                                    return false;
                                }
                                else
                                {
                                    bridge.DiscardUnsavedChangesOnDirectClose();
                                }
                            }
                        }
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 隔离原版捕获轮询，避免修改模组快捷键或在统一设置中心录入按键时触发原版 KeyBindHelper 干扰。
    /// </summary>
    [HarmonyPatch(typeof(GameOptionUIManager), "PollKeyCapture")]
    public static class Patch_GameOptionUIManager_PollKeyCapture
    {
        public static bool Prefix()
        {
            var bridge = OptionUiHost.Active;
            if (bridge != null && (bridge.IsEnhancedWindowOpen || bridge.IsStandaloneModWindowOpen || bridge.IsCapturingModHotkey))
            {
                return false;
            }
            return true;
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
                    OptionUiHost.Active?.OnModConfigWindowOpened(__result, mod);
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

            if (!ModFeatureConfig.AllowConflictingKeys &&
                (ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(id) ||
                 ComboKeyStore.IsSecondaryConflictingHotkeySuppressed("vanilla." + id)))
            {
                return false;
            }

            if (ComboKeyStore.IsExplicitlyUnbound(id) ||
                ComboKeyStore.IsExplicitlyUnbound("vanilla." + id) ||
                ComboKeyStore.IsExplicitlyUnbound("key_" + id) ||
                ComboKeyStore.IsExplicitlyUnbound("vanilla.key_" + id))
            {
                return false;
            }

            KeyCode key;
            using (ComboKeyStore.EnterBypass())
            {
                key = KeyBindHelper.Get(id);
            }
            if (key == KeyCode.None)
            {
                return false;
            }

            ComboKeyStore.EnsureLoaded();
            KeyCode effective = key;
            ComboKeyStore.FilterRuntimeKey(ref effective,
                "vanilla." + id,
                id,
                "vanilla.key_" + id,
                "key_" + id,
                id.StartsWith("key_", StringComparison.OrdinalIgnoreCase) ? "vanilla." + id.Substring(4) : "",
                id.StartsWith("key_", StringComparison.OrdinalIgnoreCase) ? id.Substring(4) : "");
            if (effective == KeyCode.None)
            {
                return false;
            }

            return held ? ComboKeyStore.IsKeyHeldRaw(effective) : ComboKeyStore.IsKeyDownRaw(effective);
        }

        internal static bool TryPollMouseButtons(out KeyCode pressed)
        {
            if (!ModFeatureConfig.EnableMouseButtonRebind)
            {
                pressed = KeyCode.None;
                return false;
            }

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
            if (ModFeatureConfig.EnableMouseButtonRebind && KeyBindRuntimeHelper.TryPollMouseButtons(out KeyCode mouseBtn))
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

            // 若关闭鼠标按键改键，单击鼠标右键也视为取消录入
            if (!ModFeatureConfig.EnableMouseButtonRebind && Input.GetMouseButtonDown(1))
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
                if (ModFeatureConfig.EnableEmptyKeyUnbind)
                {
                    key = KeyCode.None;
                    __result = KeyBindHelper.CaptureResult.Key;
                    return false;
                }
                else
                {
                    // 关闭空键解绑时：按 Esc 恢复原版行为——取消录入并保留原按键（仍吸收缓冲）
                    __result = KeyBindHelper.CaptureResult.None;
                    return false;
                }
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
    /// 让原版 KeyBindHelper.Get 在游戏运行时（非 UI / 非保存阶段）按组合键过滤按键。
    /// 当绑定了修饰组合键时，仅在修饰键完全匹配时返回物理 KeyCode，否则返回 KeyCode.None；
    /// 当绑定单键且该单键正被激活的组合键抑制时，返回 KeyCode.None。
    /// 彻底解决 ItemMouseDragHandler 等 InputActionHandler 绕过 GetKeyDown 直接读 KeyCode 导致组合键失效及同主键互斥的问题。
    /// </summary>
    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.Get))]
    public static class Patch_KeyBindHelper_Get
    {
        public static void Postfix(string id, ref KeyCode __result)
        {
            if (ComboKeyStore.IsBypassed || __result == KeyCode.None) return;
            ComboKeyStore.EnsureLoaded();
            ComboKeyStore.FilterRuntimeKey(ref __result,
                "vanilla." + id,
                id,
                "vanilla.key_" + id,
                "key_" + id,
                id.StartsWith("key_", StringComparison.OrdinalIgnoreCase) ? "vanilla." + id.Substring(4) : "",
                id.StartsWith("key_", StringComparison.OrdinalIgnoreCase) ? id.Substring(4) : "");
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
    /// 确保 KeyBindHelper.Save 期间永远以 Bypass 模式读取真实底层 KeyCode 保存至 ES3，绝不落盘 KeyCode.None。
    /// </summary>
    [HarmonyPatch(typeof(KeyBindHelper), nameof(KeyBindHelper.Save))]
    public static class Patch_KeyBindHelper_Save
    {
        private static IDisposable? _bypass;
        public static void Prefix() => _bypass = ComboKeyStore.EnterBypass();
        public static void Finalizer() { _bypass?.Dispose(); _bypass = null; }
    }

    /// <summary>
    /// 确保 ToolboxHelper.SlotLabel 渲染槽位快捷键说明时永远以 Bypass 模式获取真实 KeyCode，杜绝因未按住修饰键导致标签闪烁或丢失。
    /// </summary>
    [HarmonyPatch(typeof(ToolboxHelper), "SlotLabel")]
    public static class Patch_ToolboxHelper_SlotLabel
    {
        private static IDisposable? _bypass;
        public static void Prefix() => _bypass = ComboKeyStore.EnterBypass();
        public static void Finalizer() { _bypass?.Dispose(); _bypass = null; }
    }

    /// <summary>
    /// 修复空按键与组合键的冲突判定：
    /// 1. KeyCode.None 代表未绑定/空按键，永不与其他按键冲突；
    /// 2. 在统一设置中心开启时，放行原版按键查重，由 ConflictDetector 精确计算修饰键差异，允许同主键不同修饰键共存。
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
            var bridge = OptionUiHost.Active;
            if (bridge != null && (bridge.IsEnhancedWindowOpen || bridge.IsStandaloneModWindowOpen))
            {
                // 若允许冲突按键同时生效，放行原版查重；若关闭冲突同时生效，由原版逻辑拦截并提示冲突
                if (ModFeatureConfig.AllowConflictingKeys)
                {
                    __result = null;
                    return false;
                }
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
                if (bridge != null)
                {
                    if (bridge.StartCaptureVanillaOption(__instance, optionId))
                    {
                        return false;
                    }

                    if (optionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
                    {
                        string stripped = optionId.Substring(4);
                        if (bridge.StartCaptureVanillaOption(__instance, stripped))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        string prefixed = "key_" + optionId;
                        if (bridge.StartCaptureVanillaOption(__instance, prefixed))
                        {
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_GameOptionUIManager_StartCapture] Error routing vanilla key capture: {ex.Message}");
            }
            return true;
        }
    }

    /// <summary>
    /// 当游戏存档完成加载进入运行时（ModHook.FireOnGameLoadedLate）时，
    /// 若主菜单阶段曾因第三方模组延迟初始化产生降级条目，且当前选项界面未处于开启状态，
    /// 触发缓存失效重热重扫，实现无感热自愈（如物品堆叠增强等模组）。
    /// </summary>
    [HarmonyPatch(typeof(ModHook), "FireOnGameLoadedLate")]
    public static class Patch_ModHook_FireOnGameLoadedLate
    {
        public static void Postfix()
        {
            try
            {
                if (ModCatalogCache.HasDegradedEntries && !(OptionUiHost.Active?.IsEnhancedWindowOpen ?? false))
                {
                    ModCatalogCache.Invalidate();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[Patch_ModHook_FireOnGameLoadedLate] Error checking degraded entries: {ex.Message}");
            }
        }
    }
}
