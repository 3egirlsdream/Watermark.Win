#!/usr/bin/env bash

# This file is sourced by the platform builders. The caller provides:
#   ROOT, WMI_OCIO_PLATFORM, WMI_CMAKE_PLATFORM_ARGS[], JOBS
set -euo pipefail

: "${ROOT:?ROOT is required}"
: "${WMI_OCIO_PLATFORM:?WMI_OCIO_PLATFORM is required}"
JOBS="${JOBS:-4}"
WMI_DEPENDENCY_JOBS="${WMI_DEPENDENCY_JOBS:-$JOBS}"

WMI_OCIO_STAGE="${WMI_OCIO_STAGE:-$ROOT/native/stage/ocio/$WMI_OCIO_PLATFORM}"
WMI_OCIO_BUILD_ROOT="${WMI_OCIO_BUILD_ROOT:-$ROOT/native/build/ocio/$WMI_OCIO_PLATFORM}"
WMI_SOURCE_ROOT="$ROOT/native/third_party"

wmi_remove_generated_tree() {
  local path="$1"
  case "$path" in
    "$ROOT/native/build/"*|"$ROOT/native/stage/"*) ;;
    *) echo "Refusing to remove non-generated path: $path" >&2; return 1 ;;
  esac
  for attempt in 1 2 3; do
    rm -rf -- "$path" 2>/dev/null || true
    [[ ! -e "$path" ]] && return 0
    sleep 0.2
  done
  echo "Unable to clear generated directory after 3 attempts: $path" >&2
  return 1
}

cmake -DWMI_ROOT="$ROOT" -DWMI_EXTRACT=ON \
  -P "$ROOT/native/cmake/PrepareLockedDependencies.cmake"

wmi_remove_generated_tree "$WMI_OCIO_STAGE"
wmi_remove_generated_tree "$WMI_OCIO_BUILD_ROOT"
mkdir -p "$WMI_OCIO_STAGE" "$WMI_OCIO_BUILD_ROOT"

wmi_cmake_install() {
  local name="$1" source="$2"
  shift 2
  local build="$WMI_OCIO_BUILD_ROOT/$name"
  local build_jobs="$WMI_DEPENDENCY_JOBS"
  # yaml-cpp's generated dependency files race under the Android NDK Makefile
  # generator. Keep only that target serial; OCIO itself is safe to parallelize.
  if [[ "$WMI_OCIO_PLATFORM" == android-* ]]; then
    build_jobs="$JOBS"
    [[ "$name" == "yaml-cpp" ]] && build_jobs=1
  fi
  cmake -S "$source" -B "$build" \
    "${WMI_CMAKE_PLATFORM_ARGS[@]}" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
    -DCMAKE_INSTALL_PREFIX="$WMI_OCIO_STAGE" \
    -DCMAKE_INSTALL_LIBDIR=lib \
    -DCMAKE_PREFIX_PATH="$WMI_OCIO_STAGE" \
    -DBUILD_SHARED_LIBS=OFF \
    -DBUILD_TESTING=OFF \
    "$@"
  cmake --build "$build" --config Release --parallel "$build_jobs"
  cmake --install "$build" --config Release
}

wmi_cmake_install expat "$WMI_SOURCE_ROOT/libexpat-R_2_7_2/expat" \
  -DEXPAT_BUILD_DOCS=OFF -DEXPAT_BUILD_EXAMPLES=OFF -DEXPAT_BUILD_TESTS=OFF \
  -DEXPAT_BUILD_TOOLS=OFF -DEXPAT_SHARED_LIBS=OFF
wmi_cmake_install yaml-cpp "$WMI_SOURCE_ROOT/yaml-cpp-0.8.0" \
  -DYAML_CPP_BUILD_TESTS=OFF -DYAML_CPP_BUILD_TOOLS=OFF \
  -DYAML_CPP_BUILD_CONTRIB=OFF -DYAML_BUILD_SHARED_LIBS=OFF
wmi_cmake_install Imath "$WMI_SOURCE_ROOT/Imath-3.2.1" \
  -DIMATH_BUILD_TESTS=OFF -DIMATH_BUILD_EXAMPLES=OFF -DIMATH_BUILD_PYTHON=OFF
wmi_cmake_install pystring "$ROOT/native/cmake/pystring-static" \
  -DPYSTRING_SOURCE_DIR="$WMI_SOURCE_ROOT/pystring-1.1.4"
