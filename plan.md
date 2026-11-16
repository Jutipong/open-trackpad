# OpenTrackpad — Implementation Plan

> Project: `Jutipong/open-trackpad`
> Working directory: `C:\code\open-trackpad`
> Plan date: 2026-11-16
> Status: Phase 0 baseline installed — implementation paused for a new session

---

## 0. Current status — handoff (2026-11-17)

### Completed

- Repo initialized at `C:\code\open-trackpad`
- Git remote set to `https://github.com/Jutipong/open-trackpad.git`
- `plan.md`, `README.md`, `.gitignore`, `.editorconfig` created
- Microsoft-signed **MagicTrackpad2ForWindows v2.3** driver installed (`oem84.inf`)
- **Apple USB Precision Touchpad Device (User-mode)** is active
- `AllowNonAdminAccess=1` set; USB control pipe works without elevation
- **Battery read verified** (93% initially, 100% after recovery)
- **Haptic click verified** via pipe (medium `0x060617`)
- `tools/RawInputProbe` **implemented**:
  - `caps`, `raw`, `drag`, `setfeature`, `getfeature`, `output`, `battery`, `haptic`, `reload`, `release`, `rawlist`, `descriptor`
  - built-in MT2 PTP report parser for the v2.3 static descriptor
- **Raw Input verified**: report `0x05`, 112 bytes, ~90 Hz, contacts/pressure/force/button
- **Pressure captured**: 0..97 with a comfortable hard press; 3-finger and 4th palm contacts observed
- **3-finger drag verified** in the app: three fingers drag a window; Windows 3-finger slide is disabled and restored
- **App MVP implemented**:
  - WPF tray app, single instance, show/hide, startup with Windows
  - Battery polling + 20%/10% alerts
  - Haptic presets + preview; apply via elevated helper (UAC)
  - Pointer/palm/pressure settings + reload
  - Scroll sensitivity with backup/restore
  - 3-finger drag toggle + fail-open left-up/watchdog
  - Drag watchdog process releases the left button if the app is killed mid-drag
  - macOS-style tabs: Point & Click, Scroll & Zoom, More Gestures, Advanced
  - Tracking speed, click feel, Quiet Click, tap-to-click, two-finger secondary click
  - Natural scrolling, pinch zoom, scroll speed
  - Windows 11 `SPI_GET/SETTOUCHPADPARAMETERS` for live settings; registry fallback
  - `app.log` operation/error log
- **Installer implemented and tested**:
  - Inno Setup 6 build (`installer\build.ps1`)
  - Bundles the cached v2.3 driver and app publish output
  - `Install-Driver.ps1` verifies the catalog signature, detects existing packages, handles pnputil 259, sets `AllowNonAdminAccess`, restarts PTP, checks the pipe
  - `Uninstall-Driver.ps1` dry-run/execute, connected-device guard, backup export, no `/force`
  - `Test-DriverStatus.ps1` diagnostics
  - Silent install, upgrade, and silent uninstall verified on this machine
- **Verified in UI**: battery display, haptic preview, haptic apply via UAC, drag on/off/on, tracking speed
- **Watchdog verified**: drag enabled → child process `--drag-watchdog` is running; 3-finger slide value is 0 while enabled
- **Fixed**: DWORD registry writes (uint → int), Raw Input thread restart after toggling, window-proc GC rooting, drag status/control race, SecurityException on driver settings
- **Windows limitations**: Force Click, Look up, Smart zoom, Rotate are shown disabled
- **Tests**: 49 passing (33 Core + 16 Interop)
- SPIKE result: MI_02 exposes output `0x53` but **no feature reports**; `0x21` host-click is not available in user mode
- SPIKE result: v2.3 pipe only supports `0x00`/`0x01`/`0x02`; no host-click command
- `docs/spike-report.md` written

### Pending

- Run stuck-button soak tests (lift, Ctrl+C, kill, unplug) and an 8-hour soak
- Live device connect/disconnect detection within 2 seconds
- 3/4-finger gesture presets (Windows touchpad settings cover the native ones)
- Pester tests for the installer/driver scripts
- `HidDescriptorProbe`, `LatencyProbe`
- Install **Visual Studio 2026 + WDK 28000.2526 + SDK 28000.2957** (only needed for driver fork)
- Optional driver fork for host-click click pressure

### Important paths and logs

