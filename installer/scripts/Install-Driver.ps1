#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Install the Microsoft-signed MagicTrackpad2ForWindows v2.3 driver.
.DESCRIPTION
    Verifies the catalog signature, installs the AMD64 driver package with
    pnputil (no /force), enables non-admin access to the control pipe, and
    verifies that the Precision Touchpad and pipe respond.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DriverPath,
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "DriverStore.ps1")

$LogDir = Join-Path $env:ProgramData "OpenTrackpad\logs"
$LogPath = Join-Path $LogDir "install-driver.log"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

try {
    Write-Step "Environment"
    $os = Get-CimInstance Win32_OperatingSystem
    $os | Select-Object Caption, Version, BuildNumber | Format-List

    if ([Environment]::OSVersion.Version.Build -lt 22000) {
        throw "Windows 11 (build 22000+) is required."
    }
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw "64-bit Windows is required."
    }

    $inf = Join-Path $DriverPath "AmtPtpDevice.inf"
    if (-not (Test-Path $inf)) {
        throw "Driver INF not found: $inf"
    }

    Write-Step "Verify catalog signature"
    $cat = Get-ChildItem -Path $DriverPath -Filter "*.cat" | Select-Object -First 1
    if (-not $cat) {
        throw "Driver catalog not found in $DriverPath"
    }

    $signature = Get-AuthenticodeSignature $cat.FullName
    $signature |
        Select-Object Status, StatusMessage,
            @{n = "Subject"; e = { $_.SignerCertificate.Subject } },
            @{n = "NotAfter"; e = { $_.SignerCertificate.NotAfter } } |
        Format-List

    if ($signature.Status -ne "Valid") {
        throw "Driver catalog signature is not valid: $($signature.Status)"
    }
    if ($signature.SignerCertificate.Subject -notmatch "Microsoft") {
        Write-Warning "Catalog signer is not Microsoft: $($signature.SignerCertificate.Subject)"
    }

    Write-Step "Existing AmtPtpDevice packages"
    $packages = @(Get-AmtPtpPackages)
    if ($packages.Count -gt 1) {
        $packages | Select-Object Driver, OriginalFileName, ProviderName, Version, Date |
            Format-Table -AutoSize
        throw "Multiple AmtPtpDevice packages found. Stop and review the Driver Store."
    }

    $ptpBefore = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match "^USB\\VID_05AC&PID_0265&MI_01\\" } |
        Select-Object -First 1

    if ($packages.Count -eq 1 -and $ptpBefore) {
        Write-Step "Driver already present"
        $packages | Select-Object Driver, OriginalFileName, ProviderName, Version, Date |
            Format-List
        Write-Host "Matching driver package and PTP device are present; skipping pnputil /add-driver."
    }
    else {
        Write-Step "Install driver package"
        & pnputil.exe /add-driver $inf /install 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 259) {
            throw "pnputil /add-driver failed with exit code $LASTEXITCODE"
        }
        if ($LASTEXITCODE -eq 259) {
            Write-Host "Driver package already present in the Driver Store; continuing."
        }

        & pnputil.exe /scan-devices 2>&1 | ForEach-Object { Write-Host $_ }
        Start-Sleep -Seconds 3
    }

    Write-Step "Enable non-admin control pipe access"
    $paramKey = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters"
    New-Item -Path $paramKey -Force | Out-Null
    New-ItemProperty -Path $paramKey -Name AllowNonAdminAccess -PropertyType DWord -Value 1 -Force |
        Out-Null
    Write-Host "AllowNonAdminAccess=1"

    $palmRejection = Get-ItemProperty -Path $paramKey -Name PalmRejection -ErrorAction SilentlyContinue
    if ($null -eq $palmRejection) {
        New-ItemProperty -Path $paramKey -Name PalmRejection -PropertyType DWord -Value 1 -Force |
            Out-Null
        Write-Host "PalmRejection=1 (default)"
    }
    else {
        Write-Host "PalmRejection=$($palmRejection.PalmRejection) (kept)"
    }

    if (-not $NoRestart) {
        $ptp = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
            Where-Object { $_.InstanceId -match "^USB\\VID_05AC&PID_0265&MI_01\\" } |
            Select-Object -First 1

        if ($ptp) {
            Write-Step "Restart PTP device"
            & pnputil.exe /restart-device "$($ptp.InstanceId)" 2>&1 |
                ForEach-Object { Write-Host $_ }
            Start-Sleep -Seconds 3
        }
    }

    Write-Step "Verify devices"
    $devices = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match "VID_05AC&PID_0265" }
    $devices | Select-Object Status, Class, FriendlyName, InstanceId | Format-Table -AutoSize

    $ptpDevices = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.FriendlyName -match "Precision Touchpad|Magic Trackpad|Multi-touch Trackpad" }
    if (-not $ptpDevices) {
        Write-Warning "No Precision Touchpad device found. Replug the trackpad or reboot."
    }

    Write-Step "Control pipe check"
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(
            ".", "AmtPtpControlPanelUsbUmInterface", [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(3000)
        $pipe.ReadMode = [System.IO.Pipes.PipeTransmissionMode]::Message
        $pipe.Write([byte[]](0x01), 0, 1)
        $pipe.Flush()

        $response = New-Object byte[] 2
        $read = $pipe.Read($response, 0, 2)
        $pipe.Dispose()

        if ($read -eq 2 -and $response[0] -eq 0) {
            Write-Host "[PASS] Control pipe battery: $($response[1])%" -ForegroundColor Green
        }
        else {
            Write-Warning "Unexpected control pipe response."
        }
    }
    catch {
        Write-Warning "Control pipe check failed: $($_.Exception.Message)"
    }

    Write-Host ""
    Write-Host "Driver install finished. Log: $LogPath" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Stop-Transcript | Out-Null
}
