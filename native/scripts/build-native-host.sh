#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WMI_OCIO_PLATFORM="host"
JOBS="${JOBS:-$(sysctl -n hw.logicalcpu 2>/dev/null || echo 4)}"
WMI_CMAKE_PLATFORM_ARGS=(-DCMAKE_POSITION_INDEPENDENT_CODE=ON)
source "$ROOT/native/scripts/lib/build-ocio-static.sh"

BUILD="$ROOT/native/build/wrapper/host-ocio"
cmake -S "$ROOT/native/Watermark.Imaging.Native" -B "$BUILD" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_PREFIX_PATH="$WMI_OCIO_STAGE" \
  -DWMI_OCIO_ROOT="$WMI_OCIO_STAGE" \
  -DWMI_ENABLE_OCIO=ON -DWMI_ENABLE_LIBRAW=OFF -DWMI_ENABLE_TIFF=OFF \
  -DWMI_LIBRARY_TYPE=SHARED -DBUILD_TESTING=ON
cmake --build "$BUILD" --parallel "$JOBS"
ctest --test-dir "$BUILD" --output-on-failure
