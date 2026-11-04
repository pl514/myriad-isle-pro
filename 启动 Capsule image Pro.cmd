@echo off
taskkill /IM "Capsule image Pro.exe" /F >nul 2>&1
start "" "%~dp0build\Capsule image Pro.exe"
