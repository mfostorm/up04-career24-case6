@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch\Service.ps1" -Action Start -State "%LOCALAPPDATA%\Career24-UP04-Case6" -Port 33445
if errorlevel 1 pause
