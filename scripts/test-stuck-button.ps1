#Requires -Version 5.1
<#
.SYNOPSIS
    Exercise the drag watchdog: kill the parent process and verify the watchdog
    exits promptly (it sends a fail-open left-button up before exiting).
.DESCRIPTION
    Uses a dummy parent process, so this does not require the trackpad or a
    running tray app. Run 20 iterations to cover the kill/crash acceptance case.
#>

[CmdletBinding()]
param(
    [int]$Iterations = 20,
    [string]$AppPath = "",
    [int]$TimeoutSeconds = 5
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $AppPath) {
    $candidates = @(
        (Join-Path $RepoRoot "app\OpenTrackpad.App\bin\Debug\net10.0-windows\OpenTrackpad.App.exe"),
        (Join-Path $RepoRoot "installer\publish\OpenTrackpad.App.exe"),
        (Join-Path $env:ProgramFiles "OpenTrackpad\OpenTrackpad.App.exe")
    )
    $AppPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $AppPath -or -not (Test-Path $AppPath)) {
    throw "OpenTrackpad.App.exe not found. Build the app or pass -AppPath."
}
if ($Iterations -lt 1) {
    throw "Iterations must be at least 1."
}

$LogDir = Join-Path $RepoRoot "logs"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$LogPath = Join-Path $LogDir ("watchdog-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")

function Write-Log([string]$Message) {
    $line = "{0:O} {1}" -f (Get-Date), $Message
    Write-Host $line
    Add-Content -Path $LogPath -Value $line
}

Write-Log "Watchdog test: app=$AppPath iterations=$Iterations timeout=${TimeoutSeconds}s"
$passed = 0
$failed = 0

for ($i = 1; $i -le $Iterations; $i++) {
    $dummy = $null
    $watchdog = $null
    try {
        $dummy = Start-Process -FilePath "powershell.exe" `
            -ArgumentList "-NoProfile", "-Command", "Start-Sleep -Seconds 60" `
            -WindowStyle Hidden -PassThru

        $watchdog = Start-Process -FilePath $AppPath `
            -ArgumentList "--drag-watchdog", $dummy.Id `
            -WindowStyle Hidden -PassThru

        Start-Sleep -Milliseconds 500
        if ($watchdog.HasExited) {
            throw "watchdog exited before the parent process was killed (exit=$($watchdog.ExitCode))"
        }

        Stop-Process -Id $dummy.Id -Force -ErrorAction Stop
        $dummy = $null

        $exited = $watchdog.WaitForExit($TimeoutSeconds * 1000)
        if (-not $exited) {
            throw "watchdog did not exit within ${TimeoutSeconds}s"
        }
        if ($watchdog.ExitCode -ne 0) {
            throw "watchdog exited with code $($watchdog.ExitCode)"
        }

        $passed++
        Write-Log "[$i/$Iterations] PASS"
    }
    catch {
        $failed++
        Write-Log "[$i/$Iterations] FAIL: $($_.Exception.Message)"
    }
    finally {
        if ($dummy -and -not $dummy.HasExited) {
            Stop-Process -Id $dummy.Id -Force -ErrorAction SilentlyContinue
        }
        if ($watchdog -and -not $watchdog.HasExited) {
            Stop-Process -Id $watchdog.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Log "Result: passed=$passed failed=$failed log=$LogPath"
if ($failed -gt 0) {
    exit 1
}
