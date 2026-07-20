#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SOURCE="$ROOT/native/third_party/LibRaw-0.22.1"
TIFF_SOURCE="$ROOT/native/third_party/tiff-4.7.2"
WRAPPER="$ROOT/native/Watermark.Imaging.Native"
EXPECTED_NDK_VERSION="${WMI_ANDROID_NDK_VERSION:-26.1.10909125}"
SDK_ROOT="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-$HOME/Library/Android/sdk}}"
NDK_ROOT="${ANDROID_NDK_ROOT:-${ANDROID_NDK_HOME:-}}"
API="${ANDROID_API:-24}"
JOBS="${JOBS:-4}"
WMI_DEPENDENCY_JOBS="${WMI_DEPENDENCY_JOBS:-1}"

if [[ -z "$NDK_ROOT" && -d "$SDK_ROOT/ndk/$EXPECTED_NDK_VERSION" ]]; then
  NDK_ROOT="$SDK_ROOT/ndk/$EXPECTED_NDK_VERSION"
fi
if [[ -z "$NDK_ROOT" || ! -f "$NDK_ROOT/build/cmake/android.toolchain.cmake" ]]; then
  echo "Install Android NDK $EXPECTED_NDK_VERSION or set ANDROID_NDK_ROOT." >&2
  exit 1
fi
NDK_VERSION="$(sed -n 's/^Pkg\.Revision[[:space:]]*=[[:space:]]*//p' "$NDK_ROOT/source.properties" | head -1)"
if [[ "$NDK_VERSION" != "$EXPECTED_NDK_VERSION" ]]; then
  echo "Expected Android NDK $EXPECTED_NDK_VERSION, found ${NDK_VERSION:-unknown}." >&2
  exit 1
fi
HOST_TAG="$(find "$NDK_ROOT/toolchains/llvm/prebuilt" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | head -1)"
TOOLCHAIN="$NDK_ROOT/toolchains/llvm/prebuilt/$HOST_TAG"

build_abi() {
  local abi="$1" host="$2" clang_prefix="$3"
  local build="$ROOT/native/build/libraw/android-$abi"
  local tiff_build="$ROOT/native/build/libtiff/android-$abi"
  local stage="$ROOT/native/stage/libraw/android-$abi"
  local cc="$TOOLCHAIN/bin/${clang_prefix}${API}-clang"
  local cxx="$TOOLCHAIN/bin/${clang_prefix}${API}-clang++"

  WMI_OCIO_PLATFORM="android-$abi"
  WMI_OCIO_STAGE="$ROOT/native/stage/ocio/android-$abi"
  WMI_OCIO_BUILD_ROOT="$ROOT/native/build/ocio/android-$abi"
  WMI_CMAKE_PLATFORM_ARGS=(
    -DCMAKE_TOOLCHAIN_FILE="$NDK_ROOT/build/cmake/android.toolchain.cmake"
    -DANDROID_ABI="$abi" -DANDROID_PLATFORM="android-$API" -DANDROID_STL=c++_static
    -DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY
  )
  source "$ROOT/native/scripts/lib/build-ocio-static.sh"

  rm -rf "$build" "$tiff_build" "$stage"
  mkdir -p "$build" "$tiff_build" "$stage"
  # Keep zlib as a final-link dependency so libraw_r.a remains a flat archive.
  # The pinned static libz.a is included in ocio_libraries below.
  pushd "$build" >/dev/null
  CC="$cc" CXX="$cxx" AR="$TOOLCHAIN/bin/llvm-ar" RANLIB="$TOOLCHAIN/bin/llvm-ranlib" \
    CPPFLAGS="-I$WMI_OCIO_STAGE/include" LDFLAGS="-L$WMI_OCIO_STAGE/lib" \
    PKG_CONFIG_LIBDIR="$WMI_OCIO_STAGE/lib/pkgconfig" \
    ZLIB_CFLAGS="-I$WMI_OCIO_STAGE/include" ZLIB_LIBS="-lz" \
    CFLAGS="-O3 -fPIC -DLIBRAW_CALLOC_RAWSTORE" CXXFLAGS="-O3 -fPIC -DLIBRAW_CALLOC_RAWSTORE" \
    "$SOURCE/configure" --host="$host" --prefix="$stage" --disable-shared \
      --enable-static --disable-openmp --disable-lcms --disable-jpeg --disable-examples
  make -j"$JOBS" && make install
  popd >/dev/null

  pushd "$tiff_build" >/dev/null
  CC="$cc" CXX="$cxx" AR="$TOOLCHAIN/bin/llvm-ar" RANLIB="$TOOLCHAIN/bin/llvm-ranlib" \
    CPPFLAGS="-I$WMI_OCIO_STAGE/include" LDFLAGS="-L$WMI_OCIO_STAGE/lib" \
    PKG_CONFIG_LIBDIR="$WMI_OCIO_STAGE/lib/pkgconfig" \
    ZLIB_CFLAGS="-I$WMI_OCIO_STAGE/include" ZLIB_LIBS="$WMI_OCIO_STAGE/lib/libz.a" \
    CFLAGS="-O3 -fPIC" CXXFLAGS="-O3 -fPIC" \
    "$TIFF_SOURCE/configure" --host="$host" --prefix="$stage" --disable-shared \
      --enable-static --disable-cxx --disable-tools --disable-tests --disable-contrib \
      --disable-docs --disable-jpeg --disable-jbig --disable-lzma --disable-zstd --disable-webp
  make -j"$JOBS" && make install
  popd >/dev/null

  local ocio_libraries=("$WMI_OCIO_STAGE"/lib/*.a)
  mkdir -p "$ROOT/native/artifacts/android/$abi"
  "$cxx" -std=c++17 -shared -O3 -fPIC -fvisibility=hidden -static-libstdc++ \
    -DWMI_BUILDING_LIBRARY=1 -DWMI_HAS_LIBRAW=1 -DWMI_HAS_TIFF=1 -DWMI_HAS_OCIO=1 \
    -I"$WRAPPER/include" -I"$stage/include" -I"$WMI_OCIO_STAGE/include" \
    "$WRAPPER/src/watermark_imaging.cpp" "$WRAPPER/src/watermark_color_ocio.cpp" \
    -Wl,--start-group "$stage/lib/libraw_r.a" "$stage/lib/libtiff.a" \
    "${ocio_libraries[@]}" -Wl,--end-group -llog -ldl -lm \
    -Wl,--no-undefined -Wl,-soname,libWatermark.Imaging.Native.so \
    -o "$ROOT/native/artifacts/android/$abi/libWatermark.Imaging.Native.so"
  "$TOOLCHAIN/bin/llvm-strip" --strip-unneeded \
    "$ROOT/native/artifacts/android/$abi/libWatermark.Imaging.Native.so"
}

build_abi arm64-v8a aarch64-linux-android aarch64-linux-android
build_abi x86_64 x86_64-linux-android x86_64-linux-android
WMI_ANDROID_ARTIFACT_ABI=4 "$ROOT/native/scripts/update-native-manifest.sh"
echo "Android ABI 4 LibRaw, LibTIFF and OpenColorIO artifacts are ready under native/artifacts/android."
