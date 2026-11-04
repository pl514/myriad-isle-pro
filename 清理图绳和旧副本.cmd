@echo off
setlocal
echo 正在停止 PicRope...
taskkill /IM PicRope.exe /F >nul 2>&1

echo 正在删除 PicRope 开机自启动项...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PicRope /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v CapsuleImage /f >nul 2>&1

echo 正在删除桌面 PicRope 快捷方式...
del /q "%USERPROFILE%\Desktop\*PicRope*.lnk" >nul 2>&1
del /q "%PUBLIC%\Desktop\*PicRope*.lnk" >nul 2>&1

echo 正在删除旧副本...
if exist "E:\yingyong\deepseekgongzuoqv\Capsule image-copy" rmdir /s /q "E:\yingyong\deepseekgongzuoqv\Capsule image-copy"

echo 正在取得 PicRope 文件权限...
takeown /F "E:\OpenSourceTools\PicRope" /R /D Y >nul 2>&1
icacls "E:\OpenSourceTools\PicRope" /grant "%USERNAME%":(OI)(CI)F /T /C /Q >nul 2>&1
rmdir /s /q "E:\OpenSourceTools\PicRope" >nul 2>&1

if exist "E:\OpenSourceTools\PicRope" (
  echo PicRope 仍然存在，请手动删除。
) else (
  echo PicRope 已删除。
)

echo 完成。Caplet handoff 壳子保留。
pause
