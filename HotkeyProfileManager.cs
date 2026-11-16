using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    public sealed class ProfileSnapshot
    {
        public string Schema { get; set; } = HotkeyProfileManager.SchemaId;
        public string Version { get; set; } = "1.4.6";
        public string Name { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public Dictionary<string, string> Hotkeys { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> CustomHotkeys { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> General { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Forced { get; } = new List<string>();
        public List<string> Ignored { get; } = new List<string>();
    }

    /// <summary>
    /// 键位与设置方案快照管理（支持 3 套本地场景预设槽位与剪贴板紧凑 JSON 导入/导出，零文件/零反射权限消耗）
    /// </summary>
    public static class HotkeyProfileManager
    {
        public const string SchemaId = "puh_profile_v1";
        public const int SlotCount = 3;

        private const string ActiveSlotPrefKey = "probably_unified_hotkeys.profile_active_slot.v1";
        private const string SlotJsonPrefPrefix = "probably_unified_hotkeys.profile_slot_json.v1.";
        private const string SlotMetaPrefPrefix = "probably_unified_hotkeys.profile_slot_meta.v1.";

        private static int _activeSlotIndex = -1;

        public static int ActiveSlotIndex
        {
            get
            {
                if (_activeSlotIndex < 0)
                {
                    int saved = ReadPrefInt(ActiveSlotPrefKey, 0);
                    _activeSlotIndex = (saved >= 0 && saved < SlotCount) ? saved : 0;
                }
                return _activeSlotIndex;
            }
            set
            {
                int clamped = (value >= 0 && value < SlotCount) ? value : 0;
                _activeSlotIndex = clamped;
                WritePrefInt(ActiveSlotPrefKey, clamped);
                SavePrefs();
            }
        }

        public static int CycleNextSlot()
        {
            ActiveSlotIndex = (ActiveSlotIndex + 1) % SlotCount;
            return ActiveSlotIndex;
        }

        public static string GetSlotDisplayName(int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return I18n.Get("方案一", "Preset 1");
                case 1: return I18n.Get("方案二", "Preset 2");
                case 2: return I18n.Get("方案三", "Preset 3");
                default: return I18n.Format("方案 {0}", "Preset {0}", slotIndex + 1);
            }
        }

        public static string GetSlotShortName(int slotIndex)
        {
            return GetSlotDisplayName(slotIndex);
        }

        public static bool HasSlotData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return false;
            string raw = ReadPrefString(SlotJsonPrefPrefix + slotIndex);
            return !string.IsNullOrWhiteSpace(raw);
        }

        public static string GetBottomCycleButtonLabel()
        {
            return GetSlotDisplayName(ActiveSlotIndex);
        }

        public static string GetSlotDetailSummary(int slotIndex)
        {
            if (!HasSlotData(slotIndex))
            {
                return I18n.Get("<color=#7c8796>未保存</color>", "<color=#7c8796>Not Saved</color>");
            }

            string meta = ReadPrefString(SlotMetaPrefPrefix + slotIndex);
            if (!string.IsNullOrEmpty(meta))
            {
                string[] parts = meta.Split('|');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out int hkCount) &&
                    int.TryParse(parts[1], out int genCount))
                {
                    string ts = parts[2];
                    return I18n.Format(
                        "<color=#86efac>已存: {0} 键 / {1} 项设置</color>  <color=#99a3b0>{2}</color>",
                        "<color=#86efac>Saved: {0} keys / {1} settings</color>  <color=#99a3b0>{2}</color>",
                        hkCount,
                        genCount,
                        ts
                    );
                }
            }

            return I18n.Get("<color=#86efac>已保存</color>", "<color=#86efac>Saved</color>");
        }

        public static void ClearSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return;
            DeletePrefKey(SlotJsonPrefPrefix + slotIndex);
            DeletePrefKey(SlotMetaPrefPrefix + slotIndex);
            SavePrefs();
        }

        public static string SaveToSlot(
            int slotIndex,
            IReadOnlyList<ModHotkeyEntry> allHotkeys,
            IReadOnlyList<ModOptionGroup> modGroups,
            out int hkCount,
            out int genCount)
        {
            string slotName = GetSlotDisplayName(slotIndex);
            string json = BuildSnapshotJson(slotName, allHotkeys, modGroups, out hkCount, out genCount, out string timestamp);
            if (slotIndex >= 0 && slotIndex < SlotCount)
            {
                WritePrefString(SlotJsonPrefPrefix + slotIndex, json);
                WritePrefString(SlotMetaPrefPrefix + slotIndex, $"{hkCount}|{genCount}|{timestamp}");
                SavePrefs();
            }
            return json;
        }

        public static bool TryLoadSlotSnapshot(int slotIndex, out ProfileSnapshot? snapshot, out string error)
        {
            snapshot = null;
            if (slotIndex < 0 || slotIndex >= SlotCount)
            {
                error = I18n.Get("无效的预设槽位", "Invalid preset slot");
                return false;
            }

            string raw = ReadPrefString(SlotJsonPrefPrefix + slotIndex);
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = I18n.Format(
                    "【{0}】尚未保存任何方案，请先点击「保存方案」",
                    "[{0}] is empty. Click 'Save' first.",
                    GetSlotDisplayName(slotIndex)
                );
                return false;
            }

            return TryParseSnapshotJson(raw, out snapshot, out error);
        }

        public static string BuildSnapshotJson(
            string profileName,
            IReadOnlyList<ModHotkeyEntry> allHotkeys,
            IReadOnlyList<ModOptionGroup> modGroups,
            out int hkCount,
            out int genCount,
            out string timestamp)
        {
            hkCount = 0;
            genCount = 0;
            timestamp = DateTime.Now.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

            var sb = new StringBuilder(4096);
            sb.Append('{');
            AppendJsonKeyVal(sb, "schema", SchemaId, first: true);
            AppendJsonKeyVal(sb, "version", "1.4.6", first: false);
            AppendJsonKeyVal(sb, "name", profileName ?? "Custom", first: false);
            AppendJsonKeyVal(sb, "timestamp", timestamp, first: false);

            // 1. Hotkeys (Vanilla + All Mods)
            sb.Append(",\"hotkeys\":{");
            bool firstHk = true;
            var seenHkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (allHotkeys != null)
            {
                for (int i = 0; i < allHotkeys.Count; i++)
                {
                    var hk = allHotkeys[i];
                    if (hk == null || string.IsNullOrEmpty(hk.Id)) continue;
                    if (!seenHkIds.Add(hk.Id)) continue;

                    KeyCode key = hk.EffectiveKey;
                    KeyModifierMask mods = hk.EffectiveModifiers;
                    string comboStr = ModOptionScanner.FormatComboString(mods, key);
                    AppendJsonKeyVal(sb, hk.Id, comboStr, firstHk);
                    firstHk = false;
                    hkCount++;
                }
            }
            sb.Append('}');

            // 2. CustomHotkeys (ComboKeyStore SavedCustomHotkeys, including unbound/probed/supplemental keys)
            sb.Append(",\"customHotkeys\":{");
            bool firstCustom = true;
            var customMap = ComboKeyStore.GetSavedCustomHotkeysSnapshot();
            foreach (var kvp in customMap)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue;
                string comboStr = ModOptionScanner.FormatComboString(kvp.Value.Modifiers, kvp.Value.Key);
                AppendJsonKeyVal(sb, kvp.Key, comboStr, firstCustom);
                firstCustom = false;
            }
            sb.Append('}');

            // 3. General Settings (All Mod Toggles, Sliders, Dropdowns)
            sb.Append(",\"general\":{");
            bool firstGen = true;
            var seenGenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (modGroups != null)
            {
                for (int g = 0; g < modGroups.Count; g++)
                {
                    var group = modGroups[g];
                    if (group == null) continue;
                    for (int i = 0; i < group.GeneralSettings.Count; i++)
                    {
                        var gen = group.GeneralSettings[i];
                        if (gen == null || string.IsNullOrEmpty(gen.Id)) continue;
                        if (!seenGenIds.Add(gen.Id)) continue;

                        string? encoded = EncodeGeneralEntryValue(gen);
                        if (encoded == null) continue;

                        AppendJsonKeyVal(sb, gen.Id, encoded, firstGen);
                        firstGen = false;
                        genCount++;
                    }
                }
            }
            sb.Append('}');

            // 4. Forced Hotkey Table
            sb.Append(",\"forced\":[");
            var forcedList = ComboKeyStore.GetForcedHotkeyIdsSnapshot();
            for (int i = 0; i < forcedList.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendJsonString(sb, forcedList[i]);
            }
            sb.Append(']');

            // 5. Ignored Candidate Table
            sb.Append(",\"ignored\":[");
            var ignoredList = ComboKeyStore.GetIgnoredCandidateIdsSnapshot();
            for (int i = 0; i < ignoredList.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendJsonString(sb, ignoredList[i]);
            }
            sb.Append(']');

            sb.Append('}');
            return sb.ToString();
        }

        private static string? EncodeGeneralEntryValue(ModGeneralEntry gen)
        {
            try
            {
                switch (gen.Kind)
                {
                    case OptionKind.Toggle:
                        bool bVal = gen.PendingBool ?? (gen.GetBool != null && gen.GetBool());
                        return bVal ? "b:1" : "b:0";

                    case OptionKind.Slider:
                        float fVal = gen.PendingFloat ?? (gen.GetFloat != null ? gen.GetFloat() : gen.Min);
                        return "f:" + fVal.ToString("G7", CultureInfo.InvariantCulture);

                    case OptionKind.Dropdown:
                        int iVal = gen.PendingIndex ?? (gen.GetIndex != null ? gen.GetIndex() : 0);
                        return "i:" + iVal.ToString(CultureInfo.InvariantCulture);

                    case OptionKind.Input:
                        if (gen.IsReadOnlyInfo) return null;
                        string sVal = gen.PendingString ?? (gen.GetString != null ? (gen.GetString() ?? string.Empty) : string.Empty);
                        return "s:" + sVal;

                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public static bool TryParseSnapshotJson(string? rawText, out ProfileSnapshot? snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = I18n.Get("剪贴板为空，请先复制有效的方案 JSON 文本", "Clipboard is empty. Copy a valid profile JSON first.");
                return false;
            }

            string text = rawText!.Trim();
            int schemaIdx = text.IndexOf(SchemaId, StringComparison.Ordinal);
            int startBrace = schemaIdx >= 0 ? text.LastIndexOf('{', schemaIdx) : text.IndexOf('{');
            int endBrace = text.LastIndexOf('}');
            if (startBrace < 0 || endBrace <= startBrace)
            {
                error = I18n.Get("未识别到有效的方案 JSON 格式（缺少 { ... } 结构）", "Invalid profile JSON format (missing { ... } object).");
                return false;
            }

            string json = text.Substring(startBrace, endBrace - startBrace + 1);
            var parsed = new ProfileSnapshot();

            try
            {
                int pos = 0;
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] != '{')
                {
                    error = I18n.Get("JSON 顶层结构不是对象", "Root JSON element is not an object.");
                    return false;
                }
                pos++; // skip '{'

                while (pos < json.Length)
                {
                    SkipWhitespace(json, ref pos);
                    if (pos < json.Length && json[pos] == '}')
                    {
                        pos++;
                        break;
                    }
                    if (pos < json.Length && json[pos] == ',')
                    {
                        pos++;
                        SkipWhitespace(json, ref pos);
                    }

                    if (!TryReadJsonString(json, ref pos, out string key))
                    {
                        break;
                    }

                    SkipWhitespace(json, ref pos);
                    if (pos >= json.Length || json[pos] != ':')
                    {
                        error = I18n.Get("JSON 语法错误：键名后缺少冒号", "JSON syntax error: missing ':' after property name.");
                        return false;
                    }
                    pos++; // skip ':'
                    SkipWhitespace(json, ref pos);

                    if (string.Equals(key, "schema", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadJsonString(json, ref pos, out string v)) parsed.Schema = v;
                        else SkipJsonValue(json, ref pos);
                    }
                    else if (string.Equals(key, "version", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadJsonString(json, ref pos, out string v)) parsed.Version = v;
                        else SkipJsonValue(json, ref pos);
                    }
                    else if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadJsonString(json, ref pos, out string v)) parsed.Name = v;
                        else SkipJsonValue(json, ref pos);
                    }
                    else if (string.Equals(key, "timestamp", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadJsonString(json, ref pos, out string v)) parsed.Timestamp = v;
                        else SkipJsonValue(json, ref pos);
                    }
                    else if (string.Equals(key, "hotkeys", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadJsonStringMap(json, ref pos, parsed.Hotkeys);
                    }
                    else if (string.Equals(key, "customHotkeys", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadJsonStringMap(json, ref pos, parsed.CustomHotkeys);
                    }
                    else if (string.Equals(key, "general", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadJsonStringMap(json, ref pos, parsed.General);
                    }
                    else if (string.Equals(key, "forced", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadJsonStringArray(json, ref pos, parsed.Forced);
                    }
                    else if (string.Equals(key, "ignored", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadJsonStringArray(json, ref pos, parsed.Ignored);
                    }
                    else
                    {
                        SkipJsonValue(json, ref pos);
                    }
                }
            }
            catch (Exception ex)
            {
                error = I18n.Format("解析方案 JSON 失败: {0}", "Failed to parse profile JSON: {0}", ex.Message);
                return false;
            }

            if (!string.Equals(parsed.Schema, SchemaId, StringComparison.OrdinalIgnoreCase) &&
                parsed.Hotkeys.Count == 0 &&
                parsed.General.Count == 0 &&
                parsed.CustomHotkeys.Count == 0)
            {
                error = I18n.Get("剪贴板内容不是有效的本模组键位方案 JSON", "Clipboard content is not a valid hotkey profile JSON.");
                return false;
            }

            snapshot = parsed;
            return true;
        }

        private static void AppendJsonKeyVal(StringBuilder sb, string key, string val, bool first)
        {
            if (!first) sb.Append(',');
            AppendJsonString(sb, key);
            sb.Append(':');
            AppendJsonString(sb, val);
        }

        private static void AppendJsonString(StringBuilder sb, string? value)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value!.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ')
                            {
                                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        private static bool TryReadJsonString(string s, ref int pos, out string result)
        {
            result = string.Empty;
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] != '"') return false;
            pos++; // skip opening '"'

            var sb = new StringBuilder(32);
            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == '"')
                {
                    result = sb.ToString();
                    return true;
                }
                if (c == '\\' && pos < s.Length)
                {
                    char esc = s[pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 <= s.Length &&
                                int.TryParse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                pos += 4;
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return false;
        }

        private static void ReadJsonStringMap(string s, ref int pos, Dictionary<string, string> target)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] != '{')
            {
                SkipJsonValue(s, ref pos);
                return;
            }
            pos++; // skip '{'

            while (pos < s.Length)
            {
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && s[pos] == '}')
                {
                    pos++;
                    return;
                }
                if (pos < s.Length && s[pos] == ',')
                {
                    pos++;
                    SkipWhitespace(s, ref pos);
                }

                if (!TryReadJsonString(s, ref pos, out string k))
                {
                    SkipJsonValue(s, ref pos);
                    break;
                }
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && s[pos] == ':') pos++;
                SkipWhitespace(s, ref pos);

                if (TryReadJsonString(s, ref pos, out string v))
                {
                    target[k] = v;
                }
                else
                {
                    SkipJsonValue(s, ref pos);
                }
            }
        }

        private static void ReadJsonStringArray(string s, ref int pos, List<string> target)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] != '[')
            {
                SkipJsonValue(s, ref pos);
                return;
            }
            pos++; // skip '['

            while (pos < s.Length)
            {
                SkipWhitespace(s, ref pos);
                if (pos < s.Length && s[pos] == ']')
                {
                    pos++;
                    return;
                }
                if (pos < s.Length && s[pos] == ',')
                {
                    pos++;
                    SkipWhitespace(s, ref pos);
                }

                if (TryReadJsonString(s, ref pos, out string item))
                {
                    if (!string.IsNullOrWhiteSpace(item)) target.Add(item);
                }
                else
                {
                    SkipJsonValue(s, ref pos);
                }
            }
        }

        private static void SkipJsonValue(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) return;

            char c = s[pos];
            if (c == '"')
            {
                TryReadJsonString(s, ref pos, out _);
                return;
            }
            if (c == '{' || c == '[')
            {
                char open = c;
                char close = c == '{' ? '}' : ']';
                int depth = 0;
                while (pos < s.Length)
                {
                    char cur = s[pos];
                    if (cur == '"')
                    {
                        TryReadJsonString(s, ref pos, out _);
                        continue;
                    }
                    if (cur == open) depth++;
                    else if (cur == close)
                    {
                        depth--;
                        if (depth <= 0)
                        {
                            pos++;
                            return;
                        }
                    }
                    pos++;
                }
                return;
            }

            while (pos < s.Length && s[pos] != ',' && s[pos] != '}' && s[pos] != ']' && !char.IsWhiteSpace(s[pos]))
            {
                pos++;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadPrefString(string key) => PlayerPrefs.GetString(key, string.Empty);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WritePrefString(string key, string val) => PlayerPrefs.SetString(key, val);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ReadPrefInt(string key, int def) => PlayerPrefs.GetInt(key, def);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WritePrefInt(string key, int val) => PlayerPrefs.SetInt(key, val);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DeletePrefKey(string key) => PlayerPrefs.DeleteKey(key);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SavePrefs() => PlayerPrefs.Save();
    }
}
