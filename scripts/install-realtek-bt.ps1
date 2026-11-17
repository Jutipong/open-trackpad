#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Install the Microsoft-signed Realtek Bluetooth 1.8.1060.3000 driver for
    USB\VID_1358&PID_C123 (Huawei MateBook 14 KLVL-WXX9 / RTL8822CE).

.DESCRIPTION
    The package comes from the Microsoft Update Catalog and is the newest
    available package that declares this exact hardware ID. The previous
    driver remains in the Driver Store; use Device Manager > Roll Back Driver
    if you want to return to it.

    This only replaces the Bluetooth filter driver. The Wi-Fi driver is not
    touched.
#>

[CmdletBinding()]
param(
    [string]$DriverPath = (Join-Path (Split-Path -Parent $PSScriptRoot) ".cache\realtek-bt-1.8.1060.3000")
)

$ErrorActionPreference = "Stop"

$LogDir = Join-Path (Split-Path -Parent $PSScriptRoot) "logs"
$LogPath = Join-Path $LogDir "install-realtek-bt.log"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Get-BluetoothAdapter {
    Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -like "USB\VID_1358&PID_C123\*" } |
        Select-Object -First 1
}

function Get-DriverVersion([string]$InstanceId) {
    $value = Get-PnpDeviceProperty -InstanceId $InstanceId `
        -KeyName "DEVPKEY_Device_DriverVersion" -ErrorAction SilentlyContinue
    if ($null -eq $value) { return "unknown" }
    return $value.Data
}

try {
    Write-Step "Environment"
    $os = Get-CimInstance Win32_OperatingSystem
    $os | Select-Object Caption, Version, BuildNumber | Format-List

    $inf = Join-Path $DriverPath "Rtkfilter.inf"
    $sys = Join-Path $DriverPath "RtkBtfilter.sys"
    $cat = Join-Path $DriverPath "rtkfilter.cat"

    foreach ($file in @($inf, $sys, $cat)) {
        if (-not (Test-Path $file)) {
            throw "Driver file not found: $file"
        }
    }

    Write-Step "Verify catalog signature"
    $signature = Get-AuthenticodeSignature $cat
    $signature |
        Select-Object Status, StatusMessage,
            @{n = "Subject"; e = { $_.SignerCertificate.Subject } },
            @{n = "NotAfter"; e = { $_.SignerCertificate.NotAfter } } |
        Format-List

    if ($signature.Status -ne "Valid") {
        throw "Driver catalog signature is not valid: $($signature.Status)"
    }
    if ($signature.SignerCertificate.Subject -notmatch "Microsoft") {
        throw "Unexpected catalog signer: $($signature.SignerCertificate.Subject)"
    }

    $adapter = Get-BluetoothAdapter
    if (-not $adapter) {
        throw "Realtek Bluetooth adapter (USB\VID_1358&PID_C123) not found."
    }

    Write-Step "Current driver"
    $currentVersion = Get-DriverVersion $adapter.InstanceId
    Write-Host "Device:  $($adapter.InstanceId)"
    Write-Host "Version: $currentVersion"

    Write-Step "Install Realtek Bluetooth 1.8.1060.3000"
    & pnputil.exe /add-driver $inf /install 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 259) {
        throw "pnputil /add-driver failed with exit code $LASTEXITCODE"
    }
    if ($LASTEXITCODE -eq 259) {
        Write-Host "Driver package already present in the Driver Store; continuing."
    }

    & pnputil.exe /scan-devices 2>&1 | ForEach-Object { Write-Host $_ }
    Start-Sleep -Seconds 3

    Write-Step "Restart Bluetooth adapter"
    $adapter = Get-BluetoothAdapter
    if ($adapter) {
        & pnputil.exe /restart-device "$($adapter.InstanceId)" 2>&1 |
            ForEach-Object { Write-Host $_ }
        Start-Sleep -Seconds 8
    }

    Write-Step "Result"
    $adapter = Get-BluetoothAdapter
    if (-not $adapter) {
        Write-Warning "Bluetooth adapter is not present after restart; reboot Windows."
        return
    }

    $newVersion = Get-DriverVersion $adapter.InstanceId
    Write-Host "Device:  $($adapter.InstanceId)"
    Write-Host "Version: $newVersion"

    if ($newVersion -eq "1.8.1060.3000") {
        Write-Host "[PASS] Realtek Bluetooth 1.8.1060.3000 is active." -ForegroundColor Green
        Write-Host "If the trackpad does not reconnect, turn it off and on again."
    }
    else {
        Write-Warning "The adapter did not switch to 1.8.1060.3000 automatically."
        Write-Host "Open Device Manager > Bluetooth > Realtek Bluetooth Adapter >"
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
