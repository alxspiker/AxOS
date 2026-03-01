@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "SKIP_BOOTSTRAP="
for %%A in (%*) do (
    if /I "%%~A"=="nobootstrap" set "SKIP_BOOTSTRAP=1"
)

if not defined SKIP_BOOTSTRAP (
    echo Preparing Cosmos toolchain...
    powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0bootstrap_cosmos_toolchain.ps1"
    set "BOOTSTRAP_EXIT=!ERRORLEVEL!"
    if not "!BOOTSTRAP_EXIT!"=="0" (
        echo Cosmos bootstrap failed with exit code !BOOTSTRAP_EXIT!.
        exit /b !BOOTSTRAP_EXIT!
    )
)

echo Building AxOS (Debug)...
dotnet build AxOS.sln -c Debug
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" (
    echo Build failed with exit code %BUILD_EXIT%.
    exit /b %BUILD_EXIT%
)

echo Build succeeded.
exit /b 0
