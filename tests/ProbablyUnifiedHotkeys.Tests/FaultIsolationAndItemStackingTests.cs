#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class FaultIsolationAndItemStackingTests : TestBase
    {
        private static readonly FieldInfo? ModLoaderModsField =
            typeof(ModLoader).GetField("mods", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo? ModConfigConfigsField =
            typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        private List<LoadedMod>? _savedMods;
        private Dictionary<string, ModConfig>? _savedConfigs;

        public FaultIsolationAndItemStackingTests()
        {
            BackupEnvironment();
        }

        private void BackupEnvironment()
        {
            if (ModLoaderModsField != null)
            {
                var curMods = ModLoaderModsField.GetValue(null) as List<LoadedMod>;
                _savedMods = curMods != null ? new List<LoadedMod>(curMods) : null;
            }

            if (ModConfigConfigsField != null)
            {
                var curConfigs = ModConfigConfigsField.GetValue(null) as Dictionary<string, ModConfig>;
                _savedConfigs = curConfigs != null ? new Dictionary<string, ModConfig>(curConfigs) : null;
            }
        }

        private void RestoreEnvironment()
        {
            if (ModLoaderModsField != null)
            {
                var curMods = ModLoaderModsField.GetValue(null) as List<LoadedMod>;
                if (curMods != null)
                {
                    curMods.Clear();
                    if (_savedMods != null)
                    {
                        curMods.AddRange(_savedMods);
                    }
                }
            }

            if (ModConfigConfigsField != null)
            {
                var curConfigs = ModConfigConfigsField.GetValue(null) as Dictionary<string, ModConfig>;
                if (curConfigs != null)
                {
                    curConfigs.Clear();
                    if (_savedConfigs != null)
                    {
                        foreach (var kvp in _savedConfigs)
                        {
                            curConfigs[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }

            ModCatalogCache.Invalidate();
        }

        private static LoadedMod CreateMockLoadedMod(string id, string name, Dictionary<string, Dictionary<string, string>>? localization = null)
        {
            return new LoadedMod
            {
                Manifest = new ModManifest
                {
                    ID = id,
                    Name = name,
                    Author = "TestAuthor",
                    ModVersion = "1.0.0",
                    Description = "Mock mod for testing"
                },
                IsEnabled = true,
                Localization = localization ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static ModConfig CreateMockModConfig(string modId)
        {
            var ctor = typeof(ModConfig).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);

            if (ctor == null)
            {
                throw new InvalidOperationException("Could not find ModConfig(string modId) constructor.");
            }

            return (ModConfig)ctor.Invoke(new object[] { modId });
        }

        private static void SetMockModsAndConfigs(List<LoadedMod> mods, Dictionary<string, ModConfig> configs)
        {
            if (ModLoaderModsField != null)
            {
                var curMods = ModLoaderModsField.GetValue(null) as List<LoadedMod>;
                if (curMods != null)
                {
                    curMods.Clear();
                    curMods.AddRange(mods);
                }
            }

            if (ModConfigConfigsField != null)
            {
                var curConfigs = ModConfigConfigsField.GetValue(null) as Dictionary<string, ModConfig>;
                if (curConfigs != null)
                {
                    curConfigs.Clear();
                    foreach (var kvp in configs)
                    {
                        curConfigs[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        [Fact]
        public void ItemStacking_NreThrowingDelegates_ScanSucceeds_And_FlagsDegraded()
        {
            try
            {
                ModCatalogCache.Invalidate();
                Assert.False(ModCatalogCache.HasDegradedEntries, "Initial HasDegradedEntries must be false.");

                var itemStackingMod = CreateMockLoadedMod("item_stacking", "物品堆叠增强");
                var mods = new List<LoadedMod> { itemStackingMod };

                var config = CreateMockModConfig("item_stacking");
                var entry = config.Define(
                    "max_stack",
                    999,
                    "section_Basic",
                    () => throw new NullReferenceException("ItemStacking.Loc not ready in main menu"),
                    1,
                    9999,
                    () => throw new NullReferenceException("ItemStacking.Loc tooltip not ready in main menu"),
                    false
                );

                var configs = new Dictionary<string, ModConfig>
                {
                    ["item_stacking"] = config
                };

                SetMockModsAndConfigs(mods, configs);

                // Execute ScanAll - must not throw unhandled exception
                var groups = ModOptionScanner.ScanAll(out var allHotkeys, out var report);

                Assert.NotNull(groups);
                Assert.True(ModCatalogCache.HasDegradedEntries, "HasDegradedEntries must be set to true when delegate throws NRE.");

                var stackGroup = groups.Find(g => g.ModId == "item_stacking");
                Assert.NotNull(stackGroup);
                Assert.NotEmpty(stackGroup!.GeneralSettings);

                var gen = stackGroup.GeneralSettings.Find(s => s.Id.EndsWith("max_stack"));
                Assert.NotNull(gen);
                // When label threw and no mod localization was provided, fallback to entryKey
                Assert.Equal("max_stack", gen!.DisplayName);
            }
            finally
            {
                RestoreEnvironment();
            }
        }

        [Fact]
        public void ItemStacking_LocalizationFallback_ResolvesBilingualLabelsAndTooltips()
        {
            try
            {
                ModCatalogCache.Invalidate();

                var loc = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["zh-CN"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["section_Basic"] = "基础设置",
                        ["cfg_max_stack"] = "最大堆叠数",
                        ["cfg_max_stack.tip"] = "自定义全局物品堆叠数量上限"
                    },
                    ["en-US"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["section_Basic"] = "Basic Settings",
                        ["cfg_max_stack"] = "Max Stack Size",
                        ["cfg_max_stack.tip"] = "Custom global item stack limit"
                    }
                };

                var itemStackingMod = CreateMockLoadedMod("item_stacking", "物品堆叠增强", loc);
                var mods = new List<LoadedMod> { itemStackingMod };

                var config = CreateMockModConfig("item_stacking");
                config.Define(
                    "max_stack",
                    999,
                    "section_Basic",
                    () => throw new NullReferenceException("Simulated Loc.Lookup NRE"),
                    1,
                    9999,
                    () => throw new NullReferenceException("Simulated Loc.Lookup tip NRE"),
                    false
                );

                var configs = new Dictionary<string, ModConfig>
                {
                    ["item_stacking"] = config
                };

                SetMockModsAndConfigs(mods, configs);

                // --- 1. Chinese Mode ---
                BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
                Assert.True(BilingualText.IsChinese);

                // Test SectionLocalizer direct helpers
                Assert.Equal("基础设置", SectionLocalizer.FormatModSectionLabel("Basic", itemStackingMod));
                Assert.Equal("基础设置", SectionLocalizer.FormatModSectionLabel("section_Basic", itemStackingMod));

                bool resolvedZhLabel = SectionLocalizer.TryResolveConfigLabelFromModLocalization(itemStackingMod, "max_stack", out var zhLabel);
                Assert.True(resolvedZhLabel);
                Assert.Equal("最大堆叠数", zhLabel);

                bool resolvedZhTip = SectionLocalizer.TryResolveConfigTooltipFromModLocalization(itemStackingMod, "max_stack", out var zhTip);
                Assert.True(resolvedZhTip);
                Assert.Equal("自定义全局物品堆叠数量上限", zhTip);

                // Scan in Chinese mode
                var zhGroups = ModOptionScanner.ScanAll(out _, out _);
                var zhStackGroup = zhGroups.Find(g => g.ModId == "item_stacking");
                Assert.NotNull(zhStackGroup);
                var zhGen = zhStackGroup!.GeneralSettings.Find(s => s.Id.EndsWith("max_stack"));
                Assert.NotNull(zhGen);
                Assert.Equal("最大堆叠数", zhGen!.DisplayName);
                Assert.Equal("自定义全局物品堆叠数量上限", zhGen.Tooltip);

                // --- 2. English Mode ---
                BilingualText.ModeOverrideForTest = UiLanguageMode.English;
                Assert.False(BilingualText.IsChinese);

                Assert.Equal("Basic Settings", SectionLocalizer.FormatModSectionLabel("Basic", itemStackingMod));
                Assert.Equal("Basic Settings", SectionLocalizer.FormatModSectionLabel("section_Basic", itemStackingMod));

                bool resolvedEnLabel = SectionLocalizer.TryResolveConfigLabelFromModLocalization(itemStackingMod, "max_stack", out var enLabel);
                Assert.True(resolvedEnLabel);
                Assert.Equal("Max Stack Size", enLabel);

                bool resolvedEnTip = SectionLocalizer.TryResolveConfigTooltipFromModLocalization(itemStackingMod, "max_stack", out var enTip);
                Assert.True(resolvedEnTip);
                Assert.Equal("Custom global item stack limit", enTip);

                // Scan in English mode
                ModCatalogCache.Invalidate();
                var enGroups = ModOptionScanner.ScanAll(out _, out _);
                var enStackGroup = enGroups.Find(g => g.ModId == "item_stacking");
                Assert.NotNull(enStackGroup);
                var enGen = enStackGroup!.GeneralSettings.Find(s => s.Id.EndsWith("max_stack"));
                Assert.NotNull(enGen);
                Assert.Equal("Max Stack Size", enGen!.DisplayName);
                Assert.Equal("Custom global item stack limit", enGen.Tooltip);
            }
            finally
            {
                RestoreEnvironment();
            }
        }

        [Fact]
        public void MultiMod_MixedScenario_FaultIsolation_NormalModsAndVanillaIntact()
        {
            try
            {
                ModCatalogCache.Invalidate();

                // Mod 1: Corrupted mod (throwing label & tooltip)
                var corruptMod = CreateMockLoadedMod("mod_corrupt", "Corrupted Mod");
                var corruptConfig = CreateMockModConfig("mod_corrupt");
                corruptConfig.Define("bad_key", true, "General", () => throw new InvalidOperationException("Corrupt label"), () => "Tooltip", false);

                // Mod 2: Normal healthy mod
                var normalMod = CreateMockLoadedMod("mod_normal", "Normal Mod");
                var normalConfig = CreateMockModConfig("mod_normal");
                normalConfig.Define("good_key", 42, "Gameplay", () => "Good Setting", 0, 100, () => "Good Tooltip", false);

                var mods = new List<LoadedMod> { corruptMod, normalMod };
                var configs = new Dictionary<string, ModConfig>
                {
                    ["mod_corrupt"] = corruptConfig,
                    ["mod_normal"] = normalConfig
                };

                SetMockModsAndConfigs(mods, configs);

                // ScanAll should isolate corrupt mod and process normal mod completely
                var groups = ModOptionScanner.ScanAll(out var allHotkeys, out var scanReport);

                Assert.NotNull(groups);
                var normalGroup = groups.Find(g => g.ModId == "mod_normal");
                Assert.NotNull(normalGroup);
                Assert.NotEmpty(normalGroup!.GeneralSettings);
                var normalGen = normalGroup.GeneralSettings.Find(s => s.Id.EndsWith("good_key"));
                Assert.NotNull(normalGen);
                Assert.Equal("Good Setting", normalGen!.DisplayName);

                var corruptGroup = groups.Find(g => g.ModId == "mod_corrupt");
                Assert.NotNull(corruptGroup);

                // Verify UiSessionState: abnormal getters must NOT cause false dirty-state (HasUnsavedChanges)
                UiSessionState.ModGroups = groups;
                UiSessionState.AllHotkeys = allHotkeys;

                // Create a simulated abnormal entry whose live getter throws
                var abnormalEntry = new ModGeneralEntry
                {
                    Id = "abnormal_mod.broken_getter",
                    ModId = "abnormal_mod",
                    DisplayName = "Broken Getter",
                    Kind = OptionKind.Slider,
                    Min = 0f,
                    Max = 100f,
                    GetFloat = () => throw new InvalidOperationException("Live float getter failed")
                };
                corruptGroup.GeneralSettings.Add(abnormalEntry);

                // Without user interaction, HasUnsavedChanges must return false
                bool dirty = UiSessionState.HasUnsavedChanges();
                Assert.False(dirty, "UiSessionState.HasUnsavedChanges() must return false when live getters throw exceptions.");
            }
            finally
            {
                RestoreEnvironment();
            }
        }

        [Fact]
        public void ModCatalogCache_Invalidate_ResetsHasDegradedEntries()
        {
            ModCatalogCache.Invalidate();
            Assert.False(ModCatalogCache.HasDegradedEntries);

            ModCatalogCache.HasDegradedEntries = true;
            Assert.True(ModCatalogCache.HasDegradedEntries);

            ModCatalogCache.Invalidate();
            Assert.False(ModCatalogCache.HasDegradedEntries, "Invalidate() must reset HasDegradedEntries to false.");
        }
    }
}
