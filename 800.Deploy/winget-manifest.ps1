# winget-manifest.ps1 - writes the three winget manifest files for one published release.
# Contract: ASCII only (Windows PowerShell 5.1 parses this file). PackageIdentifier must equal UpdateOptions.WingetId in the app.
# Usage:  .\winget-manifest.ps1 -Version 1.0.3.20261008        (release tag without the leading "v")
# Output: .\winget\manifests\h\HyungJin\JHJ_AI-Usage-Monitor\<version>\*.yaml
# Next:   winget validate <dir>  ->  winget install --manifest <dir>  ->  wingetcreate submit <dir>  (or a PR to microsoft/winget-pkgs)
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Repository = "HyungJIn-kr-97/JHJ_AI_USAGE",
    [string]$OutRoot = (Join-Path $PSScriptRoot "winget")
)

$ErrorActionPreference = "Stop"
$Version = $Version.TrimStart("v", "V")
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw "Version must look like 1.0.3.20261008 (got '$Version')." }

$id        = "HyungJin.JHJ_AI-Usage-Monitor"
$tag       = "v$Version"
$url       = "https://github.com/$Repository/releases/download/$tag/JHJ_AI-Usage-Monitor_Setup.exe"
$outDir    = Join-Path $OutRoot "manifests\h\HyungJin\JHJ_AI-Usage-Monitor\$Version"
$schema    = "1.6.0"

# Hash the exact bytes GitHub serves - a local build may differ from the uploaded asset.
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) "JHJ_AI-Usage-Monitor-Setup-$Version.exe"
Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
$sha = (Get-FileHash -Path $tmp -Algorithm SHA256).Hash.ToUpperInvariant()
Remove-Item $tmp -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

$versionYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@

$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: exe
Scope: user
InstallModes:
- silent
- silentWithProgress
InstallerSwitches:
  Silent: --silent --version $Version
  SilentWithProgress: --silent --version $Version
UpgradeBehavior: install
AppsAndFeaturesEntries:
- DisplayName: JHJ AI Usage Monitor
  Publisher: HyungJin Ju
  DisplayVersion: $Version
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@

$localeYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: HyungJin Ju
PublisherUrl: https://github.com/HyungJIn-kr-97
PublisherSupportUrl: https://github.com/$Repository/issues
PackageName: JHJ AI Usage Monitor
PackageUrl: https://github.com/$Repository
License: MIT
LicenseUrl: https://github.com/$Repository/blob/main/LICENSE
ShortDescription: Windows tray app that shows Claude, Codex, Copilot and Gemini usage, limits and cost in one popup.
Moniker: aiusagemonitor
Tags:
- claude
- codex
- copilot
- gemini
- usage
- tray
ReleaseNotesUrl: https://github.com/$Repository/releases/tag/$tag
ManifestType: defaultLocale
ManifestVersion: $schema
"@

[System.IO.File]::WriteAllText((Join-Path $outDir "$id.yaml"), $versionYaml, $utf8)
[System.IO.File]::WriteAllText((Join-Path $outDir "$id.installer.yaml"), $installerYaml, $utf8)
[System.IO.File]::WriteAllText((Join-Path $outDir "$id.locale.en-US.yaml"), $localeYaml, $utf8)

Write-Host "winget manifest written: $outDir"
Write-Host "  sha256: $sha"
Write-Host "  validate : winget validate `"$outDir`""
Write-Host "  try      : winget install --manifest `"$outDir`""
Write-Host "  submit   : wingetcreate submit `"$outDir`"   (winget install wingetcreate, once)"
$outDir
