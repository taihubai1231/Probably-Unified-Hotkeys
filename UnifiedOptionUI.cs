using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AllowUnbindKey
{
    /// <summary>
    /// 全新统一按键与模组设置中心 UI 注入与控制器
    /// </summary>
    public static class UnifiedOptionUI
    {
        private static GameOptionUIManager? _currentManager;
        private static CustomUIWindow? _currentWindow;
        private static List<ModOptionGroup> _modGroups = new List<ModOptionGroup>();
        private static List<ModHotkeyEntry> _allHotkeys = new List<ModHotkeyEntry>();
        private static Dictionary<KeyCode, List<ModHotkeyEntry>> _conflicts = new Dictionary<KeyCode, List<ModHotkeyEntry>>();

        // 状态标识
        public static bool IsConflictViewActive { get; private set; } = false;
        private static string _searchQuery = string.Empty;
        private static bool _allExpanded = false;

        // 当前正在捕获按键的模组条目
        private static ModHotkeyEntry? _capturingEntry;

        // 各种容器的 GameObject 引用，用于无缝切换视图与实时过滤
        private static GameObject? _normalContentRoot;
        private static GameObject? _conflictContentRoot;
        private static CustomUIElement? _conflictToggleBtn;
        private static CustomUIElement? _expandToggleBtn;
        private static TMP_InputField? _searchInputField;

        // 反射缓存原版 GameOptionUIManager 私有成员
        private static readonly MethodInfo? BuildSectionMethod = AccessTools.Method(typeof(GameOptionUIManager), "BuildSection");
        private static readonly MethodInfo? BuildEntryMethod = AccessTools.Method(typeof(GameOptionUIManager), "BuildEntry");
        private static readonly MethodInfo? OriginalApplySettingMethod = AccessTools.Method(typeof(GameOptionUIManager), "OnApplySetting");
        private static readonly MethodInfo? RefreshRulesMethod = AccessTools.Method(typeof(GameOptionUIManager), "RefreshRules");
        private static readonly MethodInfo? SetKeyBindStatusMethod = AccessTools.Method(typeof(GameOptionUIManager), "SetKeyBindStatus");
        private static readonly FieldInfo? PendingKeysField = AccessTools.Field(typeof(GameOptionUIManager), "pendingKeys");
        private static readonly FieldInfo? OverriddenDropdownsField = AccessTools.Field(typeof(GameOptionUIManager), "overriddenDropdowns");
        private static readonly FieldInfo? WindowField = AccessTools.Field(typeof(GameOptionUIManager), "window");

        /// <summary>
        /// 打开全新统一设置面板
        /// </summary>
        public static void OpenEnhancedUI(GameOptionUIManager manager)
        {
            _currentManager = manager;
            IsConflictViewActive = false;
            _searchQuery = string.Empty;
            _capturingEntry = null;

            // 1. 初始化原版环境
            if (EmporiumOption.Instance != null)
            {
                EmporiumOption.Instance.SyncDisplayStateFromScreen();
            }
            GameOptionRegistry.EnsureVanilla();
            GameOptionRegistry.RefreshMonitorInfo();
            KeyBindHelper.StopCapture();

            // 清空 Manager 中的旧挂起状态
            var pendingKeys = PendingKeysField?.GetValue(manager) as Dictionary<string, KeyCode>;
            pendingKeys?.Clear();
            var overriddenDropdowns = OverriddenDropdownsField?.GetValue(manager) as System.Collections.IDictionary;
            overriddenDropdowns?.Clear();
            SetKeyBindStatusMethod?.Invoke(manager, new object[] { string.Empty });

            // 2. 加载全量模组配置并分析按键冲突
            _modGroups = ModCatalogCache.GetOrScanGroups(out _allHotkeys);
            RefreshConflicts();

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

            builder.SetCloseOnEscape(!isStoreUi);
            builder.OnClose(() =>
            {
                _currentWindow = null;
                _capturingEntry = null;
            });

            // =================================================================
            // 4. 顶部快捷操作栏：实时搜索框 + 冲突速览按钮 + 全部折叠展开按钮
            // =================================================================
            builder.BeginRow(6f, TextAnchor.MiddleCenter);

            // 搜索框
            builder.AddInput(
                "🔍 搜索设置项或按键 (支持中英文、拼音如 sp=商品)...",
                _searchQuery,
                OnSearchChanged,
                "unified_search_input",
                OnSearchTyping
            );
            var searchEl = builder.Last;
            searchEl.SetFixedWidth(330f);
            if (searchEl.input != null)
            {
                _searchInputField = searchEl.input;
            }

            // 冲突专属速览切换按钮
            string conflictBtnText = _conflicts.Count > 0 ? $"⚠ 仅看冲突 ({_conflicts.Count})" : "⚠ 仅看冲突";
            builder.AddButton(conflictBtnText, ToggleConflictMode, "btn_conflict_toggle");
            _conflictToggleBtn = builder.Last;
            _conflictToggleBtn.SetFixedWidth(125f);
            if (_conflicts.Count > 0)
            {
                _conflictToggleBtn.SetTextColor(ConflictDetector.ConflictColor);
            }

            // 全部折叠 / 展开按钮
            builder.AddButton(_allExpanded ? "⊟ 全部收起" : "⊞ 全部展开", ToggleExpandAll, "btn_expand_all");
            _expandToggleBtn = builder.Last;
            _expandToggleBtn.SetFixedWidth(115f);

            builder.End(); // 结束顶部控制栏
            builder.AddSeparator(null, 2);

            // =================================================================
            // 5. 滚动视窗核心区域
            // =================================================================
            builder.BeginScrollFlexible(0f);

            // --- A. 常规完整设置视图容器 ---
            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            _normalContentRoot = builder.CurrentContainer.gameObject;

            // (1) 原版官方设置区块（完全原生构建）
            foreach (var section in GameOptionRegistry.Sections)
            {
                if (section == null) continue;
                BuildSectionMethod?.Invoke(manager, new object[] { builder, section });
            }

            // (2) 模组区块分割标题
            builder.AddSpace(10f);
            builder.AddSeparator(null, 3);
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            builder.AddHeader("创意工坊模组设置与快捷键", "header_mod_sections");
            builder.End();
            builder.AddSeparator(null, 2);

            // (3) 创意工坊模组独立折叠卡片列表
            foreach (var group in _modGroups)
            {
                BuildModGroupCard(builder, group);
            }

            builder.End(); // 结束 _normalContentRoot Column

            // --- B. 冲突专属速览视图容器 (初始隐藏) ---
            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            _conflictContentRoot = builder.CurrentContainer.gameObject;
            _conflictContentRoot.SetActive(false);

            BuildConflictViewContent(builder);

            builder.End(); // 结束 _conflictContentRoot Column

            builder.End(); // 结束 BeginScrollFlexible

            // =================================================================
            // 6. 底部操作栏：原生“应用设置”按钮
            // =================================================================
            builder.AddSeparator(null, 2);
            builder.BeginRow(4f, TextAnchor.MiddleCenter);
            builder.AddFlexibleSpace();

            builder.AddButton(LocHelper.GetLocalizedMainMenu("option_apply"), () =>
            {
                ApplyAllSettings(manager);
            }, "apply");
            builder.Last.SetFixedWidth(180f);

            builder.AddFlexibleSpace();
            builder.End();

            // 7. 展示窗口
            _currentWindow = builder.Show();
            WindowField?.SetValue(manager, _currentWindow);
            RefreshRulesMethod?.Invoke(manager, null);
        }

        // =====================================================================
        // 模组卡片构建 (方案一垂直分区分段：上半截常规配置，下半截快捷键)
        // =====================================================================
        private static void BuildModGroupCard(CustomUIBuilder builder, ModOptionGroup group)
        {
            // 1. 卡片头部栏
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            group.HeaderRowObject = builder.CurrentContainer.gameObject;

            // 折叠/展开箭头按钮
            builder.AddButton(group.IsExpanded ? "▼" : "▶", () =>
            {
                group.SetExpanded(!group.IsExpanded);
            }, "fold_" + group.ModId);
            group.FoldArrowElement = builder.Last;
            group.FoldArrowElement.SetFixedWidth(36f);

            // 模组名称与数量徽章
            string badge = $"<color=#888888>(常规: {group.GeneralSettings.Count} | 按键: {group.Hotkeys.Count})</color>";
            builder.AddLabel($"<b>{group.DisplayName}</b> {badge}", "label_group_" + group.ModId);

            builder.AddFlexibleSpace();

            // 一键恢复默认按钮
            if (group.ResetToDefaultsAction != null)
            {
                builder.AddButton("恢复默认", () =>
                {
                    group.ResetToDefaultsAction?.Invoke();
                    RefreshCardUI(group);
                }, "reset_" + group.ModId);
                builder.Last.SetFixedWidth(95f);
            }

            builder.End(); // 结束头部栏

            // 2. 卡片展开内容容器
            builder.BeginColumn(2f, TextAnchor.UpperLeft);
            group.ContentContainerObject = builder.CurrentContainer.gameObject;
            group.ContentContainerObject.SetActive(group.IsExpanded);

            // --- 上半截：常规通用配置 (开关 / 滑块 / 下拉框) ---
            if (group.GeneralSettings.Count > 0)
            {
                foreach (var gen in group.GeneralSettings)
                {
                    BuildGeneralSettingRow(builder, gen);
                }
            }

            // --- 微细分割线 (两者皆有时出现) ---
            if (group.GeneralSettings.Count > 0 && group.Hotkeys.Count > 0)
            {
                builder.AddSeparator(null, 1);
            }

            // --- 下半截：快捷键绑定 ---
            if (group.Hotkeys.Count > 0)
            {
                foreach (var hotkey in group.Hotkeys)
                {
                    BuildHotkeyRow(builder, hotkey);
                }
            }

            builder.AddSpace(6f);
            builder.End(); // 结束卡片容器
        }

        private static void BuildGeneralSettingRow(CustomUIBuilder builder, ModGeneralEntry gen)
        {
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            gen.RowObject = builder.CurrentContainer.gameObject;

            string labelText = gen.DisplayName;
            if (!string.IsNullOrEmpty(gen.Tooltip))
            {
                string link = "modopt:" + gen.Id;
                TMPIconHover.CustomLinks[link] = () => gen.Tooltip;
                labelText += $" <link=\"{link}\"><sprite name=\"info\" tint=1></link>";
            }

            builder.AddLabel(labelText, "label_" + gen.Id);
            builder.AddFlexibleSpace();

            switch (gen.Kind)
            {
                case OptionKind.Toggle:
                    bool currentBool = gen.GetBool?.Invoke() ?? false;
                    builder.AddToggle(string.Empty, currentBool, (val) =>
                    {
                        gen.PendingBool = val;
                    }, "toggle_" + gen.Id);
                    gen.ControlElement = builder.Last;
                    break;

                case OptionKind.Slider:
                    float currentFloat = gen.GetFloat?.Invoke() ?? gen.Min;
                    // 数值显示标签
                    builder.AddLabel(FormatSliderValue(gen, currentFloat), "val_" + gen.Id);
                    gen.ValueLabelElement = builder.Last;
                    gen.ValueLabelElement.SetFixedWidth(56f);

                    builder.AddSlider(gen.Min, gen.Max, currentFloat, (val) =>
                    {
                        gen.PendingFloat = val;
                        gen.ValueLabelElement?.SetText(FormatSliderValue(gen, val));
                    }, gen.WholeNumbers, "slider_" + gen.Id);
                    gen.ControlElement = builder.Last;
                    gen.ControlElement.SetFixedWidth(240f);
                    break;

                case OptionKind.Dropdown:
                    var choices = gen.GetChoices?.Invoke() ?? new List<string>();
                    int currentIdx = gen.GetIndex?.Invoke() ?? 0;
                    builder.AddDropdown(choices, currentIdx, (idx) =>
                    {
                        gen.PendingIndex = idx;
                    }, "drop_" + gen.Id);
                    gen.ControlElement = builder.Last;
                    gen.ControlElement.SetFixedWidth(240f);
                    break;
            }

            builder.End();
        }

        private static void BuildHotkeyRow(CustomUIBuilder builder, ModHotkeyEntry hotkey)
        {
            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            hotkey.RowObject = builder.CurrentContainer.gameObject;

            string labelText = hotkey.DisplayName;
            string conflictTip = ConflictDetector.GetConflictTooltip(hotkey, _conflicts);
            if (!string.IsNullOrEmpty(conflictTip))
            {
                string link = "conflict:" + hotkey.Id;
                TMPIconHover.CustomLinks[link] = () => conflictTip;
                labelText += $" <link=\"{link}\"><color=#ffb347><sprite name=\"warning\" tint=1></color></link>";
            }
            else if (!string.IsNullOrEmpty(hotkey.Tooltip))
            {
                string link = "modkey:" + hotkey.Id;
                TMPIconHover.CustomLinks[link] = () => hotkey.Tooltip;
                labelText += $" <link=\"{link}\"><sprite name=\"info\" tint=1></link>";
            }

            builder.AddLabel(labelText, "label_" + hotkey.Id);
            hotkey.LabelElement = builder.Last;

            builder.AddFlexibleSpace();

            // 按键绑定按钮
            string keyText = FormatKeyText(hotkey.EffectiveKey);
            builder.AddButton(keyText, () =>
            {
                StartCaptureHotkey(hotkey);
            }, "btn_key_" + hotkey.Id);

            hotkey.ButtonElement = builder.Last;
            hotkey.ButtonElement.SetFixedWidth(160f);

            // 设置颜色
            UpdateHotkeyButtonVisual(hotkey);

            builder.End();
        }

        // =====================================================================
        // 冲突专属速览视图构建
        // =====================================================================
        private static void BuildConflictViewContent(CustomUIBuilder builder)
        {
            var sortedConflicts = ConflictDetector.GetSortedConflictGroups(_conflicts);

            builder.BeginRow(4f, TextAnchor.MiddleLeft);
            string headerText = sortedConflicts.Count > 0
                ? $"<color=#ffb347><b>当前共有 {sortedConflicts.Count} 组按键发生冲突！</b></color> (点击按键即可直接改开)"
                : "<color=#55dd88><b>太棒了！当前没有任何按键冲突。</b></color>";
            builder.AddHeader(headerText, "header_conflict_status");
            builder.End();
            builder.AddSeparator(null, 2);

            foreach (var cg in sortedConflicts)
            {
                string keyName = KeyBindHelper.KeyName(cg.Key);
                builder.BeginRow(4f, TextAnchor.MiddleLeft);
                builder.AddLabel($"<b>【 冲突按键: <color=#ffb347>{keyName}</color> 】</b> (共 {cg.ConflictingEntries.Count} 个功能重叠)", "cg_header_" + (int)cg.Key);
                builder.End();

                foreach (var entry in cg.ConflictingEntries)
                {
                    builder.BeginRow(4f, TextAnchor.MiddleLeft);
                    builder.AddSpace(16f);
                    builder.AddLabel($"[{entry.ModDisplayName}]  {entry.DisplayName}", "cg_label_" + entry.Id);
                    builder.AddFlexibleSpace();

                    builder.AddButton(FormatKeyText(entry.EffectiveKey), () =>
                    {
                        StartCaptureHotkey(entry);
                    }, "cg_btn_" + entry.Id);
                    var btn = builder.Last;
                    btn.SetFixedWidth(160f);
                    btn.SetTextColor(ConflictDetector.ConflictColor);

                    builder.End();
                }

                builder.AddSpace(6f);
                builder.AddSeparator(null, 1);
            }
        }

        // =====================================================================
        // 按键捕获流程 (支持 Backspace / Delete 设为空键)
        // =====================================================================
        private static void StartCaptureHotkey(ModHotkeyEntry entry)
        {
            _capturingEntry = entry;
            KeyBindHelper.StartCapture(entry.Id);
            entry.ButtonElement?.SetText("按任意键... (按退格设为空)");

            SetKeyBindStatusMethod?.Invoke(_currentManager, new object[] { $"正在为 [{entry.ModDisplayName}: {entry.DisplayName}] 绑定按键 (按 Backspace 可设为空/清空)" });
        }

        /// <summary>
        /// 每帧由 Patch_GameOptionUIManager_Update 驱动按键捕获
        /// </summary>
        public static void TickCapture()
        {
            if (_capturingEntry == null || !KeyBindHelper.IsCapturing) return;

            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
            {
                FinishCapture(KeyCode.None);
                return;
            }

            // 监听其他任意键盘按键按下
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None || kc == KeyCode.Backspace || kc == KeyCode.Delete) continue;
                if (kc == KeyCode.Escape) continue; // Esc 退出捕获

                if (Input.GetKeyDown(kc))
                {
                    FinishCapture(kc);
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // 取消捕获
                KeyBindHelper.StopCapture();
                UpdateHotkeyButtonVisual(_capturingEntry);
                _capturingEntry = null;
                SetKeyBindStatusMethod?.Invoke(_currentManager, new object[] { string.Empty });
            }
        }

        private static void FinishCapture(KeyCode newKey)
        {
            if (_capturingEntry == null) return;

            _capturingEntry.PendingKey = newKey;
            _capturingEntry.HasPendingChange = true;

            KeyBindHelper.StopCapture();
            UpdateHotkeyButtonVisual(_capturingEntry);

            // 重新刷新全局冲突状态与高亮
            RefreshConflicts();

            SetKeyBindStatusMethod?.Invoke(_currentManager, new object[] { string.Empty });
            _capturingEntry = null;
        }

        // =====================================================================
        // 搜索过滤与实时响应 (支持中文、英文、拼音首字母、全拼)
        // =====================================================================
        private static void OnSearchTyping(string text)
        {
            _searchQuery = text;
            ApplySearchFilter();
        }

        private static void OnSearchChanged(string text)
        {
            _searchQuery = text;
            ApplySearchFilter();
        }

        private static void ApplySearchFilter()
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(_searchQuery);

            foreach (var group in _modGroups)
            {
                int matchedCount = 0;

                // 1. 过滤常规配置
                foreach (var gen in group.GeneralSettings)
                {
                    bool match = !hasQuery || PinyinEngine.Matches(gen.DisplayName, _searchQuery);
                    gen.RowObject?.SetActive(match);
                    if (match) matchedCount++;
                }

                // 2. 过滤快捷键
                foreach (var hotkey in group.Hotkeys)
                {
                    bool match = !hasQuery || PinyinEngine.Matches(hotkey.DisplayName, _searchQuery) ||
                                 PinyinEngine.Matches(hotkey.EffectiveKey.ToString(), _searchQuery);
                    hotkey.RowObject?.SetActive(match);
                    if (match) matchedCount++;
                }

                // 3. 模组卡片本身是否命中
                bool groupNameMatch = hasQuery && (PinyinEngine.Matches(group.DisplayName, _searchQuery) ||
                                                   PinyinEngine.Matches(group.ModId, _searchQuery));

                if (!hasQuery)
                {
                    // 恢复原本的折叠状态
                    group.HeaderRowObject?.SetActive(true);
                    group.ContentContainerObject?.SetActive(group.IsExpanded);
                }
                else if (groupNameMatch || matchedCount > 0)
                {
                    // 有匹配项时，展示头部并自动展开卡片展示结果
                    group.HeaderRowObject?.SetActive(true);
                    group.ContentContainerObject?.SetActive(true);
                    group.FoldArrowElement?.SetText("▼");
                }
                else
                {
                    // 无匹配项时，完全隐藏该模组卡片
                    group.HeaderRowObject?.SetActive(false);
                    group.ContentContainerObject?.SetActive(false);
                }
            }
        }

        // =====================================================================
        // 辅助切换操作
        // =====================================================================
        private static void ToggleConflictMode()
        {
            IsConflictViewActive = !IsConflictViewActive;

            if (_normalContentRoot != null) _normalContentRoot.SetActive(!IsConflictViewActive);
            if (_conflictContentRoot != null) _conflictContentRoot.SetActive(IsConflictViewActive);

            _conflictToggleBtn?.SetText(IsConflictViewActive ? "📋 完整设置" : $"⚠ 仅看冲突 ({_conflicts.Count})");
        }

        private static void ToggleExpandAll()
        {
            _allExpanded = !_allExpanded;
            _expandToggleBtn?.SetText(_allExpanded ? "⊟ 全部收起" : "⊞ 全部展开");

            foreach (var g in _modGroups)
            {
                g.SetExpanded(_allExpanded);
            }
        }

        private static void RefreshConflicts()
        {
            _conflicts = ConflictDetector.Analyze(_allHotkeys);

            // 更新所有按钮的视觉状态
            foreach (var g in _modGroups)
            {
                foreach (var h in g.Hotkeys)
                {
                    UpdateHotkeyButtonVisual(h);
                }
            }

            // 更新顶部按钮的计数文字
            if (!IsConflictViewActive)
            {
                string text = _conflicts.Count > 0 ? $"⚠ 仅看冲突 ({_conflicts.Count})" : "⚠ 仅看冲突";
                _conflictToggleBtn?.SetText(text);
                _conflictToggleBtn?.SetTextColor(_conflicts.Count > 0 ? ConflictDetector.ConflictColor : Color.white);
            }
        }

        private static void UpdateHotkeyButtonVisual(ModHotkeyEntry hotkey)
        {
            if (hotkey.ButtonElement == null) return;

            hotkey.ButtonElement.SetText(FormatKeyText(hotkey.EffectiveKey));

            if (hotkey.EffectiveKey == KeyCode.None)
            {
                hotkey.ButtonElement.SetTextColor(ConflictDetector.EmptyKeyColor);
            }
            else if (_conflicts.ContainsKey(hotkey.EffectiveKey))
            {
                hotkey.ButtonElement.SetTextColor(ConflictDetector.ConflictColor);
            }
            else
            {
                hotkey.ButtonElement.SetTextColor(ConflictDetector.NormalColor);
            }
        }

        private static void RefreshCardUI(ModOptionGroup group)
        {
            foreach (var gen in group.GeneralSettings)
            {
                if (gen.Kind == OptionKind.Toggle && gen.ControlElement != null)
                {
                    gen.ControlElement.SetBool(gen.GetBool?.Invoke() ?? false, false);
                }
                else if (gen.Kind == OptionKind.Slider && gen.ControlElement != null)
                {
                    float val = gen.GetFloat?.Invoke() ?? 0f;
                    gen.ControlElement.SetFloat(val, false);
                    gen.ValueLabelElement?.SetText(FormatSliderValue(gen, val));
                }
                else if (gen.Kind == OptionKind.Dropdown && gen.ControlElement != null)
                {
                    gen.ControlElement.SetIndex(gen.GetIndex?.Invoke() ?? 0, false);
                }
            }

            foreach (var hotkey in group.Hotkeys)
            {
                hotkey.HasPendingChange = false;
                UpdateHotkeyButtonVisual(hotkey);
            }

            RefreshConflicts();
        }

        private static string FormatKeyText(KeyCode key)
        {
            if (key == KeyCode.None) return "None (空/已解绑)";
            return KeyBindHelper.KeyName(key);
        }

        private static string FormatSliderValue(ModGeneralEntry gen, float val)
        {
            if (gen.ValueFormatter != null) return gen.ValueFormatter(val);
            if (gen.WholeNumbers) return Mathf.RoundToInt(val).ToString();
            return val.ToString("0.0");
        }

        // =====================================================================
        // 保存与持久化 (OnApplySetting 挂钩)
        // =====================================================================
        private static void ApplyAllSettings(GameOptionUIManager manager)
        {
            // 1. 执行原版原生保存逻辑
            OriginalApplySettingMethod?.Invoke(manager, null);

            // 2. 保存所有发生变动的模组常规配置
            var touchedModIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in _modGroups)
            {
                bool groupChanged = false;

                foreach (var gen in group.GeneralSettings)
                {
                    if (gen.PendingBool.HasValue || gen.PendingFloat.HasValue || gen.PendingIndex.HasValue)
                    {
                        gen.ApplyPending();
                        groupChanged = true;
                    }
                }

                // 3. 保存所有发生变动的模组按键
                foreach (var hotkey in group.Hotkeys)
                {
                    if (hotkey.HasPendingChange)
                    {
                        hotkey.SetKey?.Invoke(hotkey.PendingKey);
                        hotkey.HasPendingChange = false;
                        groupChanged = true;
                    }
                }

                if (groupChanged)
                {
                    touchedModIds.Add(group.ModId);
                }
            }

            // 4. 触发通知链，使第三方模组即时热生效
            var fireMethod = AccessTools.Method(typeof(ModHook), "FireOnModConfigApplied", new Type[] { typeof(string) });
            foreach (var modId in touchedModIds)
            {
                try
                {
                    if (ModHook.Instance != null && fireMethod != null)
                    {
                        fireMethod.Invoke(ModHook.Instance, new object[] { modId });
                    }
                }
                catch { }
            }

            // 5. 重新刷新冲突状态
            RefreshConflicts();
        }
    }
}
