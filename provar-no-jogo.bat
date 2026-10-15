@echo off
rem Prova do overlay DENTRO do jogo: abra o jogo, rode isto, clique Sim e volte para o jogo durante a contagem de 10 s.
rem Compara o FPS por dois metodos independentes e o ping com o do Windows. Resultado em .tmp\prova-jogo.txt.
if not exist "%~dp0ProvaJogo.exe" (
    echo ProvaJogo.exe nao encontrado. Rode build.bat antes.
    pause
    exit /b 1
)
powershell -NoProfile -Command "Start-Process -FilePath '%~dp0ProvaJogo.exe' -Verb RunAs"
