# Changelog

## 1.1.0 — 2026-10-15

- Android Mirror logo throughout the app, tray, Explorer launchers, and installer.
- Multiple simultaneous device mirrors with independent audio and PC control settings.
- One Settings panel for device choices, connection mode, themes, reconnecting, and Windows startup.
- USB only, Wireless only, and Adaptive modes; USB is the default.
- In-app QR pairing and manual IP, port, and six-digit-code pairing for Android wireless debugging.
- Native Windows confirmation before enabling wireless, explaining its latency tradeoff.
- Adaptive preference for USB and handover to an available paired wireless connection without duplicate mirrors for a recognized physical device.
- Hidden ADB commands, persistent tray detection, and automatic reconnect for selected devices.
- Windows light/dark themes, acrylic where supported, reduced-motion handling, and a dark tray menu without a white gutter.
- Program Files installer and portable release, separated source/runtime/assets folders, and a GitHub-ready README.

Physical QR pairing, device audio routing, and real USB/Wi-Fi handover still need hardware validation. Automated tests use simulated ADB services and real test child processes. See [validation](VALIDATION.md).
