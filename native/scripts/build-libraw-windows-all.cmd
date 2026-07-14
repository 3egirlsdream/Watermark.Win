@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-libraw-windows-all.ps1"
exit /b %ERRORLEVEL%
