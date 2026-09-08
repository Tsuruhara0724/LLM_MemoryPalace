@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Pull-QuestResultsToPC.ps1" %*
exit /b %ERRORLEVEL%
