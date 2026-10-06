<#
.SYNOPSIS
    Sets the release version (major.minor.patch) in 200.Source/Directory.Build.props.
    The 4th part (build date) is added by the build itself - see README "version scheme".

.PARAMETER Version
    Explicit version in major.minor.patch format (e.g. 1.2.3). Cannot be combined with -Bump.

.PARAMETER Bump
    major, minor or patch - increments the current VersionPrefix. Cannot be combined with -Version.

.PARAMETER DryRun
    Show what would change without modifying any files.

.EXAMPLE
    .\bump-version.ps1 -Version "1.2.0"
.EXAMPLE
    .\bump-version.ps1 -Bump patch -DryRun
#>

param(
    [string]$Version = "",
    [ValidateSet("major", "minor", "patch")]
    [string]$Bump = "",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot       = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$buildPropsPath = Join-Path $repoRoot "200.Source\Directory.Build.props"
# Trap: Windows PowerShell 5.1 Get-Content/Set-Content default to ANSI and corrupt the UTF-8 comments in the props file
$utf8 = New-Object System.Text.UTF8Encoding $false

function Get-CurrentVersion {
    if (-not (Test-Path $buildPropsPath)) {
        throw "Directory.Build.props not found at $buildPropsPath"
    }
    $content = [System.IO.File]::ReadAllText($buildPropsPath, $utf8)
    if ($content -match '<VersionPrefix[^>]*>(\d+\.\d+\.\d+)</VersionPrefix>') {
        return $Matches[1]
    }
    throw "Could not read VersionPrefix from Directory.Build.props"
}

function Assert-SemVer {
    param([string]$Value)
    if ($Value -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must use major.minor.patch format (e.g. 1.2.3). Received: '$Value'."
    }
}

function Step-Version {
    param([string]$Current, [string]$Part)
    $parts = $Current.Split('.')
    switch ($Part) {
        "major" { $parts[0] = [int]$parts[0] + 1; $parts[1] = 0; $parts[2] = 0 }
        "minor" { $parts[1] = [int]$parts[1] + 1; $parts[2] = 0 }
        "patch" { $parts[2] = [int]$parts[2] + 1 }
    }
    return "$($parts[0]).$($parts[1]).$($parts[2])"
}

if ($Version -and $Bump) {
    throw "Specify either -Version or -Bump, not both."
}
if (-not $Version -and -not $Bump) {
    throw "Specify either -Version '1.2.3' or -Bump (major|minor|patch)."
}

$oldVersion = Get-CurrentVersion
Assert-SemVer -Value $oldVersion

if ($Bump) {
    $newVersion = Step-Version -Current $oldVersion -Part $Bump
} else {
    Assert-SemVer -Value $Version
    $newVersion = $Version
}

if ($newVersion -eq $oldVersion) {
    Write-Host "Version is already $oldVersion - nothing to do." -ForegroundColor Yellow
    exit 0
}

$label = if ($DryRun) { "[DRY RUN] " } else { "" }
Write-Host ""
Write-Host "${label}Bumping version: $oldVersion -> $newVersion" -ForegroundColor Cyan

if (-not $DryRun) {
    $content = [System.IO.File]::ReadAllText($buildPropsPath, $utf8)
    $content = $content -replace "(<VersionPrefix[^>]*>)$([regex]::Escape($oldVersion))(</VersionPrefix>)", "`${1}$newVersion`${2}"
    [System.IO.File]::WriteAllText($buildPropsPath, $content, $utf8)
}

Write-Host "${label}Done! 200.Source/Directory.Build.props is now $newVersion" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  git add -A ; git commit -m 'v$newVersion'" -ForegroundColor Gray
Write-Host "  .\800.Deploy\publish.ps1" -ForegroundColor Gray
