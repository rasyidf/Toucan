# publish.ps1 — Build and package Toucan for distribution
# Usage:
#   .\publish.ps1 portable    # Option 1: Single-file EXE
#   .\publish.ps1 installer   # Option 2: Inno Setup installer
#   .\publish.ps1 msix        # Option 3: MSIX package
#   .\publish.ps1 all         # All three

param(
    [Parameter(Position = 0)]
    [ValidateSet("portable", "installer", "msix", "all")]
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"
$Version = "0.17.3"
$Project = "Toucan\Toucan.csproj"
$Config = "Release"
$Runtime = "win-x64"

Write-Host "=== Toucan Publish Script v$Version ===" -ForegroundColor Cyan
Write-Host ""

# --- Helper: Publish to folder ---
function Publish-App {
    param([string]$OutputDir, [switch]$SingleFile)

    $args = @(
        "publish", $Project,
        "-c", $Config,
        "-r", $Runtime,
        "--self-contained",
        "-o", $OutputDir,
        "-p:DebugType=none",
        "-p:DebugSymbols=false"
    )

    if ($SingleFile) {
        $args += "-p:PublishSingleFile=true"
        $args += "-p:IncludeNativeLibrariesForSelfExtract=true"
        $args += "-p:EnableCompressionInSingleFile=true"
    }

    Write-Host "  dotnet $($args -join ' ')" -ForegroundColor DarkGray
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
}

# =============================================================
# Option 1: Portable single-file EXE
# =============================================================
function Build-Portable {
    Write-Host "[1/3] Building portable single-file..." -ForegroundColor Green
    $outDir = "publish\portable"
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    Publish-App -OutputDir $outDir -SingleFile

    # Create ZIP
    $zipPath = "publish\Toucan-$Version-portable-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

    $size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "  -> $zipPath ($size MB)" -ForegroundColor Yellow
    Write-Host ""
}

# =============================================================
# Option 2: Inno Setup installer
# =============================================================
function Build-Installer {
    Write-Host "[2/3] Building Inno Setup installer..." -ForegroundColor Green

    # First publish to win-x64 folder (framework-dependent for smaller size)
    $outDir = "publish\win-x64"
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    Publish-App -OutputDir $outDir

    # Check for Inno Setup compiler
    $iscc = Get-Command "iscc" -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $isccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        if (Test-Path $isccPath) { $iscc = Get-Item $isccPath }
        else {
            Write-Host "  [SKIP] Inno Setup not found. Install from https://jrsoftware.org/isinfo.php" -ForegroundColor Red
            Write-Host "  Published files at: $outDir" -ForegroundColor DarkGray
            return
        }
    }

    # Build installer
    $issFile = "installer.iss"
    Write-Host "  Running ISCC..." -ForegroundColor DarkGray
    & $iscc.FullName $issFile
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

    $installerPath = "publish\installer\ToucanSetup-$Version.exe"
    $size = [math]::Round((Get-Item $installerPath).Length / 1MB, 1)
    Write-Host "  -> $installerPath ($size MB)" -ForegroundColor Yellow
    Write-Host ""
}

# =============================================================
# Option 3: MSIX package
# =============================================================
function Build-Msix {
    Write-Host "[3/3] Building MSIX package..." -ForegroundColor Green

    # Publish app content
    $outDir = "publish\msix-content"
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    Publish-App -OutputDir $outDir

    # Copy MSIX assets
    $assetsDir = "$outDir\Assets"
    if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir | Out-Null }

    # Generate placeholder PNGs if assets don't exist
    $manifest = "packaging\msix\AppxManifest.xml"
    Copy-Item $manifest "$outDir\AppxManifest.xml" -Force

    # Copy packaging assets if they exist
    if (Test-Path "packaging\msix\Assets") {
        Copy-Item "packaging\msix\Assets\*" $assetsDir -Force -Recurse
    }
    else {
        Write-Host "  [WARN] No MSIX assets in packaging\msix\Assets\. Using placeholders." -ForegroundColor DarkYellow
        # Create minimal placeholder assets from the app icon
        if (Test-Path "Toucan\Assets\Images\logo.png") {
            Copy-Item "Toucan\Assets\Images\logo.png" "$assetsDir\Square44x44Logo.png" -Force
            Copy-Item "Toucan\Assets\Images\logo.png" "$assetsDir\Square150x150Logo.png" -Force
            Copy-Item "Toucan\Assets\Images\logo.png" "$assetsDir\Wide310x150Logo.png" -Force
            Copy-Item "Toucan\Assets\Images\logo.png" "$assetsDir\SmallTile.png" -Force
            Copy-Item "Toucan\Assets\Images\logo.png" "$assetsDir\LargeTile.png" -Force
            Copy-Item "Toucan\Assets\Images\logo.png" "$assetsDir\StoreLogo.png" -Force
            Copy-Item "Toucan\Assets\Images\logo.png" "$assetsDir\document.png" -Force
        }
    }

    # Find makeappx.exe
    $makeappx = Get-Command "makeappx" -ErrorAction SilentlyContinue
    if (-not $makeappx) {
        $sdkPaths = @(
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe",
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\makeappx.exe",
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
        )
        foreach ($p in $sdkPaths) {
            if (Test-Path $p) { $makeappx = Get-Item $p; break }
        }
    }

    if (-not $makeappx) {
        Write-Host "  [SKIP] makeappx.exe not found. Install Windows SDK." -ForegroundColor Red
        Write-Host "  Published files at: $outDir (ready for manual packaging)" -ForegroundColor DarkGray
        return
    }

    # Pack MSIX
    $msixPath = "publish\Toucan-$Version-x64.msix"
    if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

    & $makeappx.FullName pack /d $outDir /p $msixPath /nv
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed" }

    $size = [math]::Round((Get-Item $msixPath).Length / 1MB, 1)
    Write-Host "  -> $msixPath ($size MB)" -ForegroundColor Yellow
    Write-Host ""

    Write-Host "  NOTE: MSIX is unsigned. To install, either:" -ForegroundColor DarkGray
    Write-Host "    1. Sign with: signtool sign /fd SHA256 /a $msixPath" -ForegroundColor DarkGray
    Write-Host "    2. Enable Developer Mode in Windows Settings" -ForegroundColor DarkGray
    Write-Host ""
}

# =============================================================
# Run
# =============================================================
$startTime = Get-Date

switch ($Target) {
    "portable"  { Build-Portable }
    "installer" { Build-Installer }
    "msix"      { Build-Msix }
    "all"       { Build-Portable; Build-Installer; Build-Msix }
}

$elapsed = (Get-Date) - $startTime
Write-Host "=== Done in $([math]::Round($elapsed.TotalSeconds, 1))s ===" -ForegroundColor Cyan
