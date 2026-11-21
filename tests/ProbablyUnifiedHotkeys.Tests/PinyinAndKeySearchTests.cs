using UnityEngine;
using Xunit;

namespace ProbablyUnifiedHotkeys.Tests
{
    [Collection("SequentialStateTests")]
    public class PinyinAndKeySearchTests : TestBase
    {
        [Fact]
        public void EmbeddedPinyinResource_LoadedSuccessfully_FullCJKCoverage()
        {
            // Verify embedded pinyin.bin 20,902 characters loaded successfully into memory
            Assert.True(PinyinEngine.IsResourceLoadedForTest, "Embedded pinyin.bin resource must be loaded successfully rather than degraded array.");

            // Verify basic pinyin lookups
            Assert.Equal("kai", PinyinEngine.GetPinyin('开'));
            Assert.Equal("guan", PinyinEngine.GetPinyin('关'));
            Assert.Equal('k', PinyinEngine.GetInitial('开'));
            Assert.Equal('g', PinyinEngine.GetInitial('关'));
        }

        [Fact]
        public void TraditionalAndSimplified_MutualSearch()
        {
            // Simplified matches Traditional query and vice-versa
            Assert.True(PinyinEngine.Matches("开关", "開關"));
            Assert.True(PinyinEngine.Matches("開關", "开关"));

            // Pinyin initials and full pinyin matching
            Assert.True(PinyinEngine.Matches("开关", "kg"));
            Assert.True(PinyinEngine.Matches("開關", "kaiguan"));
            Assert.True(PinyinEngine.Matches("商品仓库", "sp"));
            Assert.True(PinyinEngine.Matches("道具倉庫", "daoju"));
            Assert.True(PinyinEngine.Matches("道具仓库", "ck"));

            // Character conversion
            Assert.Equal('关', PinyinEngine.MapToSimplified('關'));
            Assert.Equal('门', PinyinEngine.MapToSimplified('門'));
            Assert.Equal("深空当铺开关", PinyinEngine.ToSimplified("深空當鋪開關"));
        }

        [Fact]
        public void TokenBoundary_NoFalsePositives_LockingPitfallsRedline()
        {
            // PITFALLS.md Pitfall 12: prevent substring matching inside token without boundary!
            // 1. "yp" MUST NEVER match Keypad6 ("yp" is inside "Keypad")
            Assert.False(
                KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Keypad6, "yp"),
                "Search token 'yp' MUST NOT match KeyCode.Keypad6!"
            );

            // 2. "ow" MUST NEVER match UpArrow ("ow" is inside "Arrow")
            Assert.False(
                KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.UpArrow, "ow"),
                "Search token 'ow' MUST NOT match KeyCode.UpArrow!"
            );

