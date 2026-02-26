@echo off
setlocal
cd /d "%~dp0"

echo Building AxOS (Debug)...
dotnet build AxOS.sln -c Debug
set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" (
    echo Build failed with exit code %BUILD_EXIT%.
    exit /b %BUILD_EXIT%
)

echo Build succeeded.
exit /b 0
