@echo off
rem Diagnostico do FPS sem leitura (acesso negado). Pede administrador; o resultado vai para .tmp\diagnostico.txt.
powershell -NoProfile -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','%~dp0tests\diagnosticar-etw.ps1'"
