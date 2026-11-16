#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Restart USB host controllers that are in the Windows "Error" state.
.DESCRIPTION
    Some AMD USB 3.10 controllers enter CM_PROB_FAILED_POST_START (Code 43)
    after suspend/resume or a device fault. Disabling and re-enabling the
    controller resets it without a full reboot. Laptop keyboards/touchpads
    are usually I2C, so this should not remove the only input device.
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$LogDir = Join-Path $RepoRoot "logs"
$LogPath = Join-Path $LogDir "restart-usb-controller.log"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Start-Transcript -Path $LogPath -Force | Out-Null

try {
    $controllers = Get-PnpDevice -PresentOnly |
        Where-Object { $_.FriendlyName -match 'USB.*Host Controller' -and $_.Status -eq 'Error' }

    if (-not $controllers) {
        Write-Host "No USB host controller is in the Error state." -ForegroundColor Green
        return
    }

    foreach ($controller in $controllers) {
        Write-Host "Restarting $($controller.InstanceId) ..." -ForegroundColor Cyan
        Disable-PnpDevice -InstanceId $controller.InstanceId -Confirm:$false
        Start-Sleep -Seconds 3
        Enable-PnpDevice -InstanceId $controller.InstanceId -Confirm:$false
        Start-Sleep -Seconds 5
    }

    Write-Host ""
    Get-PnpDevice -PresentOnly |
        Where-Object { $_.FriendlyName -match 'USB.*Host Controller' } |
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
