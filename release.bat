@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
if %ERRORLEVEL% neq 0 (
    echo.
    echo Release script exited with code %ERRORLEVEL%.
)
echo.
pause
