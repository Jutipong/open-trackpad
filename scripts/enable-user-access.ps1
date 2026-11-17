#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Allow the interactive user to use the v2.3 USB control pipe and Bluetooth haptics without elevation.
.DESCRIPTION
    Sets AllowNonAdminAccess=1 under the USB and Bluetooth service parameters and
    restarts the matching PTP devices so the driver recreates their DACLs.
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

    $BtParamKey = "HKLM:\SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters"
    if (-not (Test-Path $BtParamKey)) {
        New-Item -Path $BtParamKey -Force | Out-Null
    }

    Write-Host "Setting Bluetooth AllowNonAdminAccess=1 ..." -ForegroundColor Cyan
    New-ItemProperty -Path $BtParamKey -Name "AllowNonAdminAccess" -PropertyType DWord -Value 1 -Force | Out-Null
    Get-ItemProperty $BtParamKey | Format-List

    $usbSettings = Get-ItemProperty -Path $ParamKey -ErrorAction SilentlyContinue
    if ($usbSettings) {
        $settingNames = @(
            "FeedbackClick", "FeedbackRelease", "StopPressure", "StopSize",
            "IgnoreNearFingers", "IgnoreButtonFinger", "PalmRejection",
            "InvertMovement", "ButtonDisabled"
        )
        foreach ($name in $settingNames) {
            $value = $usbSettings.$name
            if ($null -eq $value) { continue }
            $existing = Get-ItemProperty -Path $BtParamKey -Name $name -ErrorAction SilentlyContinue
            if ($null -eq $existing) {
                New-ItemProperty -Path $BtParamKey -Name $name -PropertyType DWord -Value $value | Out-Null
                Write-Host "Copied $name=$value to Bluetooth settings"
            }
        }
    }

    $Device = Get-PnpDevice -PresentOnly |
        Where-Object { $_.InstanceId -eq "USB\VID_05AC&PID_0265&MI_01\7&33EFA42B&0&0001" } |
        Select-Object -First 1

    if (-not $Device) {
        $Device = Get-PnpDevice -PresentOnly |
            Where-Object { $_.InstanceId -match "USB\\VID_05AC&PID_0265&MI_01" } |
            Select-Object -First 1
    }

    if ($Device) {
        Write-Host "Restarting USB device $($Device.InstanceId) ..." -ForegroundColor Cyan
        $out = & pnputil.exe /restart-device "$($Device.InstanceId)" 2>&1
        $out | ForEach-Object { Write-Host $_ }
        Start-Sleep -Seconds 3
    }
    else {
        Write-Warning "MT2 USB PTP device not found; skipping USB restart."
    }

    $BtDevice = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object {
            $_.InstanceId -match '^HID\\\{00001124-.*&COL01\\' -and
            $_.FriendlyName -match "Apple Multi-touch Trackpad HID Filter"
        } |
        Select-Object -First 1

    if ($BtDevice) {
        Write-Host "Restarting Bluetooth device $($BtDevice.InstanceId) ..." -ForegroundColor Cyan
        $out = & pnputil.exe /restart-device "$($BtDevice.InstanceId)" 2>&1
        $out | ForEach-Object { Write-Host $_ }
        Start-Sleep -Seconds 3
    }
    else {
        Write-Warning "MT2 Bluetooth filter not found; pair the trackpad first."
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
