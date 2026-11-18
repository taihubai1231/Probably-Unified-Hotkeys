param(
    [ValidateSet("query", "default", "schinese", "tags")]
    [string]$Step = "query",
    [string]$GameDir,
    [string]$SteamCmdExe = "E:\Program Files\steamcmd\steamcmd.exe",
    [string]$SteamCmdVdfDir = "E:\Program Files\steamcmd",
    [string]$SteamUser = "piggg1231",
    [switch]$SkipCheck,
    [switch]$NoDeploy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# 1. Resolve GameDir according to priority:
# ① Parameter -GameDir
# ② Directory.Build.props.user
# ③ Environment variable PROBABLY_STOLEN_DIR
# ④ Default Steam path
if (-not $GameDir -or $GameDir.Trim() -eq "") {
    $userProps = Join-Path $RepoRoot "Directory.Build.props.user"
    if (Test-Path -LiteralPath $userProps) {
        try {
            [xml]$xml = Get-Content -LiteralPath $userProps -Raw
            if ($xml.Project.PropertyGroup.GameDir) {
                $GameDir = $xml.Project.PropertyGroup.GameDir.Trim()
            }
        } catch {
            # Ignore XML parse errors
        }
    }
}
if (-not $GameDir -or $GameDir.Trim() -eq "") {
    if ($env:PROBABLY_STOLEN_DIR) {
        $GameDir = $env:PROBABLY_STOLEN_DIR
    } else {
        $GameDir = "E:\Program Files (x86)\steam\steamapps\common\Probably Stolen"
    }
}

$CheckScript = Join-Path $PSScriptRoot "check_mod.ps1"
$RepoVdf = Join-Path $PSScriptRoot "steamcmd\probably_unified_hotkeys.vdf"
$SteamTaggerCsproj = Join-Path $PSScriptRoot "SteamTagger\SteamTagger.csproj"

switch ($Step) {
    "query" {
        & dotnet run --project $SteamTaggerCsproj -c Release -p:GameDir="$GameDir" -p:DeployToGame=false -- --game-dir "$GameDir" --query schinese
        if ($LASTEXITCODE -ne 0) { throw "SteamTagger --query failed (exit $LASTEXITCODE)" }
    }
    "default" {
        if (-not $SkipCheck) {
            $checkArgs = @("-GameDir", "$GameDir")
            if ($NoDeploy) { $checkArgs += "-NoDeploy" }
            & $CheckScript @checkArgs
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
        Write-Host "=================================================================" -ForegroundColor Green
        Write-Host "[OK] steamcmd upload complete! Content package & Change Notes updated." -ForegroundColor Green
        Write-Host "[CRITICAL STEP NEXT] Do NOT skip: SteamCMD has logged out your desktop Steam client." -ForegroundColor Yellow
        Write-Host "1. Re-login / reconnect your desktop Steam client." -ForegroundColor Yellow
        Write-Host "2. Run: .\tools\publish_workshop.ps1 -Step schinese -SkipCheck" -ForegroundColor Cyan
        Write-Host "   to synchronize the Simplified Chinese workshop homepage description!" -ForegroundColor Cyan
        Write-Host "=================================================================" -ForegroundColor Green
    }
    "schinese" {
        if (-not $SkipCheck) {
            $checkArgs = @("-GameDir", "$GameDir")
            if ($NoDeploy) { $checkArgs += "-NoDeploy" }
            & $CheckScript @checkArgs
        }
        & dotnet run --project $SteamTaggerCsproj -c Release -p:GameDir="$GameDir" -p:DeployToGame=false -- --game-dir "$GameDir" --update-schinese "$RepoVdf"
        if ($LASTEXITCODE -ne 0) { throw "SteamTagger --update-schinese failed (exit $LASTEXITCODE)" }
        Write-Host "=================================================================" -ForegroundColor Green
        Write-Host "[OK] Simplified Chinese homepage description synchronized successfully!" -ForegroundColor Green
        Write-Host "=================================================================" -ForegroundColor Green
    }
    "tags" {
        & dotnet run --project $SteamTaggerCsproj -c Release -p:GameDir="$GameDir" -p:DeployToGame=false -- --game-dir "$GameDir" --apply
        if ($LASTEXITCODE -ne 0) { throw "SteamTagger --apply failed (exit $LASTEXITCODE)" }
    }
}
