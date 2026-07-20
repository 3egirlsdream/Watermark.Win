#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SOURCE="$ROOT/native/third_party/LibRaw-0.22.1"
TIFF_SOURCE="$ROOT/native/third_party/tiff-4.7.2"
WRAPPER="$ROOT/native/Watermark.Imaging.Native"
JOBS="${JOBS:-$(sysctl -n hw.logicalcpu 2>/dev/null || echo 4)}"

if [[ ! -x "$SOURCE/configure" || ! -x "$TIFF_SOURCE/configure" ]]; then
  echo "Pinned LibRaw 0.22.1 and LibTIFF 4.7.2 sources are required." >&2
  exit 1
fi

build_slice() {
  local name="$1" sdk="$2" target="$3" host="$4"
  local build="$ROOT/native/build/libraw/$name"
  local tiff_build="$ROOT/native/build/libtiff/$name"
  local stage="$ROOT/native/stage/libraw/$name"
  local sdkroot clang clangxx ar ranlib apple_libtool

  sdkroot="$(xcrun --sdk "$sdk" --show-sdk-path)"
  clang="$(xcrun --sdk "$sdk" --find clang)"
  clangxx="$(xcrun --sdk "$sdk" --find clang++)"
  ar="$(xcrun --sdk "$sdk" --find ar)"
  ranlib="$(xcrun --sdk "$sdk" --find ranlib)"
  apple_libtool="$(xcrun --sdk "$sdk" --find libtool)"

  WMI_OCIO_PLATFORM="$name"
  WMI_OCIO_STAGE="$ROOT/native/stage/ocio/$name"
  WMI_OCIO_BUILD_ROOT="$ROOT/native/build/ocio/$name"
  WMI_CMAKE_PLATFORM_ARGS=(
    -DCMAKE_C_COMPILER="$clang" -DCMAKE_CXX_COMPILER="$clangxx"
    -DCMAKE_C_COMPILER_TARGET="$target" -DCMAKE_CXX_COMPILER_TARGET="$target"
    -DCMAKE_OSX_SYSROOT="$sdkroot" -DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY
    # OCIO's macOS monitor-profile discovery uses ColorSync display APIs that
    # are unavailable to Mac Catalyst and iOS. Watermark uses a deterministic
    # in-memory config, so Apple embedded targets intentionally build the
    # SystemMonitor headless branch instead of probing the host display.
    -DCMAKE_CXX_FLAGS=-DOCIO_HEADLESS_ENABLED
  )
  source "$ROOT/native/scripts/lib/build-ocio-static.sh"

  rm -rf "$build" "$tiff_build" "$stage"
  mkdir -p "$build" "$tiff_build" "$stage/lib" "$stage/wrapper"

  # Keep zlib as a final-link dependency. Passing the archive path to LibRaw's
  # configure step makes libtool embed libz.a inside libraw_r.a, which Apple
  # libtool correctly rejects as a nested archive during XCFramework assembly.
  pushd "$build" >/dev/null
  CC="$clang" CXX="$clangxx" AR="$ar" RANLIB="$ranlib" \
    CPPFLAGS="-I$WMI_OCIO_STAGE/include" LDFLAGS="-target $target -isysroot $sdkroot -L$WMI_OCIO_STAGE/lib" \
    PKG_CONFIG_LIBDIR="$WMI_OCIO_STAGE/lib/pkgconfig" \
    ZLIB_CFLAGS="-I$WMI_OCIO_STAGE/include" ZLIB_LIBS="-lz" \
    CFLAGS="-target $target -isysroot $sdkroot -O3 -fPIC -DLIBRAW_CALLOC_RAWSTORE" \
    CXXFLAGS="-target $target -isysroot $sdkroot -O3 -fPIC -DLIBRAW_CALLOC_RAWSTORE" \
    "$SOURCE/configure" --host="$host" --prefix="$stage" \
      --disable-shared --enable-static --disable-openmp --disable-lcms \
      --disable-jpeg --disable-examples
  make -j"$JOBS"
  make install
  popd >/dev/null

  pushd "$tiff_build" >/dev/null
  CC="$clang" CXX="$clangxx" AR="$ar" RANLIB="$ranlib" \
    CPPFLAGS="-I$WMI_OCIO_STAGE/include" LDFLAGS="-target $target -isysroot $sdkroot -L$WMI_OCIO_STAGE/lib" \
    PKG_CONFIG_LIBDIR="$WMI_OCIO_STAGE/lib/pkgconfig" \
    ZLIB_CFLAGS="-I$WMI_OCIO_STAGE/include" ZLIB_LIBS="$WMI_OCIO_STAGE/lib/libz.a" \
    CFLAGS="-target $target -isysroot $sdkroot -O3 -fPIC" \
    CXXFLAGS="-target $target -isysroot $sdkroot -O3 -fPIC" \
    "$TIFF_SOURCE/configure" --host="$host" --prefix="$stage" \
      --disable-shared --enable-static --disable-cxx --disable-tools --disable-tests \
      --disable-contrib --disable-docs --disable-jpeg --disable-jbig \
      --disable-lzma --disable-zstd --disable-webp
  make -j"$JOBS"
  make install
  popd >/dev/null

  "$clangxx" -std=c++17 -target "$target" -isysroot "$sdkroot" -O3 -fPIC \
    -fvisibility=hidden -DWMI_BUILDING_LIBRARY=1 -DWMI_HAS_LIBRAW=1 \
    -DWMI_HAS_TIFF=1 -DWMI_HAS_OCIO=1 -I"$WRAPPER/include" \
    -I"$stage/include" -I"$WMI_OCIO_STAGE/include" \
    -c "$WRAPPER/src/watermark_imaging.cpp" -o "$stage/wrapper/watermark_imaging.o"
  "$clangxx" -std=c++17 -target "$target" -isysroot "$sdkroot" -O3 -fPIC \
    -fvisibility=hidden -DWMI_BUILDING_LIBRARY=1 -DWMI_HAS_OCIO=1 \
    -I"$WRAPPER/include" -I"$WMI_OCIO_STAGE/include" \
    -c "$WRAPPER/src/watermark_color_ocio.cpp" -o "$stage/wrapper/watermark_color_ocio.o"
  "$ar" crs "$stage/wrapper/libWatermark.Imaging.Wrapper.a" \
    "$stage/wrapper/watermark_imaging.o" "$stage/wrapper/watermark_color_ocio.o"

  local ocio_libraries=("$WMI_OCIO_STAGE"/lib/*.a)
  "$apple_libtool" -static -o "$stage/wrapper/libWatermark.Imaging.Native.a" \
    "$stage/wrapper/libWatermark.Imaging.Wrapper.a" "$stage/lib/libraw_r.a" \
    "$stage/lib/libtiff.a" "${ocio_libraries[@]}"
  "$ranlib" "$stage/wrapper/libWatermark.Imaging.Native.a"
}

build_slice maccatalyst-arm64 macosx arm64-apple-ios14.0-macabi aarch64-apple-darwin
build_slice maccatalyst-x86_64 macosx x86_64-apple-ios14.0-macabi x86_64-apple-darwin
build_slice ios-arm64 iphoneos arm64-apple-ios14.2 aarch64-apple-darwin
build_slice iossimulator-arm64 iphonesimulator arm64-apple-ios14.2-simulator aarch64-apple-darwin
build_slice iossimulator-x86_64 iphonesimulator x86_64-apple-ios14.2-simulator x86_64-apple-darwin

PACKAGE="$ROOT/native/build/package"
rm -rf "$PACKAGE"
mkdir -p "$PACKAGE/maccatalyst" "$PACKAGE/ios"
xcrun lipo -create \
  "$ROOT/native/stage/libraw/maccatalyst-arm64/wrapper/libWatermark.Imaging.Native.a" \
  "$ROOT/native/stage/libraw/maccatalyst-x86_64/wrapper/libWatermark.Imaging.Native.a" \
  -output "$PACKAGE/maccatalyst/libWatermark.Imaging.Native.a"
xcrun lipo -create \
  "$ROOT/native/stage/libraw/iossimulator-arm64/wrapper/libWatermark.Imaging.Native.a" \
  "$ROOT/native/stage/libraw/iossimulator-x86_64/wrapper/libWatermark.Imaging.Native.a" \
  -output "$PACKAGE/ios/libWatermark.Imaging.Native-simulator.a"
cp "$ROOT/native/stage/libraw/ios-arm64/wrapper/libWatermark.Imaging.Native.a" \
  "$PACKAGE/ios/libWatermark.Imaging.Native-device.a"

rm -rf "$ROOT/native/artifacts/maccatalyst/Watermark.Imaging.Native.xcframework"
mkdir -p "$ROOT/native/artifacts/maccatalyst"
xcodebuild -create-xcframework \
  -library "$PACKAGE/maccatalyst/libWatermark.Imaging.Native.a" -headers "$WRAPPER/include" \
  -output "$ROOT/native/artifacts/maccatalyst/Watermark.Imaging.Native.xcframework"
rm -rf "$ROOT/native/artifacts/ios/Watermark.Imaging.Native.xcframework"
mkdir -p "$ROOT/native/artifacts/ios"
xcodebuild -create-xcframework \
  -library "$PACKAGE/ios/libWatermark.Imaging.Native-device.a" -headers "$WRAPPER/include" \
  -library "$PACKAGE/ios/libWatermark.Imaging.Native-simulator.a" -headers "$WRAPPER/include" \
  -output "$ROOT/native/artifacts/ios/Watermark.Imaging.Native.xcframework"

WMI_APPLE_ARTIFACT_ABI=4 "$ROOT/native/scripts/update-native-manifest.sh"
echo "Apple ABI 4 LibRaw, LibTIFF and OpenColorIO artifacts are ready under native/artifacts."
