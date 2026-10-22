@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-ProjectServices.ps1" %*
set "LaunchResult=%ERRORLEVEL%"
pause
exit /b %LaunchResult%
