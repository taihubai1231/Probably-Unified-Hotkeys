#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 统一按键与模组设置中心顶级窗口总控生命周期管理器：
    /// 实现 IOptionUiBridge 契约，负责离线批量装配主界面、原版设置与模组设置联动、
    /// 语言切换平滑重置，以及防假脏锁死保存与通知链分发。
    /// </summary>
    public sealed class UnifiedOptionWindow : IOptionUiBridge
    {
        public static readonly UnifiedOptionWindow Instance = new UnifiedOptionWindow();

        public static void EnsureBridgeRegistered()
        {
            OptionUiHost.Active = Instance;
            UiSessionState.ApplyAllSettingsCallback = ApplyAllSettings;
            UiSessionState.RefreshConflictsCallback = ConflictAggregationView.RefreshConflicts;
            UiSessionState.RebuildConflictViewCallback = ConflictAggregationView.RebuildConflictView;
            UiSessionState.OnFeatureConfigChangedCallback = OnFeatureConfigChanged;
            I18n.LanguageChanged -= OnExternalLanguageChanged;
            I18n.LanguageChanged += OnExternalLanguageChanged;
        }

        private static void OnExternalLanguageChanged()
        {
            if (UiSessionState.CurrentWindow != null && UiSessionState.CurrentWindow.IsAlive && UiSessionState.CurrentManager != null)
            {
                ReopenEnhancedUIForLanguageSwitch(UiSessionState.CurrentManager, null);
            }
        }

        private static void OnFeatureConfigChanged()
        {
            if (UiSessionState.IsApplyingSettings) return;
            if (UiSessionState.CurrentWindow != null && UiSessionState.CurrentWindow.IsAlive && UiSessionState.CurrentWindow.IsVisible)
            {
                ModDiagnosticView.PerformFullRescanAndRefreshInPlace(
                    forceInvalidateCache: true,
                    targetModId: null,
                    statusMessage: I18n.Get("已更新功能模块设置", "Updated modular feature settings"));
            }
        }

        // =====================================================================
        // IOptionUiBridge 契约实现（共 18 个成员）
        // =====================================================================

        public bool TryOpenEnhancedUI(GameOptionUIManager instance)
        {
            if (instance == null) return false;
            if (ModFeatureConfig.DisplayLocation != OptionDisplayLocation.MergedInOptions)
            {
                return false;
            }
            OpenEnhancedUI(instance);
            return true;
        }

        public bool IsEnhancedWindowOpen => UiSessionState.IsEnhancedWindowOpen;
        public bool IsCapturingAnyKey => UiSessionState.IsCapturingAnyKey;
        public bool IsCapturingModHotkey => UiSessionState.IsCapturingModHotkey;
        public bool IsSuppressingEscapeAfterCapture => UiSessionState.IsSuppressingEscapeAfterCapture;
        public bool IsExplicitWindowClosing => UiSessionState.IsExplicitWindowClosing;
        public bool IsSearchInputFocused => UiSessionState.SearchInputField != null && UiSessionState.SearchInputField.isFocused;

        public void ReleaseSearchInputFocus()
        {
            if (UiSessionState.SearchInputField != null && UiSessionState.SearchInputField.isFocused)
            {
                UiSessionState.SearchInputField.DeactivateInputField();
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            }
        }

        public bool IsInEnhancedRefreshRules
        {
            get => UiSessionState.InEnhancedRefreshRules;
            set => UiSessionState.InEnhancedRefreshRules = value;
        }

        public CustomUIWindow? CurrentWindow => UiSessionState.CurrentWindow;
        public CustomUIWindow? UnsavedConfirmWindow => UiSessionState.UnsavedConfirmWindow;

        public bool HasUnsavedChanges() => UiSessionState.HasUnsavedChanges();

        public void PromptUnsavedChangesBeforeClosing()
        {
            UnsavedConfirmDialog.PromptUnsavedChangesBeforeClosing(ApplyAllSettings);
        }

        public void MarkEscapeConsumed() => UiSessionState.MarkEscapeConsumed();
        public void TickCapture() => KeyCaptureController.TickCapture();

        void IOptionUiBridge.OnVanillaRulesRefreshed() => OnVanillaRulesRefreshed();
        void IOptionUiBridge.OnVanillaKeyChanged() => OnVanillaKeyChanged();
        void IOptionUiBridge.OnBeforeVanillaStandaloneApply(GameOptionUIManager instance) => OnBeforeVanillaStandaloneApply(instance);
        void IOptionUiBridge.OnAfterVanillaStandaloneApply(GameOptionUIManager instance) => OnAfterVanillaStandaloneApply(instance);

        public bool StartCaptureVanillaOption(GameOptionUIManager instance, string optionId)
        {
            return KeyCaptureController.StartCaptureVanillaOption(instance, optionId);
        }

        public bool ShouldSilenceMissingElementWarning(CustomUIWindow instance, string elementId, out CustomUIElement? found)
        {
            return UiSessionState.ShouldSilenceMissingElementWarning(instance, elementId, out found);
        }

        public void ResetSessionState() => UiSessionState.ResetSessionOnClose();

        public bool IsStandaloneModWindowOpen => UiSessionState.IsStandaloneModWindowOpen;
        public bool IsVanillaStandaloneWindowOpen => UiSessionState.IsVanillaStandaloneWindowOpen;

        public bool HasVanillaStandaloneUnsavedChanges()
        {
            return UiSessionState.HasVanillaStandaloneUnsavedChanges();
        }

        public void PromptVanillaStandaloneUnsavedChanges()
        {
            var mgr = UiSessionState.CurrentManager ?? GameOptionUIManager.Instance;
            if (mgr != null)
            {
                UiSessionState.PromptVanillaStandaloneUnsavedChanges(mgr, () =>
                {
                    UiSessionState.VanillaStandaloneWindow?.Close();
                });
            }
            else
            {
                UiSessionState.VanillaStandaloneWindow?.Close();
            }
        }

        public void DiscardVanillaStandaloneUnsavedChanges()
        {
            UiSessionState.DiscardVanillaStandaloneUnsavedChanges();
        }

        public void DiscardUnsavedChangesOnDirectClose()
        {
            UiSessionState.DiscardAllPendingChanges();
        }

        public void OnVanillaOptionsWindowOpened(GameOptionUIManager instance, CustomUIWindow? window = null)
        {
            if (instance == null) return;
            if (ModFeatureConfig.DisplayLocation != OptionDisplayLocation.StandaloneBottomButton) return;

            window ??= UiSessionState.WindowField?.GetValue(instance) as CustomUIWindow;
            if (window == null || !window.IsAlive) return;

            UiSessionState.CurrentManager = instance;
            BindAndRefreshVanillaStandaloneHotkeys(instance, window);
            InjectStandaloneModButtonIntoVanillaOptions(instance, window);
        }

        internal static void BindAndRefreshVanillaStandaloneHotkeys(GameOptionUIManager manager, CustomUIWindow window)
        {
            if (manager == null || window == null || !window.IsAlive) return;
            UiSessionState.CurrentManager = manager;
            UiSessionState.VanillaStandaloneWindow = window;

            if (PrefsStorage.UseInMemoryPrefsForTests) return;

            if (UiSessionState.AllHotkeys == null || UiSessionState.AllHotkeys.Count == 0)
            {
                UiSessionState.ModGroups = ModCatalogCache.GetOrScanGroups(out var scannedHotkeys, out UiSessionState.ScanReport);
                UiSessionState.AllHotkeys = scannedHotkeys;
            }

            foreach (var hk in UiSessionState.AllHotkeys)
            {
                if (hk.ModId == "vanilla" && !string.IsNullOrEmpty(hk.RawOptionId))
                {
                    UiSessionState.VanillaHotkeyByRawId[hk.RawOptionId] = hk;
                    var ui = UiSessionState.GetOrCreateHotkeyUi(hk);
                    ui.ButtonElement = window.Get(hk.RawOptionId);
                    ui.LabelElement = window.Get("label_" + hk.RawOptionId);
                    if (ui.ButtonElement != null)
                    {
                        UiLayoutAndFontHelper.StyleNativeWidthButton(ui.ButtonElement, 160f);
                        UiLayoutAndFontHelper.SetButtonTextFast(ui.ButtonElement, UiLayoutAndFontHelper.FormatHotkeyComboText(hk));
                        UiLayoutAndFontHelper.ApplySingleButtonColor(ui.ButtonElement, hk.EffectiveCombo, hk.IsReadOnlyProbed);
                    }
                    if (ui.LabelElement != null)
                    {
                        UiLayoutAndFontHelper.SetTextIfChanged(ui.LabelElement, UiLayoutAndFontHelper.BuildHotkeyLabelText(hk));
                    }
                }
            }
            UiSessionState.Conflicts = ConflictDetector.Analyze(UiSessionState.AllHotkeys);
        }

        public void OnModConfigWindowOpened(CustomUIWindow window, LoadedMod mod)
        {
            if (window == null || !window.IsAlive || mod?.Manifest == null) return;
            if (!string.Equals(mod.Manifest.ID, ModFeatureConfig.ModId, StringComparison.OrdinalIgnoreCase)) return;

            LocalizeSelfModConfigWindow(window);
        }

        // =====================================================================
        // 主界面离线批量装配主流程
        // =====================================================================

        public static void OpenEnhancedUI(GameOptionUIManager manager)
        {
            UiSessionState.IsBuildingOrOpeningUI = true;
            try
            {
                using (ComboKeyStore.EnterBypass())
                {
                    UiSessionState.ResetSessionForOpen(manager);

                    // 1. 初始化原版环境
                    if (EmporiumOption.Instance != null)
                    {
                        EmporiumOption.Instance.SyncDisplayStateFromScreen();
                    }
                    GameOptionRegistry.EnsureVanilla();
                    GameOptionRegistry.RefreshMonitorInfo();
                    KeyBindHelper.StopCapture();

                    var pendingKeys = UiSessionState.PendingKeysField?.GetValue(manager) as Dictionary<string, KeyCode>;
                    pendingKeys?.Clear();
                    var overriddenDropdowns = UiSessionState.OverriddenDropdownsField?.GetValue(manager) as System.Collections.IDictionary;
                    overriddenDropdowns?.Clear();
                    UiSessionState.SetKeyBindStatusDelegate?.Invoke(manager, string.Empty);

                    // 2. 加载全量模组配置并分析按键冲突
                    UiSessionState.ModGroups = ModCatalogCache.GetOrScanGroups(out var scannedHotkeys, out UiSessionState.ScanReport);
                    UiSessionState.AllHotkeys = scannedHotkeys;
                    foreach (var hk in UiSessionState.AllHotkeys)
                    {
                        if (hk.ModId == "vanilla" && !string.IsNullOrEmpty(hk.RawOptionId))
                        {
                            UiSessionState.VanillaHotkeyByRawId[hk.RawOptionId] = hk;
                        }
                    }
                    UiSessionState.Conflicts = ConflictDetector.Analyze(UiSessionState.AllHotkeys);

            // 3. 构建原生窗口
            bool isStoreUi = StoreUIManager.Instance != null;
            string layer = isStoreUi ? "meta" : "menu";

            var builder = CustomUIManager.Instance.CreateWindow(
                GameOptionUIManager.WINDOW_ID,
                null,
                layer,
                CustomUIManager.Instance.optionUIPrefab,
                CustomUIManager.Style.Auto
            );

            // 若已完成当前视图层级(menu/meta)的原生按钮主题抓取，立即隐藏根对象完成离线构建，避免每生成一个控件就触发 Canvas/Layout 重算
            bool hasThemeForLayer = UiLayoutAndFontHelper.HasDefaultButtonTheme && string.Equals(UiLayoutAndFontHelper.CapturedThemeLayer, layer, StringComparison.Ordinal);
            GameObject? windowRoot = builder.Handle?.root;
            if (hasThemeForLayer && windowRoot != null && windowRoot.activeSelf)
            {
                windowRoot.SetActive(false);
            }

            builder.SetCloseOnEscape(!isStoreUi);
            builder.OnClose(() =>
            {
                UiSessionState.ResetSessionOnClose();
            });

            // 4. 顶部快捷操作栏
            TopSearchAndActionBar.BuildTopBars(builder, layer, windowRoot, hasThemeForLayer);

            // 4.5 模组排查置顶摘要框（位于顶部工具栏之下、ScrollRect 之上；浏览排查列表时固定可见）
            builder.BeginColumn(4f, TextAnchor.UpperLeft);
            UiSessionState.DiagnosticStickyHeaderRoot = builder.CurrentContainer.gameObject;
            var diagStickyVlg = UiSessionState.DiagnosticStickyHeaderRoot.GetComponent<VerticalLayoutGroup>();
            if (diagStickyVlg != null) diagStickyVlg.childForceExpandWidth = true;
            var diagStickyLe = UiSessionState.DiagnosticStickyHeaderRoot.GetComponent<LayoutElement>() ?? UiSessionState.DiagnosticStickyHeaderRoot.AddComponent<LayoutElement>();
            diagStickyLe.flexibleWidth = 1f;
            diagStickyLe.flexibleHeight = 0f;
            UiSessionState.DiagnosticStickyHeaderRoot.SetActive(false);
            builder.End();

            // 5. 滚动视窗核心区域（折叠卡片懒加载，消除打开选项页卡顿）
            builder.BeginScrollFlexible(0f);
            UiSessionState.ScrollRect = builder.CurrentContainer.GetComponentInParent<ScrollRect>(true);
            if (UiSessionState.ScrollRect == null && windowRoot != null)
            {
                UiSessionState.ScrollRect = windowRoot.GetComponentInChildren<ScrollRect>(true);
            }
            if (UiSessionState.ScrollRect != null)
            {
                UiSessionState.ScrollRect.scrollSensitivity = 60f;
                if (UiSessionState.ScrollRect.content != null)
                {
                    var contentVlg = UiSessionState.ScrollRect.content.GetComponent<VerticalLayoutGroup>();
                    if (contentVlg != null) contentVlg.childForceExpandWidth = true;
                }
            }

            // --- A. 常规完整设置视图容器 ---
            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            UiSessionState.NormalContentRoot = builder.CurrentContainer.gameObject;
            var normalColumnRt = builder.CurrentContainer;
            var normalVlg = UiSessionState.NormalContentRoot.GetComponent<VerticalLayoutGroup>();
            if (normalVlg != null) normalVlg.childForceExpandWidth = true;
            var normalLe = UiSessionState.NormalContentRoot.GetComponent<LayoutElement>() ?? UiSessionState.NormalContentRoot.AddComponent<LayoutElement>();
            normalLe.flexibleWidth = 1f;

            // (1) 原版官方设置与快捷键
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            UiSessionState.VanillaHeaderRow = builder.CurrentContainer.gameObject;
            builder.AddSpace(6f);
            string vanillaHdrText = I18n.Get("游戏原生设置与快捷键", "Vanilla Settings & Hotkeys");
            builder.AddHeader(vanillaHdrText, "header_vanilla_sections");
            I18n.ApplyFontIfRuntime(builder.Last?.text, vanillaHdrText);
            builder.End();
            builder.AddSeparator(null, 2);
            UiSessionState.VanillaHeaderSep = CollapsibleCardView.GetLastChildGameObject(normalColumnRt);

            foreach (var section in GameOptionRegistry.Sections)
            {
                if (section == null) continue;
                CollapsibleCardView.BuildVanillaSectionCard(builder, manager, section);
            }

            // (2) 模组区块分割标题
            builder.AddSpace(12f);
            UiSessionState.ModHeaderSpace = CollapsibleCardView.GetLastChildGameObject(normalColumnRt);
            builder.AddSeparator(null, 3);
            UiSessionState.ModHeaderSep = CollapsibleCardView.GetLastChildGameObject(normalColumnRt);
            BuildModHeaderRowWithSort(builder);
            builder.AddSeparator(null, 2);
            UiSessionState.ModHeaderBottomSep = CollapsibleCardView.GetLastChildGameObject(normalColumnRt);

            // (3) 创意工坊模组折叠卡片列表（置于独立子容器以支持热重扫原地重建，初始仅构建头部外壳）
            using (ComboKeyStore.EnterBypass())
            {
                builder.BeginColumn(2f, TextAnchor.UpperLeft);
                UiSessionState.ModCardsContainer = builder.CurrentContainer.gameObject;
                var modCardsVlg = UiSessionState.ModCardsContainer.GetComponent<VerticalLayoutGroup>();
                if (modCardsVlg != null) modCardsVlg.childForceExpandWidth = true;
                var modCardsLe = UiSessionState.ModCardsContainer.GetComponent<LayoutElement>() ?? UiSessionState.ModCardsContainer.AddComponent<LayoutElement>();
                modCardsLe.flexibleWidth = 1f;

                foreach (var group in UiSessionState.ModGroups)
                {
                    CollapsibleCardView.BuildModGroupCardShell(builder, group);
                }
                builder.End();

                // 空搜索结果提示（默认隐藏）
                builder.BeginRow(4f, TextAnchor.MiddleCenter);
                UiSessionState.EmptySearchLabelRow = builder.CurrentContainer.gameObject;
                builder.AddSpace(10f);
                builder.AddFlexibleSpace();
                string emptySearchText = I18n.Get(
                    "<color=#99a3b0>未找到匹配的设置项或按键，可尝试更换关键词、切换搜索范围或顶部显示筛选</color>",
                    "<color=#99a3b0>No matching settings or hotkeys found. Try another keyword, search scope, or display filter.</color>"
                );
                builder.AddLabel(emptySearchText, "label_empty_search");
                I18n.ApplyFontIfRuntime(builder.Last?.text, emptySearchText);
                builder.AddFlexibleSpace();
                builder.AddSpace(10f);
                builder.End();
                UiSessionState.EmptySearchLabelRow.SetActive(false);

                builder.End(); // 结束 NormalContentRoot Column

                // --- B. 冲突专属速览视图容器（初始为空并隐藏，点击“仅看冲突”时按需懒构建） ---
                builder.BeginColumn(2f, TextAnchor.UpperLeft);
                UiSessionState.ConflictContentRoot = builder.CurrentContainer.gameObject;
                var conflictVlg = UiSessionState.ConflictContentRoot.GetComponent<VerticalLayoutGroup>();
                if (conflictVlg != null) conflictVlg.childForceExpandWidth = true;
                var conflictLe = UiSessionState.ConflictContentRoot.GetComponent<LayoutElement>() ?? UiSessionState.ConflictContentRoot.AddComponent<LayoutElement>();
                conflictLe.flexibleWidth = 1f;
                UiSessionState.ConflictContentRoot.SetActive(false);
                builder.End();

                // --- C. 模组排查与疑难接管视图容器（初始为空并隐藏，点击“模组排查”时按需懒构建） ---
                builder.BeginColumn(4f, TextAnchor.UpperLeft);
                UiSessionState.DiagnosticContentRoot = builder.CurrentContainer.gameObject;
                var diagContentVlg = UiSessionState.DiagnosticContentRoot.GetComponent<VerticalLayoutGroup>();
                if (diagContentVlg != null) diagContentVlg.childForceExpandWidth = true;
                var diagContentLe = UiSessionState.DiagnosticContentRoot.GetComponent<LayoutElement>() ?? UiSessionState.DiagnosticContentRoot.AddComponent<LayoutElement>();
                diagContentLe.flexibleWidth = 1f;
                UiSessionState.DiagnosticContentRoot.SetActive(false);
                builder.End();
            }

            builder.End(); // 结束 BeginScrollFlexible

            // 6. 底部操作栏：原生“应用设置”按钮
            builder.AddSeparator(null, 2);
            builder.BeginRow(4f, TextAnchor.MiddleCenter);
            builder.AddSpace(10f);
            builder.AddFlexibleSpace();

            builder.AddButton(LocHelper.GetLocalizedMainMenu("option_apply"), () =>
            {
                ApplyAllSettings(manager);
            }, "apply");
            UiLayoutAndFontHelper.StyleNativeWidthButton(builder.Last, 180f);

            builder.AddFlexibleSpace();
            builder.AddSpace(10f);
            builder.End();

            // 7. 离线装配完成后单次激活展示
            UiSessionState.CurrentWindow = builder.Handle;
            CollapsibleCardView.RestoreSavedExpandedModCards();
            TopSearchAndActionBar.RefreshExpandAllButtonState();
            UiLayoutAndFontHelper.StyleVanillaKeyButtons();
            SyncVanillaPendingFromManager();
            ConflictAggregationView.RefreshConflicts();

            UiSessionState.CurrentWindow = builder.Show();
            UnsavedConfirmDialog.HookWindowCloseButton();
            UiSessionState.CurrentWindowElements = UiSessionState.CustomWindowElementsField?.GetValue(UiSessionState.CurrentWindow) as Dictionary<string, CustomUIElement>;
            UiLayoutAndFontHelper.CachedSuspendableLayoutsDirty = true;
            UiSessionState.WindowField?.SetValue(manager, UiSessionState.CurrentWindow);
            UiLayoutAndFontHelper.SyncActionButtonsAfterShow();
            UiSessionState.SuppressRuleConflictRefresh = true;
            try
            {
                UiSessionState.InvokeRefreshRulesSafe(manager);
            }
            finally
            {
                UiSessionState.SuppressRuleConflictRefresh = false;
            }
            UiSessionState.CaptureOpenBaselineSnapshot();
            UiSessionState.OpenedFrameCount = Time.frameCount;
                }
            }
            finally
            {
                UiSessionState.IsBuildingOrOpeningUI = false;
            }
        }

        // =====================================================================
        // 原版设置与按键色彩同步
        // =====================================================================

        internal static void OnVanillaRulesRefreshed()
        {
            using (ComboKeyStore.EnterBypass())
            {
                var activeWin = UiSessionState.CurrentWindow ?? UiSessionState.VanillaStandaloneWindow;
                if (activeWin == null || UiSessionState.IsApplyingSettings || UiSessionState.SuppressRuleConflictRefresh) return;
                SyncVanillaPendingFromManager();
                SyncVanillaKeyButtonColors();
                ConflictAggregationView.RefreshConflicts();
            }
        }

        internal static void OnVanillaKeyChanged()
        {
            using (ComboKeyStore.EnterBypass())
            {
                var activeWin = UiSessionState.CurrentWindow ?? UiSessionState.VanillaStandaloneWindow;
                if (activeWin == null || UiSessionState.IsApplyingSettings) return;
                SyncVanillaPendingFromManager();
                SyncVanillaKeyButtonColors();
                ConflictAggregationView.RefreshConflicts();
            }
        }

        public void OnBeforeVanillaStandaloneApply(GameOptionUIManager instance)
        {
            var manager = instance ?? UiSessionState.CurrentManager;
            if (manager != null)
            {
                UiSessionState.CurrentManager = manager;
            }

            var pendingKeys = (manager != null && UiSessionState.PendingKeysField != null)
                ? UiSessionState.PendingKeysField.GetValue(manager) as Dictionary<string, KeyCode>
                : null;

            bool comboChanged = false;
            if (UiSessionState.AllHotkeys != null)
            {
                foreach (var hk in UiSessionState.AllHotkeys)
                {
                    if (hk.ModId == "vanilla" && hk.HasPendingChange && !string.IsNullOrEmpty(hk.RawOptionId))
                    {
                        if (pendingKeys != null)
                        {
                            pendingKeys[hk.RawOptionId] = hk.PendingKey;
                        }

                        bool isUnbind = hk.PendingKey == KeyCode.None;
                        ComboKeyStore.SetExplicitlyUnbound(hk.Id, isUnbind);
                        ComboKeyStore.SetExplicitlyUnbound(hk.RawOptionId, isUnbind);
                        if (hk.RawOptionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
                        {
                            ComboKeyStore.SetExplicitlyUnbound(hk.RawOptionId.Substring(4), isUnbind);
                        }

                        KeyModifierMask modsToSave = isUnbind ? KeyModifierMask.None : hk.PendingModifiers;
                        ComboKeyStore.SetSavedCombo(hk.Id, modsToSave, hk.PendingKey);
                        ComboKeyStore.SetSavedCombo(hk.RawOptionId, modsToSave, hk.PendingKey);
                        if (hk.RawOptionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
                        {
                            ComboKeyStore.SetSavedCombo(hk.RawOptionId.Substring(4), modsToSave, hk.PendingKey);
                        }

                        hk.SetKey?.Invoke(hk.PendingKey);
                        comboChanged = true;
                    }
                }
            }

            if (comboChanged)
            {
                ComboKeyStore.Save();
                ComboKeyStore.RebuildRuntimeLookup(UiSessionState.AllHotkeys);
            }
        }

        public void OnAfterVanillaStandaloneApply(GameOptionUIManager instance)
        {
            var manager = instance ?? UiSessionState.CurrentManager;
            var window = UiSessionState.VanillaStandaloneWindow;

            var pendingKeys = (manager != null && UiSessionState.PendingKeysField != null)
                ? UiSessionState.PendingKeysField.GetValue(manager) as Dictionary<string, KeyCode>
                : null;
            pendingKeys?.Clear();

            var overriddenDropdowns = (manager != null && UiSessionState.OverriddenDropdownsField != null)
                ? UiSessionState.OverriddenDropdownsField.GetValue(manager) as System.Collections.IDictionary
                : null;
            overriddenDropdowns?.Clear();

            if (UiSessionState.AllHotkeys != null)
            {
                foreach (var hk in UiSessionState.AllHotkeys)
                {
                    if (hk.ModId == "vanilla")
                    {
                        KeyCode activeKey = hk.PendingKey != KeyCode.None ? hk.PendingKey : (hk.GetKey?.Invoke() ?? KeyCode.None);
                        KeyModifierMask activeMods = hk.PendingKey != KeyCode.None ? hk.PendingModifiers : KeyModifierMask.None;
                        hk.SnapshotKey = activeKey;
                        hk.SnapshotModifiers = activeMods;
                        hk.HasPendingChange = false;
                        hk.PendingKey = KeyCode.None;
                        hk.PendingModifiers = KeyModifierMask.None;
                    }
                }
            }

            ComboKeyStore.RebuildRuntimeLookup(UiSessionState.AllHotkeys);
            if (UiSessionState.AllHotkeys != null)
            {
                UiSessionState.Conflicts = ConflictDetector.Analyze(UiSessionState.AllHotkeys);
            }

            if (!PrefsStorage.UseInMemoryPrefsForTests && manager != null && window != null && window.IsAlive)
            {
                BindAndRefreshVanillaStandaloneHotkeys(manager, window);
                UiSessionState.CaptureVanillaStandaloneBaseline(manager, window);
            }
        }

        internal static void SyncVanillaPendingFromManager()
        {
            using (ComboKeyStore.EnterBypass())
            {
                if (UiSessionState.CurrentManager == null) return;
                var pendingKeys = UiSessionState.PendingKeysField?.GetValue(UiSessionState.CurrentManager) as Dictionary<string, KeyCode>;
                if (pendingKeys == null) return;

                foreach (var hk in UiSessionState.AllHotkeys)
                {
                    if (hk.ModId != "vanilla" || string.IsNullOrEmpty(hk.RawOptionId)) continue;
                    KeyCode currentKey = hk.GetKey?.Invoke() ?? KeyCode.None;
                    KeyModifierMask savedMods = (currentKey == KeyCode.None)
                        ? KeyModifierMask.None
                        : (hk.GetExternalModifiers != null
                            ? hk.GetExternalModifiers.Invoke()
                            : ComboKeyStore.GetSavedModifiers(hk.Id, currentKey));

                    if (pendingKeys.TryGetValue(hk.RawOptionId, out KeyCode pk))
                    {
                        if (hk.HasPendingChange)
                        {
                            // 若用户在此前已由我们增强录键赋予了 PendingModifiers，且 pendingKeys 主键一致，保留已选修饰键
                            if (hk.PendingKey != pk)
                            {
                                hk.PendingKey = pk;
                                hk.PendingModifiers = KeyModifierMask.None;
                            }
                        }
                        else
                        {
                            // 原版外部修改或默认同步
                            if (pk != currentKey)
                            {
                                hk.PendingKey = pk;
                                hk.PendingModifiers = KeyModifierMask.None;
                            }
                            else
                            {
                                // pk 与当前生效键一致且无待保存修改：对齐到当前已持久化修饰键，严禁误刷为 None！
                                hk.PendingKey = pk;
                                hk.PendingModifiers = savedMods;
                            }
                        }

                        if (hk.PendingKey == KeyCode.None && currentKey == KeyCode.None)
                        {
                            hk.HasPendingChange = false;
                            hk.PendingModifiers = KeyModifierMask.None;
                        }
                        else
                        {
                            hk.HasPendingChange = (hk.PendingKey != currentKey || hk.PendingModifiers != savedMods);
                        }
                    }
                    else
                    {
                        // pendingKeys 中无此项：若用户仅修改了修饰键（如 C -> Ctrl+C），主键未变，此时应回填 pendingKeys 确保原版触发保存
                        if (hk.HasPendingChange && hk.PendingKey == currentKey && hk.PendingModifiers != savedMods)
                        {
                            pendingKeys[hk.RawOptionId] = currentKey;
                        }
                        else
                        {
                            hk.HasPendingChange = false;
                            hk.PendingModifiers = KeyModifierMask.None;
                            hk.PendingKey = KeyCode.None;
                        }
                    }
                }
            }
        }

        internal static void SyncVanillaKeyButtonColors()
        {
            using (ComboKeyStore.EnterBypass())
            {
                var activeWin = UiSessionState.CurrentWindow ?? UiSessionState.VanillaStandaloneWindow;
                if (activeWin == null) return;

                foreach (var hk in UiSessionState.AllHotkeys)
                {
                    if (hk.ModId != "vanilla" || string.IsNullOrEmpty(hk.RawOptionId)) continue;

                    var ui = UiSessionState.GetOrCreateHotkeyUi(hk);
                    var labelEl = ui.LabelElement ?? activeWin.Get("label_" + hk.RawOptionId);
                    if (labelEl != null)
                    {
                        ui.LabelElement = labelEl;
                        UiLayoutAndFontHelper.SetTextIfChanged(labelEl, UiLayoutAndFontHelper.BuildHotkeyLabelText(hk));
                    }

                    var uiEl = ui.ButtonElement ?? activeWin.Get(hk.RawOptionId);
                    if (uiEl != null && UiSessionState.CapturingEntry != hk)
                    {
                        ui.ButtonElement = uiEl;
                        UiLayoutAndFontHelper.SetButtonTextFast(uiEl, UiLayoutAndFontHelper.FormatHotkeyComboText(hk));
                        UiLayoutAndFontHelper.ApplySingleButtonColor(uiEl, hk.EffectiveCombo);
                    }

                    if (ui.ConflictButtonElement != null && UiSessionState.CapturingEntry != hk)
                    {
                        UiLayoutAndFontHelper.SetButtonTextFast(ui.ConflictButtonElement, UiLayoutAndFontHelper.FormatHotkeyComboText(hk));
                        UiLayoutAndFontHelper.ApplySingleButtonColor(ui.ConflictButtonElement, hk.EffectiveCombo);
                    }
                }
            }
        }

        // =====================================================================
        // 语言切换平滑重置
        // =====================================================================

        internal static void ReopenEnhancedUIForLanguageSwitch(GameOptionUIManager manager, string? focusModId)
        {
            if (manager == null) return;

            string prevQuery = UiSessionState.SearchQuery;
            SearchScope prevScope = UiSessionState.SearchScope;

            ModCatalogCache.Invalidate();

            if (UiSessionState.IsStandaloneModWindowOpen || ModFeatureConfig.DisplayLocation == OptionDisplayLocation.StandaloneBottomButton)
            {
                var bgVanillaWin = UiSessionState.VanillaStandaloneWindow;
                if (bgVanillaWin != null && bgVanillaWin.IsAlive)
                {
                    OpenStandaloneModUI(manager, bgVanillaWin);
                }
                else
                {
                    OpenEnhancedUI(manager);
                }
            }
            else
            {
                OpenEnhancedUI(manager);
            }

            if (prevScope != SearchScope.All)
            {
                UiSessionState.SearchScope = prevScope;
                UiSessionState.CurrentWindow?.Get("unified_search_scope")?.SetIndex((int)prevScope, false);
            }
            if (!string.IsNullOrEmpty(prevQuery))
            {
                UiSessionState.SearchQuery = prevQuery;
                if (UiSessionState.SearchInputField != null)
                {
                    UiSessionState.SearchInputField.SetTextWithoutNotify(prevQuery);
                }
                TopSearchAndActionBar.ApplySearchFilter();
            }
            else if (!string.IsNullOrEmpty(focusModId))
            {
                ModDiagnosticView.JumpToModInNormalView(focusModId, null);
            }
        }

        // =====================================================================
        // 保存与持久化 (OnApplySetting 挂钩)
        // =====================================================================

        internal static void ApplyAllSettings(GameOptionUIManager manager)
        {
            if (UiSessionState.IsApplyingSettings) return;
            UiSessionState.IsApplyingSettings = true;
            try
            {
                KeyCaptureController.CancelCapture();
                bool wasChinese = I18n.IsChinese;
                bool selfModLanguageChanged = false;
                var prevDisplayLoc = ModFeatureConfig.DisplayLocation;

                bool comboChanged = false;

                // 1. 同步原版 PendingKeys 并写入组合键修饰位（合并与独立模式均完整落盘快捷键）
                var pendingKeys = (manager != null && UiSessionState.PendingKeysField != null)
                    ? UiSessionState.PendingKeysField.GetValue(manager) as Dictionary<string, KeyCode>
                    : null;
                if (UiSessionState.AllHotkeys != null)
                {
                    foreach (var hk in UiSessionState.AllHotkeys)
                    {
                        if (hk.ModId == "vanilla" && hk.HasPendingChange && !string.IsNullOrEmpty(hk.RawOptionId))
                        {
                            if (pendingKeys != null)
                            {
                                pendingKeys[hk.RawOptionId] = hk.PendingKey;
                            }

                            bool isUnbind = hk.PendingKey == KeyCode.None;
                            ComboKeyStore.SetExplicitlyUnbound(hk.Id, isUnbind);
                            ComboKeyStore.SetExplicitlyUnbound(hk.RawOptionId, isUnbind);
                            if (hk.RawOptionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
                            {
                                ComboKeyStore.SetExplicitlyUnbound(hk.RawOptionId.Substring(4), isUnbind);
                            }

                            ComboKeyStore.SetSavedCombo(hk.Id, hk.PendingModifiers, hk.PendingKey);
                            ComboKeyStore.SetSavedCombo(hk.RawOptionId, hk.PendingModifiers, hk.PendingKey);
                            if (hk.RawOptionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
                            {
                                ComboKeyStore.SetSavedCombo(hk.RawOptionId.Substring(4), hk.PendingModifiers, hk.PendingKey);
                            }
                            hk.SetKey?.Invoke(hk.PendingKey);
                            comboChanged = true;
                            hk.SnapshotKey = hk.PendingKey;
                            hk.SnapshotModifiers = hk.PendingModifiers;
                            hk.PendingModifiers = KeyModifierMask.None;
                            hk.PendingKey = KeyCode.None;
                            hk.HasPendingChange = false;
                        }
                    }
                }

                if (comboChanged)
                {
                    using (ComboKeyStore.EnterBypass())
                    {
                        if (!PrefsStorage.UseInMemoryPrefsForTests)
                        {
                            SaveKeyBindHelperNoInline();
                        }
                        ComboKeyStore.Save();
                    }
                    ComboKeyStore.RebuildRuntimeLookup(UiSessionState.AllHotkeys);
                }

                // 2. 仅在合并选项页模式下执行原版原生保存逻辑（独立模式下不触碰原版常规设置滑块）
                if (!UiSessionState.IsStandaloneModWindowOpen && manager != null)
                {
                    using (ComboKeyStore.EnterBypass())
                    {
                        UiSessionState.OriginalApplySettingDelegate?.Invoke(manager);
                    }
                    pendingKeys?.Clear();
                }

                if (!PrefsStorage.UseInMemoryPrefsForTests && manager != null && UiSessionState.VanillaStandaloneWindow != null && UiSessionState.VanillaStandaloneWindow.IsAlive)
                {
                    BindAndRefreshVanillaStandaloneHotkeys(manager, UiSessionState.VanillaStandaloneWindow);
                }

            // 3. 保存所有发生变动的模组常规配置与模组快捷键
            var touchedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in UiSessionState.ModGroups)
            {
                bool groupChanged = false;

                foreach (var gen in group.GeneralSettings)
                {
                    if (gen.PendingBool.HasValue || gen.PendingFloat.HasValue || gen.PendingIndex.HasValue || gen.PendingString != null)
                    {
                        if (string.Equals(group.ModId, I18n.ModId, StringComparison.OrdinalIgnoreCase))
                        {
                            selfModLanguageChanged = true;
                        }
                        try
                        {
                            gen.ApplyPending();
                            groupChanged = true;
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Warn($"Failed to apply pending general setting for mod '{group.ModId}' ({gen.Id}): {ex.Message}");
                        }
                    }
                }

                foreach (var hotkey in group.Hotkeys)
                {
                    if (hotkey.HasPendingChange)
                    {
                        try
                        {
                            bool isUnbind = hotkey.PendingKey == KeyCode.None;
                            ComboKeyStore.SetExplicitlyUnbound(hotkey.Id, isUnbind);

                            if (hotkey.SetCombo != null)
                            {
                                hotkey.SetCombo(hotkey.PendingModifiers, hotkey.PendingKey);
                                ComboKeyStore.SetSavedCombo(hotkey.Id, KeyModifierMask.None, KeyCode.None);
                                comboChanged = true;
                            }
                            else
                            {
                                hotkey.SetKey?.Invoke(hotkey.PendingKey);
                                ComboKeyStore.SetSavedCombo(hotkey.Id, hotkey.PendingModifiers, hotkey.PendingKey);
                                comboChanged = true;
                            }
                            hotkey.PendingModifiers = KeyModifierMask.None;
                            hotkey.PendingKey = KeyCode.None;
                            hotkey.HasPendingChange = false;
                            groupChanged = true;
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Warn($"Failed to apply pending hotkey for mod '{group.ModId}' ({hotkey.Id}): {ex.Message}");
                        }
                    }
                }

                if (groupChanged)
                {
                    touchedModIds.Add(group.ModId);
                    if (group.ExtraNotifyModIds != null)
                    {
                        foreach (var extraId in group.ExtraNotifyModIds)
                        {
                            if (!string.IsNullOrEmpty(extraId))
                            {
                                touchedModIds.Add(extraId);
                            }
                        }
                    }
                }
            }

            // 3B. 保存卡片排序模式与自定义拖拽顺序
            if (UiSessionState.HasCardOrderOrSortModeChanged())
            {
                try
                {
                    ModFeatureConfig.SetCardSortMode(UiSessionState.CurrentCardSortMode);
                    ComboKeyStore.SetSavedCustomCardOrder(UiSessionState.GetCurrentVisualModIdOrder());
                    comboChanged = true;
                }
                catch (Exception ex)
                {
                    ModLogger.Warn($"Failed to save card sort mode/order: {ex.Message}");
                }
            }

            if (comboChanged)
            {
                try
                {
                    ComboKeyStore.Save();
                }
                catch (Exception ex)
                {
                    ModLogger.Warn($"Failed to save ComboKeyStore: {ex.Message}");
                }
            }
            try
            {
                ComboKeyStore.RebuildRuntimeLookup(UiSessionState.AllHotkeys);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to rebuild runtime lookup: {ex.Message}");
            }

            // 4. 触发通知链，使第三方模组即时热生效
            foreach (var modId in touchedModIds)
            {
                NotifyModConfigApplied(modId);
            }

            if (PrefsStorage.UseInMemoryPrefsForTests)
            {
                UiSessionState.HasUserInteractedInSession = false;
                return;
            }

            ApplyAllSettingsVisualRefreshNoInline(manager, prevDisplayLoc, wasChinese, selfModLanguageChanged, touchedModIds, comboChanged);
            UiSessionState.HasUserInteractedInSession = false;
            }
            finally
            {
                UiSessionState.IsApplyingSettings = false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyAllSettingsVisualRefreshNoInline(GameOptionUIManager? manager, OptionDisplayLocation prevDisplayLoc, bool wasChinese, bool selfModLanguageChanged, HashSet<string> touchedModIds, bool comboChanged)
        {
            if (prevDisplayLoc != ModFeatureConfig.DisplayLocation)
            {
                var bgVanillaWin = UiSessionState.VanillaStandaloneWindow;
                UiSessionState.VanillaStandaloneWindow = null;
                UiSessionState.SuppressWakeVanillaOnStandaloneClose = true;

                UnsavedConfirmDialog.CloseEnhancedWindowSafely(() =>
                {
                    if (bgVanillaWin != null && bgVanillaWin.IsAlive)
                    {
                        UiSessionState.IsExplicitVanillaClosing = true;
                        try
                        {
                            bgVanillaWin.Close();
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Debug($"bgVanillaWin.Close error: {ex.Message}");
                        }
                        finally
                        {
                            UiSessionState.IsExplicitVanillaClosing = false;
                        }
                    }

                    UiSessionState.SuppressWakeVanillaOnStandaloneClose = false;
                    manager?.OpenUI();
                });
                return;
            }

            if (wasChinese != I18n.IsChinese)
            {
                ReopenEnhancedUIForLanguageSwitch(manager!, selfModLanguageChanged ? I18n.ModId : null);
                return;
            }

            // 7. 本模组自身设置变动时的轻量就地刷新（保留展开状态，刷新控件显隐）
            if (selfModLanguageChanged || touchedModIds.Contains(ModFeatureConfig.ModId) || comboChanged)
            {
                if (UiSessionState.SelfProfileSectionContainer != null)
                {
                    UiSessionState.SelfProfileSectionContainer.SetActive(ModFeatureConfig.ShowProfilePresetCard);
                }
                if (UiSessionState.CardSortDropdown?.root != null)
                {
                    UiSessionState.CardSortDropdown.SetVisible(ModFeatureConfig.ShowCardSortDropdown);
                }
                SyncCardSortDropdownAppearance();

                for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
                {
                    var g = UiSessionState.ModGroups[i];
                    if (UiSessionState.TryGetCardParts(g, out var p) && p.DragHandleElement?.root != null)
                    {
                        p.DragHandleElement.SetVisible(ModFeatureConfig.AllowCardDragReorder);
                    }
                }
                TopSearchAndActionBar.RefreshToolbarVisibilityInPlace();
            }

            // 5. 重新刷新基准快照 (Baseline Snapshot) 与全量条目状态复位
            try
            {
                UiSessionState.RefreshBaselineSnapshotsAfterApply(manager);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to refresh baseline snapshots after apply: {ex.Message}");
            }

            try
            {
                SyncVanillaKeyButtonColors();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to sync vanilla key button colors: {ex.Message}");
            }

            // 6. 重新刷新冲突与颜色状态
            try
            {
                ConflictAggregationView.RefreshConflicts();
                if (UiSessionState.IsConflictViewActive)
                {
                    ConflictAggregationView.RebuildConflictView();
                }
                if (touchedModIds.Count > 0 || comboChanged)
                {
                    UiSessionState.DiagnosticViewDirty = true;
                    if (UiSessionState.IsDiagnosticViewActive)
                    {
                        ModDiagnosticView.RebuildDiagnosticView();
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to refresh conflicts after apply: {ex.Message}");
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void SaveKeyBindHelperNoInline()
        {
            KeyBindHelper.Save();
        }

        private static void NotifyModConfigApplied(string modId)
        {
            try
            {
                if (ModHook.Instance != null && UiSessionState.FireModConfigAppliedDelegate != null)
                {
                    UiSessionState.FireModConfigAppliedDelegate.Invoke(modId);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to notify mod config applied for {modId}: {ex.Message}");
            }
        }

        internal static void BuildModHeaderRowWithSort(CustomUIBuilder builder)
        {
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            UiSessionState.ModHeaderRow = builder.CurrentContainer.gameObject;
            builder.AddSpace(6f);

            string modHdrText = I18n.Get("创意工坊模组设置与快捷键", "Mod Settings & Hotkeys");
            builder.AddHeader(modHdrText, "header_mod_sections");
            I18n.ApplyFontIfRuntime(builder.Last?.text, modHdrText);

            builder.AddFlexibleSpace();

            if (ModFeatureConfig.ShowCardSortDropdown)
            {
                builder.AddDropdown(
                    ModFeatureConfig.GetCardSortModeChoices(),
                    (int)UiSessionState.CurrentCardSortMode,
                    OnCardSortModeChanged,
                    "mod_card_sort_dropdown"
                );
                UiSessionState.CardSortDropdown = builder.Last;
                if (UiSessionState.CardSortDropdown != null)
                {
                    UiSessionState.CardSortDropdown.SetFixedWidth(150f);
                    UiSessionState.CardSortDropdown.SetPreferredSize(150f, 32f);
                    I18n.ApplyFontToDropdown(UiSessionState.CardSortDropdown, forceCjkCapableList: I18n.IsChinese);
                }
            }

            builder.AddSpace(6f);
            builder.End();
        }

        private static void OnCardSortModeChanged(int index)
        {
            if (index < 0 || index >= ModFeatureConfig.GetCardSortModeChoices().Count) return;

            var newMode = (ModCardSortMode)index;
            UiSessionState.CurrentCardSortMode = newMode;
            UiSessionState.IsSortModeExplicitlyChosenByUser = true;

            ComboKeyStore.SortModOptionGroups(UiSessionState.ModGroups, newMode, null, ModFeatureConfig.PinOwnCardToTop);

            for (int i = 0; i < UiSessionState.ModGroups.Count; i++)
            {
                var g = UiSessionState.ModGroups[i];
                if (UiSessionState.TryGetCardParts(g, out var parts) && parts.CardRootObject != null)
                {
                    parts.CardRootObject.transform.SetSiblingIndex(i);
                }
            }

            UiSessionState.CheckCardOrderDirtyState();
        }

        public static void SyncCardSortDropdownAppearance()
        {
            if (UiSessionState.CardSortDropdown?.dropdown != null)
            {
                UiSessionState.CardSortDropdown.dropdown.SetValueWithoutNotify((int)UiSessionState.CurrentCardSortMode);
            }
        }

        private static void InjectStandaloneModButtonIntoVanillaOptions(GameOptionUIManager manager, CustomUIWindow window)
        {
            try
            {
                UiSessionState.VanillaStandaloneWindow = window;
                UiSessionState.CaptureVanillaStandaloneBaseline(manager, window);

                var vanillaScroll = window.root?.GetComponentInChildren<ScrollRect>(true);
                if (vanillaScroll != null)
                {
                    vanillaScroll.scrollSensitivity = 60f;
                }

                if (window.frame?.closeButton != null)
                {
                    window.frame.closeButton.onClick.RemoveAllListeners();
                    window.frame.closeButton.onClick.AddListener(() =>
                    {
                        if (UiSessionState.HasVanillaStandaloneUnsavedChanges() && ModFeatureConfig.EnableUnsavedConfirmDialog)
                        {
                            UiSessionState.PromptVanillaStandaloneUnsavedChanges(manager, () => {
                                if (UiSessionState.VanillaStandaloneWindow == window)
                                {
                                    UiSessionState.VanillaStandaloneWindow = null;
                                }
                                window.Close();
                            });
                        }
                        else
                        {
                            if (UiSessionState.VanillaStandaloneWindow == window)
                            {
                                UiSessionState.VanillaStandaloneWindow = null;
                            }
                            window.Close();
                        }
                    });
                }

                var applyEl = window.Get("apply");
                if (applyEl?.root == null) return;
                var parentRt = applyEl.root.transform.parent as RectTransform;
                if (parentRt == null) return;

                var existingBtn = window.Get("puh_btn_open_mods");
                string btnText = I18n.Get("模组设置", "Mod Settings");

                if (existingBtn != null && existingBtn.button != null)
                {
                    existingBtn.SetText(btnText);
                    existingBtn.SetVisible(true);
                    existingBtn.root?.transform.SetAsLastSibling();
                    return;
                }

                if (CustomUIManager.Instance == null) return;
                var builder = new CustomUIBuilder(CustomUIManager.Instance, window, parentRt);
                builder.AddButton(btnText, () =>
                {
                    window.root?.SetActive(false);
                    OpenStandaloneModUI(manager, window);
                }, "puh_btn_open_mods");

                var newBtnEl = builder.Last;
                if (newBtnEl != null)
                {
                    UiLayoutAndFontHelper.StyleActionButton(newBtnEl, 140f, 36f, lrMargin: 8f, tbMargin: 3f, fontScale: 0.85f);
                    newBtnEl.SetVisible(true);
                    if (newBtnEl.root != null)
                    {
                        var le = newBtnEl.root.GetComponent<LayoutElement>() ?? newBtnEl.root.AddComponent<LayoutElement>();
                        le.ignoreLayout = true;
                        if (newBtnEl.root.transform is RectTransform rt)
                        {
                            rt.anchorMin = new Vector2(0f, 0.5f);
                            rt.anchorMax = new Vector2(0f, 0.5f);
                            rt.pivot = new Vector2(0f, 0.5f);
                            rt.sizeDelta = new Vector2(140f, 36f);
                            rt.anchoredPosition = new Vector2(10f, 0f);
                        }
                        newBtnEl.root.transform.SetAsLastSibling();
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[UnifiedOptionWindow] Error injecting standalone mod button: {ex.Message}");
            }
        }

        private static void LocalizeSelfModConfigWindow(CustomUIWindow window)
        {
            try
            {
                var langEl = window.Get("drop_ui_language") ?? window.Get("ui_language");
                if (langEl?.dropdown != null)
                {
                    var choices = BilingualText.GetLanguageChoices();
                    for (int i = 0; i < langEl.dropdown.options.Count && i < choices.Count; i++)
                    {
                        langEl.dropdown.options[i].text = choices[i];
                    }
                    langEl.dropdown.RefreshShownValue();
                    I18n.ApplyFontToDropdown(langEl, forceCjkCapableList: true);
                }

                var locEl = window.Get("drop_option_display_location") ?? window.Get("option_display_location");
                if (locEl?.dropdown != null)
                {
                    var choices = ModFeatureConfig.GetDisplayLocationChoices();
                    for (int i = 0; i < locEl.dropdown.options.Count && i < choices.Count; i++)
                    {
                        locEl.dropdown.options[i].text = choices[i];
                    }
                    locEl.dropdown.RefreshShownValue();
                    I18n.ApplyFontToDropdown(locEl, forceCjkCapableList: I18n.IsChinese);
                }

                var sortEl = window.Get("drop_mod_card_sort_mode") ?? window.Get("mod_card_sort_mode");
                if (sortEl?.dropdown != null)
                {
                    var choices = ModFeatureConfig.GetCardSortModeChoices();
                    for (int i = 0; i < sortEl.dropdown.options.Count && i < choices.Count; i++)
                    {
                        sortEl.dropdown.options[i].text = choices[i];
                    }
                    sortEl.dropdown.RefreshShownValue();
                    I18n.ApplyFontToDropdown(sortEl, forceCjkCapableList: I18n.IsChinese);
                }

                var debounceEl = window.Get("input_search_debounce_ms") ?? window.Get("search_debounce_ms");
                if (debounceEl != null)
                {
                    UiLayoutAndFontHelper.ConfigureFixedWidthControl(debounceEl, 92f, 32f);
                    if (debounceEl.input?.textComponent != null)
                    {
                        debounceEl.input.textComponent.alignment = TextAlignmentOptions.Center;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"LocalizeSelfModConfigWindow warning: {ex.Message}");
            }
        }

        public static void OpenStandaloneModUI(GameOptionUIManager manager, CustomUIWindow vanillaWindow)
        {
            UiSessionState.IsStandaloneModWindowOpen = true;
            UiSessionState.IsBuildingOrOpeningUI = true;

            try
            {
                using (ComboKeyStore.EnterBypass())
                {
                    UiSessionState.ResetSessionForOpen(manager);

                    UiSessionState.ModGroups = ModCatalogCache.GetOrScanGroups(out var scannedHotkeys, out UiSessionState.ScanReport);
                    UiSessionState.AllHotkeys = scannedHotkeys;
                    foreach (var hk in UiSessionState.AllHotkeys)
                    {
                        if (hk.ModId == "vanilla" && !string.IsNullOrEmpty(hk.RawOptionId))
                        {
                            UiSessionState.VanillaHotkeyByRawId[hk.RawOptionId] = hk;
                        }
                    }
                    UiSessionState.Conflicts = ConflictDetector.Analyze(UiSessionState.AllHotkeys);

                    bool isStoreUi = StoreUIManager.Instance != null;
                    string layer = isStoreUi ? "meta" : "menu";

                    var builder = CustomUIManager.Instance.CreateWindow(
                        "puh_mod_option_ui",
                        I18n.Get("创意工坊模组设置与快捷键", "Mod Settings & Hotkeys"),
                        layer,
                        CustomUIManager.Instance.optionUIPrefab,
                        CustomUIManager.Style.Auto
                    );

                    GameObject? windowRoot = builder.Handle?.root;
                    builder.SetCloseOnEscape(true);
                    builder.OnClose(() =>
                    {
                        UiSessionState.IsStandaloneModWindowOpen = false;
                        UiSessionState.ResetSessionOnClose();
                        if (!UiSessionState.SuppressWakeVanillaOnStandaloneClose && vanillaWindow != null && vanillaWindow.IsAlive)
                        {
                            vanillaWindow.root?.SetActive(true);
                            BindAndRefreshVanillaStandaloneHotkeys(manager, vanillaWindow);
                        }
                    });

                    TopSearchAndActionBar.BuildTopBars(builder, layer, windowRoot, hasThemeForLayer: false);

                    builder.BeginColumn(4f, TextAnchor.UpperLeft);
                    UiSessionState.DiagnosticStickyHeaderRoot = builder.CurrentContainer.gameObject;
                    var diagStickyVlg = UiSessionState.DiagnosticStickyHeaderRoot.GetComponent<VerticalLayoutGroup>();
                    if (diagStickyVlg != null) diagStickyVlg.childForceExpandWidth = true;
                    var diagStickyLe = UiSessionState.DiagnosticStickyHeaderRoot.GetComponent<LayoutElement>() ?? UiSessionState.DiagnosticStickyHeaderRoot.AddComponent<LayoutElement>();
                    diagStickyLe.flexibleWidth = 1f;
                    diagStickyLe.flexibleHeight = 0f;
                    UiSessionState.DiagnosticStickyHeaderRoot.SetActive(false);
                    builder.End();

                    builder.BeginScrollFlexible(0f);
                    UiSessionState.ScrollRect = builder.CurrentContainer.GetComponentInParent<ScrollRect>(true);
                    if (UiSessionState.ScrollRect != null)
                    {
                        UiSessionState.ScrollRect.scrollSensitivity = 60f;
                        if (UiSessionState.ScrollRect.content != null)
                        {
                            var contentVlg = UiSessionState.ScrollRect.content.GetComponent<VerticalLayoutGroup>();
                            if (contentVlg != null) contentVlg.childForceExpandWidth = true;
                        }
                    }

                    builder.BeginColumn(2f, TextAnchor.UpperLeft);
                    UiSessionState.NormalContentRoot = builder.CurrentContainer.gameObject;
                    var normalVlg = UiSessionState.NormalContentRoot.GetComponent<VerticalLayoutGroup>();
                    if (normalVlg != null) normalVlg.childForceExpandWidth = true;
                    var normalLe = UiSessionState.NormalContentRoot.GetComponent<LayoutElement>() ?? UiSessionState.NormalContentRoot.AddComponent<LayoutElement>();
                    normalLe.flexibleWidth = 1f;

                    BuildModHeaderRowWithSort(builder);
                    builder.AddSeparator(null, 2);
                    UiSessionState.ModHeaderBottomSep = CollapsibleCardView.GetLastChildGameObject(builder.CurrentContainer);

                    builder.BeginColumn(2f, TextAnchor.UpperLeft);
                    UiSessionState.ModCardsContainer = builder.CurrentContainer.gameObject;
                    var modCardsVlg = UiSessionState.ModCardsContainer.GetComponent<VerticalLayoutGroup>();
                    if (modCardsVlg != null) modCardsVlg.childForceExpandWidth = true;
                    var modCardsLe = UiSessionState.ModCardsContainer.GetComponent<LayoutElement>() ?? UiSessionState.ModCardsContainer.AddComponent<LayoutElement>();
                    modCardsLe.flexibleWidth = 1f;

                    foreach (var group in UiSessionState.ModGroups)
                    {
                        if (group == null) continue;
                        CollapsibleCardView.BuildModGroupCardShell(builder, group);
                    }
                    builder.End();

                    // 空搜索提示
                    builder.BeginRow(4f, TextAnchor.MiddleCenter);
                    UiSessionState.EmptySearchLabelRow = builder.CurrentContainer.gameObject;
                    builder.AddSpace(10f);
                    builder.AddFlexibleSpace();
                    string emptySearchText = I18n.Get(
                        "<color=#99a3b0>未找到匹配的设置项或按键，可尝试更换关键词、切换搜索范围或顶部显示筛选</color>",
                        "<color=#99a3b0>No matching settings or hotkeys found. Try another keyword, search scope, or display filter.</color>"
                    );
                    builder.AddLabel(emptySearchText, "label_empty_search");
                    I18n.ApplyFontIfRuntime(builder.Last?.text, emptySearchText);
                    builder.AddFlexibleSpace();
                    builder.AddSpace(10f);
                    builder.End();
                    UiSessionState.EmptySearchLabelRow.SetActive(false);

                    builder.End(); // 结束 NormalContentRoot Column

                    // 冲突与排查视图
                    builder.BeginColumn(2f, TextAnchor.UpperLeft);
                    UiSessionState.ConflictContentRoot = builder.CurrentContainer.gameObject;
                    var conflictVlg = UiSessionState.ConflictContentRoot.GetComponent<VerticalLayoutGroup>();
                    if (conflictVlg != null) conflictVlg.childForceExpandWidth = true;
                    var conflictLe = UiSessionState.ConflictContentRoot.GetComponent<LayoutElement>() ?? UiSessionState.ConflictContentRoot.AddComponent<LayoutElement>();
                    conflictLe.flexibleWidth = 1f;
                    UiSessionState.ConflictContentRoot.SetActive(false);
                    builder.End();

                    builder.BeginColumn(4f, TextAnchor.UpperLeft);
                    UiSessionState.DiagnosticContentRoot = builder.CurrentContainer.gameObject;
                    var diagContentVlg = UiSessionState.DiagnosticContentRoot.GetComponent<VerticalLayoutGroup>();
                    if (diagContentVlg != null) diagContentVlg.childForceExpandWidth = true;
                    var diagContentLe = UiSessionState.DiagnosticContentRoot.GetComponent<LayoutElement>() ?? UiSessionState.DiagnosticContentRoot.AddComponent<LayoutElement>();
                    diagContentLe.flexibleWidth = 1f;
                    UiSessionState.DiagnosticContentRoot.SetActive(false);
                    builder.End();

                    builder.End(); // 结束 ScrollFlexible

                    // 底部操作栏：返回与应用（左中绝对锚定 140f 返回按钮，中间 FlexSpace 绝对居中 180f 应用按钮）
                    builder.AddSeparator(null, 2);
                    builder.BeginRow(4f, TextAnchor.MiddleCenter);
                    builder.AddFlexibleSpace();

                    builder.AddButton(LocHelper.GetLocalizedMainMenu("option_apply"), () =>
                    {
                        ApplyAllSettings(manager);
                    }, "apply");
                    UiLayoutAndFontHelper.StyleNativeWidthButton(builder.Last, 180f);

                    builder.AddFlexibleSpace();

                    builder.AddButton(I18n.Get("返回原版设置", "Back to Vanilla"), () =>
                    {
                        if (UiSessionState.HasUnsavedChanges() && ModFeatureConfig.EnableUnsavedConfirmDialog)
                        {
                            UnsavedConfirmDialog.PromptUnsavedChangesBeforeClosing(ApplyAllSettings);
                        }
                        else
                        {
                            builder.Handle?.Close();
                        }
                    }, "puh_btn_back_vanilla");

                    var backBtnEl = builder.Last;
                    if (backBtnEl != null)
                    {
                        UiLayoutAndFontHelper.StyleActionButton(backBtnEl, 140f, 36f, lrMargin: 8f, tbMargin: 3f, fontScale: 0.85f);
                        if (backBtnEl.root != null)
                        {
                            var le = backBtnEl.root.GetComponent<LayoutElement>() ?? backBtnEl.root.AddComponent<LayoutElement>();
                            le.ignoreLayout = true;
                            if (backBtnEl.root.transform is RectTransform rt)
                            {
                                rt.anchorMin = new Vector2(0f, 0.5f);
                                rt.anchorMax = new Vector2(0f, 0.5f);
                                rt.pivot = new Vector2(0f, 0.5f);
                                rt.sizeDelta = new Vector2(140f, 36f);
                                rt.anchoredPosition = new Vector2(10f, 0f);
                            }
                        }
                    }
                    builder.End();

                    UiSessionState.CurrentWindow = builder.Handle;
                    CollapsibleCardView.RestoreSavedExpandedModCards();
                    TopSearchAndActionBar.RefreshExpandAllButtonState();

                    UiSessionState.CurrentWindow = builder.Show();
                    UiSessionState.StandaloneModWindow = UiSessionState.CurrentWindow;
                    UnsavedConfirmDialog.HookWindowCloseButton();
                    UiSessionState.CurrentWindowElements = UiSessionState.CustomWindowElementsField?.GetValue(UiSessionState.CurrentWindow) as Dictionary<string, CustomUIElement>;
                    UiSessionState.CaptureOpenBaselineSnapshot();
                    UiSessionState.OpenedFrameCount = Time.frameCount;
                }
            }
            finally
            {
                UiSessionState.IsBuildingOrOpeningUI = false;
            }
        }
    }
}