| Item | Path |
|---|---|
| Baseline install log | `C:\code\open-trackpad\logs\baseline-install.log` |
| User access log | `C:\code\open-trackpad\logs\enable-user-access.log` |
| App operation log | `%LOCALAPPDATA%\OpenTrackpad\app.log` |
| App settings | `%LOCALAPPDATA%\OpenTrackpad\settings.json` |
| Driver cache (gitignored) | `C:\code\open-trackpad\.cache\v2.3-amd64\AMD64` |
| Install script | `C:\code\open-trackpad\scripts\install-baseline.ps1` |
| User access script | `C:\code\open-trackpad\scripts\enable-user-access.ps1` |
| Probe project | `C:\code\open-trackpad\tools\RawInputProbe` |
| App solution | `C:\code\open-trackpad\app\OpenTrackpad.slnx` |

### Next action in the new session

1. Run the stuck-button and 8-hour soak tests.
2. Add live device status + gesture presets.
3. Build the Inno Setup installer/uninstaller with the signed driver.
4. Decide whether to fork the driver for click pressure.

### Spike findings (2026-11-16)

- `caps` shows MI_00, MI_02, MI_03 only. The UMDF PTP collection MI_01 is hidden from HidSharp.
- MI_02 descriptor: input `0x3F`, output `0x53` (63-byte payload), **feature report length 0**.
- `setfeature 21 01` fails: report `0x21` is not declared.
- `output 53 ...` times out; do not use it. Haptics must go through the pipe.
- v2.3 `ControlPanel.c` supports only `0x00`/`0x01`/`0x02`.
- v2.3 `Device.c` performs host-click/haptic USB control transfers in the driver; there is no user-mode API.
- **App-only click pressure is not feasible with v2.3.** 3-finger drag remains the main app-only feature.
- Raw Input delivers the full 112-byte PTP report. Contact slots are 21 bytes; Scan Time is 16-bit.
- A fourth contact can be present with `Tip=0`, `Confidence=1`, pressure 0 (palm/ghost).
  The drag FSM must count only `Tip=1` contacts.
- One AMD USB host controller hit Problem Code 43 during testing; a disable/enable reset it to OK.
  The trackpad later recovered after a USB power cycle and a known data-capable cable/port.

---

## 1. Goal

Make a personal, easy-to-install Windows 11 setup for an Apple Magic Trackpad 2 (USB first, Bluetooth later) with:

- Reliable Windows Precision Touchpad behavior
- Battery level and low-battery alerts
- Haptic / silent click settings
- Pointer / palm / pressure settings
- Scroll speed and native Windows gestures
- 3-finger drag
- Adjustable physical click pressure
- One-click installer and safe uninstaller

Non-goals for the first milestone:

- Full Magic Utilities clone
- Per-app gestures
- Multi-device support beyond Magic Trackpad 2 / USB-C variant best effort
- macOS-style force-click actions
- Bluetooth polish (deferred until USB is stable)

---

## 2. Current machine facts

| Item | Value |
|---|---|
| OS | Windows 11 Pro, build 26300 |
| Secure Boot | ON |
| Trackpad | Connected via USB, `VID_05AC&PID_0265` |
| Current driver | **v2.3 Microsoft-signed** (`oem84.inf`), PTP active |
| Precision Touchpad device | `Apple USB Precision Touchpad Device (User-mode)` |
| Battery / haptic | Verified via control pipe (93%, medium click) |
| .NET SDK | 10.0.301 |
| Visual Studio / WDK | Not installed yet |
| Disk | C: ~119.5 GB free, D: ~4.8 GB free |
| Admin shell | No (UAC elevation required for setup/install) |

Target folder `C:\code\open-trackpad` now exists and is the current working directory.

---

## 3. Decisions

1. **Use Visual Studio 2026 Community + WDK 28000.2526 + SDK 28000.2957.**
   - VS 2026 is supported by the current WDK.
   - Install VS/WDK on C: (space), keep source/fixtures on the target repo.
   - Do not rely on Driver Test Explorer: known WDK/VS 18.5+ regression. Use xUnit, Pester, and custom harnesses.
2. **Use `vitoplantamura/MagicTrackpad2ForWindows` v2.3 signed driver as baseline.**
   - Microsoft-signed for Windows 11, no test signing or Secure Boot change.
   - Already provides PTP, battery, haptic, and pointer settings.
