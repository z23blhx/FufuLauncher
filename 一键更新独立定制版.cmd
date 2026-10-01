@echo off
chcp 65001 >nul
title FufuLauncher Additive Custom Update
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-AdditiveCustom.ps1"
if errorlevel 1 (
    echo.
    echo Update failed. Send the message above to Codex.
    pause
)
