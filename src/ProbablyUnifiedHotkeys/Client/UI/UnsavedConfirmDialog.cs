#nullable enable
using System;
using TMPro;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 退出选项未保存防丢确认模态弹窗与窗口关闭安全钩子控制器。
    /// 彻底切断对主窗口生命周期的强循环引用，支持按需注入 Apply 回调。
    /// </summary>
    public static class UnsavedConfirmDialog
    {
        /// <summary>
        /// 安全平滑关闭增强选项窗口（优先关闭确认模态框，确保标记位生效后关闭窗口）。
        /// </summary>
        public static void CloseEnhancedWindowSafely(Action? onClosedAfterAction = null)
        {
            CloseUnsavedChangesConfirmDialog();
            UiSessionState.IsExplicitWindowClosing = true;
            if (UiSessionState.CurrentWindow != null && UiSessionState.CurrentWindow.IsAlive)
            {
                UiSessionState.CurrentWindow.Close();
            }
            else if (UiSessionState.CurrentManager != null)
            {
                UiSessionState.CurrentManager.CloseUI();
            }
            onClosedAfterAction?.Invoke();
        }

        /// <summary>
        /// 关闭退出未保存确认对话框。
        /// </summary>
        public static void CloseUnsavedChangesConfirmDialog()
        {
            if (UiSessionState.UnsavedConfirmWindow != null)
            {
                var win = UiSessionState.UnsavedConfirmWindow;
                UiSessionState.UnsavedConfirmWindow = null;
                if (win.IsAlive)
                {
                    win.Close();
                }
            }
        }

        /// <summary>
        /// 弹出退出未保存确认模态框（含“保存并退出”、“放弃修改”、“取消”三态选择）。
        /// </summary>
        public static void ShowUnsavedChangesConfirmDialog(Action onSaveAndExit, Action onDiscardAndExit)
        {
            if (UiSessionState.UnsavedConfirmWindow != null && UiSessionState.UnsavedConfirmWindow.IsAlive)
            {
                return;
            }

            if (CustomUIManager.Instance == null)
            {
                return;
            }

            string title = I18n.Get("未保存的更改", "Unsaved Changes");
            var promptBuilder = CustomUIManager.Instance.CreateWindow(
                "puh_unsaved_confirm",
                title,
                "overlay",
                null,
                CustomUIManager.Style.Auto
            );

            UiSessionState.UnsavedConfirmWindow = promptBuilder.Handle;
            promptBuilder.SetSize(560f, 185f);
            promptBuilder.Center();
            promptBuilder.SetCloseOnEscape(true);
            promptBuilder.SetDraggable(true);
            promptBuilder.OnClose(() =>
            {
                UiSessionState.UnsavedConfirmWindow = null;
            });

            if (UiSessionState.UnsavedConfirmWindow?.frame?.titleText != null)
            {
                UiSessionState.UnsavedConfirmWindow.frame.titleText.alignment = TextAlignmentOptions.Center;
                UiSessionState.UnsavedConfirmWindow.frame.titleText.margin = Vector4.zero;
                I18n.ApplyFontIfRuntime(UiSessionState.UnsavedConfirmWindow.frame.titleText, title);
            }

            promptBuilder.AddSpace(12f);

            string body = I18n.Get(
                "检测到您修改了按键或设置但尚未保存，是否保存后再退出？",
                "You have unsaved changes to your keybinds or settings. Save now before exiting?"
            );
            promptBuilder.BeginRow(0f, TextAnchor.MiddleCenter);
            promptBuilder.AddFlexibleSpace();
            promptBuilder.AddLabel(body, "puh_unsaved_body");
            var bodyEl = promptBuilder.Last;
            if (bodyEl?.text != null)
            {
                bodyEl.text.alignment = TextAlignmentOptions.Center;
                bodyEl.text.enableWordWrapping = false;
                bodyEl.text.overflowMode = TextOverflowModes.Overflow;
                I18n.ApplyFontIfRuntime(bodyEl.text, body);
            }
            promptBuilder.AddFlexibleSpace();
            promptBuilder.End();

            promptBuilder.AddSpace(14f);

            promptBuilder.BeginRow(12f, TextAnchor.MiddleCenter);
            promptBuilder.AddFlexibleSpace();

            // 按钮 1：保存并退出
            string btnSaveText = I18n.Get("保存并退出", "Save & Exit");
            promptBuilder.AddButton(btnSaveText, () =>
            {
                CloseUnsavedChangesConfirmDialog();
                onSaveAndExit?.Invoke();
            }, "puh_btn_save_exit");
            var btnSaveEl = promptBuilder.Last;
            if (btnSaveEl != null)
            {
                btnSaveEl.SetFixedWidth(118f);
                btnSaveEl.SetPreferredSize(118f, 36f);
                btnSaveEl.SetFlexible(0f, 0f);
                if (btnSaveEl.text != null)
                {
                    btnSaveEl.text.alignment = TextAlignmentOptions.Center;
                    btnSaveEl.text.enableWordWrapping = false;
                    btnSaveEl.text.overflowMode = TextOverflowModes.Overflow;
                    I18n.ApplyFontIfRuntime(btnSaveEl.text, btnSaveText);
                }
            }

            // 按钮 2：放弃修改
            string btnDiscardText = I18n.Get("放弃修改", "Discard");
            promptBuilder.AddButton(btnDiscardText, () =>
            {
                CloseUnsavedChangesConfirmDialog();
                onDiscardAndExit?.Invoke();
            }, "puh_btn_discard_exit");
            var btnDiscardEl = promptBuilder.Last;
            if (btnDiscardEl != null)
            {
                btnDiscardEl.SetFixedWidth(108f);
                btnDiscardEl.SetPreferredSize(108f, 36f);
                btnDiscardEl.SetFlexible(0f, 0f);
                if (btnDiscardEl.text != null)
                {
                    btnDiscardEl.text.alignment = TextAlignmentOptions.Center;
                    btnDiscardEl.text.enableWordWrapping = false;
                    btnDiscardEl.text.overflowMode = TextOverflowModes.Overflow;
                    I18n.ApplyFontIfRuntime(btnDiscardEl.text, btnDiscardText);
                }
            }

            // 按钮 3：取消
            string btnCancelText = I18n.Get("取消", "Cancel");
            promptBuilder.AddButton(btnCancelText, () =>
            {
                CloseUnsavedChangesConfirmDialog();
            }, "puh_btn_cancel");
            var btnCancelEl = promptBuilder.Last;
            if (btnCancelEl != null)
            {
                btnCancelEl.SetFixedWidth(96f);
                btnCancelEl.SetPreferredSize(96f, 36f);
                btnCancelEl.SetFlexible(0f, 0f);
                if (btnCancelEl.text != null)
                {
                    btnCancelEl.text.alignment = TextAlignmentOptions.Center;
                    btnCancelEl.text.enableWordWrapping = false;
                    btnCancelEl.text.overflowMode = TextOverflowModes.Overflow;
                    I18n.ApplyFontIfRuntime(btnCancelEl.text, btnCancelText);
                }
            }

            promptBuilder.AddFlexibleSpace();
            promptBuilder.End();

            promptBuilder.AddSpace(12f);
            promptBuilder.Show();
        }

        /// <summary>
        /// 关闭前检查未保存修改并弹出提示。
        /// </summary>
        public static void PromptUnsavedChangesBeforeClosing(Action<GameOptionUIManager>? applyAllCallback = null)
        {
            if (UiSessionState.UnsavedConfirmWindow != null && UiSessionState.UnsavedConfirmWindow.IsAlive)
            {
                return;
            }

            var callback = applyAllCallback ?? UiSessionState.ApplyAllSettingsCallback;

            ShowUnsavedChangesConfirmDialog(
                onSaveAndExit: () =>
                {
                    if (UiSessionState.CurrentManager != null)
                    {
                        callback?.Invoke(UiSessionState.CurrentManager);
                    }
                    CloseEnhancedWindowSafely();
                },
                onDiscardAndExit: () =>
                {
                    UiSessionState.DiscardAllPendingChanges();
                    CloseEnhancedWindowSafely();
                }
            );
        }

        /// <summary>
        /// 拦截右上角原生关闭按钮，挂载未保存拦截状态机。
        /// </summary>
        public static void HookWindowCloseButton(Action? onCloseButtonClicked = null)
        {
            if (UiSessionState.CurrentWindow == null || UiSessionState.CurrentWindow.frame == null || UiSessionState.CurrentWindow.frame.closeButton == null)
            {
                return;
            }

            var btn = UiSessionState.CurrentWindow.frame.closeButton;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                if (onCloseButtonClicked != null)
                {
                    onCloseButtonClicked.Invoke();
                    return;
                }

                if (UiSessionState.HasUnsavedChanges())
                {
                    PromptUnsavedChangesBeforeClosing();
                }
                else
                {
                    CloseEnhancedWindowSafely();
                }
            });
        }
    }
}