3. **Try the app-only route first.**
   - 3-finger drag and click pressure appear feasible in user mode:
     - 3-finger drag: Raw Input + SendInput (proven by ThreeFingerDrag).
     - Click pressure: host-click mode + raw pressure + SendInput + haptic (proven on Linux by mt2d; needs Windows spike).
   - If the spike succeeds: no custom driver, no test signing, easiest install.
   - If it fails: fork only the USB UMDF driver and use test signing, while keeping the same app interfaces.
4. **USB first, Bluetooth later.**
5. **Gesture allocation (configurable):**
   - 3-finger: drag (when enabled); 3-finger tap can remain middle click if it does not conflict.
   - 4-finger: back/forward navigation, task view, show desktop.
   - Disable Windows 3-finger swipe gestures while 3-finger drag is active.
6. **Tech stack:**
   - App: WPF on .NET 10, tray app, single instance.
   - Tests: xUnit (app/core/interop), Pester 5 (installer/lifecycle).
   - Installer: Inno Setup 6 + PowerShell scripts.
   - Probes: C#/C++ console tools for Raw Input, HID descriptors, latency, capture replay.
7. **Settings storage:**
   - App preferences: JSON under `%LOCALAPPDATA%\OpenTrackpad`.
   - Driver settings: the v2.3 registry values under `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\AmtPtpDeviceUsbUm\Parameters` (and the BT filter equivalent later).
   - Always back up and restore Windows touchpad settings changed by the app.
8. **Licensing:**
   - App/installer/tools: MIT.
   - Driver fork, if created: GPL-2.0, with upstream attribution.
   - Do not copy GPL-3.0 code from ThreeFingerDrag; use it as an idea/reference only.

---

## 4. Research findings and what to borrow

| Project | Use |
|---|---|
| `vitoplantamura/MagicTrackpad2ForWindows` v2.3 | Baseline driver; named pipe commands `0x00/0x01/0x02`; registry settings; haptic presets; per-contact pressure |
| `JeyKul/magic-trackpad-linux` (mt2d) | Host-click mode protocol; pressure click state machine; haptic reports; restore-on-exit pattern |
| `austinnixholm/ThreeFingerDrag` | Raw Input PTP registration; HID usage parsing; 3-finger drag via SendInput; Inno Setup tray app |
| `OnlineFix/Pearipherals` | Robust raw-contact map, pointer suppression, stale-contact expiry; extensive tests for input lifecycle and battery |
| `Suki-Kyaru/magic-trackpad-windows` | Installer lifecycle, exact Driver Store detection, signature/hash verification, no-op install, uninstall dry-run, connected-device guard, Pester tests |
| `sneakyjoeru/magic-trackpad-windows` | One-click installer, tray control panel, headless PowerShell utilities |
| `lc700x/MagicTrackPad2_Windows_Precision_Drivers` | Apple official signed driver reference/fallback; do not bundle Apple binaries in a public repo without checking licensing |
| `Pro-Qin/MacBook-Touchpad-Visualizer` | Raw Input live contact/pressure visualizer; diagnostic tool reference |
| Microsoft PTP Validation Guide + HLK | PTP descriptor and behavior acceptance criteria |
| Microsoft HID Tools (Waratah), `win-hid-dump`, `usbhid-dump`, `python-hid-parser` | Descriptor/report validation and test fixtures |
| `barryblueice/ESP32-Precision-TouchPad` | Optional PTP device emulator for descriptor/gesture testing |
| `piyush-18327/trackpad-free` | UX and feature ideas for a free companion app |

### Key protocol facts

v2.3 USB control interface (named pipe `\\.\pipe\AmtPtpControlPanelUsbUmInterface`):

| Command | Meaning | Response |
|---|---|---|
| `0x00` | Reload driver settings | `[status, 0]` |
| `0x01` | Get battery level | `[status, level]` |
| `0x02` | Fire haptic once; optional 3 strength bytes | `[status, 0]` |

Haptic presets (FeedbackClick encoding):

| Preset | Value |
|---|---|
| Light | `0x040415` |
| Medium | `0x060617` |
| Firm | `0x08081E` |

Driver registry values (v2.3 USB):

- `FeedbackClick`, `FeedbackRelease`
- `StopPressure`, `StopSize`
- `IgnoreNearFingers`, `IgnoreButtonFinger`
- `PalmRejection`
- `InvertMovement`
- `AllowNonAdminAccess`
- `ButtonDisabled`

Host-click mode / click pressure protocol (from mt2d, to verify on Windows):

