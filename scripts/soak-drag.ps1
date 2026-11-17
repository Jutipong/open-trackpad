#Requires -Version 5.1
<#
.SYNOPSIS
    Monitor the left mouse button and process lifecycle during a drag soak.
.DESCRIPTION
    Logs button transitions and detects a stuck left button after OpenTrackpad
    has exited. If the button is still held while no app or watchdog process is
    running, the script tries a fail-open left-up and records it as a failure.
    Run this in a separate terminal while performing the manual drag checklist.
#>

[CmdletBinding()]
param(
    [int]$Minutes = 0,
    [int]$Seconds = 0,
    [int]$StuckSeconds = 3,
    [string]$LogPath = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $LogPath) {
    $LogPath = Join-Path $RepoRoot (
        "logs\soak-left-button-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null

if ($Seconds -le 0 -and $Minutes -gt 0) {
    $Seconds = $Minutes * 60
}
if ($StuckSeconds -lt 1) {
    throw "StuckSeconds must be at least 1."
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class OpenTrackpadSoakInput
{
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

    public static bool LeftDown()
    {
        return (GetAsyncKeyState(0x01) & 0x8000) != 0;
    }

    public static void LeftUp()
    {
        var input = new INPUT
        {
            type = 0,
            mi = new MOUSEINPUT { dwFlags = 0x0004 }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }
}
"@

function Write-Log([string]$Message) {
    $line = "{0:O} {1}" -f (Get-Date), $Message
    Write-Host $line
    Add-Content -Path $LogPath -Value $line
}

$deadline = if ($Seconds -gt 0) { (Get-Date).AddSeconds($Seconds) } else { [datetime]::MaxValue }
$downSince = $null
$lastStuckAttempt = [datetime]::MinValue
$stuckCount = 0
$downCount = 0
$maxHoldMs = 0
$lastProcessCheck = [datetime]::MinValue
$mainRunning = $false
$watchdogRunning = $false

Write-Log "Soak monitor: log=$LogPath seconds=$Seconds stuckThreshold=${StuckSeconds}s"
Write-Log "Start the soak steps in docs/manual-test-checklist.md (section 1). Ctrl+C stops."

try {
    while ((Get-Date) -lt $deadline) {
        $down = [OpenTrackpadSoakInput]::LeftDown()
        $now = Get-Date

        if ($down -and $null -eq $downSince) {
            $downSince = $now
            $downCount++
            Write-Log "LEFT DOWN (main=$mainRunning watchdog=$watchdogRunning)"
        }
        elseif (-not $down -and $null -ne $downSince) {
            $holdMs = [int]($now - $downSince).TotalMilliseconds
            if ($holdMs -gt $maxHoldMs) { $maxHoldMs = $holdMs }
            Write-Log "LEFT UP after ${holdMs}ms (main=$mainRunning watchdog=$watchdogRunning)"
            $downSince = $null
        }

        if (($now - $lastProcessCheck).TotalSeconds -ge 1) {
            $lastProcessCheck = $now
            $processes = Get-CimInstance Win32_Process -Filter "Name='OpenTrackpad.App.exe'" `
                -ErrorAction SilentlyContinue
            $mainRunning = @($processes | Where-Object {
                $_.CommandLine -notmatch "--drag-watchdog"
            }).Count -gt 0
            $watchdogRunning = @($processes | Where-Object {
                $_.CommandLine -match "--drag-watchdog"
            }).Count -gt 0
        }

        if ($down -and $null -ne $downSince) {
            $heldSeconds = ($now - $downSince).TotalSeconds
            $noOwner = -not $mainRunning -and -not $watchdogRunning
            $retryReady = ($now - $lastStuckAttempt).TotalSeconds -ge 2

            if ($noOwner -and $heldSeconds -ge $StuckSeconds -and $retryReady) {
                $stuckCount++
                $lastStuckAttempt = $now
                Write-Log "STUCK: left button held ${heldSeconds:0.0}s with no app/watchdog; sending left-up"
                1..3 | ForEach-Object {
                    [OpenTrackpadSoakInput]::LeftUp()
                    Start-Sleep -Milliseconds 20
                }
            }
        }

        Start-Sleep -Milliseconds 50
    }
}
finally {
    $summary = "Summary: downEvents=$downCount maxHoldMs=$maxHoldMs stuckEvents=$stuckCount log=$LogPath"
    Write-Log $summary
}

if ($stuckCount -gt 0) {
    exit 1
}
