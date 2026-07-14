param(
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64",

    [string]$ZlibRoot = $env:ZLIB_ROOT,

    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$Source = Join-Path $Root "native/third_party/LibRaw-0.22.1"
$TiffSource = Join-Path $Root "native/third_party/tiff-4.7.2"
$Build = Join-Path $Root "native/build/libraw/windows-$Arch"
$TiffBuild = Join-Path $Root "native/build/libtiff/windows-$Arch"
$TiffStage = Join-Path $Root "native/stage/libtiff/windows-$Arch"
$Stage = Join-Path $Root "native/stage/libraw/windows-$Arch"
$WrapperBuild = Join-Path $Root "native/build/wrapper/windows-$Arch"
$Artifact = Join-Path $Root "native/artifacts/win-$Arch"

function Assert-NativeCommandSucceeded {
    param([Parameter(Mandatory = $true)][string]$Description)

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    throw "Run this script from a Visual Studio Developer PowerShell configured for $Arch."
}
if (-not (Get-Command nmake.exe -ErrorAction SilentlyContinue)) {
    throw "nmake.exe is unavailable. Install the Visual C++ build tools."
}
if (-not (Get-Command cmake.exe -ErrorAction SilentlyContinue)) {
    throw "cmake.exe is unavailable. Install the Visual Studio C++ CMake tools."
}
if (-not (Test-Path (Join-Path $Source "Makefile.msvc"))) {
    throw "LibRaw 0.22.1 source is missing: $Source"
}
if (-not (Test-Path (Join-Path $TiffSource "CMakeLists.txt"))) {
    throw "LibTIFF 4.7.2 source is missing: $TiffSource"
}
if ([string]::IsNullOrWhiteSpace($ZlibRoot)) {
    throw "ZlibRoot is required. Use a matching static vcpkg package such as x64-windows-static."
}
$ZlibRoot = (Resolve-Path $ZlibRoot).Path
if (-not (Test-Path (Join-Path $ZlibRoot "include/zlib.h"))) {
    throw "zlib.h was not found under $ZlibRoot."
}
if (-not (Get-ChildItem (Join-Path $ZlibRoot "lib") -Filter "*.lib" -File -ErrorAction SilentlyContinue)) {
    throw "A static zlib library was not found under $ZlibRoot/lib."
}
$VcpkgTriplet = Split-Path $ZlibRoot -Leaf
$VcpkgInstalledRoot = Split-Path $ZlibRoot -Parent
$VcpkgRoot = Split-Path $VcpkgInstalledRoot -Parent
$VcpkgToolchain = Join-Path $VcpkgRoot "scripts/buildsystems/vcpkg.cmake"
$ExpectedTriplet = if ($Arch -eq "arm64") { "arm64-windows-static" } else { "x64-windows-static" }
if ($VcpkgTriplet -ne $ExpectedTriplet) {
    throw "ZlibRoot must use vcpkg triplet $ExpectedTriplet; found $VcpkgTriplet."
}
if (-not (Test-Path $VcpkgToolchain)) {
    throw "The vcpkg CMake toolchain was not found: $VcpkgToolchain"
}

Remove-Item $Build, $TiffBuild, $TiffStage, $Stage, $WrapperBuild -Recurse -Force -ErrorAction SilentlyContinue
New-Item $Build, (Join-Path $Stage "include"), (Join-Path $Stage "lib"), $Artifact -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $Source "*") $Build -Recurse -Force

try {
    Push-Location $Build
    nmake.exe /f Makefile.msvc "COPT=/EHsc /MP /MT /I. /DWIN32 /O2 /W0 /nologo" "lib\libraw_static.lib"
    Assert-NativeCommandSucceeded "LibRaw $Arch build"
}
finally {
    Pop-Location
}

Copy-Item (Join-Path $Source "libraw") (Join-Path $Stage "include/libraw") -Recurse -Force
Copy-Item (Join-Path $Build "lib/libraw_static.lib") (Join-Path $Stage "lib/libraw_static.lib") -Force

$GeneratorArch = if ($Arch -eq "arm64") { "ARM64" } else { "x64" }
cmake -S $TiffSource -B $TiffBuild -A $GeneratorArch `
    "-DCMAKE_INSTALL_PREFIX=$TiffStage" -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
    "-DCMAKE_C_FLAGS_RELEASE=/MT /O2 /Ob2 /DNDEBUG" `
    "-DCMAKE_CXX_FLAGS_RELEASE=/MT /O2 /Ob2 /DNDEBUG" `
    "-DCMAKE_TOOLCHAIN_FILE=$VcpkgToolchain" "-DVCPKG_TARGET_TRIPLET=$VcpkgTriplet" `
    "-DZLIB_ROOT=$ZlibRoot" -DZLIB_USE_STATIC_LIBS=ON `
    -DBUILD_SHARED_LIBS=OFF -Dtiff-static=ON -Dtiff-cxx=OFF `
    -Dtiff-tools=OFF -Dtiff-tests=OFF -Dtiff-contrib=OFF -Dtiff-docs=OFF `
    -Djpeg=OFF -Djbig=OFF -Dlzma=OFF -Dzstd=OFF -Dwebp=OFF -Dlerc=OFF `
    -Dlibdeflate=OFF -Dzlib=ON
Assert-NativeCommandSucceeded "LibTIFF $Arch configure"
$TiffCache = Join-Path $TiffBuild "CMakeCache.txt"
$TiffRuntimeLine = (Select-String -Path $TiffCache -Pattern '^CMAKE_C_FLAGS_RELEASE:STRING=').Line
if ($TiffRuntimeLine -notmatch '/MT' -or $TiffRuntimeLine -match '/MD') {
    throw "LibTIFF $Arch runtime mismatch: $TiffRuntimeLine"
}
Write-Host "LibTIFF $Arch runtime: $TiffRuntimeLine"
cmake --build $TiffBuild --config Release
Assert-NativeCommandSucceeded "LibTIFF $Arch build"
cmake --install $TiffBuild --config Release
Assert-NativeCommandSucceeded "LibTIFF $Arch install"

$BuildTesting = if ($SkipTests) { "OFF" } else { "ON" }
cmake -S (Join-Path $Root "native/Watermark.Imaging.Native") -B $WrapperBuild -A $GeneratorArch `
    "-DWMI_LIBRAW_ROOT=$Stage" -DWMI_ENABLE_LIBRAW=ON `
    -DWMI_ENABLE_TIFF=ON -DWMI_LIBRARY_TYPE=SHARED `
    "-DCMAKE_TOOLCHAIN_FILE=$VcpkgToolchain" "-DVCPKG_TARGET_TRIPLET=$VcpkgTriplet" `
    "-DZLIB_ROOT=$ZlibRoot" -DZLIB_USE_STATIC_LIBS=ON `
    "-DCMAKE_PREFIX_PATH=$TiffStage;$ZlibRoot" -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
    "-DBUILD_TESTING=$BuildTesting"
Assert-NativeCommandSucceeded "Watermark.Imaging.Native $Arch configure"
cmake --build $WrapperBuild --config Release
Assert-NativeCommandSucceeded "Watermark.Imaging.Native $Arch build"
if (-not $SkipTests) {
    ctest --test-dir $WrapperBuild -C Release --output-on-failure
    Assert-NativeCommandSucceeded "Watermark.Imaging.Native $Arch tests"
}
else {
    Write-Host "Skipping $Arch native tests because this host cannot execute that architecture."
}
Copy-Item (Join-Path $WrapperBuild "Release/Watermark.Imaging.Native.dll") $Artifact -Force
Write-Host "Windows $Arch artifact is ready under native/artifacts/win-$Arch."
