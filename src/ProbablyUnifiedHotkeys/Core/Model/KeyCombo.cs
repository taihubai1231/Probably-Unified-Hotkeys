using System;
using System.Collections.Generic;
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

    /// <summary>
    /// 组合键纯值类型（主键 + 修饰键掩码）：强制维护空键与修饰位互斥契约，零 UI 依赖。
    /// </summary>
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
                Modifiers = modifiers & ~GetModifierBit(key);
                Key = key;
            }
        }

        public bool IsEmpty => Key == KeyCode.None;

        public static KeyModifierMask GetModifierBit(KeyCode key)
        {
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift) return KeyModifierMask.Shift;
            if (key == KeyCode.LeftControl || key == KeyCode.RightControl) return KeyModifierMask.Ctrl;
            if (key == KeyCode.LeftAlt || key == KeyCode.RightAlt || key == KeyCode.AltGr) return KeyModifierMask.Alt;
            return KeyModifierMask.None;
        }

        public static bool IsModifierKey(KeyCode key)
        {
            return GetModifierBit(key) != KeyModifierMask.None;
        }

        public static Func<KeyModifierMask, KeyCode, string>? CustomFormatter { get; set; }

        public static string FormatModifiers(KeyModifierMask mask)
        {
            if (mask == KeyModifierMask.None) return string.Empty;
            var parts = new List<string>(3);
            if ((mask & KeyModifierMask.Ctrl) != 0) parts.Add("Ctrl");
            if ((mask & KeyModifierMask.Shift) != 0) parts.Add("Shift");
            if ((mask & KeyModifierMask.Alt) != 0) parts.Add("Alt");
            return string.Join("+", parts);
        }

        public bool Equals(KeyCombo other) => Modifiers == other.Modifiers && Key == other.Key;

        public override bool Equals(object? obj) => obj is KeyCombo other && Equals(other);

        public override int GetHashCode() => ((int)Modifiers * 397) ^ (int)Key;

        public static bool operator ==(KeyCombo left, KeyCombo right) => left.Equals(right);

        public static bool operator !=(KeyCombo left, KeyCombo right) => !left.Equals(right);

        public override string ToString()
        {
            if (CustomFormatter != null)
            {
                return CustomFormatter(Modifiers, Key);
            }

            if (Key == KeyCode.None)
            {
                return "None";
            }

            var effectiveMods = Modifiers & ~GetModifierBit(Key);
            if (effectiveMods == KeyModifierMask.None)
            {
                return Key.ToString();
            }

            var parts = new List<string>(3);
            if ((effectiveMods & KeyModifierMask.Ctrl) != 0) parts.Add("Ctrl");
            if ((effectiveMods & KeyModifierMask.Shift) != 0) parts.Add("Shift");
            if ((effectiveMods & KeyModifierMask.Alt) != 0) parts.Add("Alt");

            return $"{string.Join(" + ", parts)} + {Key}";
        }
    }
}
