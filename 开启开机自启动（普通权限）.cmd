@echo off
rem Enable autostart with NORMAL user rights (HKCU Run, no UAC, no scheduled task).
setlocal
set "EXE=%~dp0build\Capsule image Pro.exe"
if not exist "%EXE%" (
  echo Missing "%EXE%"
  exit /b 1
)
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v CapsuleImagePro /t REG_SZ /d "\"%EXE%\" --autostart" /f >nul
if errorlevel 1 (
  echo Failed to write HKCU Run key.
  exit /b 1
)
echo Autostart enabled, normal user rights.
echo Run value: "%EXE%" --autostart
pause
