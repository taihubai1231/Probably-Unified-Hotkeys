using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Steamworks;

internal static class Program
{
    private const uint AppIdVal = 4348910;
    private const ulong ItemIdVal = 3837828444;

    private static readonly List<string> StandardTags = new()
    {
        "Mod",
        "Quality of Life",
        "QoL",
        "UI",
        "Utilities",
        "Simplified Chinese",
        "Compatibility",
        "Utility",
        "Utilities for Players",
        "Mono",
        "English"
    };

    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "--query";

        if (!SteamAPI.Init())
        {
            Console.Error.WriteLine("ERR: SteamAPI.Init() failed. Make sure Steam client is running and logged in.");
            return 2;
        }

        try
        {
            Console.WriteLine(
                $"SteamID={SteamUser.GetSteamID()} BLoggedOn={SteamUser.BLoggedOn()} " +
                $"Subscribed={SteamApps.BIsSubscribedApp(new AppId_t(AppIdVal))} AppId={AppIdVal} ItemId={ItemIdVal}");

            if (mode == "--apply")
            {
                QueryDetails("BEFORE", "schinese");
                return ApplyTags();
            }

            if (mode == "--update-schinese")
            {
                string? vdfPath = args.Length > 1 ? args[1] : null;
                QueryDetails("BEFORE", "schinese");
                return UpdateSimplifiedChinese(vdfPath);
            }

            if (mode == "--query" || mode.StartsWith("--", StringComparison.Ordinal))
            {
                string lang = args.Length > 1 ? args[1] : "schinese";
                QueryDetails("QUERY", lang);
                return 0;
            }

            QueryDetails("QUERY", mode);
            return 0;
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    private static int UpdateSimplifiedChinese(string? explicitVdfPath)
    {
        string vdfPath = ResolveVdfPath(explicitVdfPath);
        Console.WriteLine($"Using VDF: {vdfPath}");

        string vdf = File.ReadAllText(vdfPath, Encoding.UTF8);
        Match titleMatch = Regex.Match(vdf, "\"title\"\\s+\"([^\"]+)\"");
        Match descMatch = Regex.Match(vdf, "\"description\"\\s+\"([\\s\\S]*?)\"\\r?\\n\\t\"changenote\"");
        Match changeMatch = Regex.Match(vdf, "\"changenote\"\\s+\"([^\"]*)\"");
        Match contentMatch = Regex.Match(vdf, "\"contentfolder\"\\s+\"([^\"]+)\"");
        Match previewMatch = Regex.Match(vdf, "\"previewfile\"\\s+\"([^\"]+)\"");

        string title = titleMatch.Groups[1].Value;
        string desc = descMatch.Groups[1].Value.Replace("\r\n", "\n");
        string changeNote = changeMatch.Success && !string.IsNullOrWhiteSpace(changeMatch.Groups[1].Value)
            ? changeMatch.Groups[1].Value
            : "Update Simplified Chinese title and description";

        Console.WriteLine($"Parsed Title len={title.Length}, Desc chars={desc.Length}, Desc utf8Bytes={Encoding.UTF8.GetByteCount(desc)}");
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(desc))
        {
            Console.Error.WriteLine("ERR: Failed to parse title or description from VDF.");
            return 6;
        }

        if (Encoding.UTF8.GetByteCount(desc) >= 8000)
        {
            Console.Error.WriteLine("ERR: Description UTF-8 byte count >= 8000 (Steam Workshop limit).");
            return 10;
        }

        UGCUpdateHandle_t handle = SteamUGC.StartItemUpdate(new AppId_t(AppIdVal), new PublishedFileId_t(ItemIdVal));
        bool okLang = SteamUGC.SetItemUpdateLanguage(handle, "schinese");
        bool okTitle = SteamUGC.SetItemTitle(handle, title);
        bool okDesc = SteamUGC.SetItemDescription(handle, desc);
        Console.WriteLine($"SetItemUpdateLanguage(schinese)={okLang} SetItemTitle={okTitle} SetItemDescription={okDesc}");
        if (!okLang || !okTitle || !okDesc)
        {
            Console.Error.WriteLine("ERR: One or more SetItem* calls returned false.");
            return 7;
        }

        SteamAPICall_t uCall = SteamUGC.SubmitItemUpdate(handle, changeNote);
        if (!WaitCallResult<SubmitItemUpdateResult_t>(uCall, out var uRes, out bool uIoFail))
        {
            Console.Error.WriteLine($"ERR: SubmitItemUpdate failed/timeout ioFail={uIoFail}");
            return 8;
        }

        Console.WriteLine($"SubmitItemUpdate(schinese) result={uRes.m_eResult} needsLegal={uRes.m_bUserNeedsToAcceptWorkshopLegalAgreement}");
        if (uRes.m_eResult == EResult.k_EResultFileNotFound && contentMatch.Success && previewMatch.Success)
        {
            string contentFolder = contentMatch.Groups[1].Value.Replace("\\\\", "\\");
            string previewFile = previewMatch.Groups[1].Value.Replace("\\\\", "\\");
            Console.WriteLine("Retrying with SetItemContent + SetItemPreview...");
            UGCUpdateHandle_t handle2 = SteamUGC.StartItemUpdate(new AppId_t(AppIdVal), new PublishedFileId_t(ItemIdVal));
            SteamUGC.SetItemUpdateLanguage(handle2, "schinese");
            SteamUGC.SetItemTitle(handle2, title);
            SteamUGC.SetItemDescription(handle2, desc);
            SteamUGC.SetItemContent(handle2, contentFolder);
            SteamUGC.SetItemPreview(handle2, previewFile);
            SteamAPICall_t uCall2 = SteamUGC.SubmitItemUpdate(handle2, changeNote);
            if (!WaitCallResult<SubmitItemUpdateResult_t>(uCall2, out var uRes2, out bool uIoFail2))
            {
                Console.Error.WriteLine($"ERR: SubmitItemUpdate retry failed/timeout ioFail={uIoFail2}");
                return 8;
            }
            Console.WriteLine($"SubmitItemUpdate2(schinese) result={uRes2.m_eResult} needsLegal={uRes2.m_bUserNeedsToAcceptWorkshopLegalAgreement}");
            if (uRes2.m_eResult != EResult.k_EResultOK)
            {
                return 9;
            }
        }
        else if (uRes.m_eResult != EResult.k_EResultOK)
        {
            return 9;
        }

        Thread.Sleep(1500);
        QueryDetails("AFTER", "schinese");
        return 0;
    }

