@echo off
:: ================================================================
::  Kiosk Power Settings Setup Executor -- Just double-click
::  If running without Admin privileges, it will elevate via UAC,
::  then run kiosk_power_setup.ps1.
:: ================================================================

:: Check for Administrator privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting Administrator privileges... Please click "Yes" in the UAC window.
    rem Run cmd.exe as Admin to execute this batch file again.
    powershell -NoProfile -Command "Start-Process cmd.exe -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0kiosk_power_setup.ps1"

echo.
pause
