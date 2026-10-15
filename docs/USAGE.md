# Android Mirror user guide

## USB and reconnecting

Enable Developer options and USB debugging on each device. Connect using a data-capable USB cable and accept the authorization prompt on the device. Select one or several devices in the launcher.

With Connect automatically enabled, selected devices resume when they reconnect. Closing a mirror or choosing Stop pauses that session. Mirror resumes it. Stop all cancels active and pending mirrors; it does not disable background device detection.

## Wireless setup

Wireless debugging requires Android 11 or later and a shared Wi-Fi network. In Settings, change Connection from USB only to Wireless only or Adaptive. A theme-aware confirmation explains that wireless may add delay and USB offers the lowest latency. Choose Enable wireless to continue or Keep USB to retain the previous mode. Escape cancels the dialog.

Choose Set up wireless. For QR pairing, open Wireless debugging on the device and choose Pair device with QR code. Scan the code inside Android Mirror. Codes expire after three minutes; New QR code creates fresh credentials. Only the pairing service matching that QR code is accepted.

For manual pairing, choose Pairing code in the app and Pair device with pairing code on the device. Enter its IP address, pairing port, and six-digit code. Pairing codes are temporary. If a code expires or pairing fails, open a new code on the device and retry.

After pairing, local network discovery finds the separate connection port. If discovery is blocked, use the IP address and connection port shown on the main Wireless debugging page and select Connect. The connection port and pairing port are different. QR and manual pairing both keep ADB windows hidden.

Adaptive mode identifies USB and Wi-Fi transports using the device's reported hardware serial number, prefers USB, and falls back to a connected wireless transport. Only the affected mirror reopens during handover. If a device does not expose a consistent serial number, its transports may appear separately.

Wireless debugging can turn off when the device changes networks. Re-enable it and confirm that the PC and device are on the same Wi-Fi. Guest networks, corporate networks, VPNs, or firewalls can block discovery or peer connections. Manual pairing bypasses the discovery requirement for entering the pairing endpoint; it cannot bypass a network that blocks the connection itself. To unpair, use Forget under Paired devices on the device.

## Per-device audio and controls

Settings → For this device selects the profile to edit. Play audio on Android Mirror sends supported device audio to the PC. Turn it off to leave audio on the device. Control from this PC enables mouse and keyboard input; turn it off for view only with clipboard synchronization disabled. Applying changes briefly reopens that device's mirror. Other mirrors continue running.

Audio forwarding requires Android 11 or later. On Android 11, unlock the screen before starting audio capture. Some apps and protected content block capture.

## Advanced settings

Open Advanced mirroring options in Settings. Choose a device or Defaults for new devices. Use the section picker and search field together. Every long option reported by the installed engine is represented; the bundled 5.0.1 engine has 109. Device selection and wireless connection options are mapped to the shell's existing controls. Capability-listing options run as hidden queries and display results in Device capabilities.

Enable Override default to edit an option. Select Apply to save it; only the affected active session reopens. For UHID keyboard input, choose Keyboard, enable its override, and select uhid. The shortcut modifier and mouse bindings can be changed, with a full fixed-shortcut reference in Shortcuts. Individual shortcut actions cannot be assigned arbitrary new keys through the upstream engine.

Reset clears the current Advanced draft. Apply saves that reset. The device reset in Settings applies immediately; Restore app and device defaults confirms before resetting everything. Pairing keys are retained. OTG input-only mode uses USB without capturing video or audio; it can work without USB debugging if device driver support allows it. V4L2 output is Linux-only and unavailable on this Windows build.

Recordings use your selected base filename plus a timestamp and unique suffix for every new session. Relative recording paths go under Videos/Android Mirror. Ensure the chosen encoder, codec, output format, and Android capabilities are compatible.

## Engine updates

Android Mirror app releases have a separate section in Settings. Daily app checks are on by default. A newer stable release shows a themed update prompt; Later postpones it for one day. Installed copies verify the installer, ask for Windows elevation, update while retaining preferences and the current engine, then restart without administrator privileges. Portable copies open the release download page. Version 1.1.0 needs a one-time manual installation of 1.1.1 before these checks are available.

Select Off, Check only, or Automatic in Settings. App update and Engine based update popups show What’s changed and Install update / Remind me later. Automatic engine mode checks daily and can download verified releases while idle. Installation requires approval for that version and waits for mirrors to stop. Remind me later postpones the prompt for one day. Full release notes remain in Settings. Windows may request elevation for Program Files.

App settings and recordings are retained. Changed, added, and removed engine files are applied with backup and rollback. Locally modified engine files block automatic binary replacement. See [update behavior](ENGINE-UPDATES.md).

## Tray, startup, and appearance

Keep sharing when minimized is on by default. Select the mirror window in OBS or your calling app. Minimize parks that same drawable window outside the desktop, keeping modern Windows window capture alive. Its taskbar icon and Alt+Tab restore its position and maximized state. Closing the window still stops that mirror. Turning this option off restores any parked windows and permits ordinary minimization.

Rotating the phone while the mirror is in the background retains the engine's new portrait/landscape size when restored. Activation events from the minimize transition are ignored; typing or clicking in another app later does not reopen the mirror. A new taskbar/Alt+Tab activation restores it, and accessibility tools can explicitly activate it after the transition. Maximized mirrors return maximized and keep the latest rotated windowed size for a later unmaximize. Letterboxing caused by a deliberately fixed/maximized window or a different video aspect ratio can still be normal. In OBS, the scene canvas and source transform are separate from the mirror's window size.

OBS Window Capture should use Automatic or its modern Windows 10 (1903 and up) / WGC method. Legacy BitBlt and whole-display capture are not supported by background parking. The bundled renderer was validated through Windows Graphics Capture; actual Discord call behavior still needs verification. Screen sharing continues to consume rendering resources in the background.

Settings → Manage devices lists connected and saved devices. Remove stops that device's mirror and deletes its saved options and remembered app connection metadata. Other mirrors continue. A currently connected device stays hidden until it disconnects and reconnects, or until the app restarts, then returns with default options. To revoke wireless trust on Android itself, also forget this PC in the device's Wireless debugging settings.

Closing the launcher keeps it in the Windows notification area. Its native tray menu provides Open, Mirror selected / Stop all, Settings, and Quit. Connection notifications use Windows notification delivery, so Windows notification and focus settings determine whether they appear.

Start with Windows is optional and starts the app quietly in the tray. Theme can follow Windows or use Light or Dark. Acrylic is used where the OS and its transparency settings support it; other configurations use an opaque surface. Animations respect the Windows animation setting and high contrast mode.

## Installation and data

The installer writes to Program Files and requires elevation. Normal app use does not require administrator rights. Use Windows Settings → Apps to uninstall. Preferences stay in `%LOCALAPPDATA%\Android Mirror\settings.xml` and are retained on uninstall. ADB maintains pairing keys through its normal per-user key store. The app never saves pairing codes or QR secrets.

Portable use requires keeping the entire engine folder beside Android Mirror.exe. Android Mirror.exe is the only public app launcher. The engine/Android Mirror Engine.exe entry redirects to the UI. The shell runs Android Mirror Runtime.bin directly, without a terminal; updates retain the visible entry and its app icon. Connection help contains bounded runtime diagnostics.
