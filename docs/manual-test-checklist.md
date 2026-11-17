# OpenTrackpad — Manual Test Checklist

Date:  
Tester:  
App version:  
Build/commit:  
Windows build:  
Transport under test: USB / Bluetooth / Both  

## Automated checks (run first)

```powershell
dotnet test app\OpenTrackpad.slnx
pwsh -NoProfile -Command "Invoke-Pester installer/tests"
```

Hardware-gated integration tests:

```powershell
$env:OPENTRACKPAD_HW = "1"
dotnet test app\OpenTrackpad.IntegrationTests
Remove-Item Env:\OPENTRACKPAD_HW
```

## 1. Stuck-button safety (P0)

Start the monitor in a separate terminal. It logs button transitions, process state,
and any stuck left button after the app exits:

```powershell
.\scripts\soak-drag.ps1 -Minutes 480
```

The watchdog kill/crash loop can run without the trackpad:

```powershell
.\scripts\test-stuck-button.ps1 -Iterations 20
```

Automated watchdog loop: [ ] 20/20 passed  Date: __________

Run each case while a window is being dragged with three fingers.

| Case | Expected | Result |
|---|---|---|
| Lift all fingers | Left button released, drag ends | |
| Disable 3-finger drag mid-drag | Drag ends, Windows gestures restored | |
| Ctrl+C / stop app from console | Watchdog releases left button | |
| Kill `OpenTrackpad.App.exe` from Task Manager mid-drag | Watchdog releases left button within ~1 s | |
| Kill the watchdog process mid-drag | App detects and releases left button | |
| Unplug USB mid-drag | Drag ends, no stuck button | |
| Turn Bluetooth off mid-drag | Drag ends, no stuck button | |
| Sleep/wake mid-drag | Drag ends, no stuck button | |

Repeat the kill-app-mid-drag case 20 times.

Long drag soak:

- [ ] 1,000 drag cycles: short, long, fast, slow, screen edges, window move, file drag
- [ ] No duplicate pointer movement while dragging
- [ ] No false drag with 1/2/4/5 contacts or palm contacts
- [ ] 4-finger Windows gestures still work while drag is enabled

## 2. Long-run and power (P0)

- [ ] 8-hour session with drag enabled; no crash, hang, or growing handle count
- [ ] 20 sleep/wake cycles; battery polling resumes, drag re-registers
- [ ] 20 USB plug/unplug cycles; status updates within 2 seconds, no restart needed
- [ ] 10 cold boots; app starts with Windows and reports status
- [ ] Bluetooth off/on reconnect; status updates within 2 seconds
- [ ] USB + Bluetooth connected at the same time: no duplicate reports or drags;
      USB remains the preferred transport

## 3. Click and haptics

- [ ] Click feel Light / Medium / Firm applies without reboot
- [ ] Quiet Click is silent
- [ ] Maximum feedback
- [ ] Disabled: no haptics and no Force Touch button
- [ ] Haptic preview works on USB
- [ ] Haptic preview works on Bluetooth
- [ ] Firmware click still works after changing any haptic mode

## 4. Pointer and palm

- [ ] Tracking speed 1–20 applies live
- [ ] Tap to click on/off
- [ ] Two-finger secondary click
- [ ] Palm rejection and ignore-near-fingers
- [ ] Invert movement
- [ ] Stop pressure / stop size round-trip after reload
- [ ] Restart app: values persist

## 5. Scroll and zoom

- [ ] Natural scrolling on/off
- [ ] Pinch zoom on/off
- [ ] Scroll speed applies live and survives reboot
- [ ] Restore scroll speed returns the original value
- [ ] Two-finger scroll smoothness acceptable on the tested transport

## 6. Windows gesture presets

- [ ] Change 4-finger swipe preset; verify after sign-out or Explorer restart
- [ ] 3-finger swipe preset is disabled while 3-finger drag is on
- [ ] Turn drag off: 3-finger swipe preset is restored and editable
- [ ] "Restore previous Windows gestures" returns the original values
- [ ] Custom (65535) 3/4-finger settings are preserved by backup/restore

## 7. 3-finger drag

- [ ] Drag starts only with exactly 3 stable `Tip=1` contacts
- [ ] Drag ends when any finger lifts
- [ ] Speed slider changes movement gain
- [ ] Windows 3-finger slide is disabled while drag is on
- [ ] Windows 3-finger slide is restored when drag is turned off or the app exits
- [ ] Stale gesture backup is restored after a crash

## 8. Battery and UI

- [ ] Battery level is within ±5% of a known reference
- [ ] 20% and 10% alerts fire once per threshold
- [ ] Refresh button updates the level
- [ ] Tray icon and tooltip show the level
- [ ] Status shows USB / Bluetooth / no device correctly
- [ ] Device disconnect shows "not found" without crashing
- [ ] Thai / English switch applies live
- [ ] Version badge matches the installer version
- [ ] Tray "Release left button" turns drag off and sends left-up
- [ ] Export diagnostics creates a zip with app.log, settings.json, system.txt
- [ ] Reset to defaults works on every tab

## 9. Installer lifecycle

- [ ] Fresh install on a clean machine
- [ ] Same-version reinstall is a no-op
- [ ] Upgrade from the previous version keeps settings
- [ ] Uninstall keeps the driver by default
- [ ] Driver uninstall dry-run makes no changes
- [ ] Driver uninstall refuses while the trackpad is connected
- [ ] Backup is exported before driver removal
- [ ] App uninstall removes startup/shortcuts, keeps driver

## 10. Sign-off

| Area | Pass | Notes |
|---|---|---|
| Stuck-button safety | | |
| Long-run/power | | |
| Click/haptics | | |
| Pointer/palm | | |
| Scroll/zoom | | |
| Gesture presets | | |
| 3-finger drag | | |
| Battery/UI | | |
| Installer | | |

Logs:

- `%LOCALAPPDATA%\OpenTrackpad\app.log`
- `C:\ProgramData\OpenTrackpad\logs\`
- `logs\` in the repo for local captures
