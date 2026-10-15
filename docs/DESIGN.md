# Design and implementation

Android Mirror is a compact Windows 10/11 utility built with WPF and .NET Framework 4.8. The shell manages independent mirroring processes and keeps detection alive in the notification area.

## Visual direction

The interface follows Microsoft's [Windows typography](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography), [color guidance](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color), and [system backdrop API](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type). The tray reference is the charcoal Windows/Discord notification-area menu; the glass reference is the Windows 11 Start panel. Reference research informed the existing Windows design system.

Segoe UI, native title bars, neutral surfaces, 4px control corners, and 8px cards keep the shell familiar. The supplied SVG logo is preserved as vector paths and rendered into native icon sizes at build time. Its green accent is #32DE84 with dark action text.

One Settings button opens connection mode, per-device audio/control choices, device management, automatic reconnection, Windows startup, theme, Advanced, and updates. Wireless setup is a second screen inside the same window, with QR and pairing-code tabs. Navigation icons are unboxed and the header omits a subtitle. Advanced places a section picker to the left of a search field with an icon and placeholder.

Confirmations implement the [Windows content-dialog pattern](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs) in WPF: rounded surfaces, a dimmed modal backdrop, theme-aware content and footer, explicit action labels, and Escape cancellation. Background input is disabled and Tab navigation stays inside the dialog. The latency notice recommends USB before enabling wireless. These are app-rendered dialogs rather than legacy MessageBoxes or a WinUI dependency.

Custom control templates follow Windows [checkbox](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/checkbox) and [expander](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/expander) references: rounded 20px boxes with an accent fill and contrasting checkmark, bare line chevrons, and theme-aware text. Focus uses solid strokes. Default dotted focus templates are explicitly disabled, including generated Advanced editors. Keyboard and accessibility behavior is retained.

Control feedback uses 120ms easing; view changes use 200ms fades and short movement. Motion follows Windows client-area animation preferences and high contrast mode. Windows desktop acrylic or an acrylic fallback is used where available and enabled. Dark tray menus use a uniformly dark gutter and separators.

## Devices and transports

ADB's `host:track-devices-l` event stream provides complete snapshots after transport changes. The shell marshals updates onto the WPF dispatcher. Wireless identities are resolved through the reported hardware serial and cached so Adaptive mode can choose USB over Wi-Fi for the same device.

Every physical device profile owns a connection controller. It serializes process stop/start transitions, limits retries, preserves settings, and avoids opening a second mirror before the former process exits. An audio/control change or transport handover restarts only the affected session. Closing a mirror pauses it; closing the launcher preserves detection in the tray.

## Wireless pairing

The implementation follows the [AOSP ADB Wi-Fi architecture](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/dev/adb_wifi.md) and [Android wireless debugging guide](https://developer.android.com/tools/adb#connect-to-a-device-over-wi-fi).

QR payloads use `WIFI:T:ADB;S:<random-service>;P:<random-secret>;;`. A cryptographically random session is valid for up to three minutes. Discovery accepts only the pairing service with the matching session name. Pairing secrets go to redirected stdin and are not written to arguments, diagnostics, or app preferences. QRCoder is pinned and embedded into the executable.

Manual pairing validates the IP address, port, and six-digit code before invoking ADB. Pairing and connection ports are distinct. After pairing, discovery searches for a single connection service at the paired host; otherwise the user enters the connection port. Commands have timeouts and cancellation, redirect their output, and set `CreateNoWindow=true` and `UseShellExecute=false`.

USB only is the default and skips app-owned wireless discovery and pairing. Other applications may continue using their own ADB connections because Android Mirror does not kill the shared ADB server. Wireless and Adaptive modes support discovering previously selected, paired devices. A native notification reports newly connected devices subject to Windows notification settings.

## Distribution

Runtime binaries are isolated in `engine/`; source, assets, vendor code, notices, and generated files have separate folders. Android Mirror.exe is the only public launcher. Android Mirror Engine.exe is a branded UI entry. The upstream runtime is Android Mirror Runtime.bin and receives the app icon through PE resource branding; engine code remains unchanged. Verified updates reapply the name and icon and record original and branded hashes separately. Preferences use LocalAppData so normal use under Program Files needs no elevation.

The installer maintains a file manifest and removes only its owned files and empty directories. Unrelated files and user preferences are preserved. Releases contain a portable ZIP, installer, checksums, and a matching runtime source bundle.
