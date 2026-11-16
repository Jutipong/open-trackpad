#Requires -Version 5.1
<#
.SYNOPSIS
    Stop OpenTrackpad safely before install/uninstall.
.DESCRIPTION
    Stops the main tray process, lets the drag watchdog release the left mouse
    button, sends a fail-open left-up, and restores a backed-up 3-finger slide
    setting if the app was killed while drag was enabled.
#>

$ErrorActionPreference = "Stop"

$main = Get-CimInstance Win32_Process -Filter "Name='OpenTrackpad.App.exe'" |
    Where-Object { $_.CommandLine -notmatch '--drag-watchdog' } |
    Select-Object -First 1

if ($main) {
    Write-Host "Stopping OpenTrackpad (PID $($main.ProcessId))..."
    Stop-Process -Id $main.ProcessId -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# Let the watchdog exit, then remove any leftover instance.
Get-Process OpenTrackpad.App -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

# Final fail-open: always release the left mouse button.
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class OpenTrackpadInput
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

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

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

1..3 | ForEach-Object {
    [OpenTrackpadInput]::LeftUp()
    Start-Sleep -Milliseconds 20
}

# Restore a stale gesture backup from a crashed drag session.
$backup = Join-Path $env:LOCALAPPDATA "OpenTrackpad\three-finger-slide.backup"
if (Test-Path $backup) {
    try {
        $value = [int](Get-Content $backup -Raw).Trim()
        $key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad"
        if (Test-Path $key) {
            Set-ItemProperty -Path $key -Name ThreeFingerSlideEnabled -Type DWord -Value $value
            Write-Host "Restored ThreeFingerSlideEnabled=$value"
        }
        Remove-Item $backup -Force
    }
    catch {
        Write-Warning "Could not restore gesture backup: $($_.Exception.Message)"
    }
}

Write-Host "OpenTrackpad stopped." -ForegroundColor Green
