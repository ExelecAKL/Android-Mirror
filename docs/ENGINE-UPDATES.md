# Engine updates

Android Mirror's shell and mirroring engine are separate components. Engine logic is currently upstream code. The visible `Android Mirror Engine.exe` entry has the app icon and redirects to the UI. The shell runs the upstream engine internally as `Android Mirror Runtime.bin`, with resource-only icon branding; server and runtime-library code is unchanged. Device selection, reconnect supervision, settings, wireless pairing, branding, and tray behavior live in the shell.

## What users experience

Automatic mode checks the official stable Windows x64 release once a day. Downloads start while mirroring is idle. Installation waits until there are no active mirrors and the launcher has been hidden in the tray for two idle minutes. Users can instead choose Check only or Off, or manually check and install from Settings. No terminals are opened.

Official GitHub metadata provides the expected package size and SHA-256. Both are verified before use. The updater rechecks official metadata, validates archive paths and the new engine's version, and only then applies the package. Packages lacking a verifiable Windows asset are not installed.

The official executable is mapped to `Android Mirror Runtime.bin` in staging; the visible `Android Mirror Engine.exe` UI entry is maintained separately and preserved by engine updates. The local Android Mirror icon is reapplied before installation. Its original upstream hash, resulting file hash, and icon hash are recorded separately in the inventory as `Source: upstream-branded`. This known resource-only branding does not count as a custom engine code patch; additional file modifications still pause binary updates.

Full release ZIPs are downloaded. During installation, only changed, added, and removed owned engine files are applied; unchanged files keep their timestamps. The app, user preferences, recordings, and unrelated files are preserved. Former files are retained under `updates/backups/`. An installation failure attempts to restore the previous engine. Files locked by other apps can delay installation; retry after those apps release them. Program Files updates require the normal Windows administrator approval.

The installed option catalogue is refreshed after an update and when opening Advanced. Newly documented options become available; stored options removed by the engine are dropped. Existing option values and device profiles are retained, although a future engine can change the accepted values or behavior of a flag.

## Protecting engine changes

The inventory records the hashes and ownership of bundled engine files. Missing, modified, custom, or unverified engine files block binary replacement before any engine file is touched. This guard preserves local changes; it does not merge binaries.

If maintainers later customize the engine, use a source fork and keep patches in version control. Merge or rebase new upstream source, resolve conflicts, build the customized engine, and validate it with the shell. Mark a custom inventory as `Source: custom` so upstream binary updates remain paused. Ship the merged, tested build through an Android Mirror app release. Users receive an installer or portable update, never a source-code merge task.

Upstream engine updates do not update Android Mirror's own shell. Shell releases remain available from the [Android Mirror releases page](https://github.com/ExelecAKL/Android-Mirror/releases).
