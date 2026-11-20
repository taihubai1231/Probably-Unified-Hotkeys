#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    /// <summary>
    /// 《15 项实机反馈专项防退化自动化单测套件》
    /// 严格焊死老板实机反馈的 #1、#2、#6、#7、#10、#12、#13、#14 等关键边界。
    /// </summary>
    [Collection("SequentialStateTests")]
    public class BugfixRegression15SuiteTests : TestBase
    {
        private sealed class ReadOnlyKeybindMock
        {
            public static KeyCode Key => KeyCode.F7;
        }

        /// <summary>
        /// 针对 #1：验证开窗基准快照机制下，未发生用户修改时（即使存在外部下拉框状态或 KeyCode.None 条目）
        /// UiSessionState.HasUnsavedChanges() 恒返 false；一旦发生实际修改则返回 true，放弃修改后恢复 false。
        /// </summary>
        [Fact]
        public void Feedback01_BaselineSnapshot_NoUserChanges_HasUnsavedChanges_AlwaysReturnsFalse()
        {
            UiSessionState.AllHotkeys.Clear();
            UiSessionState.ModGroups.Clear();
            UiSessionState.VanillaCards.Clear();
            UiSessionState.VanillaOpenToggleBaseline.Clear();
            UiSessionState.VanillaOpenSliderBaseline.Clear();
            UiSessionState.VanillaOpenDropdownBaseline.Clear();

            // 1. 模拟未绑定的 None 快捷键与已绑定正常快捷键
            var unboundEntry = new ModHotkeyEntry
            {
                Id = "test.mod.unbound_action",
                ModId = "test_mod",
                DefaultKey = KeyCode.None,
                HasExplicitDefault = true,
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.None
            };
            var boundEntry = new ModHotkeyEntry
            {
                Id = "test.mod.bound_action",
                ModId = "test_mod",
                DefaultKey = KeyCode.F1,
                HasExplicitDefault = true,
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.F1
            };

            UiSessionState.AllHotkeys.Add(unboundEntry);
            UiSessionState.AllHotkeys.Add(boundEntry);

            // 2. 模拟常规设置项（布尔、浮点、字符串）
            var modGroup = new ModOptionGroup
            {
                ModId = "test_mod",
                DisplayName = "Test Mod"
            };
            var genBool = new ModGeneralEntry
            {
                Id = "test.mod.bool_opt",
                Kind = OptionKind.Toggle,
                GetBool = () => true
            };
            var genFloat = new ModGeneralEntry
            {
                Id = "test.mod.float_opt",
                Kind = OptionKind.Slider,
                GetFloat = () => 0.75f,
                Min = 0f,
                Max = 1f
            };
            modGroup.GeneralSettings.Add(genBool);
            modGroup.GeneralSettings.Add(genFloat);
            UiSessionState.ModGroups.Add(modGroup);

            // 初始未发生任何修改：HasUnsavedChanges 必须恒返回 false
            Assert.False(UiSessionState.HasUnsavedChanges());

            // 即使调用了快照刷新，依然为 false
            UiSessionState.CaptureOpenBaselineSnapshot();
            Assert.False(UiSessionState.HasUnsavedChanges());

            // 3. 用户修改了快捷键
            boundEntry.PendingKey = KeyCode.F2;
            boundEntry.PendingModifiers = KeyModifierMask.None;
            boundEntry.HasPendingChange = true;
            Assert.True(UiSessionState.HasUnsavedChanges());

            // 放弃修改后，重置回原值，必须再次变回 false
            UiSessionState.DiscardAllPendingChanges();
            Assert.False(UiSessionState.HasUnsavedChanges());

            // 4. 用户将原本为 None 的键再次置为 None（无实质改动），必须依然为 false
            unboundEntry.PendingKey = KeyCode.None;
            unboundEntry.PendingModifiers = KeyModifierMask.None;
            unboundEntry.HasPendingChange = true;
            Assert.False(UiSessionState.HasUnsavedChanges());

            // 5. 用户修改了常规布尔项
            genBool.PendingBool = false;
            Assert.True(UiSessionState.HasUnsavedChanges());

            UiSessionState.DiscardAllPendingChanges();
            Assert.False(UiSessionState.HasUnsavedChanges());
        }

        /// <summary>
        /// 针对 #2：验证显式解绑为 KeyCode.None 保存后，即使底层模拟模组重启返回默认键（如 F5/C），
        /// 显式解绑保护机制依然强制拦截保持 KeyCode.None；而重新绑定为新键后正常释放。
        /// </summary>
        [Fact]
        public void Feedback02_ExplicitUnbound_SurvivesUnderlyingModReset_RebindReleasesProtection()
        {
            const string modKeyId = "smart_inventory.auto_sort_key";
            const KeyCode underlyingDefaultKey = KeyCode.F5;

            // 1. 用户显式解绑为 None
            ComboKeyStore.SetSavedCustomHotkey(modKeyId, KeyModifierMask.None, KeyCode.None, KeyModifierMask.None, underlyingDefaultKey);
            Assert.True(ComboKeyStore.IsExplicitlyUnbound(modKeyId));

            // 2. 模拟底层模组在重启后，依然从其自身内存或默认配置返回 F5
            var effective = ComboKeyStore.GetEffectiveCustomHotkey(modKeyId, KeyModifierMask.None, underlyingDefaultKey);
            Assert.Equal(KeyCode.None, effective.Key);
            Assert.Equal(KeyModifierMask.None, effective.Modifiers);

            // 验证运行时 FilterRuntimeKey 拦截：原本为 F5 的键被拦截为 None
            KeyCode runtimeKey = underlyingDefaultKey;
            ComboKeyStore.FilterRuntimeKey(ref runtimeKey, modKeyId);
            Assert.Equal(KeyCode.None, runtimeKey);

            // 3. 验证跨会话持久化与重载
            ComboKeyStore.Save();
            ComboKeyStore.Load();
            Assert.True(ComboKeyStore.IsExplicitlyUnbound(modKeyId));
            var afterReload = ComboKeyStore.GetEffectiveCustomHotkey(modKeyId, KeyModifierMask.None, underlyingDefaultKey);
            Assert.Equal(KeyCode.None, afterReload.Key);

            // 4. 玩家重新绑定为新键位（如 KeyCode.G）
            ComboKeyStore.SetSavedCustomHotkey(modKeyId, KeyModifierMask.None, KeyCode.G, KeyModifierMask.None, underlyingDefaultKey);

            // 显式解绑标记自动解除，保护机制正常释放，返回新绑定的 G
            Assert.False(ComboKeyStore.IsExplicitlyUnbound(modKeyId));
            var newlyBound = ComboKeyStore.GetEffectiveCustomHotkey(modKeyId, KeyModifierMask.None, underlyingDefaultKey);
            Assert.Equal(KeyCode.G, newlyBound.Key);

            KeyCode runtimeNewKey = KeyCode.G;
            ComboKeyStore.FilterRuntimeKey(ref runtimeNewKey, modKeyId);
            Assert.Equal(KeyCode.G, runtimeNewKey);
        }

        /// <summary>
        /// 针对 #6 & #7：验证组合键（Ctrl + C、Shift + F、Alt + Space）保存至 ComboKeyStore 后，
        /// 格式化输出严格为 Ctrl + C 等组合文本而非单键，且在按下 Ctrl + C 时同主键单键 C 被精确抑制、
        /// 单按 C 时不触发 Ctrl + C；同时验证 ConflictScanner 只读属性适配器调用 SetCombo / SetKey
        /// 零异常（彻底锁死 Set Method not found for 'Key' 崩溃！）。
        /// </summary>
        [Fact]
        public void Feedback06_07_ComboKeys_Formatting_AndSuppression_AndConflictScannerReadOnlySafety()
        {
            // Part A: 格式化输出严格性验证
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            Assert.Equal("Ctrl + C", ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl, KeyCode.C));
            Assert.Equal("Shift + F", ComboKeyStore.FormatCombo(KeyModifierMask.Shift, KeyCode.F));
            Assert.Equal("Alt + Space", ComboKeyStore.FormatCombo(KeyModifierMask.Alt, KeyCode.Space));

            var comboCtrlC = new KeyCombo(KeyModifierMask.Ctrl, KeyCode.C);
            Assert.Equal("Ctrl + C", comboCtrlC.ToString());

            // Part B: 单键抑制与精准激活验证
            const string comboEntryId = "vanilla.call_next_customer";
            const string plainEntryId = "vanilla.some_plain_c";

            var comboEntry = new ModHotkeyEntry
            {
                Id = comboEntryId,
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.C,
                DefaultModifiers = KeyModifierMask.Ctrl,
                GetKey = () => KeyCode.C
            };
            var plainEntry = new ModHotkeyEntry
            {
                Id = plainEntryId,
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.C,
                DefaultModifiers = KeyModifierMask.None,
                GetKey = () => KeyCode.C
            };

            ComboKeyStore.SetSavedCustomHotkey(comboEntryId, KeyModifierMask.Ctrl, KeyCode.C);
            ComboKeyStore.SetSavedCustomHotkey(plainEntryId, KeyModifierMask.None, KeyCode.C);
            ComboKeyStore.RebuildRuntimeLookup(new[] { comboEntry, plainEntry });

            ModFeatureConfig.OverrideKeySuppressionForTests = true;

            // 情景 1：物理按下了 Ctrl + C
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.C;
            ComboKeyStore.HeldModifiersOverride = k => KeyModifierMask.Ctrl;

            // 同主键单键 C 必须被判定存在冲突并被抑制
            Assert.True(ComboKeyStore.HasActiveComboConflictForPlainKey(KeyCode.C));

            KeyCode plainFilterKey = KeyCode.C;
            ComboKeyStore.FilterRuntimeKey(ref plainFilterKey, plainEntryId);
            Assert.Equal(KeyCode.None, plainFilterKey); // 单键 C 被精确抑制！

            KeyCode comboFilterKey = KeyCode.C;
            ComboKeyStore.FilterRuntimeKey(ref comboFilterKey, comboEntryId);
            Assert.Equal(KeyCode.C, comboFilterKey); // 组合键 Ctrl + C 正常放行！

            // 情景 2：物理仅单按 C（未按 Ctrl）
            ComboKeyStore.HeldModifiersOverride = k => KeyModifierMask.None;

            Assert.False(ComboKeyStore.HasActiveComboConflictForPlainKey(KeyCode.C));

            plainFilterKey = KeyCode.C;
            ComboKeyStore.FilterRuntimeKey(ref plainFilterKey, plainEntryId);
            Assert.Equal(KeyCode.C, plainFilterKey); // 单按 C 正常放行！

            comboFilterKey = KeyCode.C;
            ComboKeyStore.FilterRuntimeKey(ref comboFilterKey, comboEntryId);
            Assert.Equal(KeyCode.None, comboFilterKey); // 未按修饰键时不触发 Ctrl + C！

            // Part C: ConflictScanner 只读属性适配器安全写入验证（彻底锁死 Set Method not found for 'Key' 异常）
            var groups = new List<ModOptionGroup>();
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var keyPropField = typeof(KnownModAdapters).GetField("_csKeyProp", BindingFlags.NonPublic | BindingFlags.Static);
            var setPropFnField = typeof(KnownModAdapters).GetField("_csSetKeyPropFn", BindingFlags.NonPublic | BindingFlags.Static);
            var keyFieldField = typeof(KnownModAdapters).GetField("_csKeyField", BindingFlags.NonPublic | BindingFlags.Static);
            var loadedFieldField = typeof(KnownModAdapters).GetField("_csLoadedField", BindingFlags.NonPublic | BindingFlags.Static);
            var saveFnField = typeof(KnownModAdapters).GetField("_csSaveMethodFn", BindingFlags.NonPublic | BindingFlags.Static);

            // 注入只读属性（模拟 ConflictScanner.Keybind.Key 无 setter 场景）
            PropertyInfo readOnlyProp = typeof(ReadOnlyKeybindMock).GetProperty(nameof(ReadOnlyKeybindMock.Key))!;
            Assert.False(readOnlyProp.CanWrite);

            keyPropField?.SetValue(null, readOnlyProp);
            setPropFnField?.SetValue(null, null);
            keyFieldField?.SetValue(null, null);
            loadedFieldField?.SetValue(null, null);
            saveFnField?.SetValue(null, null);

            KnownModAdapters.TryAdaptKnownMods(groups, handled);

            ModOptionGroup? csGroup = groups.Find(g => g.ModId == "conflict_scanner");
            Assert.NotNull(csGroup);
            ModHotkeyEntry? csEntry = csGroup!.Hotkeys.Find(h => h.Id == "conflict_scanner.ReportKey");
            Assert.NotNull(csEntry);

            // 调用 SetKey 与 SetCombo：必须 0 异常抛出，且持久化正常生效
            var exSetKey = Record.Exception(() => csEntry!.SetKey?.Invoke(KeyCode.F8));
            Assert.Null(exSetKey);

            var exSetCombo = Record.Exception(() => csEntry!.SetCombo?.Invoke(KeyModifierMask.Ctrl, KeyCode.F8));
            Assert.Null(exSetCombo);

            var effectiveCs = ComboKeyStore.GetEffectiveCustomHotkey("conflict_scanner.ReportKey", KeyModifierMask.None, KeyCode.F7);
            Assert.Equal(KeyCode.F8, effectiveCs.Key);
            Assert.Equal(KeyModifierMask.Ctrl, effectiveCs.Modifiers);
        }

        /// <summary>
        /// 针对 #10（替老板自动化验真模组排查强转与忽略功能！）：
        /// 验证 ComboKeyStore.SetForcedHotkey("some_mod.custom_str_key", true) 与
        /// SetIgnoredCandidate("some_mod.not_a_key", true) 的持久化、互斥清理、
        /// GetForcedHotkeysRevision() 递增（驱动 ModCatalogCache 自动重扫）及取消恢复全链路。
        /// </summary>
        [Fact]
        public void Feedback10_ModDiagnostic_ForcedHotkey_And_IgnoredCandidate_FullLifecycle()
        {
            const string strKeyCandidate = "some_mod.custom_str_key";
            const string notKeyCandidate = "some_mod.not_a_key";

            int initialRevision = ComboKeyStore.GetForcedHotkeysRevision();

            // 1. 强转为热键
            ComboKeyStore.SetForcedHotkey(strKeyCandidate, true);
            Assert.True(ComboKeyStore.IsForcedHotkey(strKeyCandidate));
            Assert.False(ComboKeyStore.IsIgnoredCandidate(strKeyCandidate));
            int revAfterForce = ComboKeyStore.GetForcedHotkeysRevision();
            Assert.True(revAfterForce > initialRevision);

            // 2. 互斥性测试：将同一个项标记为忽略，必须自动从强转表移除
            ComboKeyStore.SetIgnoredCandidate(strKeyCandidate, true);
            Assert.False(ComboKeyStore.IsForcedHotkey(strKeyCandidate));
            Assert.True(ComboKeyStore.IsIgnoredCandidate(strKeyCandidate));
            int revAfterIgnore = ComboKeyStore.GetForcedHotkeysRevision();
            Assert.True(revAfterIgnore > revAfterForce);

            // 再次将其标记为强转，必须自动从忽略表移除
            ComboKeyStore.SetForcedHotkey(strKeyCandidate, true);
            Assert.True(ComboKeyStore.IsForcedHotkey(strKeyCandidate));
            Assert.False(ComboKeyStore.IsIgnoredCandidate(strKeyCandidate));

            // 3. 将另一个项设为忽略
            ComboKeyStore.SetIgnoredCandidate(notKeyCandidate, true);
            Assert.True(ComboKeyStore.IsIgnoredCandidate(notKeyCandidate));
            Assert.False(ComboKeyStore.IsForcedHotkey(notKeyCandidate));

            // 4. 持久化存储与加载测试
            ComboKeyStore.Load();
            Assert.True(ComboKeyStore.IsForcedHotkey(strKeyCandidate));
            Assert.True(ComboKeyStore.IsIgnoredCandidate(notKeyCandidate));

            var forcedSnapshot = ComboKeyStore.GetForcedHotkeyIdsSnapshot();
            Assert.Contains(strKeyCandidate, forcedSnapshot);
            var ignoredSnapshot = ComboKeyStore.GetIgnoredCandidateIdsSnapshot();
            Assert.Contains(notKeyCandidate, ignoredSnapshot);

            // 5. 取消恢复全链路
            ComboKeyStore.SetForcedHotkey(strKeyCandidate, false);
            ComboKeyStore.SetIgnoredCandidate(notKeyCandidate, false);
            Assert.False(ComboKeyStore.IsForcedHotkey(strKeyCandidate));
            Assert.False(ComboKeyStore.IsIgnoredCandidate(notKeyCandidate));
            Assert.True(ComboKeyStore.GetForcedHotkeysRevision() > revAfterIgnore);
        }

        /// <summary>
        /// 针对 #12, #13, #14（替老板自动化验真预设槽位与非法剪贴板保护！）：
        /// 验证方案槽位 SaveToSlot -> 修改键位与设置 -> TryLoadSlotSnapshot + ApplySnapshot
        /// 100% 恢复修改前键位与常规设置、ClearSlot 彻底清空槽位，
        /// 以及向 TryImportFromJson 传入空串、乱码文本、"{bad json"、错误 schema JSON 时
        /// 100% 安全返回 false 并给出错误提示、零异常零状态污染！
        /// </summary>
        [Fact]
        public void Feedback12_13_14_ProfilePresets_SaveLoadClear_AndIllegalClipboardDefense()
        {
            // Part A: 预设槽位保存、恢复与清空闭环验证
            const int slot = 0;
            const string hkId1 = "profile_test.action_fire";
            const string hkId2 = "profile_test.action_jump";

            var hk1 = new ModHotkeyEntry
            {
                Id = hkId1,
                ModId = "profile_test",
                DefaultKey = KeyCode.F1,
                DefaultModifiers = KeyModifierMask.None,
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.F1
            };
            var hk2 = new ModHotkeyEntry
            {
                Id = hkId2,
                ModId = "profile_test",
                DefaultKey = KeyCode.Space,
                DefaultModifiers = KeyModifierMask.None,
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.Space
            };
            var hotkeys = new List<ModHotkeyEntry> { hk1, hk2 };

            var group = new ModOptionGroup
            {
                ModId = "profile_test",
                DisplayName = "Profile Test Mod"
            };
            bool boolVal = true;
            float floatVal = 0.85f;
            var genBool = new ModGeneralEntry
            {
                Id = "profile_test.enable_fx",
                Kind = OptionKind.Toggle,
                GetBool = () => boolVal,
                SetBool = b => boolVal = b
            };
            var genFloat = new ModGeneralEntry
            {
                Id = "profile_test.fx_volume",
                Kind = OptionKind.Slider,
                GetFloat = () => floatVal,
                SetFloat = f => floatVal = f,
                Min = 0f,
                Max = 1f
            };
            group.GeneralSettings.Add(genBool);
            group.GeneralSettings.Add(genFloat);
            var groups = new List<ModOptionGroup> { group };

            // 1. 设置特定键位并保存至槽位 0
            ComboKeyStore.SetSavedCustomHotkey(hkId1, KeyModifierMask.Ctrl, KeyCode.F1);
            ComboKeyStore.SetSavedCustomHotkey(hkId2, KeyModifierMask.Shift, KeyCode.Space);

            string savedJson = HotkeyProfileManager.SaveToSlot(slot, hotkeys, groups, out int hkCount, out int genCount);
            Assert.False(string.IsNullOrWhiteSpace(savedJson));
            Assert.Equal(2, hkCount);
            Assert.Equal(2, genCount);

            // 2. 模拟玩家大幅篡改键位与常规设置
            ComboKeyStore.SetSavedCustomHotkey(hkId1, KeyModifierMask.None, KeyCode.Q);
            ComboKeyStore.SetSavedCustomHotkey(hkId2, KeyModifierMask.None, KeyCode.None);
            boolVal = false;
            floatVal = 0.1f;

            Assert.Equal(KeyCode.Q, ComboKeyStore.GetEffectiveCustomHotkey(hkId1, KeyModifierMask.None, KeyCode.F1).Key);
            Assert.Equal(KeyCode.None, ComboKeyStore.GetEffectiveCustomHotkey(hkId2, KeyModifierMask.None, KeyCode.Space).Key);
            Assert.False(boolVal);
            Assert.Equal(0.1f, floatVal);

            // 3. 从槽位 0 读取快照并应用恢复
            bool loadSuccess = HotkeyProfileManager.TryLoadSlotSnapshot(slot, out var loadedSnapshot, out string loadError);
            Assert.True(loadSuccess, $"Failed to load slot: {loadError}");
            Assert.NotNull(loadedSnapshot);

            bool applySuccess = HotkeyProfileManager.ApplySnapshot(loadedSnapshot!, hotkeys, groups, applyToUnderlying: true);
            Assert.True(applySuccess);

            // 验证 100% 恢复修改前状态
            var restoredHk1 = ComboKeyStore.GetEffectiveCustomHotkey(hkId1, KeyModifierMask.None, KeyCode.F1);
            Assert.Equal(KeyCode.F1, restoredHk1.Key);
            Assert.Equal(KeyModifierMask.Ctrl, restoredHk1.Modifiers);

            var restoredHk2 = ComboKeyStore.GetEffectiveCustomHotkey(hkId2, KeyModifierMask.None, KeyCode.Space);
            Assert.Equal(KeyCode.Space, restoredHk2.Key);
            Assert.Equal(KeyModifierMask.Shift, restoredHk2.Modifiers);

            Assert.True(boolVal);
            Assert.Equal(0.85f, floatVal);

            // 4. 清空槽位验证
            HotkeyProfileManager.ClearSlot(slot);
            bool loadAfterClear = HotkeyProfileManager.TryLoadSlotSnapshot(slot, out var clearedSnapshot, out string clearError);
            Assert.False(loadAfterClear);
            Assert.Null(clearedSnapshot);
            Assert.False(string.IsNullOrWhiteSpace(clearError));

            // Part B: 针对 #14 非法剪贴板与畸变文本防御验证
            string?[] badInputs = new string?[]
            {
                null,
                string.Empty,
                "     ",
                "不是有效的JSON文本！~!@#$%^&*()_+\t\r\n",
                "{bad json format: 123",
                "{\"schema_version\": 99999, \"profile_name\": \"future_schema\"}",
                "{\"unrelated_object\": [1, 2, 3]}",
                "\"just a raw string\"",
                "[1, 2, 3, 4]"
            };

            foreach (var bad in badInputs)
            {
                var ex = Record.Exception(() =>
                {
                    bool ok = HotkeyProfileManager.TryImportFromJson(bad, out var badSnapshot, out string errorMsg);
                    Assert.False(ok);
                    Assert.Null(badSnapshot);
                    Assert.False(string.IsNullOrWhiteSpace(errorMsg));
                });

                // 绝不允许向外抛出任何未捕获异常！
                Assert.Null(ex);
            }
        }
    }
}
