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

        [Fact]
        public void KnownModAdapters_ConflictScanner_And_ShowItemNames_UnbindToNone_Persistence()
        {
            // Verify ConflictScanner v1.6.5 default key is F7
            var csDefault = ComboKeyStore.GetEffectiveCustomHotkey("conflict_scanner.ReportKey", KeyModifierMask.None, KeyCode.F7);
            Assert.Equal(KeyCode.F7, csDefault.Key);

            // Set to None (unbound)
            ComboKeyStore.SetSavedCustomHotkey("conflict_scanner.ReportKey", KeyModifierMask.None, KeyCode.None, KeyModifierMask.None, KeyCode.F7);
            var csEffective = ComboKeyStore.GetEffectiveCustomHotkey("conflict_scanner.ReportKey", KeyModifierMask.None, KeyCode.F7);
            Assert.Equal(KeyCode.None, csEffective.Key);

            // Set ShowItemNames to None
            ComboKeyStore.SetSavedCustomHotkey("ShowItemNames.Hotkey", KeyModifierMask.None, KeyCode.None, KeyModifierMask.None, KeyCode.LeftAlt);
            var sinEffective = ComboKeyStore.GetEffectiveCustomHotkey("ShowItemNames.Hotkey", KeyModifierMask.None, KeyCode.LeftAlt);
            Assert.Equal(KeyCode.None, sinEffective.Key);

            // Verify SyncExternalNonConfigHotkeysOnBoot executes cleanly without throwing
            KnownModAdapters.SyncExternalNonConfigHotkeysOnBoot();
        }

        [Fact]
        public void FastQuit_And_ModEntryQuittingState_Safety()
        {
            Assert.False(ModEntry.IsApplicationQuitting);

            // Test PrepareFastQuit idempotency and state transition
            PerformancePatches.PrepareFastQuit();
            Assert.True(ModEntry.IsApplicationQuitting);

            // Calling again while quitting is safe and idempotent
            PerformancePatches.PrepareFastQuit();
            Assert.True(ModEntry.IsApplicationQuitting);
        }

        [Fact]
        public void WorkshopModPatcher_SiaTrySet_NullSafe()
        {
            string? error = null;
            bool result = false;
            // When instance or target is null, prefix safely yields to original method (returns true)
            bool runOrig = WorkshopModPatcher.SmartInventoryArrange_TrySet_Prefix(new object(), null!, new object(), ref error, ref result);
            Assert.True(runOrig);

            // Assert parameter name is 'target' (not 'binding') to avoid Harmony binding warning
            var method = typeof(WorkshopModPatcher).GetMethod(nameof(WorkshopModPatcher.SmartInventoryArrange_TrySet_Prefix));
            Assert.NotNull(method);
            var parameters = method!.GetParameters();
            Assert.Equal("target", parameters[1].Name);
        }

        [Fact]
        public void MouseButtons_RightAndMiddleClick_FormattingAndBilingualSupport()
        {
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            Assert.Equal("右键", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse1));
            Assert.Equal("中键", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse2));
            Assert.Equal("左键", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse0));
            Assert.Equal("Ctrl + 右键", ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl, KeyCode.Mouse1));
            Assert.Equal("Alt + 中键", ComboKeyStore.FormatCombo(KeyModifierMask.Alt, KeyCode.Mouse2));

            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
            Assert.Equal("Right Click", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse1));
            Assert.Equal("Middle Click", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse2));
            Assert.Equal("Left Click", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse0));
            Assert.Equal("Ctrl + Right Click", ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl, KeyCode.Mouse1));
            Assert.Equal("Alt + Middle Click", ComboKeyStore.FormatCombo(KeyModifierMask.Alt, KeyCode.Mouse2));
        }

        [Fact]
        public void ConflictScanner_LegacyPrefsFallback_DoesNotOverwriteToF7()
        {
            // Case 1: Legacy PlayerPrefs has "F10", custom_hotkeys.v1 is completely empty
            KnownModAdapters.ResetBootSyncForTest();
            PrefsStorage.SetString("conflict_scanner.report_key", "F10");

            KnownModAdapters.SyncExternalNonConfigHotkeysOnBoot();

            // Must NOT have been overwritten to F7
            string afterSync1 = PrefsStorage.GetString("conflict_scanner.report_key", string.Empty);
            Assert.Equal("F10", afterSync1);

            // Case 2: Legacy PlayerPrefs has "None" (user unbound in ConflictScanner UI), custom_hotkeys.v1 is empty
            KnownModAdapters.ResetBootSyncForTest();
            PrefsStorage.SetString("conflict_scanner.report_key", "None");

            KnownModAdapters.SyncExternalNonConfigHotkeysOnBoot();

            string afterSync2 = PrefsStorage.GetString("conflict_scanner.report_key", string.Empty);
            Assert.Equal("None", afterSync2);
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
