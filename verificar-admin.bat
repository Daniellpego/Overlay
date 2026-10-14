@echo off
rem Teste com tudo ligado (PresentMon + rede). Pede administrador e deixa a janela aberta com o resultado.
powershell -NoProfile -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','%~dp0tests\verificar-admin.ps1'"
