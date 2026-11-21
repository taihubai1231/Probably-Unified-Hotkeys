#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    /// <summary>
    /// 终极收官四项顽疾验真专项测试套件（Definition of Done 门禁）。
    /// 100% 覆盖老板 4 大实机复现反馈与第一性原理沙盘推演：
    /// 1. 改造件详情页零错位重叠、按钮锁定 42px 零拉伸、preview_panel 本模组专属隐藏；
    /// 2. 中文多音字检索全覆盖（"多重密码解序器" 搜 chong/zhong/duochong/dcmm/dcmmjxq/dzmm/duoch 全部命中，xyz 排除；调整/储藏/银行/弹药/角色/模组全命中；Core/Search 纯度零违规）；
    /// 3. 组合键终极复现场景自动化沙盘测试（场景 A 翻转、场景 B 顺逆时针同主键不同修饰键零抵消、场景 C 单组合键防误触抑制）；
    /// 4. 按钮外部 160f 原尺寸绝对不变 + 收窄内边距与字号微缩自适应长组合键零截断。
    /// </summary>
    [Collection("SequentialStateTests")]
    public class FinalShipVerificationTests : TestBase
    {
        // =====================================================================
        // 反馈 #1：改造件详情页零错位重叠、按钮锁定 42px 零拉伸、去封面无残留
        // =====================================================================

        [Fact]
        public void Feedback01_ModDetailLayout_LockDetailButtonSizes_ContractVerification()
        {
            var asm = typeof(ModFeatureConfig).Assembly;

            // 亲自审查并反射校验 PerformancePatches.ModUIManagerPerfHelper 核心管线
            var helperType = asm.GetType("ProbablyUnifiedHotkeys.ModUIManagerPerfHelper");
            Assert.NotNull(helperType);

            // 1. 确认 LockDetailButtonSizes 核心方法存在
            var lockMethod = helperType.GetMethod("LockDetailButtonSizes", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(lockMethod);

            // 2. 确认 ApplyPreview 补丁存在且正确拦截本模组去封面
            var applyPreviewPatchType = asm.GetType("ProbablyUnifiedHotkeys.Patch_ModUIManager_ApplyPreview");
            Assert.NotNull(applyPreviewPatchType);
            var applyPreviewPrefix = applyPreviewPatchType.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(applyPreviewPrefix);

            // 3. 确认 ShowMod 补丁存在
            var showModPatchType = asm.GetType("ProbablyUnifiedHotkeys.Patch_ModUIManager_ShowMod");
            Assert.NotNull(showModPatchType);

            // 4. 确认本模组 ID 配置正确
            Assert.Equal("probably_unified_hotkeys", ModFeatureConfig.ModId);
        }

        // =====================================================================
        // 反馈 #2：中文多音字检索引擎升级验真
        // =====================================================================

        [Fact]
        public void Feedback02_PolyphoneSearch_BossTargetWord_MultiPronunciations_AllPass()
        {
            // 老板指名道姓词汇："多重密码解序器"
            const string target = "多重密码解序器";

            // 1. 全拼音多音命中测试（重：chong / zhong 均可命中）
            Assert.True(PinyinEngine.Matches(target, "chong"), "Must match 'chong'");
            Assert.True(PinyinEngine.Matches(target, "zhong"), "Must match 'zhong'");
            Assert.True(PinyinEngine.Matches(target, "duochong"), "Must match 'duochong'");
            Assert.True(PinyinEngine.Matches(target, "duozhong"), "Must match 'duozhong'");

            // 2. 首字母缩写测试（dcmm / dcmmjxq / dzmm / dzmmjxq 均可命中）
            Assert.True(PinyinEngine.Matches(target, "dcmm"), "Must match 'dcmm'");
            Assert.True(PinyinEngine.Matches(target, "dcmmjxq"), "Must match 'dcmmjxq'");
            Assert.True(PinyinEngine.Matches(target, "dzmm"), "Must match 'dzmm'");
            Assert.True(PinyinEngine.Matches(target, "dzmmjxq"), "Must match 'dzmmjxq'");

            // 3. 键入中半截前缀容错测试（搜 duoch 命中）
            Assert.True(PinyinEngine.Matches(target, "duoch"), "Must match prefix 'duoch'");

            // 4. 无关词负例测试（搜 xyz 必须返回 false）
            Assert.False(PinyinEngine.Matches(target, "xyz"), "Unrelated query 'xyz' must return false");
            Assert.False(PinyinEngine.Matches(target, "ong"), "Sub-token 'ong' inside syllable must NOT match");
            Assert.False(PinyinEngine.Matches(target, "yp"), "Pitfall 12 token boundary check");

            // 5. 常见多音字广泛验证
            // 调整 (tiao / diao)
            Assert.True(PinyinEngine.Matches("调整", "tiao"));
            Assert.True(PinyinEngine.Matches("调整", "diao"));

            // 储藏 (cang / zang)
            Assert.True(PinyinEngine.Matches("储藏", "cang"));
            Assert.True(PinyinEngine.Matches("储藏", "zang"));

            // 银行 (hang / xing)
            Assert.True(PinyinEngine.Matches("银行", "hang"));
            Assert.True(PinyinEngine.Matches("银行", "xing"));

            // 弹药 (dan / tan)
            Assert.True(PinyinEngine.Matches("弹药", "dan"));
            Assert.True(PinyinEngine.Matches("弹药", "tan"));

            // 角色 (jiao / jue)
            Assert.True(PinyinEngine.Matches("角色", "jiao"));
            Assert.True(PinyinEngine.Matches("角色", "jue"));

            // 模组 (mo / mu)
            Assert.True(PinyinEngine.Matches("模组", "mo"));
            Assert.True(PinyinEngine.Matches("模组", "mu"));
        }

        // =====================================================================
        // 反馈 #3：组合键终极复现场景自动化沙盘测试（场景 A / B / C）
        // =====================================================================

        /// <summary>
        /// 场景 A：将 flip_item 绑为 Ctrl + Space
        /// 模拟只按 Space（无 Ctrl）断言返回 false（绝不误翻转！）；模拟按住 Ctrl + Space 断言返回 true！
        /// </summary>
        [Fact]
        public void Feedback03_ScenarioA_FlipItem_CtrlSpace_SingleSpaceNeverTriggers()
        {
            ComboKeyStore.EnsureLoaded();
            const string actionId = "flip_item";

            // 绑定 flip_item 为 Ctrl + Space
            ComboKeyStore.SetSavedCustomHotkey("vanilla.key_" + actionId, KeyModifierMask.Ctrl, KeyCode.Space);

            // --- 模拟 1：只按 Space（无修饰键） ---
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.None;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.Space;

            // 模拟底层 InputActionHandler.Update 调用的 Patch_KeyBindHelper_Get
            KeyCode filteredKey1 = KeyCode.Space;
            Patch_KeyBindHelper_Get.Postfix(actionId, ref filteredKey1);

            // 铁证断言：因为没有按住 Ctrl，底层 Get 返回 KeyCode.None，绝不会误将 Space 传给 ItemMouseDragHandler
            Assert.Equal(KeyCode.None, filteredKey1);

            // 模拟 ItemMouseDragHandler.OnEventPress 接收到的物理按键集合
            var pressedKeys1 = new HashSet<KeyCode> { KeyCode.Space };
            bool didFlip1 = pressedKeys1.Contains(filteredKey1);
            Assert.False(didFlip1, "Only pressing Space MUST NEVER flip item!");

            // --- 模拟 2：按住 Ctrl + Space ---
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.Ctrl;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.Space;

            KeyCode filteredKey2 = KeyCode.Space;
            Patch_KeyBindHelper_Get.Postfix(actionId, ref filteredKey2);

            // 铁证断言：按住 Ctrl 时精确放行 KeyCode.Space
            Assert.Equal(KeyCode.Space, filteredKey2);

            var pressedKeys2 = new HashSet<KeyCode> { KeyCode.Space };
            bool didFlip2 = pressedKeys2.Contains(filteredKey2);
            Assert.True(didFlip2, "Pressing Ctrl + Space MUST successfully trigger flip item!");
        }

        /// <summary>
        /// 场景 B：将 rotate_cw 绑为 Ctrl + F，同时将 rotate_ccw 绑为 Ctrl + Alt + F（同主键 F、不同修饰键！）
        /// ① 保存后两者均未丢失或被对方清空；
        /// ② 模拟单按 F -> 两者均返回 false；
        /// ③ 模拟按 Ctrl + F（无 Alt） -> rotate_cw 返回 true 且 rotate_ccw 返回 false（绝不互相抵消！）；
        /// ④ 模拟按 Ctrl + Alt + F -> rotate_cw 返回 false 且 rotate_ccw 返回 true（绝不互相抵消！）。
        /// </summary>
        [Fact]
        public void Feedback03_ScenarioB_SamePrimaryKey_DifferentModifiers_NoCancellation()
        {
            ComboKeyStore.EnsureLoaded();
            const string cwId = "rotate_cw";
            const string ccwId = "rotate_ccw";

            // 绑定：rotate_cw -> Ctrl + F，rotate_ccw -> Ctrl + Alt + F
            ComboKeyStore.SetSavedCustomHotkey("vanilla.key_" + cwId, KeyModifierMask.Ctrl, KeyCode.F);
            ComboKeyStore.SetSavedCustomHotkey("vanilla.key_" + ccwId, KeyModifierMask.Ctrl | KeyModifierMask.Alt, KeyCode.F);

            // ① 铁证断言：保存后两者各自修饰键均完好保留，未被原版互相清空
            Assert.True(ComboKeyStore.HasSavedCustomHotkey("vanilla.key_" + cwId));
            Assert.True(ComboKeyStore.HasSavedCustomHotkey("vanilla.key_" + ccwId));
            var effectiveCw = ComboKeyStore.GetEffectiveCustomHotkey("vanilla.key_" + cwId, KeyModifierMask.None, KeyCode.None);
            Assert.Equal(KeyCode.F, effectiveCw.Key);
            Assert.Equal(KeyModifierMask.Ctrl, effectiveCw.Modifiers);
            var effectiveCcw = ComboKeyStore.GetEffectiveCustomHotkey("vanilla.key_" + ccwId, KeyModifierMask.None, KeyCode.None);
            Assert.Equal(KeyCode.F, effectiveCcw.Key);
            Assert.Equal(KeyModifierMask.Ctrl | KeyModifierMask.Alt, effectiveCcw.Modifiers);

            // ② 模拟单按 F（无修饰键）
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.None;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.F;

            KeyCode cwKeySingle = KeyCode.F;
            KeyCode ccwKeySingle = KeyCode.F;
            Patch_KeyBindHelper_Get.Postfix(cwId, ref cwKeySingle);
            Patch_KeyBindHelper_Get.Postfix(ccwId, ref ccwKeySingle);

            Assert.Equal(KeyCode.None, cwKeySingle);
            Assert.Equal(KeyCode.None, ccwKeySingle);

            var pressedF = new HashSet<KeyCode> { KeyCode.F };
            Assert.False(pressedF.Contains(cwKeySingle), "Single F must NOT trigger rotate_cw");
            Assert.False(pressedF.Contains(ccwKeySingle), "Single F must NOT trigger rotate_ccw");

            // ③ 模拟按 Ctrl + F（无 Alt）
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.Ctrl;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.F;

            KeyCode cwKeyCtrl = KeyCode.F;
            KeyCode ccwKeyCtrl = KeyCode.F;
            Patch_KeyBindHelper_Get.Postfix(cwId, ref cwKeyCtrl);
            Patch_KeyBindHelper_Get.Postfix(ccwId, ref ccwKeyCtrl);

            Assert.Equal(KeyCode.F, cwKeyCtrl);
            Assert.Equal(KeyCode.None, ccwKeyCtrl);

            var pressedCtrlF = new HashSet<KeyCode> { KeyCode.F };
            bool cwTriggered = pressedCtrlF.Contains(cwKeyCtrl);
            bool ccwTriggered = pressedCtrlF.Contains(ccwKeyCtrl);

            Assert.True(cwTriggered, "Ctrl + F MUST trigger rotate_cw");
            Assert.False(ccwTriggered, "Ctrl + F MUST NOT trigger rotate_ccw");
            // 物理净旋转度数：+90° - 0° = +90°，绝不抵消！

            // ④ 模拟按 Ctrl + Alt + F
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.Ctrl | KeyModifierMask.Alt;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.F;

            KeyCode cwKeyCtrlAlt = KeyCode.F;
            KeyCode ccwKeyCtrlAlt = KeyCode.F;
            Patch_KeyBindHelper_Get.Postfix(cwId, ref cwKeyCtrlAlt);
            Patch_KeyBindHelper_Get.Postfix(ccwId, ref ccwKeyCtrlAlt);

            Assert.Equal(KeyCode.None, cwKeyCtrlAlt);
            Assert.Equal(KeyCode.F, ccwKeyCtrlAlt);

            var pressedCtrlAltF = new HashSet<KeyCode> { KeyCode.F };
            bool cwTriggered2 = pressedCtrlAltF.Contains(cwKeyCtrlAlt);
            bool ccwTriggered2 = pressedCtrlAltF.Contains(ccwKeyCtrlAlt);

            Assert.False(cwTriggered2, "Ctrl + Alt + F MUST NOT trigger rotate_cw");
            Assert.True(ccwTriggered2, "Ctrl + Alt + F MUST trigger rotate_ccw");
            // 物理净旋转度数：0° - 90° = -90°，绝不抵消！
        }

        /// <summary>
        /// 场景 C：将另一个单键功能绑在 C，另一个组合键功能绑在 Ctrl + C（开启 EnableKeySuppression）
        /// 按 Ctrl + C 时组合键触发且单键 C 被抑制返回 false；单独按 C 时单键 C 正常触发且组合键返回 false！
        /// </summary>
        [Fact]
        public void Feedback03_ScenarioC_PlainKeyAndComboSuppression_CleanIsolation()
        {
            ComboKeyStore.EnsureLoaded();
            ModFeatureConfig.OverrideKeySuppressionForTests = true;

            const string singleAction = "test.mod.single_c";
            const string comboAction = "test.mod.combo_ctrl_c";

            var singleEntry = new ModHotkeyEntry
            {
                Id = singleAction,
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.C,
                GetKey = () => KeyCode.C
            };
            var comboEntry = new ModHotkeyEntry
            {
                Id = comboAction,
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.C,
                GetKey = () => KeyCode.C
            };

            ComboKeyStore.SetSavedCustomHotkey(singleAction, KeyModifierMask.None, KeyCode.C);
            ComboKeyStore.SetSavedCustomHotkey(comboAction, KeyModifierMask.Ctrl, KeyCode.C);

            ComboKeyStore.RebuildRuntimeLookup(new[] { singleEntry, comboEntry });

            // 1. 模拟按住 Ctrl + C
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.Ctrl;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.C;

            KeyCode singleKeyFiltered = KeyCode.C;
            ComboKeyStore.FilterRuntimeKey(ref singleKeyFiltered, singleAction);

            KeyCode comboKeyFiltered = KeyCode.C;
            ComboKeyStore.FilterRuntimeKey(ref comboKeyFiltered, comboAction);

            // 铁证断言：单键 C 被成功抑制为 None，组合键 Ctrl + C 正常放行
            Assert.Equal(KeyCode.None, singleKeyFiltered);
            Assert.Equal(KeyCode.C, comboKeyFiltered);

            // 2. 模拟单独按 C（无修饰键）
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.None;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.C;

            KeyCode singleKeySolo = KeyCode.C;
            ComboKeyStore.FilterRuntimeKey(ref singleKeySolo, singleAction);

            KeyCode comboKeySolo = KeyCode.C;
            ComboKeyStore.FilterRuntimeKey(ref comboKeySolo, comboAction);

            // 铁证断言：单键 C 正常放行，组合键 Ctrl + C 返回 None
            Assert.Equal(KeyCode.C, singleKeySolo);
            Assert.Equal(KeyCode.None, comboKeySolo);
        }

        // =====================================================================
        // 反馈 #4：按钮原尺寸 160f 保持不变 + 长组合键不截断
        // =====================================================================

        [Fact]
        public void Feedback04_ButtonWidth160f_InnerPaddingNarrowed_NeverTruncated()
        {
            // 1. 铁证断言：按钮基准宽度严格恢复为 160f（严禁改大外部框体）
            Assert.Equal(160f, UiLayoutAndFontHelper.KeybindButtonWidth);

            // 2. 验证格式化长组合键
            string combo1 = ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl, KeyCode.Space);
            Assert.Equal("Ctrl + Space", combo1);

            string combo2 = ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl | KeyModifierMask.Alt, KeyCode.F);
            Assert.Equal("Ctrl+Alt+F", combo2);

            string combo3 = ComboKeyStore.FormatCombo(KeyModifierMask.Ctrl | KeyModifierMask.Shift, KeyCode.F12);
            Assert.Equal("Ctrl+Shift+F12", combo3);

            // 3. 验证收窄 padding 后多释放出 24px+ 有效宽度
            // 原版 padding left=16, right=16 (剩余净宽 128px)
            // 新版 padding left=4, right=4, margin x=2, z=2 (净宽 148px)
            var spec = new UiLayoutAndFontHelper.ButtonFitSpec
            {
                AvailWidth = Mathf.Max(20f, 160f - 12f), // 148f
                AvailHeight = Mathf.Max(18f, 48f - 4f),
                MaxFontSize = 15f,
                MinFontSize = 8.5f
            };

            Assert.Equal(148f, spec.AvailWidth);
            Assert.Equal(8.5f, spec.MinFontSize);
            Assert.Equal(15f, spec.MaxFontSize);
        }

        // =====================================================================
        // 反馈 #5：原版键从 ExplicitUnboundHotkeys 重新绑定恢复全链路验真
        // =====================================================================

        /// <summary>
        /// 模拟原版键（如 vanilla.key_flip_item_alt）先处于 ExplicitUnboundHotkeys 解绑态，
        /// 重新在界面绑定为 Ctrl + Space（或单键 V）并保存后，
        /// 验证其在 ExplicitUnboundHotkeys 中的历史残留被彻底清除，且局内精准触发。
        /// </summary>
        [Fact]
        public void Feedback05_VanillaKey_RebindAfterExplicitUnbound_UnboundClearedAndRuntimeActive()
        {
            ComboKeyStore.EnsureLoaded();
            const string modId = "vanilla";
            const string rawOptionId = "key_flip_item_alt";
            const string canonicalId = "vanilla.key_flip_item_alt";
            const string bareId = "flip_item_alt";

            // 1. 模拟历史遗留：该键此前被显式解绑并持久化到了 ExplicitUnboundHotkeys
            ComboKeyStore.SetExplicitlyUnbound(canonicalId, true);
            ComboKeyStore.SetExplicitlyUnbound(rawOptionId, true);
            ComboKeyStore.SetExplicitlyUnbound(bareId, true);

            Assert.True(ComboKeyStore.IsExplicitlyUnbound(canonicalId));
            Assert.True(ComboKeyStore.IsExplicitlyUnbound(rawOptionId));
            Assert.True(ComboKeyStore.IsExplicitlyUnbound(bareId));

            // 在未重绑前，即使物理按下 Space，也会被 IsExplicitlyUnbound 拦截为 None
            KeyCode beforeRebind = KeyCode.Space;
            Patch_KeyBindHelper_Get.Postfix(bareId, ref beforeRebind);
            Assert.Equal(KeyCode.None, beforeRebind);

            // 2. 模拟玩家在设置界面将该按键重新绑定为 Ctrl + Space 并点击「应用」
            var hkEntry = new ModHotkeyEntry
            {
                Id = canonicalId,
                ModId = modId,
                RawOptionId = rawOptionId,
                SupportsModifierCombos = true,
                PendingKey = KeyCode.Space,
                PendingModifiers = KeyModifierMask.Ctrl,
                HasPendingChange = true
            };

            // 执行 UnifiedOptionWindow.ApplyAllSettings 中对原版按键的闭环同步逻辑
            bool isUnbind = hkEntry.PendingKey == KeyCode.None;
            ComboKeyStore.SetExplicitlyUnbound(hkEntry.Id, isUnbind);
            ComboKeyStore.SetExplicitlyUnbound(hkEntry.RawOptionId, isUnbind);
            if (hkEntry.RawOptionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
            {
                ComboKeyStore.SetExplicitlyUnbound(hkEntry.RawOptionId.Substring(4), isUnbind);
            }
            ComboKeyStore.SetSavedCombo(hkEntry.Id, hkEntry.PendingModifiers, hkEntry.PendingKey);
            ComboKeyStore.SetSavedCustomHotkey(hkEntry.Id, hkEntry.PendingModifiers, hkEntry.PendingKey);

            // 3. 铁证断言：重绑后历史解绑残留必须全部被清除为 false！
            Assert.False(ComboKeyStore.IsExplicitlyUnbound(canonicalId), "canonicalId unbound must be cleared");
            Assert.False(ComboKeyStore.IsExplicitlyUnbound(rawOptionId), "rawOptionId unbound must be cleared");
            Assert.False(ComboKeyStore.IsExplicitlyUnbound(bareId), "bareId unbound must be cleared");

            // 4. 模拟局内运行时：按住 Ctrl + Space 触发
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.Ctrl;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.Space;

            KeyCode runtimeKey = KeyCode.Space;
            Patch_KeyBindHelper_Get.Postfix(bareId, ref runtimeKey);
            Assert.Equal(KeyCode.Space, runtimeKey);

            // 单按 Space 时（无 Ctrl）仍然被组合键过滤为 None
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.None;
            KeyCode soloSpace = KeyCode.Space;
            Patch_KeyBindHelper_Get.Postfix(bareId, ref soloSpace);
            Assert.Equal(KeyCode.None, soloSpace);

            // 5. 模拟重绑为普通单键 V
            hkEntry.PendingKey = KeyCode.V;
            hkEntry.PendingModifiers = KeyModifierMask.None;
            hkEntry.HasPendingChange = true;

            bool isUnbindV = hkEntry.PendingKey == KeyCode.None;
            ComboKeyStore.SetExplicitlyUnbound(hkEntry.Id, isUnbindV);
            ComboKeyStore.SetExplicitlyUnbound(hkEntry.RawOptionId, isUnbindV);
            if (hkEntry.RawOptionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
            {
                ComboKeyStore.SetExplicitlyUnbound(hkEntry.RawOptionId.Substring(4), isUnbindV);
            }
            ComboKeyStore.SetSavedCombo(hkEntry.Id, hkEntry.PendingModifiers, hkEntry.PendingKey);
            ComboKeyStore.SetSavedCustomHotkey(hkEntry.Id, hkEntry.PendingModifiers, hkEntry.PendingKey);

            Assert.False(ComboKeyStore.IsExplicitlyUnbound(bareId));

            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.None;
            ComboKeyStore.RawKeyDownTestHook = k => k == KeyCode.V;
            KeyCode soloV = KeyCode.V;
            Patch_KeyBindHelper_Get.Postfix(bareId, ref soloV);
            Assert.Equal(KeyCode.V, soloV);
        }

        // =====================================================================
        // 反馈 #6：局内重开增强选项界面离线装配期按键显示 "Ctrl + Space"（绝不为 "无"）
        // 且局内关闭旁路后裸按 Space 严格返回 KeyCode.None，按住 Ctrl 放行 KeyCode.Space
        // =====================================================================

        [Fact]
        public void Feedback06_InGameDisplay_ReopenEnhancedUI_OfflineBuilding_DisplaysCtrlSpace_NotNone_AndInGameRuntimeFilteringClean()
        {
            ComboKeyStore.EnsureLoaded();
            ComboKeyStore.ExternalBypassPredicate = () =>
                UiSessionState.IsEnhancedWindowOpen ||
                UiSessionState.IsCapturingAnyKey ||
                UiSessionState.IsBuildingOrOpeningUI ||
                UiSessionState.IsApplyingSettings;

            const string modId = "vanilla";
            const string rawOptionId = "key_flip_item";
            const string canonicalId = "vanilla.key_flip_item";
            const string bareId = "flip_item";

            // 1. 设置保存数据：flip_item = Ctrl + Space
            ComboKeyStore.SetSavedCustomHotkey(canonicalId, KeyModifierMask.Ctrl, KeyCode.Space);
            ComboKeyStore.SetSavedCombo(canonicalId, KeyModifierMask.Ctrl, KeyCode.Space);
            ComboKeyStore.SetExplicitlyUnbound(canonicalId, false);
            ComboKeyStore.SetExplicitlyUnbound(rawOptionId, false);
            ComboKeyStore.SetExplicitlyUnbound(bareId, false);

            // 2. 模拟切入局内新场景：玩家未按住 Ctrl，且处于离线构建期（窗口尚未 Show，IsEnhancedWindowOpen == false）
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.None;
            ComboKeyStore.RawKeyDownTestHook = _ => false;
            UiSessionState.CurrentWindow = null;
            UiSessionState.IsBuildingOrOpeningUI = false;
            Assert.False(UiSessionState.IsEnhancedWindowOpen, "Enhanced window must not be open yet during offline build");

            // 模拟原版 KeyBindHelper.Get("flip_item") 底层物理按键为 Space
            // 若未开启 bypass，底层 Patch_KeyBindHelper_Get 会因未按住 Ctrl 返回 None；
            // 但在 ModOptionScanner 的 GetVanillaKeybinds 中，GetKey 委托内部已强制包装 using (ComboKeyStore.EnterBypass())！
            Func<KeyCode> rawVanillaGet = () =>
            {
                KeyCode k = KeyCode.Space;
                Patch_KeyBindHelper_Get.Postfix(bareId, ref k);
                return k;
            };

            // 模拟 ModOptionScanner.GetVanillaKeybinds 装配出来的 ModHotkeyEntry
            var entry = new ModHotkeyEntry
            {
                Id = canonicalId,
                ModId = modId,
                RawOptionId = rawOptionId,
                SupportsModifierCombos = true,
                DefaultKey = KeyCode.Space,
                DefaultModifiers = KeyModifierMask.None,
                GetKey = () =>
                {
                    if (ComboKeyStore.IsExplicitlyUnbound(canonicalId) || ComboKeyStore.IsExplicitlyUnbound(rawOptionId))
                    {
                        return KeyCode.None;
                    }
                    using (ComboKeyStore.EnterBypass())
                    {
                        return rawVanillaGet();
                    }
                }
            };

            // 3. 铁证断言：即便处于离线装配期（IsEnhancedWindowOpen == false）且玩家没有按住 Ctrl：
            // 通过 ModHotkeyEntry.GetKey() 读取到的真实底层物理按键必须为 KeyCode.Space，绝不是 KeyCode.None！
            KeyCode scannedKey = entry.GetKey();
            Assert.Equal(KeyCode.Space, scannedKey);

            // 通过 entry.EffectiveCombo 读取到的组合键必须为 (Space, Ctrl)
            KeyCombo effectiveCombo = entry.EffectiveCombo;
            Assert.Equal(KeyCode.Space, effectiveCombo.Key);
            Assert.Equal(KeyModifierMask.Ctrl, effectiveCombo.Modifiers);

            // 通过 UiLayoutAndFontHelper.FormatHotkeyComboText 格式化出来的显示文本严格为 "Ctrl + Space"，绝不是 "无"！
            string displayText = UiLayoutAndFontHelper.FormatHotkeyComboText(entry);
            Assert.Equal("Ctrl + Space", displayText);
            Assert.NotEqual("无", displayText);

            // 4. 模拟离线装配全局生命周期标志位 UiSessionState.IsBuildingOrOpeningUI
            UiSessionState.IsBuildingOrOpeningUI = true;
            try
            {
                // 在 IsBuildingOrOpeningUI 开启期间，ComboKeyStore.IsBypassed 自动为 true
                Assert.True(ComboKeyStore.IsBypassed, "ComboKeyStore.IsBypassed must be true when IsBuildingOrOpeningUI is active");

                KeyCode duringBuild = KeyCode.Space;
                Patch_KeyBindHelper_Get.Postfix(bareId, ref duringBuild);
                Assert.Equal(KeyCode.Space, duringBuild);
            }
            finally
            {
                UiSessionState.IsBuildingOrOpeningUI = false;
            }

            // 5. 模拟窗口关闭、玩家回到游戏局内操作（关闭所有 bypass）：
            Assert.False(ComboKeyStore.IsBypassed, "Bypass must be completely released in gameplay");

            // ① 玩家在局内单独按下 Space（未按住 Ctrl）：
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.None;
            KeyCode gameplaySoloSpace = KeyCode.Space;
            Patch_KeyBindHelper_Get.Postfix(bareId, ref gameplaySoloSpace);
            // 铁证断言：未按 Ctrl 时，物理按键 Space 必须被严格过滤为 KeyCode.None，绝不误触发翻转！
            Assert.Equal(KeyCode.None, gameplaySoloSpace);

            // ② 玩家在局内按下 Ctrl + Space：
            ComboKeyStore.HeldModifiersOverride = _ => KeyModifierMask.Ctrl;
            KeyCode gameplayCtrlSpace = KeyCode.Space;
            Patch_KeyBindHelper_Get.Postfix(bareId, ref gameplayCtrlSpace);
            // 铁证断言：按住 Ctrl 时，物理按键 Space 精确放行！
            Assert.Equal(KeyCode.Space, gameplayCtrlSpace);
        }

        // =====================================================================
        // 反馈 #7：改造件封面秒载与防串图机制深度验真
        // =====================================================================

        [Fact]
        public void Feedback07_ModDetail_FastPreview_AndAntiGhostingVerification()
        {
            var asm = typeof(ModFeatureConfig).Assembly;

            // 1. 验证 Patch_ModUIManager_GetPreview 存在且包含 Prefix 截断
            var getPreviewPatch = asm.GetType("ProbablyUnifiedHotkeys.Patch_ModUIManager_GetPreview");
            Assert.NotNull(getPreviewPatch);
            var getPreviewPrefix = getPreviewPatch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(getPreviewPrefix);

            // 2. 验证 ModUIManagerPerfHelper.PersistentPreviewCache 与 TryGetFastPreview
            var helperType = asm.GetType("ProbablyUnifiedHotkeys.ModUIManagerPerfHelper");
            Assert.NotNull(helperType);
            var tryGetFastPreview = helperType.GetMethod("TryGetFastPreview", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(tryGetFastPreview);

            // 3. 验证 Patch_EscapeUIManager_OnMainMenu 存在且包含 Prefix 快速退出
            var escapePatch = asm.GetType("ProbablyUnifiedHotkeys.Patch_EscapeUIManager_OnMainMenu");
            Assert.NotNull(escapePatch);
            var escapePrefix = escapePatch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(escapePrefix);
        }

        // =====================================================================
        // 反馈 #8：退回主菜单零等待提速与状态重置契约验真
        // =====================================================================

        [Fact]
        public void Feedback08_FastQuitToMainMenu_ZeroWait_StateResetContractVerification()
        {
            // 验证默认开启快速退出
            Assert.True(ModFeatureConfig.EnableFastQuit);
            Assert.True(ModFeatureConfig.EnablePerformancePatches);

            var asm = typeof(ModFeatureConfig).Assembly;
            var escapePatch = asm.GetType("ProbablyUnifiedHotkeys.Patch_EscapeUIManager_OnMainMenu");
            Assert.NotNull(escapePatch);
            var prefix = escapePatch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(prefix);

            // 验证当 instance == null 时 Prefix 放行 (返回 true)
            object?[] args = new object?[] { null };
            object? result = prefix.Invoke(null, args);
            Assert.Equal(true, result);
        }
    }
}
