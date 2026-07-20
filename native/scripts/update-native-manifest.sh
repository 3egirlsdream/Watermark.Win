#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ARTIFACTS="$ROOT/native/artifacts"
MANIFEST="$ARTIFACTS/manifest.json"
ANDROID_NDK_VERSION="${WMI_ANDROID_NDK_VERSION:-26.1.10909125}"
ANDROID_API="${ANDROID_API:-24}"
SOURCE_ABI=4

existing_abi() {
  local key="$1" fallback="${2:-3}" line value
  [[ -f "$MANIFEST" ]] || { printf '%s' "$fallback"; return; }
  line="$(grep -E "^[[:space:]]*\"$key\"[[:space:]]*:" "$MANIFEST" || true)"
  value="$(printf '%s' "$line" | sed -nE 's/.*"nativeAbiVersion"[[:space:]]*:[[:space:]]*([0-9]+).*/\1/p')"
  printf '%s' "${value:-$fallback}"
}

APPLE_ABI="${WMI_APPLE_ARTIFACT_ABI:-$(existing_abi maccatalyst)}"
ANDROID_ABI="${WMI_ANDROID_ARTIFACT_ABI:-$(existing_abi androidArm64V8a)}"
WINDOWS_X64_ABI="${WMI_WINDOWS_X64_ARTIFACT_ABI:-${WMI_WINDOWS_ARTIFACT_ABI:-$(existing_abi windowsX64)}}"
WINDOWS_ARM64_ABI="${WMI_WINDOWS_ARM64_ARTIFACT_ABI:-${WMI_WINDOWS_ARTIFACT_ABI:-$(existing_abi windowsArm64)}}"
ARTIFACT_SET_ABI="$APPLE_ABI"
for platform_abi in "$ANDROID_ABI" "$WINDOWS_X64_ABI" "$WINDOWS_ARM64_ABI"; do
  if ((platform_abi < ARTIFACT_SET_ABI)); then ARTIFACT_SET_ABI="$platform_abi"; fi
done
if [[ "$APPLE_ABI" == "$SOURCE_ABI" && "$ANDROID_ABI" == "$SOURCE_ABI"
      && "$WINDOWS_X64_ABI" == "$SOURCE_ABI" && "$WINDOWS_ARM64_ABI" == "$SOURCE_ABI" ]]; then
  ARTIFACT_SET_READY=true
else
  ARTIFACT_SET_READY=false
fi
LOCK_SHA="$(shasum -a 256 "$ROOT/native/dependencies.lock.json" | awk '{print $1}')"

artifact_entries=()

append_artifact() {
  local key="$1" relative_path="$2" architectures="$3" artifact_abi="$4" extra_fields="${5:-}"
  local binary="$ARTIFACTS/$relative_path"
  local sha entry

  [[ -f "$binary" ]] || return 0

  sha="$(shasum -a 256 "$binary" | awk '{print $1}')"
  entry="    \"$key\": { \"path\": \"$relative_path\", \"architectures\": $architectures"
  if [[ -n "$extra_fields" ]]; then
    entry="$entry, $extra_fields"
  fi
  artifact_entries+=("$entry, \"nativeAbiVersion\": $artifact_abi, \"binarySha256\": \"$sha\" }")
}

append_artifact \
  "maccatalyst" \
  "maccatalyst/Watermark.Imaging.Native.xcframework/ios-arm64_x86_64-maccatalyst/libWatermark.Imaging.Native.a" \
  '["arm64", "x86_64"]' \
  "$APPLE_ABI"
append_artifact \
  "iosDevice" \
  "ios/Watermark.Imaging.Native.xcframework/ios-arm64/libWatermark.Imaging.Native-device.a" \
  '["arm64"]' \
  "$APPLE_ABI"
append_artifact \
  "iosSimulator" \
  "ios/Watermark.Imaging.Native.xcframework/ios-arm64_x86_64-simulator/libWatermark.Imaging.Native-simulator.a" \
  '["arm64", "x86_64"]' \
  "$APPLE_ABI"
append_artifact \
  "androidArm64V8a" \
  "android/arm64-v8a/libWatermark.Imaging.Native.so" \
  '["arm64-v8a"]' \
  "$ANDROID_ABI" \
  "\"minimumApi\": $ANDROID_API"
append_artifact \
  "androidX86_64" \
  "android/x86_64/libWatermark.Imaging.Native.so" \
  '["x86_64"]' \
  "$ANDROID_ABI" \
  "\"minimumApi\": $ANDROID_API"
append_artifact \
  "windowsX64" \
  "win-x64/Watermark.Imaging.Native.dll" \
  '["x64"]' \
  "$WINDOWS_X64_ABI"
append_artifact \
  "windowsArm64" \
  "win-arm64/Watermark.Imaging.Native.dll" \
  '["arm64"]' \
  "$WINDOWS_ARM64_ABI"

mkdir -p "$ARTIFACTS"
temporary_manifest="$(mktemp "$MANIFEST.tmp.XXXXXX")"
trap 'rm -f "$temporary_manifest"' EXIT

cat > "$temporary_manifest" <<EOF
{
  "schemaVersion": 3,
  "generatedAtUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "nativeAbiVersion": $SOURCE_ABI,
  "artifactSetAbiVersion": $ARTIFACT_SET_ABI,
  "artifactSetReady": $ARTIFACT_SET_READY,
  "dependencyLockSha256": "$LOCK_SHA",
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
  "colorEngine": {
    "name": "OpenColorIO",
    "version": "2.5.2",
    "sha256": "722601e01b78b7a12da4829cb450674935f404b0e508f3f20046fa77570e3272",
    "capability": "WMI_CAP_COLOR_OCIO",
    "dependencies": {
      "Expat": { "version": "2.7.2", "sha256": "d09e2dd23398805cec1bac2860304714c96dc2fde629a7a1a77d0880ab7cd242" },
      "yaml-cpp": { "version": "0.8.0", "sha256": "fbe74bbdcee21d656715688706da3c8becfd946d92cd44705cc6098bb23b3a16" },
      "Imath": { "version": "3.2.1", "sha256": "b2c8a44c3e4695b74e9644c76f5f5480767355c6f98cde58ba0e82b4ad8c63ce" },
      "pystring": { "version": "1.1.4", "sha256": "49da0fe2a049340d3c45cce530df63a2278af936003642330287b68cefd788fb" },
      "minizip-ng": { "version": "4.0.10", "sha256": "c362e35ee973fa7be58cc5e38a4a6c23cc8f7e652555daf4f115a9eb2d3a6be7" },
      "zlib": { "version": "1.3.2", "sha256": "bb329a0a2cd0274d05519d61c667c062e06990d72e125ee2dfa8de64f0119d16" },
      "sse2neon": { "version": "227cc413fb2d50b2a10073087be96b59d5364aea", "sha256": "3427a495743bb6fd1b5f9f806b80f57d67b1ac7ccf39a5f44aedd487fd7e6da1" }
    }
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
      "apple": "pinned-static-1.3.2",
      "android": "pinned-static-1.3.2",
      "windows": "pinned-static-1.3.2"
    },
    "callocRawStore": true,
    "starAlignment": "LoG+centroid+local-triangle-buckets+deterministic-RANSAC",
    "previewPipeline": "WMPV1+Gray16+RGB16-bicubic+SIMD-stack",
    "openCv": false,
    "openColorIO": {
      "buildSharedLibs": false,
      "installExtPackages": "NONE",
      "apps": false,
      "python": false,
      "java": false,
      "docs": false,
      "tests": false,
      "gpuTests": false,
      "openFx": false
    }
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