            // 3. "on" MUST NEVER match None ("on" is inside "None")
            Assert.False(
                KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.None, "on"),
                "Search token 'on' MUST NOT match KeyCode.None!"
            );

            // 4. Valid token boundary / alias matches MUST succeed
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Keypad6, "pad6"));
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Keypad6, "kp6"));
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Keypad6, "numpad6"));
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Keypad6, "小键盘6"));
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Keypad6, "xjp6"));

            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Mouse4, "mouse4"));
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Mouse4, "侧键2"));
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.Mouse4, "鼠标侧键2"));

            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.F12, "f12"));
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.LeftControl, "ctrl"));
            Assert.True(KeySearchMatcher.MatchesKeyCodeSearchToken(KeyCode.LeftShift, "shift"));
        }

        [Fact]
        public void FormattedKeyTextAndHotkeyBinding_Matching()
        {
            // Formatted key text matching
            Assert.True(KeySearchMatcher.MatchesFormattedKeyText("Ctrl + E", "ctrl"));
            Assert.True(KeySearchMatcher.MatchesFormattedKeyText("Ctrl + E", "e"));
            Assert.True(KeySearchMatcher.MatchesFormattedKeyText("Ctrl + E", "ctrl+e"));
            Assert.False(KeySearchMatcher.MatchesFormattedKeyText("Ctrl + E", "x"));

            // Token boundary check on formatted key text: "ow" should not match "Up Arrow"
            Assert.False(KeySearchMatcher.MatchesFormattedKeyText("Up Arrow", "ow"));
            Assert.True(KeySearchMatcher.MatchesFormattedKeyText("Up Arrow", "arrow"));
            Assert.True(KeySearchMatcher.MatchesFormattedKeyText("Up Arrow", "up"));

            // Hotkey binding comprehensive search
            var hotkey = new ModHotkeyEntry
            {
                Id = "test.mod.rotate_fast",
                DisplayName = "快速旋转",
                SectionLabel = "战斗视角",
                SupportsModifierCombos = true,
                GetKey = () => KeyCode.Keypad6
            };

            // Matches by DisplayName
            Assert.True(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "快速"));
            Assert.True(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "ks"));
            Assert.True(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "xuanzhuan"));

            // Matches by Section
            Assert.True(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "战斗"));
            Assert.True(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "zd"));

            // Matches by Key alias
            Assert.True(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "pad6"));
            Assert.True(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "小键盘6"));

            // False positive prevention
            Assert.False(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "yp"));
            Assert.False(KeySearchMatcher.MatchesHotkeyBinding(hotkey, "背包"));
        }

        [Fact]
        public void PolyphoneSearch_MultiPronunciation_FullAndInitialAndMixedCoverage()
        {
            // PolyphoneTable capacity check: at least 160+ polyphones covered
            Assert.True(PolyphoneTable.Count >= 160, $"PolyphoneTable should contain at least 160 polyphones, actual: {PolyphoneTable.Count}");

            // Boss Issue #2: "多重密码解序器" search by "chong", "zhong", "duochong", "dcmm", "dcmmjxq", "duoch", etc.
            string target = "多重密码解序器";

            // Full pinyin matches with polyphone
            Assert.True(PinyinEngine.Matches(target, "chong"), "Must match 'chong' for '多重密码解序器'");
            Assert.True(PinyinEngine.Matches(target, "zhong"), "Must match 'zhong' for '多重密码解序器'");
            Assert.True(PinyinEngine.Matches(target, "duochong"), "Must match 'duochong' for '多重密码解序器'");
            Assert.True(PinyinEngine.Matches(target, "duozhong"), "Must match 'duozhong' for '多重密码解序器'");

            // Initial acronym matches with polyphone
            Assert.True(PinyinEngine.Matches(target, "dcmm"), "Must match 'dcmm' (duo-chong-mi-ma)");
            Assert.True(PinyinEngine.Matches(target, "dzmm"), "Must match 'dzmm' (duo-zhong-mi-ma)");
            Assert.True(PinyinEngine.Matches(target, "dcmmjxq"), "Must match 'dcmmjxq' full initials");
            Assert.True(PinyinEngine.Matches(target, "dzmmjxq"), "Must match 'dzmmjxq' full initials");

            // Mixed initial + full pinyin
            Assert.True(PinyinEngine.Matches(target, "dchong"), "Must match mixed 'd' + 'chong'");
            Assert.True(PinyinEngine.Matches(target, "duocmm"), "Must match mixed 'duo' + 'c' + 'm' + 'm'");

            // Incomplete / prefix syllable matches
            Assert.True(PinyinEngine.Matches(target, "duoch"), "Must match prefix 'duoch'");
            Assert.True(PinyinEngine.Matches(target, "jxq"), "Must match substring initials 'jxq'");

            // Negative tests: middle substring inside syllable must NOT match (Pitfall 12)
            Assert.False(PinyinEngine.Matches(target, "ong"), "Substring 'ong' inside 'chong' must NOT match");
            Assert.False(PinyinEngine.Matches(target, "hon"), "Substring 'hon' inside 'chong' must NOT match");
            Assert.False(PinyinEngine.Matches(target, "yp"), "Token boundary check false positive prevention");

            // Other key polyphones check
            Assert.True(PinyinEngine.Matches("调整参数", "tiao"));
            Assert.True(PinyinEngine.Matches("调整参数", "diao"));
            Assert.True(PinyinEngine.Matches("调整参数", "tzcs"));
            Assert.True(PinyinEngine.Matches("调整参数", "dzcs"));

            Assert.True(PinyinEngine.Matches("银行账目", "hang"));
            Assert.True(PinyinEngine.Matches("银行账目", "xing"));
            Assert.True(PinyinEngine.Matches("银行账目", "yhzm"));
            Assert.True(PinyinEngine.Matches("银行账目", "yxzm"));

            Assert.True(PinyinEngine.Matches("无限子弹", "dan"));
            Assert.True(PinyinEngine.Matches("无限子弹", "tan"));

            Assert.True(PinyinEngine.Matches("长按切换", "chang"));
            Assert.True(PinyinEngine.Matches("长按切换", "zhang"));

            Assert.True(PinyinEngine.Matches("外壳装甲", "ke"));
            Assert.True(PinyinEngine.Matches("外壳装甲", "qiao"));

            Assert.True(PinyinEngine.Matches("模组设置", "mo"));
            Assert.True(PinyinEngine.Matches("模组设置", "mu"));

            Assert.True(PinyinEngine.Matches("降低延迟", "jiang"));
            Assert.True(PinyinEngine.Matches("降低延迟", "xiang"));

            // Traditional Chinese polyphone matching
            Assert.True(PinyinEngine.Matches("多重密碼解序器", "chong"));
            Assert.True(PinyinEngine.Matches("多重密碼解序器", "dcmm"));
        }
    }
}
