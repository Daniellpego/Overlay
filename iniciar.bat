@echo off
rem Abre o overlay como administrador (o PresentMon precisa disso para ler os eventos de frame do Windows).
powershell -NoProfile -Command "Start-Process -FilePath '%~dp0Overlay.exe' -WorkingDirectory '%~dp0' -Verb RunAs"
