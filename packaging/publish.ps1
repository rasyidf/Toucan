# publish.ps1 — Build and package Toucan for distribution
# Usage:
#   .\packaging\publish.ps1 portable    # Option 1: Single-file EXE
#   .\packaging\publish.ps1 installer   # Option 2: Inno Setup installer
#   .\packaging\publish.ps1 msix        # Option 3: MSIX package
#   .\packaging\publish.ps1 all         # All three

param(
    [Parameter(Position = 0)]
    [ValidateSet("portable", "installer", "msix", "all")]
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"
# The script lives in packaging/; every path below is relative to the repository root.
$Root = Split-Path $PSScriptRoot -Parent
$Version = ([xml](Get-Content "$Root\Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$Project = "Toucan.Avalonia\Toucan.Avalonia.csproj"
$Config = "Release"
$Runtime = "win-x64"

Write-Host "=== Toucan Publish Script v$Version ===" -ForegroundColor Cyan
Write-Host ""

# --- Helper: Publish to folder ---
function Clear-PublishDirectory {
    param([string]$Path)
    $resolved = [IO.Path]::GetFullPath($Path)
    $stageRoot = [IO.Path]::GetFullPath("$Root\build\publish") + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($stageRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a directory outside build/publish: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

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

    & python "$Root\tools\Verify-SpellingArtifacts.py" --folder $OutputDir $(if ($SingleFile) { "--bundled" })
    if ($LASTEXITCODE -ne 0) { throw "Spelling source/notices verification failed" }

    # Avalonia's native packages ship ~100 MB of .pdb files that DebugType=none does not remove.
    Get-ChildItem $OutputDir -Recurse -Include *.pdb, *.xml | Remove-Item -Force
}

# =============================================================
# Option 1: Portable single-file EXE
# =============================================================
function Build-Portable {
    Write-Host "[1/3] Building portable single-file..." -ForegroundColor Green
    $outDir = "$Root\build\publish\portable"
    Clear-PublishDirectory $outDir

    Publish-App -OutputDir $outDir -SingleFile

    # Create ZIP
    $zipPath = "$Root\dist\Toucan-$Version-portable-x64.zip"
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

    # Publish the self-contained application and required sidecars.
    $outDir = "$Root\build\publish\win-x64"
    Clear-PublishDirectory $outDir

    Publish-App -OutputDir $outDir

    # Check for Inno Setup compiler
    $isccPath = (Get-Command "iscc" -ErrorAction SilentlyContinue).Source
    if (-not $isccPath) {
        foreach ($major in @(7, 6)) {
            $candidate = "${env:ProgramFiles(x86)}\Inno Setup $major\ISCC.exe"
            if (Test-Path -LiteralPath $candidate) { $isccPath = $candidate; break }
        }
    }
    if (-not $isccPath) { throw "Inno Setup 6/7 not found. Published files are at $outDir" }

    # Build installer
    $issFile = "$PSScriptRoot\installer.iss"
    Write-Host "  Running ISCC..." -ForegroundColor DarkGray
    & $isccPath "/DAppVersion=$Version" $issFile
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

    $installerPath = "$Root\dist\ToucanSetup-$Version.exe"
    $size = [math]::Round((Get-Item $installerPath).Length / 1MB, 1)
    Write-Host "  -> $installerPath ($size MB)" -ForegroundColor Yellow
    Write-Host ""
}

# =============================================================
# Option 3: MSIX package
# =============================================================
function Build-Msix {
    & "$PSScriptRoot\Build-Msix.ps1" -Configuration $Config
}

# =============================================================
# Run
# =============================================================
$startTime = Get-Date

Push-Location $Root
try {
    New-Item -ItemType Directory -Path "$Root\dist" -Force | Out-Null
    switch ($Target) {
        "portable"  { Build-Portable }
        "installer" { Build-Installer }
        "msix"      { Build-Msix }
        "all"       { Build-Portable; Build-Installer; Build-Msix }
    }
} finally {
    Pop-Location
}

$elapsed = (Get-Date) - $startTime
Write-Host "=== Done in $([math]::Round($elapsed.TotalSeconds, 1))s ===" -ForegroundColor Cyan
