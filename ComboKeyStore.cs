using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        private const string CustomHotkeysPrefsKey = "probably_unified_hotkeys.custom_hotkeys.v1";
        private const string ForcedHotkeysPrefsKey = "probably_unified_hotkeys.forced_hotkeys.v1";
        private const string IgnoredCandidatesPrefsKey = "probably_unified_hotkeys.ignored_candidates.v1";

        private static readonly Dictionary<string, (KeyModifierMask Modifiers, KeyCode Key)> SavedCombos
            = new Dictionary<string, (KeyModifierMask, KeyCode)>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, (KeyModifierMask Modifiers, KeyCode Key)> SavedCustomHotkeys
            = new Dictionary<string, (KeyModifierMask, KeyCode)>(StringComparer.OrdinalIgnoreCase);

        public struct InputRemapRule
        {
            public string EntryId;
            public KeyModifierMask OriginalModifiers;
            public KeyCode OriginalKey;
            public KeyModifierMask TargetModifiers;
            public KeyCode TargetKey;
        }

        private static readonly Dictionary<KeyCode, List<InputRemapRule>> ActiveInputRemapsByOrigKey
            = new Dictionary<KeyCode, List<InputRemapRule>>();

        private static bool _hasAnyActiveInputRemap = false;
        private static int _rawInputCheckDepth = 0;

        private struct MethodInputRemapScopeFrame
        {
            public string EntryId;
            public KeyCode OrigKeyPrimary;
            public KeyCode OrigKeySecondary;
            public KeyModifierMask DefaultModifiers;
            public KeyCode DefaultKey;
        }

        private static readonly List<MethodInputRemapScopeFrame> MethodRemapScopeStack
            = new List<MethodInputRemapScopeFrame>(4);

        public static bool HasAnyActiveInputRemapOrScope =>
            _rawInputCheckDepth == 0 && (_hasAnyActiveInputRemap || MethodRemapScopeStack.Count > 0);

        private static readonly HashSet<string> ForcedHotkeyIds
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> IgnoredCandidateIds
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static HashSet<string> _managedStringHotkeyIds
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static HashSet<string> _managedIntHotkeyIds
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static int _forcedHotkeysRevision = 0;

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

        internal static bool UseInMemoryPrefsForTests = false;
        internal static Func<KeyCode, KeyModifierMask>? HeldModifiersOverride = null;
        private static readonly Dictionary<string, string> InMemoryTestPrefs = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string ReadPrefString(string key)
        {
            if (UseInMemoryPrefsForTests)
            {
                return InMemoryTestPrefs.TryGetValue(key, out string? v) ? v : string.Empty;
            }
            return ReadPlayerPrefRaw(key);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadPlayerPrefRaw(string key) => PlayerPrefs.GetString(key, string.Empty);

        private static void WritePrefString(string key, string value)
        {
            if (UseInMemoryPrefsForTests)
            {
                InMemoryTestPrefs[key] = value;
                return;
            }
            WritePlayerPrefRaw(key, value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WritePlayerPrefRaw(string key, string value) => PlayerPrefs.SetString(key, value);

        private static void DeletePrefKey(string key)
        {
            if (UseInMemoryPrefsForTests)
            {
                InMemoryTestPrefs.Remove(key);
                return;
            }
            DeletePlayerPrefRaw(key);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DeletePlayerPrefRaw(string key) => PlayerPrefs.DeleteKey(key);

        private static void SavePrefsStorage()
        {
            if (UseInMemoryPrefsForTests) return;
            SavePlayerPrefsRaw();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SavePlayerPrefsRaw() => PlayerPrefs.Save();

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Load();
        }

        public static void Load()
        {
            SavedCombos.Clear();
            SavedCustomHotkeys.Clear();
            ForcedHotkeyIds.Clear();
            IgnoredCandidateIds.Clear();
            try
            {
                string raw = ReadPrefString(PrefsKey);
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

                string rawCustom = ReadPrefString(CustomHotkeysPrefsKey);
                if (!string.IsNullOrEmpty(rawCustom))
                {
                    string[] parts = rawCustom.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
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
                                if (kc == KeyCode.None)
                                {
                                    SavedCustomHotkeys[id] = (KeyModifierMask.None, KeyCode.None);
                                }
                                else
                                {
                                    SavedCustomHotkeys[id] = (mask & ~GetModifierBit(kc), kc);
                                }
                            }
                        }
                    }
                }

                LoadIdSet(ForcedHotkeysPrefsKey, ForcedHotkeyIds);
                LoadIdSet(IgnoredCandidatesPrefsKey, IgnoredCandidateIds);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error loading combo store: {ex.Message}");
            }

            RebuildRuntimeFromSaved();
        }

        private static void LoadIdSet(string prefKey, HashSet<string> target)
        {
            string raw = ReadPrefString(prefKey);
            if (string.IsNullOrEmpty(raw)) return;
            string[] parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string id = Uri.UnescapeDataString(part).Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    target.Add(id);
                }
            }
        }

        private static void SaveIdSet(string prefKey, HashSet<string> ids, string label)
        {
            try
            {
                if (ids.Count == 0)
                {
                    DeletePrefKey(prefKey);
                }
                else
                {
                    var sb = new StringBuilder(128);
                    foreach (string id in ids)
                    {
                        if (string.IsNullOrEmpty(id)) continue;
                        if (sb.Length > 0) sb.Append(';');
                        sb.Append(Uri.EscapeDataString(id));
                    }
                    string encoded = sb.ToString();
                    if (string.IsNullOrEmpty(encoded)) DeletePrefKey(prefKey);
                    else WritePrefString(prefKey, encoded);
                }
                SavePrefsStorage();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error saving {label}: {ex.Message}");
            }
        }

        public static void SaveForcedHotkeys() => SaveIdSet(ForcedHotkeysPrefsKey, ForcedHotkeyIds, "forced hotkeys");

        public static bool IsForcedHotkey(string? canonicalId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(canonicalId)) return false;
            return ForcedHotkeyIds.Contains(canonicalId);
        }

        public static void SetForcedHotkey(string? canonicalId, bool forced)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(canonicalId)) return;

            bool changed = forced
                ? ForcedHotkeyIds.Add(canonicalId)
                : ForcedHotkeyIds.Remove(canonicalId);

            if (changed)
            {
                _forcedHotkeysRevision++;
                if (forced && IgnoredCandidateIds.Remove(canonicalId))
                {
                    SaveIdSet(IgnoredCandidatesPrefsKey, IgnoredCandidateIds, "ignored candidates");
                }
                if (!forced)
                {
                    if (SavedCombos.Remove(canonicalId))
                    {
                        Save();
                        RebuildRuntimeFromSaved();
                    }
                }
                SaveForcedHotkeys();
            }
        }

        public static bool IsIgnoredCandidate(string? canonicalId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(canonicalId)) return false;
            return IgnoredCandidateIds.Contains(canonicalId);
        }

        public static void SetIgnoredCandidate(string? canonicalId, bool ignored)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(canonicalId)) return;

            bool changed = ignored
                ? IgnoredCandidateIds.Add(canonicalId)
                : IgnoredCandidateIds.Remove(canonicalId);

            if (changed)
            {
                _forcedHotkeysRevision++;
                SaveIdSet(IgnoredCandidatesPrefsKey, IgnoredCandidateIds, "ignored candidates");
            }
        }

        public static int GetIgnoredCandidatesCount()
        {
            EnsureLoaded();
            return IgnoredCandidateIds.Count;
        }

        public static int GetForcedHotkeysRevision()
        {
            EnsureLoaded();
            return _forcedHotkeysRevision;
        }

        public static List<string> GetForcedHotkeyIdsSnapshot()
        {
            EnsureLoaded();
            var list = new List<string>(ForcedHotkeyIds);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static List<string> GetIgnoredCandidateIdsSnapshot()
        {
            EnsureLoaded();
            var list = new List<string>(IgnoredCandidateIds);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static bool ReplaceForcedAndIgnoredSets(IEnumerable<string>? forcedIds, IEnumerable<string>? ignoredIds, out bool forcedChanged)
        {
            EnsureLoaded();
            var nextForced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (forcedIds != null)
            {
                foreach (var id in forcedIds)
                {
                    if (!string.IsNullOrWhiteSpace(id)) nextForced.Add(id.Trim());
                }
            }

            var nextIgnored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ignoredIds != null)
            {
                foreach (var id in ignoredIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        string trimmed = id.Trim();
                        if (!nextForced.Contains(trimmed))
                        {
                            nextIgnored.Add(trimmed);
                        }
                    }
                }
            }

            forcedChanged = !ForcedHotkeyIds.SetEquals(nextForced);
            bool ignoredChanged = !IgnoredCandidateIds.SetEquals(nextIgnored);
            if (!forcedChanged && !ignoredChanged)
            {
                return false;
            }

            ForcedHotkeyIds.Clear();
            foreach (var id in nextForced) ForcedHotkeyIds.Add(id);

            IgnoredCandidateIds.Clear();
            foreach (var id in nextIgnored) IgnoredCandidateIds.Add(id);

            _forcedHotkeysRevision++;
            SaveForcedHotkeys();
            SaveIdSet(IgnoredCandidatesPrefsKey, IgnoredCandidateIds, "ignored candidates");
            return true;
        }

        public static Dictionary<string, KeyCombo> GetSavedCustomHotkeysSnapshot()
        {
            EnsureLoaded();
            var map = new Dictionary<string, KeyCombo>(SavedCustomHotkeys.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in SavedCustomHotkeys)
            {
                map[kvp.Key] = new KeyCombo(kvp.Value.Modifiers, kvp.Value.Key);
            }
            return map;
        }

        public static void ApplyCustomHotkeysMap(IDictionary<string, KeyCombo>? customMap)
        {
            EnsureLoaded();
            if (customMap == null) return;
            foreach (var kvp in customMap)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue;
                var kc = kvp.Value.Key;
                var mods = kc == KeyCode.None ? KeyModifierMask.None : (kvp.Value.Modifiers & ~GetModifierBit(kc));
                SavedCustomHotkeys[kvp.Key] = (mods, kc);
            }
            SaveCustomHotkeysCore();
            SavePrefsStorage();
            RebuildRuntimeFromSaved();
        }

        public static void RegisterManagedConfigKeyIds(IEnumerable<string>? stringIds, IEnumerable<string>? intIds)
        {
            var nextStr = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (stringIds != null)
            {
                foreach (string id in stringIds)
                {
                    if (!string.IsNullOrEmpty(id)) nextStr.Add(id);
                }
            }

            var nextInt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (intIds != null)
            {
                foreach (string id in intIds)
                {
                    if (!string.IsNullOrEmpty(id)) nextInt.Add(id);
                }
            }

            _managedStringHotkeyIds = nextStr;
            _managedIntHotkeyIds = nextInt;
        }

        public static bool IsManagedStringHotkey(string? fullId, string? keyName = null)
        {
            EnsureLoaded();
            var strSet = _managedStringHotkeyIds;
            if (!string.IsNullOrEmpty(fullId))
            {
                if (strSet.Contains(fullId) || ForcedHotkeyIds.Contains(fullId) || SavedCombos.ContainsKey(fullId))
                {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(keyName) && strSet.Contains(keyName))
            {
                return true;
            }
            return false;
        }

        public static bool IsManagedIntHotkey(string? fullId, string? keyName = null)
        {
            EnsureLoaded();
            var intSet = _managedIntHotkeyIds;
            if (!string.IsNullOrEmpty(fullId))
            {
                if (intSet.Contains(fullId) || ForcedHotkeyIds.Contains(fullId) || SavedCombos.ContainsKey(fullId))
                {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(keyName) && intSet.Contains(keyName))
            {
                return true;
            }
            return false;
        }

        public static bool HasActiveComboConflictForPlainKey(KeyCode primaryKey)
        {
            return ShouldSuppressPlainKey(primaryKey);
        }

        public static bool HasSavedComboFor(string? fullId)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(fullId) && SavedCombos.ContainsKey(fullId);
        }

        public static void Save()
        {
            try
            {
                if (SavedCombos.Count == 0)
                {
                    DeletePrefKey(PrefsKey);
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
                    if (string.IsNullOrEmpty(encoded)) DeletePrefKey(PrefsKey);
                    else WritePrefString(PrefsKey, encoded);
                }

                SaveCustomHotkeysCore();
                SavePrefsStorage();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProbablyUnifiedHotkeys] Error saving combo store: {ex.Message}");
            }
        }

        private static void SaveCustomHotkeysCore()
        {
            if (SavedCustomHotkeys.Count == 0)
            {
                DeletePrefKey(CustomHotkeysPrefsKey);
                return;
            }

            var sb = new StringBuilder(256);
            foreach (var kvp in SavedCustomHotkeys)
            {
                sb.Append(Uri.EscapeDataString(kvp.Key))
                  .Append('=')
                  .Append((int)kvp.Value.Modifiers)
                  .Append(':')
                  .Append((int)kvp.Value.Key)
                  .Append(';');
            }
            string encoded = sb.ToString();
            if (string.IsNullOrEmpty(encoded)) DeletePrefKey(CustomHotkeysPrefsKey);
            else WritePrefString(CustomHotkeysPrefsKey, encoded);
        }

        public static bool HasSavedCustomHotkey(string? entryId)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(entryId) && SavedCustomHotkeys.ContainsKey(entryId);
        }

        public static KeyCombo GetEffectiveCustomHotkey(string? entryId, KeyModifierMask defaultMods, KeyCode defaultKey)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(entryId) && SavedCustomHotkeys.TryGetValue(entryId, out var custom))
            {
                return new KeyCombo(custom.Modifiers, custom.Key);
            }
            if (!string.IsNullOrEmpty(entryId) && SavedCombos.TryGetValue(entryId, out var combo))
            {
                return new KeyCombo(combo.Modifiers, combo.Key);
            }
            return new KeyCombo(defaultMods, defaultKey);
        }

        public static void SetSavedCustomHotkey(
            string? entryId,
            KeyModifierMask modifiers,
            KeyCode primaryKey,
            KeyModifierMask defaultMods = KeyModifierMask.None,
            KeyCode defaultKey = KeyCode.None)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(entryId)) return;

            var normalized = primaryKey == KeyCode.None
                ? KeyModifierMask.None
                : (modifiers & ~GetModifierBit(primaryKey));

            var normDefault = defaultKey == KeyCode.None
                ? KeyModifierMask.None
                : (defaultMods & ~GetModifierBit(defaultKey));

            if (primaryKey == defaultKey && normalized == normDefault)
            {
                SavedCustomHotkeys.Remove(entryId);
            }
            else
            {
                SavedCustomHotkeys[entryId] = (normalized, primaryKey);
            }

            SetSavedCombo(entryId, normalized, primaryKey);
            Save();
            RebuildRuntimeFromSaved();
        }

        public static KeyModifierMask GetSavedModifiers(string entryId, KeyCode expectedPrimaryKey = KeyCode.None)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(entryId)) return KeyModifierMask.None;
            if (SavedCustomHotkeys.TryGetValue(entryId, out var customPair))
            {
                if (expectedPrimaryKey == KeyCode.None || customPair.Key == expectedPrimaryKey)
                {
                    return customPair.Modifiers;
                }
            }
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
                    if (hk.IsReadOnlyProbed) continue;
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
            foreach (var kvp in SavedCustomHotkeys)
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

        public static void ClearInputKeyRemaps()
        {
            ActiveInputRemapsByOrigKey.Clear();
            _hasAnyActiveInputRemap = false;
        }

        public static void RegisterInputKeyRemap(
            string entryId,
            KeyModifierMask origMods,
            KeyCode origKey,
            KeyModifierMask targetMods,
            KeyCode targetKey)
        {
            if (string.IsNullOrEmpty(entryId) || origKey == KeyCode.None) return;

            var normOrig = origMods & ~GetModifierBit(origKey);
            var normTarget = targetKey == KeyCode.None
                ? KeyModifierMask.None
                : (targetMods & ~GetModifierBit(targetKey));

            // Remove any existing rule for the same entryId first
            if (ActiveInputRemapsByOrigKey.TryGetValue(origKey, out var existingList))
            {
                for (int i = existingList.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(existingList[i].EntryId, entryId, StringComparison.OrdinalIgnoreCase))
                    {
                        existingList.RemoveAt(i);
                    }
                }
                if (existingList.Count == 0)
                {
                    ActiveInputRemapsByOrigKey.Remove(origKey);
                }
            }

            KeyCode altOrigKey = GetPairedModifierKey(origKey);
            if (altOrigKey != KeyCode.None && ActiveInputRemapsByOrigKey.TryGetValue(altOrigKey, out var altList))
            {
                for (int i = altList.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(altList[i].EntryId, entryId, StringComparison.OrdinalIgnoreCase))
                    {
                        altList.RemoveAt(i);
                    }
                }
                if (altList.Count == 0)
                {
                    ActiveInputRemapsByOrigKey.Remove(altOrigKey);
                }
            }

            if (origKey == targetKey && normOrig == normTarget)
            {
                _hasAnyActiveInputRemap = ActiveInputRemapsByOrigKey.Count > 0;
                return;
            }

            var rule = new InputRemapRule
            {
                EntryId = entryId,
                OriginalModifiers = normOrig,
                OriginalKey = origKey,
                TargetModifiers = normTarget,
                TargetKey = targetKey
            };

            AddInputRemapRuleForKey(origKey, rule);
            if (altOrigKey != KeyCode.None)
            {
                AddInputRemapRuleForKey(altOrigKey, rule);
            }
            _hasAnyActiveInputRemap = ActiveInputRemapsByOrigKey.Count > 0;
        }

        private static void AddInputRemapRuleForKey(KeyCode key, InputRemapRule rule)
        {
            if (!ActiveInputRemapsByOrigKey.TryGetValue(key, out var list))
            {
                list = new List<InputRemapRule>(2);
                ActiveInputRemapsByOrigKey[key] = list;
            }
            list.Add(rule);
        }

        public static KeyCode GetPairedModifierKey(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.LeftShift: return KeyCode.RightShift;
                case KeyCode.RightShift: return KeyCode.LeftShift;
                case KeyCode.LeftControl: return KeyCode.RightControl;
                case KeyCode.RightControl: return KeyCode.LeftControl;
                case KeyCode.LeftAlt: return KeyCode.RightAlt;
                case KeyCode.RightAlt: return KeyCode.LeftAlt;
                default: return KeyCode.None;
            }
        }

        public static void EnterMethodInputRemapScope(
            string entryId,
            KeyCode origKeyPrimary,
            KeyCode origKeySecondary = KeyCode.None,
            KeyModifierMask defaultModifiers = KeyModifierMask.None,
            KeyCode defaultKey = KeyCode.None)
        {
            MethodRemapScopeStack.Add(new MethodInputRemapScopeFrame
            {
                EntryId = entryId,
                OrigKeyPrimary = origKeyPrimary,
                OrigKeySecondary = origKeySecondary,
                DefaultModifiers = defaultModifiers,
                DefaultKey = defaultKey == KeyCode.None ? origKeyPrimary : defaultKey
            });
        }

        public static void ExitMethodInputRemapScope()
        {
            int count = MethodRemapScopeStack.Count;
            if (count > 0)
            {
                MethodRemapScopeStack.RemoveAt(count - 1);
            }
        }

        public enum InputQueryKind
        {
            Held = 0,
            Down = 1,
            Up = 2
        }

        public static bool TryEvaluateInputRemap(KeyCode queriedKey, InputQueryKind kind, out bool result)
        {
            result = false;
            if (_rawInputCheckDepth > 0 || queriedKey == KeyCode.None || IsBypassed)
            {
                return false;
            }

            // 1. Scoped method interception (highest precision)
            int scopeCount = MethodRemapScopeStack.Count;
            if (scopeCount > 0)
            {
                var top = MethodRemapScopeStack[scopeCount - 1];
                if (queriedKey == top.OrigKeyPrimary)
                {
                    result = EvaluateCustomHotkeyByKind(top.EntryId, top.DefaultModifiers, top.DefaultKey, kind);
                    return true;
                }
                if (top.OrigKeySecondary != KeyCode.None && queriedKey == top.OrigKeySecondary)
                {
                    // Secondary paired modifier key (e.g. RightAlt when Primary is LeftAlt) returns false because Primary already evaluated both
                    result = false;
                    return true;
                }
            }

            // 2. Global probed/custom key remap table
            if (!_hasAnyActiveInputRemap) return false;
            if (!ActiveInputRemapsByOrigKey.TryGetValue(queriedKey, out var rules) || rules.Count == 0)
            {
                return false;
            }

            bool anyRuleEvaluated = false;
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                // If original key was a paired modifier secondary (e.g. RightAlt when rule.OriginalKey is LeftAlt), suppress secondary
                if (queriedKey != rule.OriginalKey && queriedKey == GetPairedModifierKey(rule.OriginalKey))
                {
                    anyRuleEvaluated = true;
                    continue;
                }

                anyRuleEvaluated = true;
                if (EvaluateTargetComboByKind(rule.TargetModifiers, rule.TargetKey, kind))
                {
                    result = true;
                    return true;
                }
            }

            if (anyRuleEvaluated)
            {
                result = false;
                return true;
            }
            return false;
        }

        public static bool IsCustomHotkeyDown(string entryId, KeyModifierMask defaultMods, KeyCode defaultKey)
            => EvaluateCustomHotkeyByKind(entryId, defaultMods, defaultKey, InputQueryKind.Down);

        public static bool IsCustomHotkeyHeld(string entryId, KeyModifierMask defaultMods, KeyCode defaultKey)
            => EvaluateCustomHotkeyByKind(entryId, defaultMods, defaultKey, InputQueryKind.Held);

        public static bool IsCustomHotkeyUp(string entryId, KeyModifierMask defaultMods, KeyCode defaultKey)
            => EvaluateCustomHotkeyByKind(entryId, defaultMods, defaultKey, InputQueryKind.Up);

        private static bool EvaluateCustomHotkeyByKind(
            string entryId,
            KeyModifierMask defaultMods,
            KeyCode defaultKey,
            InputQueryKind kind)
        {
            if (IsBypassed) return false;
            var effective = GetEffectiveCustomHotkey(entryId, defaultMods, defaultKey);
            return EvaluateTargetComboByKind(effective.Modifiers, effective.Key, kind);
        }

        public static bool EvaluateTargetComboByKind(
            KeyModifierMask modifiers,
            KeyCode key,
            InputQueryKind kind)
        {
            if (key == KeyCode.None || IsBypassed) return false;

            var normMods = modifiers & ~GetModifierBit(key);
            if (normMods != KeyModifierMask.None)
            {
                if (!AreModifiersExactMatch(normMods, key))
                {
                    return false;
                }
            }
            else if (ShouldSuppressPlainKey(key))
            {
                return false;
            }

            switch (kind)
            {
                case InputQueryKind.Down:
                    return IsKeyDownOrModifierPairRaw(key);
                case InputQueryKind.Up:
                    return IsKeyUpOrModifierPairRaw(key);
                default:
                    return IsKeyHeldOrModifierPairRaw(key);
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
            if (HeldModifiersOverride != null)
            {
                return HeldModifiersOverride(excludeModifierKey);
            }

            KeyModifierMask mask = KeyModifierMask.None;
            KeyCode pairedExclude = GetPairedModifierKey(excludeModifierKey);

            _rawInputCheckDepth++;
            try
            {
                bool shift = (excludeModifierKey != KeyCode.LeftShift && pairedExclude != KeyCode.LeftShift && Input.GetKey(KeyCode.LeftShift)) ||
                             (excludeModifierKey != KeyCode.RightShift && pairedExclude != KeyCode.RightShift && Input.GetKey(KeyCode.RightShift));
                bool ctrl = (excludeModifierKey != KeyCode.LeftControl && pairedExclude != KeyCode.LeftControl && Input.GetKey(KeyCode.LeftControl)) ||
                            (excludeModifierKey != KeyCode.RightControl && pairedExclude != KeyCode.RightControl && Input.GetKey(KeyCode.RightControl));
                bool alt = (excludeModifierKey != KeyCode.LeftAlt && pairedExclude != KeyCode.LeftAlt && Input.GetKey(KeyCode.LeftAlt)) ||
                           (excludeModifierKey != KeyCode.RightAlt && pairedExclude != KeyCode.RightAlt && Input.GetKey(KeyCode.RightAlt));

                if (shift) mask |= KeyModifierMask.Shift;
                if (ctrl) mask |= KeyModifierMask.Ctrl;
                if (alt) mask |= KeyModifierMask.Alt;
            }
            finally
            {
                _rawInputCheckDepth--;
            }

            return mask;
        }

        public static bool AreModifiersExactMatch(KeyModifierMask required, KeyCode primaryKey)
        {
            var normalizedReq = required & ~GetModifierBit(primaryKey);
            return GetHeldModifiersMask(IsModifierKey(primaryKey) ? primaryKey : KeyCode.None) == normalizedReq;
        }

        public static bool ShouldSuppressPlainKey(KeyCode primaryKey)
        {
            if (!ModFeatureConfig.EnableKeySuppression) return false;
            if (primaryKey == KeyCode.None || ActiveCombosByKey.Count == 0) return false;
            if (!ActiveCombosByKey.TryGetValue(primaryKey, out var list)) return false;

            var held = GetHeldModifiersMask(IsModifierKey(primaryKey) ? primaryKey : KeyCode.None);
            return held != KeyModifierMask.None && list.Contains(held);
        }

        public static bool IsKeyDownRaw(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            _rawInputCheckDepth++;
            try
            {
                if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6)
                {
                    int btn = (int)k - (int)KeyCode.Mouse0;
                    return Input.GetMouseButtonDown(btn) || Input.GetKeyDown(k);
                }
                return Input.GetKeyDown(k);
            }
            finally
            {
                _rawInputCheckDepth--;
            }
        }

        public static bool IsKeyHeldRaw(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            _rawInputCheckDepth++;
            try
            {
                if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6)
                {
                    int btn = (int)k - (int)KeyCode.Mouse0;
                    return Input.GetMouseButton(btn) || Input.GetKey(k);
                }
                return Input.GetKey(k);
            }
            finally
            {
                _rawInputCheckDepth--;
            }
        }

        public static bool IsKeyUpRaw(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            _rawInputCheckDepth++;
            try
            {
                if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6)
                {
                    int btn = (int)k - (int)KeyCode.Mouse0;
                    return Input.GetMouseButtonUp(btn) || Input.GetKeyUp(k);
                }
                return Input.GetKeyUp(k);
            }
            finally
            {
                _rawInputCheckDepth--;
            }
        }

        public static bool IsKeyDownOrModifierPairRaw(KeyCode k)
        {
            if (IsKeyDownRaw(k)) return true;
            KeyCode paired = GetPairedModifierKey(k);
            return paired != KeyCode.None && IsKeyDownRaw(paired);
        }

        public static bool IsKeyHeldOrModifierPairRaw(KeyCode k)
        {
            if (IsKeyHeldRaw(k)) return true;
            KeyCode paired = GetPairedModifierKey(k);
            return paired != KeyCode.None && IsKeyHeldRaw(paired);
        }

        public static bool IsKeyUpOrModifierPairRaw(KeyCode k)
        {
            if (IsKeyUpRaw(k)) return true;
            KeyCode paired = GetPairedModifierKey(k);
            return paired != KeyCode.None && IsKeyUpRaw(paired);
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
            bool matchedCustom = false;
            if (candidateEntryIds != null)
            {
                foreach (string id in candidateEntryIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (SavedCustomHotkeys.TryGetValue(id, out var customPair))
                    {
                        if (customPair.Key == KeyCode.None)
                        {
                            key = KeyCode.None;
                            return;
                        }
                        key = customPair.Key;
                        required = customPair.Modifiers;
                        matchedCustom = true;
                        break;
                    }
                    if (SavedCombos.TryGetValue(id, out var pair) && pair.Key == key)
                    {
                        required = pair.Modifiers;
                        break;
                    }
                }
            }

            if (!matchedCustom && required == KeyModifierMask.None && !string.IsNullOrEmpty(modIdPrefix))
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
            if (key == KeyCode.None) return I18n.Get("无", "None");

            switch (key)
            {
                case KeyCode.Mouse0: return I18n.Get("左键", "Left Click");
                case KeyCode.Mouse1: return I18n.Get("右键", "Right Click");
                case KeyCode.Mouse2: return I18n.Get("中键", "Middle Click");
                case KeyCode.Mouse3: return I18n.Get("鼠标侧键1", "Mouse Side 1");
                case KeyCode.Mouse4: return I18n.Get("鼠标侧键2", "Mouse Side 2");
                case KeyCode.Mouse5: return I18n.Get("鼠标侧键3", "Mouse Side 3");
                case KeyCode.Mouse6: return I18n.Get("鼠标侧键4", "Mouse Side 4");
                default: return KeyBindHelper.KeyName(key);
            }
        }

        public static string FormatCombo(KeyModifierMask modifiers, KeyCode key)
        {
            if (key == KeyCode.None) return I18n.Get("无", "None");

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
