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
        return 0;
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
}
