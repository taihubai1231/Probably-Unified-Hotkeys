#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 高性能纯静态按键检索匹配器（支持按键枚举名词元边界、别名、双语及拼音检索）
    /// </summary>
    public static class KeySearchMatcher
    {
        private static readonly object _scratchLock = new object();
        private static readonly List<string> _searchKeyTokensScratch = new List<string>(8);
        private static readonly List<string> _searchQueryTokensScratch = new List<string>(8);
        private static readonly StringBuilder _searchConcatScratch = new StringBuilder(32);

        /// <summary>
        /// 检查热键条目是否匹配查询关键词（支持功能名、分组、只读探测源及实际按键文本综合匹配）
        /// </summary>
        public static bool MatchesHotkeyBinding(
            ModHotkeyEntry hotkey,
            string query,
            Func<KeyModifierMask, KeyCode, string>? comboFormatter = null,
            Func<KeyCode, string>? primaryKeyFormatter = null)
        {
            if (hotkey == null) return false;
            if (string.IsNullOrWhiteSpace(query)) return true;

            if (PinyinEngine.Matches(hotkey.DisplayName, query))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(hotkey.SectionLabel) && PinyinEngine.Matches(hotkey.SectionLabel, query))
            {
                return true;
            }
            if (!string.IsNullOrEmpty(hotkey.Section) && PinyinEngine.Matches(hotkey.Section, query))
            {
                return true;
            }
            if (hotkey.IsReadOnlyProbed && !string.IsNullOrEmpty(hotkey.ProbedSourceDetail) &&
                PinyinEngine.Matches(hotkey.ProbedSourceDetail, query))
            {
                return true;
            }

            KeyCode effKey = hotkey.EffectiveKey;
            if (effKey == KeyCode.None)
            {
                string noneText = comboFormatter != null
                    ? comboFormatter(hotkey.EffectiveModifiers, effKey)
                    : BilingualText.Get("无", "None");
                return MatchesFormattedKeyText(noneText, query);
            }

            if (MatchesKeyCodeSearchToken(effKey, query))
            {
                return true;
            }

            string comboText = comboFormatter != null
                ? comboFormatter(hotkey.EffectiveModifiers, effKey)
                : (hotkey.EffectiveModifiers == KeyModifierMask.None
                    ? effKey.ToString()
                    : $"{hotkey.EffectiveModifiers} + {effKey}");

            if (MatchesFormattedKeyText(comboText, query))
            {
                return true;
            }

            if (hotkey.EffectiveModifiers != KeyModifierMask.None)
            {
                string primaryText = primaryKeyFormatter != null
                    ? primaryKeyFormatter(effKey)
                    : effKey.ToString();
                if (MatchesFormattedKeyText(primaryText, query))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 对格式化按键文本执行词元边界前缀/完整组合匹配（中文走拼音匹配，英文/数字禁止词中子串如 "ow"->"Arrow" / "on"->"None" 误命中）
        /// </summary>
        public static bool MatchesFormattedKeyText(string? keyText, string query)
        {
            if (string.IsNullOrWhiteSpace(keyText))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            string qTrim = query.Trim();
            bool keyHasNonAscii = false;
            StringBuilder? cjkOnly = null;
            for (int i = 0; i < keyText.Length; i++)
            {
                char ch = keyText[i];
                if (ch > 127)
                {
                    keyHasNonAscii = true;
                    cjkOnly ??= new StringBuilder(keyText.Length);
                    cjkOnly.Append(ch);
                }
            }

            if (keyHasNonAscii)
            {
                // 仅将非 ASCII/中文部分送入拼音子串匹配，防止 "Ctrl + 小键盘6" 中的 ASCII 词中子串误触发
                if (cjkOnly != null && PinyinEngine.Matches(cjkOnly.ToString(), qTrim))
                {
                    return true;
                }
                if (PinyinEngine.Matches(keyText, qTrim) && qTrim.IndexOf(' ') >= 0)
                {
                    return true;
                }
            }

            lock (_scratchLock)
            {
                TokenizeAsciiSearchTokens(keyText, _searchKeyTokensScratch);
                TokenizeAsciiSearchTokens(qTrim, _searchQueryTokensScratch);

                if (_searchKeyTokensScratch.Count == 0 || _searchQueryTokensScratch.Count == 0)
                {
                    return string.Equals(keyText.Trim(), qTrim, StringComparison.OrdinalIgnoreCase);
                }

                if (_searchQueryTokensScratch.Count == 1)
                {
                    string qt = _searchQueryTokensScratch[0];
                    for (int i = 0; i < _searchKeyTokensScratch.Count; i++)
                    {
                        string kt = _searchKeyTokensScratch[i];
                        if (qt.Length == 1)
                        {
                            if (string.Equals(kt, qt, StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }
                        else if (kt.StartsWith(qt, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }

                    // 允许如 "numpad6" / "downarrow" / "mouse1" 从词元起点连写命中 "Numpad 6" / "Down Arrow" / "Mouse 1"
                    if (qt.Length >= 2)
                    {
                        return MatchesConcatenatedFromTokenBoundary(_searchKeyTokensScratch, qt);
                    }
                    return false;
                }

                // 多词元组合查询（如 "ctrl+c" / "numpad 6" / "left ctrl"）
                int qCount = _searchQueryTokensScratch.Count;
                int kCount = _searchKeyTokensScratch.Count;
                for (int start = 0; start <= kCount - qCount; start++)
                {
                    bool matched = true;
                    for (int j = 0; j < qCount; j++)
                    {
                        string kt = _searchKeyTokensScratch[start + j];
                        string qt = _searchQueryTokensScratch[j];
                        bool isLast = j == qCount - 1;
                        bool ok = isLast && qt.Length >= 2
                            ? kt.StartsWith(qt, StringComparison.Ordinal)
                            : string.Equals(kt, qt, StringComparison.Ordinal);
                        if (!ok)
                        {
                            matched = false;
                            break;
                        }
                    }
                    if (matched)
                    {
                        return true;
                    }
                }

                string compactQuery = string.Concat(_searchQueryTokensScratch);
                return MatchesConcatenatedFromTokenBoundary(_searchKeyTokensScratch, compactQuery);
            }
        }

        private static bool MatchesConcatenatedFromTokenBoundary(List<string> kTokens, string compactQuery)
        {
            if (string.IsNullOrEmpty(compactQuery))
            {
                return false;
            }
            var sb = _searchConcatScratch;
            for (int start = 0; start < kTokens.Count; start++)
            {
                sb.Length = 0;
                for (int end = start; end < kTokens.Count; end++)
                {
                    sb.Append(kTokens[end]);
                    if (sb.Length >= compactQuery.Length)
                    {
                        break;
                    }
                }
                if (sb.Length >= compactQuery.Length)
                {
                    string joined = sb.ToString();
                    if (compactQuery.Length == 1
                        ? string.Equals(joined, compactQuery, StringComparison.Ordinal)
                        : joined.StartsWith(compactQuery, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void TokenizeAsciiSearchTokens(string text, List<string> output)
        {
            output.Clear();
            int len = text.Length;
            int tokenStart = -1;

            for (int i = 0; i < len; i++)
            {
                char ch = text[i];
                bool isAlpha = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
                bool isDigit = ch >= '0' && ch <= '9';
                bool isWordChar = isAlpha || isDigit;

                if (!isWordChar)
                {
                    if (tokenStart >= 0)
                    {
                        output.Add(text.Substring(tokenStart, i - tokenStart).ToLowerInvariant());
                        tokenStart = -1;
                    }
                    // 对孤立运算符号（如 "Numpad +" 的末尾 '+'），保留为词元；跳过组合键中的 " + "
                    if ((ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '.') &&
                        (i == 0 || char.IsWhiteSpace(text[i - 1])) &&
                        i == len - 1)
                    {
                        output.Add(ch.ToString());
                    }
                    continue;
                }

                if (tokenStart < 0)
                {
                    tokenStart = i;
                    continue;
                }

                char prev = text[i - 1];
                bool prevLower = prev >= 'a' && prev <= 'z';
                bool curUpper = ch >= 'A' && ch <= 'Z';
                bool prevDigit = prev >= '0' && prev <= '9';
                bool curDigit = ch >= '0' && ch <= '9';
                bool prevAlpha = (prev >= 'a' && prev <= 'z') || (prev >= 'A' && prev <= 'Z');
                bool curAlpha = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');

                // 拆分驼峰与字母/数字过渡边界（如 Keypad6 -> keypad, 6；Alpha1 -> alpha, 1；Mouse3 -> mouse, 3）
                if ((prevLower && curUpper) || (prevAlpha && curDigit) || (prevDigit && curAlpha))
                {
                    output.Add(text.Substring(tokenStart, i - tokenStart).ToLowerInvariant());
                    tokenStart = i;
                }
            }

            if (tokenStart >= 0)
            {
                output.Add(text.Substring(tokenStart, len - tokenStart).ToLowerInvariant());
            }
        }

        /// <summary>
        /// 按键枚举名词根前缀 / 常用别名精确匹配，避免 KeyCode.Keypad6 等原始枚举名被 "yp" 等词中子串误命中
        /// </summary>
        public static bool MatchesKeyCodeSearchToken(KeyCode key, string query)
        {
            if (key == KeyCode.None || string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            string q = query.Trim();

            // 1. 常用玩家可读别名（小键盘 / 侧键 / 修饰键简称）
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                int digit = (int)key - (int)KeyCode.Keypad0;
                if (IsQueryPrefixOrAlias(q, "keypad" + digit) ||
                    IsQueryPrefixOrAlias(q, "kp" + digit) ||
                    IsQueryPrefixOrAlias(q, "numpad" + digit) ||
                    IsQueryPrefixOrAlias(q, "numpad " + digit) ||
                    IsQueryPrefixOrAlias(q, "num" + digit) ||
                    IsQueryPrefixOrAlias(q, "pad" + digit) ||
                    IsQueryPrefixOrAlias(q, "pad " + digit) ||
                    IsQueryPrefixOrAlias(q, "小键盘" + digit) ||
                    IsQueryPrefixOrAlias(q, "xjp" + digit))
                {
                    return true;
                }
            }
            else if (key >= KeyCode.KeypadPeriod && key <= KeyCode.KeypadEquals)
            {
                if (IsQueryPrefixOrAlias(q, "keypad") ||
                    IsQueryPrefixOrAlias(q, "kp") ||
                    IsQueryPrefixOrAlias(q, "numpad") ||
                    IsQueryPrefixOrAlias(q, "pad") ||
                    IsQueryPrefixOrAlias(q, "小键盘") ||
                    IsQueryPrefixOrAlias(q, "xjp"))
                {
                    return true;
                }
            }
            else if (key >= KeyCode.Mouse3 && key <= KeyCode.Mouse6)
            {
                int sideIndex = (int)key - (int)KeyCode.Mouse3 + 1;
                if (IsQueryPrefixOrAlias(q, "mouse" + ((int)key - (int)KeyCode.Mouse0)) ||
                    IsQueryPrefixOrAlias(q, "mouse side " + sideIndex) ||
                    IsQueryPrefixOrAlias(q, "side " + sideIndex) ||
                    IsQueryPrefixOrAlias(q, "鼠标侧键" + sideIndex) ||
                    IsQueryPrefixOrAlias(q, "侧键" + sideIndex))
                {
                    return true;
                }
            }
            else if (key == KeyCode.LeftControl || key == KeyCode.RightControl)
            {
                if (IsQueryPrefixOrAlias(q, "ctrl") ||
                    IsQueryPrefixOrAlias(q, key == KeyCode.LeftControl ? "lctrl" : "rctrl"))
                {
                    return true;
                }
            }
            else if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
            {
                if (IsQueryPrefixOrAlias(q, "shift") ||
                    IsQueryPrefixOrAlias(q, key == KeyCode.LeftShift ? "lshift" : "rshift"))
                {
                    return true;
                }
            }
            else if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt)
            {
                if (IsQueryPrefixOrAlias(q, "alt") ||
                    IsQueryPrefixOrAlias(q, key == KeyCode.LeftAlt ? "lalt" : "ralt"))
                {
                    return true;
                }
            }

            // 2. 原始枚举名仅允许从开头或 PascalCase/数字词元起点匹配（>=2 字符允许前缀，单字符需精准匹配完整词元）
            string raw = key.ToString();
            if (q.Length >= 2 && raw.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int tokenStart = 0;
            for (int i = 1; i <= raw.Length; i++)
            {
                bool atBoundary = i == raw.Length ||
                                  char.IsUpper(raw[i]) ||
                                  (char.IsDigit(raw[i]) != char.IsDigit(raw[i - 1]));
                if (!atBoundary) continue;

                int tokenLen = i - tokenStart;
                if (tokenLen > 0)
                {
                    bool matched = q.Length >= 2
                        ? (tokenLen >= q.Length && string.Compare(raw, tokenStart, q, 0, q.Length, StringComparison.OrdinalIgnoreCase) == 0)
                        : (tokenLen == 1 && char.ToUpperInvariant(raw[tokenStart]) == char.ToUpperInvariant(q[0]));
                    if (matched)
                    {
                        return true;
                    }
                }
                tokenStart = i;
            }

            return false;
        }

        private static bool IsQueryPrefixOrAlias(string query, string alias)
        {
            if (query.Length >= 2)
            {
                return alias.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(query, alias, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(query, alias, StringComparison.OrdinalIgnoreCase);
        }
    }
}
