using System;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class Batch2AdapterAndBridgeTests : TestBase
    {
        [Fact]
        public void SectionLocalizer_KnownDictionary_BilingualMapping_Chinese()
        {
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;

            Assert.Equal("常规", SectionLocalizer.FormatModSectionLabel("General"));
            Assert.Equal("快捷键", SectionLocalizer.FormatModSectionLabel("Hotkeys"));
            Assert.Equal("背包与库存", SectionLocalizer.FormatModSectionLabel("Inventory"));
            Assert.Equal("提示框设置", SectionLocalizer.FormatModSectionLabel("Tooltips"));
        }

        [Fact]
        public void SectionLocalizer_KnownDictionary_BilingualMapping_English()
        {
            BilingualText.ModeOverrideForTest = UiLanguageMode.English;

            Assert.Equal("General", SectionLocalizer.FormatModSectionLabel("General"));
            Assert.Equal("Hotkeys", SectionLocalizer.FormatModSectionLabel("Hotkeys"));
            Assert.Equal("Inventory", SectionLocalizer.FormatModSectionLabel("Inventory"));
            Assert.Equal("Tooltips", SectionLocalizer.FormatModSectionLabel("Tooltips"));
        }

        [Fact]
        public void SectionLocalizer_StructuredKey_IntelligentHumanization()
        {
            // 1. nicokobo.speed-up structured dictionary key
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            Assert.Equal("对话", SectionLocalizer.FormatModSectionLabel("speed.config.dialogue"));

            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
            Assert.Equal("Dialogue", SectionLocalizer.FormatModSectionLabel("speed.config.dialogue"));

            // 2. Structured dotted and prefixed key from unknown mod: "some_mod.settings_section_combat"
            // Should strip dotted and underscore prefix, humanizing to "Combat"
            string humanizedCombat = SectionLocalizer.FormatModSectionLabel("some_mod.settings_section_combat");
            Assert.Equal("Combat", humanizedCombat);
            Assert.DoesNotContain("some_mod", humanizedCombat);
            Assert.DoesNotContain("settings_section_", humanizedCombat);

            // 3. Structured dotted key whose tail matches known dictionary: "mod.config.hotkeys"
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            Assert.Equal("快捷键", SectionLocalizer.FormatModSectionLabel("mod.config.hotkeys"));

            // 4. Underscore prefix key whose tail matches known dictionary: "settings_section_audio"
            Assert.Equal("音频与音效", SectionLocalizer.FormatModSectionLabel("settings_section_audio"));

            // 5. Empty / null / whitespace safety
            Assert.Equal(string.Empty, SectionLocalizer.FormatModSectionLabel(null));
            Assert.Equal(string.Empty, SectionLocalizer.FormatModSectionLabel(""));
            Assert.Equal(string.Empty, SectionLocalizer.FormatModSectionLabel("   "));
        }

        [Fact]
        public void OptionUiHost_NullSafe_And_ComboKeyStore_ExternalBypass_Linkage()
        {
            // 1. Initial State: OptionUiHost.Active is null
            OptionUiHost.Active = null;

            ComboKeyStore.ExternalBypassPredicate = () =>
                (OptionUiHost.Active?.IsEnhancedWindowOpen ?? false) ||
                (OptionUiHost.Active?.IsCapturingModHotkey ?? false);

            Assert.False(ComboKeyStore.IsBypassed, "ComboKeyStore should not be bypassed when OptionUiHost.Active is null");

            // 2. Register mock bridge
            var mock = new MockOptionUiBridge();
            OptionUiHost.Active = mock;

            Assert.False(ComboKeyStore.IsBypassed, "When mock is inactive, bypass should remain false");

            // 3. Window open should trigger bypass
            mock.IsEnhancedWindowOpen = true;
            Assert.True(ComboKeyStore.IsBypassed, "When enhanced window is open, bypass should be true");

            // 4. Capturing mod hotkey should trigger bypass
            mock.IsEnhancedWindowOpen = false;
            mock.IsCapturingModHotkey = true;
            Assert.True(ComboKeyStore.IsBypassed, "When capturing mod hotkey, bypass should be true");

            // 5. Turn off capturing
            mock.IsCapturingModHotkey = false;
            Assert.False(ComboKeyStore.IsBypassed, "When neither open nor capturing, bypass should be false");

            // 6. Reset OptionUiHost.Active to null (teardown / null safety)
            OptionUiHost.Active = null;
            Assert.False(ComboKeyStore.IsBypassed, "When OptionUiHost.Active is reset to null, bypass should be false");
        }

        [Fact]
        public void ModCatalogCache_EventSubscriptions_And_ResetSession()
        {
            // Idempotent event subscriptions
            ModCatalogCache.EnsureEventSubscriptions();
            ModCatalogCache.EnsureEventSubscriptions();

            bool eventFired = false;
            Action handler = () => { eventFired = true; };
            ModCatalogCache.SessionResetRequested += handler;

            try
            {
                var entry = new ModHotkeyEntry
                {
                    Id = "test.mod.key",
                    ModId = "test.mod",
                    DisplayName = "Test Key",
                    HasPendingChange = true,
                    PendingKey = KeyCode.F5,
                    PendingModifiers = KeyModifierMask.Ctrl
                };

                var hotkeys = new System.Collections.Generic.List<ModHotkeyEntry> { entry };
                var groups = new System.Collections.Generic.List<ModOptionGroup>();

                ModCatalogCache.ResetPendingSessionState(groups, hotkeys);

                Assert.False(entry.HasPendingChange);
                Assert.Equal(KeyCode.None, entry.PendingKey);
                Assert.Equal(KeyModifierMask.None, entry.PendingModifiers);
                Assert.True(eventFired, "SessionResetRequested event should have been fired");
            }
            finally
            {
                ModCatalogCache.SessionResetRequested -= handler;
            }
        }

        private sealed class MockOptionUiBridge : IOptionUiBridge
        {
            public bool IsEnhancedWindowOpen { get; set; }
            public bool IsCapturingAnyKey { get; set; }
            public bool IsCapturingModHotkey { get; set; }
            public bool IsSuppressingEscapeAfterCapture { get; set; }
            public bool IsExplicitWindowClosing { get; set; }
            public bool IsInEnhancedRefreshRules { get; set; }
            public CustomUIWindow? CurrentWindow => null;
            public CustomUIWindow? UnsavedConfirmWindow => null;

            public bool TryOpenEnhancedUI(GameOptionUIManager instance) => true;
            public bool HasUnsavedChanges() => false;
            public void PromptUnsavedChangesBeforeClosing() { }
            public void MarkEscapeConsumed() { }
            public void TickCapture() { }
            public void OnVanillaRulesRefreshed() { }
            public void OnVanillaKeyChanged() { }
            public bool StartCaptureVanillaOption(GameOptionUIManager instance, string optionId) => true;
            public bool ShouldSilenceMissingElementWarning(CustomUIWindow instance, string elementId, out CustomUIElement? found)
            {
                found = null;
                return false;
            }
            public void ResetSessionState() { }
        }
    }
}
