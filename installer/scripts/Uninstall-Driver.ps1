#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Remove the MagicTrackpad2ForWindows v2.3 driver package.
.DESCRIPTION
    Dry-run by default. Use -Execute to export a backup and delete the exact
    AmtPtpDevice package from the Driver Store. The trackpad must be disconnected
    unless -Force is supplied. No /force is passed to pnputil.
#>

[CmdletBinding()]
param(
    [switch]$Execute,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "DriverStore.ps1")

$LogDir = Join-Path $env:ProgramData "OpenTrackpad\logs"
$LogPath = Join-Path $LogDir "uninstall-driver.log"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

try {
    Write-Step "Connected Apple devices"
    $devices = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match "VID_05AC&PID_0265" }
    $devices | Select-Object Status, Class, FriendlyName, InstanceId | Format-Table -AutoSize

    if ($devices -and -not $Force) {
        throw "Disconnect the Magic Trackpad before removing the driver, or use -Force."
    }

    Write-Step "Driver packages"
    $packages = @(Get-AmtPtpPackages)
    if ($packages.Count -eq 0) {
        Write-Host "No AmtPtpDevice package found in the Driver Store." -ForegroundColor Green
        return
    }

    $packages | Select-Object Driver, OriginalFileName, ProviderName, Version, Date |
        Format-Table -AutoSize

    if ($packages.Count -gt 1 -and -not $Force) {
        throw "Multiple AmtPtpDevice packages found. Review the list or use -Force."
    }

    if (-not $Execute) {
        Write-Host ""
        Write-Host "DRY RUN. Nothing was removed." -ForegroundColor Yellow
        Write-Host "Re-run with -Execute to export a backup and delete the package(s)."
        return
    }

    $backupDir = Join-Path $env:ProgramData (
        "OpenTrackpad\driver-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

    foreach ($package in $packages) {
        Write-Step "Export backup for $($package.Driver)"
        & pnputil.exe /export-driver $package.Driver $backupDir 2>&1 |
            ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Export failed for $($package.Driver); continuing to delete."
        }

        Write-Step "Delete $($package.Driver)"
        & pnputil.exe /delete-driver $package.Driver /uninstall 2>&1 |
            ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "pnputil /delete-driver failed with exit code $LASTEXITCODE"
        }
    }

    & pnputil.exe /scan-devices 2>&1 | ForEach-Object { Write-Host $_ }

    Write-Host ""
    Write-Host "Driver removed. Backup: $backupDir" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Stop-Transcript | Out-Null
}
