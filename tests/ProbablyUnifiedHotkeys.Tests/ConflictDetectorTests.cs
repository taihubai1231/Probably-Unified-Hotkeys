using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class ConflictDetectorTests : TestBase
    {
        [Fact]
        public void MultipleNoneEntries_NeverDeemedConflict()
        {
            var entry1 = new ModHotkeyEntry
            {
                Id = "entry.none.1",
                DisplayName = "Action 1",
                GetKey = () => KeyCode.None
            };
            var entry2 = new ModHotkeyEntry
            {
                Id = "entry.none.2",
                DisplayName = "Action 2",
                GetKey = () => KeyCode.None
            };
            var entry3 = new ModHotkeyEntry
            {
                Id = "entry.none.3",
                DisplayName = "Action 3",
                GetKey = () => KeyCode.None
            };

            var conflicts = ConflictDetector.Analyze(new[] { entry1, entry2, entry3 });
            Assert.Empty(conflicts);
        }

        [Fact]
        public void DifferentModifiers_DoNotConflict_SameModifiers_DoConflict()
        {
            var entryCtrlE = new ModHotkeyEntry
            {
                Id = "entry.ctrl_e.1",
                ModDisplayName = "Mod Alpha",
                DisplayName = "Action Alpha",
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.E
            };
            entryCtrlE.GetExternalModifiers = () => KeyModifierMask.Ctrl;

            var entryShiftE = new ModHotkeyEntry
            {
                Id = "entry.shift_e",
                ModDisplayName = "Mod Beta",
                DisplayName = "Action Beta",
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.E
            };
            entryShiftE.GetExternalModifiers = () => KeyModifierMask.Shift;

            var entryPlainE = new ModHotkeyEntry
            {
                Id = "entry.plain_e",
                ModDisplayName = "Mod Gamma",
                DisplayName = "Action Gamma",
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.E
            };
            entryPlainE.GetExternalModifiers = () => KeyModifierMask.None;

            // These three have different modifier masks (Ctrl, Shift, None), so NO conflict
            var noConflicts = ConflictDetector.Analyze(new[] { entryCtrlE, entryShiftE, entryPlainE });
            Assert.Empty(noConflicts);

            // Now add a fourth entry also bound to Ctrl + E
            var entryCtrlE2 = new ModHotkeyEntry
            {
                Id = "entry.ctrl_e.2",
                ModDisplayName = "Mod Delta",
                DisplayName = "Action Delta",
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.E
            };
            entryCtrlE2.GetExternalModifiers = () => KeyModifierMask.Ctrl;

            var conflicts = ConflictDetector.Analyze(new[] { entryCtrlE, entryShiftE, entryPlainE, entryCtrlE2 });
            Assert.Single(conflicts);

            var conflictCombo = new KeyCombo(KeyModifierMask.Ctrl, KeyCode.E);
            Assert.True(conflicts.ContainsKey(conflictCombo));
            Assert.Equal(2, conflicts[conflictCombo].Count);
            Assert.Contains(entryCtrlE, conflicts[conflictCombo]);
            Assert.Contains(entryCtrlE2, conflicts[conflictCombo]);
        }

        [Fact]
        public void ReadOnlyProbedEntries_PureProbedDoNotConflict_WithEditableEntryConflicts()
        {
            // Pure read-only probed entries (e.g. from static assemblies)
            var probed1 = new ModHotkeyEntry
            {
                Id = "probed.1",
                ModDisplayName = "Mod ThirdParty 1",
                DisplayName = "Hidden Action 1",
                IsReadOnlyProbed = true,
                GetKey = () => KeyCode.F
            };
            var probed2 = new ModHotkeyEntry
            {
                Id = "probed.2",
                ModDisplayName = "Mod ThirdParty 2",
                DisplayName = "Hidden Action 2",
                IsReadOnlyProbed = true,
                GetKey = () => KeyCode.F
            };

            // Two probed items alone should NOT report conflict (avoid false positive warnings for unmodifiable third-party code)
            var conflictsPureProbed = ConflictDetector.Analyze(new[] { probed1, probed2 });
            Assert.Empty(conflictsPureProbed);

            // Once an editable entry is present with the same key, it MUST report conflict
            var editable = new ModHotkeyEntry
            {
                Id = "editable.1",
                ModDisplayName = "Mod UserConfigurable",
                DisplayName = "User Action",
                IsReadOnlyProbed = false,
                GetKey = () => KeyCode.F
            };

            var conflictsWithEditable = ConflictDetector.Analyze(new[] { probed1, probed2, editable });
            Assert.Single(conflictsWithEditable);

            var keyF = new KeyCombo(KeyModifierMask.None, KeyCode.F);
            Assert.Equal(3, conflictsWithEditable[keyF].Count);
        }

        [Fact]
        public void ConflictTooltip_Bilingual_AndSortedConflictGroups()
        {
            var item1 = new ModHotkeyEntry
            {
                Id = "h1",
                ModDisplayName = "Mod A",
                DisplayName = "Inventory",
                GetKey = () => KeyCode.I
            };
            var item2 = new ModHotkeyEntry
            {
                Id = "h2",
                ModDisplayName = "Mod B",
                DisplayName = "Inspect",
                IsReadOnlyProbed = true,
                GetKey = () => KeyCode.I
            };

            var conflicts = ConflictDetector.Analyze(new[] { item1, item2 });

            // Chinese tooltip
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            string zhTooltip = ConflictDetector.GetConflictTooltip(item1, conflicts);
            Assert.Contains("[!] 按键冲突提醒", zhTooltip);
            Assert.Contains("[Mod B] Inspect (只读探测)", zhTooltip);

            // English tooltip
            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
            string enTooltip = ConflictDetector.GetConflictTooltip(item1, conflicts);
            Assert.Contains("[!] Keybind Conflict", enTooltip);
            Assert.Contains("[Mod B] Inspect (Probed · Read-Only)", enTooltip);

            // Sorted groups test: 3-way conflict should precede 2-way conflict
            var groupA1 = new ModHotkeyEntry { Id = "a1", GetKey = () => KeyCode.A };
            var groupA2 = new ModHotkeyEntry { Id = "a2", GetKey = () => KeyCode.A };
            var groupA3 = new ModHotkeyEntry { Id = "a3", GetKey = () => KeyCode.A };

            var groupB1 = new ModHotkeyEntry { Id = "b1", GetKey = () => KeyCode.B };
            var groupB2 = new ModHotkeyEntry { Id = "b2", GetKey = () => KeyCode.B };

            var allConflicts = ConflictDetector.Analyze(new[] { groupA1, groupA2, groupA3, groupB1, groupB2 });
            var sorted = ConflictDetector.GetSortedConflictGroups(allConflicts);

            Assert.Equal(2, sorted.Count);
            Assert.Equal(KeyCode.A, sorted[0].Combo.Key);
            Assert.Equal(3, sorted[0].ConflictingEntries.Count);
            Assert.Equal(KeyCode.B, sorted[1].Combo.Key);
            Assert.Equal(2, sorted[1].ConflictingEntries.Count);
        }
    }
}
