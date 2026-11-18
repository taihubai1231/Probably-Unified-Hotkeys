using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class KeyComboAndStoreTests : TestBase
    {
        [Fact]
        public void KeyCombo_WithNoneKey_ClearsModifiers_EnforcesInvariant()
        {
            // Requirement: new KeyCombo(KeyModifierMask.Ctrl, KeyCode.None) auto clears modifier mask to None
            var combo1 = new KeyCombo(KeyModifierMask.Ctrl, KeyCode.None);
            Assert.Equal(KeyModifierMask.None, combo1.Modifiers);
            Assert.Equal(KeyCode.None, combo1.Key);
            Assert.True(combo1.IsEmpty);

            // With CustomFormatter active (English mode vs Chinese mode)
            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
            Assert.Equal("None", combo1.ToString());
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            Assert.Equal("无", combo1.ToString());

            // With CustomFormatter null, fallback returns "None"
            KeyCombo.CustomFormatter = null;
            Assert.Equal("None", combo1.ToString());
            ComboKeyStore.EnsureInitialized();

            // Multi-modifiers with None
            var combo2 = new KeyCombo(KeyModifierMask.Ctrl | KeyModifierMask.Shift | KeyModifierMask.Alt, KeyCode.None);
            Assert.Equal(KeyModifierMask.None, combo2.Modifiers);
            Assert.Equal(KeyCode.None, combo2.Key);
            Assert.True(combo2.IsEmpty);

            // Redundant modifier in key itself should be stripped from modifiers
            var combo3 = new KeyCombo(KeyModifierMask.Ctrl, KeyCode.LeftControl);
            Assert.Equal(KeyModifierMask.None, combo3.Modifiers);
            Assert.Equal(KeyCode.LeftControl, combo3.Key);
            Assert.False(combo3.IsEmpty);

            // Valid combo
            var combo4 = new KeyCombo(KeyModifierMask.Ctrl | KeyModifierMask.Shift, KeyCode.E);
            Assert.Equal(KeyModifierMask.Ctrl | KeyModifierMask.Shift, combo4.Modifiers);
            Assert.Equal(KeyCode.E, combo4.Key);
            Assert.False(combo4.IsEmpty);
        }

        [Fact]
        public void ComboKeyStore_SaveCtrlE_ThenSetNone_ClearsModifiers_LockingFakeDirtyFlagRootCause()
        {
            // Lock down v1.4.7 root cause:
            // When setting hotkey to KeyCode.None, GetSavedModifiers and EffectiveModifiers MUST return KeyModifierMask.None!
            const string entryId = "test.mod.action_rotate";
            var entry = new ModHotkeyEntry
            {
                Id = entryId,
                DisplayName = "Rotate CW",
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.E,
                DefaultModifiers = KeyModifierMask.None
            };

            // 1. Initially default
            entry.GetKey = () => KeyCode.E;
            Assert.Equal(KeyModifierMask.None, entry.EffectiveModifiers);
            Assert.Equal(KeyCode.E, entry.EffectiveKey);

            // 2. Save Ctrl + E
            ComboKeyStore.SetSavedCustomHotkey(entryId, KeyModifierMask.Ctrl, KeyCode.E);
            Assert.Equal(KeyModifierMask.Ctrl, ComboKeyStore.GetSavedModifiers(entryId, KeyCode.E));
            Assert.Equal(KeyModifierMask.Ctrl, entry.EffectiveModifiers);
            Assert.Equal(new KeyCombo(KeyModifierMask.Ctrl, KeyCode.E), entry.EffectiveCombo);

            // 3. User unbinds action: sets key to KeyCode.None
            entry.GetKey = () => KeyCode.None;

            // Querying with KeyCode.None MUST return KeyModifierMask.None
            Assert.Equal(KeyModifierMask.None, ComboKeyStore.GetSavedModifiers(entryId, KeyCode.None));
            // Entry EffectiveModifiers MUST be KeyModifierMask.None
            Assert.Equal(KeyModifierMask.None, entry.EffectiveModifiers);
            Assert.Equal(KeyCode.None, entry.EffectiveKey);
            Assert.True(entry.EffectiveCombo.IsEmpty);

            // 4. Persistence layer SetSavedCustomHotkey with KeyCode.None completely clears saved entry
            ComboKeyStore.SetSavedCustomHotkey(entryId, KeyModifierMask.Ctrl, KeyCode.None);
            Assert.Equal(KeyModifierMask.None, ComboKeyStore.GetSavedModifiers(entryId, KeyCode.None));
            Assert.Equal(KeyModifierMask.None, ComboKeyStore.GetSavedModifiers(entryId, KeyCode.E));
            Assert.False(ComboKeyStore.HasSavedCustomHotkey(entryId));
        }

        [Fact]
        public void MouseSideButtons_FormattingAndBilingualSupport()
        {
            // Verify Mouse3 ~ Mouse6 formatting in both Chinese and English
            // Chinese Mode
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            Assert.Equal("鼠标侧键1", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse3));
            Assert.Equal("鼠标侧键2", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse4));
            Assert.Equal("鼠标侧键3", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse5));
            Assert.Equal("鼠标侧键4", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse6));
            Assert.Equal("左键", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse0));
            Assert.Equal("右键", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse1));
            Assert.Equal("中键", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse2));
            Assert.Equal("无", ComboKeyStore.FormatPrimaryKeyName(KeyCode.None));

            Assert.Equal("Ctrl + 鼠标侧键2", ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl, KeyCode.Mouse4));
            Assert.Equal("Ctrl+Shift+鼠标侧键1", ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl | KeyModifierMask.Shift, KeyCode.Mouse3));

            // English Mode
            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
            Assert.Equal("Mouse Side 1", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse3));
            Assert.Equal("Mouse Side 2", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse4));
            Assert.Equal("Mouse Side 3", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse5));
            Assert.Equal("Mouse Side 4", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse6));
            Assert.Equal("Left Click", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse0));
            Assert.Equal("Right Click", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse1));
            Assert.Equal("Middle Click", ComboKeyStore.FormatPrimaryKeyName(KeyCode.Mouse2));
            Assert.Equal("None", ComboKeyStore.FormatPrimaryKeyName(KeyCode.None));

            Assert.Equal("Ctrl + Mouse Side 2", ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl, KeyCode.Mouse4));
            Assert.Equal("Ctrl+Shift+Mouse Side 1", ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl | KeyModifierMask.Shift, KeyCode.Mouse3));

            // KeyCombo.ToString() uses CustomFormatter connected from ComboKeyStore
            var combo = new KeyCombo(KeyModifierMask.Alt, KeyCode.Mouse5);
            Assert.Equal("Alt + Mouse Side 3", combo.ToString());

            // IsMouseSideButton check
            Assert.True(ComboKeyStore.IsMouseSideButton(KeyCode.Mouse3));
            Assert.True(ComboKeyStore.IsMouseSideButton(KeyCode.Mouse4));
            Assert.True(ComboKeyStore.IsMouseSideButton(KeyCode.Mouse5));
            Assert.True(ComboKeyStore.IsMouseSideButton(KeyCode.Mouse6));
            Assert.False(ComboKeyStore.IsMouseSideButton(KeyCode.Mouse0));
            Assert.False(ComboKeyStore.IsMouseSideButton(KeyCode.Space));
        }

        [Fact]
        public void ForcedAndIgnoredCandidateIds_PersistenceAndSetOperations()
        {
            const string id1 = "mod.forced.test1";
            const string id2 = "mod.ignored.test2";

            // Add forced and ignored
            ComboKeyStore.SetForcedHotkey(id1, true);
            ComboKeyStore.SetIgnoredCandidate(id2, true);

            Assert.True(ComboKeyStore.IsForcedHotkey(id1));
            Assert.False(ComboKeyStore.IsForcedHotkey(id2));
            Assert.True(ComboKeyStore.IsIgnoredCandidate(id2));
            Assert.False(ComboKeyStore.IsIgnoredCandidate(id1));

            // When an item in ignored is marked as forced, it should be auto removed from ignored
            ComboKeyStore.SetForcedHotkey(id2, true);
            Assert.True(ComboKeyStore.IsForcedHotkey(id2));
            Assert.False(ComboKeyStore.IsIgnoredCandidate(id2));

            // ReplaceForcedAndIgnoredSets
            var forcedList = new[] { "mod.a", "mod.b" };
            var ignoredList = new[] { "mod.c", "mod.d" };
            bool changed = ComboKeyStore.ReplaceForcedAndIgnoredSets(forcedList, ignoredList, out bool forcedChanged);
            Assert.True(changed);
            Assert.True(forcedChanged);

            var forcedSnapshot = ComboKeyStore.GetForcedHotkeyIdsSnapshot();
            Assert.Contains("mod.a", forcedSnapshot);
            Assert.Contains("mod.b", forcedSnapshot);
            Assert.DoesNotContain(id1, forcedSnapshot);

            var ignoredSnapshot = ComboKeyStore.GetIgnoredCandidateIdsSnapshot();
            Assert.Contains("mod.c", ignoredSnapshot);
            Assert.Contains("mod.d", ignoredSnapshot);

            // Reload from storage to verify persistent encoding and decoding
            ComboKeyStore.Load();
            Assert.True(ComboKeyStore.IsForcedHotkey("mod.a"));
            Assert.True(ComboKeyStore.IsIgnoredCandidate("mod.c"));
        }

        [Fact]
        public void ModifierKeySuppression_AndInputRemapEvaluation()
        {
            // 1. Remap evaluation: remap Ctrl+E to Shift+F
            const string remapId = "remap.action";
            ComboKeyStore.RegisterInputKeyRemap(remapId, KeyModifierMask.Ctrl, KeyCode.E, KeyModifierMask.Shift, KeyCode.F);

            // When queried for KeyCode.E:
            // If physical Shift+F is NOT pressed (via hooks), should return false (or true when pressed)
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.F;
            ComboKeyStore.HeldModifiersOverride = k => KeyModifierMask.Shift;

            bool handled = ComboKeyStore.TryEvaluateInputRemap(KeyCode.E, ComboKeyStore.InputQueryKind.Down, out bool downResult);
            Assert.True(handled);
            Assert.True(downResult);

            // If physical modifier does not match target (e.g. None instead of Shift)
            ComboKeyStore.HeldModifiersOverride = k => KeyModifierMask.None;
            handled = ComboKeyStore.TryEvaluateInputRemap(KeyCode.E, ComboKeyStore.InputQueryKind.Down, out downResult);
            Assert.True(handled);
            Assert.False(downResult);

            // Clean remaps
            ComboKeyStore.ClearInputKeyRemaps();

            // 2. Anti-accidental suppression of plain key when modifiers are held
            var entry = new ModHotkeyEntry
            {
                Id = "suppress.test.ctrl_c",
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.C,
                DefaultModifiers = KeyModifierMask.None
            };
            entry.GetKey = () => KeyCode.C;
            ComboKeyStore.SetSavedCustomHotkey("suppress.test.ctrl_c", KeyModifierMask.Ctrl, KeyCode.C);

            ComboKeyStore.RebuildRuntimeLookup(new[] { entry });

            // When suppression is active and Ctrl is held:
            ModFeatureConfig.OverrideKeySuppressionForTests = true;
            ComboKeyStore.HeldModifiersOverride = k => KeyModifierMask.Ctrl;
            Assert.True(ComboKeyStore.HasActiveComboConflictForPlainKey(KeyCode.C));

            // When Ctrl is NOT held:
            ComboKeyStore.HeldModifiersOverride = k => KeyModifierMask.None;
            Assert.False(ComboKeyStore.HasActiveComboConflictForPlainKey(KeyCode.C));

            // When suppression feature is disabled:
            ModFeatureConfig.OverrideKeySuppressionForTests = false;
            ComboKeyStore.HeldModifiersOverride = k => KeyModifierMask.Ctrl;
            Assert.False(ComboKeyStore.HasActiveComboConflictForPlainKey(KeyCode.C));
        }
    }
}
