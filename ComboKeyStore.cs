using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    [Flags]
    public enum KeyModifierMask
    {
        None = 0,
        Shift = 1,
        Ctrl = 2,
        Alt = 4
    }

    public struct KeyCombo : IEquatable<KeyCombo>
    {
        public KeyModifierMask Modifiers;
        public KeyCode Key;

        public KeyCombo(KeyModifierMask modifiers, KeyCode key)
        {
            if (key == KeyCode.None)
            {
                Modifiers = KeyModifierMask.None;
                Key = KeyCode.None;
            }
            else
            {
                Modifiers = modifiers & ~ComboKeyStore.GetModifierBit(key);
                Key = key;
            }
        }

        public bool IsEmpty => Key == KeyCode.None;

        public bool Equals(KeyCombo other) => Modifiers == other.Modifiers && Key == other.Key;
        public override bool Equals(object? obj) => obj is KeyCombo other && Equals(other);
        public override int GetHashCode() => ((int)Modifiers * 397) ^ (int)Key;
        public override string ToString() => ComboKeyStore.FormatCombo(Modifiers, Key);
    }

    /// <summary>
    /// 组合键（Shift / Ctrl / Alt + 主键）与鼠标侧键（Mouse3~Mouse6）管理及运行时分派核心
    /// </summary>
    public static class ComboKeyStore
    {
        private const string PrefsKey = "probably_unified_hotkeys.combos.v1";

        private static readonly Dictionary<string, (KeyModifierMask Modifiers, KeyCode Key)> SavedCombos
            = new Dictionary<string, (KeyModifierMask, KeyCode)>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<KeyCode, List<KeyModifierMask>> ActiveCombosByKey
            = new Dictionary<KeyCode, List<KeyModifierMask>>();

        private static bool _loaded = false;
        private static int _bypassDepth = 0;

        public static bool IsBypassed =>
            _bypassDepth > 0 ||
            UnifiedOptionUI.IsEnhancedWindowOpen ||
            KeyBindHelper.IsCapturing ||
            UnifiedOptionUI.IsCapturingModHotkey;

        public static IDisposable EnterBypass()
        {
            _bypassDepth++;
            return new BypassScope();
        }

        private sealed class BypassScope : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_bypassDepth > 0) _bypassDepth--;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Load();
        }

        public static void Load()
        {
            SavedCombos.Clear();
            try
            {
                string raw = PlayerPrefs.GetString(PrefsKey, string.Empty);
                if (!string.IsNullOrEmpty(raw))
                {
                    string[] parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string part in parts)
                    {
                        int eq = part.IndexOf('=');
                        if (eq <= 0) continue;

                        string id = Uri.UnescapeDataString(part.Substring(0, eq));
                        string val = part.Substring(eq + 1);
                        int colon = val.IndexOf(':');
                        if (colon > 0)
                        {
                            if (int.TryParse(val.Substring(0, colon), out int maskInt) &&
                                int.TryParse(val.Substring(colon + 1), out int keyInt))
                            {
                                var mask = (KeyModifierMask)(maskInt & 7);
                                var kc = (KeyCode)keyInt;
                                if (mask != KeyModifierMask.None && kc != KeyCode.None)
                                {
                                    SavedCombos[id] = (mask, kc);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error loading combo store: {ex.Message}");
            }

            RebuildRuntimeFromSaved();
        }

        public static void Save()
        {
            try
            {
                if (SavedCombos.Count == 0)
                {
                    PlayerPrefs.DeleteKey(PrefsKey);
                }
                else
                {
                    var sb = new StringBuilder(256);
                    foreach (var kvp in SavedCombos)
                    {
                        if (kvp.Value.Modifiers == KeyModifierMask.None || kvp.Value.Key == KeyCode.None) continue;
                        sb.Append(Uri.EscapeDataString(kvp.Key))
                          .Append('=')
                          .Append((int)kvp.Value.Modifiers)
                          .Append(':')
                          .Append((int)kvp.Value.Key)
                          .Append(';');
                    }
                    string encoded = sb.ToString();
                    if (string.IsNullOrEmpty(encoded)) PlayerPrefs.DeleteKey(PrefsKey);
                    else PlayerPrefs.SetString(PrefsKey, encoded);
                }
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error saving combo store: {ex.Message}");
            }
        }

        public static KeyModifierMask GetSavedModifiers(string entryId, KeyCode expectedPrimaryKey = KeyCode.None)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(entryId)) return KeyModifierMask.None;
            if (!SavedCombos.TryGetValue(entryId, out var pair)) return KeyModifierMask.None;
            if (expectedPrimaryKey != KeyCode.None && pair.Key != expectedPrimaryKey)
            {
                return KeyModifierMask.None;
            }
            return pair.Modifiers;
        }

        public static void SetSavedCombo(string entryId, KeyModifierMask modifiers, KeyCode primaryKey)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(entryId)) return;

            var normalized = primaryKey == KeyCode.None
                ? KeyModifierMask.None
                : (modifiers & ~GetModifierBit(primaryKey));

            if (normalized == KeyModifierMask.None || primaryKey == KeyCode.None)
            {
                SavedCombos.Remove(entryId);
            }
            else
            {
                SavedCombos[entryId] = (normalized, primaryKey);
            }
        }

        public static void RebuildRuntimeLookup(IEnumerable<ModHotkeyEntry>? allHotkeys)
        {
            ActiveCombosByKey.Clear();

            if (allHotkeys != null)
            {
                foreach (var hk in allHotkeys)
                {
                    var mods = hk.EffectiveModifiers;
                    var key = hk.EffectiveKey;
                    if (key == KeyCode.None || mods == KeyModifierMask.None) continue;
                    RegisterActiveCombo(mods, key);
                }
                return;
            }

            RebuildRuntimeFromSaved();
        }

        private static void RebuildRuntimeFromSaved()
        {
            ActiveCombosByKey.Clear();
            foreach (var kvp in SavedCombos)
            {
                if (kvp.Value.Modifiers != KeyModifierMask.None && kvp.Value.Key != KeyCode.None)
                {
                    RegisterActiveCombo(kvp.Value.Modifiers, kvp.Value.Key);
                }
            }
        }

        private static void RegisterActiveCombo(KeyModifierMask mods, KeyCode key)
        {
            var normalized = mods & ~GetModifierBit(key);
            if (normalized == KeyModifierMask.None || key == KeyCode.None) return;

            if (!ActiveCombosByKey.TryGetValue(key, out var list))
            {
                list = new List<KeyModifierMask>(2);
                ActiveCombosByKey[key] = list;
            }
            if (!list.Contains(normalized))
            {
                list.Add(normalized);
            }
        }

        public static bool IsModifierKey(KeyCode k)
        {
            return k == KeyCode.LeftShift || k == KeyCode.RightShift ||
                   k == KeyCode.LeftControl || k == KeyCode.RightControl ||
                   k == KeyCode.LeftAlt || k == KeyCode.RightAlt;
        }

        public static KeyModifierMask GetModifierBit(KeyCode k)
        {
            if (k == KeyCode.LeftShift || k == KeyCode.RightShift) return KeyModifierMask.Shift;
            if (k == KeyCode.LeftControl || k == KeyCode.RightControl) return KeyModifierMask.Ctrl;
            if (k == KeyCode.LeftAlt || k == KeyCode.RightAlt) return KeyModifierMask.Alt;
            return KeyModifierMask.None;
        }

        public static bool IsMouseSideButton(KeyCode k)
        {
            return k >= KeyCode.Mouse3 && k <= KeyCode.Mouse6;
        }

        public static KeyModifierMask GetHeldModifiersMask(KeyCode excludeModifierKey = KeyCode.None)
        {
            KeyModifierMask mask = KeyModifierMask.None;

            bool shift = (excludeModifierKey != KeyCode.LeftShift && Input.GetKey(KeyCode.LeftShift)) ||
                         (excludeModifierKey != KeyCode.RightShift && Input.GetKey(KeyCode.RightShift));
            bool ctrl = (excludeModifierKey != KeyCode.LeftControl && Input.GetKey(KeyCode.LeftControl)) ||
                        (excludeModifierKey != KeyCode.RightControl && Input.GetKey(KeyCode.RightControl));
            bool alt = (excludeModifierKey != KeyCode.LeftAlt && Input.GetKey(KeyCode.LeftAlt)) ||
                       (excludeModifierKey != KeyCode.RightAlt && Input.GetKey(KeyCode.RightAlt));

            if (shift) mask |= KeyModifierMask.Shift;
            if (ctrl) mask |= KeyModifierMask.Ctrl;
            if (alt) mask |= KeyModifierMask.Alt;

            return mask;
        }

        public static bool AreModifiersExactMatch(KeyModifierMask required, KeyCode primaryKey)
        {
            var normalizedReq = required & ~GetModifierBit(primaryKey);
            return GetHeldModifiersMask(IsModifierKey(primaryKey) ? primaryKey : KeyCode.None) == normalizedReq;
        }

        public static bool ShouldSuppressPlainKey(KeyCode primaryKey)
        {
            if (primaryKey == KeyCode.None || ActiveCombosByKey.Count == 0) return false;
            if (!ActiveCombosByKey.TryGetValue(primaryKey, out var list)) return false;

            var held = GetHeldModifiersMask(IsModifierKey(primaryKey) ? primaryKey : KeyCode.None);
            return held != KeyModifierMask.None && list.Contains(held);
        }

        public static bool IsKeyDownRaw(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6)
            {
                int btn = (int)k - (int)KeyCode.Mouse0;
                return Input.GetMouseButtonDown(btn) || Input.GetKeyDown(k);
            }
            return Input.GetKeyDown(k);
        }

        public static bool IsKeyHeldRaw(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6)
            {
                int btn = (int)k - (int)KeyCode.Mouse0;
                return Input.GetMouseButton(btn) || Input.GetKey(k);
            }
            return Input.GetKey(k);
        }

        /// <summary>
        /// 在游戏运行时过滤第三方模组读取到的 KeyCode：
        /// 若设置了组合键修饰符，仅在修饰符精确按下时返回真实主键，否则返回 KeyCode.None；
        /// 若未设置修饰符但当前按下了与其它已绑定组合键一致的修饰键，同样抑制单键防止误触。
        /// </summary>
        public static void FilterRuntimeKey(ref KeyCode key, params string[] candidateEntryIds)
        {
            FilterRuntimeKeyCore(ref key, candidateEntryIds, null);
        }

        /// <summary>
        /// 按模组 ID 前缀兜底过滤（用于存在不同内部 Entry ID 命名的独立模组）
        /// </summary>
        public static void FilterRuntimeKeyByModPrefix(ref KeyCode key, string modIdPrefix)
        {
            FilterRuntimeKeyCore(ref key, null, modIdPrefix);
        }

        /// <summary>
        /// 优先按精确 Entry ID 过滤，未命中时再回落按模组 ID 前缀匹配过滤
        /// </summary>
        public static void FilterRuntimeKeyWithPrefix(ref KeyCode key, string modIdPrefix, params string[] candidateEntryIds)
        {
            FilterRuntimeKeyCore(ref key, candidateEntryIds, modIdPrefix);
        }

        private static void FilterRuntimeKeyCore(ref KeyCode key, string[]? candidateEntryIds, string? modIdPrefix)
        {
            if (key == KeyCode.None || IsBypassed) return;
            EnsureLoaded();

            KeyModifierMask required = KeyModifierMask.None;
            if (candidateEntryIds != null)
            {
                foreach (string id in candidateEntryIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (SavedCombos.TryGetValue(id, out var pair) && pair.Key == key)
                    {
                        required = pair.Modifiers;
                        break;
                    }
                }
            }

            if (required == KeyModifierMask.None && !string.IsNullOrEmpty(modIdPrefix))
            {
                foreach (var kvp in SavedCombos)
                {
                    if (kvp.Key.StartsWith(modIdPrefix, StringComparison.OrdinalIgnoreCase) && kvp.Value.Key == key)
                    {
                        required = kvp.Value.Modifiers;
                        break;
                    }
                }
            }

            if (required != KeyModifierMask.None)
            {
                if (!AreModifiersExactMatch(required, key))
                {
                    key = KeyCode.None;
                }
            }
            else if (ShouldSuppressPlainKey(key))
            {
                key = KeyCode.None;
            }
        }

        public static string FormatModifiers(KeyModifierMask mask)
        {
            if (mask == KeyModifierMask.None) return string.Empty;
            var parts = new List<string>(3);
            if ((mask & KeyModifierMask.Ctrl) != 0) parts.Add("Ctrl");
            if ((mask & KeyModifierMask.Shift) != 0) parts.Add("Shift");
            if ((mask & KeyModifierMask.Alt) != 0) parts.Add("Alt");
            return string.Join("+", parts);
        }

        public static string FormatPrimaryKeyName(KeyCode key)
        {
            if (key == KeyCode.None) return KeyBindHelper.KeyName(KeyCode.None);

            switch (key)
            {
                case KeyCode.Mouse0: return "左键";
                case KeyCode.Mouse1: return "右键";
                case KeyCode.Mouse2: return "中键";
                case KeyCode.Mouse3: return "鼠标侧键1";
                case KeyCode.Mouse4: return "鼠标侧键2";
                case KeyCode.Mouse5: return "鼠标侧键3";
                case KeyCode.Mouse6: return "鼠标侧键4";
                default: return KeyBindHelper.KeyName(key);
            }
        }

        public static string FormatCombo(KeyModifierMask modifiers, KeyCode key)
        {
            if (key == KeyCode.None) return KeyBindHelper.KeyName(KeyCode.None);

            var effectiveMods = modifiers & ~GetModifierBit(key);
            string keyName = FormatPrimaryKeyName(key);
            if (effectiveMods == KeyModifierMask.None)
            {
                return keyName;
            }

            int bitCount = 0;
            if ((effectiveMods & KeyModifierMask.Ctrl) != 0) bitCount++;
            if ((effectiveMods & KeyModifierMask.Shift) != 0) bitCount++;
            if ((effectiveMods & KeyModifierMask.Alt) != 0) bitCount++;

            string modStr = FormatModifiers(effectiveMods);
            return bitCount == 1 ? $"{modStr} + {keyName}" : $"{modStr}+{keyName}";
        }
    }
}
