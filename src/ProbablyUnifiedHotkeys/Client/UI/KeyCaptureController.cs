#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 统一按键捕获状态机控制器（原版设置与模组设置通用）。
    /// 支持单键、修饰组合键（Ctrl/Shift/Alt）、鼠标右键绑定、鼠标侧键及 Esc 解绑清空。
    /// </summary>
    internal static class KeyCaptureController
    {
        private static readonly KeyCode[] TrackedModifierKeys = new KeyCode[]
        {
            KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt
        };

        private static readonly Stopwatch PrebuildStopwatch = new Stopwatch();
        private static readonly Color CapturePromptColor = new Color(0.4f, 0.8f, 1f, 1f);

        private static string CapturePrompt => I18n.Get("请按键... (Esc=无)", "Press key... (Esc=None)");

        /// <summary>
        /// 开启原版原生设置项的按键录制。
        /// </summary>
        internal static bool StartCaptureVanillaOption(GameOptionUIManager manager, string optionId)
        {
            if (UiSessionState.CurrentWindow == null || UiSessionState.CurrentManager != manager || string.IsNullOrEmpty(optionId))
            {
                return false;
            }

            var vanillaEntry = UiSessionState.AllHotkeys.Find(h =>
            {
                if (h.ModId != "vanilla" || string.IsNullOrEmpty(h.RawOptionId)) return false;
                if (string.Equals(h.RawOptionId, optionId, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(h.RawOptionId, "key_" + optionId, StringComparison.OrdinalIgnoreCase)) return true;
                if (optionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(h.RawOptionId, optionId.Substring(4), StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            });
            if (vanillaEntry == null)
            {
                return false;
            }

            StartCaptureHotkey(vanillaEntry);
            return true;
        }

        /// <summary>
        /// 开启指定热键条目的捕获录制流程。
        /// </summary>
        internal static void StartCaptureHotkey(ModHotkeyEntry entry)
        {
            if (UiSessionState.SuppressSameEntryRestart && UiSessionState.LastEndedCaptureEntry == entry)
            {
                return;
            }

            if (UiSessionState.CapturingEntry != null)
            {
                UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(UiSessionState.CapturingEntry);
            }

            KeyBindHelper.StopCapture();

            if (UiSessionState.CapturingEntry == null && UiSessionState.CurrentWindow != null)
            {
                UiSessionState.CloseOnEscapeBeforeCapture = UiSessionState.CurrentWindow.closeOnEscape;
                UiSessionState.CurrentWindow.closeOnEscape = false;
            }

            UiSessionState.CapturingEntry = entry;
            UiSessionState.CapturingStartedFrame = Time.frameCount;
            UiSessionState.RestoreCloseOnEscapeFrame = -1;
            UiSessionState.WaitingMouseRelease = Input.GetMouseButton(0) || Input.GetMouseButton(1);
            UiSessionState.PendingStandaloneModifier = KeyCode.None;
            UiSessionState.HadMultipleModifiers = false;

            SetCapturingEntryDisplayText(entry, CapturePrompt);

            if (UiSessionState.CurrentManager != null)
            {
                string statusPrompt = entry.SupportsModifierCombos
                    ? I18n.Format(
                        "正在为 [{0}: {1}] 绑定按键 (支持组合键与鼠标侧键；按 Esc 设为空，左键取消)",
                        "Binding [{0}: {1}] (combos & side buttons supported; Esc to clear, Left click to cancel)",
                        entry.ModDisplayName, entry.DisplayName)
                    : I18n.Format(
                        "正在为 [{0}: {1}] 绑定按键 (支持单键与鼠标侧键；按 Esc 设为空，左键取消；此模组暂不支持组合键前缀)",
                        "Binding [{0}: {1}] (single key & side buttons; Esc to clear, Left click to cancel; no modifier combos)",
                        entry.ModDisplayName, entry.DisplayName);
                UiSessionState.SetKeyBindStatusDelegate?.Invoke(UiSessionState.CurrentManager, statusPrompt);
            }
        }

        private static void SetCapturingEntryDisplayText(ModHotkeyEntry entry, string text)
        {
            if (UiSessionState.TryGetHotkeyUi(entry, out var ui))
            {
                if (ui.ButtonElement != null)
                {
                    UiLayoutAndFontHelper.SetButtonTextFast(ui.ButtonElement, text);
                    UiLayoutAndFontHelper.ApplyButtonTextTheme(ui.ButtonElement, CapturePromptColor);
                }
                if (ui.ConflictButtonElement != null)
                {
                    UiLayoutAndFontHelper.SetButtonTextFast(ui.ConflictButtonElement, text);
                    UiLayoutAndFontHelper.ApplyButtonTextTheme(ui.ConflictButtonElement, CapturePromptColor);
                }
            }

            if (entry.ModId == "vanilla" && !string.IsNullOrEmpty(entry.RawOptionId))
            {
                var vanillaEl = UiSessionState.CurrentWindow?.Get(entry.RawOptionId);
                if (vanillaEl != null)
                {
                    UiLayoutAndFontHelper.SetButtonTextFast(vanillaEl, text);
                    UiLayoutAndFontHelper.ApplyButtonTextTheme(vanillaEl, CapturePromptColor);
                }
            }
        }

        /// <summary>
        /// 窗口开启后的按帧预算后台静默预建：在约 3.0ms/帧 的平滑预算内于折叠态(inactive)容器连续构建卡片内容，
        /// 既不在开启首帧产生卡顿，又能平滑预热全量卡片，使“全部展开”与搜索瞬开。
        /// </summary>
        internal static void PrebuildNextCollapsedModCard()
        {
            if (UiSessionState.CurrentWindow == null || UiSessionState.IsConflictViewActive || UiSessionState.IsDiagnosticViewActive || UiSessionState.CapturingEntry != null || UiSessionState.PendingScrollTarget != null || !string.IsNullOrWhiteSpace(UiSessionState.SearchQuery) || TopSearchAndActionBar.HasPendingProgressiveExpand)
            {
                return;
            }
            if (UiSessionState.OpenedFrameCount < 0 || Time.frameCount <= UiSessionState.OpenedFrameCount)
            {
                return;
            }

            PrebuildStopwatch.Restart();
            while (UiSessionState.PrebuildModIndex < UiSessionState.ModGroups.Count)
            {
                var group = UiSessionState.ModGroups[UiSessionState.PrebuildModIndex++];
                if (!UiSessionState.TryGetCardParts(group, out var parts))
                {
                    continue;
                }
                if (parts.IsContentBuilt || parts.ContentContainerObject == null || parts.ContentContainerObject.activeSelf)
                {
                    continue;
                }

                CollapsibleCardView.EnsureGroupContentBuilt(group);

                if (PrebuildStopwatch.Elapsed.TotalMilliseconds >= 3.0)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 每帧由 Patch_GameOptionUIManager_Update 驱动模组/原版快捷键捕获、搜索防抖、渐进展开、静默预建、安全 Esc 恢复与搜索延迟滚动。
        /// </summary>
        internal static void TickCapture()
        {
            // 搜索输入防抖倒计时检测
            TopSearchAndActionBar.TickSearchDebounce();

            // 全部展开分帧渐进展开调度（视口优先瞬开后的屏外卡片平滑处理）
            TopSearchAndActionBar.TickProgressiveExpand();

            // 搜索 / 跨视图跳转后跨帧滚动校准
            UiLayoutAndFontHelper.TickPendingScroll();

            // 闲置时每帧在折叠容器内后台预建未建卡片（Stopwatch 预算 <= 3.0ms）
            PrebuildNextCollapsedModCard();

            // 鼠标组合键完成/取消后的同键防重触发窗口释放检测
            if (UiSessionState.SuppressSameEntryRestart)
            {
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
                {
                    if (UiSessionState.SuppressRestartReleaseFrame < 0)
                    {
                        UiSessionState.SuppressRestartReleaseFrame = Time.frameCount + 2;
                    }
                    else if (Time.frameCount >= UiSessionState.SuppressRestartReleaseFrame)
                    {
                        UiSessionState.SuppressSameEntryRestart = false;
                        UiSessionState.LastEndedCaptureEntry = null;
                        UiSessionState.SuppressRestartReleaseFrame = -1;
                    }
                }
            }

            // 延迟恢复关闭窗口的 Esc 权限，防止解绑当帧窗口意外关闭
            if (UiSessionState.CapturingEntry == null && UiSessionState.RestoreCloseOnEscapeFrame >= 0 && Time.frameCount >= UiSessionState.RestoreCloseOnEscapeFrame)
            {
                if (UiSessionState.CurrentWindow != null)
                {
                    UiSessionState.CurrentWindow.closeOnEscape = UiSessionState.CloseOnEscapeBeforeCapture;
                }
                UiSessionState.RestoreCloseOnEscapeFrame = -1;
            }

            // 正在录制按键或刚完成按键录制的缓冲帧内：由 Patch_CustomUIManager_HandleEscape 保证不触发未保存 Esc 弹窗
            if (UiSessionState.CapturingEntry == null && UiSessionState.CurrentWindow != null && UiSessionState.CurrentWindow.IsAlive && UiSessionState.CurrentWindow.IsVisible && UiSessionState.RestoreCloseOnEscapeFrame < 0)
            {
                bool isStoreUi = StoreUIManager.Instance != null;
                UiSessionState.CurrentWindow.closeOnEscape = !isStoreUi;
            }

            if (UiSessionState.CapturingEntry == null) return;
            if (Time.frameCount <= UiSessionState.CapturingStartedFrame) return;

            if (UiSessionState.WaitingMouseRelease)
            {
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1)) return;
                UiSessionState.WaitingMouseRelease = false;
            }

            bool allowCombos = UiSessionState.CapturingEntry.SupportsModifierCombos;
            KeyModifierMask heldModifiers = allowCombos ? ComboKeyStore.GetHeldModifiersMask() : KeyModifierMask.None;

            // 1. 仅 Esc 用于解除绑定（清空为无），Backspace 与 Delete 均作为普通可绑定按键
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                UiSessionState.MarkEscapeConsumed();
                FinishCapture(KeyModifierMask.None, KeyCode.None, fromMouseClick: false);
                return;
            }

            // 2. 鼠标按键优先轮询（ADR-010 核心契约：无修饰左键取消，右键直接绑定，修饰键+左键绑定）
            if (Input.GetMouseButtonDown(0))
            {
                if (heldModifiers == KeyModifierMask.None)
                {
                    CancelCapture(fromMouseClick: true);
                }
                else
                {
                    FinishCapture(heldModifiers, KeyCode.Mouse0, fromMouseClick: true);
                }
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                FinishCapture(heldModifiers, KeyCode.Mouse1, fromMouseClick: true);
                return;
            }

            for (int btn = 2; btn <= 6; btn++)
            {
                KeyCode mk = KeyCode.Mouse0 + btn;
                if (ComboKeyStore.IsKeyDownRaw(mk))
                {
                    FinishCapture(heldModifiers, mk, fromMouseClick: false);
                    return;
                }
            }

            // 3. 修饰键追踪（允许按住 Shift/Ctrl/Alt 等等待主键，或单击松开绑为独立修饰键）
            foreach (KeyCode modKey in TrackedModifierKeys)
            {
                if (Input.GetKeyDown(modKey))
                {
                    if (UiSessionState.PendingStandaloneModifier == KeyCode.None)
                    {
                        UiSessionState.PendingStandaloneModifier = modKey;
                        UiSessionState.HadMultipleModifiers = false;
                    }
                    else if (UiSessionState.PendingStandaloneModifier != modKey)
                    {
                        UiSessionState.HadMultipleModifiers = true;
                    }
                }
            }

            if (((int)heldModifiers & ((int)heldModifiers - 1)) != 0)
            {
                UiSessionState.HadMultipleModifiers = true;
            }

            // 4. 常规非修饰键盘主键轮询（使用静态预筛选键值表，零 GC 分配）
            if (Input.anyKeyDown)
            {
                for (int i = 0; i < UiSessionState.BindableKeyboardKeys.Length; i++)
                {
                    KeyCode kc = UiSessionState.BindableKeyboardKeys[i];
                    if (Input.GetKeyDown(kc))
                    {
                        KeyModifierMask finalMods = allowCombos ? ComboKeyStore.GetHeldModifiersMask(kc) : KeyModifierMask.None;
                        FinishCapture(finalMods, kc, fromMouseClick: false);
                        return;
                    }
                }
            }

            // 5. 若按住修饰键后未按主键而松开：仅单修饰键点按才回落绑定该修饰键自身（保留如 LeftAlt 单独快移）
            if (UiSessionState.PendingStandaloneModifier != KeyCode.None && Input.GetKeyUp(UiSessionState.PendingStandaloneModifier))
            {
                KeyCode standalone = UiSessionState.PendingStandaloneModifier;
                UiSessionState.PendingStandaloneModifier = KeyCode.None;

                KeyModifierMask afterMods = allowCombos ? ComboKeyStore.GetHeldModifiersMask() : KeyModifierMask.None;
                if (!UiSessionState.HadMultipleModifiers && afterMods == KeyModifierMask.None)
                {
                    FinishCapture(KeyModifierMask.None, standalone, fromMouseClick: false);
                    return;
                }

                if (afterMods == KeyModifierMask.None)
                {
                    UiSessionState.HadMultipleModifiers = false;
                    SetCapturingEntryDisplayText(UiSessionState.CapturingEntry, CapturePrompt);
                }
                else
                {
                    SetCapturingEntryDisplayText(UiSessionState.CapturingEntry, ComboKeyStore.FormatModifiers(afterMods) + "...");
                }
                return;
            }

            // 实时显示当前按住的修饰键前缀
            if (heldModifiers != KeyModifierMask.None)
            {
                SetCapturingEntryDisplayText(UiSessionState.CapturingEntry, ComboKeyStore.FormatModifiers(heldModifiers) + "...");
            }
            else if (UiSessionState.PendingStandaloneModifier == KeyCode.None)
            {
                SetCapturingEntryDisplayText(UiSessionState.CapturingEntry, CapturePrompt);
            }
        }

        /// <summary>
        /// 取消当前的按键捕获并恢复原始键位展示。
        /// </summary>
        internal static void CancelCapture(bool fromMouseClick = false)
        {
            UiSessionState.CancelCapture(fromMouseClick);
        }

        /// <summary>
        /// 确认完成按键捕获并更新待保存数据。
        /// </summary>
        internal static void FinishCapture(KeyModifierMask newMods, KeyCode newKey, bool fromMouseClick)
        {
            if (UiSessionState.CapturingEntry == null) return;

            var entry = UiSessionState.CapturingEntry;
            if (!entry.SupportsModifierCombos) newMods = KeyModifierMask.None;
            entry.PendingModifiers = newKey == KeyCode.None ? KeyModifierMask.None : newMods;
            entry.PendingKey = newKey;

            // 校验是否有实质性修改
            KeyCode currentKey = entry.GetKey?.Invoke() ?? KeyCode.None;
            KeyModifierMask savedMods = (currentKey == KeyCode.None)
                ? KeyModifierMask.None
                : (entry.GetExternalModifiers != null
                    ? entry.GetExternalModifiers.Invoke()
                    : ComboKeyStore.GetSavedModifiers(entry.Id, currentKey));

            if (newKey == KeyCode.None && currentKey == KeyCode.None)
            {
                entry.HasPendingChange = false;
                entry.PendingModifiers = KeyModifierMask.None;
            }
            else
            {
                entry.HasPendingChange = (newKey != currentKey || entry.PendingModifiers != savedMods);
            }

            // 若捕获的是原版官方按键，立即同步主键到原版挂起字典
            if (entry.ModId == "vanilla" && UiSessionState.CurrentManager != null && !string.IsNullOrEmpty(entry.RawOptionId))
            {
                if (UiSessionState.PendingKeysField?.GetValue(UiSessionState.CurrentManager) is Dictionary<string, KeyCode> pendingKeys)
                {
                    pendingKeys[entry.RawOptionId] = newKey;
                }
            }

            UiSessionState.CapturingEntry = null;
            UiSessionState.PendingStandaloneModifier = KeyCode.None;
            UiSessionState.HadMultipleModifiers = false;
            UiSessionState.SuppressEscapeUntilFrame = Time.frameCount + 2;
            UiSessionState.RestoreCloseOnEscapeFrame = Time.frameCount + 2;
            UiSessionState.MarkEscapeConsumed();

            if (fromMouseClick)
            {
                UiSessionState.LastEndedCaptureEntry = entry;
                UiSessionState.SuppressSameEntryRestart = true;
                UiSessionState.SuppressRestartReleaseFrame = -1;
            }

            UiSessionState.RefreshConflicts();
            ConflictAggregationView.RefreshConflicts();
            UiLayoutAndFontHelper.UpdateHotkeyButtonVisual(entry);
            if (UiSessionState.CurrentManager != null)
            {
                UiSessionState.SetKeyBindStatusDelegate?.Invoke(UiSessionState.CurrentManager, string.Empty);
            }
        }
    }
}
