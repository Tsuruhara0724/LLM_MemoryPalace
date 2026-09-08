@echo off
setlocal

where py >nul 2>nul
if %errorlevel% equ 0 (
    py -3 "%~dp0server.py" %*
    exit /b %errorlevel%
)

where python >nul 2>nul
if %errorlevel% equ 0 (
    python "%~dp0server.py" %*
    exit /b %errorlevel%
)

echo Python 3 was not found. Install Python 3, then run this script again.
exit /b 1
