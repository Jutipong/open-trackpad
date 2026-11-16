#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Allow the interactive user to use the v2.3 USB control pipe without elevation.
.DESCRIPTION
    Sets AllowNonAdminAccess=1 under the UMDF service parameters and restarts the
    Magic Trackpad PTP device so the driver recreates the pipe with the relaxed DACL.
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$LogDir = Join-Path $RepoRoot "logs"
$LogPath = Join-Path $LogDir "enable-user-access.log"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

try {
    $ParamKey = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters"
    if (-not (Test-Path $ParamKey)) {
        New-Item -Path $ParamKey -Force | Out-Null
    }

    Write-Host "Setting AllowNonAdminAccess=1 ..." -ForegroundColor Cyan
    New-ItemProperty -Path $ParamKey -Name "AllowNonAdminAccess" -PropertyType DWord -Value 1 -Force | Out-Null
    Get-ItemProperty $ParamKey | Format-List

    $Device = Get-PnpDevice -PresentOnly |
        Where-Object { $_.InstanceId -eq "USB\VID_05AC&PID_0265&MI_01\7&33EFA42B&0&0001" } |
        Select-Object -First 1

    if (-not $Device) {
        $Device = Get-PnpDevice -PresentOnly |
            Where-Object { $_.InstanceId -match "USB\\VID_05AC&PID_0265&MI_01" } |
            Select-Object -First 1
    }

    if ($Device) {
        Write-Host "Restarting device $($Device.InstanceId) ..." -ForegroundColor Cyan
        $out = & pnputil.exe /restart-device "$($Device.InstanceId)" 2>&1
        $out | ForEach-Object { Write-Host $_ }
        Start-Sleep -Seconds 3
    }
    else {
        Write-Warning "MT2 PTP device not found; replug the trackpad."
    }

    Write-Host ""
    Write-Host "Done. Log: $LogPath" -ForegroundColor Green
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Stop-Transcript | Out-Null
}
