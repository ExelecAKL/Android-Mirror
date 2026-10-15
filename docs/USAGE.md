# Android Mirror user guide

## USB and reconnecting

Enable Developer options and USB debugging on each device. Connect using a data-capable USB cable and accept the authorization prompt on the device. Select one or several devices in the launcher.

With Connect automatically enabled, selected devices resume when they reconnect. Closing a mirror or choosing Stop pauses that session. Mirror resumes it. Stop all cancels active and pending mirrors; it does not disable background device detection.

## Wireless setup

Wireless debugging requires Android 11 or later and a shared Wi-Fi network. In Settings, change Connection from USB only to Wireless only or Adaptive. Windows displays a native confirmation explaining that wireless may add delay and USB offers the lowest latency. Choosing No preserves the previous mode.

Choose Set up wireless. For QR pairing, open Wireless debugging on the device and choose Pair device with QR code. Scan the code inside Android Mirror. Codes expire after three minutes; New QR code creates fresh credentials. Only the pairing service matching that QR code is accepted.

For manual pairing, choose Pairing code in the app and Pair device with pairing code on the device. Enter its IP address, pairing port, and six-digit code. Pairing codes are temporary. If a code expires or pairing fails, open a new code on the device and retry.

After pairing, local network discovery finds the separate connection port. If discovery is blocked, use the IP address and connection port shown on the main Wireless debugging page and select Connect. The connection port and pairing port are different. QR and manual pairing both keep ADB windows hidden.

Adaptive mode identifies USB and Wi-Fi transports using the device's reported hardware serial number, prefers USB, and falls back to a connected wireless transport. Only the affected mirror reopens during handover. If a device does not expose a consistent serial number, its transports may appear separately.

Wireless debugging can turn off when the device changes networks. Re-enable it and confirm that the PC and device are on the same Wi-Fi. Guest networks, corporate networks, VPNs, or firewalls can block discovery or peer connections. Manual pairing bypasses the discovery requirement for entering the pairing endpoint; it cannot bypass a network that blocks the connection itself. To unpair, use Forget under Paired devices on the device.

## Per-device audio and controls

Settings → For this device selects the profile to edit. Play audio on Android Mirror sends supported device audio to the PC. Turn it off to leave audio on the device. Control from this PC enables mouse and keyboard input; turn it off for view only with clipboard synchronization disabled. Applying changes briefly reopens that device's mirror. Other mirrors continue running.

Audio forwarding requires Android 11 or later. On Android 11, unlock the screen before starting audio capture. Some apps and protected content block capture.

## Tray, startup, and appearance

Closing the launcher keeps it in the Windows notification area. Its native tray menu provides Open, Mirror selected / Stop all, Settings, and Quit. Connection notifications use Windows notification delivery, so Windows notification and focus settings determine whether they appear.

Start with Windows is optional and starts the app quietly in the tray. Theme can follow Windows or use Light or Dark. Acrylic is used where the OS and its transparency settings support it; other configurations use an opaque surface. Animations respect the Windows animation setting and high contrast mode.

## Installation and data

The installer writes to Program Files and requires elevation. Normal app use does not require administrator rights. Use Windows Settings → Apps to uninstall. Preferences stay in `%LOCALAPPDATA%\Android Mirror\settings.xml` and are retained on uninstall. ADB maintains pairing keys through its normal per-user key store. The app never saves pairing codes or QR secrets.

Portable use requires keeping the entire engine folder beside Android Mirror.exe. Root compatibility launchers open the UI; the original CLI engine is inside engine. Connection help contains bounded runtime diagnostics.
