@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Push-VRSessionPackageToQuest.ps1" %*
pause
