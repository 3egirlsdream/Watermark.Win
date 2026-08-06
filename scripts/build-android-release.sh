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

for required_tool in unzip cmp sed; do
  if ! command -v "$required_tool" >/dev/null 2>&1; then
    echo "未找到 $required_tool，无法校验 Android 包内静态资源。" >&2
    exit 1
  fi
done

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

MASA_VERSION="$(sed -n 's/.*<PackageReference Include="Masa.Blazor" Version="\([^"]*\)".*/\1/p' \
  "$ROOT/Watermark.Razor/Watermark.Razor.csproj")"
if [[ -z "$MASA_VERSION" ]]; then
  echo "无法从 Watermark.Razor.csproj 读取 Masa.Blazor 版本。" >&2
  exit 1
fi

NUGET_GLOBAL_PACKAGES="${NUGET_PACKAGES:-$(dotnet nuget locals global-packages --list | sed 's/^[^:]*: *//')}"
NUGET_GLOBAL_PACKAGES="${NUGET_GLOBAL_PACKAGES%/}"
MASA_ASSET_ROOT="$NUGET_GLOBAL_PACKAGES/masa.blazor/$MASA_VERSION/staticwebassets"

verify_masa_asset() {
  local apk_asset="$1"
  local package_asset="$2"

  if [[ ! -f "$package_asset" ]]; then
    echo "未找到 Masa.Blazor $MASA_VERSION NuGet 静态资源：$package_asset" >&2
    exit 1
  fi

  if ! unzip -p "$APK" "$apk_asset" | cmp -s - "$package_asset"; then
    echo "Android APK 内静态资源与 Masa.Blazor $MASA_VERSION 不一致：$apk_asset" >&2
    echo "请先清理 Release 中间产物后重新打包，禁止发布程序集与静态资源混用的 APK。" >&2
    exit 1
  fi
}

verify_masa_asset \
  "assets/wwwroot/_content/Masa.Blazor/css/masa-blazor.min.css" \
  "$MASA_ASSET_ROOT/css/masa-blazor.min.css"
verify_masa_asset \
  "assets/wwwroot/_content/Masa.Blazor/js/masa-blazor.js" \
  "$MASA_ASSET_ROOT/js/masa-blazor.js"

echo
echo "Android Release APK 已生成："
echo "$APK"
echo "Masa.Blazor $MASA_VERSION CSS/JS 已与 NuGet 包逐字节核对。"
