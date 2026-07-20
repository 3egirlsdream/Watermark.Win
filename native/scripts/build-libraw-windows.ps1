param(
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64",
    [string]$ZlibRoot = $env:ZLIB_ROOT,
    [switch]$SkipTests
)

Write-Host "build-libraw-windows.ps1 is a compatibility entry; running build-native-windows.ps1."
$Arguments = @{ Arch = $Arch }
if ($SkipTests) { $Arguments.SkipTests = $true }
& (Join-Path $PSScriptRoot "build-native-windows.ps1") @Arguments
exit $LASTEXITCODE
