@echo off
setlocal
where py >nul 2>nul
if %errorlevel% equ 0 (
    py -3 "%~dp0share_link.py" %*
    exit /b
)
where python >nul 2>nul
if %errorlevel% equ 0 (
    python "%~dp0share_link.py" %*
    exit /b
)
echo Python 3 was not found.
exit /b 1
