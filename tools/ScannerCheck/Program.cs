using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

internal static class Program
{
    private static readonly HashSet<string> Resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--inspect")
        {
            return RunInspectMode(args.Skip(1).ToArray());
        }
        if (args.Length > 0 && args[0] == "--probe-workshop")
        {
            return RunProbeWorkshopMode(args.Skip(1).ToArray());
        }

        string gameDir = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : (Environment.GetEnvironmentVariable("PROBABLY_STOLEN_DIR") ?? @"E:\Program Files (x86)\steam\steamapps\common\Probably Stolen");
        string modDir = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
            ? args[1]
            : Path.Combine(gameDir, "Mods", "probably_unified_hotkeys");
        string managed = Path.Combine(gameDir, "Probably Stolen_Data", "Managed");
        string dllPath = Path.Combine(modDir, "ProbablyUnifiedHotkeys.dll");

        if (!File.Exists(Path.Combine(managed, "Assembly-CSharp.dll")))
        {
            Console.Error.WriteLine($"ERR: Assembly-CSharp.dll not found under {managed}");
            return 2;
        }
        if (!File.Exists(dllPath))
        {
            Console.Error.WriteLine($"ERR: Mod DLL not found: {dllPath}");
            return 3;
        }

        RegisterManagedResolver(managed);

        var asm = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
        var scannerType = asm.GetType("ModScanner", throwOnError: true)!;
        var scanMethod = scannerType.GetMethod("Scan", BindingFlags.Public | BindingFlags.Static)!;

        string[] allowedRefs = System.IO.Directory.GetFiles(managed, "*.dll")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .ToArray();

        object? result = scanMethod.Invoke(null, new object[]
        {
            new[] { dllPath },
            allowedRefs,
            modDir,
            true
        });

        var declaredCaps = LoadDeclaredCapabilities(Path.Combine(modDir, "manifest.xml"));
        Console.WriteLine($"Declared Capabilities in manifest.xml: {(declaredCaps.Count == 0 ? "<none>" : string.Join(", ", declaredCaps))}");

        var detectedCaps = new List<string>();
        var findings = new List<string>();
        int hardCount = 0;
        int pendingCount = 0;
        int depCapCount = 0;
        bool usesDepCap = false;
        bool usesDepRef = false;
        bool refused = false;
        string errorText = string.Empty;

        foreach (var f in result!.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? val = f.GetValue(result);
            PrintMember(f.Name, val, detectedCaps, findings, ref hardCount, ref pendingCount, ref depCapCount, ref usesDepCap, ref usesDepRef, ref refused, ref errorText);
        }
        foreach (var p in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? val = p.GetValue(result);
            PrintMember(p.Name, val, detectedCaps, findings, ref hardCount, ref pendingCount, ref depCapCount, ref usesDepCap, ref usesDepRef, ref refused, ref errorText);
        }

