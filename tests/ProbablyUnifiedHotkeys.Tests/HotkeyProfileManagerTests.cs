using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class HotkeyProfileManagerTests : TestBase
    {
        [Fact]
        public void SlotCycleSwitching_AndOutOfRangeReset()
        {
            // Verify initial slot
            HotkeyProfileManager.ActiveSlotIndex = 0;
            Assert.Equal(0, HotkeyProfileManager.ActiveSlotIndex);

            // Cycle through slots: 0 -> 1 -> 2 -> 0
            Assert.Equal(1, HotkeyProfileManager.CycleNextSlot());
            Assert.Equal(1, HotkeyProfileManager.ActiveSlotIndex);

            Assert.Equal(2, HotkeyProfileManager.CycleNextSlot());
            Assert.Equal(2, HotkeyProfileManager.ActiveSlotIndex);

            Assert.Equal(0, HotkeyProfileManager.CycleNextSlot());
            Assert.Equal(0, HotkeyProfileManager.ActiveSlotIndex);

            // Out of range indices auto clamp to 0
            HotkeyProfileManager.ActiveSlotIndex = 999;
            Assert.Equal(0, HotkeyProfileManager.ActiveSlotIndex);

            HotkeyProfileManager.ActiveSlotIndex = -10;
            Assert.Equal(0, HotkeyProfileManager.ActiveSlotIndex);

            // Display names
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            Assert.Equal("方案一", HotkeyProfileManager.GetSlotDisplayName(0));
            Assert.Equal("方案二", HotkeyProfileManager.GetSlotDisplayName(1));
            Assert.Equal("方案三", HotkeyProfileManager.GetSlotDisplayName(2));

            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
            Assert.Equal("Preset 1", HotkeyProfileManager.GetSlotDisplayName(0));
            Assert.Equal("Preset 2", HotkeyProfileManager.GetSlotDisplayName(1));
            Assert.Equal("Preset 3", HotkeyProfileManager.GetSlotDisplayName(2));
        }

        [Fact]
        public void SaveToSlot_ExportToJson_Clear_Import_Apply_RoundTrip()
        {
            // 1. Prepare sample mod option group and hotkeys
            var group = new ModOptionGroup
            {
                ModId = "test.roundtrip.mod",
                DisplayName = "RoundTrip Mod"
            };

            var toggleOpt = new ModGeneralEntry
            {
                Id = "roundtrip.toggle",
                ModId = group.ModId,
                Kind = OptionKind.Toggle,
                GetBool = () => true
            };
            var sliderOpt = new ModGeneralEntry
            {
                Id = "roundtrip.slider",
                ModId = group.ModId,
                Kind = OptionKind.Slider,
                Min = 0f,
                Max = 100f,
                GetFloat = () => 42.5f
            };
            var dropdownOpt = new ModGeneralEntry
            {
                Id = "roundtrip.dropdown",
                ModId = group.ModId,
                Kind = OptionKind.Dropdown,
                GetIndex = () => 2
            };
            var inputOpt = new ModGeneralEntry
            {
                Id = "roundtrip.input",
                ModId = group.ModId,
                Kind = OptionKind.Input,
                GetString = () => "hello_world"
            };

            group.GeneralSettings.Add(toggleOpt);
            group.GeneralSettings.Add(sliderOpt);
            group.GeneralSettings.Add(dropdownOpt);
            group.GeneralSettings.Add(inputOpt);

            var hotkey1 = new ModHotkeyEntry
            {
                Id = "roundtrip.hk.ctrl_k",
                DisplayName = "Special Action",
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.K
            };
            hotkey1.GetExternalModifiers = () => KeyModifierMask.Ctrl;

            var hotkeyNone = new ModHotkeyEntry
            {
                Id = "roundtrip.hk.unbound",
                DisplayName = "Unbound Action",
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.None
            };

            var allHotkeys = new List<ModHotkeyEntry> { hotkey1, hotkeyNone };
            var modGroups = new List<ModOptionGroup> { group };

            // Setup forced and ignored IDs
            ComboKeyStore.SetForcedHotkey("forced.item.1", true);
            ComboKeyStore.SetIgnoredCandidate("ignored.item.1", true);

            // 2. SaveToSlot(0)
            string json = HotkeyProfileManager.SaveToSlot(0, allHotkeys, modGroups, out int hkCount, out int genCount);
            Assert.Equal(2, hkCount);
            Assert.Equal(4, genCount);
            Assert.True(HotkeyProfileManager.HasSlotData(0));

            // ExportToJson
            string exportedJson = HotkeyProfileManager.ExportToJson(0);
            Assert.Equal(json, exportedJson);

            // 3. Mutate in-memory states
            toggleOpt.PendingBool = false;
            sliderOpt.PendingFloat = 10.0f;
            dropdownOpt.PendingIndex = 0;
            inputOpt.PendingString = "mutated";
            hotkey1.PendingKey = KeyCode.L;
            hotkey1.PendingModifiers = KeyModifierMask.Shift;
            hotkey1.HasPendingChange = true;
            hotkeyNone.PendingKey = KeyCode.P;
            hotkeyNone.HasPendingChange = true;

            ComboKeyStore.SetForcedHotkey("forced.item.1", false);
            ComboKeyStore.SetIgnoredCandidate("ignored.item.1", false);
            Assert.False(ComboKeyStore.IsForcedHotkey("forced.item.1"));
            Assert.False(ComboKeyStore.IsIgnoredCandidate("ignored.item.1"));

            // 4. Import / ApplySlot(0)
            bool applySuccess = HotkeyProfileManager.ApplySlot(0, allHotkeys, modGroups);
            Assert.True(applySuccess);

            // 5. Verify lossless restore
            Assert.True(toggleOpt.PendingBool);
            Assert.Equal(42.5f, sliderOpt.PendingFloat);
            Assert.Equal(2, dropdownOpt.PendingIndex);
            Assert.Equal("hello_world", inputOpt.PendingString);

            Assert.Equal(KeyCode.K, hotkey1.PendingKey);
            Assert.Equal(KeyModifierMask.Ctrl, hotkey1.PendingModifiers);

            Assert.Equal(KeyCode.None, hotkeyNone.PendingKey);
            Assert.Equal(KeyModifierMask.None, hotkeyNone.PendingModifiers);

            Assert.True(ComboKeyStore.IsForcedHotkey("forced.item.1"));
            Assert.True(ComboKeyStore.IsIgnoredCandidate("ignored.item.1"));

            // Test ClearSlot
            HotkeyProfileManager.ClearSlot(0);
            Assert.False(HotkeyProfileManager.HasSlotData(0));
        }

        [Fact]
        public void BackwardCompatibility_V147_ProfileImport()
        {
            // Construct a legacy v1.4.7 JSON string matching schema "puh_profile_v1"
            string legacyJson = @"{
                ""schema"": ""puh_profile_v1"",
                ""version"": ""1.4.7"",
                ""name"": ""OldPreset"",
                ""timestamp"": ""11-18 12:00"",
                ""hotkeys"": {
                    ""mod.hk1"": ""Ctrl+E"",
                    ""mod.hk2"": ""None"",
                    ""mod.hk3"": ""Shift+Mouse4""
                },
                ""customHotkeys"": {
                    ""custom.hk1"": ""Alt+F1""
                },
                ""general"": {
                    ""mod.opt1"": ""b:1"",
                    ""mod.opt2"": ""f:75.5"",
                    ""mod.opt3"": ""i:3"",
                    ""mod.opt4"": ""s:custom_text""
                },
                ""forced"": [""legacy.forced.id""],
                ""ignored"": [""legacy.ignored.id""]
            }";

            bool ok = HotkeyProfileManager.TryImportFromJson(legacyJson, out var snapshot, out string error);
            Assert.True(ok, $"Import failed with error: {error}");
            Assert.NotNull(snapshot);
            Assert.Equal("puh_profile_v1", snapshot!.Schema);
            Assert.Equal("1.4.7", snapshot.Version);
            Assert.Equal("OldPreset", snapshot.Name);
            Assert.Equal("11-18 12:00", snapshot.Timestamp);

            // Hotkeys
            Assert.Equal(3, snapshot.Hotkeys.Count);
            Assert.Equal("Ctrl+E", snapshot.Hotkeys["mod.hk1"]);
            Assert.Equal("None", snapshot.Hotkeys["mod.hk2"]);
            Assert.Equal("Shift+Mouse4", snapshot.Hotkeys["mod.hk3"]);

            // Custom Hotkeys
            Assert.Single(snapshot.CustomHotkeys);
            Assert.Equal("Alt+F1", snapshot.CustomHotkeys["custom.hk1"]);

            // General settings
            Assert.Equal(4, snapshot.General.Count);
            Assert.Equal("b:1", snapshot.General["mod.opt1"]);
            Assert.Equal("f:75.5", snapshot.General["mod.opt2"]);
            Assert.Equal("i:3", snapshot.General["mod.opt3"]);
            Assert.Equal("s:custom_text", snapshot.General["mod.opt4"]);

            // Forced and ignored
            Assert.Contains("legacy.forced.id", snapshot.Forced);
            Assert.Contains("legacy.ignored.id", snapshot.Ignored);
        }
    }
}
