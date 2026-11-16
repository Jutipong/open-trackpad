# Phase 0.5 Spike Report

Date: 2026-11-16
Status: protocol findings complete; raw input captured; 3-finger drag prototype implemented but not yet feel-tested

## Summary

| Item | Result |
|---|---|
| `RawInputProbe` implementation | Done (`caps`, `raw`, `drag`, `setfeature`, `getfeature`, `output`, `battery`, `haptic`, `reload`, `release`, `rawlist`, `descriptor`) |
| HID descriptors for MI_00 / MI_02 / MI_03 | Captured via HidSharp; see below |
| PTP descriptor (MI_01) | Not readable through HidSharp; the UMDF PTP collection is hidden from HID enumeration. Known layout from the v2.3 source is used by the probe. |
| Feature report `0x21` (host-click) | **Not exposed**. `SetFeature(0x21, 01)` fails with "SetFeature failed" because report `0x21` is not declared. |
| Feature reports `0x22` / `0x23` | **Not exposed** on MI_02. |
| Output report `0x53` | Declared on MI_02 (63-byte payload), but a user-mode HID write times out. Not usable as a haptic path. |
| v2.3 control pipe | Commands `0x00` reload, `0x01` battery, `0x02` actuate only. No host-click command. |
| App-only click pressure | **Blocked**. v2.3 sends click/release/haptic USB control transfers from the kernel driver; no user-mode surface exists. Driver fork required for this feature. |
| Raw Input contacts | **Verified**. Report ID `0x05`, 112 bytes, ~90 Hz, up to 5 contact slots. |
| Raw pressure | **Verified** in user mode. Observed 0..97 with a comfortable hard press; the field is 0..255. |
| 3-finger drag | Prototype implemented (`RawInputProbe drag`); needs a feel test and stuck-button soak. |

## Descriptor findings

### MI_00 — Device Management

Two vendor collections:

- `0xFF00:0x000B`, input report `0xE0` (4 bytes)
- `0xFF00:0x0014`, input report `0x90` (2 bytes)

### MI_02 — Actuator

```
06 00 FF              Usage Page (Vendor 0xFF00)
09 0D                 Usage 0x0D
A1 01                 Collection (Application)
  85 3F               Report ID 0x3F
  09 0D               Usage 0x0D
  15 00 25 FF 35 00 45 00 55 00 65 00
  75 08 95 0F         Report Size 8, Count 15
  81 02               Input (Data,Var,Abs)
  85 53               Report ID 0x53
  09 0D
  95 3F               Report Count 63
  91 02               Output (Data,Var,Abs)
C0
```

There are **no Feature main items** in this descriptor. `feature=0` is reported by the HID stack.

### MI_03 — Accelerometer

Vendor collection `0xFF00:0x0003`, input report `0xC0` (107 bytes).

### MI_01 — PTP touchpad

HidSharp does not enumerate this UMDF collection. The probe uses the static descriptor from
`MagicTrackpad2ForWindows/AmtPtpDeviceUsbUm/include/DeviceFamily/WellspringMt2.h`:

- Report ID `0x05`
- 5 finger collections × 21 bytes
  - flags byte: bit 0 Confidence, bit 1 Tip Switch, 6 pad bits
  - Contact ID (4 bytes), X (2), Y (2), Width (2), Height (2), Tip Pressure (2), 6 vendor bytes
- Scan Time (2), Contact Count (1), Mechanical Force (2), Button (1)
- Total payload 111 bytes + report ID = **112 bytes**

Expected pressure range is 0..255 from the Apple pressure byte.

## Raw input result

Captured with `RawInputProbe raw` on 2026-11-16 while the device was healthy:

```
frames=749
contact counts: 1:401, 2:288, 3:26, 4:34
max pressure=97 (comfortable press); max mechanical force=97
report rate: ~90 Hz (scan time delta ~110 per frame, ~11 ms)
```

Three-finger frames had consistent contact IDs and `tip=1`, `conf=1`, `state=4`.
The fourth contact in some frames was a palm/ghost shape: `tip=0`, `conf=1`, `p=0`.
This confirms the drag FSM must require exactly three `Tip=1` contacts, not merely
three contacts present in the report.

Captured fixtures:

- `tests/fixtures/raw-input/mt2-usb-raw-2026-11-16.jsonl`
- `tests/fixtures/raw-input/mt2-usb-pressure-3finger-2026-11-16.jsonl`

## Why app-only click pressure is blocked

`AmtPtpDeviceUsbUm/Device.c` sends click/release/haptic data as raw USB control transfers:

- `0x22` / `0x23` feature-like setup packets to interface 2
- `0x53` output report to interface 2

`ControlPanel.c` exposes only the named pipe commands:

```
0x00  reload settings
0x01  get battery
0x02  actuate once (optional 3 strength bytes)
```

There is no `0x21` host-click command and no pipe command that enters host-click mode.
`HidD_SetFeature(0x21, 01)` cannot work because MI_02's descriptor does not declare a
feature report with ID `0x21`. A user-mode HID output write to `0x53` also timed out.

Conclusion: the app-only architecture can deliver 3-finger drag, battery, haptics,
pointer settings, and Windows gesture settings, but **not adjustable click pressure**.
That feature needs the driver-fork path (or a small filter driver) to expose host-click
mode to user mode.

## Hardware incident during the spike

During the first raw-input attempts, the Magic Trackpad stopped producing pointer input.
Windows reported the device as present with `ProblemCode=0`, but:

- `GetRawInputDeviceList` no longer contained `MI_01&Col01`
- the control pipe `Connect` timed out
- pressing the trackpad still produced haptic feedback, so the firmware and battery were alive

A second issue was found: one AMD USB 3.10 host controller was in **Error / Problem Code 43**.
Disabling and re-enabling that controller restored it to `OK`. The trackpad later recovered
after a USB power cycle and a known data-capable cable/port. Raw input capture then worked.

Treat the `0x53` HID output write as unsafe on this device: it timed out and is not a viable
haptic path. Use the control pipe instead.

## Next steps

1. Feel-test `RawInputProbe drag` with three fingers; tune gain/threshold.
2. Run stuck-button tests: lift, Ctrl+C, `release`, process kill, device unplug.
3. Decide: ship app-only without click pressure, or fork the driver for host-click.
4. Start Phase 1 app MVP (battery, haptics, pointer settings, scroll/gestures).

