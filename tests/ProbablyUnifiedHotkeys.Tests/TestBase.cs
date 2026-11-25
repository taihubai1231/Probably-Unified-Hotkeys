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
            ModFeatureConfig.ResetAllTestOverrides();
            UiSessionState.CurrentCardSortMode = ModCardSortMode.PinyinAsc;
            UiSessionState.BaselineCardSortMode = null;
            UiSessionState.BaselineVisualModIdOrder = null;
            UiSessionState.IsSortModeExplicitlyChosenByUser = false;
            UiSessionState.CardSortDropdown = null;
            UiSessionState.ModGroups.Clear();
            HotkeyProfileManager.ActiveSlotIndex = 0;
            ModEntry.ResetApplicationQuittingForTest();
            KnownModAdapters.ResetBootSyncForTest();
            UiSessionState.FrameCountProviderForTest = () => 100;
        }
    }
}
