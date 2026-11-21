#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

        public bool StartCaptureVanillaOption(GameOptionUIManager instance, string optionId)
        {
            return KeyCaptureController.StartCaptureVanillaOption(instance, optionId);
        }

        public bool ShouldSilenceMissingElementWarning(CustomUIWindow instance, string elementId, out CustomUIElement? found)
        {
            return UiSessionState.ShouldSilenceMissingElementWarning(instance, elementId, out found);
        }

        public void ResetSessionState() => UiSessionState.ResetSessionOnClose();

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
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            UiSessionState.ModHeaderRow = builder.CurrentContainer.gameObject;
            builder.AddSpace(6f);
            string modHdrText = I18n.Get("创意工坊模组设置与快捷键", "Mod Settings & Hotkeys");
            builder.AddHeader(modHdrText, "header_mod_sections");
            I18n.ApplyFontIfRuntime(builder.Last?.text, modHdrText);
            builder.End();
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
                if (UiSessionState.CurrentWindow == null || UiSessionState.IsApplyingSettings || UiSessionState.SuppressRuleConflictRefresh) return;
                SyncVanillaPendingFromManager();
                ConflictAggregationView.RefreshConflicts();
            }
        }

        internal static void OnVanillaKeyChanged()
        {
            using (ComboKeyStore.EnterBypass())
            {
                if (UiSessionState.CurrentWindow == null || UiSessionState.IsApplyingSettings) return;
                SyncVanillaPendingFromManager();
                ConflictAggregationView.RefreshConflicts();
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
                if (UiSessionState.CurrentWindow == null) return;

                foreach (var hk in UiSessionState.AllHotkeys)
                {
                    if (hk.ModId != "vanilla" || string.IsNullOrEmpty(hk.RawOptionId)) continue;

                    var ui = UiSessionState.GetOrCreateHotkeyUi(hk);
                    var labelEl = ui.LabelElement ?? UiSessionState.CurrentWindow.Get("label_" + hk.RawOptionId);
                    if (labelEl != null)
                    {
                        ui.LabelElement = labelEl;
                        UiLayoutAndFontHelper.SetTextIfChanged(labelEl, UiLayoutAndFontHelper.BuildHotkeyLabelText(hk));
                    }

                    var uiEl = ui.ButtonElement ?? UiSessionState.CurrentWindow.Get(hk.RawOptionId);
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
            OpenEnhancedUI(manager);

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
            KeyCaptureController.CancelCapture();
            bool wasChinese = I18n.IsChinese;
            bool selfModLanguageChanged = false;

            // 1. 同步原版 PendingKeys 并写入组合键修饰位
            var pendingKeys = UiSessionState.PendingKeysField?.GetValue(manager) as Dictionary<string, KeyCode>;
            bool comboChanged = false;
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
                    comboChanged = true;
                    hk.SnapshotKey = hk.PendingKey;
                    hk.SnapshotModifiers = hk.PendingModifiers;
                    hk.PendingModifiers = KeyModifierMask.None;
                    hk.PendingKey = KeyCode.None;
                    hk.HasPendingChange = false;
                }
            }

            // 2. 执行原版原生保存逻辑
            UiSessionState.IsApplyingSettings = true;
            try
            {
                using (ComboKeyStore.EnterBypass())
                {
                    UiSessionState.OriginalApplySettingDelegate?.Invoke(manager);
                }
                pendingKeys?.Clear();
            }
            finally
            {
                UiSessionState.IsApplyingSettings = false;
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

            if (wasChinese != I18n.IsChinese)
            {
                ReopenEnhancedUIForLanguageSwitch(manager, selfModLanguageChanged ? I18n.ModId : null);
                return;
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
            UiSessionState.HasUserInteractedInSession = false;
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
    }
}
