using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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

        int capCount = 0;
        int findCount = 0;
        int hardCount = 0;
        bool refused = false;
        string errorText = string.Empty;

        foreach (var f in result!.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? val = f.GetValue(result);
            PrintMember(f.Name, val, ref capCount, ref findCount, ref hardCount, ref refused, ref errorText);
        }
        foreach (var p in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? val = p.GetValue(result);
            PrintMember(p.Name, val, ref capCount, ref findCount, ref hardCount, ref refused, ref errorText);
        }

        if (refused || capCount > 0 || findCount > 0 || hardCount > 0 || !string.IsNullOrEmpty(errorText))
        {
            Console.Error.WriteLine("FAIL: ModScanner reported findings, capabilities, hard flags, or refusal.");
            return 4;
        }

        Console.WriteLine("PASS: ModScanner 0 Capabilities / 0 Findings / 0 HardFlags / Refused=False.");
        return 0;
    }

    private static void PrintMember(
        string name,
        object? val,
        ref int capCount,
        ref int findCount,
        ref int hardCount,
        ref bool refused,
        ref string errorText)
    {
        if (val is IEnumerable en && !(val is string))
        {
            var items = new List<object?>();
            foreach (var item in en) items.Add(item);
            Console.WriteLine($"{name}: Count={items.Count}");
            foreach (var item in items) Console.WriteLine("  - " + item);
            if (name == "Capabilities") capCount = items.Count;
            else if (name == "Findings") findCount = items.Count;
            else if (name == "HardFlags") hardCount = items.Count;
        }
        else
        {
            Console.WriteLine($"{name}: {val}");
            if (name == "Refused" && val is bool b) refused = b;
            else if (name == "Error" && val is string s) errorText = s;
        }
    }
}
