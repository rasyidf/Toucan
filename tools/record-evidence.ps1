# Records a verification result for the current commit in docs/evidence/ and commits it. See docs/evidence/README.md.
#   pwsh tools/record-evidence.ps1 tests [-FromResults DIR] [-Notes TEXT] [-NoCommit] [-AllowDirty]
#   pwsh tools/record-evidence.ps1 check <name> -Result passed|failed [-Packaged] [-Notes TEXT] [-NoCommit] [-AllowDirty]
# -FromResults DIR records TRX files from an earlier `dotnet test --logger trx` run instead of running the tests (CI).
# Needs git, the .NET SDK and PowerShell 7. Runs on Windows, macOS and Linux; the shell script is the usual choice outside Windows.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][ValidateSet('tests', 'check')][string]$Mode,
    [Parameter(Position = 1)][string]$Check = 'tests',
    [ValidateSet('passed', 'failed')][string]$Result,
    [string]$Notes = '',
    [switch]$Packaged,
    [switch]$NoCommit,
    [switch]$AllowDirty,
    [string]$FromResults
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root

if ($Mode -eq 'tests') { $Check = 'tests' }
elseif (-not $Result) { throw '-Result passed|failed is required for a hand check.' }
if ($Check -notmatch '^[a-z0-9][a-z0-9-]*$') { throw "Check names are lowercase words joined by '-'." }

$changes = git status --porcelain -- . ':(exclude)docs/evidence'
$dirty = [bool]$changes
if ($dirty -and -not $AllowDirty) { throw 'The working tree has uncommitted changes; commit them first or pass -AllowDirty.' }
$commit = (git rev-parse HEAD).Trim()
$version = ([xml](Get-Content -Raw Directory.Build.props)).SelectSingleNode('//Version').InnerText

if ($IsWindows) {
    $family = 'Windows'
    $osVersion = (Get-CimInstance Win32_OperatingSystem | ForEach-Object { "$($_.Caption) $($_.Version)" })
} elseif ($IsMacOS) {
    $family = 'macOS'; $osVersion = (sw_vers -productVersion).Trim()
} else {
    $family = 'Linux'
    $osVersion = (Get-Content /etc/os-release -ErrorAction SilentlyContinue | Where-Object { $_ -like 'PRETTY_NAME=*' } | ForEach-Object { $_.Split('=', 2)[1].Trim('"') })
    if (-not $osVersion) { $osVersion = (uname -r).Trim() }
}
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$environment = 'native'
if ($env:GITHUB_ACTIONS -eq 'true') { $environment = 'ci' }
elseif ($family -eq 'Linux' -and (Test-Path /proc/version) -and ((Get-Content /proc/version -Raw) -match 'microsoft')) { $environment = 'wsl' }
$ciRun = if ($environment -eq 'ci') { "$($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)" } else { $null }

$counts = $null
$command = $null
if ($Mode -eq 'tests') {
    $command = 'dotnet test Toucan.slnx --configuration Release --logger trx'
    $results = if ($FromResults) { $FromResults } else { Join-Path ([System.IO.Path]::GetTempPath()) ("toucan-evidence-" + [guid]::NewGuid().ToString('N')) }
    try {
        if (-not $FromResults) { dotnet test Toucan.slnx --configuration Release --logger trx --results-directory $results }
        $total = 0; $passed = 0; $failed = 0; $skipped = 0
        $files = @(Get-ChildItem -Path $results -Filter *.trx -Recurse)
        foreach ($file in $files) {
            $xml = [xml](Get-Content -Raw $file.FullName)
            $c = $xml.TestRun.ResultSummary.Counters
            $total += [int]$c.total; $passed += [int]$c.passed
            $failed += [int]$c.failed + [int]$c.error + [int]$c.timeout + [int]$c.aborted
            # Skipped tests are counted as not executed.
            $skipped += [Math]::Max([int]$c.total - [int]$c.executed, 0)
        }
        $counts = [ordered]@{ total = $total; passed = $passed; failed = $failed; skipped = $skipped }
        $Result = if ($files.Count -gt 0 -and $failed -eq 0) { 'passed' } else { 'failed' }
        Write-Host "Tests: $total total, $passed passed, $failed failed, $skipped skipped in $($files.Count) assemblies."
    } finally {
        if (-not $FromResults) { Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $results }
    }
}

$who = (git config user.name)
if (-not $who) { $who = $env:USERNAME }
if (-not $who) { $who = $env:USER }
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$folder = Join-Path 'docs/evidence' $version
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$relative = "docs/evidence/$version/$($family.ToLowerInvariant())-$Check-$stamp.json"

$record = [ordered]@{
    schema = 1; check = $Check; kind = $(if ($Mode -eq 'tests') { 'automated' } else { 'manual' }); result = $Result
    commit = $commit; dirty = $dirty; version = $version
    os = [ordered]@{ family = $family; version = $osVersion; arch = $arch }
    environment = $environment; packaged = [bool]$Packaged; command = $command; counts = $counts; notes = $Notes
    recordedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"); recordedBy = $who; ciRun = $ciRun
}
# LF line endings, as the shell script writes, so records diff the same on every OS.
$json = ($record | ConvertTo-Json -Depth 5) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $root $relative), $json + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Recorded ${Check}: $Result -> $relative"

if (-not $NoCommit) {
    git add -- $relative
    git commit -q -m "chore(evidence): record $Check on $family for $($commit.Substring(0, 7))" -- $relative
    Write-Host "Committed $((git rev-parse --short HEAD).Trim())."
}
if ($Result -ne 'passed') { exit 1 }