| Report | Purpose |
|---|---|
| Feature `0x21`, `01` / `00` | Host-click mode on/off |
| Feature `0x22 ...` | Stored click feel (`0xCCBBSS`) |
| Feature `0x23 ...` | Stored release feel |
| Output `0x53 ...` | Fire actuator once |

USB topology:

- Interface 0: battery
- Interface 1: touches / PTP
- Interface 2: vendor feature/output reports (`0x21/0x22/0x23/0x53`)
- Interface 3: additional vendor/config

MT2 pressure range is approximately `0..253`; use hysteresis and a configurable threshold. T2 MacBook force range is different; not required for this project.

### Windows 11 native capabilities

- Scroll speed slider and `PanSensitivity` (build 26100.8973+; current 26300 supports it)
- `SystemParametersInfo(SPI_GET/SETTOUCHPADPARAMETERS)` (build 26027+)
- Advanced gestures: 3/4-finger back/forward navigation, task view, show desktop, etc.
- Therefore scroll speed and back/forward should not require custom driver work.

---

## 5. Architecture

### 5.1 Preferred: app-only on signed v2.3 driver

```text
Magic Trackpad 2 (USB)
  -> v2.3 signed PTP driver (Microsoft-signed)
  -> Windows precision touchpad stack
  -> OpenTrackpad app
       - battery/haptic/settings via driver pipe + registry
       - 3-finger drag via Raw Input + SendInput
       - click pressure via host-click HID reports + raw pressure + SendInput + haptic
       - scroll/gesture presets via Windows touchpad settings APIs
```

Benefits:

- No test signing, no Secure Boot change
- One-click installer with signed driver
- Fastest path to a stable daily setup

### 5.2 Fallback: fork v2.3 USB UMDF driver

Only if the app-only spike fails for a required feature:

```text
Magic Trackpad 2 (USB)
  -> OpenTrackpad USB UMDF driver (fork of v2.3, test-signed)
  -> Windows precision touchpad stack
  -> OpenTrackpad app (same interfaces + capability detection)
```

Fallback changes:

- Add `ThreeFingerDrag` and/or `ClickPressure` settings in the driver
- Keep the v2.3 pipe/registry interface compatible
- Add a capability/version marker
- Test signing required; Secure Boot must be off
- BT later ports the same logic to the HID filter

### 5.3 App components

| Component | Responsibility |
|---|---|
| `OpenTrackpad.App` | WPF tray app, settings window, notifications, startup |
| `OpenTrackpad.Core` | Pure logic: settings model, battery state, gesture FSM, click-pressure FSM, validation, backup/restore |
| `OpenTrackpad.Interop` | P/Invoke: Raw Input, HID, named pipe, SendInput, registry, `SystemParametersInfo` |
| `OpenTrackpad.Core.Tests` | Unit tests, no hardware |
| `OpenTrackpad.Interop.Tests` | Contract tests with fakes |
| `OpenTrackpad.IntegrationTests` | Hardware-gated tests, skipped without device |
| `tools/*` | Probes and diagnostic utilities |

Dependency rule: `App -> Core`, `App -> Interop`, `Interop -> Core` abstractions only; no UI code in Core/Interop. Inject clock, input injector, HID transport, pipe transport, and registry provider for testability.

---

## 6. Feature specification

### 6.1 Device status and detection

- Detect MT2 by hardware ID (`VID_05AC`, `PID_0265`) and PTP usage page.
- Show: connected (USB/BT), battery, driver status.
- Handle connect/disconnect/reconnect without app restart.
- Acceptance: tray updates within 2 seconds of plug/unplug; no crash on missing device.

### 6.2 Battery

- Read via pipe `0x01`.
- Poll every 60 seconds while idle; refresh on tray open and device connect.
- Low battery notifications at 20% and 10%; charging status if available.
- Backoff and no busy loop on failure.
- Acceptance: reported level within ±5% of a known reference; duplicate polls do not leak threads.

### 6.3 Haptic and silent click

- Presets: light, medium, firm, off/silent, maximum, disable feedback.
- Write `FeedbackClick` / `FeedbackRelease`, send reload `0x00`.
- Preview button fires `0x02` (with strength) and respects the 20 ms minimum gap.
- Acceptance: preset applies without reboot; silent mode produces no audible click; preview works on USB.

### 6.4 Pointer / palm / pressure settings

- Expose v2.3 controls:
  - `StopPressure`, `StopSize`
  - `IgnoreNearFingers`, `IgnoreButtonFinger`
  - `PalmRejection`
  - `InvertMovement`
