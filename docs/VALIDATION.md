# Validation — Android Mirror 1.1.0

## Automated coverage

`src/build.ps1` builds the app using the Windows .NET Framework compiler and runs 51 connection, feature, and update checks. Coverage includes:

- ADB snapshot parsing, authorization/offline handling, fragmented TCP frames, and server reconnection.
- Cable loss and rapid reattachment without overlapping child processes.
- Stop, manual window close, bounded retries, process startup errors, and shutdown cleanup.
- Simultaneous real test child processes and independent per-device audio/control flags.
- Applying settings to one device without interrupting another, including rapid changes during disconnection.
- Saved profiles and legacy settings migration.
- USB-only defaults, transport filtering, physical-device deduplication, and serialized Adaptive handover.
- QR format, unique random credentials, exact service matching, cancellation, endpoint/code validation, and pairing secrets passed through stdin.
- Separate wireless pairing/connection ports, identity caching, and hidden ADB process configuration.
- Real redirected stdin, timed-out/cancelled command cleanup, and saved connection-mode metadata without secrets.
- Removing a saved device without interrupting another, suppressing rediscovery until disconnection, and restoring clean defaults on reconnection.
- All 109 engine options, optional/required values, managed flags, per-device copies, recording filename uniqueness, and OTG transport selection.
- Trusted release metadata, checksum failures, file additions/changes/removals, preservation of unrelated files, rollback, archive path validation, and blocking replacement of locally modified engines.
- Upstream executable renaming and icon reapplication, matching native icon pixels, unchanged executable code sections, and retained branded-update provenance.

The app's `--self-check` path exercises background startup, tray restore, close-to-tray, single-instance activation, light/dark switching, Settings navigation, embedded SVG artwork, QR rendering, manual pairing screens, Advanced search and reset, rounded checkbox and chevron templates, and modal confirmation acceptance/cancellation. Checks suppress notifications, pairing, mirroring, preference writes, and Windows startup changes.

`src/build-setup.ps1` performs five installer checks using a workspace fixture: path traversal rejection; full installation with the branded engine and a single app launcher; native shortcut creation; reinstall preservation of unrelated files; and uninstall removal of owned files while retaining unrelated files. It does not install into Program Files or change Windows installation settings.

## Visual and native integration checks

App-owned preview rendering covers dark/light settings, several devices, QR pairing, manual pairing, reconnecting, error states, and tray menus. The logo is rendered from the supplied SVG. The dark tray gutter uses the same charcoal surface as the menu. The OS accepted Desktop acrylic on the Windows 11 validation host. WPF animation properties confirm active Settings transitions and toggle motion with Windows animations enabled.

WPF bitmap previews use opaque backgrounds. Windows draws desktop acrylic outside that renderer, so screenshots do not capture its real desktop blur. Material activation is checked through the native API result.

The real official release API and Windows x64 download were also validated: engine 5.0.1 was downloaded, verified against its published SHA-256, and safely extracted as 16 files in a test staging folder. The installed engine was not replaced during this check. A full native shell self-check passed for the updated controls, Advanced editors, and dialog behavior.

## Hardware and release limitations

The bundled ADB event stream initializes successfully. Nearby wireless services may be discovered without being paired or connected. Automated simulations do not verify a real device's USB cable cycle, QR camera scan, TLS pairing, audio output, PC input, or USB/Wi-Fi handover. Those need physical device validation on the user's network.

Windows notification delivery depends on Windows notification/focus settings. App confirmations use a WPF implementation of the Windows 11 content-dialog pattern, including themed surfaces, dimming, focus containment, and safe cancellation. Wireless pairing requires Android 11+ and a network that allows device-to-PC connections; older USB-assisted Wi-Fi uses unencrypted legacy ADB. Devices without a consistent reported hardware serial can appear as separate USB/Wi-Fi entries.

This release is unsigned and distributed through GitHub, not the Microsoft Store. Windows 10 compatibility is an implementation target; the recorded native material checks were performed on Windows 11.
