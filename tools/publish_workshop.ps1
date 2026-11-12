param(
    [ValidateSet("query", "default", "schinese", "tags")]
    [string]$Step = "query",
    [string]$GameDir = $(if ($env:PROBABLY_STOLEN_DIR) { $env:PROBABLY_STOLEN_DIR } else { "E:\Program Files (x86)\steam\steamapps\common\Probably Stolen" }),
    [string]$SteamCmdExe = "E:\Program Files\steamcmd\steamcmd.exe",
    [string]$SteamCmdVdfDir = "E:\Program Files\steamcmd",
    [string]$SteamUser = "piggg1231",
    [switch]$SkipCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$CheckScript = Join-Path $PSScriptRoot "check_mod.ps1"
$RepoVdf = Join-Path $PSScriptRoot "steamcmd\probably_unified_hotkeys.vdf"
$SteamTaggerCsproj = Join-Path $PSScriptRoot "SteamTagger\SteamTagger.csproj"

switch ($Step) {
    "query" {
        & dotnet run --project $SteamTaggerCsproj -c Release -p:GameDir="$GameDir" -- --query schinese
        if ($LASTEXITCODE -ne 0) { throw "SteamTagger --query failed (exit $LASTEXITCODE)" }
    }
    "default" {
        if (-not $SkipCheck) {
            & $CheckScript -GameDir "$GameDir"
        }
        if (-not (Test-Path -LiteralPath $SteamCmdExe)) {
            throw "steamcmd.exe not found at $SteamCmdExe"
        }
        $targetVdf = Join-Path $SteamCmdVdfDir "probably_unified_hotkeys.vdf"
        Copy-Item -LiteralPath $RepoVdf -Destination $targetVdf -Force
        Write-Host "Synced VDF to: $targetVdf"
        Write-Host "Note: steamcmd logs out desktop Steam client during upload; re-login desktop Steam afterward before running '-Step schinese'."
        & $SteamCmdExe +login $SteamUser +workshop_build_item "$targetVdf" +quit
        if ($LASTEXITCODE -ne 0) { throw "steamcmd failed (exit $LASTEXITCODE)" }
    }
    "schinese" {
        if (-not $SkipCheck) {
            & $CheckScript -GameDir "$GameDir"
        }
        & dotnet run --project $SteamTaggerCsproj -c Release -p:GameDir="$GameDir" -- --update-schinese "$RepoVdf"
        if ($LASTEXITCODE -ne 0) { throw "SteamTagger --update-schinese failed (exit $LASTEXITCODE)" }
    }
    "tags" {
        & dotnet run --project $SteamTaggerCsproj -c Release -p:GameDir="$GameDir" -- --apply
        if ($LASTEXITCODE -ne 0) { throw "SteamTagger --apply failed (exit $LASTEXITCODE)" }
    }
}
