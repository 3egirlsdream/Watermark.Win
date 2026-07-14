# Watermark.Imaging.Native

Stable C ABI for the cross-platform imaging pipeline. The managed layer owns WM16,
job scheduling and manifests; this library owns LibRaw decoding, deterministic
star alignment, RGB16 preview/full-resolution transforms, native float32 stacking
and LibTIFF output. The current public contract is ABI V3; ABI V2 is rejected.

Pinned dependencies:

- LibRaw 0.22.1
- LibTIFF 4.7.2

Star alignment is implemented by this C++17 library with LoG-style detection,
sub-pixel centroids, grid-balanced features, local triangle descriptor buckets and
deterministic RANSAC. Preview stacking is bounded by the managed CPU budget and
parallelized by output ranges, with NEON/SSE/AVX compiler paths and a scalar
fallback. The release does not link or deploy OpenCV.

Application builds consume the verified binaries committed under
`native/artifacts`; they do not download or compile LibRaw. The repository does
not track LibRaw implementation sources. It only keeps LibRaw's license and
copyright files, while the exact official source URL and archive checksum are
recorded in `native/artifacts/manifest.json` and `native/NATIVE-BINARY-COMMIT.md`.
LibTIFF 4.7.2 sources are vendored under `native/third_party`.

The build scripts below are maintainer-only workflows for regenerating native
artifacts. They never download LibRaw automatically: obtain the pinned official
archive yourself, verify its SHA-256, and place the extracted source at
`native/third_party/LibRaw-0.22.1` before running them. Generated intermediate
files stay under ignored `native/build` and `native/stage` directories.

Apple builds
------------

Requirements: Xcode command-line tools, `pkg-config`, and the Autotools-compatible
`make` shipped with macOS. Run:

```sh
native/scripts/build-libraw-apple.sh
```

This produces:

- Mac Catalyst arm64/x86_64 static XCFramework
- iOS device arm64 and simulator arm64/x86_64 static XCFramework

LibRaw is built as the thread-safe static archive with system zlib,
`LIBRAW_CALLOC_RAWSTORE`, and without OpenMP, LCMS, examples, or lossy-DNG JPEG.
LibTIFF is built as a static library with Deflate support and without its tools,
tests, documentation or optional image codecs. The C ABI wrapper, LibRaw and
LibTIFF objects are merged into one archive per slice.

Android builds
--------------

Install Android NDK `26.1.10909125` (the version used by the .NET 8 Android
workload) under the standard Android SDK directory, or set `ANDROID_NDK_ROOT` to
it, and run:

```sh
native/scripts/build-libraw-android.sh
```

The script automatically checks `$ANDROID_SDK_ROOT`, `$ANDROID_HOME`, and the
default macOS path `$HOME/Library/Android/sdk` for that exact NDK version.

The script produces arm64-v8a and x86_64 shared libraries for API 24. The C++
runtime is linked statically, so no extra `libc++_shared.so` is deployed.

Apple and Android builds refresh `native/artifacts/manifest.json` after they
finish. To refresh it without rebuilding, run:

```sh
native/scripts/update-native-manifest.sh
```

The manifest records every Apple, Android and Windows artifact currently present,
including its relative path, architecture and SHA-256 digest.

Windows builds
--------------

On a Windows VM with Visual Studio 2026 x64 and ARM64 C++ build tools installed,
double-click `native\scripts\build-libraw-windows-all.cmd`, or run the builder
from an ordinary PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File native/scripts/build-libraw-windows-all.ps1
```

It bootstraps vcpkg under `%LOCALAPPDATA%\Watermark.Native\vcpkg` when needed,
installs static zlib for both
architectures, creates isolated Visual Studio developer environments, builds and
verifies both DLLs. An existing vcpkg checkout can be selected with
`-VcpkgRoot D:\tools\vcpkg`.

The verified outputs are written to:

```text
native\artifacts\win-x64\Watermark.Imaging.Native.dll
native\artifacts\win-arm64\Watermark.Imaging.Native.dll
```

To build only one architecture, use a Visual Studio 2026 Developer PowerShell
matching the requested architecture and provide a matching static zlib prefix:

```powershell
$env:ZLIB_ROOT = "C:\vcpkg\installed\x64-windows-static"
native/scripts/build-libraw-windows.ps1 -Arch x64 -ZlibRoot $env:ZLIB_ROOT
```

The script builds the official LibRaw and LibTIFF static targets, then links them
into the single `Watermark.Imaging.Native.dll` deployed by the WPF project. All
three projects use the static MSVC runtime (`/MT`), so the package does not
require a separate Visual C++ runtime installation.

CMake integration
-----------------

`WMI_LIBRAW_ROOT` accepts a staged LibRaw prefix and verifies the pinned header
version before linking. Without it, CMake retains support for an explicitly
provided `LibRawConfig.cmake` package. A developer can also build the ABI-only
stub with LibRaw and LibTIFF disabled.

```sh
cmake -S . -B build -DWMI_ENABLE_LIBRAW=OFF -DWMI_ENABLE_TIFF=OFF
cmake --build build --config Release
```

Native smoke verification
-------------------------

`tests/Watermark.Imaging.Native.Smoke.csproj` verifies that the static XCFramework
is force-linked into a .NET Mac Catalyst or iOS application and that the WMI ABI
and RAW capability exports can be resolved from the main executable.
The CMake test targets also cover feature alignment, bicubic/Lanczos transforms,
validity masks, maximum stacking and two-pass sigma rejection.

No dependency or binary is downloaded at application runtime.
