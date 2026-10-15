<div align="center">
  <img src="assets/Android%20Mirror%20logo.svg" alt="Android Mirror logo" width="132" />
  <h1>Android Mirror</h1>
  <p>Your Android™ devices, together on Windows.</p>
  <p>A simple native desktop app for mirroring, automatic reconnection, and wireless pairing.</p>
  <p>
    <a href="https://github.com/ExelecAKL/Android-Mirror/releases/latest">Download</a> ·
    <a href="#getting-started">Getting started</a> ·
    <a href="docs/USAGE.md">User guide</a> ·
    <a href="docs/CHANGELOG.md">What's new</a>
  </p>
  <p><strong>Windows 10 / 11</strong> &nbsp;·&nbsp; <strong>64-bit</strong> &nbsp;·&nbsp; <strong>Native WPF</strong></p>
</div>

---

<div align="center">
  <img src="assets/screenshots/devices-dark.png" alt="Android Mirror showing a tablet and phone with independent settings" width="360" />
  <img src="assets/screenshots/wireless-dark.png" alt="Wireless debugging QR pairing inside Android Mirror" width="360" />
</div>

<p align="center"><sub>Actual app screens with demonstration devices. Glass effects follow Windows transparency settings.</sub></p>

## Made for everyday mirroring

| | What you get |
| :-- | :-- |
| **Reconnect automatically** | Unplug a selected device and plug it back in. Its mirror returns when the connection is ready. |
| **One device or several** | Mirror your phone and tablet at the same time, each in its own window. |
| **Audio where you want it** | Send audio to your PC, or leave it playing on the device. |
| **Control is optional** | Use your mouse and keyboard, or switch a device to view only. |
| **USB and Wi-Fi** | Choose USB only, Wireless only, or Adaptive. Pair with a QR code or a six-digit code inside the app. |
| **Quiet in the tray** | Close the launcher to keep detection running. Restore it from the native Windows tray menu. |
| **Keep sharing in the background** | Minimize the mirror while its picture stays live for modern Windows window capture. |
| **A familiar Windows feel** | System, light, and dark themes; acrylic where supported; subtle motion; your Android Mirror logo throughout. |
| **Advanced when you need it** | Search all 109 engine options, including UHID, keyboard modifiers, mouse bindings, recording, camera, virtual displays, and gamepads. |
| **Engine updates built in** | Check official releases, read changes, and install verified updates while idle. Local engine modifications pause binary updates. |
| **Android Mirror updates** | Daily checks for our app releases, a themed update prompt, verified installer downloads, and restart after updating. |

All options live behind one **Settings** icon. Audio and control choices are saved separately for every device.

**Settings → Manage devices** removes a saved device and stops its mirror without interrupting others. Reconnect it to add it again with defaults. Android wireless pairing trust is removed separately on the device.

## Getting started

### Install

