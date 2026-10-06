<#
.SYNOPSIS
    One-shot release: (optional) version bump -> commit -> build -> push -> GitHub Release -> prune older builds.

.DESCRIPTION
    The release name is <VersionPrefix>.<today yyyyMMdd>, e.g. v1.0.0.20261007. The date comes from the day
    this script runs; the three-part version changes only when you answer the bump question.
    Every outward step (commit, push, release, delete) asks first. Run it from Release.bat.
    Keep this file ASCII-only: Windows PowerShell 5.1 misparses BOM-less UTF-8.
#>

param(
    [string]$Repository = "HyungJIn-kr-97/JHJ_AI_USAGE"
)

# Trap: with "Stop", Windows PowerShell 5.1 turns git/gh progress on stderr into a terminating error. Check exit codes instead.
$ErrorActionPreference = "Continue"
$env:GIT_TERMINAL_PROMPT = "0"

$repo  = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$props = Join-Path $repo "200.Source\Directory.Build.props"
$out   = Join-Path $PSScriptRoot "publish"

function Fail {
    param([string]$Message)
    Write-Host ""
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    exit 1
}

function Ask {
    param([string]$Question, [string]$Default)
    $answer = Read-Host $Question
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim().ToLowerInvariant()
}

function Get-Prefix {
    if ((Get-Content -Path $props -Raw) -match '<VersionPrefix[^>]*>(\d+\.\d+\.\d+)</VersionPrefix>') { return $Matches[1] }
    Fail "Cannot read VersionPrefix from $props"
}

function Step {
    param([string]$Title)
    Write-Host ""
    Write-Host "== $Title" -ForegroundColor Cyan
}

# --- 0. tools ---------------------------------------------------------------
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { Fail "gh is not signed in. Run: gh auth login --web" }

$branch = (git -C $repo rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne "main") { Fail "Current branch is '$branch'. Switch to main first." }

# --- 1. version -------------------------------------------------------------
Step "Version"
$current = Get-Prefix
$date    = Get-Date -Format "yyyyMMdd"
Write-Host "Current release version : $current"
Write-Host "Build date (today)      : $date"
Write-Host "Without a bump the release will be v$current.$date"
Write-Host ""
Write-Host "Bump the release version?"
Write-Host "  [Enter] no - keep $current, only the date changes (default)"
Write-Host "  p = patch     m = minor     j = major     or type x.y.z"
$bump = Ask "Choice" "n"

$bumpArgs = $null
switch -Regex ($bump) {
    '^(n|no)$'        { break }
    '^(p|patch)$'     { $bumpArgs = @{ Bump = "patch" }; break }
    '^(m|minor)$'     { $bumpArgs = @{ Bump = "minor" }; break }
    '^(j|major)$'     { $bumpArgs = @{ Bump = "major" }; break }
    '^\d+\.\d+\.\d+$' { $bumpArgs = @{ Version = $bump }; break }
    default           { Fail "Unknown choice '$bump'." }
}

if ($bumpArgs) {
    try { & (Join-Path $PSScriptRoot "bump-version.ps1") @bumpArgs }
    catch { Fail "Version bump failed: $($_.Exception.Message)" }
}

$version = Get-Prefix
$release = "$version.$date"
$tag     = "v$release"

Write-Host ""
Write-Host "Release to publish: $tag" -ForegroundColor Yellow
if ((Ask "Continue? [y/N]" "n") -notmatch '^(y|yes)$') { Fail "Cancelled. Nothing was pushed or released." }

# --- 2. commit --------------------------------------------------------------
Step "Source"
$dirty = @(git -C $repo status --porcelain)
if ($dirty.Count -gt 0) {
    $dirty | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    if ((Ask "Commit these $($dirty.Count) change(s) as '$tag'? [y/N]" "n") -notmatch '^(y|yes)$') {
        Fail "Uncommitted changes. The release must match the pushed source - commit them first."
    }
    git -C $repo add -A
    git -C $repo commit -q -m $tag
    if ($LASTEXITCODE -ne 0) { Fail "git commit failed." }
    Write-Host "Committed: $(git -C $repo log --oneline -1)"
}
else {
    Write-Host "Working tree is clean: $(git -C $repo log --oneline -1)"
}

# --- 3. build ---------------------------------------------------------------
Step "Build"
# A dev-build instance started from this folder locks bin\ and breaks publish. Installed copies are left alone.
Get-Process AiUsageMonitor -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like ($repo + "\200.Source\*") } |
    Stop-Process -Force

try { & (Join-Path $PSScriptRoot "publish.ps1") -Platform x64 }
catch { Fail "publish.ps1 failed: $($_.Exception.Message)" }
if ($LASTEXITCODE -ne 0) { Fail "publish.ps1 failed." }

$assets = @(
    (Join-Path $out "AiUsageMonitor-win-x64-$tag.zip"),
    (Join-Path $out "AiUsageMonitor-win-x64-$tag.zip.sha256"),
    (Join-Path $out "AiUsageMonitor-win-x64-$tag.exe"),
    (Join-Path $out "AiUsageMonitor-Setup.exe")
)
foreach ($asset in $assets) {
    # Trap: publish.ps1 reads the date itself - a run that crosses midnight produces a different name.
    if (-not (Test-Path $asset)) { Fail "Missing build output: $asset" }
}

# --- 4. push + release ------------------------------------------------------
Step "GitHub"
gh release view $tag --repo $Repository *> $null
if ($LASTEXITCODE -eq 0) {
    Write-Host "$tag already exists on GitHub (same version, same day)." -ForegroundColor Yellow
    if ((Ask "Delete it and upload this build instead? [y/N]" "n") -notmatch '^(y|yes)$') { Fail "Cancelled. The existing release was kept." }
    gh release delete $tag --repo $Repository --cleanup-tag --yes
    if ($LASTEXITCODE -ne 0) { Fail "Could not delete the existing release." }
}

git -C $repo push origin main
if ($LASTEXITCODE -ne 0) { Fail "git push failed." }

gh release create $tag $assets --repo $Repository --target main --title $tag --generate-notes --latest
if ($LASTEXITCODE -ne 0) { Fail "gh release create failed." }

# --- 5. prune older builds of the same version ------------------------------
Step "Older builds"
$pruneScript = Join-Path $PSScriptRoot "prune-releases.ps1"
try {
    $plan = @(& $pruneScript -Repository $Repository 6>&1 | ForEach-Object { "$_" })
    $plan | ForEach-Object { Write-Host "  $_" }
    if ($plan -match '^would delete') {
        if ((Ask "Delete the older build(s) listed above? [Y/n]" "y") -match '^(y|yes)$') {
            & $pruneScript -Repository $Repository -Apply
        }
        else {
            Write-Host "Kept. Run prune-releases.ps1 -Apply later to delete them."
        }
    }
}
catch {
    Write-Host "Prune step failed (the release itself is published): $($_.Exception.Message)" -ForegroundColor Yellow
}

# --- done -------------------------------------------------------------------
Write-Host ""
Write-Host "[OK] Released $tag" -ForegroundColor Green
Write-Host "  Release page : https://github.com/$Repository/releases/tag/$tag"
Write-Host "  Installer    : https://github.com/$Repository/releases/latest/download/AiUsageMonitor-Setup.exe"
Write-Host "  The dev build was closed for the build. Start it again from 200.Source\costats.App\bin\Release\net10.0-windows\win-x64\AiUsageMonitor.exe"
exit 0
