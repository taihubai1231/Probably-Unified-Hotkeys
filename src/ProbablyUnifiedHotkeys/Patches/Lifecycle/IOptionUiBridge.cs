using System;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 统一设置与改键中心 UI 桥梁接口：
    /// 彻底切断 Patches 对巨石类 UnifiedOptionUI 的静态强引用，
    /// 在第 2 批 UI 未挂载时，Active 为 null 实现优雅降级放行原版，杜绝黑屏死锁。
    /// </summary>
    public interface IOptionUiBridge
    {
        bool TryOpenEnhancedUI(GameOptionUIManager instance);
        bool IsEnhancedWindowOpen { get; }
        bool IsCapturingAnyKey { get; }
        bool IsCapturingModHotkey { get; }
        bool IsSuppressingEscapeAfterCapture { get; }
        bool IsExplicitWindowClosing { get; }
        bool IsInEnhancedRefreshRules { get; set; }
        CustomUIWindow? CurrentWindow { get; }
        CustomUIWindow? UnsavedConfirmWindow { get; }
        bool HasUnsavedChanges();
        void PromptUnsavedChangesBeforeClosing();
        void MarkEscapeConsumed();
        void TickCapture();
        void OnVanillaRulesRefreshed();
        void OnVanillaKeyChanged();
        bool StartCaptureVanillaOption(GameOptionUIManager instance, string optionId);
        bool ShouldSilenceMissingElementWarning(CustomUIWindow instance, string elementId, out CustomUIElement? found);
        void ResetSessionState();
    }

    /// <summary>
    /// 选项界面宿主持有者，供 Client/UI 挂载注册。
    /// </summary>
    public static class OptionUiHost
    {
        public static IOptionUiBridge? Active { get; set; }
    }
}
