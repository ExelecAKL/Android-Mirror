# Engine updates

Android Mirror's shell and mirroring engine are separate components. Engine logic is currently upstream code. The visible `Android Mirror Engine.exe` entry has the app icon and redirects to the UI. The shell runs the upstream engine internally as `Android Mirror Runtime.bin`, with resource-only icon branding; server and runtime-library code is unchanged. Device selection, reconnect supervision, settings, wireless pairing, branding, and tray behavior live in the shell.

## What users experience

Automatic mode checks the official stable Windows x64 release once a day and can download while mirroring is idle. A themed **Engine based update** popup shows readable publisher changes and **Install update / Remind me later** actions. Later defers that version for a day. Installation requires approval for that exact engine version and waits until there are no active mirrors. Users can instead choose Check only or Off, or manually check and install from Settings. No terminals are opened.

Official GitHub metadata provides the expected package size and SHA-256. Both are verified before use. The updater rechecks official metadata, validates archive paths and the new engine's version, and only then applies the package. Packages lacking a verifiable Windows asset are not installed.

The official executable is mapped to `Android Mirror Runtime.bin` in staging; the visible `Android Mirror Engine.exe` UI entry is maintained separately and preserved by engine updates. The local Android Mirror icon is reapplied before installation. Its original upstream hash, resulting file hash, and icon hash are recorded separately in the inventory as `Source: upstream-branded`. This known resource-only branding does not count as a custom engine code patch; additional file modifications still pause binary updates.

Full release ZIPs are downloaded. During installation, only changed, added, and removed owned engine files are applied; unchanged files keep their timestamps. The app, user preferences, recordings, and unrelated files are preserved. Former files are retained under `updates/backups/`. An installation failure attempts to restore the previous engine. Files locked by other apps can delay installation; retry after those apps release them. Program Files updates require the normal Windows administrator approval.

The installed option catalogue is refreshed after an update and when opening Advanced. Newly documented options become available; stored options removed by the engine are dropped. Existing option values and device profiles are retained, although a future engine can change the accepted values or behavior of a flag.

## Protecting engine changes

The inventory records the hashes and ownership of bundled engine files. Missing, modified, custom, or unverified engine files block binary replacement before any engine file is touched. This guard preserves local changes; it does not merge binaries.

If maintainers later customize the engine, use a source fork and keep patches in version control. Merge or rebase new upstream source, resolve conflicts, build the customized engine, and validate it with the shell. Mark a custom inventory as `Source: custom` so upstream binary updates remain paused. Ship the merged, tested build through an Android Mirror app release. Users receive an installer or portable update, never a source-code merge task.

## Android Mirror app updates

Engine updates and app updates use independent release feeds. Starting in 1.1.1, Settings checks the [Android Mirror releases page](https://github.com/ExelecAKL/Android-Mirror/releases) daily. Its **App update** popup shows **What’s changed** and **Install update / Remind me later**. Release-note formatting removes Markdown markup and bounds long summaries inside a scrollable area. Full publisher notes remain in Settings. Remind me later defers that version for a day; automatic checking can be disabled separately from engine updates.

Program Files installations download the exact stable installer named by our repository, verify its size and GitHub-provided SHA-256, and verify the selected release again in the update helper. The modern installer requests elevation, closes this installation gracefully, preserves its current engine, replaces app files with a rollback backup, and updates Windows registration. A separate helper running as the original user restarts the app so it does not inherit the installer's administrator privileges. Cancelling elevation keeps the app running. A failed file replacement attempts to restore previous app files; failed rollback retains recovery files and reports their location.

Portable copies receive checks and prompts but use the release page for manual replacement. Version 1.1.0 has no app-update checker, so users of that version must install 1.1.1 once manually. Publishing a release alone cannot make an older app execute update logic it does not contain.

Background screen-sharing behavior lives in the shell's Windows window-management layer. It does not patch engine code, so independent engine updates retain it.
