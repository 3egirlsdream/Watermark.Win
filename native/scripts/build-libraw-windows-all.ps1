[CmdletBinding()]
param(
    [string]$VcpkgRoot = "",

    [ValidateSet("auto", "amd64", "arm64")]
    [string]$HostArch = "auto",

    [switch]$NoPause,

    [Parameter(DontShow = $true)]
    [switch]$ChildBuild,

    [Parameter(DontShow = $true)]
    [ValidateSet("x64", "arm64")]
    [string]$Arch,

    [Parameter(DontShow = $true)]
    [string]$VsInstallPath,

    [Parameter(DontShow = $true)]
    [switch]$SkipTests,

    [Parameter(DontShow = $true)]
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-LastExitCode([string]$Operation) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Write-FailureDetails($ErrorRecord) {
    Write-Host ""
    Write-Host "Build failed: $($ErrorRecord.Exception.Message)" -ForegroundColor Red
    if ($ErrorRecord.InvocationInfo.PositionMessage) {
        Write-Host $ErrorRecord.InvocationInfo.PositionMessage -ForegroundColor DarkYellow
    }
    if ($ErrorRecord.ScriptStackTrace) {
        Write-Host "Stack trace:" -ForegroundColor DarkYellow
        Write-Host $ErrorRecord.ScriptStackTrace -ForegroundColor DarkYellow
    }
}

function Resolve-HostArchitecture {
    if ($HostArch -ne "auto") {
        return $HostArch
    }
    if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64" -or
        $env:PROCESSOR_ARCHITEW6432 -eq "ARM64") {
        return "arm64"
    }
    return "amd64"
}

