@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-VRSessionServer.ps1" %*
pause
