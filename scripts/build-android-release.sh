#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/Watermark.Andorid/Watermark.Andorid.csproj"
APK="$ROOT/Watermark.Andorid/bin/Release/net8.0-android/com.top.thankful.watermark.andorid-Signed.apk"
SIGNING_PROPS="$ROOT/Watermark.Andorid/Signing/AndroidSigning.props"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "未找到 dotnet。请先安装 .NET 8 SDK 与 Android MAUI 工作负载。" >&2
  exit 1
fi

if [[ ! -f "$PROJECT" ]]; then
  echo "未找到 Android 项目：$PROJECT" >&2
  exit 1
fi

if [[ ! -f "$SIGNING_PROPS" ]]; then
  echo "未找到 Android 发布签名配置：$SIGNING_PROPS" >&2
  echo "请根据 Watermark.Andorid/Signing/AndroidSigning.props.example 创建本机配置。" >&2
  exit 1
fi

cd "$ROOT"

echo "还原 Android Release 依赖…"
dotnet restore "$PROJECT"

echo "完整重建并打包 Android Release APK…"
# Rebuild clears stale Android manifest/package intermediates. Single-process
# execution avoids concurrent MSBuild access to Watermark.Shared.deps.json.
dotnet msbuild "$PROJECT" '-t:Rebuild;SignAndroidPackage' \
  -p:Configuration=Release \
  -p:TargetFramework=net8.0-android \
  -p:Platform=AnyCPU \
  -p:AndroidPackageFormat=apk \
  -p:AndroidPackageFormats=apk \
  -m:1 \
  -nr:false

if [[ ! -f "$APK" ]]; then
  echo "打包完成但未找到预期 APK：$APK" >&2
  exit 1
fi

echo
echo "Android Release APK 已生成："
echo "$APK"
