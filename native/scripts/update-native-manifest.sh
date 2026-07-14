#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ARTIFACTS="$ROOT/native/artifacts"
MANIFEST="$ARTIFACTS/manifest.json"
ANDROID_NDK_VERSION="${WMI_ANDROID_NDK_VERSION:-26.1.10909125}"
ANDROID_API="${ANDROID_API:-24}"

artifact_entries=()

append_artifact() {
  local key="$1" relative_path="$2" architectures="$3" extra_fields="${4:-}"
  local binary="$ARTIFACTS/$relative_path"
  local sha entry

  [[ -f "$binary" ]] || return 0

  sha="$(shasum -a 256 "$binary" | awk '{print $1}')"
  entry="    \"$key\": { \"path\": \"$relative_path\", \"architectures\": $architectures"
  if [[ -n "$extra_fields" ]]; then
    entry="$entry, $extra_fields"
  fi
  artifact_entries+=("$entry, \"binarySha256\": \"$sha\" }")
}

append_artifact \
  "maccatalyst" \
  "maccatalyst/Watermark.Imaging.Native.xcframework/ios-arm64_x86_64-maccatalyst/libWatermark.Imaging.Native.a" \
  '["arm64", "x86_64"]'
append_artifact \
  "iosDevice" \
  "ios/Watermark.Imaging.Native.xcframework/ios-arm64/libWatermark.Imaging.Native-device.a" \
  '["arm64"]'
append_artifact \
  "iosSimulator" \
  "ios/Watermark.Imaging.Native.xcframework/ios-arm64_x86_64-simulator/libWatermark.Imaging.Native-simulator.a" \
  '["arm64", "x86_64"]'
append_artifact \
  "androidArm64V8a" \
  "android/arm64-v8a/libWatermark.Imaging.Native.so" \
  '["arm64-v8a"]' \
  "\"minimumApi\": $ANDROID_API"
append_artifact \
  "androidX86_64" \
  "android/x86_64/libWatermark.Imaging.Native.so" \
  '["x86_64"]' \
  "\"minimumApi\": $ANDROID_API"
append_artifact \
  "windowsX64" \
  "win-x64/Watermark.Imaging.Native.dll" \
  '["x64"]'
append_artifact \
  "windowsArm64" \
  "win-arm64/Watermark.Imaging.Native.dll" \
  '["arm64"]'

mkdir -p "$ARTIFACTS"
temporary_manifest="$(mktemp "$MANIFEST.tmp.XXXXXX")"
trap 'rm -f "$temporary_manifest"' EXIT

cat > "$temporary_manifest" <<EOF
{
  "schemaVersion": 2,
  "generatedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "nativeAbiVersion": 3,
  "source": {
    "name": "LibRaw",
    "version": "0.22.1",
    "url": "https://www.libraw.org/data/LibRaw-0.22.1.tar.gz",
    "sha256": "a789dc4e2409e2901d93793a4e0b80c7b49d0d97cf6ad71c850eb7616acfd786"
  },
  "tiffSource": {
    "name": "LibTIFF",
    "version": "4.7.2",
    "url": "https://download.osgeo.org/libtiff/tiff-4.7.2.tar.xz",
    "sha256": "4996f0c4f93094719b1ca5c6279b20e588773ba8a247533e486416fb662ddb88"
  },
  "toolchains": {
    "android": { "ndkVersion": "$ANDROID_NDK_VERSION", "minimumApi": $ANDROID_API }
  },
  "configuration": {
    "threadSafeArchive": true,
    "openMp": false,
    "lcms": false,
    "lossyDngJpeg": false,
    "zlib": {
      "apple": "system",
      "android": "system",
      "windows": "vcpkg-static"
    },
    "callocRawStore": true,
    "starAlignment": "LoG+centroid+local-triangle-buckets+deterministic-RANSAC",
    "previewPipeline": "WMPV1+Gray16+RGB16-bicubic+SIMD-stack",
    "openCv": false
  },
  "artifacts": {
EOF

for ((index = 0; index < ${#artifact_entries[@]}; index++)); do
  if ((index + 1 < ${#artifact_entries[@]})); then
    printf '%s,\n' "${artifact_entries[$index]}" >> "$temporary_manifest"
  else
    printf '%s\n' "${artifact_entries[$index]}" >> "$temporary_manifest"
  fi
done

cat >> "$temporary_manifest" <<'EOF'
  }
}
EOF

mv "$temporary_manifest" "$MANIFEST"
trap - EXIT
echo "Updated $MANIFEST"
