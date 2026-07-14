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

if [[ -z "$NDK_ROOT" && -d "$SDK_ROOT/ndk/$EXPECTED_NDK_VERSION" ]]; then
  NDK_ROOT="$SDK_ROOT/ndk/$EXPECTED_NDK_VERSION"
fi

if [[ -z "$NDK_ROOT" || ! -d "$NDK_ROOT/toolchains/llvm/prebuilt" ]]; then
  echo "Install Android NDK $EXPECTED_NDK_VERSION under $SDK_ROOT or set ANDROID_NDK_ROOT." >&2
  exit 1
fi
if [[ ! -x "$SOURCE/configure" || ! -x "$TIFF_SOURCE/configure" ]]; then
  echo "Pinned LibRaw 0.22.1 and LibTIFF 4.7.2 sources are required." >&2
  exit 1
fi
if [[ ! -f "$NDK_ROOT/source.properties" ]]; then
  echo "Android NDK metadata is missing: $NDK_ROOT/source.properties" >&2
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
  local abi="$1" target="$2" host="$3" clang_prefix="$4"
  local build="$ROOT/native/build/libraw/android-$abi"
  local tiff_build="$ROOT/native/build/libtiff/android-$abi"
  local stage="$ROOT/native/stage/libraw/android-$abi"
  local cc="$TOOLCHAIN/bin/${clang_prefix}${API}-clang"
  local cxx="$TOOLCHAIN/bin/${clang_prefix}${API}-clang++"

  rm -rf "$build" "$tiff_build" "$stage"
  mkdir -p "$build" "$tiff_build" "$stage"
  pushd "$build" >/dev/null
  CC="$cc" CXX="$cxx" AR="$TOOLCHAIN/bin/llvm-ar" RANLIB="$TOOLCHAIN/bin/llvm-ranlib" \
    CFLAGS="-O3 -fPIC -DLIBRAW_CALLOC_RAWSTORE" \
    CXXFLAGS="-O3 -fPIC -DLIBRAW_CALLOC_RAWSTORE" \
    "$SOURCE/configure" --host="$host" --prefix="$stage" \
      --disable-shared --enable-static --disable-openmp --disable-lcms \
      --disable-jpeg --disable-examples
  make -j"$JOBS"
  make install
  popd >/dev/null

  pushd "$tiff_build" >/dev/null
  CC="$cc" CXX="$cxx" AR="$TOOLCHAIN/bin/llvm-ar" RANLIB="$TOOLCHAIN/bin/llvm-ranlib" \
    CFLAGS="-O3 -fPIC" CXXFLAGS="-O3 -fPIC" \
    "$TIFF_SOURCE/configure" --host="$host" --prefix="$stage" \
      --disable-shared --enable-static --disable-cxx \
      --disable-tools --disable-tests --disable-contrib --disable-docs \
      --disable-jpeg --disable-jbig --disable-lzma --disable-zstd --disable-webp
  make -j"$JOBS"
  make install
  popd >/dev/null

  mkdir -p "$ROOT/native/artifacts/android/$abi"
  "$cxx" -std=c++17 -shared -O3 -fPIC -fvisibility=hidden -static-libstdc++ \
    -DWMI_BUILDING_LIBRARY=1 -DWMI_HAS_LIBRAW=1 -DWMI_HAS_TIFF=1 \
    -I"$WRAPPER/include" -I"$stage/include" \
    "$WRAPPER/src/watermark_imaging.cpp" "$stage/lib/libraw_r.a" \
    "$stage/lib/libtiff.a" -lz -lm \
    -Wl,--no-undefined -Wl,-soname,libWatermark.Imaging.Native.so \
    -o "$ROOT/native/artifacts/android/$abi/libWatermark.Imaging.Native.so"
}

build_abi arm64-v8a aarch64-linux-android aarch64-linux-android aarch64-linux-android
build_abi x86_64 x86_64-linux-android x86_64-linux-android x86_64-linux-android

"$ROOT/native/scripts/update-native-manifest.sh"
echo "Android LibRaw and Watermark.Imaging.Native artifacts are ready under native/artifacts/android."
