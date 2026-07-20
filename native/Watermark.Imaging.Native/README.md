# Watermark.Imaging.Native

Stable C ABI for the cross-platform imaging pipeline. The managed layer owns
WM16, job scheduling and manifests; this library owns LibRaw decoding,
deterministic star alignment, RGB16 transforms, native float32 stacking,
LibTIFF output and OpenColorIO color processing. The current public contract is
ABI V4. ABI V3 packages remain usable for RAW/TIFF features, but do not expose
`WMI_CAP_COLOR_OCIO` and therefore cannot enable production color grading.

Pinned native dependencies:

- LibRaw 0.22.1
- LibTIFF 4.7.2
- OpenColorIO 2.5.2
- Expat 2.7.2
- yaml-cpp 0.8.0
- Imath 3.2.1
- pystring 1.1.4
- minizip-ng 4.0.10
- zlib 1.3.2
- sse2neon commit `227cc413fb2d50b2a10073087be96b59d5364aea`

The exact source URLs, revisions and SHA-256 values for OCIO and its transitive
dependencies are stored in `native/dependencies.lock.json`. A maintainer places
the archives in the ignored `native/downloads` directory. The preparation step
verifies every archive before extracting it to ignored `native/third_party` and
never downloads sources. Application builds only consume verified binaries
under `native/artifacts`; they never download or compile native dependencies.

OpenColorIO is linked statically with apps, Python, Java, documentation, tests,
GPU tests and OpenFX disabled. The wrapper exposes both the CPU Processor and an
OCIO-authored GLSL ES 3.0 snapshot. No handwritten production color formula is
used as a fallback. The build also excludes OpenCV, OpenMP, LCMS and lossy-DNG
JPEG support.

Offline preparation
-------------------

Place every archive named by `native/dependencies.lock.json` in
`native/downloads`, then run:

```sh
cmake -DWMI_ROOT="$PWD" -DWMI_EXTRACT=ON \
  -P native/cmake/PrepareLockedDependencies.cmake
```

Missing archives and checksum mismatches fail immediately. Generated build and
stage files remain in ignored `native/build` and `native/stage` directories.

Host smoke build
----------------

On macOS, the following command builds the locked OCIO dependency graph, ABI V4
wrapper and native tests without changing packaged platform artifacts:

```sh
native/scripts/build-native-host.sh
```

Apple builds
------------

Requirements: Xcode command-line tools, CMake, Autotools-compatible `make`, and
the pinned LibRaw/LibTIFF source trees. Run:

```sh
native/scripts/build-native-apple.sh
```

This produces Mac Catalyst arm64/x86_64 and iOS device/simulator static
XCFrameworks. `build-libraw-apple.sh` remains a compatibility entry and calls
the same builder.

Android builds
--------------

Install Android NDK `26.1.10909125`, or set `ANDROID_NDK_ROOT` to that exact
version, then run:

```sh
native/scripts/build-native-android.sh
```

The builder checks `ANDROID_SDK_ROOT`, `ANDROID_HOME`, and the default macOS SDK
location. It produces API 24 `arm64-v8a` and `x86_64` shared libraries with the
C++ runtime and OCIO dependency graph statically linked. `build-libraw-android.sh`
is a compatibility entry.

Windows builds
--------------

On a Windows machine with Visual Studio C++ x64/ARM64 tools and CMake, run:

```powershell
powershell -ExecutionPolicy Bypass -File native/scripts/build-ocio-windows-all.ps1
```

This named entry point is the recommended command when enabling OpenColorIO.
OpenColorIO is statically linked into the final wrapper, so the command rebuilds
the complete ABI 4 native wrapper rather than producing a standalone OCIO DLL.
It reuses the same compatibility orchestrator as the historical LibRaw entry,
which verifies the locked offline archives, creates
isolated Visual Studio developer environments, invokes
`build-native-windows.ps1` for x64 and ARM64, runs tests where the host can
execute them, checks PE architecture and refreshes the manifest. It does not
bootstrap vcpkg or access the network.

`build-libraw-windows-all.ps1` remains available as a compatibility entry point
for the same build.

Subsequent runs reuse the generated dependency build and stage directories. Use
`-Clean` after changing toolchains, compiler flags or locked dependency sources;
use `-SkipTests` for a faster local iteration.

The verified outputs are written to:

```text
native\artifacts\win-x64\Watermark.Imaging.Native.dll
native\artifacts\win-arm64\Watermark.Imaging.Native.dll
```

Artifact manifest
-----------------

Apple and Android builders update only their own platform ABI markers. Windows
updates its markers after both DLLs pass verification. Therefore a partial
rebuild cannot incorrectly mark another platform as ABI V4. To refresh hashes
without changing platform ABI markers, run:

```sh
native/scripts/update-native-manifest.sh
```

`native/artifacts/manifest.json` schema 3 records the source ABI, per-platform
artifact ABI, dependency lock hash, architectures, minimum API and binary
SHA-256 values. `artifactSetReady` is true only when Apple, Android and Windows
artifacts are all ABI V4.

CMake integration
-----------------

`WMI_LIBRAW_ROOT`, `WMI_OCIO_ROOT` and the normal CMake prefix path accept the
staged static dependency prefixes. A developer can still build an ABI-only stub
with imaging features disabled:

```sh
cmake -S native/Watermark.Imaging.Native -B native/build/wrapper/stub \
  -DWMI_ENABLE_LIBRAW=OFF -DWMI_ENABLE_TIFF=OFF -DWMI_ENABLE_OCIO=OFF
cmake --build native/build/wrapper/stub --config Release
```

Native verification
-------------------

The CMake suite covers ABI/capabilities, processor lifecycle, pixel formats,
alpha passthrough, cancellation and the existing alignment/WM16 functionality.
The managed suite additionally checks OCIO parameter semantics, session
migration and the shared preview/export pipeline. Application UI validation for
mobile must be performed in the Android emulator rather than using Mac Catalyst
as a substitute.
