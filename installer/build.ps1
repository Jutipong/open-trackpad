#Requires -Version 5.1
<#
.SYNOPSIS
    Build the OpenTrackpad installer.
.DESCRIPTION
    Publishes the WPF app, optionally bundles the cached v2.3 driver package,
    and compiles installer\OpenTrackpad.iss with Inno Setup 6.
#>

[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $PSScriptRoot "publish"
$DriverDir = Join-Path $RepoRoot ".cache\v2.3-amd64\AMD64"
$IssPath = Join-Path $PSScriptRoot "OpenTrackpad.iss"
$OutputPath = Join-Path $PSScriptRoot "output\OpenTrackpad-Setup.exe"

if (-not $SkipPublish) {
    Write-Host "== Publishing app ==" -ForegroundColor Cyan
    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
    }

    & dotnet publish (Join-Path $RepoRoot "app\OpenTrackpad.App\OpenTrackpad.App.csproj") `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -o $PublishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path (Join-Path $PublishDir "OpenTrackpad.App.exe"))) {
    throw "Published app not found in $PublishDir"
}

if (Test-Path (Join-Path $DriverDir "AmtPtpDevice.inf")) {
    Write-Host "Driver package: $DriverDir" -ForegroundColor Green
}
else {
    Write-Warning "Driver package not found at $DriverDir. The installer will be app-only."
}

$iscc = $null
$command = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($command) {
    $iscc = $command.Source
}
else {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $iscc) {
    throw "Inno Setup 6 not found. Install it with: winget install -e --id JRSoftware.InnoSetup"
}

Write-Host "== Compiling installer ==" -ForegroundColor Cyan
& $iscc $IssPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $OutputPath)) {
    throw "Expected installer not found: $OutputPath"
}

Write-Host ""
Write-Host "Installer: $OutputPath" -ForegroundColor Green