- Apply + reload, validate ranges, backup/restore.
- Acceptance: values round-trip; invalid input rejected with a clear message.

### 6.5 Scroll and native gestures

- Scroll speed slider: use `SystemParametersInfo(SPI_SETTOUCHPADPARAMETERS)` if available; fallback to `HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\PanSensitivity` (0–100, default 50).
- Scroll direction toggle.
- Gesture presets:
  - 4-finger left/right: back/forward
  - 4-finger up/down: task view / show desktop
  - 3-finger: disabled when 3-finger drag is on
- Always back up the previous Windows settings and offer Restore.
- Acceptance: settings apply without reboot where Windows supports it; restore returns original values.

### 6.6 3-finger drag

Preferred implementation: user mode.

- Register Raw Input for `UsagePage=0x0D`, `Usage=0x05`, `RIDEV_INPUTSINK`.
- Parse HID usages: Contact ID, Tip Switch, X, Y, Contact Count, Scan Time, pressure if present.
- Maintain a contact map keyed by Contact ID; expire stale contacts; reject padding/ghost contacts.
- Activate only when exactly 3 stable contacts are old enough (e.g., 30–50 ms) and move beyond a threshold.
- Move cursor with `SendInput` relative motion and hold left button; release on:
  - any finger lift / contact count < 3
  - mode off
  - app exit / crash watchdog
  - device disconnect
- Suppress Windows pointer input while drag is active to avoid double movement.
- Disable Windows 3-finger gestures while active; restore on exit.
- Fail-open: always attempt left-button release, retry bounded times, never leave button stuck.

Acceptance:

- 1,000 drag cycles with no stuck button.
- Contact ID reuse, stale contacts, and padding contacts do not start a false drag.
- Killing the app during drag releases the button.
- 4-finger gestures still work.

### 6.7 Click pressure (host-click mode)

Preferred implementation: user mode on top of v2.3.

- Before enabling, verify the device exposes feature report `0x21` and output `0x53` on the vendor interface.
- Send host-click on `0x21 01`; on exit/crash/start send `0x21 00`.
- Read per-contact pressure from Raw Input; compute peak pressure.
- State machine: idle -> pending -> down -> (force) -> release, with threshold and hysteresis.
- On click transition: inject left down via `SendInput`; fire haptic click via pipe or output `0x53`.
- On release: inject left up; fire release feedback if configured.
- Tap-to-click remains Windows-owned; do not inject for taps.
- 3-finger drag overrides pressure clicks; coordinate both FSMs.
- Provide a clear toggle: “Use firmware click (default)” vs “Custom click pressure (experimental)”.
- Watchdog/restore: if the app exits or crashes, restore firmware click. On startup, always send `0x21 00` before optional enable.
- Acceptance:
  - Click at low/medium/high threshold behaves consistently.
  - No double click when firmware and host click are both active.
  - Killing the app restores firmware click every time.
  - Latency is acceptable compared with firmware click.

### 6.8 Installer

- One-click `Install.cmd`/Inno Setup:
  1. Check admin, Windows version, architecture.
  2. Detect and remove conflicting old Magic Trackpad drivers safely.
  3. Install signed v2.3 driver via `pnputil` (no `/force`).
  4. Install app to Program Files or LocalAppData (decide by scope).
  5. Create Start Menu shortcut and optional startup entry.
  6. Set `AllowNonAdminAccess` for the pipe if needed; request elevation only for settings writes.
  7. Open a short pairing/usage guide.
- Uninstall:
  - Stop app and release input/host-click state.
  - Remove startup/shortcuts/settings.
  - Optionally remove driver; default keep driver unless user chooses.
  - Restore Windows touchpad settings.
  - No destructive removal while trackpad is in use; show a guard.
- Fallback driver package (if needed): separate “dev/test-signed” installer; check Secure Boot, enable test signing, install cert, prompt reboot; uninstaller disables test signing and removes cert.

---

## 7. Repository layout

