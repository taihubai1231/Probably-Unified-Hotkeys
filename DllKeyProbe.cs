using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DseCecil;
using DseCecil.Cil;
using UnityEngine;

namespace ProbablyUnifiedHotkeys
{
    public sealed class ProbedKeyRecord
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public KeyModifierMask Modifiers { get; set; } = KeyModifierMask.None;
        public KeyCode Key { get; set; } = KeyCode.None;
        public string SourceKindZh { get; set; } = string.Empty;
        public string SourceKindEn { get; set; } = string.Empty;
        public string SourceDetail { get; set; } = string.Empty;
        public string? PlayerPrefsKey { get; set; }
    }

    /// <summary>
    /// 基于 DseCecil 静态 IL 扫描（files 权限）与只读 ModData 配置探测的黑盒热键发现引擎。
    /// 仅作只读冲突提醒与排查可视化，不修改或拦截任何第三方模组逻辑。
    /// </summary>
    public static class DllKeyProbe
    {
        private const string CacheModDataFile = "dll_probe_cache_v1.txt";
        private const int MaxConfigFileBytes = 262144;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, (string Fingerprint, List<ProbedKeyRecord> Records)> MemoryCache
            = new Dictionary<string, (string, List<ProbedKeyRecord>)>(StringComparer.OrdinalIgnoreCase);

        private static bool _diskCacheLoaded = false;

        private static readonly Regex JsonPairRegex = new Regex(
            "\"([A-Za-z0-9_.-]+)\"\\s*:\\s*(?:\"([^\"]*)\"|(-?\\d+))",
            RegexOptions.Compiled);

        private static readonly HashSet<string> KnownVanillaKeybindIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "interact", "rotate_cw", "rotate_ccw", "flip", "discard", "call_next_customer",
            "quick_move_inventory", "quick_move_showcase", "quick_move_storage", "pause",
            "secondary_click", "flip_item_alt"
        };

        public static void InvalidateAll()
        {
            lock (CacheLock)
            {
                MemoryCache.Clear();
                _diskCacheLoaded = true;
                try { ModHelper.DeleteModDataFile(I18n.ModId, CacheModDataFile); } catch { }
            }
        }

        public static void InvalidateMod(string? modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            lock (CacheLock)
            {
                EnsureDiskCacheLoaded();
                MemoryCache.Remove(modId!);
                SaveDiskCache();
            }
        }

        public static List<ProbedKeyRecord> ProbeMod(LoadedMod mod)
        {
            if (mod?.Manifest == null || string.IsNullOrEmpty(mod.Manifest.ID))
            {
                return new List<ProbedKeyRecord>();
            }

            string modId = mod.Manifest.ID;
            var dllPaths = CollectModDllPaths(mod);
            List<string>? modDataFiles = null;
            try { modDataFiles = ModHelper.ListModDataFiles(modId); } catch { }

            string fingerprint = BuildFingerprint(mod.FolderPath, mod.Manifest.ModVersion, dllPaths, modDataFiles);

            lock (CacheLock)
            {
                EnsureDiskCacheLoaded();
                if (MemoryCache.TryGetValue(modId, out var cached) &&
                    string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return CloneAndRefreshLivePrefs(cached.Records);
                }
            }

            var discovered = ScanModUncached(modId, mod.FolderPath, dllPaths, modDataFiles);

            lock (CacheLock)
            {
                MemoryCache[modId] = (fingerprint, discovered);
                SaveDiskCache();
            }

            return CloneAndRefreshLivePrefs(discovered);
        }

        public static ModHotkeyEntry ToReadOnlyHotkeyEntry(LoadedMod mod, ProbedKeyRecord rec)
        {
            string modId = mod.Manifest?.ID ?? "unknown";
            string modName = string.IsNullOrEmpty(mod.Manifest?.Name) ? modId : mod.Manifest!.Name;
            string kindLabel = I18n.Get(rec.SourceKindZh, rec.SourceKindEn);
            string detail = $"{kindLabel}: {rec.SourceDetail}";

            return new ModHotkeyEntry
            {
                Id = $"probed:{modId}:{rec.Id}",
                RawOptionId = rec.Id,
                ModId = modId,
                ModDisplayName = modName,
                DisplayName = rec.DisplayName,
                Tooltip = I18n.Format(
                    "<color=#99a3b0>[只读自动探测 · 无法由本模组修改]\n来源：{0}\n说明：该热键来自代码硬编码或独立配置文件，用于提前提示撞键冲突。</color>",
                    "<color=#99a3b0>[Read-Only Auto-Probed]\nSource: {0}\nDetected from code or standalone config for conflict warning.</color>",
                    detail),
                IsReadOnlyProbed = true,
                SupportsModifierCombos = rec.Modifiers != KeyModifierMask.None,
                DefaultKey = rec.Key,
                DefaultModifiers = rec.Modifiers,
                ProbedSourceDetail = detail,
                GetKey = () => ResolveLiveKey(rec),
                GetExternalModifiers = () => rec.Modifiers
            };
        }

        private static KeyCode ResolveLiveKey(ProbedKeyRecord rec)
        {
            if (!string.IsNullOrEmpty(rec.PlayerPrefsKey))
            {
                try
                {
                    if (PlayerPrefs.HasKey(rec.PlayerPrefsKey))
                    {
                        int v = PlayerPrefs.GetInt(rec.PlayerPrefsKey, (int)rec.Key);
                        if (Enum.IsDefined(typeof(KeyCode), v))
                        {
                            return (KeyCode)v;
                        }
                    }
                }
                catch { }
            }
            return rec.Key;
        }

        private static List<ProbedKeyRecord> CloneAndRefreshLivePrefs(List<ProbedKeyRecord> src)
        {
            var result = new List<ProbedKeyRecord>(src.Count);
            foreach (var r in src)
            {
                KeyCode key = ResolveLiveKey(r);
                result.Add(new ProbedKeyRecord
                {
                    Id = r.Id,
                    DisplayName = r.DisplayName,
                    Modifiers = r.Modifiers,
                    Key = key,
                    SourceKindZh = r.SourceKindZh,
                    SourceKindEn = r.SourceKindEn,
                    SourceDetail = r.SourceDetail,
                    PlayerPrefsKey = r.PlayerPrefsKey
                });
            }
            return result;
        }

        private static List<string> CollectModDllPaths(LoadedMod mod)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(mod.DllPath) && File.Exists(mod.DllPath) && seen.Add(mod.DllPath))
            {
                list.Add(mod.DllPath);
            }

            if (!string.IsNullOrEmpty(mod.FolderPath) && mod.Manifest?.Assemblies != null)
            {
                foreach (string rel in mod.Manifest.Assemblies)
                {
                    if (string.IsNullOrWhiteSpace(rel)) continue;
                    string full = Path.Combine(mod.FolderPath, rel);
                    if (File.Exists(full) && seen.Add(full))
                    {
                        list.Add(full);
                    }
                }
            }

            return list;
        }

        private static string BuildFingerprint(
            string? folderPath,
            string? modVersion,
            List<string> dllPaths,
            List<string>? modDataFiles)
        {
            var sb = new StringBuilder(256);
            sb.Append("V:").Append(modVersion ?? string.Empty).Append('|');

            foreach (string path in dllPaths)
            {
                try
                {
                    var fi = new FileInfo(path);
                    sb.Append(fi.Name).Append(':').Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append('|');
                }
                catch
                {
                    sb.Append(path).Append(":err|");
                }
            }

            if (modDataFiles != null && modDataFiles.Count > 0)
            {
                sb.Append("MD:");
                int added = 0;
                foreach (string rel in modDataFiles)
                {
                    if (!IsSupportedConfigExtension(rel)) continue;
                    try
                    {
                        string? txt = ModHelper.ReadModDataFileText(folderPath != null ? Path.GetFileName(folderPath) : string.Empty, rel);
                        sb.Append(rel).Append('=').Append(txt?.Length ?? 0).Append(',');
                    }
                    catch
                    {
                        sb.Append(rel).Append(',');
                    }
                    if (++added >= 12) break;
                }
            }

            return sb.ToString();
        }

        private static List<ProbedKeyRecord> ScanModUncached(
            string modId,
            string? folderPath,
            List<string> dllPaths,
            List<string>? modDataFiles)
        {
            var records = new List<ProbedKeyRecord>();
            var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 扫描 ModData/<modId>/ 下可读的外部自定义配置文本（.txt / .json / .cfg / .ini）
            if (modDataFiles != null)
            {
                foreach (string rel in modDataFiles)
                {
                    if (!IsSupportedConfigExtension(rel)) continue;
                    string? content = null;
                    try { content = ModHelper.ReadModDataFileText(modId, rel); } catch { }
                    if (string.IsNullOrEmpty(content) || content!.Length > MaxConfigFileBytes) continue;

                    ScanConfigText(
                        content,
                        "ModData: " + rel,
                        "ModData 配置",
                        "ModData Config",
                        records,
                        dedup);
                }
            }

            // 2. 扫描模组安装目录下的小型外部 .json / .cfg / .ini 配置文件（排除 manifest/readme/本地化等）
            if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
            {
                try
                {
                    foreach (string file in System.IO.Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly))
                    {
                        string ext = Path.GetExtension(file);
                        if (!string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(ext, ".cfg", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(ext, ".ini", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var fi = new FileInfo(file);
                        if (fi.Length <= 0 || fi.Length > MaxConfigFileBytes) continue;
                        string content = File.ReadAllText(file, Encoding.UTF8);
                        ScanConfigText(
                            content,
                            "File: " + fi.Name,
                            "模组目录配置",
                            "Mod File Config",
                            records,
                            dedup);
                    }
                }
                catch { }
            }

            // 3. 使用 DseCecil 静态解析模组 DLL IL 指令中的 KeyCode / 鼠标侧键 / PlayerPrefs
            foreach (string dllPath in dllPaths)
            {
                try
                {
                    ScanAssemblyWithDseCecil(dllPath, records, dedup);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ProbablyUnifiedHotkeys] DllKeyProbe error scanning '{Path.GetFileName(dllPath)}': {ex.Message}");
                }
            }

            return records;
        }

        private static bool IsSupportedConfigExtension(string path)
        {
            return path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase);
        }

        private static void ScanConfigText(
            string content,
            string sourceDetail,
            string kindZh,
            string kindEn,
            List<ProbedKeyRecord> records,
            HashSet<string> dedup)
        {
            // JSON 键值对："OpenKey": "F8" / "hotkey": 289
            foreach (Match m in JsonPairRegex.Matches(content))
            {
                string keyName = m.Groups[1].Value;
                if (!LooksLikeConfigKeyName(keyName)) continue;

                string? strVal = m.Groups[2].Success ? m.Groups[2].Value : null;
                string? intStr = m.Groups[3].Success ? m.Groups[3].Value : null;

                TryAddConfigParsedKey(keyName, strVal, intStr, sourceDetail, kindZh, kindEn, records, dedup);
            }

            // 行式 key=value 或 key: value（常见于 settings.txt / alarm.txt / hotkey.txt）
            using var reader = new StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) ||
                    trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    continue;
                }

                int sep = trimmed.IndexOf('=');
                if (sep <= 0) sep = trimmed.IndexOf(':');
                if (sep <= 0 || sep >= trimmed.Length - 1) continue;

                string keyName = trimmed.Substring(0, sep).Trim().Trim('"');
                string rawVal = trimmed.Substring(sep + 1).Trim().Trim(',', '"');
                if (!LooksLikeConfigKeyName(keyName)) continue;

                if (int.TryParse(rawVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    TryAddConfigParsedKey(keyName, null, rawVal, sourceDetail, kindZh, kindEn, records, dedup);
                }
                else
                {
                    TryAddConfigParsedKey(keyName, rawVal, null, sourceDetail, kindZh, kindEn, records, dedup);
                }
            }
        }

        private static bool LooksLikeConfigKeyName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("hotkey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("bind", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("shortcut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("trigger", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TryAddConfigParsedKey(
            string keyName,
            string? strVal,
            string? intStr,
            string sourceDetail,
            string kindZh,
            string kindEn,
            List<ProbedKeyRecord> records,
            HashSet<string> dedup)
        {
            KeyModifierMask mods = KeyModifierMask.None;
            KeyCode key = KeyCode.None;

            if (!string.IsNullOrEmpty(strVal))
            {
                if (!ModOptionScanner.TryParseComboString(strVal, out mods, out key)) return;
            }
            else if (!string.IsNullOrEmpty(intStr) &&
                     int.TryParse(intStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            {
                if (!Enum.IsDefined(typeof(KeyCode), intVal)) return;
                key = (KeyCode)intVal;
            }

            if (IsBoilerplateOrNoiseKey(mods, key)) return;

            string dedupKey = $"cfg:{(int)mods}:{(int)key}:{keyName}";
            if (!dedup.Add(dedupKey)) return;

            records.Add(new ProbedKeyRecord
            {
                Id = $"cfg_{NormalizeId(keyName)}_{(int)mods}_{(int)key}",
                DisplayName = keyName,
                Modifiers = mods,
                Key = key,
                SourceKindZh = kindZh,
                SourceKindEn = kindEn,
                SourceDetail = $"{sourceDetail} ({keyName})"
            });
        }

        private static void ScanAssemblyWithDseCecil(
            string dllPath,
            List<ProbedKeyRecord> records,
            HashSet<string> dedup)
        {
            using var asm = AssemblyDefinition.ReadAssembly(dllPath);
            var fieldDefaults = new Dictionary<string, KeyCode>(StringComparer.Ordinal);
            var getterFieldMap = new Dictionary<string, string>(StringComparer.Ordinal);

            // Pass 1：收集静态/实例构造器写入的 KeyCode 默认字段，以及简单 get_ 属性映射
            foreach (var module in asm.Modules)
            {
                CollectTypeTreeDefaults(module.Types, fieldDefaults, getterFieldMap);
            }

            // Pass 2：扫描所有方法体中的 Input.GetKey* / GetMouseButton* / PlayerPrefs.GetInt / KeyBindHelper
            foreach (var module in asm.Modules)
            {
                ScanTypeTreeMethods(module.Types, fieldDefaults, getterFieldMap, records, dedup);
            }
        }

        private static void CollectTypeTreeDefaults(
            Mono.Collections.Generic.Collection<TypeDefinition> types,
            Dictionary<string, KeyCode> fieldDefaults,
            Dictionary<string, string> getterFieldMap)
        {
            foreach (var t in types)
            {
                if (t.HasNestedTypes)
                {
                    CollectTypeTreeDefaults(t.NestedTypes, fieldDefaults, getterFieldMap);
                }

                foreach (var m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    var insns = m.Body.Instructions;

                    // 收集 .ctor / .cctor / Init 中直接写入字段的 KeyCode 常量
                    for (int i = 1; i < insns.Count; i++)
                    {
                        var ins = insns[i];
                        if ((ins.OpCode.Code == Code.Stsfld || ins.OpCode.Code == Code.Stfld) &&
                            ins.Operand is FieldReference fr &&
                            (fr.FieldType.FullName == "UnityEngine.KeyCode" ||
                             (fr.FieldType.FullName == "System.Int32" && LooksLikeConfigKeyName(fr.Name))) &&
                            TryFindPrecedingIntConstant(insns, i - 1, out int val) &&
                            Enum.IsDefined(typeof(KeyCode), val))
                        {
                            string fKey = fr.DeclaringType.FullName + "::" + fr.Name;
                            if (!fieldDefaults.ContainsKey(fKey) || m.IsConstructor)
                            {
                                fieldDefaults[fKey] = (KeyCode)val;
                            }
                        }
                    }

                    // 收集简短属性 get_Xxx -> ldfld/ldsfld
                    if (m.IsGetter && insns.Count <= 8)
                    {
                        for (int i = 0; i < insns.Count; i++)
                        {
                            if ((insns[i].OpCode.Code == Code.Ldfld || insns[i].OpCode.Code == Code.Ldsfld) &&
                                insns[i].Operand is FieldReference gfr)
                            {
                                getterFieldMap[m.DeclaringType.FullName + "::" + m.Name] = gfr.DeclaringType.FullName + "::" + gfr.Name;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private static void ScanTypeTreeMethods(
            Mono.Collections.Generic.Collection<TypeDefinition> types,
            Dictionary<string, KeyCode> fieldDefaults,
            Dictionary<string, string> getterFieldMap,
            List<ProbedKeyRecord> records,
            HashSet<string> dedup)
        {
            foreach (var t in types)
            {
                if (t.HasNestedTypes)
                {
                    ScanTypeTreeMethods(t.NestedTypes, fieldDefaults, getterFieldMap, records, dedup);
                }

                foreach (var m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    var insns = m.Body.Instructions;

                    for (int i = 0; i < insns.Count; i++)
                    {
                        var ins = insns[i];
                        if ((ins.OpCode.Code != Code.Call && ins.OpCode.Code != Code.Callvirt) ||
                            ins.Operand is not MethodReference mr)
                        {
                            continue;
                        }

                        string declName = mr.DeclaringType?.FullName ?? string.Empty;
                        string mName = mr.Name ?? string.Empty;

                        if (declName == "UnityEngine.Input" &&
                            (mName == "GetKeyDown" || mName == "GetKey" || mName == "GetKeyUp") &&
                            mr.Parameters.Count == 1 &&
                            mr.Parameters[0].ParameterType.FullName == "UnityEngine.KeyCode")
                        {
                            HandleInputGetKeyCall(t, m, insns, i, fieldDefaults, getterFieldMap, records, dedup);
                        }
                        else if (declName == "UnityEngine.Input" &&
                                 (mName == "GetMouseButtonDown" || mName == "GetMouseButton" || mName == "GetMouseButtonUp") &&
                                 mr.Parameters.Count == 1)
                        {
                            if (TryFindPrecedingIntConstant(insns, i - 1, out int btnIdx) && btnIdx >= 2 && btnIdx <= 6)
                            {
                                KeyCode mc = KeyCode.Mouse0 + btnIdx;
                                KeyModifierMask mods = DetectNearbyModifiers(insns, i);
                                string detail = $"{FormatTypeMethod(t, m)} -> Input.{mName}({mc})";
                                string label = BuildReadableLabel(t.Name, m.Name, mc.ToString());
                                AddIlRecord(label, mods, mc, null, "IL 鼠标探测", "IL Mouse Probe", detail, records, dedup);
                            }
                        }
                        else if (declName == "UnityEngine.PlayerPrefs" &&
                                 mName == "GetInt" &&
                                 mr.Parameters.Count == 2)
                        {
                            HandlePlayerPrefsGetInt(t, m, insns, i, records, dedup);
                        }
                        else if (declName == "KeyBindHelper" &&
                                 (mName == "GetKeyDown" || mName == "GetKey" || mName == "Get") &&
                                 mr.Parameters.Count == 1 &&
                                 mr.Parameters[0].ParameterType.FullName == "System.String" &&
                                 i > 0 &&
                                 insns[i - 1].OpCode.Code == Code.Ldstr &&
                                 insns[i - 1].Operand is string bindId &&
                                 !KnownVanillaKeybindIds.Contains(bindId))
                        {
                            // 若未走官方注册表而直接使用 KeyBindHelper.GetKeyDown("custom_id")，记录提示
                            string detail = $"{FormatTypeMethod(t, m)} -> KeyBindHelper.{mName}(\"{bindId}\")";
                            KeyCode liveKey = KeyCode.None;
                            try { liveKey = KeyBindHelper.Get(bindId); } catch { }
                            if (!IsBoilerplateOrNoiseKey(KeyModifierMask.None, liveKey))
                            {
                                AddIlRecord(bindId, KeyModifierMask.None, liveKey, null, "IL 原生绑定调用", "IL KeyBindHelper", detail, records, dedup);
                            }
                        }
                    }
                }
            }
        }

        private static void HandleInputGetKeyCall(
            TypeDefinition t,
            MethodDefinition m,
            Mono.Collections.Generic.Collection<Instruction> insns,
            int callIndex,
            Dictionary<string, KeyCode> fieldDefaults,
            Dictionary<string, string> getterFieldMap,
            List<ProbedKeyRecord> records,
            HashSet<string> dedup)
        {
            if (callIndex <= 0) return;

            Instruction argIns = insns[callIndex - 1];
            KeyModifierMask mods = DetectNearbyModifiers(insns, callIndex);

            // 1. 直接写死的整型 KeyCode 常量（例如 ldc.i4 269）
            if (TryGetIntConstant(argIns, out int rawKey))
            {
                if (!Enum.IsDefined(typeof(KeyCode), rawKey)) return;
                KeyCode kc = (KeyCode)rawKey;
                if (IsModifierOnlyKeyCode(kc)) return;
                if (IsBoilerplateOrNoiseKey(mods, kc)) return;

                string detail = $"{FormatTypeMethod(t, m)} -> {kc}";
                string label = BuildReadableLabel(t.Name, m.Name, kc.ToString());
                AddIlRecord(label, mods, kc, null, "IL 硬编码", "IL Hardcoded", detail, records, dedup);
                return;
            }

            // 2. 字段或属性读取的 KeyCode（例如 ldsfld ClockAlarm::OpenKey / call get_OpenKey）
            string? fieldKey = null;
            string? shortFieldName = null;
            if ((argIns.OpCode.Code == Code.Ldsfld || argIns.OpCode.Code == Code.Ldfld) &&
                argIns.Operand is FieldReference fr)
            {
                fieldKey = fr.DeclaringType.FullName + "::" + fr.Name;
                shortFieldName = fr.Name;
            }
            else if ((argIns.OpCode.Code == Code.Call || argIns.OpCode.Code == Code.Callvirt) &&
                     argIns.Operand is MethodReference propMr &&
                     propMr.Name.StartsWith("get_", StringComparison.Ordinal))
            {
                string getterKey = propMr.DeclaringType.FullName + "::" + propMr.Name;
                if (getterFieldMap.TryGetValue(getterKey, out string? mappedField))
                {
                    fieldKey = mappedField;
                }
                shortFieldName = propMr.Name.Substring(4);
            }

            if (fieldKey != null &&
                fieldDefaults.TryGetValue(fieldKey, out KeyCode resolved) &&
                !IsModifierOnlyKeyCode(resolved) &&
                !IsBoilerplateOrNoiseKey(mods, resolved))
            {
                string detail = $"{FormatTypeMethod(t, m)} ({shortFieldName})";
                string label = BuildReadableLabel(t.Name, m.Name, shortFieldName ?? resolved.ToString());
                AddIlRecord(label, mods, resolved, null, "IL 字段默认值", "IL Field Default", detail, records, dedup);
            }
        }

        private static void HandlePlayerPrefsGetInt(
            TypeDefinition t,
            MethodDefinition m,
            Mono.Collections.Generic.Collection<Instruction> insns,
            int callIndex,
            List<ProbedKeyRecord> records,
            HashSet<string> dedup)
        {
            if (callIndex < 2) return;
            if (!TryGetIntConstant(insns[callIndex - 1], out int defInt)) return;

            string? prefKey = null;
            for (int k = callIndex - 2; k >= Math.Max(0, callIndex - 4); k--)
            {
                if (insns[k].OpCode.Code == Code.Ldstr && insns[k].Operand is string s)
                {
                    prefKey = s;
                    break;
                }
            }
            if (string.IsNullOrEmpty(prefKey) || !LooksLikeConfigKeyName(prefKey!)) return;
            if (!Enum.IsDefined(typeof(KeyCode), defInt)) return;

            KeyCode kc = (KeyCode)defInt;
            if (IsBoilerplateOrNoiseKey(KeyModifierMask.None, kc)) return;

            string detail = $"PlayerPrefs[{prefKey}] @ {FormatTypeMethod(t, m)}";
            AddIlRecord(prefKey!, KeyModifierMask.None, kc, prefKey, "PlayerPrefs 私存", "PlayerPrefs Key", detail, records, dedup);
        }

        private static KeyModifierMask DetectNearbyModifiers(Mono.Collections.Generic.Collection<Instruction> insns, int callIndex)
        {
            KeyModifierMask mask = KeyModifierMask.None;
            int from = Math.Max(0, callIndex - 26);
            int to = Math.Min(insns.Count - 1, callIndex + 4);

            for (int k = from + 1; k <= to; k++)
            {
                var ins = insns[k];
                if ((ins.OpCode.Code != Code.Call && ins.OpCode.Code != Code.Callvirt) ||
                    ins.Operand is not MethodReference mr ||
                    mr.DeclaringType?.FullName != "UnityEngine.Input" ||
                    mr.Name != "GetKey")
                {
                    continue;
                }

                if (TryGetIntConstant(insns[k - 1], out int modKey))
                {
                    if (modKey == (int)KeyCode.LeftControl || modKey == (int)KeyCode.RightControl) mask |= KeyModifierMask.Ctrl;
                    else if (modKey == (int)KeyCode.LeftShift || modKey == (int)KeyCode.RightShift) mask |= KeyModifierMask.Shift;
                    else if (modKey == (int)KeyCode.LeftAlt || modKey == (int)KeyCode.RightAlt) mask |= KeyModifierMask.Alt;
                }
            }

            // 通用组合键分派器往往同时穷举全部 3 种修饰键，避免把全修饰误粘给默认主键
            if (mask == (KeyModifierMask.Ctrl | KeyModifierMask.Shift | KeyModifierMask.Alt))
            {
                return KeyModifierMask.None;
            }
            return mask;
        }

        private static bool TryFindPrecedingIntConstant(Mono.Collections.Generic.Collection<Instruction> insns, int startIndex, out int value)
        {
            for (int i = startIndex; i >= Math.Max(0, startIndex - 2); i--)
            {
                if (TryGetIntConstant(insns[i], out value)) return true;
                if (insns[i].OpCode.Code != Code.Dup && insns[i].OpCode.Code != Code.Conv_I4) break;
            }
            value = 0;
            return false;
        }

        private static bool TryGetIntConstant(Instruction ins, out int value)
        {
            switch (ins.OpCode.Code)
            {
                case Code.Ldc_I4:
                    value = (int)ins.Operand;
                    return true;
                case Code.Ldc_I4_S:
                    value = (sbyte)ins.Operand;
                    return true;
                case Code.Ldc_I4_0: value = 0; return true;
                case Code.Ldc_I4_1: value = 1; return true;
                case Code.Ldc_I4_2: value = 2; return true;
                case Code.Ldc_I4_3: value = 3; return true;
                case Code.Ldc_I4_4: value = 4; return true;
                case Code.Ldc_I4_5: value = 5; return true;
                case Code.Ldc_I4_6: value = 6; return true;
                case Code.Ldc_I4_7: value = 7; return true;
                case Code.Ldc_I4_8: value = 8; return true;
                case Code.Ldc_I4_M1: value = -1; return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private static bool IsModifierOnlyKeyCode(KeyCode kc)
        {
            return (kc >= KeyCode.RightShift && kc <= KeyCode.LeftAlt) ||
                   (kc >= KeyCode.LeftWindows && kc <= KeyCode.Menu);
        }

        private static bool IsBoilerplateOrNoiseKey(KeyModifierMask mods, KeyCode kc)
        {
            if (kc == KeyCode.None) return true;
            if (mods == KeyModifierMask.None)
            {
                if (kc == KeyCode.Escape ||
                    kc == KeyCode.Return ||
                    kc == KeyCode.Mouse0 ||
                    kc == KeyCode.Mouse1 ||
                    kc == KeyCode.KeypadEnter)
                {
                    return true;
                }
            }
            return IsModifierOnlyKeyCode(kc);
        }

        private static void AddIlRecord(
            string displayName,
            KeyModifierMask mods,
            KeyCode key,
            string? playerPrefsKey,
            string kindZh,
            string kindEn,
            string sourceDetail,
            List<ProbedKeyRecord> records,
            HashSet<string> dedup)
        {
            string dedupKey = $"il:{(int)mods}:{(int)key}:{displayName}";
            if (!dedup.Add(dedupKey)) return;

            records.Add(new ProbedKeyRecord
            {
                Id = $"il_{NormalizeId(displayName)}_{(int)mods}_{(int)key}",
                DisplayName = displayName,
                Modifiers = mods,
                Key = key,
                PlayerPrefsKey = playerPrefsKey,
                SourceKindZh = kindZh,
                SourceKindEn = kindEn,
                SourceDetail = sourceDetail
            });
        }

        private static string FormatTypeMethod(TypeDefinition t, MethodDefinition m)
        {
            string typeName = t.Name;
            int tick = typeName.IndexOf('`');
            if (tick > 0) typeName = typeName.Substring(0, tick);
            if (t.DeclaringType != null) typeName = t.DeclaringType.Name + "." + typeName;
            return $"{typeName}.{m.Name}";
        }

        private static string BuildReadableLabel(string typeName, string methodName, string hintName)
        {
            string cleanType = typeName.Replace("`1", string.Empty);
            if (!string.IsNullOrEmpty(hintName) && !string.Equals(hintName, "Update", StringComparison.OrdinalIgnoreCase))
            {
                return $"{cleanType}.{hintName}";
            }
            return $"{cleanType}.{methodName}";
        }

        private static string NormalizeId(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "item";
            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        private static void EnsureDiskCacheLoaded()
        {
            if (_diskCacheLoaded) return;
            _diskCacheLoaded = true;
            try
            {
                string? raw = ModHelper.ReadModDataFileText(I18n.ModId, CacheModDataFile);
                if (string.IsNullOrEmpty(raw)) return;

                using var reader = new StringReader(raw);
                string? line;
                string? curModId = null;
                string curFp = string.Empty;
                List<ProbedKeyRecord>? curList = null;

                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("M\t", StringComparison.Ordinal))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length >= 3)
                        {
                            curModId = p[1];
                            curFp = p[2];
                            curList = new List<ProbedKeyRecord>();
                            MemoryCache[curModId] = (curFp, curList);
                        }
                    }
                    else if (line.StartsWith("K\t", StringComparison.Ordinal) && curList != null)
                    {
                        string[] p = line.Split('\t');
                        if (p.Length >= 8 &&
                            int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int modsVal) &&
                            int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int keyVal) &&
                            Enum.IsDefined(typeof(KeyCode), keyVal))
                        {
                            curList.Add(new ProbedKeyRecord
                            {
                                Id = Unesc(p[1]),
                                DisplayName = Unesc(p[2]),
                                Modifiers = (KeyModifierMask)modsVal,
                                Key = (KeyCode)keyVal,
                                SourceKindZh = Unesc(p[5]),
                                SourceKindEn = Unesc(p[6]),
                                SourceDetail = Unesc(p[7]),
                                PlayerPrefsKey = p.Length >= 9 && !string.IsNullOrEmpty(p[8]) ? Unesc(p[8]) : null
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private static void SaveDiskCache()
        {
            try
            {
                var sb = new StringBuilder(1024);
                foreach (var kvp in MemoryCache)
                {
                    sb.Append("M\t").Append(Esc(kvp.Key)).Append('\t').Append(Esc(kvp.Value.Fingerprint)).Append('\n');
                    foreach (var r in kvp.Value.Records)
                    {
                        sb.Append("K\t")
                          .Append(Esc(r.Id)).Append('\t')
                          .Append(Esc(r.DisplayName)).Append('\t')
                          .Append((int)r.Modifiers).Append('\t')
                          .Append((int)r.Key).Append('\t')
                          .Append(Esc(r.SourceKindZh)).Append('\t')
                          .Append(Esc(r.SourceKindEn)).Append('\t')
                          .Append(Esc(r.SourceDetail)).Append('\t')
                          .Append(Esc(r.PlayerPrefsKey ?? string.Empty))
                          .Append('\n');
                    }
                }
                ModHelper.WriteModDataFileText(I18n.ModId, CacheModDataFile, sb.ToString());
            }
            catch { }
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
        }
    }
}
