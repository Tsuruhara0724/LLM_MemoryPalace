@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Generate-FormalWordAudio.ps1" -Force
if errorlevel 1 pause
