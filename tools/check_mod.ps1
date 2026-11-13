param(
    [string]$GameDir = $(if ($env:PROBABLY_STOLEN_DIR) { $env:PROBABLY_STOLEN_DIR } else { "E:\Program Files (x86)\steam\steamapps\common\Probably Stolen" }),
    [switch]$SkipBuild,
    [switch]$ZeroPermission
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$MainCsproj = Join-Path $RepoRoot "ProbablyUnifiedHotkeys.csproj"
$ScannerCsproj = Join-Path $RepoRoot "tools\ScannerCheck\ScannerCheck.csproj"
$VdfPath = Join-Path $RepoRoot "tools\steamcmd\probably_unified_hotkeys.vdf"
$DeployDir = Join-Path $GameDir "Mods\probably_unified_hotkeys"
$AllowedFiles = @(
    "ProbablyUnifiedHotkeys.dll",
    "manifest.xml",
    "preview.png",
    "README.md"
) | Sort-Object

Write-Host "[1/4] Build main mod (Release) [ZeroPermission=$($ZeroPermission.IsPresent)]..."
$targetDll = Join-Path $DeployDir "ProbablyUnifiedHotkeys.dll"
$oldDll = Join-Path $GameDir "ProbablyUnifiedHotkeys.dll.old"

if (-not $SkipBuild) {
    # If the deployed DLL is currently locked by a running game process, rotate it to game root (outside Mods) so build can copy cleanly
    if (Test-Path $targetDll) {
        try {
            $stream = [System.IO.File]::Open($targetDll, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $stream.Dispose()
        } catch {
            Write-Host "Deployed DLL is locked by running game process; rotating to $oldDll..."
            if (Test-Path $oldDll) { Remove-Item $oldDll -Force -ErrorAction SilentlyContinue }
            Move-Item -LiteralPath $targetDll -Destination $oldDll -Force -ErrorAction SilentlyContinue
        }
    }

    if ($ZeroPermission) {
        $zeroManifest = Join-Path $RepoRoot "tools\manifest.zero.xml"
        Copy-Item $zeroManifest -Destination (Join-Path $RepoRoot "manifest.xml") -Force
        & dotnet build $MainCsproj -c Release -p:GameDir="$GameDir" -p:DisableDllKeyProbe=true
    } else {
        $filesManifest = Join-Path $RepoRoot "tools\manifest.files.xml"
        if (Test-Path $filesManifest) {
            Copy-Item $filesManifest -Destination (Join-Path $RepoRoot "manifest.xml") -Force
        }
        & dotnet build $MainCsproj -c Release -p:GameDir="$GameDir" -p:DisableDllKeyProbe=false
    }
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for ProbablyUnifiedHotkeys.csproj (exit $LASTEXITCODE)"
    }
} else {
    Write-Host "Skipped build (-SkipBuild)."
}

Write-Host "[2/4] Verify deploy directory cleanliness: $DeployDir"
if (-not (Test-Path -LiteralPath $DeployDir)) {
    throw "Deploy directory does not exist: $DeployDir"
}

# Try cleanup of any stale .old files if game has closed
if (Test-Path $oldDll) {
    Remove-Item $oldDll -Force -ErrorAction SilentlyContinue
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

Write-Host "[4/4] Run official ModScanner static check..."
& dotnet run --project $ScannerCsproj -c Release -p:GameDir="$GameDir" -- "$GameDir" "$DeployDir"
if ($LASTEXITCODE -ne 0) {
    throw "ModScanner check failed (exit $LASTEXITCODE)"
}

Write-Host "ALL CHECKS PASSED."
