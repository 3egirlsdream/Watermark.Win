param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("x64", "arm64")]
    [string]$Arch
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$ArtifactsRoot = Join-Path $Root "native/artifacts"
$ManifestPath = Join-Path $ArtifactsRoot "manifest.json"
$DependencyLock = Join-Path $Root "native/dependencies.lock.json"

if (-not (Test-Path $ManifestPath)) {
    throw "Native manifest does not exist: $ManifestPath"
}

$Manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$ArtifactKey = if ($Arch -eq "arm64") { "windowsArm64" } else { "windowsX64" }
$RelativePath = if ($Arch -eq "arm64") {
    "win-arm64/Watermark.Imaging.Native.dll"
} else {
    "win-x64/Watermark.Imaging.Native.dll"
}
$BinaryPath = Join-Path $ArtifactsRoot $RelativePath
if (-not (Test-Path $BinaryPath)) {
    throw "Windows native artifact does not exist: $BinaryPath"
}

$Entry = [pscustomobject][ordered]@{
    path = $RelativePath
    architectures = @($Arch)
    nativeAbiVersion = 4
    binarySha256 = (Get-FileHash $BinaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
$Manifest.artifacts | Add-Member -NotePropertyName $ArtifactKey -NotePropertyValue $Entry -Force
$Manifest.schemaVersion = 3
$Manifest.nativeAbiVersion = 4
$Manifest.generatedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$Manifest.dependencyLockSha256 = (Get-FileHash $DependencyLock -Algorithm SHA256).Hash.ToLowerInvariant()

$RequiredArtifactKeys = @(
    "maccatalyst",
    "iosDevice",
    "iosSimulator",
    "androidArm64V8a",
    "androidX86_64",
    "windowsX64",
    "windowsArm64"
)
$AbiVersions = foreach ($Key in $RequiredArtifactKeys) {
    $Candidate = $Manifest.artifacts.PSObject.Properties[$Key]
    if ($null -eq $Candidate -or $null -eq $Candidate.Value.nativeAbiVersion) { 0 }
    else { [int]$Candidate.Value.nativeAbiVersion }
}
$Manifest.artifactSetAbiVersion = ($AbiVersions | Measure-Object -Minimum).Minimum
$Manifest.artifactSetReady = ($AbiVersions | Where-Object { $_ -ne 4 }).Count -eq 0

$TemporaryPath = "$ManifestPath.tmp.$PID"
try {
    $Json = $Manifest | ConvertTo-Json -Depth 20
    $Utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($TemporaryPath, $Json, $Utf8WithoutBom)
    Move-Item $TemporaryPath $ManifestPath -Force
}
finally {
    Remove-Item $TemporaryPath -Force -ErrorAction SilentlyContinue
}

Write-Host "Updated $ManifestPath for Windows $Arch ABI 4."
