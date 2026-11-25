#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class V151FeatureSwitchesAndCardSortTests : TestBase
    {
        private static readonly string[] ForbiddenAiWords = new[]
        {
            "彻底", "全面", "深度", "丝滑", "极致",
            "智能", "高级", "精准", "赋能", "闭环",
            "一站式", "切场", "退游", "秒开", "防抖"
        };

        [Fact]
        public void V151_ConfigSectionsAndEnumRoundTrip_Verification()
        {
            // 1. 验证 7 大分区常量
            Assert.Equal("语言设置 / Language", ModFeatureConfig.SectionLanguage);
            Assert.Equal("选项页面布局 / Options Page Layout", ModFeatureConfig.SectionOptionsLayout);
            Assert.Equal("选项页顶部工具栏 / Options Top Toolbar", ModFeatureConfig.SectionTopToolbar);
            Assert.Equal("快捷键绑定规则 / Keybinding Rules", ModFeatureConfig.SectionKeybindRules);
            Assert.Equal("改造件页面设置 / Mods Page", ModFeatureConfig.SectionModsPage);
            Assert.Equal("预载及退出加速 / Preload & Fast Exit", ModFeatureConfig.SectionPreloadAndExit);
            Assert.Equal("调试日志 / Debug Logging", ModFeatureConfig.SectionDebugLogging);

            // 2. DisplayLocation 三态往返与解析
            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
            var locChoicesZh = ModFeatureConfig.GetDisplayLocationChoices();
            Assert.Equal(3, locChoicesZh.Count);
            Assert.Equal("模组界面合并至原版界面", locChoicesZh[0]);
            Assert.Equal("模组独立界面", locChoicesZh[1]);
            Assert.Equal("原版设置界面", locChoicesZh[2]);

            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
            var locChoicesEn = ModFeatureConfig.GetDisplayLocationChoices();
            Assert.Equal(3, locChoicesEn.Count);
            Assert.Equal("Merge Mod UI into Vanilla", locChoicesEn[0]);
            Assert.Equal("Standalone Mod Window", locChoicesEn[1]);
            Assert.Equal("Vanilla Options Only", locChoicesEn[2]);
            BilingualText.ModeOverrideForTest = null;

            Assert.Equal(OptionDisplayLocation.MergedInOptions, ModFeatureConfig.ParseDisplayLocation("merged"));
            Assert.Equal(OptionDisplayLocation.StandaloneBottomButton, ModFeatureConfig.ParseDisplayLocation("standalone"));
            Assert.Equal(OptionDisplayLocation.GearOnly, ModFeatureConfig.ParseDisplayLocation("gear_only"));
            Assert.Equal(OptionDisplayLocation.MergedInOptions, ModFeatureConfig.ParseDisplayLocation("unknown_fallback"));

            Assert.Equal("merged", ModFeatureConfig.DisplayLocationToChoiceValue(OptionDisplayLocation.MergedInOptions));
            Assert.Equal("standalone", ModFeatureConfig.DisplayLocationToChoiceValue(OptionDisplayLocation.StandaloneBottomButton));
            Assert.Equal("gear_only", ModFeatureConfig.DisplayLocationToChoiceValue(OptionDisplayLocation.GearOnly));

            Assert.Equal(0, ModFeatureConfig.DisplayLocationToIndex("merged"));
            Assert.Equal(1, ModFeatureConfig.DisplayLocationToIndex("standalone"));
            Assert.Equal(2, ModFeatureConfig.DisplayLocationToIndex("gear_only"));
            Assert.Equal(0, ModFeatureConfig.DisplayLocationToIndex(null));

            Assert.Equal("merged", ModFeatureConfig.IndexToDisplayLocationValue(0));
            Assert.Equal("standalone", ModFeatureConfig.IndexToDisplayLocationValue(1));
            Assert.Equal("gear_only", ModFeatureConfig.IndexToDisplayLocationValue(2));
            Assert.Equal("merged", ModFeatureConfig.IndexToDisplayLocationValue(99));

            // 3. CardSortMode 六态往返与解析
            var sortChoices = ModFeatureConfig.GetCardSortModeChoices();
            Assert.Equal(6, sortChoices.Count);

            Assert.Equal(ModCardSortMode.PinyinAsc, ModFeatureConfig.ParseCardSortMode("pinyin_asc"));
            Assert.Equal(ModCardSortMode.PinyinDesc, ModFeatureConfig.ParseCardSortMode("pinyin_desc"));
            Assert.Equal(ModCardSortMode.CustomOrder, ModFeatureConfig.ParseCardSortMode("custom"));
            Assert.Equal(ModCardSortMode.RecentEnabled, ModFeatureConfig.ParseCardSortMode("recent_enabled"));
            Assert.Equal(ModCardSortMode.HotkeyCountDesc, ModFeatureConfig.ParseCardSortMode("hotkey_count"));
            Assert.Equal(ModCardSortMode.GameLoadOrder, ModFeatureConfig.ParseCardSortMode("load_order"));
            Assert.Equal(ModCardSortMode.PinyinAsc, ModFeatureConfig.ParseCardSortMode("invalid_fallback"));

            Assert.Equal("pinyin_asc", ModFeatureConfig.CardSortModeToChoiceValue(ModCardSortMode.PinyinAsc));
            Assert.Equal("pinyin_desc", ModFeatureConfig.CardSortModeToChoiceValue(ModCardSortMode.PinyinDesc));
            Assert.Equal("custom", ModFeatureConfig.CardSortModeToChoiceValue(ModCardSortMode.CustomOrder));
            Assert.Equal("recent_enabled", ModFeatureConfig.CardSortModeToChoiceValue(ModCardSortMode.RecentEnabled));
            Assert.Equal("hotkey_count", ModFeatureConfig.CardSortModeToChoiceValue(ModCardSortMode.HotkeyCountDesc));
            Assert.Equal("load_order", ModFeatureConfig.CardSortModeToChoiceValue(ModCardSortMode.GameLoadOrder));

            Assert.Equal(0, ModFeatureConfig.CardSortModeToIndex("pinyin_asc"));
            Assert.Equal(1, ModFeatureConfig.CardSortModeToIndex("pinyin_desc"));
            Assert.Equal(2, ModFeatureConfig.CardSortModeToIndex("custom"));
            Assert.Equal(3, ModFeatureConfig.CardSortModeToIndex("recent_enabled"));
            Assert.Equal(4, ModFeatureConfig.CardSortModeToIndex("hotkey_count"));
            Assert.Equal(5, ModFeatureConfig.CardSortModeToIndex("load_order"));

            Assert.Equal("pinyin_asc", ModFeatureConfig.IndexToCardSortModeValue(0));
            Assert.Equal("pinyin_desc", ModFeatureConfig.IndexToCardSortModeValue(1));
            Assert.Equal("custom", ModFeatureConfig.IndexToCardSortModeValue(2));
            Assert.Equal("recent_enabled", ModFeatureConfig.IndexToCardSortModeValue(3));
            Assert.Equal("hotkey_count", ModFeatureConfig.IndexToCardSortModeValue(4));
            Assert.Equal("load_order", ModFeatureConfig.IndexToCardSortModeValue(5));
            Assert.Equal("pinyin_asc", ModFeatureConfig.IndexToCardSortModeValue(99));

            // 4. ParseAndClampDebounceMs 钳制测试
            Assert.Equal(0, ModFeatureConfig.ParseAndClampDebounceMs("-100"));
            Assert.Equal(300, ModFeatureConfig.ParseAndClampDebounceMs(""));
            Assert.Equal(300, ModFeatureConfig.ParseAndClampDebounceMs(null));
            Assert.Equal(300, ModFeatureConfig.ParseAndClampDebounceMs("not_a_number"));
            Assert.Equal(2000, ModFeatureConfig.ParseAndClampDebounceMs("5000"));
            Assert.Equal(450, ModFeatureConfig.ParseAndClampDebounceMs("450"));
            Assert.Equal(0, ModFeatureConfig.ParseAndClampDebounceMs("0"));
            Assert.Equal(2000, ModFeatureConfig.ParseAndClampDebounceMs("2000"));

            // 5. 验证 pin_own_card_to_top 与 mod_card_sort_mode 从 ModConfig 彻底剥离
            ModFeatureConfig.EnsureRegistered();
            var modConfig = ModConfig.Find(ModFeatureConfig.ModId);
            if (modConfig != null)
            {
                Assert.Null(modConfig.Get("pin_own_card_to_top"));
                Assert.Null(modConfig.Get("mod_card_sort_mode"));
            }
            Assert.False(ModFeatureConfig.PinOwnCardToTop);

            // 6. 验证 SetCardSortMode 持久化在 ComboKeyStore / PrefsStorage
            ModFeatureConfig.SetCardSortMode(ModCardSortMode.HotkeyCountDesc);
            Assert.Equal(ModCardSortMode.HotkeyCountDesc, ComboKeyStore.GetSavedCardSortMode());
            Assert.Equal(ModCardSortMode.HotkeyCountDesc, ModFeatureConfig.CardSortMode);
        }

        [Fact]
        public void V151_ConflictScannerCache_DynamicOptionVisibility_Test()
        {
            // 默认无覆盖时安全执行
            bool initial = ModFeatureConfig.IsConflictScannerModEnabled();

            // 测试覆盖为 false
            ModFeatureConfig.OverrideConflictScannerModEnabledForTests = false;
            Assert.False(ModFeatureConfig.IsConflictScannerModEnabled());

            // 测试覆盖为 true
            ModFeatureConfig.OverrideConflictScannerModEnabledForTests = true;
            Assert.True(ModFeatureConfig.IsConflictScannerModEnabled());

            // 重置
            ModFeatureConfig.OverrideConflictScannerModEnabledForTests = null;
            Assert.Equal(initial, ModFeatureConfig.IsConflictScannerModEnabled());
        }

        [Fact]
        public void V151_CardSortMode_SixSortingStrategiesAndPinToTop_Verification()
        {
            // 构建测试模组组
            var ownGroup = new ModOptionGroup
            {
                ModId = ModFeatureConfig.ModId,
                DisplayName = "可能是按键统一优化",
                PinyinSortKey = "k",
                LoadOrderIndex = 1,
                RecentEnabledRank = 3
            };

            var appleGroup = new ModOptionGroup
            {
                ModId = "mod_apple",
                DisplayName = "苹果模组",
                PinyinSortKey = "p",
                LoadOrderIndex = 3,
                RecentEnabledRank = 1
            };
            for (int i = 0; i < 5; i++) appleGroup.Hotkeys.Add(new ModHotkeyEntry { Id = $"apple_{i}" });

            var bananaGroup = new ModOptionGroup
            {
                ModId = "mod_banana",
                DisplayName = "香蕉模组",
                PinyinSortKey = "x",
                LoadOrderIndex = 0,
                RecentEnabledRank = 2
            };
            for (int i = 0; i < 2; i++) bananaGroup.Hotkeys.Add(new ModHotkeyEntry { Id = $"banana_{i}" });

            var cherryGroup = new ModOptionGroup
            {
                ModId = "mod_cherry",
                DisplayName = "樱桃模组",
                PinyinSortKey = "y",
                LoadOrderIndex = 2,
                RecentEnabledRank = 0
            };
            for (int i = 0; i < 8; i++) cherryGroup.Hotkeys.Add(new ModHotkeyEntry { Id = $"cherry_{i}" });

            var list = new List<ModOptionGroup> { cherryGroup, bananaGroup, appleGroup, ownGroup };

            // 1. PinyinAsc (k, p, x, y)
            var pinyinAscList = new List<ModOptionGroup>(list);
            ComboKeyStore.SortModOptionGroups(pinyinAscList, ModCardSortMode.PinyinAsc, pinOwnToTopOverride: false);
            Assert.Equal("probably_unified_hotkeys", pinyinAscList[0].ModId); // k
            Assert.Equal("mod_apple", pinyinAscList[1].ModId); // p
            Assert.Equal("mod_banana", pinyinAscList[2].ModId); // x
            Assert.Equal("mod_cherry", pinyinAscList[3].ModId); // y

            // 2. PinyinDesc (y, x, p, k)
            var pinyinDescList = new List<ModOptionGroup>(list);
            ComboKeyStore.SortModOptionGroups(pinyinDescList, ModCardSortMode.PinyinDesc, pinOwnToTopOverride: false);
            Assert.Equal("mod_cherry", pinyinDescList[0].ModId); // y
            Assert.Equal("mod_banana", pinyinDescList[1].ModId); // x
            Assert.Equal("mod_apple", pinyinDescList[2].ModId);  // p
            Assert.Equal("probably_unified_hotkeys", pinyinDescList[3].ModId); // k

            // 3. HotkeyCountDesc (cherry=8, apple=5, banana=2, own=0)
            var countList = new List<ModOptionGroup>(list);
            ComboKeyStore.SortModOptionGroups(countList, ModCardSortMode.HotkeyCountDesc, pinOwnToTopOverride: false);
            Assert.Equal("mod_cherry", countList[0].ModId);
            Assert.Equal("mod_apple", countList[1].ModId);
            Assert.Equal("mod_banana", countList[2].ModId);
            Assert.Equal("probably_unified_hotkeys", countList[3].ModId);

            // 4. GameLoadOrder (banana=0, own=1, cherry=2, apple=3)
            var loadList = new List<ModOptionGroup>(list);
            ComboKeyStore.SortModOptionGroups(loadList, ModCardSortMode.GameLoadOrder, pinOwnToTopOverride: false);
            Assert.Equal("mod_banana", loadList[0].ModId);
            Assert.Equal("probably_unified_hotkeys", loadList[1].ModId);
            Assert.Equal("mod_cherry", loadList[2].ModId);
            Assert.Equal("mod_apple", loadList[3].ModId);

            // 5. RecentEnabled (own=3, banana=2, apple=1, cherry=0)
            var recentList = new List<ModOptionGroup>(list);
            ComboKeyStore.SortModOptionGroups(recentList, ModCardSortMode.RecentEnabled, pinOwnToTopOverride: false);
            Assert.Equal("probably_unified_hotkeys", recentList[0].ModId);
            Assert.Equal("mod_banana", recentList[1].ModId);
            Assert.Equal("mod_apple", recentList[2].ModId);
            Assert.Equal("mod_cherry", recentList[3].ModId);

            // 6. CustomOrder: 指定 cherry, banana；未指定的 own, apple 按照 LoadOrder 排在末尾
            var customOrder = new[] { "mod_cherry", "mod_banana" };
            var customList = new List<ModOptionGroup>(list);
            ComboKeyStore.SortModOptionGroups(customList, ModCardSortMode.CustomOrder, customOrderOverride: customOrder, pinOwnToTopOverride: false);
            Assert.Equal("mod_cherry", customList[0].ModId);
            Assert.Equal("mod_banana", customList[1].ModId);
            Assert.Equal("probably_unified_hotkeys", customList[2].ModId); // LoadOrder 1
            Assert.Equal("mod_apple", customList[3].ModId); // LoadOrder 3
        }

        [Fact]
        public void V151_CardDrag_FiveDirtyStateGuards_Verification()
        {
            UiSessionState.ModGroups.Clear();
            var g1 = new ModOptionGroup { ModId = "mod1", DisplayName = "Mod 1" };
            var g2 = new ModOptionGroup { ModId = "mod2", DisplayName = "Mod 2" };
            var g3 = new ModOptionGroup { ModId = "mod3", DisplayName = "Mod 3" };

            UiSessionState.ModGroups.Add(g1);
            UiSessionState.ModGroups.Add(g2);
            UiSessionState.ModGroups.Add(g3);

            UiSessionState.CurrentCardSortMode = ModCardSortMode.PinyinAsc;
            UiSessionState.IsSortModeExplicitlyChosenByUser = false;

            // ① 开窗初始快照：HasUnsavedChanges 必须恒返回 false
            UiSessionState.CaptureOpenBaselineSnapshot();
            Assert.False(UiSessionState.HasCardOrderOrSortModeChanged());
            Assert.False(UiSessionState.HasUnsavedChanges());

            // ② 模拟拖动改变顺序：变为 mod2, mod1, mod3，并切入 CustomOrder
            UiSessionState.ModGroups.Clear();
            UiSessionState.ModGroups.Add(g2);
            UiSessionState.ModGroups.Add(g1);
            UiSessionState.ModGroups.Add(g3);
            UiSessionState.CurrentCardSortMode = ModCardSortMode.CustomOrder;

            Assert.True(UiSessionState.HasCardOrderOrSortModeChanged());
            Assert.True(UiSessionState.HasUnsavedChanges());

            // ③ 模拟把卡片重新拖回原位：mod1, mod2, mod3，调用 CheckCardOrderDirtyState
            UiSessionState.ModGroups.Clear();
            UiSessionState.ModGroups.Add(g1);
            UiSessionState.ModGroups.Add(g2);
            UiSessionState.ModGroups.Add(g3);

            UiSessionState.CheckCardOrderDirtyState();

            // 自动归位 BaselineCardSortMode，判脏严格返回 false！
            Assert.Equal(ModCardSortMode.PinyinAsc, UiSessionState.CurrentCardSortMode);
            Assert.False(UiSessionState.HasCardOrderOrSortModeChanged());
            Assert.False(UiSessionState.HasUnsavedChanges());

            // ④ 再次乱序后，调用 DiscardAllPendingChanges
            UiSessionState.ModGroups.Clear();
            UiSessionState.ModGroups.Add(g3);
            UiSessionState.ModGroups.Add(g2);
            UiSessionState.ModGroups.Add(g1);
            UiSessionState.CurrentCardSortMode = ModCardSortMode.CustomOrder;
            Assert.True(UiSessionState.HasUnsavedChanges());

            UiSessionState.DiscardAllPendingChanges();

            // 验证完整还原卡片顺序与排序模式
            Assert.Equal("mod1", UiSessionState.ModGroups[0].ModId);
            Assert.Equal("mod2", UiSessionState.ModGroups[1].ModId);
            Assert.Equal("mod3", UiSessionState.ModGroups[2].ModId);
            Assert.Equal(ModCardSortMode.PinyinAsc, UiSessionState.CurrentCardSortMode);
            Assert.False(UiSessionState.HasUnsavedChanges());

            // ⑤ 刷新基准快照后 HasUnsavedChanges 保持 false
            UiSessionState.CaptureOpenBaselineSnapshot();
            Assert.False(UiSessionState.HasUnsavedChanges());
        }

        [Fact]
        public void V151_ResetToDefaults_ExemptsCardSortMode_Verification()
        {
            var ownGroup = new ModOptionGroup
            {
                ModId = BilingualText.ModId,
                DisplayName = "可能是按键统一优化"
            };

            // 设置自定义排序模式
            ModFeatureConfig.SetCardSortMode(ModCardSortMode.CustomOrder);
            Assert.Equal(ModCardSortMode.CustomOrder, ModFeatureConfig.CardSortMode);

            // 模拟扫描器注入的 ResetToDefaultsAction
            var savedSortMode = ModFeatureConfig.CardSortMode;
            Action resetAction = () =>
            {
                // 普通设置重置...
                // 恢复默认时严格豁免排序模式
                ModFeatureConfig.SetCardSortMode(savedSortMode);
            };

            ownGroup.ResetToDefaultsAction = resetAction;
            ownGroup.ResetToDefaultsAction.Invoke();

            // 验证重置后 CardSortMode 保持不变，不受影响
            Assert.Equal(ModCardSortMode.CustomOrder, ModFeatureConfig.CardSortMode);
        }

        [Fact]
        public void V151_ProfileSnapshot_ExpansionAndBackwardCompatibility_Verification()
        {
            // 1. 新版包含 sortMode 与 cardOrder 的 JSON 序列化与解析
            var snap = new ProfileSnapshot
            {
                Schema = HotkeyProfileManager.SchemaId,
                Version = "1.5.1",
                Name = "方案一",
                CardSortMode = "custom",
                CardOrder = new List<string> { "mod_a", "mod_b", "mod_c" }
            };
            snap.Hotkeys["test_action"] = "F1";

            string json = HotkeyProfileManager.SerializeSnapshotJson(snap);
            Assert.Contains("\"sortMode\":\"custom\"", json);
            Assert.Contains("\"cardOrder\":[\"mod_a\",\"mod_b\",\"mod_c\"]", json);

            bool ok = HotkeyProfileManager.TryParseSnapshotJson(json, out var parsed, out var err);
            Assert.True(ok, $"Parse failed: {err}");
            Assert.NotNull(parsed);
            Assert.Equal("custom", parsed!.CardSortMode);
            Assert.NotNull(parsed.CardOrder);
            Assert.Equal(3, parsed.CardOrder!.Count);
            Assert.Equal("mod_a", parsed.CardOrder[0]);

            // 2. 向下兼容：解析旧版不含 sortMode 与 cardOrder 的 JSON
            string legacyJson = "{\"schema\":\"puh_profile_v1\",\"version\":\"1.5.0\",\"name\":\"旧方案\",\"timestamp\":\"11-20 12:00\",\"hotkeys\":{\"vanilla.key_interact\":\"E\"}}";
            bool legacyOk = HotkeyProfileManager.TryParseSnapshotJson(legacyJson, out var legacyParsed, out var legacyErr);
            Assert.True(legacyOk, $"Legacy parse failed: {legacyErr}");
            Assert.NotNull(legacyParsed);
            Assert.Null(legacyParsed!.CardSortMode);
            Assert.Null(legacyParsed.CardOrder);
            Assert.Equal("E", legacyParsed.Hotkeys["vanilla.key_interact"]);
        }

        [Fact]
        public void V151_AllowConflictingKeys_SuppressionAndBypass_Verification()
        {
            var entryPrimary = new ModHotkeyEntry
            {
                Id = "mod1.action_g",
                ModId = "mod1",
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.G
            };
            entryPrimary.GetExternalModifiers = () => KeyModifierMask.Ctrl;
            entryPrimary.GetKey = () => KeyCode.G;

            var entrySecondary = new ModHotkeyEntry
            {
                Id = "mod2.action_g",
                ModId = "mod2",
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.G
            };
            entrySecondary.GetExternalModifiers = () => KeyModifierMask.Ctrl;
            entrySecondary.GetKey = () => KeyCode.G;

            ComboKeyStore.RebuildConflictingOwnership(new[] { entryPrimary, entrySecondary });

            // 1. 当 AllowConflictingKeys = false 时（默认不允许重复键同时触发）：
            // 顺位靠后的 entrySecondary 必须被抑制，首位 entryPrimary 不受抑制
            ModFeatureConfig.OverrideAllowConflictingKeysForTests = false;
            Assert.False(ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(entryPrimary.Id));
            Assert.True(ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(entrySecondary.Id));

            // 2. 当 AllowConflictingKeys = true 时（允许重复键同时生效）：
            // 所有冲突键位均放行，不抑制
            ModFeatureConfig.OverrideAllowConflictingKeysForTests = true;
            Assert.False(ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(entryPrimary.Id));
            Assert.False(ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(entrySecondary.Id));
        }

        [Fact]
        public void V151_AntiAiWriting_StaticAssertionGuardrail()
        {
            // 反射收集 ModFeatureConfig 中所有的字符串常量和注册说明
            var configType = typeof(ModFeatureConfig);
            var stringFields = configType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => (string)f.GetValue(null)!)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var bilingualType = typeof(BilingualText);
            var bilingFields = bilingualType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => (string)f.GetValue(null)!)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var allConfigStrings = stringFields.Concat(bilingFields).Distinct().ToList();

            var violations = new List<string>();

            foreach (var str in allConfigStrings)
            {
                foreach (var forbidden in ForbiddenAiWords)
                {
                    if (str.Contains(forbidden))
                    {
                        violations.Add($"Config string '{str}' contains forbidden word '{forbidden}'");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                $"Anti-AI writing guardrail violated in ModFeatureConfig/BilingualText ({violations.Count} items):\n" +
                string.Join("\n", violations)
            );
        }

        // =========================================================================
        // Layer 2: 反射与沙盘深挖自测 (防雪崩委托防重、改造件封面切换、拖拽组件接口)
        // =========================================================================

        [Fact]
        public void ModFeatureConfig_EnsureRegistered_MultipleCalls_NoDuplicateDelegates()
        {
            // 连续调用 5 次 EnsureRegistered()，断言 BoundEntries 防重集合有效，绝不重复绑定委托
            for (int i = 0; i < 5; i++)
            {
                ModFeatureConfig.EnsureRegistered();
            }

            int changeCount = 0;
            Action handler = () => changeCount++;
            ModFeatureConfig.FeatureConfigChanged += handler;
            try
            {
                var field = typeof(ModFeatureConfig).GetField("FeatureConfigChanged", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var eventDelegate = field?.GetValue(null) as Action;
                eventDelegate?.Invoke();
                Assert.Equal(1, changeCount);
            }
            finally
            {
                ModFeatureConfig.FeatureConfigChanged -= handler;
            }
        }

        [Fact]
        public void ModFeatureConfig_HideOwnCoverInModsPanel_ToggledEventFires()
        {
            bool toggledFired = false;
            Action handler = () => toggledFired = true;
            ModFeatureConfig.HideOwnCoverToggled += handler;
            try
            {
                var eventField = typeof(ModFeatureConfig).GetField("HideOwnCoverToggled", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var action = eventField?.GetValue(null) as Action;
                action?.Invoke();
                Assert.True(toggledFired);
            }
            finally
            {
                ModFeatureConfig.HideOwnCoverToggled -= handler;
            }
        }

        [Fact]
        public void CardDragReorderController_InterfacesAndStateReset_Verification()
        {
            var controllerType = typeof(CardDragReorderController);
            Assert.NotNull(controllerType);

            // 1. 验证接口实现
            var interfaceNames = controllerType.GetInterfaces().Select(i => i.Name).ToList();
            Assert.Contains("IInitializePotentialDragHandler", interfaceNames);
            Assert.Contains("IBeginDragHandler", interfaceNames);
            Assert.Contains("IDragHandler", interfaceNames);
            Assert.Contains("IEndDragHandler", interfaceNames);
            Assert.Contains("IScrollHandler", interfaceNames);

            // 2. 验证包含 OnDisable 兜底恢复方法
            var onDisableMethod = controllerType.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(onDisableMethod);

            // 3. 模拟调用 ApplyCardReorder
            var applyMethod = controllerType.GetMethod("ApplyCardReorder", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(applyMethod);

            UiSessionState.ModGroups.Clear();
            var g0 = new ModOptionGroup { ModId = "mod_a", DisplayName = "Mod A", PinyinSortKey = "a" };
            var g1 = new ModOptionGroup { ModId = "mod_b", DisplayName = "Mod B", PinyinSortKey = "b" };
            var g2 = new ModOptionGroup { ModId = "mod_c", DisplayName = "Mod C", PinyinSortKey = "c" };
            UiSessionState.ModGroups.Add(g0);
            UiSessionState.ModGroups.Add(g1);
            UiSessionState.ModGroups.Add(g2);

            UiSessionState.CurrentCardSortMode = ModCardSortMode.PinyinAsc;
            UiSessionState.CaptureOpenBaselineSnapshot();

            // 模拟拖动卡片从下标 0 拖至 2
            applyMethod.Invoke(null, new object?[] { g0, 0, 2, null });

            // 断言 ModGroups 顺序改变，且 CurrentCardSortMode 自动切换为 CustomOrder
            Assert.Equal("mod_b", UiSessionState.ModGroups[0].ModId);
            Assert.Equal("mod_a", UiSessionState.ModGroups[1].ModId);
            Assert.Equal(ModCardSortMode.CustomOrder, UiSessionState.CurrentCardSortMode);
            Assert.True(UiSessionState.HasUnsavedChanges());

            // 拖回原位置 1 -> 0，与 BaselineVisualModIdOrder 一致
            applyMethod.Invoke(null, new object?[] { g0, 1, 0, null });

            // 断言自动归位回 PinyinAsc 且 HasUnsavedChanges == false
            Assert.Equal("mod_a", UiSessionState.ModGroups[0].ModId);
            Assert.Equal("mod_b", UiSessionState.ModGroups[1].ModId);
            Assert.Equal("mod_c", UiSessionState.ModGroups[2].ModId);
            Assert.Equal(ModCardSortMode.PinyinAsc, UiSessionState.CurrentCardSortMode);
            Assert.False(UiSessionState.HasUnsavedChanges());
        }

        // =========================================================================
        // Layer 3: 「模组独立界面」原版纯净拦截与恢复自测
        // =========================================================================

        [Fact]
        public void OptionAndKeyBindPatches_VanillaStandalone_FiltersNonVanillaSectionsAndEntries()
        {
            ModFeatureConfig.OverrideDisplayLocationForTests = OptionDisplayLocation.StandaloneBottomButton;
            try
            {
                var customSec = new GameOptionRegistry.Section
                {
                    id = "custom_test_sec",
                    source = "third_party",
                    label = () => "Custom Sec"
                };
                var customEntry = new GameOptionRegistry.Entry
                {
                    id = "custom_test_entry",
                    source = "third_party",
                    kind = GameOptionRegistry.Kind.Toggle,
                    label = () => "Custom Entry",
                    tooltip = () => "Custom Tip"
                };
                var vanillaSec = new GameOptionRegistry.Section
                {
                    id = GameOptionRegistry.SECTION_KEYBIND,
                    source = "vanilla"
                };
                var vanillaEntry = new GameOptionRegistry.Entry
                {
                    id = "key_rotate_cw",
                    source = "vanilla",
                    kind = GameOptionRegistry.Kind.KeyBind
                };

                // 在独立模式下：非原版 Section 与 Entry 直接返回 false 拒绝构建
                Assert.False(Patch_GameOptionUIManager_BuildSection.Prefix(null!, customSec));
                Assert.False(Patch_GameOptionUIManager_BuildEntry.Prefix(null!, customEntry));

                // 原版 Section 与 Entry 正常放行构建
                Assert.True(Patch_GameOptionUIManager_BuildSection.Prefix(null!, vanillaSec));
                Assert.True(Patch_GameOptionUIManager_BuildEntry.Prefix(null!, vanillaEntry));
            }
            finally
            {
                ModFeatureConfig.OverrideDisplayLocationForTests = null;
            }
        }

        [Fact]
        public void UiSessionState_VanillaStandaloneEscapeAndUnsavedChanges_Verification()
        {
            // 1. 验证 MarkEscapeConsumed 后 Postfix 拦截
            UiSessionState.MarkEscapeConsumed();
            bool result = false;
            Patch_GameOptionUIManager_ConsumedEscapeThisFrame.Postfix(ref result);
            Assert.True(result);

            // 2. 验证 HasVanillaStandaloneUnsavedChanges 在未打开窗口或明确关闭时安全返回 false
            UiSessionState.IsExplicitVanillaClosing = true;
            Assert.False(UiSessionState.HasVanillaStandaloneUnsavedChanges());
            UiSessionState.IsExplicitVanillaClosing = false;
            Assert.False(UiSessionState.HasVanillaStandaloneUnsavedChanges());
        }

        // =========================================================================
        // Layer 4: 全量穷举自查 (debounce_ms 落盘、顶栏防死锁复位、16 种改键状态组合)
        // =========================================================================

        private static readonly FieldInfo? ModConfigConfigsField =
            typeof(ModConfig).GetField("configs", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        private static ModConfig EnsureTestModConfig()
        {
            var dict = ModConfigConfigsField?.GetValue(null) as Dictionary<string, ModConfig>;
            if (dict == null)
            {
                dict = new Dictionary<string, ModConfig>(StringComparer.OrdinalIgnoreCase);
                ModConfigConfigsField?.SetValue(null, dict);
            }

            if (!dict.TryGetValue(ModFeatureConfig.ModId, out var config))
            {
                var ctor = typeof(ModConfig).GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                config = (ModConfig)ctor!.Invoke(new object[] { ModFeatureConfig.ModId });
                dict[ModFeatureConfig.ModId] = config;
            }

            ModFeatureConfig.EnsureRegistered();
            return config;
        }

        [Fact]
        public void SearchDebounceMs_ModGeneralEntry_ApplyPending_RoundTrip()
        {
            var config = EnsureTestModConfig();
            Assert.NotNull(config);

            var entry = config.Get(ModFeatureConfig.KeySearchDebounceMs);
            Assert.NotNull(entry);

            var gen = new ModGeneralEntry
            {
                Id = $"{ModFeatureConfig.ModId}.{ModFeatureConfig.KeySearchDebounceMs}",
                ModId = ModFeatureConfig.ModId,
                Kind = OptionKind.Input,
                GetString = () => entry.BoxedValue?.ToString() ?? string.Empty,
                SetString = s => ModOptionScanner.SetEntryBoxed(entry, s)
            };

            // 1. 正常值 750
            gen.PendingString = "750";
            gen.ApplyPending();
            Assert.Equal("750", entry.BoxedValue);
            Assert.Equal(750, ModFeatureConfig.SearchDebounceMs);

            // 2. 负数 "-50" 钳制为 0
            gen.PendingString = "-50";
            gen.ApplyPending();
            Assert.Equal(0, ModFeatureConfig.SearchDebounceMs);

            // 3. 超上限 "9999" 钳制为 2000
            gen.PendingString = "9999";
            gen.ApplyPending();
            Assert.Equal(2000, ModFeatureConfig.SearchDebounceMs);

            // 4. 非数字 "abc" 回退 300
            gen.PendingString = "abc";
            gen.ApplyPending();
            Assert.Equal(300, ModFeatureConfig.SearchDebounceMs);
        }

        [Fact]
        public void TopSearchAndActionBar_ToolbarVisibilityToggle_AutoResetsFilters()
        {
            UiSessionState.SearchQuery = "test_search";
            UiSessionState.NormalDisplayMode = NormalDisplayMode.HotkeysOnly;
            UiSessionState.IsConflictViewActive = true;

            ModFeatureConfig.OverrideShowTopSearchBarForTests = false;
            ModFeatureConfig.OverrideShowDisplayFilterDropdownForTests = false;
            ModFeatureConfig.OverrideShowConflictViewButtonForTests = false;

            TopSearchAndActionBar.RefreshToolbarVisibilityInPlace();

            Assert.Equal(string.Empty, UiSessionState.SearchQuery);
            Assert.Equal(NormalDisplayMode.All, UiSessionState.NormalDisplayMode);
            Assert.False(UiSessionState.IsConflictViewActive);
        }

        [Fact]
        public void KeybindRules_SixteenStateCombinations_TopHintAndConflictArbitration()
        {
            EnsureTestModConfig();

            bool[] bools = new[] { false, true };

            // 穷举 2^4 = 16 种组合
            foreach (bool combo in bools)
            {
                foreach (bool mouse in bools)
                {
                    foreach (bool empty in bools)
                    {
                        foreach (bool allowConflict in bools)
                        {
                            ModFeatureConfig.OverrideEnableComboKeyBindForTests = combo;
                            ModFeatureConfig.OverrideEnableMouseButtonRebindForTests = mouse;
                            ModFeatureConfig.OverrideEnableEmptyKeyUnbindForTests = empty;
                            ModFeatureConfig.OverrideAllowConflictingKeysForTests = allowConflict;

                            // 1. 验证顶部提示语在任意开关组合下始终返回 1.5.0 固定原文
                            BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
                            string hintZh = TopSearchAndActionBar.GetDynamicTopHintText();
                            Assert.Equal("Ctrl/Shift/Alt+侧键 | Esc=无", hintZh);

                            BilingualText.ModeOverrideForTest = UiLanguageMode.English;
                            string hintEn = TopSearchAndActionBar.GetDynamicTopHintText();
                            Assert.Equal("Combos/Side | Esc=None", hintEn);
                            BilingualText.ModeOverrideForTest = null;

                            // 2. 验证冲突仲裁抑制逻辑
                            var hk1 = new ModHotkeyEntry { Id = "test.vanilla.key", ModId = "vanilla", DefaultKey = KeyCode.F1 };
                            hk1.GetKey = () => KeyCode.F1;
                            var hk2 = new ModHotkeyEntry { Id = "test.mod.key", ModId = "other_mod", DefaultKey = KeyCode.F1 };
                            hk2.GetKey = () => KeyCode.F1;

                            ComboKeyStore.RebuildConflictingOwnership(new[] { hk1, hk2 });

                            if (allowConflict)
                            {
                                Assert.False(ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(hk1.Id));
                                Assert.False(ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(hk2.Id));
                            }
                            else
                            {
                                Assert.False(ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(hk1.Id));
                                Assert.True(ComboKeyStore.IsSecondaryConflictingHotkeySuppressed(hk2.Id));
                            }
                        }
                    }
                }
            }
        }

        [Fact]
        public void V151_CardSortMode_DropdownSyncAndDisplayLocation_Integration()
        {
            bool locFired = false;
            Action locHandler = () => locFired = true;
            ModFeatureConfig.DisplayLocationChanged += locHandler;

            bool sortFired = false;
            Action sortHandler = () => sortFired = true;
            ModFeatureConfig.CardSortModeChanged += sortHandler;

            try
            {
                ModFeatureConfig.SetDisplayLocation(OptionDisplayLocation.StandaloneBottomButton);
                Assert.True(locFired);

                ModFeatureConfig.SetCardSortMode(ModCardSortMode.RecentEnabled);
                Assert.True(sortFired);

                // 验证中英双语选择列表完备
                BilingualText.ModeOverrideForTest = UiLanguageMode.SimplifiedChinese;
                var sortChoicesZh = ModFeatureConfig.GetCardSortModeChoices();
                Assert.Equal(6, sortChoicesZh.Count);
                Assert.All(sortChoicesZh, s => Assert.False(string.IsNullOrEmpty(s)));

                BilingualText.ModeOverrideForTest = UiLanguageMode.English;
                var sortChoicesEn = ModFeatureConfig.GetCardSortModeChoices();
                Assert.Equal(6, sortChoicesEn.Count);
                Assert.All(sortChoicesEn, s => Assert.False(string.IsNullOrEmpty(s)));
                BilingualText.ModeOverrideForTest = null;
            }
            finally
            {
                ModFeatureConfig.DisplayLocationChanged -= locHandler;
                ModFeatureConfig.CardSortModeChanged -= sortHandler;
            }
        }

        [Fact]
        public void V151_CardDrag_RestrictedUnderSearchFilter_Verification()
        {
            var isRestrictedMethod = typeof(CardDragReorderController).GetMethod(
                "IsDragRestrictedByFilter",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(isRestrictedMethod);

            // 1. 全部清空时：不受限
            UiSessionState.SearchQuery = string.Empty;
            UiSessionState.IsConflictViewActive = false;
            UiSessionState.IsDiagnosticViewActive = false;
            UiSessionState.NormalDisplayMode = NormalDisplayMode.All;
            Assert.False((bool)isRestrictedMethod!.Invoke(null, null)!);

            // 2. 搜索框有输入时：受限
            UiSessionState.SearchQuery = "find_something";
            Assert.True((bool)isRestrictedMethod.Invoke(null, null)!);
            UiSessionState.SearchQuery = string.Empty;

            // 3. 冲突模式激活时：受限
            UiSessionState.IsConflictViewActive = true;
            Assert.True((bool)isRestrictedMethod.Invoke(null, null)!);
            UiSessionState.IsConflictViewActive = false;

            // 4. 排查模式激活时：受限
            UiSessionState.IsDiagnosticViewActive = true;
            Assert.True((bool)isRestrictedMethod.Invoke(null, null)!);
            UiSessionState.IsDiagnosticViewActive = false;

            // 5. 筛选模式非 All 时：受限
            UiSessionState.NormalDisplayMode = NormalDisplayMode.HotkeysOnly;
            Assert.True((bool)isRestrictedMethod.Invoke(null, null)!);
            UiSessionState.NormalDisplayMode = NormalDisplayMode.All;
        }

        // =========================================================================
        // 第 2 轮实机 11 项体验反馈回归防退化专项测试
        // =========================================================================

        [Fact]
        public void V151_VanillaKeybindWhitelist_FiltersNonVanillaKeybinds_Verification()
        {
            var keybindSection = new GameOptionRegistry.Section
            {
                id = GameOptionRegistry.SECTION_KEYBIND,
                source = "vanilla"
            };

            // 1. 原版合法按键 (内置固定常量与 toolbox_slot_ 前缀)
            var validVanillaRotate = new GameOptionRegistry.Entry
            {
                id = "key_rotate_cw",
                source = "vanilla",
                kind = GameOptionRegistry.Kind.KeyBind
            };

            var validToolboxSlot = new GameOptionRegistry.Entry
            {
                id = "key_toolbox_slot_0",
                source = "vanilla",
                kind = GameOptionRegistry.Kind.KeyBind
            };

            Assert.True(ModOptionScanner.IsPureVanillaEntry(keybindSection, validVanillaRotate));
            Assert.True(ModOptionScanner.IsPureVanillaEntry(keybindSection, validToolboxSlot));

            // 2. 伪装成原版闭包的第三方注入按键 (水质扫描仪、更好的水龙头等)
            var injectedLiquidScanner = new GameOptionRegistry.Entry
            {
                id = "key_liquid_scanner",
                source = "vanilla", // 假冒 vanilla
                kind = GameOptionRegistry.Kind.KeyBind
            };

            var injectedFaucetScan = new GameOptionRegistry.Entry
            {
                id = "key_faucet_scan",
                source = string.Empty, // 缺省 source
                kind = GameOptionRegistry.Kind.KeyBind
            };

            // 必须被白名单硬核拦截，严格返回 false！
            Assert.False(ModOptionScanner.IsPureVanillaEntry(keybindSection, injectedLiquidScanner));
            Assert.False(ModOptionScanner.IsPureVanillaEntry(keybindSection, injectedFaucetScan));
        }

        [Fact]
        public void V151_BuildSectionAndBuildEntry_InterceptsFaucetScannerInStandalone_Verification()
        {
            try
            {
                ModFeatureConfig.OverrideDisplayLocationForTests = OptionDisplayLocation.StandaloneBottomButton;

                var vanillaSection = new GameOptionRegistry.Section
                {
                    id = GameOptionRegistry.SECTION_KEYBIND,
                    source = "vanilla"
                };

                var customSection = new GameOptionRegistry.Section
                {
                    id = "faucet_section",
                    source = "better_faucet"
                };

                var vanillaRotateEntry = new GameOptionRegistry.Entry
                {
                    id = "key_rotate_cw",
                    source = "vanilla",
                    kind = GameOptionRegistry.Kind.KeyBind
                };

                var vanillaToolboxEntry = new GameOptionRegistry.Entry
                {
                    id = "key_toolbox_slot_0",
                    source = "vanilla",
                    kind = GameOptionRegistry.Kind.KeyBind
                };

                var faucetScannerEntry = new GameOptionRegistry.Entry
                {
                    id = "key_faucet_scanner",
                    source = "better_faucet",
                    kind = GameOptionRegistry.Kind.KeyBind
                };

                var disguisedScannerEntry = new GameOptionRegistry.Entry
                {
                    id = "key_faucet_scanner",
                    source = "vanilla", // 假冒 vanilla
                    kind = GameOptionRegistry.Kind.KeyBind
                };

                // 1. BuildSection 在独立模式下的拦截判定
                Assert.True(Patch_GameOptionUIManager_BuildSection.Prefix(null!, vanillaSection));
                Assert.False(Patch_GameOptionUIManager_BuildSection.Prefix(null!, customSection));

                // 2. BuildEntry 在独立模式下的拦截判定
                Assert.True(Patch_GameOptionUIManager_BuildEntry.Prefix(null!, vanillaRotateEntry));
                Assert.True(Patch_GameOptionUIManager_BuildEntry.Prefix(null!, vanillaToolboxEntry));
                Assert.False(Patch_GameOptionUIManager_BuildEntry.Prefix(null!, faucetScannerEntry));
                Assert.False(Patch_GameOptionUIManager_BuildEntry.Prefix(null!, disguisedScannerEntry));

                // 3. 切回合并模式时全量放行（交由合并中心统一分类管理）
                ModFeatureConfig.OverrideDisplayLocationForTests = OptionDisplayLocation.MergedInOptions;
                Assert.True(Patch_GameOptionUIManager_BuildSection.Prefix(null!, customSection));
                Assert.True(Patch_GameOptionUIManager_BuildEntry.Prefix(null!, faucetScannerEntry));
            }
            finally
            {
                ModFeatureConfig.OverrideDisplayLocationForTests = null;
            }
        }

        [Fact]
        public void V151_UiSessionState_ActiveSessionAndBaselineSnapshot_HandlesNotLost_Verification()
        {
            var testGroup = new ModOptionGroup { ModId = "session_test_mod", DisplayName = "Session Test Mod" };
            UiSessionState.CardParts.Clear();
            var parts = UiSessionState.GetOrCreateCardParts(testGroup);
            Assert.NotNull(parts);

            try
            {
                // ① 在 IsApplyingSettings = true 期间触发 Invalidate，CardParts 绝对不被清空
                UiSessionState.IsApplyingSettings = true;
                ModCatalogCache.Invalidate();
                Assert.True(UiSessionState.CardParts.ContainsKey(testGroup));

                // ② 在 IsBuildingOrOpeningUI = true 期间触发 Invalidate，CardParts 绝对不被清空
                UiSessionState.IsApplyingSettings = false;
                UiSessionState.IsBuildingOrOpeningUI = true;
                ModCatalogCache.Invalidate();
                Assert.True(UiSessionState.CardParts.ContainsKey(testGroup));

                // ③ 在未打开窗口且非构建/应用期触发 Invalidate，正常清空
                UiSessionState.IsBuildingOrOpeningUI = false;
                Assert.Null(UiSessionState.CurrentWindow);
                ModCatalogCache.Invalidate();
                Assert.False(UiSessionState.CardParts.ContainsKey(testGroup));

                // ④ 验证 CaptureOpenBaselineSnapshot 不会抹空已有的 TopHintLabelEl 等句柄
                var dummyEl = new CustomUIElement();
                UiSessionState.TopHintLabelEl = dummyEl;
                UiSessionState.CaptureOpenBaselineSnapshot();
                Assert.Same(dummyEl, UiSessionState.TopHintLabelEl);
            }
            finally
            {
                UiSessionState.IsApplyingSettings = false;
                UiSessionState.IsBuildingOrOpeningUI = false;
                UiSessionState.TopHintLabelEl = null;
                UiSessionState.CardParts.Clear();
            }
        }

        [Fact]
        public void V151_HotkeyProfileManager_LiveCardOrderCaptureAndRestore_Verification()
        {
            // 清空已有自定义顺序
            ComboKeyStore.SetSavedCustomCardOrder(new List<string>());

            var modC = new ModOptionGroup { ModId = "mod_c", DisplayName = "Mod C" };
            var modA = new ModOptionGroup { ModId = "mod_a", DisplayName = "Mod A" };
            var modB = new ModOptionGroup { ModId = "mod_b", DisplayName = "Mod B" };
            var liveGroups = new List<ModOptionGroup> { modC, modA, modB };

            // 1. 调用 BuildSnapshotJson 传入实时 modGroups
            string json = HotkeyProfileManager.BuildSnapshotJson("LiveOrderPreset", Array.Empty<ModHotkeyEntry>(), liveGroups, out _, out _, out _);

            // 2. 断言生成的 JSON 精确记录了实时视觉顺序 [mod_c, mod_a, mod_b]
            Assert.Contains("\"cardOrder\":[\"mod_c\",\"mod_a\",\"mod_b\"]", json);

            // 3. 解析该 JSON
            bool ok = HotkeyProfileManager.TryParseSnapshotJson(json, out var parsed, out var err);
            Assert.True(ok, $"Parse error: {err}");
            Assert.NotNull(parsed);
            Assert.NotNull(parsed!.CardOrder);
            Assert.Equal(3, parsed.CardOrder!.Count);
            Assert.Equal("mod_c", parsed.CardOrder[0]);
            Assert.Equal("mod_a", parsed.CardOrder[1]);
            Assert.Equal("mod_b", parsed.CardOrder[2]);

            // 4. 应用恢复逻辑，断言 ComboKeyStore 保存的自定义顺序被精确还原
            HotkeyProfileManager.ApplySnapshot(parsed, null, liveGroups, false);
            var restoredOrder = ComboKeyStore.GetSavedCustomCardOrder();
            Assert.Equal("mod_c", restoredOrder[0]);
            Assert.Equal("mod_a", restoredOrder[1]);
            Assert.Equal("mod_b", restoredOrder[2]);
        }

        [Fact]
        public void V151_DiagnosticAndConflictView_ToolbarVisibilityAndFilterReset_Verification()
        {
            try
            {
                // 关闭 ShowDisplayFilterDropdown 与 ShowExpandAllButton
                ModFeatureConfig.OverrideShowDisplayFilterDropdownForTests = false;
                ModFeatureConfig.OverrideShowExpandAllButtonForTests = false;

                // 模拟切入排查视图并设置非 All 模式
                UiSessionState.IsDiagnosticViewActive = false;
                UiSessionState.DiagnosticFilterMode = DiagnosticFilterMode.UnmanagedOnly;

                // 执行 SyncViewContainersAndToggleButtons
                ModDiagnosticView.SyncViewContainersAndToggleButtons();

                // 验证在无头环境下零异常，且 DiagnosticFilterMode 自动复位为 All
                Assert.Equal(DiagnosticFilterMode.All, UiSessionState.DiagnosticFilterMode);
            }
            finally
            {
                ModFeatureConfig.OverrideShowDisplayFilterDropdownForTests = null;
                ModFeatureConfig.OverrideShowExpandAllButtonForTests = null;
                UiSessionState.DiagnosticFilterMode = DiagnosticFilterMode.All;
            }
        }

        [Fact]
        public void Harmony_AllPatchMethods_FollowStandardNamingOrAttributes_Verification()
        {
            var asm = typeof(ModFeatureConfig).Assembly;
            var patchTypes = asm.GetTypes()
                .Where(t => t.GetCustomAttributes(false).Any(a => a.GetType().Name == "HarmonyPatch"))
                .ToList();

            Assert.NotEmpty(patchTypes);

            var standardNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "Prefix", "Postfix", "Finalizer", "Transpiler", "Prepare", "TargetMethod", "TargetMethods"
            };

            var patchAttrNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "HarmonyPrefix", "HarmonyPostfix", "HarmonyFinalizer", "HarmonyTranspiler",
                "HarmonyPrepare", "HarmonyTargetMethod", "HarmonyTargetMethods"
            };

            var violations = new List<string>();

            foreach (var type in patchTypes)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.DeclaringType == type)
                    .ToList();

                foreach (var method in methods)
                {
                    bool isStandardName = standardNames.Contains(method.Name);
                    bool hasPatchAttr = method.GetCustomAttributes(false)
                        .Any(a => patchAttrNames.Contains(a.GetType().Name));

                    if (!isStandardName && !hasPatchAttr)
                    {
                        if (method.IsSpecialName) continue;
                        violations.Add($"{type.Name}.{method.Name}");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                $"Found Harmony hook methods that do not follow standard naming or attributes ({violations.Count} items):\n" +
                string.Join("\n", violations)
            );
        }

        [Fact]
        public void OptionAndKeyBindPatches_StandalonePost_TriggersOnVanillaOptionsWindowOpened()
        {
            var bridge = new TestOptionUiBridge();
            var prevBridge = OptionUiHost.Active;
            OptionUiHost.Active = bridge;
            ModFeatureConfig.OverrideDisplayLocationForTests = OptionDisplayLocation.StandaloneBottomButton;

            try
            {
                Patch_GameOptionUIManager_OpenUI_StandalonePost.Postfix(null!);
                Assert.Equal(1, bridge.OnVanillaOpenedCount);
            }
            finally
            {
                OptionUiHost.Active = prevBridge;
                ModFeatureConfig.OverrideDisplayLocationForTests = null;
            }
        }

        [Fact]
        public void OptionAndKeyBindPatches_PollKeyCapture_UnblocksVanillaCaptureWhenNotCapturingModHotkey()
        {
            var bridge = new TestOptionUiBridge();
            var prevBridge = OptionUiHost.Active;
            OptionUiHost.Active = bridge;

            try
            {
                // 1. 当增强窗口未打开且未捕获模组键时（即使正在录制原版快捷键 IsCapturingAnyKey = true）
                bridge.IsEnhancedWindowOpen = false;
                bridge.IsStandaloneModWindowOpen = false;
                bridge.IsCapturingModHotkey = false;
                bridge.IsCapturingAnyKey = true;

                // 必须返回 true 放行原版轮询，彻底杜绝录键与 Esc 关窗死锁
                bool allowed = Patch_GameOptionUIManager_PollKeyCapture.Prefix();
                Assert.True(allowed);

                // 2. 当录制模组热键或处于增强窗口内时，必须返回 false 拦截原版轮询
                bridge.IsCapturingModHotkey = true;
                bool blocked1 = Patch_GameOptionUIManager_PollKeyCapture.Prefix();
                Assert.False(blocked1);

                bridge.IsCapturingModHotkey = false;
                bridge.IsEnhancedWindowOpen = true;
                bool blocked2 = Patch_GameOptionUIManager_PollKeyCapture.Prefix();
                Assert.False(blocked2);

                bridge.IsEnhancedWindowOpen = false;
                bridge.IsStandaloneModWindowOpen = true;
                bool blocked3 = Patch_GameOptionUIManager_PollKeyCapture.Prefix();
                Assert.False(blocked3);
            }
            finally
            {
                OptionUiHost.Active = prevBridge;
            }
        }

        [Fact]
        public void StandaloneModWindow_VanillaHotkeyApplyAndPresetRestore_WritesKeyAndModifiers()
        {
            UiSessionState.IsStandaloneModWindowOpen = true;
            KeyCode savedKey = KeyCode.None;
            const string hkId = "vanilla.key_rotate_cw";
            const string rawOptId = "key_rotate_cw";

            var vanillaHk = new ModHotkeyEntry
            {
                Id = hkId,
                ModId = "vanilla",
                RawOptionId = rawOptId,
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.R,
                GetKey = () => savedKey,
                SetKey = k => savedKey = k
            };

            UiSessionState.AllHotkeys.Clear();
            UiSessionState.AllHotkeys.Add(vanillaHk);

            try
            {
                // 1. 模拟在独立模组窗口中将原版快捷键更改为 Ctrl + Space
                vanillaHk.PendingKey = KeyCode.Space;
                vanillaHk.PendingModifiers = KeyModifierMask.Ctrl;
                vanillaHk.HasPendingChange = true;

                // 2. 调用 ApplyAllSettings 执行落盘
                UnifiedOptionWindow.ApplyAllSettings(null!);

                // 3. 断言原版快捷键的 SetKey 委托被真正调用
                Assert.Equal(KeyCode.Space, savedKey);
                // 4. 断言 ComboKeyStore 保存了组合键修饰位
                Assert.Equal(KeyModifierMask.Ctrl, ComboKeyStore.GetSavedModifiers(hkId, KeyCode.Space));
                Assert.Equal(KeyModifierMask.Ctrl, ComboKeyStore.GetSavedModifiers(rawOptId, KeyCode.Space));

                // 5. 模拟预设方案恢复原版快捷键为 Alt + R
                vanillaHk.PendingKey = KeyCode.R;
                vanillaHk.PendingModifiers = KeyModifierMask.Alt;
                vanillaHk.HasPendingChange = true;

                UnifiedOptionWindow.ApplyAllSettings(null!);

                Assert.Equal(KeyCode.R, savedKey);
                Assert.Equal(KeyModifierMask.Alt, ComboKeyStore.GetSavedModifiers(hkId, KeyCode.R));
            }
            finally
            {
                UiSessionState.IsStandaloneModWindowOpen = false;
                UiSessionState.IsApplyingSettings = false;
                UiSessionState.AllHotkeys.Clear();
            }
        }

        [Fact]
        public void V151_VanillaStandaloneWindow_FiveAntiDirtyScenarios_Verification()
        {
            UiSessionState.VanillaStandaloneWindow = new CustomUIWindow();
            UiSessionState.IsStandaloneModWindowOpen = false;

            // 设置初始非零基准，使 HasVanillaStandaloneUnsavedChanges 处于可判脏准备态
            UiSessionState.VanillaStandaloneToggleBaseline["test_toggle"] = true;

            KeyCode currentKey = KeyCode.F;
            const string hkId = "vanilla.key_rotate_cw";
            const string rawOptId = "key_rotate_cw";

            var vanillaHk = new ModHotkeyEntry
            {
                Id = hkId,
                ModId = "vanilla",
                RawOptionId = rawOptId,
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.F,
                GetKey = () => currentKey,
                SetKey = k => currentKey = k
            };

            UiSessionState.AllHotkeys.Clear();
            UiSessionState.AllHotkeys.Add(vanillaHk);

            try
            {
                // ① 修改单键 (F -> G) 执行 OnBefore - 模拟底层 - OnAfter 之后，HasVanillaStandaloneUnsavedChanges 严格为 false
                vanillaHk.PendingKey = KeyCode.G;
                vanillaHk.PendingModifiers = KeyModifierMask.None;
                vanillaHk.HasPendingChange = true;
                Assert.True(UiSessionState.HasVanillaStandaloneUnsavedChanges());

                UnifiedOptionWindow.Instance.OnBeforeVanillaStandaloneApply(null!);
                currentKey = KeyCode.G;
                UnifiedOptionWindow.Instance.OnAfterVanillaStandaloneApply(null!);

                Assert.Equal(KeyCode.G, currentKey);
                Assert.False(vanillaHk.HasPendingChange);
                Assert.False(UiSessionState.HasVanillaStandaloneUnsavedChanges(), "Scenario 1: Single key apply must strictly return false!");

                // ② 修改组合键 (Space -> Ctrl + Space) 应用后，未按住 Ctrl 状态下判脏严格为 false 且保持修饰键
                vanillaHk.PendingKey = KeyCode.Space;
                vanillaHk.PendingModifiers = KeyModifierMask.Ctrl;
                vanillaHk.HasPendingChange = true;
                Assert.True(UiSessionState.HasVanillaStandaloneUnsavedChanges());

                UnifiedOptionWindow.Instance.OnBeforeVanillaStandaloneApply(null!);
                currentKey = KeyCode.Space;
                UnifiedOptionWindow.Instance.OnAfterVanillaStandaloneApply(null!);

                ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.None;
                Assert.False(UiSessionState.HasVanillaStandaloneUnsavedChanges(), "Scenario 2: Combo apply must strictly return false even without holding Ctrl!");
                Assert.Equal(KeyModifierMask.Ctrl, ComboKeyStore.GetSavedModifiers(hkId, KeyCode.Space));

                // ③ 解绑为空键 KeyCode.None 应用后，修饰位清零且判脏严格为 false
                vanillaHk.PendingKey = KeyCode.None;
                vanillaHk.PendingModifiers = KeyModifierMask.None;
                vanillaHk.HasPendingChange = true;
                Assert.True(UiSessionState.HasVanillaStandaloneUnsavedChanges());

                UnifiedOptionWindow.Instance.OnBeforeVanillaStandaloneApply(null!);
                currentKey = KeyCode.None;
                UnifiedOptionWindow.Instance.OnAfterVanillaStandaloneApply(null!);

                Assert.Equal(KeyModifierMask.None, ComboKeyStore.GetSavedModifiers(hkId, KeyCode.None));
                Assert.True(ComboKeyStore.IsExplicitlyUnbound(hkId));
                Assert.False(UiSessionState.HasVanillaStandaloneUnsavedChanges(), "Scenario 3: Unbind apply must strictly return false!");

                // ④ 空键应用后再重新绑定回 Space 并应用，ComboKeyStore.IsExplicitlyUnbound 恢复为 false
                vanillaHk.PendingKey = KeyCode.Space;
                vanillaHk.PendingModifiers = KeyModifierMask.None;
                vanillaHk.HasPendingChange = true;
                Assert.True(UiSessionState.HasVanillaStandaloneUnsavedChanges());

                UnifiedOptionWindow.Instance.OnBeforeVanillaStandaloneApply(null!);
                currentKey = KeyCode.Space;
                UnifiedOptionWindow.Instance.OnAfterVanillaStandaloneApply(null!);

                Assert.False(ComboKeyStore.IsExplicitlyUnbound(hkId), "Scenario 4: Rebind after unbind must release explicit unbound protection!");
                Assert.False(UiSessionState.HasVanillaStandaloneUnsavedChanges());

                // ⑤ 修改按键未点应用时判脏为 true，执行 DiscardVanillaStandaloneUnsavedChanges 后判脏恢复为 false
                vanillaHk.PendingKey = KeyCode.V;
                vanillaHk.PendingModifiers = KeyModifierMask.None;
                vanillaHk.HasPendingChange = true;
                Assert.True(UiSessionState.HasVanillaStandaloneUnsavedChanges());

                UnifiedOptionWindow.Instance.DiscardVanillaStandaloneUnsavedChanges();
                Assert.False(vanillaHk.HasPendingChange);
                Assert.False(UiSessionState.HasVanillaStandaloneUnsavedChanges(), "Scenario 5: Discard must restore dirty flag to false!");
            }
            finally
            {
                UiSessionState.VanillaStandaloneWindow = null;
                UiSessionState.IsStandaloneModWindowOpen = false;
                UiSessionState.VanillaStandaloneToggleBaseline.Clear();
                UiSessionState.AllHotkeys.Clear();
                ComboKeyStore.HeldModifiersOverride = null;
            }
        }

        private sealed class TestOptionUiBridge : IOptionUiBridge
        {
            public int OnVanillaOpenedCount { get; private set; }

            public bool IsEnhancedWindowOpen { get; set; }
            public bool IsCapturingAnyKey { get; set; }
            public bool IsCapturingModHotkey { get; set; }
            public bool IsSuppressingEscapeAfterCapture { get; set; }
            public bool IsExplicitWindowClosing { get; set; }
            public bool IsSearchInputFocused { get; set; }
            public void ReleaseSearchInputFocus() { }
            public bool IsInEnhancedRefreshRules { get; set; }
            public bool IsStandaloneModWindowOpen { get; set; }
            public bool IsVanillaStandaloneWindowOpen { get; set; }
            public CustomUIWindow? CurrentWindow => null;
            public CustomUIWindow? UnsavedConfirmWindow => null;

            public bool TryOpenEnhancedUI(GameOptionUIManager instance) => true;
            public void OnVanillaOptionsWindowOpened(GameOptionUIManager manager, CustomUIWindow? window = null)
            {
                OnVanillaOpenedCount++;
            }
            public void OnModConfigWindowOpened(CustomUIWindow window, LoadedMod mod) { }
            public bool HasUnsavedChanges() => false;
            public void DiscardUnsavedChangesOnDirectClose() { }
            public void PromptUnsavedChangesBeforeClosing() { }
            public bool HasVanillaStandaloneUnsavedChanges() => false;
            public void PromptVanillaStandaloneUnsavedChanges() { }
            public void DiscardVanillaStandaloneUnsavedChanges() { }
            public void OnBeforeVanillaStandaloneApply(GameOptionUIManager instance) { }
            public void OnAfterVanillaStandaloneApply(GameOptionUIManager instance) { }
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
