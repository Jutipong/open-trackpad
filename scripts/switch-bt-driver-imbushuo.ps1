#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Switch ONLY the Bluetooth Magic Trackpad 2 HID filter to the imbushuo
    2105-3979 (2021) signed driver, keeping the v2.3 USB driver untouched.

.DESCRIPTION
    This is a reversible experiment for Bluetooth cursor smoothness.
    The v2.3 package remains in the Driver Store; run
    scripts\switch-bt-driver-v23.ps1 to switch the Bluetooth device back.

    While the imbushuo filter is active, Bluetooth battery/haptics/settings
    from v2.3 are unavailable. USB still uses v2.3.
#>

[CmdletBinding()]
param(
    [string]$DriverPath = (Join-Path (Split-Path -Parent $PSScriptRoot) ".cache\imbushuo-2105-3979\drivers\amd64")
)

$ErrorActionPreference = "Stop"

$LogDir = Join-Path (Split-Path -Parent $PSScriptRoot) "logs"
$LogPath = Join-Path $LogDir "switch-bt-imbushuo.log"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

$BluetoothHardwareId =
    "HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0265&Col01"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class PnpDriverUpdate
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

function Restart-BluetoothFilter {
    $device = Get-BluetoothFilter
    if (-not $device) {
        Write-Warning "Bluetooth trackpad filter is not present; turn the trackpad off/on."
        return
    }

    Write-Host "Restarting $($device.InstanceId) ..."
    & pnputil.exe /restart-device "$($device.InstanceId)" 2>&1 |
        ForEach-Object { Write-Host $_ }
    Start-Sleep -Seconds 8
}

try {
    Write-Step "Verify driver package"
    $inf = Join-Path $DriverPath "AmtPtpDevice.inf"
    $cat = Join-Path $DriverPath "amtptpdevice.cat"
    $sys = Join-Path $DriverPath "AmtPtpHidFilter.sys"

    foreach ($file in @($inf, $cat, $sys)) {
        if (-not (Test-Path $file)) {
            throw "Driver file not found: $file"
        }
    }

    $signature = Get-AuthenticodeSignature $cat
    $signature |
        Select-Object Status, StatusMessage,
            @{n = "Subject"; e = { $_.SignerCertificate.Subject } },
            @{n = "NotAfter"; e = { $_.SignerCertificate.NotAfter } } |
        Format-List

    if ($signature.Status -ne "Valid") {
        throw "Catalog signature is not valid: $($signature.Status)"
    }
    if ($signature.SignerCertificate.Subject -notmatch "Microsoft") {
        throw "Unexpected catalog signer: $($signature.SignerCertificate.Subject)"
    }

    $device = Get-BluetoothFilter
    if (-not $device) {
        throw "Bluetooth Magic Trackpad filter not found. Turn the trackpad on and pair it first."
    }

    Write-Step "Current Bluetooth driver"
    Write-Host "Device:  $($device.InstanceId)"
    Write-Host "Version: $(Get-DriverVersion $device.InstanceId)"

    Write-Step "Stage driver package"
    & pnputil.exe /add-driver $inf 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 259) {
        throw "pnputil /add-driver failed with exit code $LASTEXITCODE"
    }

    Write-Step "Switch only the Bluetooth HID filter"
    Write-Host "Hardware ID: $BluetoothHardwareId"
    Write-Host "INF:         $inf"

    $rebootRequired = $false
    $updated = [PnpDriverUpdate]::UpdateDriverForPlugAndPlayDevicesW(
        [IntPtr]::Zero, $BluetoothHardwareId, $inf, 1, [ref]$rebootRequired)

    if (-not $updated) {
        $error = [System.ComponentModel.Win32Exception]::new(
            [System.Runtime.InteropServices.Marshal]::GetLastWin32Error())
        throw "UpdateDriverForPlugAndPlayDevices failed: $($error.Message)"
    }

    Write-Host "Driver update requested. Reboot required: $rebootRequired"
    Restart-BluetoothFilter

    Write-Step "Result"
    $device = Get-BluetoothFilter
    if (-not $device) {
        Write-Warning "Bluetooth filter did not come back; turn the trackpad off/on or reboot."
        return
    }

    $version = Get-DriverVersion $device.InstanceId
    Write-Host "Device:  $($device.InstanceId)"
    Write-Host "Version: $version"

    if ($version -like "2021.3979*") {
        Write-Host "[PASS] imbushuo 2105-3979 Bluetooth filter is active." -ForegroundColor Green
        Write-Host "Test cursor smoothness now. To roll back, run:"
        Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\switch-bt-driver-v23.ps1')`"" -ForegroundColor Yellow
    }
    else {
        Write-Warning "The Bluetooth filter did not switch to 2021.3979."
        Write-Host "Open Device Manager > Bluetooth > Apple Multi-touch Trackpad HID Filter >"
        Write-Host "Update driver > Browse > $DriverPath"
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
