@echo off
taskkill /IM "MyriadIslePro.exe" /F >nul 2>&1
start "" "%~dp0build\MyriadIslePro.exe"
