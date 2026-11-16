#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Restart the Apple Magic Trackpad 2 USB composite device and its PTP interface.
.DESCRIPTION
    Resets the whole USB composite device (not just MI_01) so the UMDF HID
    driver, PTP stack, and control pipe are recreated. Requires elevation.
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$LogDir = Join-Path $RepoRoot "logs"
$LogPath = Join-Path $LogDir "restart-trackpad.log"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

try {
    $parent = Get-PnpDevice -PresentOnly |
        Where-Object { $_.InstanceId -match '^USB\\VID_05AC&PID_0265\\' } |
        Select-Object -First 1

    if (-not $parent) {
        throw "Magic Trackpad 2 composite device not found."
    }

    Write-Host "Restarting $($parent.InstanceId) ..." -ForegroundColor Cyan
    & pnputil.exe /restart-device "$($parent.InstanceId)" 2>&1 | ForEach-Object { Write-Host $_ }
    Start-Sleep -Seconds 3

    $ptp = Get-PnpDevice -PresentOnly |
        Where-Object { $_.InstanceId -match '^USB\\VID_05AC&PID_0265&MI_01\\' } |
        Select-Object -First 1

    if ($ptp) {
        Write-Host "Restarting $($ptp.InstanceId) ..." -ForegroundColor Cyan
        & pnputil.exe /restart-device "$($ptp.InstanceId)" 2>&1 | ForEach-Object { Write-Host $_ }
        Start-Sleep -Seconds 3
    }

    Write-Host ""
    Get-PnpDevice -PresentOnly |
        Where-Object { $_.InstanceId -match 'VID_05AC&PID_0265' } |
        Select-Object Status, FriendlyName, InstanceId |
        Format-Table -AutoSize

    Write-Host "Done. Log: $LogPath" -ForegroundColor Green
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Stop-Transcript | Out-Null
}
