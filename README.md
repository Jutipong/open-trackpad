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

Status: **Phase 0 — v2.3 signed baseline installed; Raw Input / host-click spike pending.**

Current machine state and next steps are documented at the top of [plan.md](plan.md#0-current-status--handoff-2026-11-16).

See [plan.md](plan.md) for the full implementation plan, architecture, protocol notes, test plan, and risks.

## Target hardware

- Apple Magic Trackpad 2 (Lightning, `VID_05AC&PID_0265`)
- Windows 11 x64
- USB first; Bluetooth later

## Baseline driver

The plan starts with the Microsoft-signed
[vitoplantamura/MagicTrackpad2ForWindows](https://github.com/vitoplantamura/MagicTrackpad2ForWindows)
v2.3 driver. If the user-mode feature spike succeeds, no custom driver is needed.

## License

The app, installer, and tools are intended to be MIT-licensed.
Any driver fork will be GPL-2.0 with upstream attribution.
See the plan for details.