function Find-VisualStudio {
    $programFilesRoots = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        $_.Trim().Trim('"')
    } | Select-Object -Unique

    $candidates = @($programFilesRoots | ForEach-Object {
        Join-Path $_ "Microsoft Visual Studio/Installer/vswhere.exe"
    } | Where-Object { Test-Path -LiteralPath $_ })

    if ($candidates.Count -eq 0) {
        throw "vswhere.exe was not found. Install Visual Studio 2026 with Desktop development with C++."
    }

    $vswhere = $candidates[0]
    $path = @(& $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Workload.NativeDesktop `
        -property installationPath)
    Assert-LastExitCode "Visual Studio discovery"
    if ($path.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string]$path[0])) {
        throw "Visual Studio with the Desktop development with C++ workload was not found."
    }
    return $path[0].Trim().Trim('"')
}

function Find-CMake([string]$VsInstallPath) {
    $candidates = @()
    $pathCommand = Get-Command cmake.exe -ErrorAction SilentlyContinue
    if ($pathCommand) {
        $candidates += $pathCommand.Source
    }
    if ($VsInstallPath) {
        $candidates += Join-Path $VsInstallPath "Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"
    }
    $programFilesRoots = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        $_.Trim().Trim('"')
    } | Select-Object -Unique
    $candidates += $programFilesRoots | ForEach-Object {
        Join-Path $_ "CMake/bin/cmake.exe"
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw "cmake.exe was not found. Install CMake or the Visual Studio C++ CMake tools."
}

function Find-Dumpbin([string]$VsInstallPath, [string]$TargetArch, [string]$HostArchitecture) {
    $msvcRoot = Join-Path $VsInstallPath "VC/Tools/MSVC"
    if (-not (Test-Path -LiteralPath $msvcRoot)) {
        throw "Visual C++ tools directory was not found: $msvcRoot"
    }

    $targetDirectory = if ($TargetArch -eq "x64") { "x64" } else { "arm64" }
    $hostDirectory = if ($HostArchitecture -eq "arm64") { "HostARM64" } else { "Hostx64" }
    $preferred = @(Get-ChildItem -LiteralPath $msvcRoot -Filter dumpbin.exe -File -Recurse |
        Where-Object { $_.FullName -match "\\$hostDirectory\\$targetDirectory\\dumpbin\.exe$" } |
        Sort-Object FullName -Descending)
    if ($preferred.Count -gt 0) {
        return $preferred[0].FullName
    }

    $fallback = @(Get-ChildItem -LiteralPath $msvcRoot -Filter dumpbin.exe -File -Recurse |
        Where-Object { $_.FullName -match "\\$targetDirectory\\dumpbin\.exe$" } |
        Sort-Object FullName -Descending)
    if ($fallback.Count -gt 0) {
        return $fallback[0].FullName
    }
    throw "dumpbin.exe was not found under the Visual C++ tools directory. Install the MSVC build tools."
}

function Get-PeArchitecture([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "$Path is not a valid PE image."
        }
        $machine = $reader.ReadUInt16()
        switch ($machine) {
            0x8664 { return "x64" }
            0xAA64 { return "arm64" }
            default { return ("unknown-0x{0:X4}" -f $machine) }
        }
    }
    finally {
        $stream.Dispose()
    }
}

if ($ChildBuild) {
    try {
        if (-not $Arch -or -not $VsInstallPath) {
            throw "ChildBuild requires Arch and VsInstallPath."
        }

        $VsInstallPath = $VsInstallPath.Trim().Trim('"')
        $resolvedHostArch = Resolve-HostArchitecture
        $targetArch = if ($Arch -eq "x64") { "amd64" } else { "arm64" }
        $devShell = Join-Path $VsInstallPath "Common7/Tools/Launch-VsDevShell.ps1"
        if (-not (Test-Path -LiteralPath $devShell)) {
            throw "Visual Studio developer shell was not found: $devShell"
        }

        Write-Step "Initialize Visual Studio for target $Arch (host $resolvedHostArch)"
        & $devShell -Arch $targetArch -HostArch $resolvedHostArch -SkipAutomaticLocation
        if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
            throw "MSVC cl.exe for $Arch is unavailable. Add the $Arch C++ build tools in Visual Studio Installer."
        }
        if (-not (Get-Command cmake.exe -ErrorAction SilentlyContinue)) {
            throw "cmake.exe is unavailable. Add C++ CMake tools in Visual Studio Installer."
        }

        $singleArchScript = Join-Path $PSScriptRoot "build-native-windows.ps1"
        $buildParameters = @{
            Arch = $Arch
        }
        if ($SkipTests) { $buildParameters.SkipTests = $true }
        if ($Clean) { $buildParameters.Clean = $true }

        Write-Step "Build Watermark.Imaging.Native.dll for $Arch"
        & $singleArchScript @buildParameters
        exit 0
    }
    catch {
        Write-FailureDetails $_
        exit 1
    }
}

try {
    if ($env:OS -ne "Windows_NT") {
        throw "This script must run inside Windows."
    }

    $root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
    Write-Host "Workspace: $root"
    if ($root -like "C:\Mac\Home\*") {
        Write-Warning "The workspace is on a macOS shared folder. Dependencies stay on local Windows storage; if MSVC reports file locking errors, copy the repository to C:\Projects before building."
    }
    $requiredPaths = @(
        (Join-Path $root "native/third_party/LibRaw-0.22.1/Makefile.msvc"),
        (Join-Path $root "native/third_party/tiff-4.7.2/CMakeLists.txt"),
        (Join-Path $root "native/Watermark.Imaging.Native/CMakeLists.txt")
    )
    foreach ($requiredPath in $requiredPaths) {
        if (-not (Test-Path $requiredPath)) {
            throw "Required source is missing: $requiredPath"
        }
    }

    Write-Step "Find Visual Studio 2026/MSVC"
    $VsInstallPath = Find-VisualStudio
    Write-Host "Visual Studio: $VsInstallPath"

    $CMakePath = Find-CMake $VsInstallPath
    Write-Host "CMake: $CMakePath"
    $DumpbinPath = Find-Dumpbin $VsInstallPath "x64" (Resolve-HostArchitecture)
    Write-Host "Dumpbin: $DumpbinPath"

    Write-Step "Verify locked offline dependency archives"
    & $CMakePath "-DWMI_ROOT=$root" -DWMI_EXTRACT=ON -P (Join-Path $root "native/cmake/PrepareLockedDependencies.cmake")
    Assert-LastExitCode "Locked dependency verification"

    $resolvedHostArch = Resolve-HostArchitecture
    $currentPowerShell = (Get-Process -Id $PID).Path
    foreach ($buildArch in @("x64", "arm64")) {
        $skipArchitectureTests = $buildArch -eq "arm64" -and $resolvedHostArch -eq "amd64"
        $childArguments = @(
            "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", $PSCommandPath,
            "-ChildBuild", "-Arch", $buildArch,
            "-HostArch", $resolvedHostArch,
            "-VsInstallPath", $VsInstallPath,
            "-NoPause"
        )
        if ($skipArchitectureTests) { $childArguments += "-SkipTests" }
        if ($Clean) { $childArguments += "-Clean" }
        & $currentPowerShell @childArguments
        Assert-LastExitCode "$buildArch child build"
    }

    Write-Step "Verify output architectures"
    $outputs = @{
        x64 = Join-Path $root "native/artifacts/win-x64/Watermark.Imaging.Native.dll"
        arm64 = Join-Path $root "native/artifacts/win-arm64/Watermark.Imaging.Native.dll"
    }
    foreach ($expected in @("x64", "arm64")) {
        $output = $outputs[$expected]
        if (-not (Test-Path $output)) {
            throw "Expected output was not created: $output"
        }
        $actual = Get-PeArchitecture $output
        if ($actual -ne $expected) {
            throw "Architecture mismatch for $output. Expected $expected, found $actual."
        }
        $file = Get-Item $output
        $hash = (Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
        $exports = (& $DumpbinPath /nologo /exports $output | Out-String)
        Assert-LastExitCode "Inspect $expected exports"
        foreach ($requiredExport in @("wmi_get_abi_version", "wmi_color_processor_create", "wmi_color_gpu_snapshot_create")) {
            if (-not $exports.Contains($requiredExport)) {
                throw "$output does not export $requiredExport."
            }
        }
        Write-Host "$expected  $([Math]::Round($file.Length / 1MB, 2)) MB  SHA256 $hash"
        Write-Host "  $output"
    }

    Write-Step "Refresh Windows ABI markers and artifact hashes"
    $manifestUpdater = Join-Path $root "native/scripts/update-native-manifest.ps1"
    & $manifestUpdater -Arch x64
    & $manifestUpdater -Arch arm64

    Write-Host ""
    Write-Host "All Windows native artifacts are ready." -ForegroundColor Green
}
catch {
    Write-FailureDetails $_
    exit 1
}
finally {
    if (-not $NoPause -and -not $ChildBuild) {
        Write-Host ""
        Read-Host "Press Enter to close"
    }
}
