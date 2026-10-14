@echo off
setlocal
rem Compila com o csc do .NET Framework 4.x que já vem no Windows (C# 5: sem $"", ?. ou nameof).
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Compilador C# nao encontrado em %CSC%
    exit /b 1
)
rem /platform:x64: o MonitorRede usa offsets de estruturas ETW de 64 bits.
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /codepage:65001 ^
    /out:"%~dp0Overlay.exe" ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    "%~dp0src\*.cs"
exit /b %ERRORLEVEL%
