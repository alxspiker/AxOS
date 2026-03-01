@echo off
setlocal
cd /d "%~dp0"

set "SKIP_BUILD="
set "SERIAL_MODE="
set "VGA_MODEL=vmware"
for %%A in (%*) do (
    if /I "%%~A"=="nobuild" set "SKIP_BUILD=1"
    if /I "%%~A"=="serial" set "SERIAL_MODE=1"
    if /I "%%~A"=="vmware" set "VGA_MODEL=vmware"
    if /I "%%~A"=="std" set "VGA_MODEL=std"
)

if not defined SKIP_BUILD (
    call "%~dp0build.bat"
    if errorlevel 1 exit /b %errorlevel%
)

set "ISO=%~dp0bin\Debug\net6.0\AxOS.iso"
if not exist "%ISO%" (
    echo ISO not found: %ISO%
    echo Run build first or remove "nobuild".
    exit /b 1
)

set "DATA_IMG=%~dp0axos-data.img"

set "QEMU_EXE="
if exist "%ProgramFiles%\qemu\qemu-system-i386.exe" set "QEMU_EXE=%ProgramFiles%\qemu\qemu-system-i386.exe"
if not defined QEMU_EXE if exist "%ProgramFiles(x86)%\qemu\qemu-system-i386.exe" set "QEMU_EXE=%ProgramFiles(x86)%\qemu\qemu-system-i386.exe"
if not defined QEMU_EXE (
    where qemu-system-i386.exe >nul 2>nul
    if errorlevel 1 (
        echo QEMU was not found.
        echo Install it from winget:
        echo   winget install --id SoftwareFreedomConservancy.QEMU
        exit /b 1
    )
    set "QEMU_EXE=qemu-system-i386.exe"
)

powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepare_data_disk.ps1" -ImagePath "%DATA_IMG%" -SizeMB 128
if errorlevel 1 (
    echo Failed to prepare FAT data image.
    exit /b 1
)

set "DISPLAY_OPTS=-serial stdio"
if defined SERIAL_MODE (
    REM Removed "-display none" so we don't blind the system while debugging!
    set "DISPLAY_OPTS=-monitor none -serial stdio"
    echo Running AxOS in serial mode with video enabled - vga=%VGA_MODEL%...
) else (
    echo Running AxOS - vga=%VGA_MODEL%...
)

"%QEMU_EXE%" -vga %VGA_MODEL% -cdrom "%ISO%" -boot d -m 256 %DISPLAY_OPTS% -drive file="%DATA_IMG%",format=raw,if=ide,media=disk -device isa-debug-exit,iobase=0xf4,iosize=0x04 -no-reboot

set "QEMU_EXIT=%ERRORLEVEL%"
echo QEMU exited with code %QEMU_EXIT%.
if not "%QEMU_EXIT%"=="0" if not "%QEMU_EXIT%"=="1" (
    echo QEMU terminated unexpectedly.
)
exit /b %QEMU_EXIT%
