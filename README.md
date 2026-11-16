# OpenTrackpad

A personal, open-source companion for the Apple Magic Trackpad 2 on Windows 11.

The goal is a simple install with:

- reliable Windows Precision Touchpad behavior over USB (Bluetooth later)
- battery level and low-battery alerts
- haptic / silent click settings
- pointer, palm, and pressure settings
- scroll speed and native Windows gesture presets
- 3-finger drag
- adjustable physical click pressure

Status: **Phase 1 MVP — macOS-style Trackpad settings on USB: Point & Click, Scroll & Zoom, More Gestures, and Advanced. Click pressure is not available with the v2.3 user-mode surface and is deferred to a driver fork.**

Current machine state and next steps are documented at the top of [plan.md](plan.md#0-current-status--handoff-2026-11-17). The Phase 0.5 findings are in [docs/spike-report.md](docs/spike-report.md).

## Quick start (development)

```powershell
.\scripts\run-app.ps1
```

Or manually:

```powershell
dotnet build app\OpenTrackpad.slnx
app\OpenTrackpad.App\bin\Debug\net10.0-windows\OpenTrackpad.App.exe
```

The script stops any running instance first, rebuilds, and restarts the tray app.
Battery polling and haptic preview work without elevation; changing driver settings prompts
for UAC through a small elevated helper.

## Current features

- macOS-style tabs: Point & Click, Scroll & Zoom, More Gestures, Advanced
- Tracking speed (Windows touchpad cursor speed, live)
- Click feel: Light / Medium / Firm + Quiet Click, with preview
- Tap to click, two-finger secondary click
- Natural scrolling, pinch zoom, scroll speed with backup/restore
- 3-finger drag with fail-open left-button release and stale-frame watchdog
- Battery level polling with 20%/10% alerts
- Pointer, palm, pressure, and invert-movement settings
- Start with Windows and single-instance tray app
- Thai-first UI with English technical terms

Windows 11 24H2+ settings use `SPI_GET/SETTOUCHPADPARAMETERS`; the app falls back to
the PrecisionTouchPad registry values on older builds.

### macOS features that Windows does not expose

- Force Click (second-stage click)
- Look up & data detectors
- Smart zoom (two-finger double-tap)
- Rotate

These controls are shown disabled with a note instead of pretending to work.

## Tests

```powershell
dotnet test app\OpenTrackpad.slnx
```

49 tests cover the drag FSM, battery alert policy, haptic presets, PTP report parsing,
control-pipe protocol, and the named-pipe client against a fake server.

## Not implemented yet

- Adjustable click pressure (needs a driver fork for host-click mode)
- Bluetooth support
- Installer/uninstaller
- Live device disconnect/reconnect within 2 seconds
- 3/4-finger gesture presets (use Windows touchpad settings for now)
- 8-hour soak and kill/crash soak tests

See [plan.md](plan.md) for the full implementation plan, architecture, protocol notes, test plan, and risks.

See [plan.md](plan.md) for the full implementation plan, architecture, protocol notes, test plan, and risks.

## Target hardware

- Apple Magic Trackpad 2 (Lightning, `VID_05AC&PID_0265`)
- Windows 11 x64
- USB first; Bluetooth later

## Baseline driver

The plan starts with the Microsoft-signed
[vitoplantamura/MagicTrackpad2ForWindows](https://github.com/vitoplantamura/MagicTrackpad2ForWindows)
v2.3 driver. The app-only route is enough for battery, haptics, pointer settings, scroll,
and 3-finger drag. The v2.3 driver does not expose host-click mode to user mode, so
adjustable click pressure requires a driver fork (deferred).

## License

The app, installer, and tools are intended to be MIT-licensed.
Any driver fork will be GPL-2.0 with upstream attribution.
See the plan for details.
