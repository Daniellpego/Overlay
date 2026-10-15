@echo off
rem Instala/atualiza o overlay em "C:\Program Files\Overlay" (pede administrador). Rode build.bat antes.
powershell -NoProfile -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','%~dp0tools\instalar.ps1'"
                                                      