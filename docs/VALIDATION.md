# Validation — Android Mirror 1.1.3

## Automated coverage

`src/build.ps1` builds the app using the Windows .NET Framework compiler and runs 58 connection, feature, and update checks. Coverage includes:

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
- Stable app-release metadata from our repository, pinned release verification, rejection of foreign URLs/previews/missing checksums, version comparisons, old preference migration, and one-day prompt deferral persistence.
- New authorized device hotplug and delayed Android authorization alongside an existing mirror, with explicit deselection preserved.
- Branded displayed logs retain device/error details and use our GitHub URL.
- Publisher release-note summaries remove Markdown, preserve change text, and bound oversized/missing notes; engine consent and app deferrals persist separately. Native self-checks verify both update labels, summaries, and action buttons.

The app's `--self-check` path exercises background startup, tray restore, close-to-tray, single-instance activation, light/dark switching, Settings navigation, embedded SVG artwork, QR rendering, manual pairing screens, Advanced search and reset, rounded checkbox and chevron templates, and modal confirmation acceptance/cancellation. Checks suppress notifications, pairing, mirroring, preference writes, and Windows startup changes.

`src/build-setup.ps1` performs eight installer checks using a workspace fixture: path traversal rejection; full installation with the branded engine and a single app launcher; native shortcut identity, target and icon; shortcut repair and cleanup without removing another installation's link; reinstall preservation of unrelated files; app upgrade preservation of a customized engine and app updater; rollback after a locked file interrupts app replacement; and uninstall removal of owned files while retaining unrelated files. It does not install into Program Files or change Windows installation settings.

`src/test-screen-sharing.ps1` creates an animated SDL window using the bundled SDL3.dll and reads changing pixel samples through the real Windows Graphics Capture and D3D11 APIs. Its fixture models content-driven portrait/landscape resizing and gray letterboxing. Checks cover continuing background frames, repeated minimize, rotations while parked and after restore, preserved client dimensions, activation without fresh input, maximized rotation and unmaximize, setting changes, and cleanup. Capture buffers and readback textures are recreated when the window dimensions change. Background samples continued changing across more than 100 captured frames. Activation checks use controlled input timestamps alongside real native foreground events; physical taskbar/Alt+Tab interaction is not automated by this fixture.

Running with `-Baseline` against the fetched `v1.1.2` source tag reproduces the stale-size restoration failure. The corrected implementation passes the same sequence, including rotation from a parked maximized window. This validates the SDL rendering/capture path, not a fresh OBS recording, Discord call, or mixed-DPI multi-monitor setup. Windows SDK metadata is needed only to compile this optional test.

## Visual and native integration checks

App-owned preview rendering covers dark/light settings, several devices, QR pairing, manual pairing, reconnecting, error states, and tray menus. The logo is rendered from the supplied SVG. The dark tray gutter uses the same charcoal surface as the menu. The OS accepted Desktop acrylic on the Windows 11 validation host. WPF animation properties confirm active Settings transitions and toggle motion with Windows animations enabled.

WPF bitmap previews use opaque backgrounds. Windows draws desktop acrylic outside that renderer, so screenshots do not capture its real desktop blur. Material activation is checked through the native API result.

The real official release API and Windows x64 download were also validated: engine 5.0.1 was downloaded, verified against its published SHA-256, and safely extracted as 16 files in a test staging folder. The installed engine was not replaced during this check. A full native shell self-check passed for the updated controls, Advanced editors, and dialog behavior.

The 1.1.2 notification fix reads back `System.AppUserModel.ID`, the target executable and its icon from real native shortcuts. A live per-user registration check confirms Windows' Start app resolver reports **Android Mirror** for the process identity. The shell self-check exercises all four notification routes (background, device connected, app update, engine update) without delivering OS notifications, checking their distinct titles and details, native information symbol, and exact embedded logo bytes. All four use one delivery function and registered identity. Updater error dialogs retain Android Mirror titles and their appropriate error/information severity; installer status stays within the branded setup window. The desktop capture tool did not expose the transient notification banner during this check, so its final rendered appearance still needs visual confirmation.

## Hardware and release limitations

The bundled ADB event stream initializes successfully. Nearby wireless services may be discovered without being paired or connected. Automated simulations do not verify a real device's USB cable cycle, QR camera scan, TLS pairing, audio output, PC input, or USB/Wi-Fi handover. Those need physical device validation on the user's network.

Windows notification delivery depends on Windows notification/focus settings. App confirmations use a WPF implementation of the Windows 11 content-dialog pattern, including themed surfaces, dimming, focus containment, and safe cancellation. Wireless pairing requires Android 11+ and a network that allows device-to-PC connections; older USB-assisted Wi-Fi uses unencrypted legacy ADB. Devices without a consistent reported hardware serial can appear as separate USB/Wi-Fi entries.

This release is unsigned and distributed through GitHub, not the Microsoft Store. Windows 10 compatibility is an implementation target; the recorded native material checks were performed on Windows 11.
