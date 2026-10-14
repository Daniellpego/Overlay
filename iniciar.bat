@echo off
rem Abre o overlay como administrador (o PresentMon e a deteccao do servidor precisam disso).
rem Usa a copia instalada em Program Files quando existir; senao, a da pasta do projeto.
set "EXE=%ProgramFiles%\Overlay\Overlay.exe"
if not exist "%EXE%" set "EXE=%~dp0Overlay.exe"
powershell -NoProfile -Command "Start-Process -FilePath '%EXE%' -Verb RunAs"
