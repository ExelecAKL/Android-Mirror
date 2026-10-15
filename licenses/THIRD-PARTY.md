# Third-party components and source

Android Mirror is an independent project. The Android platform and Google's brands do not confer Google ownership over this app or its bundled libraries. Each component retains its own authorship and terms.

| Component | Bundled version | Licence / notice | Source |
| :-- | :-- | :-- | :-- |
| scrcpy engine | 5.0.1 | [Apache 2.0; original authors' notice](scrcpy-LICENSE.txt) | [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy/tree/v5.0.1) |
| Android Debug Bridge / Windows ADB DLLs | 37.0.1 | [Original Google package notices](ADB-NOTICE.txt) | [AOSP ADB](https://android.googlesource.com/platform/packages/modules/adb/) |
| FFmpeg shared libraries | 9.0.2 | [LGPL 2.1 or later](LGPL-2.1.txt) | [FFmpeg 9.0.2](https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz) |
| libusb | 1.0.30 | [LGPL 2.1 or later](libusb-LICENSE.txt) | [libusb](https://github.com/libusb/libusb/tree/v1.0.30) |
| SDL | 3.4.18 | [zlib](SDL-LICENSE.txt) | [SDL](https://github.com/libsdl-org/SDL/tree/release-3.4.18) |
| dav1d, included by FFmpeg | 1.5.4 | [BSD 2-Clause](dav1d-LICENSE.txt) | [VideoLAN](https://code.videolan.org/videolan/dav1d/-/tree/1.5.4) |
| QRCoder | 1.8.1 | [MIT](QRCoder-LICENSE.txt) | [Pinned source](https://github.com/Shane32/QRCoder/tree/d050cc7c0bc986f14cfedf24b947e0ec5c1a7736) |

This software uses FFmpeg libraries licensed under LGPL 2.1 or later. Matching runtime sources are provided as **Android-Mirror-1.1.0-Runtime-Sources.zip** alongside the [release downloads](https://github.com/ExelecAKL/Android-Mirror/releases/tag/v1.1.0). The source bundle includes FFmpeg, libusb, SDL, and dav1d archives, verified against the upstream engine's pinned SHA-256 values, together with dependency build scripts and metadata. FFmpeg is dynamically linked; its DLLs remain replaceable in `engine/`.

[SOURCES.json](SOURCES.json) records exact URLs, versions, and hashes. [upstream-build](upstream-build) contains the dependency build scripts from scrcpy v5.0.1. The engine and runtime binaries are unmodified. The FFmpeg configuration reported by the bundled library is recorded in [FFmpeg-CONFIGURATION.txt](FFmpeg-CONFIGURATION.txt). Build tools and external dependencies retain their own notices in their respective source packages.

Android is a trademark of Google LLC. The Android robot is reproduced or modified from work created and shared by Google and used according to terms described in the [Creative Commons 3.0 Attribution License](https://creativecommons.org/licenses/by/3.0/). The supplied Android Mirror logo adds the project's own composition; Google retains its rights in the underlying Android robot and trademarks.