        var undeclaredCaps = detectedCaps
            .Where(c => !declaredCaps.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var unexpectedFindings = findings
            .Where(item => !declaredCaps.Any(c => item.StartsWith(c + ":", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (refused ||
            undeclaredCaps.Count > 0 ||
            unexpectedFindings.Count > 0 ||
            hardCount > 0 ||
            pendingCount > 0 ||
            depCapCount > 0 ||
            usesDepCap ||
            usesDepRef ||
            !string.IsNullOrEmpty(errorText))
        {
            Console.Error.WriteLine("FAIL: ModScanner reported undeclared capabilities, unexpected findings, deprecated usages, hard flags, pending flags, or refusal.");
            if (undeclaredCaps.Count > 0) Console.Error.WriteLine("  Undeclared Capabilities: " + string.Join(", ", undeclaredCaps));
            if (unexpectedFindings.Count > 0) Console.Error.WriteLine("  Unexpected Findings: " + string.Join(" | ", unexpectedFindings));
            return 4;
        }

        Console.WriteLine($"PASS: ModScanner verified cleanly (Declared=[{string.Join(", ", declaredCaps)}], Capabilities={detectedCaps.Count}, Findings={findings.Count}, HardFlags=0, Deprecated=0, Refused=False).");

        if (!VerifyProfileSnapshotEngine(dllPath))
        {
            return 5;
        }
        return 0;
    }

    private static bool VerifyProfileSnapshotEngine(string dllPath)
    {
        var modAsm = Assembly.LoadFrom(dllPath);
        var storeType = modAsm.GetType("ProbablyUnifiedHotkeys.ComboKeyStore", throwOnError: true)!;
        var inMemField = storeType.GetField("UseInMemoryPrefsForTests", BindingFlags.NonPublic | BindingFlags.Static)!;
        inMemField.SetValue(null, true);

        var mgrType = modAsm.GetType("ProbablyUnifiedHotkeys.HotkeyProfileManager", throwOnError: true)!;
        var tryParse = mgrType.GetMethod("TryParseSnapshotJson", BindingFlags.Public | BindingFlags.Static)!;

        string sample = "分享我的键位配置：{\"schema\":\"puh_profile_v1\",\"version\":\"1.4.6\",\"name\":\"方案一\",\"timestamp\":\"11-15 19:38\",\"hotkeys\":{\"vanilla.interact\":\"E\",\"smart_inventory_arrange.OpenSettingsHotkey\":\"Ctrl+F6\",\"ShowItemNames.Hotkey\":\"None\"},\"customHotkeys\":{\"chiqingsan.BetterPerformance.hover_peek_key\":\"Mouse3\"},\"general\":{\"ShowItemNames.Enabled\":\"b:1\",\"ShowItemNames.FontScale\":\"f:1.25\",\"item_details.expand_key\":\"i:2\",\"hollow.clientdemandhighlight.userIgnoredIds\":\"s:metal_scanner,uv_filter\"},\"forced\":[\"foo.bar\"],\"ignored\":[\"baz.qux\"]} 快试试！";
        object?[] args = new object?[] { sample, null, null };
        bool ok = (bool)(tryParse.Invoke(null, args) ?? false);
        object? snap = args[1];
        string? err = args[2] as string;
        if (!ok || snap == null)
        {
            Console.Error.WriteLine($"FAIL: TryParseSnapshotJson returned false: {err}");
            return false;
        }

        var snapType = snap.GetType();
        var hkDict = (IDictionary)snapType.GetProperty("Hotkeys")!.GetValue(snap)!;
        var customDict = (IDictionary)snapType.GetProperty("CustomHotkeys")!.GetValue(snap)!;
        var genDict = (IDictionary)snapType.GetProperty("General")!.GetValue(snap)!;
        var forcedList = (IList)snapType.GetProperty("Forced")!.GetValue(snap)!;
        var ignoredList = (IList)snapType.GetProperty("Ignored")!.GetValue(snap)!;

        if ((string?)hkDict["smart_inventory_arrange.OpenSettingsHotkey"] != "Ctrl+F6" ||
            (string?)hkDict["ShowItemNames.Hotkey"] != "None" ||
            (string?)customDict["chiqingsan.BetterPerformance.hover_peek_key"] != "Mouse3" ||
            (string?)genDict["ShowItemNames.FontScale"] != "f:1.25" ||
            (string?)genDict["hollow.clientdemandhighlight.userIgnoredIds"] != "s:metal_scanner,uv_filter" ||
            forcedList.Count != 1 ||
            ignoredList.Count != 1)
        {
            Console.Error.WriteLine("FAIL: Snapshot parsed content mismatch.");
            return false;
        }

        Console.WriteLine($"PASS: ProfileSnapshotEngine verified cleanly (Hotkeys={hkDict.Count}, CustomHotkeys={customDict.Count}, General={genDict.Count}, Forced={forcedList.Count}, Ignored={ignoredList.Count}).");
        return true;
    }

    private static HashSet<string> LoadDeclaredCapabilities(string manifestPath)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(manifestPath)) return set;
        try
        {
            var doc = XDocument.Load(manifestPath);
            var root = doc.Root;
            var caps = root?.Element("Capabilities")?.Elements("Capability");
            if (caps != null)
            {
                foreach (var c in caps)
                {
                    string v = c.Value.Trim();
                    if (!string.IsNullOrEmpty(v)) set.Add(v);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("WARN: Failed to parse manifest.xml capabilities: " + ex.Message);
        }
        return set;
    }

    private static void PrintMember(
        string name,
        object? val,
        List<string> detectedCaps,
        List<string> findings,
        ref int hardCount,
        ref int pendingCount,
        ref int depCapCount,
        ref bool usesDepCap,
        ref bool usesDepRef,
        ref bool refused,
        ref string errorText)
    {
        if (val is IEnumerable en && !(val is string))
        {
            var items = new List<object?>();
            foreach (var item in en) items.Add(item);
            Console.WriteLine($"{name}: Count={items.Count}");
            foreach (var item in items) Console.WriteLine("  - " + item);
            if (name == "Capabilities")
            {
                foreach (var item in items)
                {
                    string? s = item?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) detectedCaps.Add(s);
                }
            }
            else if (name == "Findings")
            {
                foreach (var item in items)
                {
                    string? s = item?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(s)) findings.Add(s);
                }
            }
            else if (name == "HardFlags") hardCount = items.Count;
            else if (name == "Pending") pendingCount = items.Count;
            else if (name == "DeprecatedCapabilities") depCapCount = items.Count;
        }
        else
        {
            Console.WriteLine($"{name}: {val}");
            if (name == "Refused" && val is bool b) refused = b;
            else if (name == "UsesDeprecatedCapability" && val is bool dc) usesDepCap = dc;
            else if (name == "UsesDeprecatedReflection" && val is bool dr) usesDepRef = dr;
            else if (name == "Error" && val is string s) errorText = s;
        }
    }

    private static void RegisterManagedResolver(string managed)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, resolveArgs) =>
        {
            string shortName = resolveArgs.Name!.Split(',')[0];
            if (!Resolving.Add(shortName)) return null;
            try
            {
                string p = Path.Combine(managed, shortName + ".dll");
                return File.Exists(p) ? Assembly.LoadFrom(p) : null;
            }
            finally
            {
                Resolving.Remove(shortName);
            }
        };
    }

    /// <summary>
    /// Safe metadata inspection using DseCecil (never loads mscorlib/netstandard into PowerShell or CLR).
    /// Usage: dotnet run --project tools/ScannerCheck -- --inspect TypeName1 TypeName2 ...
    /// </summary>
    private static int RunInspectMode(string[] typeNames)
    {
        string gameDir = Environment.GetEnvironmentVariable("PROBABLY_STOLEN_DIR") ?? @"E:\Program Files (x86)\steam\steamapps\common\Probably Stolen";
        string managed = Path.Combine(gameDir, "Probably Stolen_Data", "Managed");
        RegisterManagedResolver(managed);

        var cecilAsm = Assembly.LoadFrom(Path.Combine(managed, "DseCecil.dll"));
        var asmDefType = cecilAsm.GetType("DseCecil.AssemblyDefinition", throwOnError: true)!;
        var readAsm = asmDefType.GetMethod("ReadAssembly", new[] { typeof(string) })!;

        object asmDef = readAsm.Invoke(null, new object[] { Path.Combine(managed, "Assembly-CSharp.dll") })!;
        object mainModule = asmDef.GetType().GetProperty("MainModule")!.GetValue(asmDef)!;
        var getTypes = mainModule.GetType().GetMethod("GetTypes", Type.EmptyTypes)!;
        var allTypes = (IEnumerable)getTypes.Invoke(mainModule, null)!;

        var typeMap = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in allTypes)
        {
            string fullName = (string)t!.GetType().GetProperty("FullName")!.GetValue(t)!;
            typeMap[fullName] = t;
            typeMap[fullName.Replace('/', '+')] = t;
        }

        foreach (string rawQuery in typeNames)
        {
            string query = rawQuery;
            string? methodFilter = null;
            int sepIdx = rawQuery.IndexOf("::", StringComparison.Ordinal);
            if (sepIdx > 0)
            {
                query = rawQuery.Substring(0, sepIdx);
                methodFilter = rawQuery.Substring(sepIdx + 2);
            }

            if (!typeMap.TryGetValue(query, out var t))
            {
                Console.WriteLine($"=== {query} (NOT FOUND) ===");
                continue;
            }
            var tt = t.GetType();
            Console.WriteLine($"=== {rawQuery} ===");
            bool isEnum = (bool)tt.GetProperty("IsEnum")!.GetValue(t)!;
            var fields = (IEnumerable)tt.GetProperty("Fields")!.GetValue(t)!;
            if (isEnum)
            {
                foreach (var f in fields)
                {
                    string fn = (string)f!.GetType().GetProperty("Name")!.GetValue(f)!;
                    if (fn != "value__") Console.WriteLine($"  Enum: {fn}");
                }
                continue;
            }

            if (methodFilter == null)
            {
                foreach (var f in fields)
                {
                    var ft = f!.GetType().GetProperty("FieldType")!.GetValue(f)!;
                    string ftn = (string)ft.GetType().GetProperty("Name")!.GetValue(ft)!;
                    string fn = (string)f.GetType().GetProperty("Name")!.GetValue(f)!;
                    Console.WriteLine($"  Field: {ftn} {fn}");
                }

                var props = (IEnumerable)tt.GetProperty("Properties")!.GetValue(t)!;
                foreach (var p in props)
                {
                    var pt = p!.GetType().GetProperty("PropertyType")!.GetValue(p)!;
                    string ptn = (string)pt.GetType().GetProperty("Name")!.GetValue(pt)!;
                    string pn = (string)p.GetType().GetProperty("Name")!.GetValue(p)!;
                    Console.WriteLine($"  Prop: {ptn} {pn}");
                }
            }

            var methods = (IEnumerable)tt.GetProperty("Methods")!.GetValue(t)!;
            foreach (var m in methods)
            {
                var mt = m!.GetType();
                string mn = (string)mt.GetProperty("Name")!.GetValue(m)!;
                if (methodFilter != null && !string.Equals(mn, methodFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                bool isSpecial = (bool)mt.GetProperty("IsSpecialName")!.GetValue(m)!;
                if (isSpecial && methodFilter == null) continue;
                var rt = mt.GetProperty("ReturnType")!.GetValue(m)!;
                string rtn = (string)rt.GetType().GetProperty("Name")!.GetValue(rt)!;
                var mParams = (IEnumerable)mt.GetProperty("Parameters")!.GetValue(m)!;
                var pList = new List<string>();
                foreach (var mp in mParams)
                {
                    var mpt = mp!.GetType().GetProperty("ParameterType")!.GetValue(mp)!;
                    string mptn = (string)mpt.GetType().GetProperty("Name")!.GetValue(mpt)!;
                    string mpn = (string)mp.GetType().GetProperty("Name")!.GetValue(mp)!;
                    pList.Add($"{mptn} {mpn}");
                }
                Console.WriteLine($"  Method: {rtn} {mn}({string.Join(", ", pList)})");
                if (methodFilter != null && (bool)mt.GetProperty("HasBody")!.GetValue(m)!)
                {
                    object body = mt.GetProperty("Body")!.GetValue(m)!;
                    var insts = (IEnumerable)body.GetType().GetProperty("Instructions")!.GetValue(body)!;
                    foreach (var inst in insts)
                    {
                        Console.WriteLine("    IL: " + inst);
                    }
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Safe workshop DLL probe runner in isolated .NET 8 process (never triggers PowerShell stack overflow).
    /// Usage: dotnet run --project tools/ScannerCheck -- --probe-workshop [workshopContentDir]
    /// </summary>
    private static int RunProbeWorkshopMode(string[] extraArgs)
    {
        string gameDir = Environment.GetEnvironmentVariable("PROBABLY_STOLEN_DIR") ?? @"E:\Program Files (x86)\steam\steamapps\common\Probably Stolen";
        string managed = Path.Combine(gameDir, "Probably Stolen_Data", "Managed");
        string modDll = Path.Combine(gameDir, "Mods", "probably_unified_hotkeys", "ProbablyUnifiedHotkeys.dll");
        string wsRoot = extraArgs.Length > 0 && !string.IsNullOrWhiteSpace(extraArgs[0])
            ? extraArgs[0]
            : @"E:\Program Files (x86)\steam\steamapps\workshop\content\4348910";

        RegisterManagedResolver(managed);
        var modAsm = Assembly.LoadFrom(modDll);
        var probeType = modAsm.GetType("ProbablyUnifiedHotkeys.DllKeyProbe", throwOnError: true)!;
        var probeCecil = probeType.GetMethod("ProbeDllWithCecil", BindingFlags.NonPublic | BindingFlags.Static)!;

        if (!System.IO.Directory.Exists(wsRoot))
        {
            Console.WriteLine($"Workshop directory not found: {wsRoot}");
            return 0;
        }

        int totalMods = 0;
        int modsWithHits = 0;
        int totalHits = 0;

        foreach (string modDir in System.IO.Directory.GetDirectories(wsRoot))
        {
            string manifest = Path.Combine(modDir, "manifest.xml");
            if (!File.Exists(manifest)) continue;
            totalMods++;

            try
            {
                var doc = XDocument.Load(manifest);
                string modId = doc.Root?.Element("ID")?.Value ?? Path.GetFileName(modDir);
                string modName = doc.Root?.Element("Name")?.Value ?? modId;
                var dlls = System.IO.Directory.GetFiles(modDir, "*.dll").ToList();
                var list = (IList)probeCecil.Invoke(null, new object[] { modId, dlls })!;
                if (list.Count > 0)
                {
                    modsWithHits++;
                    totalHits += list.Count;
                    Console.WriteLine($"[{modId}] ({modName}) -> {list.Count} probed keys");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"WARN [{Path.GetFileName(modDir)}]: {ex.Message}");
            }
        }

        Console.WriteLine($"SUMMARY: Scanned {totalMods} workshop mods, {modsWithHits} mods had {totalHits} total probed keys.");
        return 0;
    }
}
