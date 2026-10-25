# Toucan MSIX Packaging Script
# Requirements: Windows 10 SDK (for makeappx.exe and signtool.exe)
# Usage: .\Build-Msix.ps1 [-SkipBuild] [-Sign] [-CertPath <path>] [-CertPassword <pwd>]

param(
    [switch]$SkipBuild,
    [switch]$LayoutOnly,
    [switch]$Sign,
    [string]$CertPath,
    [string]$CertPassword,
    [string]$Configuration = "Release",
    [string]$Version
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    $AppVersion = ([xml](Get-Content "$Root\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $Version = "$AppVersion.0"
}
$PublishDir = "$Root\build\publish\msix"
$PublishDir = [IO.Path]::GetFullPath($PublishDir)
$StageRoot = [IO.Path]::GetFullPath("$Root\build\publish") + [IO.Path]::DirectorySeparatorChar
if (-not $PublishDir.StartsWith($StageRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "MSIX publish output must stay within build/publish"
}
$PackagingDir = "$Root\packaging"
$OutputDir = "$Root\dist"
$MsixPath = "$OutputDir\Toucan-$Version-x64.msix"

Write-Host "=== Toucan MSIX Build ===" -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host ""

# Step 1: Publish self-contained
if (-not $SkipBuild) {
    if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
    Write-Host "[1/4] Publishing self-contained build..." -ForegroundColor Yellow
    dotnet publish "$Root\Toucan.Avalonia\Toucan.Avalonia.csproj" -c $Configuration -r win-x64 --self-contained `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -p:DebugType=none `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
    # Native packages ship ~100 MB of .pdb files that DebugType=none does not remove.
    Get-ChildItem $PublishDir -Recurse -Include *.pdb, *.xml | Remove-Item -Force
    Write-Host "  Published to: $PublishDir" -ForegroundColor Green
} else {
    Write-Host "[1/4] Skipping build (--SkipBuild)" -ForegroundColor DarkGray
}

# Step 2: Prepare MSIX layout
# Source and notice sidecars must survive publishing and --SkipBuild alike.
& python "$PSScriptRoot\..\tools\Verify-SpellingArtifacts.py" --folder $PublishDir --bundled
if ($LASTEXITCODE -ne 0) { throw "Spelling source/notices verification failed" }

Write-Host "[2/4] Preparing MSIX layout..." -ForegroundColor Yellow
$LayoutDir = [IO.Path]::GetFullPath("$Root\build\publish\msix-layout")
if (-not $LayoutDir.StartsWith($StageRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "MSIX layout must stay within build/publish"
}
if (Test-Path -LiteralPath $LayoutDir) { Remove-Item -LiteralPath $LayoutDir -Recurse -Force }
New-Item -ItemType Directory -Path $LayoutDir -Force | Out-Null
New-Item -ItemType Directory -Path "$LayoutDir\Assets" -Force | Out-Null

# Copy published app
Copy-Item -Path "$PublishDir\*" -Destination $LayoutDir -Recurse -Force

# Copy manifest (update version)
$manifest = Get-Content "$PackagingDir\msix\AppxManifest.xml" -Raw
$manifest = $manifest -replace '(<Identity\s[^>]*?Version=")[^"]*', "`${1}$Version"
Set-Content -Path "$LayoutDir\AppxManifest.xml" -Value $manifest

# Generate correctly sized assets from the app logo; optional authored assets override them.
& "$PackagingDir\New-MsixAssets.ps1" -OutputDirectory "$LayoutDir\Assets"
$assetSizes = @("StoreLogo.png", "Square44x44Logo.png", "Square150x150Logo.png", "Wide310x150Logo.png", "SmallTile.png", "LargeTile.png", "document.png")
foreach ($asset in $assetSizes) {
    $src = "$PackagingDir\msix\Assets\$asset"
    if (Test-Path $src) {
        Copy-Item $src "$LayoutDir\Assets\$asset"
    }
}

Write-Host "  Layout ready at: $LayoutDir" -ForegroundColor Green
if ($LayoutOnly) { return }

# Step 3: Create MSIX
Write-Host "[3/4] Creating MSIX package..." -ForegroundColor Yellow
$makeappx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'x64' } |
            Sort-Object { [version]($_.Directory.Parent.Name) } -Descending | Select-Object -First 1

if (-not $makeappx) {
    Write-Host "  ERROR: makeappx.exe not found. Install Windows 10 SDK." -ForegroundColor Red
    Write-Host "  Download: https://developer.microsoft.com/windows/downloads/windows-sdk/" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  Alternative: The publish output is ready at:" -ForegroundColor Yellow
    Write-Host "    $PublishDir" -ForegroundColor White
    Write-Host "  You can distribute it as a self-contained folder or zip." -ForegroundColor DarkGray
    exit 1
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
if (Test-Path $MsixPath) { Remove-Item $MsixPath -Force }

& $makeappx.FullName pack /d $LayoutDir /p $MsixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
Write-Host "  MSIX created: $MsixPath" -ForegroundColor Green

# Step 4: Sign (optional)
if ($Sign) {
    Write-Host "[4/4] Signing MSIX..." -ForegroundColor Yellow
    if (-not $CertPath) { throw "Provide -CertPath for signing" }

    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
                Where-Object { $_.Directory.Name -eq 'x64' } |
                Sort-Object { [version]($_.Directory.Parent.Name) } -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found" }

    $signArgs = @("sign", "/fd", "SHA256", "/f", $CertPath)
    if ($CertPassword) { $signArgs += @("/p", $CertPassword) }
    $signArgs += @("/t", "http://timestamp.digicert.com", $MsixPath)

    & $signtool.FullName @signArgs
    if ($LASTEXITCODE -ne 0) { throw "Signing failed" }
    Write-Host "  Signed successfully" -ForegroundColor Green
} else {
    Write-Host "[4/4] Skipping signing (use -Sign -CertPath <pfx>)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host "Output: $MsixPath"
