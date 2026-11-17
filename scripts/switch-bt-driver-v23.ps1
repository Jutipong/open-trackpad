#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Switch the Bluetooth Magic Trackpad 2 HID filter back to the v2.3 driver.

.DESCRIPTION
    Restores the v2.3 Bluetooth filter after the imbushuo experiment.
    The v2.3 package is already in the Driver Store. USB is not touched.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$LogDir = Join-Path (Split-Path -Parent $PSScriptRoot) "logs"
$LogPath = Join-Path $LogDir "switch-bt-v23.log"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

$BluetoothHardwareId =
    "HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0265&Col01"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PnpDriverRollback
{
    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UpdateDriverForPlugAndPlayDevicesW(
        IntPtr hwndParent,
        string hardwareId,
        string fullInfPath,
        uint installFlags,
        out bool rebootRequired);
}
"@

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Get-BluetoothFilter {
    Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object {
            $_.InstanceId -match '^HID\\\{00001124-.*&COL01\\' -and
            $_.FriendlyName -match "Apple Multi-touch Trackpad HID Filter"
        } |
        Select-Object -First 1
}

function Get-DriverVersion([string]$InstanceId) {
    $value = Get-PnpDeviceProperty -InstanceId $InstanceId `
        -KeyName "DEVPKEY_Device_DriverVersion" -ErrorAction SilentlyContinue
    if ($null -eq $value) { return "unknown" }
    return $value.Data
}

function Find-V23Inf {
    $candidates = Get-ChildItem `
        "C:\Windows\System32\DriverStore\FileRepository\amtptpdevice.inf_amd64_*\AmtPtpDevice.inf" `
        -ErrorAction SilentlyContinue

    foreach ($candidate in $candidates) {
        $content = Get-Content $candidate.FullName -Raw
        if ($content -match "2026\.3983\.1\.1000") {
            return $candidate.FullName
        }
    }

    return $null
}

try {
    $device = Get-BluetoothFilter
    if (-not $device) {
        throw "Bluetooth Magic Trackpad filter not found. Turn the trackpad on first."
    }

    Write-Step "Current Bluetooth driver"
    Write-Host "Device:  $($device.InstanceId)"
    Write-Host "Version: $(Get-DriverVersion $device.InstanceId)"

    $inf = Find-V23Inf
    if (-not $inf) {
        throw "v2.3 AmtPtpDevice.inf not found in the Driver Store."
    }

    Write-Step "Switch back to v2.3"
    Write-Host "Hardware ID: $BluetoothHardwareId"
    Write-Host "INF:         $inf"

    $rebootRequired = $false
    $updated = [PnpDriverRollback]::UpdateDriverForPlugAndPlayDevicesW(
        [IntPtr]::Zero, $BluetoothHardwareId, $inf, 1, [ref]$rebootRequired)

    if (-not $updated) {
        $error = [System.ComponentModel.Win32Exception]::new(
            [System.Runtime.InteropServices.Marshal]::GetLastWin32Error())
        throw "UpdateDriverForPlugAndPlayDevices failed: $($error.Message)"
    }

    Write-Host "Driver update requested. Reboot required: $rebootRequired"

    $device = Get-BluetoothFilter
    if ($device) {
        Write-Host "Restarting $($device.InstanceId) ..."
        & pnputil.exe /restart-device "$($device.InstanceId)" 2>&1 |
            ForEach-Object { Write-Host $_ }
        Start-Sleep -Seconds 8
    }

    Write-Step "Result"
    $device = Get-BluetoothFilter
    if (-not $device) {
        Write-Warning "Bluetooth filter did not come back; turn the trackpad off/on or reboot."
        return
    }

    $version = Get-DriverVersion $device.InstanceId
    Write-Host "Device:  $($device.InstanceId)"
    Write-Host "Version: $version"

    if ($version -like "2026.3983*") {
        Write-Host "[PASS] v2.3 Bluetooth filter is active again." -ForegroundColor Green
        Write-Host "Battery and haptics over Bluetooth should work again."
    }
    else {
        Write-Warning "The Bluetooth filter did not switch back to 2026.3983."
        Write-Host "Open Device Manager > Bluetooth > Apple Multi-touch Trackpad HID Filter >"
        Write-Host "Update driver > Browse > $inf"
    }

    Write-Host ""
    Write-Host "Log: $LogPath" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Stop-Transcript | Out-Null
}
