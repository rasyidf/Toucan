# Toucan MSIX Packaging Script
# Requirements: Windows 10 SDK (for makeappx.exe and signtool.exe)
# Usage: .\Build-Msix.ps1 [-SkipBuild] [-Sign] [-CertPath <path>] [-CertPassword <pwd>]

param(
    [switch]$SkipBuild,
    [switch]$Sign,
    [string]$CertPath,
    [string]$CertPassword,
    [string]$Configuration = "Release",
    [string]$Version = "0.14.0.0"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$PublishDir = "$Root\Toucan\bin\publish"
$PackagingDir = "$Root\packaging"
$OutputDir = "$Root\dist"
$MsixPath = "$OutputDir\Toucan-$Version-x64.msix"

Write-Host "=== Toucan MSIX Build ===" -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host ""

# Step 1: Publish self-contained
if (-not $SkipBuild) {
    Write-Host "[1/4] Publishing self-contained build..." -ForegroundColor Yellow
    Push-Location $Root
    dotnet publish Toucan/Toucan.csproj -c $Configuration -r win-x64 --self-contained `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=true `
        -p:DebugType=none `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
    Pop-Location
    Write-Host "  Published to: $PublishDir" -ForegroundColor Green
} else {
    Write-Host "[1/4] Skipping build (--SkipBuild)" -ForegroundColor DarkGray
}

# Step 2: Prepare MSIX layout
Write-Host "[2/4] Preparing MSIX layout..." -ForegroundColor Yellow
$LayoutDir = "$OutputDir\layout"
if (Test-Path $LayoutDir) { Remove-Item -Recurse -Force $LayoutDir }
New-Item -ItemType Directory -Path $LayoutDir -Force | Out-Null
New-Item -ItemType Directory -Path "$LayoutDir\Assets" -Force | Out-Null

# Copy published app
Copy-Item -Path "$PublishDir\*" -Destination $LayoutDir -Recurse -Force

# Copy manifest (update version)
$manifest = Get-Content "$PackagingDir\Package.appxmanifest" -Raw
$manifest = $manifest -replace 'Version="[^"]*"', "Version=`"$Version`""
Set-Content -Path "$LayoutDir\AppxManifest.xml" -Value $manifest

# Copy/create placeholder assets (if not provided, create minimal PNGs)
$assetSizes = @{ "StoreLogo.png" = 50; "Square44x44Logo.png" = 44; "Square150x150Logo.png" = 150; "Wide310x150Logo.png" = 310 }
foreach ($asset in $assetSizes.Keys) {
    $src = "$PackagingDir\Assets\$asset"
    if (Test-Path $src) {
        Copy-Item $src "$LayoutDir\Assets\$asset"
    } else {
        # Create a minimal 1x1 PNG placeholder (replace with real assets for store submission)
        Write-Host "  Warning: Missing $asset — using placeholder" -ForegroundColor DarkYellow
        [byte[]]$png = 0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
                       0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,
                       0x89,0x00,0x00,0x00,0x0A,0x49,0x44,0x41,0x54,0x78,0x9C,0x62,0x00,0x00,0x00,0x02,
                       0x00,0x01,0xE5,0x27,0xDE,0xFC,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,
                       0x60,0x82
        [IO.File]::WriteAllBytes("$LayoutDir\Assets\$asset", $png)
    }
}

Write-Host "  Layout ready at: $LayoutDir" -ForegroundColor Green

# Step 3: Create MSIX
Write-Host "[3/4] Creating MSIX package..." -ForegroundColor Yellow
$makeappx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
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
