# Contrato do projeto (aprovado em 2026-10-14)

## Objetivo
Overlay minúsculo no canto da tela mostrando horário, ping e FPS real enquanto o Daniel joga (EA FC, Fortnite, Battlefield, COD).

## Restrições
- Zero injeção no jogo e zero leitura de memória dele. FPS vem dos eventos ETW do Windows via PresentMon (Intel).
  Exceção aceita (revisão da etapa 5): o PresentMon abre um handle só de consulta (`PROCESS_QUERY_LIMITED_INFORMATION`,
  o mesmo do Gerenciador de Tarefas) em cada processo que apresenta frames, para saber quando ele termina. Inevitável pela CLI.
- Clique atravessa, sempre por cima, fora da barra de tarefas e do Alt+Tab, nunca rouba o foco.
- < 1% de CPU e < 50 MB de RAM.
- C# compilado pelo `csc.exe` do .NET Framework 4.x que vem no Windows; sem SDK.
- Ping v1: servidor fixo configurável. Ping v2: detectar o servidor da partida.

## Formato
`E:\overlay` com código, `CLAUDE.md`, `config.ini` e `iniciar.bat` (roda como admin).
Uso diário: cópia instalada em `C:\Program Files\Overlay` (`instalar.bat`), pasta que só admin altera — é ela que roda
elevada e inicia com o Windows. Config dela em `%LOCALAPPDATA%\Overlay` (só lido); log em `C:\Program Files\Overlay\log`.

## Falhou se
- FPS diverge > 5% do limitador do jogo.
- O overlay rouba foco ou clique.
- Some no modo tela cheia sem borda.
- Derruba o FPS do jogo de forma perceptível.
- Trava ou fecha sozinho depois de 1 h jogando.

## Etapas
1. Janela + relógio + config.ini — **feita** (`tests\verificar.ps1` passa).
2. FPS real via PresentMon (jogo em primeiro plano, admin) — **código pronto, testes automáticos OK; falta validar num jogo com limitador de FPS** (`iniciar.bat`).
   Sem admin o overlay mostra `ADM` e oferece "Reiniciar como administrador" no menu do ícone. **Validado pelo Daniel num jogo com limitador.**
3. Ping — ping ao alvo fixo funciona. **A detecção do servidor nunca funcionou até a etapa 5** (OpenTrace ligava à versão
   ANSI; achado 1 da revisão). O teste da etapa 3 relatado como OK não é compatível com aquele código. Corrigido; revalidar numa partida.
   v2: `MonitorRede` lê eventos ETW de envio UDP (Kernel-Network) e `DetectorServidor` escolhe o IP público com mais pacotes do processo em primeiro plano.
   Servidor que não responde ICMP (comum) → mostra o `alvo_ping` com `ms*`.
4. Ajustes — **código pronto, testes automáticos OK (atalho testado de verdade); falta o Daniel testar o atalho dentro do jogo e o "Iniciar com o Windows" (admin)**:
   atalho global (`atalho_ocultar`, padrão Ctrl+Shift+F10, via RegisterHotKey); cores por faixa de FPS/ping;
   "Iniciar com o Windows" no menu do ícone = tarefa agendada no logon com privilégio mais alto (sobe como admin sem UAC).
   **FC 27 (2026-10-14):** com o jogo aberto, o anticheat da EA faz o Windows recusar QUALQUER sessão ETW, mesmo como admin
   (`diagnosticar.bat`: até sessão vazia do `logman` dá "Acesso negado"). PresentMon e detecção do servidor ficam bloqueados (`BLQ`, `ms*`).
   Plano B: `ContadorTela` conta os quadros que chegam ao monitor pela Desktop Duplication (API do OBS/Discord, sem tocar no jogo
   e sem ETW) e mostra `FPS*`. Medido com o FC aberto: funciona, ~0,5% CPU. Limites: teto = taxa do monitor (240 Hz),
   +20-30 MB de RAM só enquanto o bloqueio durar.
   Desde 2026-10-14 21:30 conta só mudanças dentro da janela do jogo e fora do overlay (retângulos alterados da Desktop
   Duplication) e calcula pelo tempo entre quadros. `tests\ProvaFpsTela.cs` (gabarito 30-144/s, ruído 50/s ao lado,
   overlay falso por cima): erro máximo 1,3 quadro em 3 rodadas; sem ruído, +0,3. Resíduo: o Windows às vezes junta
   áreas alteradas vizinhas num retângulo só. `provar-no-jogo.bat` compara PresentMon x tela e ping x ping.exe no jogo real.
5. Revisão independente (subagente de contexto limpo) — **feita em 2026-10-14: 10 achados, 1–9 corrigidos** (10, DPI por monitor, adiado).
   Falta: `verificar-admin.bat` (CPU/RAM com tudo ligado), revalidar a detecção do servidor e o teste de 1 h jogando.
