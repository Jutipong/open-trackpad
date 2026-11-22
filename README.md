# OpenTrackpad

A personal, open-source companion for the Apple Magic Trackpad 2 on Windows 11.

The goal is a simple install with:

- reliable Windows Precision Touchpad behavior over USB and Bluetooth
- battery level and low-battery alerts
- haptic / silent click settings
- pointer, palm, and pressure settings
- scroll speed and native Windows touchpad options
- 3-finger drag
- adjustable physical click pressure

Status: **Phase 1 MVP — macOS-style Trackpad settings on USB and Bluetooth with three sidebar sections: Point & Click, Scroll & Gestures, and Device & App. Device status updates live within 2 seconds. Adjustable click pressure is out of scope; OpenTrackpad does not fork the driver.**

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

## Bluetooth

The v2.3 driver already includes the Bluetooth HID filter. No driver fork or test signing is needed.

1. Unplug the Lightning/USB cable from the trackpad.
2. In Windows: `Settings > Bluetooth & devices > Add device > Bluetooth`.
3. Turn the trackpad off and on again, wait for **Magic Trackpad**, then pair.
   If Windows asks for a code, enter `0000`.
4. Open OpenTrackpad. The status line changes to **เชื่อมต่อผ่าน Bluetooth**.

To allow haptic preview without running the whole app elevated, run once as administrator:

```powershell
.\scripts\enable-user-access.ps1
```

This sets `AllowNonAdminAccess=1` for both USB and Bluetooth and restarts the devices.
Bluetooth battery is read from the Windows device property, so polling does not need elevation.

### Bluetooth smoothness

One-finger cursor movement over Bluetooth can be less smooth than USB. This comes from the
v2.3 Bluetooth filter/Windows Bluetooth stack, not from OpenTrackpad: raw captures show scan-time
gaps (dropped reports) on BT while USB stays at a steady ~90 Hz. The app does not control the
one-finger pointer path, so it cannot interpolate that movement.

Things that help on some machines:

- Update the Bluetooth adapter driver (a 2020 Realtek 1.8.x driver is a common cause).
  On the Huawei MateBook 14 KLVL-WXX9 (`USB\VID_1358&PID_C123`), the newest package in the
  Microsoft Update Catalog is Realtek **1.8.1060.3000** (2021-10-25, Microsoft WHCP). The
  cached package and installer are in this repo:

  ```powershell
  # Run as administrator
  .\scripts\install-realtek-bt.ps1
  ```

  Roll back with Device Manager > Bluetooth > Realtek Bluetooth Adapter > Properties >
  Driver > Roll Back Driver if the new driver misbehaves.

  Measured on this MateBook 14: the update reduced Bluetooth scan-time gaps (dropped reports)
  from roughly **15% to ~4%**, and cursor movement felt better. It is still not identical to USB.

  The imbushuo 2105-3979 Bluetooth filter was also tested: it did **not** improve smoothness
  and uses a different 50-byte PTP report, so it was rolled back to v2.3. The helper scripts
  remain in `scripts/switch-bt-driver-imbushuo.ps1` and `scripts/switch-bt-driver-v23.ps1`
  for future experiments.
- Turn off other 2.4 GHz devices nearby (Bluetooth headphones, 2.4 GHz Wi-Fi, wireless dongles).
- Disable power saving for the Bluetooth adapter and the trackpad filter in Device Manager.
- Use USB when precise pointer movement matters; Bluetooth is best for cable-free use.

## Current features

- macOS-style sidebar: Point & Click, Scroll & Gestures, and Device & App
- Driver/device health banner with one-click Enable access, a Driver status dialog, and a compact status card
- Version in the title bar and tray tooltip, plus version in every startup log
- Rotating `app.log`, tray "Release left button" fail-open action, and diagnostics export
- USB and Bluetooth transport with automatic battery source selection
- Live device connect/disconnect status (1-second poll, no restart needed)
- Settings apply automatically (sliders debounce; driver changes prompt UAC)
- Tracking speed (Windows touchpad cursor speed, live)
- Touchpad sensitivity (five Windows precision-touchpad levels)
- Click feel: Off / Light / Medium / Firm, with preview
- Full disable that turns off both haptics and the Force Touch button
- Tap to click, tap and drag, two-finger secondary click, and bottom-right right-click corner
- Leave touchpad on when a mouse is connected
- Natural scrolling, pinch zoom, scroll speed with backup/restore
- 3-finger drag with palm filtering, tremor suppression, adjustable speed, fail-open left-button release, and stale-frame watchdog
- Battery level polling with 20%/10% alerts
- Pointer, palm, pressure, and invert-movement settings
- "Reset to defaults" on every settings section, with confirmation
- Start with Windows and single-instance tray app
- Thai / English UI that switches live from Device & App

