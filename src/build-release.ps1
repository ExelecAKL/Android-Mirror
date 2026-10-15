$ErrorActionPreference = 'Stop'
$packageDirectory = Split-Path -Parent $PSScriptRoot
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $packageDirectory 'Android Mirror.exe')).FileVersion
$version = ($version -split '\.')[0..2] -join '.'
$releaseDirectory = Join-Path $packageDirectory ('releases\' + $version)
$payloadPath = Join-Path $packageDirectory 'build\setup\payload.zip'
$setupPath = Join-Path $packageDirectory 'installer\Android Mirror Setup.exe'
if (-not (Test-Path -LiteralPath $payloadPath) -or -not (Test-Path -LiteralPath $setupPath)) { throw 'Build the app and installer before packaging a release.' }
if ((Get-Item -LiteralPath $payloadPath).LastWriteTimeUtc -lt (Get-Item -LiteralPath (Join-Path $packageDirectory 'Android Mirror.exe')).LastWriteTimeUtc) { throw 'The installer payload is older than the app. Rebuild setup first.' }
$sourceDirectory = Join-Path $packageDirectory 'build\runtime-sources'
$pins = Get-Content -LiteralPath (Join-Path $packageDirectory 'licenses\SOURCES.json') -Raw | ConvertFrom-Json
foreach ($pin in $pins | Where-Object name -ne 'platform-tools') {
    $filename = $pin.name + '-' + [IO.Path]::GetFileName(([Uri]$pin.url).AbsolutePath)
    $path = Join-Path $sourceDirectory $filename
    if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $pin.sha256) { throw 'Matching runtime sources are missing. Run src/fetch-runtime-sources.ps1.' }
}
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$portable = Join-Path $releaseDirectory ('Android-Mirror-' + $version + '-Windows-x64.zip')
if (Test-Path -LiteralPath $portable) { Remove-Item -LiteralPath $portable }
$output = [IO.Compression.ZipFile]::Open($portable, [IO.Compression.ZipArchiveMode]::Create)
$payload = [IO.Compression.ZipFile]::OpenRead($payloadPath)
try {
    foreach ($entry in $payload.Entries | Where-Object FullName -ne 'Uninstall Android Mirror.exe') {
        $copy = $output.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal)
        $source = $entry.Open(); $destination = $copy.Open()
        try { $source.CopyTo($destination) } finally { $source.Dispose(); $destination.Dispose() }
    }
} finally { $payload.Dispose(); $output.Dispose() }
Copy-Item -LiteralPath $setupPath -Destination (Join-Path $releaseDirectory ('Android-Mirror-' + $version + '-Setup.exe')) -Force

$sourceZip = Join-Path $releaseDirectory ('Android-Mirror-' + $version + '-Runtime-Sources.zip')
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
$archive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($pin in $pins | Where-Object name -ne 'platform-tools') {
        $filename = $pin.name + '-' + [IO.Path]::GetFileName(([Uri]$pin.url).AbsolutePath)
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $sourceDirectory $filename), $filename, [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $packageDirectory 'licenses') -File -Recurse) {
        $relative = $file.FullName.Substring($packageDirectory.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }

$notes = @'
# Android Mirror 1.1.0

A native Windows shell for mirroring your Android devices, with automatic reconnection, per-device settings, and in-app wireless pairing.

- New Android Mirror logo, Windows light/dark themes, acrylic where supported, animated controls, and a dark native tray menu.
- Mirror a phone and tablet at once with separate audio and PC control options.
- USB only (default), Wireless only, and Adaptive modes. Adaptive prefers USB and can hand over to an available paired Wi-Fi transport.
- QR pairing or manual IP, pairing port, and six-digit code entry, with no ADB terminal windows.
- Native Windows confirmation before enabling wireless: Wi-Fi can add latency; USB offers the lowest delay.
- Program Files installation, optional desktop shortcut, background tray operation, and a portable ZIP.

## Downloads

- **Android-Mirror-1.1.0-Setup.exe** — install for all users in Program Files.
- **Android-Mirror-1.1.0-Windows-x64.zip** — extract and run Android Mirror.exe.
- **Android-Mirror-1.1.0-Runtime-Sources.zip** — matching sources and build metadata for bundled FFmpeg, libusb, SDL, and dav1d. FFmpeg uses LGPL 2.1 or later.
- **SHA256SUMS.txt** — verify downloaded files with Get-FileHash -Algorithm SHA256.

Windows 10/11 x64 and .NET Framework 4.8 are required. Wireless pairing and audio forwarding require Android 11+. The executables are unsigned. This is a GitHub release, not a Microsoft Store listing.

## Validation and limitations

35 automated connection checks and five installer checks pass. Native shell checks cover tray/background behavior, themes, SVG/QR rendering, and pairing navigation. Physical camera pairing, audio routing, input, and cable/Wi-Fi handover still require testing on actual devices. Wireless discovery can be blocked by network configuration; manual connection remains available.

Android Mirror is independent and is not affiliated with or endorsed by Google. Android is a trademark of Google LLC. Google/Android, the original mirroring engine authors, and runtime-library credits and licence notices are included with every package.
'@
[IO.File]::WriteAllText((Join-Path $releaseDirectory 'RELEASE-NOTES.md'), $notes, [Text.UTF8Encoding]::new($false))
$hashes = Get-ChildItem -LiteralPath $releaseDirectory -File | Where-Object { $_.Extension -in @('.exe','.zip') } | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $releaseDirectory 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath $releaseDirectory -File | Select-Object Name,Length
