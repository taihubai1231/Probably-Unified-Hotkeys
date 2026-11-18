using System;
using System.Collections.Generic;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class FoundationAndConfigTests : TestBase
    {
        [Fact]
        public void ModLogger_AndSafeExecution_HandleExceptionsGracefully()
        {
            var logs = new List<string>();
            ModLogger.TestLogSink = msg => logs.Add(msg);

            // 1. ModLogger levels
            ModLogger.Info("Test info message");
            Assert.Contains(logs, l => l.Contains("[ProbablyUnifiedHotkeys][INFO] Test info message"));

            ModLogger.Warn("Test warn message", new InvalidOperationException("warn_cause"));
            Assert.Contains(logs, l => l.Contains("[ProbablyUnifiedHotkeys][WARN] Test warn message") && l.Contains("warn_cause"));

            ModLogger.Error("Test error message", new ArgumentException("err_cause"));
            Assert.Contains(logs, l => l.Contains("[ProbablyUnifiedHotkeys][ERROR] Test error message") && l.Contains("err_cause"));

            // 2. SafeExecution
            bool actionCalled = false;
            bool ok = SafeExecution.SafeExecute(() =>
            {
                actionCalled = true;
                throw new InvalidOperationException("boom");
            }, "TestContext");

            Assert.True(actionCalled);
            Assert.False(ok); // Safely caught without crashing
            Assert.Contains(logs, l => l.Contains("TestContext failed") && l.Contains("boom"));

            // SafeFunc
            int result = SafeExecution.SafeFunc<int>(() => throw new Exception("fail"), 99, "FuncContext");
            Assert.Equal(99, result);
            Assert.Contains(logs, l => l.Contains("FuncContext failed"));

            int successResult = SafeExecution.SafeFunc(() => 42, 0, "SuccessContext");
            Assert.Equal(42, successResult);
        }

        [Fact]
        public void BilingualText_ModesAndLanguageSwitching()
        {
            // SimplifiedChinese mode
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            Assert.True(BilingualText.IsChinese);
            Assert.Equal("你好", BilingualText.Get("你好", "Hello"));
            Assert.Equal("当前数量: 5", BilingualText.Format("当前数量: {0}", "Count: {0}", 5));

            // English mode
            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
            Assert.False(BilingualText.IsChinese);
            Assert.Equal("Hello", BilingualText.Get("你好", "Hello"));
            Assert.Equal("Count: 5", BilingualText.Format("当前数量: {0}", "Count: {0}", 5));

            // Mode parsing
            Assert.Equal(UiLanguageMode.FollowGame, BilingualText.ParseMode("auto"));
            Assert.Equal(UiLanguageMode.SimplifiedChinese, BilingualText.ParseMode("zh"));
            Assert.Equal(UiLanguageMode.English, BilingualText.ParseMode("en"));
            Assert.Equal(UiLanguageMode.FollowGame, BilingualText.ParseMode("invalid_unknown"));

            Assert.Equal("auto", BilingualText.ModeToChoiceValue(UiLanguageMode.FollowGame));
            Assert.Equal("zh", BilingualText.ModeToChoiceValue(UiLanguageMode.SimplifiedChinese));
            Assert.Equal("en", BilingualText.ModeToChoiceValue(UiLanguageMode.English));

            // LanguageChanged event
            bool eventFired = false;
            Action handler = () => eventFired = true;
            BilingualText.LanguageChanged += handler;
            try
            {
                BilingualText.NotifyLanguageChanged();
                Assert.True(eventFired);
            }
            finally
            {
                BilingualText.LanguageChanged -= handler;
            }
        }

        [Fact]
        public void ModFeatureConfig_SwitchesAndTestOverrides()
        {
            // Default states
            Assert.True(ModFeatureConfig.EnableFastQuit);
            Assert.True(ModFeatureConfig.EnablePerformancePatches);
            Assert.True(ModFeatureConfig.EnablePinyinSearch);
            Assert.True(ModFeatureConfig.EnableKeySuppression);
            Assert.False(ModFeatureConfig.EnableDebugLogging);

            // Under DISABLE_DLL_KEY_PROBE build constant, EnableDllProbe is false
            Assert.False(ModFeatureConfig.EnableDllProbe);

            // Test overrides
            ModFeatureConfig.OverridePinyinSearchForTests = false;
            Assert.False(ModFeatureConfig.IsPinyinSearchActive);
            ModFeatureConfig.OverridePinyinSearchForTests = true;
            Assert.True(ModFeatureConfig.IsPinyinSearchActive);

            ModFeatureConfig.OverrideKeySuppressionForTests = false;
            Assert.False(ModFeatureConfig.IsKeySuppressionActive);
            ModFeatureConfig.OverrideKeySuppressionForTests = true;
            Assert.True(ModFeatureConfig.IsKeySuppressionActive);
        }

        [Fact]
        public void TradSimpTable_Exact2473MappingsIntegrity()
        {
            Assert.Equal(2473, TradSimpTable.TradChars.Length);
            Assert.Equal(2473, TradSimpTable.SimpChars.Length);

            // Spot check common characters
            int idx1 = TradSimpTable.TradChars.IndexOf('開');
            Assert.True(idx1 >= 0);
            Assert.Equal('开', TradSimpTable.SimpChars[idx1]);

            int idx2 = TradSimpTable.TradChars.IndexOf('關');
            Assert.True(idx2 >= 0);
            Assert.Equal('关', TradSimpTable.SimpChars[idx2]);

            int idx3 = TradSimpTable.TradChars.IndexOf('鋪');
            Assert.True(idx3 >= 0);
            Assert.Equal('铺', TradSimpTable.SimpChars[idx3]);
        }

        [Fact]
        public void ModGeneralEntry_ApplyPendingAndResetToSnapshot()
        {
            bool currentBool = true;
            float currentFloat = 50f;
            int currentIndex = 1;
            string currentString = "original";

            var entryBool = new ModGeneralEntry
            {
                Id = "gen.bool",
                Kind = OptionKind.Toggle,
                GetBool = () => currentBool,
                SetBool = b => currentBool = b,
                SnapshotBool = true,
                PendingBool = false
            };

            var entryFloat = new ModGeneralEntry
            {
                Id = "gen.float",
                Kind = OptionKind.Slider,
                Min = 0f,
                Max = 100f,
                GetFloat = () => currentFloat,
                SetFloat = f => currentFloat = f,
                SnapshotFloat = 50f,
                PendingFloat = 80f
            };

            var entryDropdown = new ModGeneralEntry
            {
                Id = "gen.dropdown",
                Kind = OptionKind.Dropdown,
                GetIndex = () => currentIndex,
                SetIndex = i => currentIndex = i,
                SnapshotIndex = 1,
                PendingIndex = 3
            };

            var entryInput = new ModGeneralEntry
            {
                Id = "gen.input",
                Kind = OptionKind.Input,
                GetString = () => currentString,
                SetString = s => currentString = s,
                SnapshotString = "original",
                PendingString = "modified"
            };

            // Apply pending
            entryBool.ApplyPending();
            Assert.False(currentBool);

            entryFloat.ApplyPending();
            Assert.Equal(80f, currentFloat);

            entryDropdown.ApplyPending();
            Assert.Equal(3, currentIndex);

            entryInput.ApplyPending();
            Assert.Equal("modified", currentString);

            // Mutate pending to something else
            entryBool.PendingBool = true;
            entryFloat.PendingFloat = 10f;
            entryDropdown.PendingIndex = 0;
            entryInput.PendingString = "trash";

            // Reset to snapshot
            entryBool.ResetToSnapshot();
            entryFloat.ResetToSnapshot();
            entryDropdown.ResetToSnapshot();
            entryInput.ResetToSnapshot();

            // Underlying values restored to snapshot and pending cleared
            Assert.True(currentBool);
            Assert.Null(entryBool.PendingBool);

            Assert.Equal(50f, currentFloat);
            Assert.Null(entryFloat.PendingFloat);

            Assert.Equal(1, currentIndex);
            Assert.Null(entryDropdown.PendingIndex);

            Assert.Equal("original", currentString);
            Assert.Null(entryInput.PendingString);
        }
    }
}
