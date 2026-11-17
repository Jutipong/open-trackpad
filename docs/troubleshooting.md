# Troubleshooting

## Reporting a problem

1. Note the local time and what you did.
2. Open **Advanced > Export diagnostics** or run:

   ```powershell
   OpenTrackpad.App.exe --export-diagnostics
   Get-Content "$env:LOCALAPPDATA\OpenTrackpad\last-diagnostics.txt"
   ```

3. Send the zip from `%LOCALAPPDATA%\OpenTrackpad\OpenTrackpad-diagnostics-*.zip`.
   It contains rotated `app.log` files, `settings.json`, a device summary, and
   `driver-status.json` when the installed scripts are present.

The app version is shown next to the title and in the tray tooltip. Every startup
logs `OpenTrackpad vX.Y.Z started: os=..., transport=..., ...`.

## The trackpad is not detected

1. Check USB first with a known data-capable cable and port.
2. Open Device Manager and confirm a Precision Touchpad device exists.
3. Run the status report from an elevated PowerShell:

   ```powershell
   .\installer\scripts\Test-DriverStatus.ps1
   ```

4. If the status shows no driver package, install the v2.3 driver with
   `installer\scripts\Install-Driver.ps1`.
5. If only Bluetooth fails, remove the pairing in Windows Settings and pair again;
   use code `0000` if asked.

## Battery is unavailable

- USB: the v2.3 control pipe must be running. Check `ControlPipeAvailable` in
  `Test-DriverStatus.ps1`.
- Bluetooth: the battery comes from the Windows device property. If the value is
  missing, disconnect/reconnect the trackpad.
- Pipe access denied after a driver update: run `scripts\enable-user-access.ps1`
  as administrator once.

## Haptic preview fails

- Run the app or `scripts\enable-user-access.ps1` once as administrator.
- Confirm `AllowNonAdminAccess=1` for both:
  - `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters`
  - `HKLM\SYSTEM\CurrentControlSet\Services\AmtPtpHidFilter\Parameters`
- The v2.3 pipe exposes only `0x00` reload, `0x01` battery, and `0x02` actuate.
  Do not send HID output `0x53`; it times out on this device.

## 3-finger drag leaves the left button held

- Use the tray menu **Release left button**. It turns drag off and sends three
  fail-open left-up events.
- The app starts a watchdog process that releases the left button when the app exits.
- `installer\scripts\Stop-App.ps1` also sends three fail-open left-up events and
  restores a stale `ThreeFingerSlideEnabled` backup.
- If it still happens, export diagnostics and record the steps from
  `docs/manual-test-checklist.md` (stuck-button section).

## Bluetooth cursor movement is less smooth than USB

This is the one-finger pointer path owned by Windows and the v2.3 Bluetooth filter;
the app cannot interpolate it. Try:

- update the Bluetooth adapter driver (`scripts\install-realtek-bt.ps1` for the
  tested Realtek adapter),
- keep 2.4 GHz devices away from the trackpad,
- disable power saving for the adapter and the trackpad HID filter,
- use USB when precise movement matters.

## Gesture preset changed but nothing happens

Windows caches three- and four-finger gesture values. Sign out and back in, or restart
Explorer, then test the gesture again. The app stores the previous values so
"Restore previous Windows gestures" can put them back.

## Windows touchpad settings were changed

- Scroll speed has a Restore button in the app.
- If the app crashed while 3-finger drag was on, run
  `installer\scripts\Stop-App.ps1` to restore `ThreeFingerSlideEnabled`.

## Installer reports a driver signature error

The installer verifies the catalog with `Get-AuthenticodeSignature` and requires a
valid Microsoft-signed catalog. Do not bypass it with `/force`. Re-download the
driver package and rebuild the installer.

## Logs

`app.log` rotates at 2 MB into `app.log.1`–`app.log.3` (about 6 MB total).

| Item | Path |
|---|---|
| App log | `%LOCALAPPDATA%\OpenTrackpad\app.log` |
| Rotated logs | `%LOCALAPPDATA%\OpenTrackpad\app.log.1`–`.3` |
| Diagnostics zips | `%LOCALAPPDATA%\OpenTrackpad\OpenTrackpad-diagnostics-*.zip` |
| Installer logs | `C:\ProgramData\OpenTrackpad\logs\` |
| Local captures | `C:\code\open-trackpad\logs\` |
