using System;
using UnityEngine;

namespace ProbablyUnifiedHotkeys.Tests
{
    public abstract class TestBase : IDisposable
    {
        protected TestBase()
        {
            ResetGlobalState();
        }

        public void Dispose()
        {
            ResetGlobalState();
        }

        protected static void ResetGlobalState()
        {
            ModLogger.SuppressUnityLogForTests = true;
            PrefsStorage.UseInMemoryPrefsForTests = true;
            PrefsStorage.ResetInMemoryPrefsForTests();
            ComboKeyStore.ResetInMemoryPrefsForTests();
            ComboKeyStore.EnsureInitialized();
            BilingualText.ModeOverrideForTest = null;
            BilingualText.LocaleCodeOverrideForTest = null;
            ModFeatureConfig.OverridePinyinSearchForTests = null;
            ModFeatureConfig.OverrideKeySuppressionForTests = null;
            HotkeyProfileManager.ActiveSlotIndex = 0;
            ModEntry.ResetApplicationQuittingForTest();
        }
    }
}