1. Download **Android-Mirror-1.1.1-Setup.exe** from [Releases](https://github.com/ExelecAKL/Android-Mirror/releases/latest).
2. Run the installer and approve the Windows elevation prompt.
3. Open **Android Mirror** from Start.

The installer uses `C:\Program Files\Android Mirror`, adds a Start menu shortcut, and registers an uninstaller in Windows Settings. Desktop shortcuts are optional. This release is distributed directly through GitHub; it is not a Microsoft Store listing.

<p align="center"><img src="assets/screenshots/installer-dark.png" alt="Android Mirror's modern dark installer with app icon, installation location and rounded controls" width="520" /></p>

For a portable copy, extract **Android-Mirror-1.1.1-Windows-x64.zip** and open `Android Mirror.exe`. Keep its `engine` folder beside it. The release executables are currently unsigned.

**Already using 1.1.0?** Install 1.1.1 once from Releases. Version 1.1.0 cannot show app-update prompts; automatic app-release checks start with 1.1.1. Your preferences and installed engine are retained on upgrade.

### Connect with USB

1. Enable **Developer options → USB debugging** on your Android device.
2. Connect it using a USB cable that supports data, then accept the debugging prompt on the device.
3. Select the devices you want to mirror.

USB is the default. With **Connect automatically** enabled, the first single device connects automatically and selected devices reconnect when they return. When several devices appear for the first time, choose which ones to mirror.

Plugging in one newly discovered device also opens its mirror alongside existing mirrors as soon as Android authorizes it. An explicitly stopped or deselected device remains paused.

### Connect over Wi-Fi

1. Put your PC and Android 11+ device on the same Wi-Fi network.
2. Open **Settings → Connection** and choose **Wireless only** or **Adaptive**.
3. Read the Windows 11 style latency notice, then choose **Enable wireless** to continue.
4. Choose **Set up wireless**. On your device, open **Developer options → Wireless debugging**.
5. Choose **Pair device with QR code** on the device and scan the app's code, or use the app's **Pairing code** tab.

Manual pairing accepts the device IP address, pairing port, and six-digit code. If automatic discovery cannot find the connection afterward, enter the separate **connection port** from the main Wireless debugging screen and choose **Connect**. ADB commands run in the background without opening a terminal.

| Mode | Behavior |
| :-- | :-- |
| **USB only** | Mirrors USB devices and skips wireless pairing and discovery. |
| **Wireless only** | Mirrors devices connected over Wi-Fi. |
| **Adaptive** | Prefers USB for the same physical device and switches to an available paired Wi-Fi connection when USB is lost. |

Wireless may add delay or stutter. Use USB for the lowest latency. Adaptive handover briefly reopens the affected mirror; it is not a continuous video stream across transports.

## Your devices, your settings

Open **Settings**, choose a device under **For this device**, and adjust:

| Example | Play audio on Android Mirror | Control from this PC |
| :-- | :--: | :--: |
| Phone: listen on the PC, view only | On | Off |
| Tablet: keep its audio, use mouse and keyboard | Off | On |

Changing audio or controls briefly reopens only that device's active mirror. Audio forwarding requires Android 11 or later; protected content and device restrictions can limit capture. Turning PC control off also disables clipboard synchronization for that mirror.

Closing a mirror pauses that device. Closing the launcher keeps it in the tray. Choose **Quit Android Mirror** from the tray menu to stop the app and its mirrors.

### Share through OBS or Discord

Choose the **mirror window** as the sharing source. **Settings → Keep sharing when minimized** is on by default. Minimize moves that same drawable window outside the desktop so capture can continue; clicking its taskbar icon or using Alt+Tab restores it. Closing the mirror still stops it.

In OBS Window Capture, use **Automatic** or the modern **Windows 10 (1903 and up) / WGC** capture method. Legacy BitBlt and whole-display capture are not supported by this background behavior. Live Windows capture was tested with the bundled renderer; an actual Discord call still needs end-to-end verification. Background sharing continues rendering and uses GPU resources.

<details>
<summary><strong>Settings and appearance</strong></summary>

<p align="center">
  <img src="assets/screenshots/settings-dark.png" alt="Connection and per-device controls in dark mode" width="340" />
  <img src="assets/screenshots/settings-light.png" alt="The same settings in light mode" width="340" />
</p>

The app follows Windows light/dark mode by default. Theme overrides, automatic reconnection, and optional **Start with Windows** are in Settings. Motion and transparency respect Windows accessibility preferences. Preferences are stored per user in `%LOCALAPPDATA%\Android Mirror\settings.xml`; pairing codes and QR secrets are never stored there.

</details>

## Advanced options and defaults

Open **Settings → Advanced mirroring options**, choose a device or **Defaults for new devices**, then pick a section on the left or search on the right. Enable **Override default**, choose a value, and select **Apply**. For example, **Keyboard → Keyboard → uhid** enables USB HID keyboard emulation (`--keyboard=uhid`) for that device.

Keyboard shortcut modifiers and mouse bindings are editable. The complete shortcut, gesture, and drag-and-drop reference is included; arbitrary remapping of individual engine shortcuts is not supported by the bundled engine. Device capability buttons list available apps, cameras, displays, and encoders. The Windows build does not support Linux V4L2 output.

**Reset** clears the Advanced draft; select **Apply** to save it. Settings also offers **Reset this device to defaults** and **Restore app and device defaults**. Recording sessions get unique filenames so reconnecting or mirroring several devices does not overwrite previous recordings.

<p align="center"><img src="assets/screenshots/advanced-dark.png" alt="Advanced options with a section picker, search field, and per-device controls" width="340" /> <img src="assets/screenshots/dialog-dark.png" alt="Themed wireless confirmation dialog" width="340" /></p>

## Updates without losing your setup

**Android Mirror updates** checks this project's GitHub releases daily and asks before installing. Installed copies download a size- and SHA-256-verified installer, request Windows administrator approval, stop their mirrors, update, and restart at the original user's privilege level. **Remind me later** defers the prompt for a day. Release notes and a manual check are in Settings. Portable copies get the same checks and a link to download the new version.

<p align="center"><img src="assets/screenshots/app-update-dark.png" alt="Android Mirror's themed Install update and Remind me later popup, with a demonstration version" width="380" /></p>

App upgrades keep the current engine, including newer or custom builds, and attempt to restore previous app files if replacement fails. Version 1.1.0 needs the one-time manual update described above.

Settings offers **Off**, **Check only**, or **Automatic** engine updates. Both app and engine update popups show **What’s changed**, with an **App update** or **Engine based update** label. **Remind me later** defers that version for a day. Engine Automatic mode checks daily and can download a verified package while mirroring is idle, but installation requires **Install update** approval first. Approved engine updates wait for active mirrors to stop. Program Files installations may require Windows administrator approval.

The updater replaces changed engine files, adds new files, and removes obsolete owned files. It preserves the UI, preferences, recordings, unrelated files, and unchanged engine files. Failed installations attempt rollback from a backup. Packages are full downloads; file application is incremental.

Engine code is currently upstream code; the Windows executable name and icon resources are branded for Android Mirror. Updates reapply the name and icon automatically. If engine files are locally changed, binary updates pause to preserve those changes. A future customized engine must be merged and rebuilt by maintainers, then distributed as a tested compatible release. End users do not merge source code. See [engine updates](docs/ENGINE-UPDATES.md).

## Build from source

Requires Windows x64 and .NET Framework 4.8. The build uses the compiler included with Windows; Node.js and Visual Studio are not required.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\build-setup.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\fetch-runtime-sources.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\build-release.ps1
```

The first command builds the app and runs the connection tests. The second builds and validates the installer. The third downloads and verifies matching upstream runtime sources. The last creates the portable ZIP, versioned installer, runtime source bundle, release notes, and checksums in `releases/1.1.1`. The optional `src/test-screen-sharing.ps1` live capture test requires Windows SDK metadata and an interactive Windows session.

```text
Android Mirror.exe   Desktop app
engine/              Bundled mirroring and device tools
tools/               Generated engine update helper
assets/              Vector logo and README screenshots
src/                 Native app, installer, build scripts, and tests
vendor/              Pinned QR code dependency
docs/                Usage, design, changelog, and validation notes
licenses/            Original third-party licence texts
installer/           Generated installer (local build)
build/               Generated tools and checks (local only)
releases/            Versioned release packages (local only)
archive/             Preserved files from the old layout (local only)
```

`Android Mirror.exe` is the single public app launcher. The app-icon entry `engine/Android Mirror Engine.exe` also opens the UI when double-clicked. The shell runs `engine/Android Mirror Runtime.bin` internally; updates preserve the visible entry and its icon. [Validation notes](docs/VALIDATION.md) describe what has been checked and what still requires physical devices.

## Credits and licences

Android Mirror is an independent Windows project. It is not an official Google product and is not affiliated with or endorsed by Google.

- **Android and Google:** Android is a trademark of Google LLC. Android platform and ADB work are credited to Google and the Android Open Source Project. See Google's [Android brand guidelines](https://developer.android.com/distribute/marketing-tools/brand-guidelines).
- **Logo:** The Android Mirror composition was supplied by this project's creator. The Android robot is reproduced or modified from work created and shared by Google and used according to terms described in the [Creative Commons 3.0 Attribution License](https://creativecommons.org/licenses/by/3.0/).


Project code is distributed under [Apache 2.0](LICENSE.txt). Third-party code, trademarks, and artwork remain subject to their own terms. [NOTICE.txt](NOTICE.txt) records attribution; it does not transfer ownership to Google or the Android project.
