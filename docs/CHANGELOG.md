# Changelog

## 1.1.4 — 2026-10-15

- Fix a remaining minimize race found during physical phone testing: unrelated later PC input no longer restores a mirror whose foreground state was left behind by the minimize transition.
- Handle a taskbar click that Windows reports as another minimize. Wait for that transition before restoring, keeping the last drawable portrait/landscape size.
- Ignore temporary minimized caption-strip bounds instead of caching them as the mirror's video dimensions.
- Extend the isolated SDL/Windows Graphics Capture checks with stale foreground plus later input, taskbar toggling, and retained client dimensions. Use atomic fixture commands to avoid test-only file-sharing races.
- Load the ZIP assembly explicitly in packaging scripts so the documented Windows PowerShell build commands work in a fresh session.
- Physically verify background capture and landscape/portrait resizing on a connected phone. Restore and maximized round trips remain covered by the isolated fixture; a new OBS/Discord session remains unverified.
- Publish as a newer app version so existing installations receive the normal update prompt, retaining settings and the current engine.

## 1.1.3 — 2026-10-15

- Keep the engine's latest window dimensions when restoring a mirror after rotation, preventing stale portrait/landscape sizing and the resulting large gray letterbox areas.
- Move the background window without resizing or repeatedly rebuilding its frame. Prefer parking near the original monitor to reduce DPI changes.
- Ignore foreground changes without new PC input after minimizing, preventing a delayed activation from reopening the mirror. Repeated minimize requests reset this guard.
- Normalize a maximized mirror during background capture so rotation can resize it; restore the maximized state and retain the rotated size for a later unmaximize.
- Extend the live SDL/Windows Graphics Capture regression with repeated rotations, changing capture dimensions, activation without fresh input, and maximized rotation. The released 1.1.2 code fails the new size-restoration check; the corrected implementation passes.

## 1.1.2 — 2026-10-15

- Repair Windows Start menu shortcut identity so notification headers resolve Android Mirror and its logo instead of the internal app ID.
- Restore Windows' blue information symbol for background, device-connected, app-update, and engine-update notifications through their shared delivery path.
- Repair older per-user shortcuts automatically, set matching metadata on installer shortcuts, and retain other installations' shortcuts during cleanup.
- Add checks for all four notification routes, embedded logo bytes, native shortcut metadata, repair, and ownership-aware cleanup.
- Publish as a newer app version so existing 1.1.1 installations can offer Install update / Remind me later on their next enabled automatic check.

## 1.1.1 — 2026-10-15

- Keep sharing when minimized: preserves the same drawable mirror window outside the desktop for modern Windows window capture, with taskbar/Alt+Tab restoration and a Settings toggle enabled by default.
- Daily checks for Android Mirror's own GitHub releases, themed update prompts, release notes, verified downloads, and an unelevated restart after the modern installer completes. Portable copies open the release download page.
- App update / Engine based update labels and readable What’s changed summaries in the popup. Both offer Install update / Remind me later; engine installation requires consent before waiting for active mirrors to stop.
- App upgrades retain the current mirroring engine and roll back app files if replacement fails. Version 1.1.0 users need to install this release once before app-update prompts become available.
- Android Mirror branding and our GitHub repository in displayed connection logs, while preserving error/device details and original licence notices.
- Initial Windows notification identity metadata; its missing shortcut registration is corrected in 1.1.2.
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
