@echo off
chcp 65001 >nul
title ESP32 자전거 시뮬레이터 시리얼 모니터

python serial_monitor.py %*

echo.
pause