```text
C:\code\open-trackpad\
  README.md
  LICENSE
  .editorconfig
  .gitignore
  app\
    OpenTrackpad.sln
    OpenTrackpad.App\
    OpenTrackpad.Core\
    OpenTrackpad.Interop\
    OpenTrackpad.Core.Tests\
    OpenTrackpad.Interop.Tests\
    OpenTrackpad.IntegrationTests\
  driver\                       # optional fallback; GPL-2.0
    README.md
    upstream\                   # v2.3 fork
    package\
  installer\
    OpenTrackpad.iss
    scripts\
      Install-Driver.ps1
      Uninstall-Driver.ps1
      Test-DriverStatus.ps1
    tests\                      # Pester
  tools\
    RawInputProbe\
    HidDescriptorProbe\
    LatencyProbe\
    CaptureReplayer\
  tests\
    fixtures\
      descriptors\
      raw-input\
      battery\
      protocol\
      installer\
  docs\
    architecture.md
    test-plan.md
    manual-test-checklist.md
    troubleshooting.md
    licensing.md
```

Git:

- Remote: `https://github.com/Jutipong/open-trackpad.git`
- Branches: `main`, `feat/app-mvp`, `feat/3finger-drag`, `feat/click-pressure`, `feat/installer`, optional `feat/driver-usb`.
- Commit small; keep `main` buildable.
- Add upstream notice for v2.3/imbushuo if driver code is included.

---

## 8. Test plan

### 8.1 Test pyramid

- ~70% unit tests: pure logic, parsers, state machines
- ~20% contract/integration: pipe, HID, registry, installer lifecycle
- ~10% manual/hardware: feel, haptics, gestures, sleep/wake

### 8.2 Unit tests — `OpenTrackpad.Core.Tests`

Gesture FSM:

- Exactly 3 stable contacts start drag
- 1, 2, 4, 5 contacts do not start drag
- Contact ID reuse / churn / padding / ghost contacts
- Stale contact expiry
- Grace period and movement threshold
- Lift any finger -> release
- Mode switch -> release
- Quit -> release
- Failed left-up retries and fail-open cleanup
- Pointer suppression only while drag active
- 4-finger gestures unaffected

Click-pressure FSM:

- Threshold crossing with hysteresis
- Very light press does not click
- Hold past threshold clicks once
- Release fires release feedback
- No double click
- Force-click dwell behavior (if implemented)
- 3-finger drag overrides pressure click
- Host-click off -> firmware click path unaffected

Battery:

- Parse valid/invalid reports
- Charging/full/discharging states
- Low battery 20/10 thresholds and cooldown
- Poll lifecycle: start/stop/repeated start/thread failure
- Failure backoff

Settings:

- Validate ranges
- Round-trip save/load
- Backup/restore Windows touchpad settings
- Pipe reload after apply
- Capability detection

### 8.3 Contract tests — `OpenTrackpad.Interop.Tests`

- Named pipe: each command, status `0x00/0xFF/0xFE`, timeout, reconnect, access denied
- HID feature bytes: `21 01/00`, `22`, `23`, `53`
- Raw Input registration: usage page/usage, `RIDEV_INPUTSINK`, 32/64-bit struct sizes
- SendInput wrappers: injected flag/tag, left-up retry
- Registry: exact keys, value types, permission errors
- Windows settings: API vs registry fallback, restore
- Device detection: VID/PID, PTP usage, USB/BT

### 8.4 Integration tests — `OpenTrackpad.IntegrationTests`

Hardware-gated with `[Trait("Hardware","MT2")]` or env var `OPENTRACKPAD_HW=1`.

- Device detected over USB
- Battery read
- Haptic preview
- Host-click enable/disable and restore
- Pressure reading range
- 3-finger drag end-to-end
- Sleep/wake, plug/unplug
- App kill/crash recovery
- Driver pipe unavailable -> graceful degradation

### 8.5 Protocol tests

Fixtures:

- HID descriptors from `win-hid-dump`/`HidDescriptorProbe`
- Raw PTP frames recorded via `RawInputProbe` and USBPcap
- Battery reports
- Feature/output report bytes

Assertions:

- Required PTP TLC exists (Digitizer/Touch Pad, `0x0D/0x05`)
- Required usages: Contact ID, Tip Switch, Confidence, X, Y, Contact Count, Scan Time
- Optional pressure/size present
- Contact count/ID consistency
- Button bit behavior
- Vendor report IDs and lengths
- No malformed frame crashes; fuzz 1,000 random frames

### 8.6 Installer/lifecycle tests — Pester

- Fresh install
- Same-version no-op (idempotent)
- Upgrade from older version
- Newer version present (do not downgrade silently)
- Multiple matching packages (stop for review)
- Driver Store detection by exact INF/hash/signature, not generic “Apple”
- Signature/hash verification
- Uninstall dry-run
- App-only uninstall keeps driver
- Driver removal with connected-device guard
- Export/backup before delete, no `/force`
- Rollback on failure
- Reboot handling
- PowerShell 5.1 compatibility
- Test-signing setup/cleanup (fallback only)
- Diagnostics redaction (serial/instance IDs)

