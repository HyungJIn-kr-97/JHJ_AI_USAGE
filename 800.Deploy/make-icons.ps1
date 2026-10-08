# Regenerate the app and setup .ico files from the single drawing code (TrayIconRenderer.DefaultStyle).
# Contract: run this whenever the default icon shape or palette changes - the .ico files have no other source.
# Usage: pwsh -File 800.Deploy\make-icons.ps1        (add -Configuration Debug to use a debug build)
[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $repo "200.Source\costats.App"
$exe = Join-Path $appDir "bin\$Configuration\net10.0-windows\win-x64\AI-Usage-Monitor_JHJ.exe"

if (-not (Test-Path $exe)) {
    Write-Host "Build first: dotnet build 200.Source\costats.sln -c $Configuration" -ForegroundColor Red
    exit 1
}

# Trap: the app refuses to start a second instance, so stop the running dev build first.
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force
Start-Sleep -Milliseconds 400

$stage = Join-Path $env:TEMP ("icon-export-" + [guid]::NewGuid().ToString("N"))
& $exe --export-icon $stage | Out-Null

# Trap: the app is a WinExe, so it detaches - wait for the files instead of the exit code.
$deadline = (Get-Date).AddSeconds(30)
$wanted = @("tray-icon.ico", "setup-icon.ico")
while ((Get-Date) -lt $deadline) {
    $done = $wanted | Where-Object { Test-Path (Join-Path $stage $_) }
    if ($done.Count -eq $wanted.Count) { break }
    Start-Sleep -Milliseconds 300
}

$targets = @{
    "tray-icon.ico"  = Join-Path $appDir "Resources\tray-icon.ico"
    "setup-icon.ico" = Join-Path $repo "200.Source\costats.Setup\Resources\setup-icon.ico"
}

$failed = $false
foreach ($name in $wanted) {
    $src = Join-Path $stage $name
    if (-not (Test-Path $src)) {
        Write-Host "[FAIL] not produced: $name" -ForegroundColor Red
        $failed = $true
        continue
    }

    Copy-Item -LiteralPath $src -Destination $targets[$name] -Force
    $kb = [math]::Round((Get-Item $targets[$name]).Length / 1KB, 1)
    Write-Host ("[OK] {0,-15} {1,6} KB  -> {2}" -f $name, $kb, $targets[$name]) -ForegroundColor Green
}

Remove-Item -Recurse -Force -LiteralPath $stage -ErrorAction SilentlyContinue

if ($failed) { exit 1 }

Write-Host ""
Write-Host "Rebuild so the new icons are embedded: dotnet build 200.Source\costats.sln -c $Configuration" -ForegroundColor Yellow
exit 0
