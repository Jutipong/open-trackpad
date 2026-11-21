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

- [ ] Click feel Off / Light / Medium / Firm applies without reboot
- [ ] Off is silent and the Force Touch button still works
- [ ] Disable haptics and Force Touch button completely
- [ ] Haptic preview works on USB
- [ ] Haptic preview works on Bluetooth
- [ ] Firmware click still works after changing any haptic mode

## 4. Pointer and palm

- [ ] Tracking speed 1–20 applies live
- [ ] Touchpad sensitivity: all five levels apply live
- [ ] Tap to click on/off
- [ ] Tap and drag on/off (only available while tap to click is on)
- [ ] Two-finger secondary click
- [ ] Right-click in the bottom-right corner on/off
- [ ] Leave touchpad on when a mouse is connected on/off
- [ ] Palm rejection and ignore-near-fingers
- [ ] Invert movement
- [ ] Stop pressure / stop size round-trip after reload
- [ ] Point & Click reset restores tracking 10, sensitivity Medium, tap off,
      tap-and-drag on, secondary click off
- [ ] Advanced reset restores leave-on-with-mouse on
- [ ] Restart app: values persist

## 5. Scroll and zoom

- [ ] Natural scrolling on/off
- [ ] Pinch zoom on/off
- [ ] Scroll speed applies live and survives reboot
- [ ] Restore scroll speed returns the original value
- [ ] Two-finger scroll smoothness acceptable on the tested transport

## 6. Windows gestures (managed in Windows)

- [ ] "Open Windows touchpad settings" opens the Windows page from More Gestures
- [ ] The app does not change 3/4-finger swipe actions
- [ ] 4-finger Windows gestures still work while 3-finger drag is enabled

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
- [ ] Health banner is hidden when driver, device, and access are ready
- [ ] Health banner shows driver missing, device missing, and access denied
- [ ] Enable access runs UAC, restarts the trackpad, and clears the banner
- [ ] Driver status dialog shows the script output and Copy works
- [ ] Advanced health values update on connect/disconnect
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
| Gesture settings | | |
| 3-finger drag | | |
| Battery/UI | | |
| Installer | | |

Logs:

- `%LOCALAPPDATA%\OpenTrackpad\app.log`
- `C:\ProgramData\OpenTrackpad\logs\`
- `logs\` in the repo for local captures
