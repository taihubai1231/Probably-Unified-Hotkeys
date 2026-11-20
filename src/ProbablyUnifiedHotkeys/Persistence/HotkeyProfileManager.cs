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
        public string Schema { get; set; } = string.Empty;
        public string Version { get; set; } = "1.5.0";
        public string Name { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public Dictionary<string, string> Hotkeys { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> CustomHotkeys { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> General { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Forced { get; } = new List<string>();
        public List<string> Ignored { get; } = new List<string>();
    }

    /// <summary>
    /// 键位与设置方案快照管理（支持 3 套本地场景预设槽位与剪贴板紧凑 JSON 导入/导出，零文件/零反射权限消耗）。
    /// 底层读写统一走 PrefsStorage，实现 100% 离线单测无头验证。
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
                    int saved = PrefsStorage.GetInt(ActiveSlotPrefKey, 0);
                    _activeSlotIndex = (saved >= 0 && saved < SlotCount) ? saved : 0;
                }
                return _activeSlotIndex;
            }
            set
            {
                int clamped = (value >= 0 && value < SlotCount) ? value : 0;
                _activeSlotIndex = clamped;
                PrefsStorage.SetInt(ActiveSlotPrefKey, clamped);
                PrefsStorage.Save();
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
                case 0: return BilingualText.Get("方案一", "Preset 1");
                case 1: return BilingualText.Get("方案二", "Preset 2");
                case 2: return BilingualText.Get("方案三", "Preset 3");
                default: return BilingualText.Format("方案 {0}", "Preset {0}", slotIndex + 1);
            }
        }

        public static string GetSlotShortName(int slotIndex)
        {
            return GetSlotDisplayName(slotIndex);
        }

        public static bool HasSlotData(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return false;
            string raw = PrefsStorage.GetString(SlotJsonPrefPrefix + slotIndex);
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
                return BilingualText.Get("<color=#7c8796>未保存</color>", "<color=#7c8796>Not Saved</color>");
            }

            string meta = PrefsStorage.GetString(SlotMetaPrefPrefix + slotIndex);
            if (!string.IsNullOrEmpty(meta))
            {
                string[] parts = meta.Split('|');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out int hkCount) &&
                    int.TryParse(parts[1], out int genCount))
                {
                    string ts = parts[2];
                    return BilingualText.Format(
                        "<color=#86efac>已存: {0} 键 / {1} 项设置</color>  <color=#99a3b0>{2}</color>",
                        "<color=#86efac>Saved: {0} keys / {1} settings</color>  <color=#99a3b0>{2}</color>",
                        hkCount,
                        genCount,
                        ts
                    );
                }
            }

            return BilingualText.Get("<color=#86efac>已保存</color>", "<color=#86efac>Saved</color>");
        }

        public static void ClearSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return;
            PrefsStorage.DeleteKey(SlotJsonPrefPrefix + slotIndex);
            PrefsStorage.DeleteKey(SlotMetaPrefPrefix + slotIndex);
            PrefsStorage.Save();
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
                PrefsStorage.SetString(SlotJsonPrefPrefix + slotIndex, json);
                PrefsStorage.SetString(SlotMetaPrefPrefix + slotIndex, $"{hkCount}|{genCount}|{timestamp}");
                PrefsStorage.Save();
            }
            return json;
        }

        public static bool TryLoadSlotSnapshot(int slotIndex, out ProfileSnapshot? snapshot, out string error)
        {
            snapshot = null;
            if (slotIndex < 0 || slotIndex >= SlotCount)
            {
                error = BilingualText.Get("无效的预设槽位", "Invalid preset slot");
                return false;
            }

            string raw = PrefsStorage.GetString(SlotJsonPrefPrefix + slotIndex);
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = BilingualText.Format(
                    "【{0}】尚未保存任何方案，请先点击「保存方案」",
                    "[{0}] is empty. Click 'Save' first.",
                    GetSlotDisplayName(slotIndex)
                );
                return false;
            }

            return TryParseSnapshotJson(raw, out snapshot, out error);
        }

        public static string ExportToJson(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount) return string.Empty;
            return PrefsStorage.GetString(SlotJsonPrefPrefix + slotIndex);
        }

        public static string ExportToJson(
            IReadOnlyList<ModHotkeyEntry> allHotkeys,
            IReadOnlyList<ModOptionGroup> modGroups,
            string? profileName = null)
        {
            string name = profileName ?? GetSlotDisplayName(ActiveSlotIndex);
            return BuildSnapshotJson(name, allHotkeys, modGroups, out _, out _, out _);
        }

        public static string ExportToJson(ProfileSnapshot snapshot)
        {
            return SerializeSnapshotJson(snapshot);
        }

        public static bool TryImportFromJson(string? rawText, out ProfileSnapshot? snapshot, out string error)
        {
            return TryParseSnapshotJson(rawText, out snapshot, out error);
        }

        public static bool ApplySlot(
            int slotIndex,
            IReadOnlyList<ModHotkeyEntry>? allHotkeys = null,
            IReadOnlyList<ModOptionGroup>? modGroups = null,
            bool applyToUnderlying = false)
        {
            if (!TryLoadSlotSnapshot(slotIndex, out var snapshot, out _) || snapshot == null)
            {
                return false;
            }
            return ApplySnapshot(snapshot, allHotkeys, modGroups, applyToUnderlying);
        }

        public static bool ApplySlot(
            ProfileSnapshot snapshot,
            IReadOnlyList<ModHotkeyEntry>? allHotkeys = null,
            IReadOnlyList<ModOptionGroup>? modGroups = null,
            bool applyToUnderlying = false)
        {
            return ApplySnapshot(snapshot, allHotkeys, modGroups, applyToUnderlying);
        }

        public static bool ApplySnapshot(
            ProfileSnapshot snapshot,
            IReadOnlyList<ModHotkeyEntry>? allHotkeys = null,
            IReadOnlyList<ModOptionGroup>? modGroups = null,
            bool applyToUnderlying = false)
        {
            if (snapshot == null) return false;

            // 1. 恢复强制纳管表与忽略表
            ComboKeyStore.ReplaceForcedAndIgnoredSets(snapshot.Forced, snapshot.Ignored, out _);

            // 2. 恢复自定义/探测热键映射表
            if (snapshot.CustomHotkeys != null && snapshot.CustomHotkeys.Count > 0)
            {
                var customMap = new Dictionary<string, KeyCombo>(snapshot.CustomHotkeys.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in snapshot.CustomHotkeys)
                {
                    if (TryParseComboString(kvp.Value, out var cMods, out var cKey))
                    {
                        customMap[kvp.Key] = new KeyCombo(cMods, cKey);
                    }
                }
                ComboKeyStore.ApplyCustomHotkeysMap(customMap);
            }

            // 3. 逐项恢复原版与所有模组快捷键
            if (allHotkeys != null)
            {
                for (int i = 0; i < allHotkeys.Count; i++)
                {
                    var hk = allHotkeys[i];
                    if (hk == null || string.IsNullOrEmpty(hk.Id)) continue;
                    if (!snapshot.Hotkeys.TryGetValue(hk.Id, out string? comboStr)) continue;
                    if (!TryParseComboString(comboStr, out var targetMods, out var targetKey)) continue;

                    KeyModifierMask finalMods = (hk.SupportsModifierCombos && targetKey != KeyCode.None)
                        ? targetMods
                        : KeyModifierMask.None;

                    hk.PendingModifiers = finalMods;
                    hk.PendingKey = targetKey;
                    hk.HasPendingChange = true;

                    if (targetKey != KeyCode.None)
                    {
                        ComboKeyStore.SetExplicitlyUnbound(hk.Id, false);
                    }
                    else
                    {
                        ComboKeyStore.SetExplicitlyUnbound(hk.Id, true);
                    }

                    if (applyToUnderlying)
                    {
                        if (hk.SetCombo != null)
                        {
                            hk.SetCombo(finalMods, targetKey);
                        }
                        else if (hk.SetKey != null)
                        {
                            hk.SetKey(targetKey);
                        }
                        else
                        {
                            ComboKeyStore.SetSavedCustomHotkey(hk.Id, finalMods, targetKey, hk.DefaultModifiers, hk.DefaultKey);
                        }
                    }
                }
            }

            // 4. 逐项恢复所有模组常规配置 (开关 / 滑块 / 下拉框 / 输入框)
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
                        if (!snapshot.General.TryGetValue(gen.Id, out string? encoded) || string.IsNullOrEmpty(encoded)) continue;

                        if (gen.Kind == OptionKind.Toggle && encoded.StartsWith("b:", StringComparison.OrdinalIgnoreCase))
                        {
                            string valPart = encoded.Substring(2).Trim();
                            bool bVal = valPart == "1" || string.Equals(valPart, "true", StringComparison.OrdinalIgnoreCase);
                            gen.PendingBool = bVal;
                            if (applyToUnderlying && gen.SetBool != null) gen.SetBool(bVal);
                        }
                        else if (gen.Kind == OptionKind.Slider && encoded.StartsWith("f:", StringComparison.OrdinalIgnoreCase))
                        {
                            string valPart = encoded.Substring(2).Trim();
                            if (float.TryParse(valPart, NumberStyles.Float, CultureInfo.InvariantCulture, out float fVal))
                            {
                                float clamped = fVal < gen.Min ? gen.Min : (fVal > gen.Max && gen.Max > gen.Min ? gen.Max : fVal);
                                float finalF = gen.WholeNumbers ? (float)Math.Round(clamped) : clamped;
                                gen.PendingFloat = finalF;
                                if (applyToUnderlying && gen.SetFloat != null) gen.SetFloat(finalF);
                            }
                        }
                        else if (gen.Kind == OptionKind.Dropdown && encoded.StartsWith("i:", StringComparison.OrdinalIgnoreCase))
                        {
                            string valPart = encoded.Substring(2).Trim();
                            if (int.TryParse(valPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iVal) && iVal >= 0)
                            {
                                gen.PendingIndex = iVal;
                                if (applyToUnderlying && gen.SetIndex != null) gen.SetIndex(iVal);
                            }
                        }
                        else if (gen.Kind == OptionKind.Input && !gen.IsReadOnlyInfo && encoded.StartsWith("s:", StringComparison.OrdinalIgnoreCase))
                        {
                            string sVal = encoded.Substring(2);
                            gen.PendingString = sVal;
                            if (applyToUnderlying && gen.SetString != null) gen.SetString(sVal);
                        }
                    }
                }
            }

            return true;
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
            AppendJsonKeyVal(sb, "version", "1.5.0", first: false);
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
                    string comboStr = FormatComboString(mods, key);
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
                string comboStr = FormatComboString(kvp.Value.Modifiers, kvp.Value.Key);
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

        public static string SerializeSnapshotJson(ProfileSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;
            var sb = new StringBuilder(4096);
            sb.Append('{');
            AppendJsonKeyVal(sb, "schema", string.IsNullOrEmpty(snapshot.Schema) ? SchemaId : snapshot.Schema, first: true);
            AppendJsonKeyVal(sb, "version", string.IsNullOrEmpty(snapshot.Version) ? "1.5.0" : snapshot.Version, first: false);
            AppendJsonKeyVal(sb, "name", snapshot.Name ?? "Custom", first: false);
            AppendJsonKeyVal(sb, "timestamp", snapshot.Timestamp ?? string.Empty, first: false);

            sb.Append(",\"hotkeys\":{");
            bool firstHk = true;
            if (snapshot.Hotkeys != null)
            {
                foreach (var kvp in snapshot.Hotkeys)
                {
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    AppendJsonKeyVal(sb, kvp.Key, kvp.Value ?? "None", firstHk);
                    firstHk = false;
                }
            }
            sb.Append('}');

            sb.Append(",\"customHotkeys\":{");
            bool firstCustom = true;
            if (snapshot.CustomHotkeys != null)
            {
                foreach (var kvp in snapshot.CustomHotkeys)
                {
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    AppendJsonKeyVal(sb, kvp.Key, kvp.Value ?? "None", firstCustom);
                    firstCustom = false;
                }
            }
            sb.Append('}');

            sb.Append(",\"general\":{");
            bool firstGen = true;
            if (snapshot.General != null)
            {
                foreach (var kvp in snapshot.General)
                {
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    AppendJsonKeyVal(sb, kvp.Key, kvp.Value ?? string.Empty, firstGen);
                    firstGen = false;
                }
            }
            sb.Append('}');

            sb.Append(",\"forced\":[");
            if (snapshot.Forced != null)
            {
                for (int i = 0; i < snapshot.Forced.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendJsonString(sb, snapshot.Forced[i]);
                }
            }
            sb.Append(']');

            sb.Append(",\"ignored\":[");
            if (snapshot.Ignored != null)
            {
                for (int i = 0; i < snapshot.Ignored.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendJsonString(sb, snapshot.Ignored[i]);
                }
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
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to encode general setting {gen.Id}: {ex.Message}", ex);
                return null;
            }
        }

        public static bool TryParseSnapshotJson(string? rawText, out ProfileSnapshot? snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                error = BilingualText.Get("剪贴板为空，请先复制有效的方案 JSON 文本", "Clipboard is empty. Copy a valid profile JSON first.");
                return false;
            }

            string text = rawText!.Trim();
            int schemaIdx = text.IndexOf(SchemaId, StringComparison.Ordinal);
            int startBrace = schemaIdx >= 0 ? text.LastIndexOf('{', schemaIdx) : text.IndexOf('{');
            int endBrace = text.LastIndexOf('}');
            if (startBrace < 0 || endBrace <= startBrace)
            {
                error = BilingualText.Get("未识别到有效的方案 JSON 格式（缺少 { ... } 结构）", "Invalid profile JSON format (missing { ... } object).");
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
                    error = BilingualText.Get("JSON 顶层结构不是对象", "Root JSON element is not an object.");
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
                        error = BilingualText.Get("JSON 语法错误：键名后缺少冒号", "JSON syntax error: missing ':' after property name.");
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
                ModLogger.Warn($"Failed to parse profile JSON: {ex.Message}", ex);
                error = BilingualText.Format("解析方案 JSON 失败: {0}", "Failed to parse profile JSON: {0}", ex.Message);
                return false;
            }

            if (!string.IsNullOrEmpty(parsed.Schema) && !string.Equals(parsed.Schema, SchemaId, StringComparison.OrdinalIgnoreCase))
            {
                error = BilingualText.Format("不兼容的方案版本或格式 (schema: {0})", "Incompatible profile schema ({0})", parsed.Schema);
                return false;
            }

            if (!string.Equals(parsed.Schema, SchemaId, StringComparison.OrdinalIgnoreCase) &&
                parsed.Hotkeys.Count == 0 &&
                parsed.General.Count == 0 &&
                parsed.CustomHotkeys.Count == 0)
            {
                error = BilingualText.Get("剪贴板内容不是有效的本模组键位方案 JSON", "Clipboard content is not a valid hotkey profile JSON.");
                return false;
            }

            snapshot = parsed;
            return true;
        }

        public static string FormatComboString(KeyModifierMask mods, KeyCode key)
        {
            if (key == KeyCode.None) return "None";
            mods &= ~KeyCombo.GetModifierBit(key);
            if (mods == KeyModifierMask.None) return key.ToString();

            var sb = new StringBuilder(24);
            if ((mods & KeyModifierMask.Ctrl) != 0) sb.Append("Ctrl+");
            if ((mods & KeyModifierMask.Shift) != 0) sb.Append("Shift+");
            if ((mods & KeyModifierMask.Alt) != 0) sb.Append("Alt+");
            sb.Append(key.ToString());
            return sb.ToString();
        }

        public static bool TryParseComboString(string? raw, out KeyModifierMask mods, out KeyCode key)
        {
            mods = KeyModifierMask.None;
            key = KeyCode.None;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string trimmed = raw!.Trim();
            if (trimmed.Length == 0 ||
                string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase) ||
                trimmed == "0")
            {
                key = KeyCode.None;
                return true;
            }

            if (trimmed.IndexOf('+') < 0)
            {
                if (char.IsDigit(trimmed[0])) return false;
                if (Enum.TryParse<KeyCode>(trimmed, true, out var singleKey) &&
                    Enum.IsDefined(typeof(KeyCode), singleKey))
                {
                    key = singleKey;
                    return true;
                }
                return false;
            }

            string[] parts = trimmed.Split('+');
            bool sawKey = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string tok = parts[i].Trim();
                if (tok.Length == 0) return false;

                if (string.Equals(tok, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tok, "Control", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tok, "LCtrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tok, "RCtrl", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= KeyModifierMask.Ctrl;
                }
                else if (string.Equals(tok, "Shift", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(tok, "LShift", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(tok, "RShift", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= KeyModifierMask.Shift;
                }
                else if (string.Equals(tok, "Alt", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(tok, "LAlt", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(tok, "RAlt", StringComparison.OrdinalIgnoreCase))
                {
                    mods |= KeyModifierMask.Alt;
                }
                else if (i == parts.Length - 1 &&
                         !char.IsDigit(tok[0]) &&
                         Enum.TryParse<KeyCode>(tok, true, out var parsedKey) &&
                         Enum.IsDefined(typeof(KeyCode), parsedKey))
                {
                    key = parsedKey;
                    sawKey = true;
                }
                else
                {
                    return false;
                }
            }

            if (!sawKey || key == KeyCode.None) return false;
            mods &= ~KeyCombo.GetModifierBit(key);
            return true;
        }

        #region 剪贴板安全操作封装

        public static bool TryExportToClipboard(
            IReadOnlyList<ModHotkeyEntry> allHotkeys,
            IReadOnlyList<ModOptionGroup> modGroups,
            out string json,
            out string error)
        {
            error = string.Empty;
            try
            {
                string name = GetSlotDisplayName(ActiveSlotIndex);
                json = BuildSnapshotJson(name, allHotkeys, modGroups, out _, out _, out _);
                SetSystemCopyBufferSafe(json);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to export profile to clipboard: {ex.Message}", ex);
                error = ex.Message;
                json = string.Empty;
                return false;
            }
        }

        public static bool TryImportFromClipboard(out ProfileSnapshot? snapshot, out string error)
        {
            string raw = GetSystemCopyBufferSafe();
            return TryParseSnapshotJson(raw, out snapshot, out error);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetSystemCopyBufferSafe(string text)
        {
            if (PrefsStorage.UseInMemoryPrefsForTests) return;
            try
            {
                GUIUtility.systemCopyBuffer = text;
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to set GUIUtility.systemCopyBuffer: {ex.Message}", ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetSystemCopyBufferSafe()
        {
            if (PrefsStorage.UseInMemoryPrefsForTests) return string.Empty;
            try
            {
                return GUIUtility.systemCopyBuffer ?? string.Empty;
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"Failed to read GUIUtility.systemCopyBuffer: {ex.Message}", ex);
                return string.Empty;
            }
        }

        #endregion

        #region JSON 序列化与解析辅助

        private static void AppendJsonKeyVal(StringBuilder sb, string key, string val, bool first)
        {
            if (!first) sb.Append(',');
            AppendJsonString(sb, key);
            sb.Append(':');
            AppendJsonString(sb, val);
        }

        private static void AppendJsonString(StringBuilder sb, string? value)
        {
            sb.Append('\"');
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value!.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '\"': sb.Append("\\\""); break;
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
            sb.Append('\"');
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        private static bool TryReadJsonString(string s, ref int pos, out string result)
        {
            result = string.Empty;
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] != '\"') return false;
            pos++; // skip opening '"'

            var sb = new StringBuilder(32);
            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == '\"')
                {
                    result = sb.ToString();
                    return true;
                }
                if (c == '\\' && pos < s.Length)
                {
                    char esc = s[pos++];
                    switch (esc)
                    {
                        case '\"': sb.Append('\"'); break;
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
            if (c == '\"')
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
                    if (cur == '\"')
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

        #endregion
    }
}
