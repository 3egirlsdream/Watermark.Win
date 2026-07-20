[CmdletBinding()]
param(
    [ValidateSet("auto", "amd64", "arm64")]
    [string]$HostArch = "auto",

    [switch]$NoPause,

    [switch]$SkipTests,

    [switch]$Clean
)

$ErrorActionPreference = "Stop"

# OpenColorIO is linked statically into Watermark.Imaging.Native.dll. Keep one
# Windows build implementation so the OCIO and LibRaw entry points cannot drift.
$Builder = Join-Path $PSScriptRoot "build-libraw-windows-all.ps1"
if (-not (Test-Path -LiteralPath $Builder)) {
    throw "Windows native builder was not found: $Builder"
}

$Arguments = @(
    "-HostArch", $HostArch
)
if ($NoPause) { $Arguments += "-NoPause" }
if ($SkipTests) { $Arguments += "-SkipTests" }
if ($Clean) { $Arguments += "-Clean" }

Write-Host "OpenColorIO Windows build: rebuilding the ABI 4 native wrapper for x64 and ARM64."
& $Builder @Arguments
exit $LASTEXITCODE
