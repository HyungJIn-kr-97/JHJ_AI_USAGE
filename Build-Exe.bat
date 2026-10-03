@echo off
rem Build the installable exe (self-contained, single file) and the update package.
rem Usage: Build-Exe.bat            -> version from src\Directory.Build.props
rem        Build-Exe.bat 1.0.1      -> also writes 1.0.1 into src\Directory.Build.props
setlocal
cd /d "%~dp0"

if not "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bump-version.ps1" -Version %~1
    if errorlevel 1 goto :fail
)

rem A dev-build instance started from this folder locks bin\ and breaks publish. Installed copies are left alone.
powershell -NoProfile -Command "Get-Process costats.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '%~dp0*' } | Stop-Process -Force"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" -Platform x64
if errorlevel 1 goto :fail

echo.
echo [OK] Installer exe : %~dp0publish\win-x64\costats.App.exe
echo      Run it once - it installs itself to %%LOCALAPPDATA%%\costats-jhj\app
echo [OK] Update package: %~dp0publish\costats-win-x64-v*.zip and .zip.sha256
echo      Upload both to a GitHub Release tagged v{version} to ship an update
echo.
pause
exit /b 0

:fail
echo.
echo [FAIL] Build failed. See the messages above.
pause
exit /b 1
