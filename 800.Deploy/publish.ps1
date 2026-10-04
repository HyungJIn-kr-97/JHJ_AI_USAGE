<#
.SYNOPSIS
    Publishes AI Usage Monitor for Windows x64 and ARM64.

.DESCRIPTION
    Creates self-contained, single-file executables for distribution.

.PARAMETER Version
    Version number for the build (major.minor.patch). Defaults to VersionPrefix from 200.Source/Directory.Build.props.

.PARAMETER Platform
    Target platform: x64, arm64, or all. Defaults to all.

.PARAMETER Configuration
    Build configuration: Release or Debug. Defaults to Release.

.EXAMPLE
    .\publish.ps1
    Publishes for all platforms with default settings.

.EXAMPLE
    .\publish.ps1 -Version "1.2.0" -Platform x64
    Publishes version 1.2.0 for Windows x64 only.
#>

param(
    [string]$Version = "",
    [ValidateSet("x64", "arm64", "all")]
    [string]$Platform = "all",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Get-DefaultVersion {
    $propsPath = Join-Path $PSScriptRoot "..\200.Source\Directory.Build.props"
    if (-not (Test-Path $propsPath)) {
        return "1.0.0"
    }

    # Trap: VersionPrefix carries a Condition attribute, so [xml] access yields an XmlElement, not a string.
    # Keep this file ASCII-only: Windows PowerShell 5.1 misparses BOM-less UTF-8.
    if ((Get-Content -Path $propsPath -Raw) -match '<VersionPrefix[^>]*>(\d+\.\d+\.\d+)</VersionPrefix>') {
        return $Matches[1]
    }

    return "1.0.0"
}

function Assert-SemVer {
    param([string]$Value)

    if ($Value -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must use major.minor.patch format (for example 1.2.3). Received: '$Value'."
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-DefaultVersion
}

Assert-SemVer -Value $Version

# Hash via .NET: Get-FileHash fails to auto-load when Windows PowerShell inherits a PowerShell 7 PSModulePath.
function Write-Checksum {
    param([string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try { $hash = ([System.BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
    $checksumPath = "$Path.sha256"
    Set-Content -Path $checksumPath -Value "$hash  $(Split-Path -Path $Path -Leaf)" -Encoding Ascii
    return $checksumPath
}
$projectPath = Join-Path $PSScriptRoot "..\200.Source\costats.App\costats.App.csproj"
$outputBase = Join-Path $PSScriptRoot "publish"

$platforms = if ($Platform -eq "all") { @("win-x64", "win-arm64") } else { @("win-$Platform") }

Write-Host "Building AI Usage Monitor v$Version" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host "Platforms: $($platforms -join ', ')" -ForegroundColor Gray
Write-Host ""

foreach ($rid in $platforms) {
    $outputPath = Join-Path $outputBase $rid

    Write-Host "Publishing for $rid..." -ForegroundColor Yellow

    dotnet publish $projectPath `
        --configuration $Configuration `
        --runtime $rid `
        --self-contained true `
        --output $outputPath `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=embedded `
        -p:VersionPrefix=$Version `
        -p:Version=$Version

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to publish for $rid" -ForegroundColor Red
        exit 1
    }

    # Create ZIP archive
    $zipPath = Join-Path $outputBase "AiUsageMonitor-$rid-v$Version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Compress-Archive -Path "$outputPath\*" -DestinationPath $zipPath
    $checksumPath = Write-Checksum -Path $zipPath
    Write-Host "Created: $zipPath" -ForegroundColor Green
    Write-Host "Checksum: $checksumPath" -ForegroundColor Green

    # Offline installer: the single-file exe installs itself on first run (SelfInstaller), so it ships as-is.
    $exeAsset = Join-Path $outputBase "AiUsageMonitor-$rid-v$Version.exe"
    Copy-Item -Path (Join-Path $outputPath "AiUsageMonitor.exe") -Destination $exeAsset -Force
    $null = Write-Checksum -Path $exeAsset
    Write-Host "Created: $exeAsset" -ForegroundColor Green
    Write-Host ""
}

# Web installer: small .NET Framework 4.8 exe that lists GitHub releases and installs the chosen version.
Write-Host "Building web installer..." -ForegroundColor Yellow
$setupProject = Join-Path $PSScriptRoot "..\200.Source\costats.Setup\costats.Setup.csproj"
$setupOut = Join-Path $outputBase "setup"
dotnet build $setupProject --configuration Release --output $setupOut -p:VersionPrefix=$Version -p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to build web installer" -ForegroundColor Red
    exit 1
}
$setupAsset = Join-Path $outputBase "AiUsageMonitor-Setup.exe"
Copy-Item -Path (Join-Path $setupOut "AiUsageMonitor-Setup.exe") -Destination $setupAsset -Force
$null = Write-Checksum -Path $setupAsset
Write-Host "Created: $setupAsset" -ForegroundColor Green
Write-Host ""
Write-Host "Build complete!" -ForegroundColor Cyan
Write-Host "Output directory: $outputBase" -ForegroundColor Gray

# Show file sizes
Write-Host ""
Write-Host "Artifacts:" -ForegroundColor Yellow
Get-ChildItem $outputBase -File | Where-Object { $_.Extension -in ".zip", ".exe" } | ForEach-Object {
    $sizeMB = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.Name) - $sizeMB MB" -ForegroundColor Gray
}
