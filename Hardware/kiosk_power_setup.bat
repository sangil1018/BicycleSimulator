@echo off
chcp 65001 >nul
:: ================================================================
::  키오스크 절전 해제 설정 실행기 — 더블클릭만 하면 됨
::  관리자 권한이 없으면 UAC 창을 띄워 스스로 승격한 뒤
::  kiosk_power_setup.ps1을 실행한다.
:: ================================================================

:: 관리자 권한 확인 (net session은 관리자에서만 성공)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo 관리자 권한 요청 중... UAC 창에서 "예"를 누르세요.
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0kiosk_power_setup.ps1"

echo.
pause
