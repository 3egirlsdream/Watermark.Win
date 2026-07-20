param(
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64",
    [switch]$SkipTests,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$Source = Join-Path $Root "native/third_party/LibRaw-0.22.1"
$TiffSource = Join-Path $Root "native/third_party/tiff-4.7.2"
$OcioSourceRoot = Join-Path $Root "native/third_party"
$OcioStage = Join-Path $Root "native/stage/ocio/windows-$Arch"
$OcioBuildRoot = Join-Path $Root "native/build/ocio/windows-$Arch"
$LibRawBuild = Join-Path $Root "native/build/libraw/windows-$Arch"
$LibRawStage = Join-Path $Root "native/stage/libraw/windows-$Arch"
$TiffBuild = Join-Path $Root "native/build/libtiff/windows-$Arch"
$TiffStage = Join-Path $Root "native/stage/libtiff/windows-$Arch"
$WrapperBuild = Join-Path $Root "native/build/wrapper/windows-$Arch"
$Artifact = Join-Path $Root "native/artifacts/win-$Arch"
$GeneratorArch = if ($Arch -eq "arm64") { "ARM64" } else { "x64" }
$MsvcReleaseFlags = "/MT /O2 /Ob2 /DNDEBUG"

function Assert-Succeeded([string]$Operation) {
    if ($LASTEXITCODE -ne 0) { throw "$Operation failed with exit code $LASTEXITCODE." }
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

cmake "-DWMI_ROOT=$Root" -DWMI_EXTRACT=ON -P (Join-Path $Root "native/cmake/PrepareLockedDependencies.cmake")
Assert-Succeeded "Locked dependency verification"

if ($Clean) {
    Remove-Item $OcioStage, $OcioBuildRoot, $LibRawBuild, $LibRawStage, $TiffBuild, $TiffStage, $WrapperBuild `
        -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item $OcioStage, $OcioBuildRoot, $LibRawBuild, (Join-Path $LibRawStage "include"), `
    (Join-Path $LibRawStage "lib"), (Join-Path $OcioStage "include/sse2neon"), `
    $Artifact -ItemType Directory -Force | Out-Null

Copy-Item (Join-Path $OcioSourceRoot "sse2neon-227cc413fb2d50b2a10073087be96b59d5364aea/sse2neon.h") `
    (Join-Path $OcioStage "include/sse2neon/sse2neon.h") -Force

$Common = @(
    "-A", $GeneratorArch,
    "-DCMAKE_BUILD_TYPE=Release",
    "-DCMAKE_POLICY_VERSION_MINIMUM=3.5",
    "-DCMAKE_INSTALL_PREFIX=$OcioStage",
    "-DCMAKE_INSTALL_LIBDIR=lib",
    "-DCMAKE_PREFIX_PATH=$OcioStage",
    "-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded",
    "-DCMAKE_C_FLAGS_RELEASE=$MsvcReleaseFlags",
    "-DCMAKE_CXX_FLAGS_RELEASE=$MsvcReleaseFlags",
    "-DBUILD_SHARED_LIBS=OFF",
    "-DBUILD_TESTING=OFF"
)

function Install-CMakeDependency([string]$Name, [string]$SourcePath, [string[]]$Options) {
    $BuildPath = Join-Path $OcioBuildRoot $Name
    & cmake -S $SourcePath -B $BuildPath @Common @Options
    Assert-Succeeded "$Name configure"
    & cmake --build $BuildPath --config Release --parallel
    Assert-Succeeded "$Name build"
    & cmake --install $BuildPath --config Release
    Assert-Succeeded "$Name install"
}

Install-CMakeDependency "expat" (Join-Path $OcioSourceRoot "libexpat-R_2_7_2/expat") @(
    "-DEXPAT_BUILD_DOCS=OFF", "-DEXPAT_BUILD_EXAMPLES=OFF", "-DEXPAT_BUILD_TESTS=OFF",
    "-DEXPAT_BUILD_TOOLS=OFF", "-DEXPAT_SHARED_LIBS=OFF", "-DEXPAT_MSVC_STATIC_CRT=ON")
Install-CMakeDependency "yaml-cpp" (Join-Path $OcioSourceRoot "yaml-cpp-0.8.0") @(
    "-DYAML_CPP_BUILD_TESTS=OFF", "-DYAML_CPP_BUILD_TOOLS=OFF",
    "-DYAML_CPP_BUILD_CONTRIB=OFF", "-DYAML_BUILD_SHARED_LIBS=OFF")
Install-CMakeDependency "Imath" (Join-Path $OcioSourceRoot "Imath-3.2.1") @(
    "-DIMATH_BUILD_TESTS=OFF", "-DIMATH_BUILD_EXAMPLES=OFF", "-DIMATH_BUILD_PYTHON=OFF")
Install-CMakeDependency "pystring" (Join-Path $Root "native/cmake/pystring-static") @(
    "-DPYSTRING_SOURCE_DIR=$(Join-Path $OcioSourceRoot 'pystring-1.1.4')")
Install-CMakeDependency "zlib" (Join-Path $OcioSourceRoot "zlib-1.3.2") @(
    "-DZLIB_BUILD_SHARED=OFF", "-DZLIB_BUILD_STATIC=ON", "-DZLIB_BUILD_TESTING=OFF")
Install-CMakeDependency "minizip-ng" (Join-Path $OcioSourceRoot "minizip-ng-4.0.10") @(
    "-DZLIB_ROOT=$OcioStage", "-DZLIB_DIR=$(Join-Path $OcioStage 'lib/cmake/zlib')",
    "-DMZ_FETCH_LIBS=OFF", "-DMZ_FORCE_FETCH_LIBS=OFF", "-DMZ_BZIP2=OFF", "-DMZ_LZMA=OFF",
    "-DMZ_ZSTD=OFF", "-DMZ_OPENSSL=OFF", "-DMZ_LIBBSD=OFF", "-DMZ_ICONV=OFF",
    "-DMZ_PKCRYPT=OFF", "-DMZ_WZAES=OFF", "-DMZ_COMPAT=OFF", "-DMZ_BUILD_TESTS=OFF",
    "-DMZ_BUILD_UNIT_TESTS=OFF", "-DMZ_BUILD_FUZZ_TESTS=OFF")
Install-CMakeDependency "OpenColorIO" (Join-Path $OcioSourceRoot "OpenColorIO-2.5.2") @(
    "-DOCIO_INSTALL_EXT_PACKAGES=NONE", "-DOCIO_BUILD_APPS=OFF", "-DOCIO_BUILD_PYTHON=OFF",
    "-DOCIO_BUILD_JAVA=OFF", "-DOCIO_BUILD_DOCS=OFF", "-DOCIO_BUILD_TESTS=OFF",
    "-DOCIO_BUILD_GPU_TESTS=OFF", "-DOCIO_BUILD_OPENFX=OFF", "-DOCIO_BUILD_NUKE=OFF",
    "-Dexpat_DIR=$(Join-Path $OcioStage 'lib/cmake/expat-2.7.2')",
    "-Dyaml-cpp_DIR=$(Join-Path $OcioStage 'lib/cmake/yaml-cpp')",
    "-Dpystring_LIBRARY=$(Join-Path $OcioStage 'lib/pystring.lib')",
    "-Dpystring_INCLUDE_DIR=$(Join-Path $OcioStage 'include')",
    "-DImath_DIR=$(Join-Path $OcioStage 'lib/cmake/Imath')",
    "-DZLIB_ROOT=$OcioStage", "-DZLIB_DIR=$(Join-Path $OcioStage 'lib/cmake/zlib')",
    "-DZLIB_USE_STATIC_LIBS=ON",
    "-Dminizip-ng_DIR=$(Join-Path $OcioStage 'lib/cmake/minizip-ng')",
    "-Dminizip-ng_STATIC_LIBRARY=ON",
    "-Dsse2neon_ROOT=$(Join-Path $OcioStage 'include/sse2neon')")

Copy-Item (Join-Path $Source "*") $LibRawBuild -Recurse -Force
try {
    Push-Location $LibRawBuild
    nmake.exe /f Makefile.msvc "COPT=/EHsc /MP /MT /I. /DWIN32 /O2 /W0 /nologo" "lib\libraw_static.lib"
    Assert-Succeeded "LibRaw $Arch build"
}
finally { Pop-Location }
Copy-Item (Join-Path $Source "libraw") (Join-Path $LibRawStage "include/libraw") -Recurse -Force
Copy-Item (Join-Path $LibRawBuild "lib/libraw_static.lib") (Join-Path $LibRawStage "lib/libraw_static.lib") -Force

cmake -S $TiffSource -B $TiffBuild -A $GeneratorArch `
    "-DCMAKE_INSTALL_PREFIX=$TiffStage" "-DCMAKE_PREFIX_PATH=$OcioStage" `
    "-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded" "-DZLIB_ROOT=$OcioStage" `
    "-DCMAKE_C_FLAGS_RELEASE=$MsvcReleaseFlags" "-DCMAKE_CXX_FLAGS_RELEASE=$MsvcReleaseFlags" `
    "-DZLIB_DIR=$(Join-Path $OcioStage 'lib/cmake/zlib')" -DZLIB_USE_STATIC_LIBS=ON `
    -DBUILD_SHARED_LIBS=OFF -Dtiff-static=ON -Dtiff-cxx=OFF -Dtiff-tools=OFF -Dtiff-tests=OFF `
    -Dtiff-contrib=OFF -Dtiff-docs=OFF -Djpeg=OFF -Djbig=OFF -Dlzma=OFF -Dzstd=OFF `
    -Dwebp=OFF -Dlerc=OFF -Dlibdeflate=OFF -Dzlib=ON
Assert-Succeeded "LibTIFF $Arch configure"
cmake --build $TiffBuild --config Release --parallel
Assert-Succeeded "LibTIFF $Arch build"
cmake --install $TiffBuild --config Release
Assert-Succeeded "LibTIFF $Arch install"

$BuildTesting = if ($SkipTests) { "OFF" } else { "ON" }
cmake -S (Join-Path $Root "native/Watermark.Imaging.Native") -B $WrapperBuild -A $GeneratorArch `
    "-DWMI_LIBRAW_ROOT=$LibRawStage" -DWMI_ENABLE_LIBRAW=ON -DWMI_ENABLE_TIFF=ON `
    -DWMI_ENABLE_OCIO=ON -DWMI_LIBRARY_TYPE=SHARED "-DWMI_OCIO_ROOT=$OcioStage" `
    "-DCMAKE_PREFIX_PATH=$TiffStage;$OcioStage" "-DZLIB_ROOT=$OcioStage" `
    "-DZLIB_DIR=$(Join-Path $OcioStage 'lib/cmake/zlib')" -DZLIB_USE_STATIC_LIBS=ON `
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
    "-DCMAKE_C_FLAGS_RELEASE=$MsvcReleaseFlags" "-DCMAKE_CXX_FLAGS_RELEASE=$MsvcReleaseFlags" `
    "-DBUILD_TESTING=$BuildTesting"
Assert-Succeeded "Watermark.Imaging.Native $Arch configure"
cmake --build $WrapperBuild --config Release --parallel
Assert-Succeeded "Watermark.Imaging.Native $Arch build"
if (-not $SkipTests) {
    ctest --test-dir $WrapperBuild -C Release --output-on-failure
    Assert-Succeeded "Watermark.Imaging.Native $Arch tests"
}
Copy-Item (Join-Path $WrapperBuild "Release/Watermark.Imaging.Native.dll") $Artifact -Force
& (Join-Path $Root "native/scripts/update-native-manifest.ps1") -Arch $Arch
Write-Host "Windows $Arch ABI 4 artifact is ready under native/artifacts/win-$Arch."
