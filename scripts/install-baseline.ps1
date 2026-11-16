#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Install the Microsoft-signed MagicTrackpad2ForWindows v2.3 baseline driver.
.DESCRIPTION
    Installs the AMD64 driver package from the local cache. This script is only
    for the baseline phase; it does not modify Secure Boot or test signing.
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$InfPath = Join-Path $RepoRoot ".cache\v2.3-amd64\AMD64\AmtPtpDevice.inf"
$LogDir = Join-Path $RepoRoot "logs"
$LogPath = Join-Path $LogDir "baseline-install.log"

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

try {
    Write-Step "Environment"
    Get-CimInstance Win32_OperatingSystem |
        Select-Object Caption, Version, BuildNumber |
        Format-List

    if (-not (Test-Path $InfPath)) {
        throw "Driver INF not found: $InfPath"
    }

    Write-Step "Verify driver catalog signature"
    $CatPath = Join-Path (Split-Path -Parent $InfPath) "amtptpdevice.cat"
    $Sig = Get-AuthenticodeSignature $CatPath
    $Sig | Select-Object Status, StatusMessage,
        @{n = "Subject"; e = { $_.SignerCertificate.Subject } },
        @{n = "NotAfter"; e = { $_.SignerCertificate.NotAfter } } |
        Format-List

    if ($Sig.Status -ne "Valid") {
        throw "Driver catalog signature is not valid: $($Sig.Status)"
    }

    Write-Step "Current Apple HID devices (before)"
    Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match "VID_05AC&PID_0265" } |
        Select-Object Status, Class, FriendlyName, InstanceId |
        Format-Table -AutoSize

    Write-Step "Install driver package"
    $Output = & pnputil.exe /add-driver $InfPath /install 2>&1
    $Output | ForEach-Object { Write-Host $_ }

    if ($LASTEXITCODE -ne 0) {
        throw "pnputil failed with exit code $LASTEXITCODE"
    }

    Write-Step "Rescan devices"
    & pnputil.exe /scan-devices 2>&1 | ForEach-Object { Write-Host $_ }

    Start-Sleep -Seconds 3

    Write-Step "Apple HID devices (after)"
    Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match "VID_05AC&PID_0265" } |
        Select-Object Status, Class, FriendlyName, InstanceId |
        Format-Table -AutoSize

    Write-Step "Precision touchpad device check"
    $Ptp = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.FriendlyName -match "Precision Touchpad|Multi-touch Trackpad|Magic Trackpad" }

    if ($Ptp) {
        $Ptp | Select-Object Status, Class, FriendlyName, InstanceId |
            Format-Table -AutoSize
        Write-Host "[PASS] A Precision Touchpad / Magic Trackpad device is present." -ForegroundColor Green
    }
    else {
        Write-Warning "No Precision Touchpad device found yet. A reboot or replug may be required."
    }

    Write-Step "Driver package in Driver Store"
    & pnputil.exe /enum-drivers 2>&1 |
        Select-String -Pattern "AmtPtp|Magic Trackpad" -Context 0, 4 |
        ForEach-Object { Write-Host $_ }

    Write-Host ""
    Write-Host "Baseline install finished. Log: $LogPath" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Stop-Transcript | Out-Null
}