### 8.7 Manual checklist

- Click feel: light/medium/firm/silent/max
- Click pressure low/medium/high
- 3-finger drag: short/long, fast/slow, edges, while dragging windows
- Scroll smoothness and speed
- 2-finger right click, tap-to-click, pinch zoom
- 4-finger back/forward, task view, show desktop
- Palm rejection, thumb click + index move
- Battery accuracy and alerts
- Sleep/wake, plug/unplug, cold boot, app restart
- No stuck left button after kill/crash

### 8.8 Fault injection

- Kill app during drag -> left up
- Kill app during host-click -> `0x21 00`
- Pipe timeout
- HID unplug mid-gesture
- Registry access denied
- Malformed HID reports
- Battery read failure
- Duplicate app instance
- Driver missing/old

### 8.9 Acceptance criteria

- 1,000 drag cycles no stuck button
- 20 kill/crash tests pass
- 20 sleep/wake, 20 plug/unplug, 10 cold boot
- Battery within ±5%
- Unit coverage of `OpenTrackpad.Core` ≥ 80%
- 100% pipe command coverage
- Installer idempotent and safe
- Manual checklist pass

### 8.10 CI

GitHub Actions on `windows-latest`:

- Build app
- Run xUnit unit/contract tests
- Run Pester static/installer-dry-run tests (no real driver install)
- Optionally build driver with WDK NuGet (unsigned, no deploy)
- Hardware tests are local/manual only

---

## 9. Implementation phases

### Phase 0 — Setup and baseline

- [x] Create `C:\code\open-trackpad`; init git; set remote
- [x] Install v2.3 signed driver on USB (`oem84.inf`)
- [x] Verify Windows sees precision touchpad and gestures work
- [x] Add repo skeleton, README, plan
- [x] Create baseline install/access scripts and verify pipe access
- [ ] Install VS 2026 Community + SDK/WDK
- [x] Implement `RawInputProbe` (caps/raw/setfeature + pipe commands)
- [ ] Build `HidDescriptorProbe`, `LatencyProbe`

Verification: `pnputil`/Device Manager shows PTP device; Windows Touchpad Settings opens; probes see contacts.

### Phase 0.5 — Feasibility spike (1–2 days)

- [x] Dump descriptors for interfaces 0–3
- [x] Confirm feature `0x21` and output `0x53` exist on interface 2 — **result: feature 0x21 absent; output 0x53 present but write times out**
- [x] Try `HidD_SetFeature(0x21, 01/00)`; observe firmware click behavior — **result: not supported**
- [x] Read pressure via Raw Input; verify 0–253 range and per-contact values — **result: 0..97 observed, field is 0..255; contacts/3-finger verified**
- [ ] Enable host-click, inject click via SendInput, fire haptic — **blocked: needs driver fork**
- [x] Implement minimal 3-finger drag via Raw Input + SendInput — implemented in probe and app
- [ ] Verify no double movement / no left-button stuck — feel tested OK; kill/soak pending
- [x] Decide: app-only vs driver fallback — **app-only MVP chosen; driver fork deferred until click pressure is prioritized**

Verification: written spike report with captures/logs; go/no-go.

### Phase 1 — App MVP

- [x] Tray app, single instance, startup
- [x] Device detection and driver status — basic: battery/pipe status, no 2-second live reconnect
- [x] Battery + alerts — poll + 20/10 policy, unit tested
- [x] Haptic/silent UI + preview — preview works; apply uses an elevated helper (UAC)
- [x] Pointer/palm settings + reload
- [x] Scroll speed + natural scrolling + zoom + tap-to-click + two-finger secondary click — 3/4-finger presets still pending
- [x] Unit + contract tests — 49 tests passing
- [ ] Basic installer with signed driver

Verification: daily use on USB; all unit tests pass; battery/haptic/settings work after reboot.

### Phase 2 — Custom features

- [x] 3-finger drag FSM + tests
- [x] Finger suppression + stale contact handling — `Tip=1` filter, stale-frame watchdog
- [x] Left-button fail-open + retry — in-process watchdog + separate drag watchdog process; kill/soak test pending
- [ ] Click pressure FSM + tests (if spike passed) — **blocked by v2.3 user-mode surface**
- [ ] Host-click enable/disable + watchdog/restore — **needs driver fork**
- [ ] Haptic integration for clicks — **needs driver fork**
- [x] Conflict rules with 3-finger drag and Windows gestures — 3-finger slide disabled/restored
- [ ] Integration tests + manual checklist