wmi_cmake_install zlib "$WMI_SOURCE_ROOT/zlib-1.3.2" \
  -DZLIB_BUILD_SHARED=OFF -DZLIB_BUILD_STATIC=ON -DZLIB_BUILD_TESTING=OFF
wmi_cmake_install minizip-ng "$WMI_SOURCE_ROOT/minizip-ng-4.0.10" \
  -DZLIB_ROOT="$WMI_OCIO_STAGE" \
  -DZLIB_LIBRARY="$WMI_OCIO_STAGE/lib/libz.a" \
  -DZLIB_INCLUDE_DIR="$WMI_OCIO_STAGE/include" \
  -DMZ_FETCH_LIBS=OFF -DMZ_FORCE_FETCH_LIBS=OFF \
  -DMZ_BZIP2=OFF -DMZ_LZMA=OFF -DMZ_ZSTD=OFF -DMZ_OPENSSL=OFF \
  -DMZ_LIBBSD=OFF -DMZ_LIBCOMP=OFF -DMZ_ICONV=OFF \
  -DMZ_PKCRYPT=OFF -DMZ_WZAES=OFF -DMZ_COMPAT=OFF \
  -DMZ_BUILD_TESTS=OFF -DMZ_BUILD_UNIT_TESTS=OFF -DMZ_BUILD_FUZZ_TESTS=OFF

mkdir -p "$WMI_OCIO_STAGE/include/sse2neon"
cmake -E copy_if_different \
  "$WMI_SOURCE_ROOT/sse2neon-227cc413fb2d50b2a10073087be96b59d5364aea/sse2neon.h" \
  "$WMI_OCIO_STAGE/include/sse2neon/sse2neon.h"

wmi_cmake_install OpenColorIO "$WMI_SOURCE_ROOT/OpenColorIO-2.5.2" \
  -DOCIO_INSTALL_EXT_PACKAGES=NONE \
  -DOCIO_BUILD_APPS=OFF -DOCIO_BUILD_PYTHON=OFF -DOCIO_BUILD_JAVA=OFF \
  -DOCIO_BUILD_DOCS=OFF -DOCIO_BUILD_TESTS=OFF -DOCIO_BUILD_GPU_TESTS=OFF \
  -DOCIO_BUILD_OPENFX=OFF -DOCIO_BUILD_NUKE=OFF \
  -DOCIO_USE_OIIO_FOR_APPS=OFF -DOCIO_WARNING_AS_ERROR=OFF \
  -Dexpat_DIR="$WMI_OCIO_STAGE/lib/cmake/expat-2.7.2" \
  -Dexpat_LIBRARY="$WMI_OCIO_STAGE/lib/libexpat.a" \
  -Dexpat_INCLUDE_DIR="$WMI_OCIO_STAGE/include" \
  -Dyaml-cpp_DIR="$WMI_OCIO_STAGE/lib/cmake/yaml-cpp" \
  -Dyaml-cpp_LIBRARY="$WMI_OCIO_STAGE/lib/libyaml-cpp.a" \
  -Dyaml-cpp_INCLUDE_DIR="$WMI_OCIO_STAGE/include" \
  -Dpystring_LIBRARY="$WMI_OCIO_STAGE/lib/libpystring.a" \
  -Dpystring_INCLUDE_DIR="$WMI_OCIO_STAGE/include" \
  -DImath_DIR="$WMI_OCIO_STAGE/lib/cmake/Imath" \
  -DImath_LIBRARY="$WMI_OCIO_STAGE/lib/libImath-3_2.a" \
  -DZLIB_ROOT="$WMI_OCIO_STAGE" \
  -DZLIB_LIBRARY="$WMI_OCIO_STAGE/lib/libz.a" \
  -DZLIB_INCLUDE_DIR="$WMI_OCIO_STAGE/include" \
  -Dminizip-ng_DIR="$WMI_OCIO_STAGE/lib/cmake/minizip-ng" \
  -Dminizip-ng_LIBRARY="$WMI_OCIO_STAGE/lib/libminizip-ng.a" \
  -Dminizip-ng_INCLUDE_DIR="$WMI_OCIO_STAGE/include" \
  -Dminizip-ng_STATIC_LIBRARY=ON \
  -Dsse2neon_ROOT="$WMI_OCIO_STAGE/include/sse2neon"

if [[ ! -f "$WMI_OCIO_STAGE/lib/cmake/OpenColorIO/OpenColorIOConfig.cmake" ]]; then
  echo "OpenColorIO 2.5.2 static package was not installed under $WMI_OCIO_STAGE." >&2
  exit 1
fi
export WMI_OCIO_STAGE