    private static int ApplyTags()
    {
        UGCUpdateHandle_t updateHandle = SteamUGC.StartItemUpdate(new AppId_t(AppIdVal), new PublishedFileId_t(ItemIdVal));
        bool okSet = SteamUGC.SetItemTags(updateHandle, StandardTags, false);
        Console.WriteLine($"SetItemTags ok={okSet} count={StandardTags.Count}");
        if (!okSet)
        {
            Console.Error.WriteLine("ERR: SetItemTags returned false.");
            return 3;
        }

        SteamAPICall_t call = SteamUGC.SubmitItemUpdate(updateHandle, "Update workshop tags");
        if (!WaitCallResult<SubmitItemUpdateResult_t>(call, out var res, out bool ioFail))
        {
            Console.Error.WriteLine($"ERR: SubmitItemUpdate failed/timeout ioFail={ioFail}");
            return 4;
        }

        Console.WriteLine($"SubmitItemUpdate result={res.m_eResult} needsLegal={res.m_bUserNeedsToAcceptWorkshopLegalAgreement} itemId={res.m_nPublishedFileId}");
        if (res.m_eResult != EResult.k_EResultOK)
        {
            return 5;
        }

        Thread.Sleep(1500);
        QueryDetails("AFTER", "schinese");
        return 0;
    }

    private static string ResolveVdfPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "tools", "steamcmd", "probably_unified_hotkeys.vdf"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "steamcmd", "probably_unified_hotkeys.vdf"),
            @"E:\Program Files\steamcmd\probably_unified_hotkeys.vdf"
        };

        foreach (string candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full))
            {
                return full;
            }
        }

        throw new FileNotFoundException("Could not locate probably_unified_hotkeys.vdf");
    }

    private static void QueryDetails(string label, string language)
    {
        var ids = new[] { new PublishedFileId_t(ItemIdVal) };
        UGCQueryHandle_t q = SteamUGC.CreateQueryUGCDetailsRequest(ids, 1);
        if (!string.IsNullOrWhiteSpace(language))
        {
            SteamUGC.SetLanguage(q, language);
        }
        SteamUGC.SetReturnLongDescription(q, true);
        SteamAPICall_t call = SteamUGC.SendQueryUGCRequest(q);
        try
        {
            if (!WaitCallResult<SteamUGCQueryCompleted_t>(call, out var qres, out bool ioFail))
            {
                Console.WriteLine($"[{label}] Query timeout/fail ioFail={ioFail}");
                return;
            }
            Console.WriteLine($"[{label}] Lang={language} Query eResult={qres.m_eResult} count={qres.m_unNumResultsReturned}");
            if (qres.m_eResult == EResult.k_EResultOK && qres.m_unNumResultsReturned > 0 && SteamUGC.GetQueryUGCResult(q, 0, out var details))
            {
                Console.WriteLine($"[{label}] Title={details.m_rgchTitle}");
                Console.WriteLine($"[{label}] Visibility={details.m_eVisibility} Owner={details.m_ulSteamIDOwner} Truncated={details.m_bTagsTruncated}");
                Console.WriteLine($"[{label}] Tags='{details.m_rgchTags}'");
                string desc = details.m_rgchDescription ?? "";
                Console.WriteLine($"[{label}] DescLen={desc.Length} Head='{desc.Substring(0, Math.Min(120, desc.Length)).Replace("\n", " ")}'");
            }
        }
        finally
        {
            SteamUGC.ReleaseQueryUGCRequest(q);
        }
    }

    private static bool WaitCallResult<T>(SteamAPICall_t call, out T result, out bool ioFailure)
    {
        result = default!;
        ioFailure = false;
        if (call == SteamAPICall_t.Invalid)
        {
            ioFailure = true;
            return false;
        }

        bool done = false;
        T captured = default!;
        bool capturedIoFail = false;

        using var cr = CallResult<T>.Create((r, fail) =>
        {
            captured = r;
            capturedIoFail = fail;
            done = true;
        });
        cr.Set(call);

        for (int i = 0; i < 400 && !done; i++)
        {
            SteamAPI.RunCallbacks();
            Thread.Sleep(50);
        }

        result = captured;
        ioFailure = capturedIoFail;
        return done && !ioFailure;
    }
}
