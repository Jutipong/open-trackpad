#Requires -Version 5.1
<#
.SYNOPSIS
    Report the Magic Trackpad driver, PTP device, and control pipe status.
#>

[CmdletBinding()]
param(
    [switch]$Json
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "DriverStore.ps1")

function Test-ControlPipe {
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(
            ".", "AmtPtpControlPanelUsbUmInterface", [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(2000)
        $pipe.ReadMode = [System.IO.Pipes.PipeTransmissionMode]::Message
        $pipe.Write([byte[]](0x01), 0, 1)
        $pipe.Flush()

        $response = New-Object byte[] 2
        $read = $pipe.Read($response, 0, 2)
        $pipe.Dispose()

        if ($read -eq 2 -and $response[0] -eq 0) {
            return [pscustomobject]@{
                Available = $true
                Battery   = [int]$response[1]
                Error     = $null
            }
        }

        return [pscustomobject]@{
            Available = $false
            Battery   = $null
            Error     = "Unexpected response: status=0x{0:X2}" -f $response[0]
        }
    }
    catch {
        return [pscustomobject]@{
            Available = $false
            Battery   = $null
            Error     = $_.Exception.Message
        }
    }
}

$devices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match "VID_05AC&PID_0265" })

$ptp = @($devices | Where-Object {
    $_.FriendlyName -match "Precision Touchpad|Magic Trackpad|Multi-touch Trackpad"
})

$packages = @(Get-AmtPtpPackages |
    Select-Object Driver, OriginalFileName, ProviderName, Version, Date)

$allowNonAdmin = $null
$paramKey = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters"
if (Test-Path $paramKey) {
    $allowNonAdmin = (Get-ItemProperty $paramKey -Name AllowNonAdminAccess -ErrorAction SilentlyContinue).AllowNonAdminAccess
}

$pipe = Test-ControlPipe

$result = [pscustomobject]@{
    Timestamp            = (Get-Date).ToString("O")
    OSBuild              = [Environment]::OSVersion.Version.Build
    AppleDeviceCount     = $devices.Count
    PtpPresent           = ($ptp.Count -gt 0)
    PtpDevice            = $(if ($ptp.Count -gt 0) { $ptp[0].FriendlyName } else { $null })
    DriverPackages       = $packages
    AllowNonAdminAccess  = $allowNonAdmin
    ControlPipeAvailable = $pipe.Available
    BatteryLevel         = $pipe.Battery
    ControlPipeError     = $pipe.Error
}

if ($Json) {
    $result | ConvertTo-Json -Depth 5
    return
}

Write-Host "OpenTrackpad driver status" -ForegroundColor Cyan
Write-Host "Timestamp:            $($result.Timestamp)"
Write-Host "OS build:             $($result.OSBuild)"
Write-Host "Apple devices:        $($result.AppleDeviceCount)"
Write-Host "PTP present:          $($result.PtpPresent) ($($result.PtpDevice))"
Write-Host "AllowNonAdminAccess:  $($result.AllowNonAdminAccess)"
Write-Host "Control pipe:         $($result.ControlPipeAvailable)"
Write-Host "Battery:              $(if ($null -ne $result.BatteryLevel) { "$($result.BatteryLevel)%" } else { "n/a" })"

if ($packages.Count -eq 0) {
    Write-Host "Driver packages:      none found (or Get-WindowsDriver needs elevation)" -ForegroundColor Yellow
}
else {
    Write-Host "Driver packages:"
    $packages | Format-Table -AutoSize
}