Verification: acceptance criteria above.

### Phase 3 — Installer and lifecycle

- [x] Inno Setup one-click install
- [x] Driver status/no-op/upgrade/uninstall scripts
- [x] Connected-device guard
- [x] Settings backup/restore — scroll sensitivity and Quiet Click; Windows toggles use the touchpad API directly
- [ ] Pester suite
- [x] Diagnostics/log collection — `app.log`, installer/driver logs, `Test-DriverStatus.ps1`
- [ ] Fallback test-signed package (only if needed)

Verification: fresh VM install/uninstall; no-op install; safe removal; logs clean.

### Phase 4 — Soak and polish

- [ ] 8-hour soak
- [ ] Sleep/wake and plug/unplug cycles
- [ ] Latency measurement vs baseline
- [ ] UI polish, Thai/English decision
- [ ] README/troubleshooting

### Phase 5 — Bluetooth

- [ ] Validate v2.3 BT PTP + battery + haptic
- [ ] Verify Raw Input contact reassembly over BT
- [ ] Port host-click/3-finger logic or adjust
- [ ] BT-specific reconnect/sleep tests
- [ ] Installer pairing guide

---

## 10. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Windows blocks `HidD_SetFeature(0x21)` on interface 2 | Spike first; fallback tiny driver pipe command |
| Raw Input does not expose pressure | Check descriptor; fallback driver or use pressure/size heuristics; gate feature |
| SendInput conflicts with PTP pointer movement | Suppress pointer while drag active; tune thresholds; test on hardware |
| App crash leaves host-click mode on | Startup reset, exit handler, watchdog, panic restore shortcut |
| Left button stuck after crash | Fail-open cleanup, bounded retries, kill tests |
| 3-finger drag conflicts with Windows gestures | Disable 3-finger gestures while active; 4-finger for back/forward; restore |
| v2.3 driver changes/abandonment | Pin package; keep fork option; preserve interfaces |
| Windows Insider build changes | Capability checks, logs, graceful degradation |
| Installer conflicts with old drivers | Exact Driver Store detection, safe removal, no `/force`, backups |
| Unsigned app SmartScreen | Personal use; optional code signing later |
| Test signing required for fallback | Separate dev installer; Secure Boot off; cert cleanup; user accepted |

---

## 11. Open questions

1. ~~Confirm app-only route is acceptable~~ — **resolved: app-only MVP chosen; click pressure deferred to a possible driver fork.**
2. ~~UI language~~ — **resolved: Thai-first UI with English technical terms.**
3. Should the installer bundle the signed v2.3 driver (with GPL attribution) or download it?
4. 3-finger tap behavior when 3-finger drag is enabled: keep middle click or disable?
5. Is experimental click pressure acceptable with a clear fallback toggle? (Deferred with the driver fork.)

---

## 12. References

- https://github.com/imbushuo/mac-precision-touchpad
- https://github.com/vitoplantamura/MagicTrackpad2ForWindows
- https://github.com/JeyKul/magic-trackpad-linux
- https://github.com/austinnixholm/ThreeFingerDrag
- https://github.com/OnlineFix/Pearipherals
- https://github.com/Suki-Kyaru/magic-trackpad-windows
- https://github.com/sneakyjoeru/magic-trackpad-windows
- https://github.com/lc700x/MagicTrackPad2_Windows_Precision_Drivers
- https://github.com/Pro-Qin/MacBook-Touchpad-Visualizer
- https://github.com/microsoft/hidtools
- https://github.com/todbot/win-hid-dump
- https://github.com/gregkh/usbutils/tree/master/usbhid-dump
- https://github.com/usb-tools/python-hid-parser
- https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-validation-guide
- https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-required-hid-descriptors
- https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/touchpad-tuning-guidelines
- https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk

---

## 13. Next steps for the new session

1. Read `plan.md` and check `git status`.
2. Implement `tools/RawInputProbe` with `caps`, `raw`, and `setfeature` modes.
3. Run the Phase 0.5 spike and write `docs/spike-report.md`.
4. Install VS 2026 + WDK (can run in parallel with probe work).
5. Choose app-only or driver fallback, then start Phase 1 with tests first.
