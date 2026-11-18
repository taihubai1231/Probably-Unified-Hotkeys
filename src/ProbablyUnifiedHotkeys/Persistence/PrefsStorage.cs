using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    /// <summary>
    /// 统一封装底层 PlayerPrefs 读写门面（供 ComboKeyStore 与 HotkeyProfileManager 共用）。
    /// 支持基于内存字典的无头单测模拟，并将所有 UnityEngine.PlayerPrefs 调用严格隔离在
    /// [MethodImpl(MethodImplOptions.NoInlining)] 私有方法中，确保在 .NET 8 离线单测模式下
    /// JIT 绝不触发 Unity ECall。
    /// </summary>
    public static class PrefsStorage
    {
        private static readonly object PrefsLock = new object();
        private static readonly Dictionary<string, string> InMemoryStringPrefs = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> InMemoryIntPrefs = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// 供无头测试使用纯内存存储，避免访问 UnityEngine.PlayerPrefs。
        /// </summary>
        internal static bool UseInMemoryPrefsForTests { get; set; }

        /// <summary>
        /// 清空测试内存存储的所有键值。
        /// </summary>
        internal static void ResetInMemoryPrefsForTests()
        {
            lock (PrefsLock)
            {
                InMemoryStringPrefs.Clear();
                InMemoryIntPrefs.Clear();
            }
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;

            if (UseInMemoryPrefsForTests)
            {
                lock (PrefsLock)
                {
                    return InMemoryStringPrefs.TryGetValue(key, out string? val) ? val : defaultValue;
                }
            }

            return GetStringRaw(key, defaultValue);
        }

        public static void SetString(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            string val = value ?? string.Empty;

            if (UseInMemoryPrefsForTests)
            {
                lock (PrefsLock)
                {
                    InMemoryStringPrefs[key] = val;
                }
                return;
            }

            SetStringRaw(key, val);
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;

            if (UseInMemoryPrefsForTests)
            {
                lock (PrefsLock)
                {
                    return InMemoryIntPrefs.TryGetValue(key, out int val) ? val : defaultValue;
                }
            }

            return GetIntRaw(key, defaultValue);
        }

        public static void SetInt(string key, int value)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (UseInMemoryPrefsForTests)
            {
                lock (PrefsLock)
                {
                    InMemoryIntPrefs[key] = value;
                }
                return;
            }

            SetIntRaw(key, value);
        }

        public static bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (UseInMemoryPrefsForTests)
            {
                lock (PrefsLock)
                {
                    return InMemoryStringPrefs.ContainsKey(key) || InMemoryIntPrefs.ContainsKey(key);
                }
            }

            return HasKeyRaw(key);
        }

        public static void DeleteKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (UseInMemoryPrefsForTests)
            {
                lock (PrefsLock)
                {
                    InMemoryStringPrefs.Remove(key);
                    InMemoryIntPrefs.Remove(key);
                }
                return;
            }

            DeleteKeyRaw(key);
        }

        public static void Save()
        {
            if (UseInMemoryPrefsForTests) return;

            SaveRaw();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetStringRaw(string key, string defaultValue)
        {
            try
            {
                return PlayerPrefs.GetString(key, defaultValue);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to read PlayerPrefs string for key '{key}': {ex.Message}", ex);
                return defaultValue;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetStringRaw(string key, string value)
        {
            try
            {
                PlayerPrefs.SetString(key, value);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to write PlayerPrefs string for key '{key}': {ex.Message}", ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int GetIntRaw(string key, int defaultValue)
        {
            try
            {
                return PlayerPrefs.GetInt(key, defaultValue);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to read PlayerPrefs int for key '{key}': {ex.Message}", ex);
                return defaultValue;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetIntRaw(string key, int value)
        {
            try
            {
                PlayerPrefs.SetInt(key, value);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to write PlayerPrefs int for key '{key}': {ex.Message}", ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HasKeyRaw(string key)
        {
            try
            {
                return PlayerPrefs.HasKey(key);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to check PlayerPrefs key '{key}': {ex.Message}", ex);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DeleteKeyRaw(string key)
        {
            try
            {
                PlayerPrefs.DeleteKey(key);
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to delete PlayerPrefs key '{key}': {ex.Message}", ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SaveRaw()
        {
            try
            {
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to save PlayerPrefs: {ex.Message}", ex);
            }
        }
    }
}
