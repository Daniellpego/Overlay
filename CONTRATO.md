# Contrato do projeto (aprovado em 2026-10-14)

## Objetivo
Overlay minúsculo no canto da tela mostrando horário, ping e FPS real enquanto o Daniel joga (EA FC, Fortnite, Battlefield, COD).

## Restrições
- Zero injeção no jogo e zero leitura de memória dele. FPS vem dos eventos ETW do Windows via PresentMon (Intel).
- Clique atravessa, sempre por cima, fora da barra de tarefas e do Alt+Tab, nunca rouba o foco.
- < 1% de CPU e < 50 MB de RAM.
- C# compilado pelo `csc.exe` do .NET Framework 4.x que vem no Windows; sem SDK.
- Ping v1: servidor fixo configurável. Ping v2: detectar o servidor da partida.

## Formato
`E:\overlay` com código, `CLAUDE.md`, `config.ini` e `iniciar.bat` (roda como admin).

## Falhou se
- FPS diverge > 5% do limitador do jogo.
- O overlay rouba foco ou clique.
- Some no modo tela cheia sem borda.
- Derruba o FPS do jogo de forma perceptível.
- Trava ou fecha sozinho depois de 1 h jogando.

## Etapas
1. Janela + relógio + config.ini — **feita** (`tests\verificar.ps1` passa).
2. FPS real via PresentMon (jogo em primeiro plano, admin).
3. Ping (v1 alvo fixo; v2 servidor da partida).
4. Ajustes: atalho para ocultar, cores por faixa de FPS/ping, iniciar com o Windows.
5. Revisão independente (subagente de contexto limpo) + teste de 1 h jogando.
