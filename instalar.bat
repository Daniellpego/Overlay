@echo off
setlocal DisableDelayedExpansion
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\instalar.ps1"
set "OVERLAY_RESULTADO=%ERRORLEVEL%"
if not "%OVERLAY_RESULTADO%"=="0" pause
exit /b %OVERLAY_RESULTADO%
