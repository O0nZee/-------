@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Stop.ps1" %*
if errorlevel 1 (
    echo.
    echo The platform could not stop. See the error above.
    pause
    exit /b 1
)
exit /b 0
