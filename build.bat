@echo off
setlocal
rem Compila com o csc do .NET Framework 4.x que já vem no Windows (C# 5: sem $"", ?. ou nameof).
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Compilador C# nao encontrado em %CSC%
    exit /b 1
)
rem Auxiliar ja compilado: instalar/iniciar/remover nao dependem de Add-Type e TEMP.
"%CSC%" /nologo /target:library /platform:x64 /optimize+ /warnaserror+ /codepage:65001 ^
    /out:"%~dp0tools\Overlay.Distribuicao.dll" ^
    "%~dp0tools\Distribuicao.cs" "%~dp0src\Versao.cs"
if errorlevel 1 exit /b 1
rem /platform:x64: o MonitorRede usa offsets de estruturas ETW de 64 bits.
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /codepage:65001 ^
    /out:"%~dp0Overlay.exe" ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    "%~dp0src\*.cs"
if errorlevel 1 exit /b 1
rem Prova de um clique dentro do jogo (provar-no-jogo.bat): mesmo codigo do overlay + tests\ProvaJogo.cs.
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ /codepage:65001 /main:ProvaJogo ^
    /out:"%~dp0ProvaJogo.exe" ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    "%~dp0src\*.cs" "%~dp0tests\ProvaJogo.cs"
exit /b %ERRORLEVEL%
