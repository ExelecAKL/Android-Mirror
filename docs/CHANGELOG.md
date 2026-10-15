# Changelog

## 1.1.1 — 2026-10-15

- Keep sharing when minimized: preserves the same drawable mirror window outside the desktop for modern Windows window capture, with taskbar/Alt+Tab restoration and a Settings toggle enabled by default.
- Daily checks for Android Mirror's own GitHub releases, themed update prompts, release notes, verified downloads, and an unelevated restart after the modern installer completes. Portable copies open the release download page.
- App update / Engine based update labels and readable What’s changed summaries in the popup. Both offer Install update / Remind me later; engine installation requires consent before waiting for active mirrors to stop.
- App upgrades retain the current mirroring engine and roll back app files if replacement fails. Version 1.1.0 users need to install this release once before app-update prompts become available.
- Android Mirror branding and our GitHub repository in displayed connection logs, while preserving error/device details and original licence notices.
- Native notifications use the Android Mirror logo and a dedicated Windows application identity instead of stock information artwork.
- One newly discovered, authorized device opens immediately with automatic connection enabled, including alongside existing mirrors. Explicit Stop and deselection choices are retained.

## 1.1.0 — 2026-10-15

- Searchable Advanced sections for all 109 bundled engine options, including UHID, shortcut modifiers, mouse bindings, recording, camera, virtual displays, gamepads, capability queries, and USB input-only mode.
- Per-device and app defaults, plus unique recording filenames across reconnects.
- Official engine release checks, release notes, verified background downloads, idle installation, owned-file updates, rollback, and protection for locally modified engines.
- Rounded accent checkboxes, bare animated chevrons, solid keyboard focus indicators, unboxed navigation icons, and a section picker beside a search field with an icon and placeholder.
- Theme-aware Windows 11 style confirmation dialogs with a dimmed backdrop, clear actions, and keyboard cancellation.
- Manage devices in Settings removes a device's saved profile and stops its mirror while preserving other sessions.
- Disabled Advanced overrides visibly dim their editors; empty editable pickers show Default. Background clicks clear focus highlights, and expanded descriptions use readable paragraphs and documentation links.
- Redesigned installer and uninstaller with larger app artwork, Windows light/dark themes, clear progress, a separate footer, and rounded controls.
- One public Android Mirror.exe launcher. The runtime is Android Mirror Engine.exe with the app icon; verified engine updates retain both automatically.

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
