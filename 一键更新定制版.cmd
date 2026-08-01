@echo off
chcp 65001 >nul
title FufuLauncher Custom Updater

powershell.exe -NoLogo -NoProfile -Command "if ((New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { exit 0 } else { exit 1 }"
if errorlevel 1 (
    powershell.exe -NoLogo -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-LocalCustom.ps1"
set "update_exit_code=%errorlevel%"

echo.
if not "%update_exit_code%"=="0" (
    echo Update failed. Read the message above or send it to Codex.
) else (
    echo Update completed successfully.
)

pause
exit /b %update_exit_code%