Windows 11 24H2+ settings use `SPI_GET/SETTOUCHPADPARAMETERS`; the app falls back to
the PrecisionTouchPad registry values on older builds.

### macOS features that Windows does not expose

- Force Click (second-stage click)
- Look up & data detectors
- Smart zoom (two-finger double-tap)
- Rotate

These controls are shown disabled with a note instead of pretending to work.

## Installer

```powershell
# One-time build tool
winget install -e --id JRSoftware.InnoSetup --scope user

# Build the installer (publishes the app + bundles the cached v2.3 driver)
.\installer\build.ps1

# Output
installer\output\OpenTrackpad-Setup.exe
```

The app is published self-contained, so the installer does not need a .NET runtime on
the target machine (the installer is ~51 MB). It installs to `C:\Program Files\OpenTrackpad`,
verifies the driver catalog signature, installs the v2.3 driver with `pnputil` (no `/force`),
sets `AllowNonAdminAccess=1`, and creates Start Menu/desktop shortcuts.
Uninstall keeps the driver by default; use `installer\scripts\Uninstall-Driver.ps1`
from an elevated PowerShell to remove it.

The driver package is read from `.cache\v2.3-amd64\AMD64` (gitignored). If it is missing,
the installer is built app-only and assumes the driver is already installed.

## Tests

```powershell
dotnet test app\OpenTrackpad.slnx

# Installer/driver static tests (Pester 5)
Install-Module Pester -RequiredVersion 5.7.1 -Scope CurrentUser -Force
pwsh -NoProfile -Command "Invoke-Pester installer/tests"
```

98 xUnit cases cover the drag FSM (including a replay of a real 3-finger capture),
battery alert policy, health evaluation, haptic presets, PTP report parsing, control-pipe protocol,
Bluetooth IOCTL codes, USB/Bluetooth device matching, device-change monitoring,
and the named-pipe client against a fake server.

39 Pester tests cover Driver Store parsing, installer/driver safety invariants
(no `/force`, signature check, dry-run uninstall, connected-device guard,
PowerShell 5.1 compatibility), and script parsing.

Soak helpers:

```powershell
# 20 watchdog kill/crash iterations (does not need the trackpad)
.\scripts\test-stuck-button.ps1 -Iterations 20

# Monitor the left button during a manual drag soak
.\scripts\soak-drag.ps1 -Minutes 480
```

Hardware-gated integration tests run with `OPENTRACKPAD_HW=1`; see
[docs/manual-test-checklist.md](docs/manual-test-checklist.md) for the manual and soak checklist.

## Out of scope

- Adjustable click pressure (host-click mode would require a driver fork; not planned)
- Force Click, Look up, Smart zoom, and Rotate (Windows does not expose them)

## Not implemented yet

- 8-hour soak, kill/crash soak, and Bluetooth reconnect/sleep test results

See [plan.md](plan.md) for the full implementation plan, architecture, protocol notes, test plan, and risks.
See [docs/troubleshooting.md](docs/troubleshooting.md) for common device, battery, haptic, and drag issues.

For support, use **Device & App > Diagnostics > Export diagnostics** (or `OpenTrackpad.App.exe --export-diagnostics`)
to create a zip with rotated logs, settings, device summary, and driver status.

## Target hardware

- Apple Magic Trackpad 2 (Lightning, `VID_05AC&PID_0265`)
- Windows 11 x64
- USB and Bluetooth

## Baseline driver

The plan starts with the Microsoft-signed
[vitoplantamura/MagicTrackpad2ForWindows](https://github.com/vitoplantamura/MagicTrackpad2ForWindows)
v2.3 driver. The app-only route is enough for battery, haptics, pointer settings, scroll,
and 3-finger drag. Adjustable click pressure is not planned because it would require
forking the driver.

## License

The app, installer, and tools are MIT-licensed; see [LICENSE](LICENSE).
The bundled v2.3 driver is GPL-2.0; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for attribution and source.
