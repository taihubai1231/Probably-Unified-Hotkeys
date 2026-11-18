param(
    [string]$GameDir,
    [switch]$SkipBuild,
    [switch]$NoDeploy,
    [switch]$ZeroPermission = $true,
    [switch]$FilesPermission
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

$MainCsproj = Join-Path $RepoRoot "src\ProbablyUnifiedHotkeys\ProbablyUnifiedHotkeys.csproj"
$MainManifest = Join-Path $RepoRoot "src\ProbablyUnifiedHotkeys\manifest.xml"
$ScannerCsproj = Join-Path $RepoRoot "tools\ScannerCheck\ScannerCheck.csproj"
$VdfPath = Join-Path $RepoRoot "tools\steamcmd\probably_unified_hotkeys.vdf"
$DeployDir = Join-Path $GameDir "Mods\probably_unified_hotkeys"

$AllowedFiles = @(
    "ProbablyUnifiedHotkeys.dll",
    "manifest.xml",
    "preview.png",
    "README.md"
) | Sort-Object

Write-Host "=========================================================="
Write-Host "  Probably Unified Hotkeys - Pre-flight Check Gateway"
Write-Host "  Target GameDir: $GameDir"
Write-Host "  NoDeploy Flag:  $($NoDeploy.IsPresent)"
Write-Host "=========================================================="

Write-Host "[1/4] Build main mod (Release) [ZeroPermission=$($ZeroPermission.IsPresent)]..."
$targetDll = Join-Path $DeployDir "ProbablyUnifiedHotkeys.dll"
$oldDll = Join-Path $GameDir "ProbablyUnifiedHotkeys.dll.old"

if (-not $SkipBuild) {
    # If not running with -NoDeploy, handle process lock rotation in deploy dir
    if (-not $NoDeploy -and (Test-Path -LiteralPath $targetDll)) {
        try {
            $stream = [System.IO.File]::Open($targetDll, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $stream.Dispose()
        } catch {
            Write-Host "Deployed DLL is locked by running game process; rotating to $oldDll..."
            if (Test-Path -LiteralPath $oldDll) { Remove-Item -LiteralPath $oldDll -Force -ErrorAction SilentlyContinue }
            Move-Item -LiteralPath $targetDll -Destination $oldDll -Force -ErrorAction SilentlyContinue
        }
    }

    $deployArg = if ($NoDeploy) { "-p:DeployToGame=false" } else { "-p:DeployToGame=true" }

    if ($FilesPermission) {
        $filesManifest = Join-Path $RepoRoot "tools\manifest.files.xml"
        if (Test-Path -LiteralPath $filesManifest) {
            Copy-Item -LiteralPath $filesManifest -Destination $MainManifest -Force
        }
        & dotnet build $MainCsproj -c Release -p:GameDir="$GameDir" -p:DisableDllKeyProbe=false $deployArg
    } else {
        $zeroManifest = Join-Path $RepoRoot "tools\manifest.zero.xml"
        Copy-Item -LiteralPath $zeroManifest -Destination $MainManifest -Force
        & dotnet build $MainCsproj -c Release -p:GameDir="$GameDir" -p:DisableDllKeyProbe=true $deployArg
    }
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for ProbablyUnifiedHotkeys.csproj (exit $LASTEXITCODE)"
    }
} else {
    Write-Host "Skipped build (-SkipBuild)."
}

# Determine target directory to scan and verify
$ScanDir = $DeployDir
if ($NoDeploy) {
    # When -NoDeploy, scan the output directory from dotnet build
    $binDir = Join-Path $RepoRoot "src\ProbablyUnifiedHotkeys\bin\Release\netstandard2.1"
    if (-not (Test-Path -LiteralPath $binDir)) {
        $binDir = Join-Path $RepoRoot "src\ProbablyUnifiedHotkeys\bin\Release"
    }
    $ScanDir = $binDir
    # Ensure manifest.xml is in scan directory for ModScanner
    if (Test-Path -LiteralPath $MainManifest) {
        Copy-Item -LiteralPath $MainManifest -Destination (Join-Path $ScanDir "manifest.xml") -Force
    }
}

Write-Host "[2/4] Verify target directory cleanliness: $ScanDir"
if (-not (Test-Path -LiteralPath $ScanDir)) {
    throw "Target scan directory does not exist: $ScanDir"
}

if (-not $NoDeploy) {
    # Try cleanup of any stale .old files if game has closed
    if (Test-Path -LiteralPath $oldDll) {
        Remove-Item -LiteralPath $oldDll -Force -ErrorAction SilentlyContinue
    }

    $entries = @(Get-ChildItem -LiteralPath $DeployDir -Force)
    $unexpectedDirs = @($entries | Where-Object { $_.PSIsContainer })
    if ($unexpectedDirs.Count -gt 0) {
        $names = ($unexpectedDirs | ForEach-Object { $_.Name }) -join ", "
        throw "Unexpected subdirectories in deploy folder: $names"
    }

    $actualFiles = @($entries | Where-Object { -not $_.PSIsContainer } | ForEach-Object { $_.Name } | Sort-Object)
    $diff = Compare-Object -ReferenceObject $AllowedFiles -DifferenceObject $actualFiles
    if ($diff) {
        $actualStr = $actualFiles -join ", "
        $allowedStr = $AllowedFiles -join ", "
        throw "Deploy folder mismatch! Expected strictly [$allowedStr], found [$actualStr]."
    }
    Write-Host "Deploy folder verified cleanly: $($actualFiles -join ', ')"
} else {
    Write-Host "Skipped deploy folder strict cleanliness check (-NoDeploy is active)."
}

Write-Host "[3/4] Verify Steam Workshop VDF description byte limit (< 8000 UTF-8 bytes)..."
if (-not (Test-Path -LiteralPath $VdfPath)) {
    throw "VDF not found: $VdfPath"
}
$vdfText = [System.IO.File]::ReadAllText($VdfPath, [System.Text.Encoding]::UTF8)
$descMatch = [System.Text.RegularExpressions.Regex]::Match($vdfText, '"description"\s+"([\s\S]*?)"\r?\n\t"changenote"')
if (-not $descMatch.Success) {
    throw "Failed to parse description block from VDF: $VdfPath"
}
$descNormalized = $descMatch.Groups[1].Value.Replace("`r`n", "`n")
$descBytes = [System.Text.Encoding]::UTF8.GetByteCount($descNormalized)
Write-Host "VDF description length: $($descNormalized.Length) chars, $descBytes / 8000 UTF-8 bytes."
if ($descBytes -ge 8000) {
    throw "VDF description exceeds Steam 8000-byte limit ($descBytes bytes)."
}

Write-Host "[4/4] Run official ModScanner static check (.NET 8.0 isolated process)..."
& dotnet run --project $ScannerCsproj -c Release -p:GameDir="$GameDir" -p:DeployToGame=false -- --game-dir "$GameDir" --mod-dir "$ScanDir"
if ($LASTEXITCODE -ne 0) {
    throw "ModScanner check failed (exit $LASTEXITCODE)"
}

Write-Host "=========================================================="
Write-Host "  ALL CHECKS PASSED CLEANLY (Zero HardFlags / 0 Errors)"
Write-Host "=========================================================="
