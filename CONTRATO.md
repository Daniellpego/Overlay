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
   **Teste de 1 h (2026-10-14 21:27-22:26, FC 27 aberto o tempo todo, `tests\monitorar-estabilidade.ps1`):** 118 amostras,
   0 quedas, mesmo processo do início ao fim; CPU média 0,25% (pico 3,1%); RAM privada 17-24 MB, sem subir (23,5 MB nas
   10 primeiras x 21,4 MB nas 10 últimas); handles oscilando 484-677 sem tendência (606 no início, 615 no fim);
   12 prints mostrando FPS*, ms* e horário atualizados até o fim.

6. Correções da revisão autorizadas no chat em 15/10/2026 — **validadas localmente; uso real pendente**:
   - FPS normal: superfície com maior amostra, pelo menos 3 quadros e 250 ms de observação; rajadas auxiliares não vencem.
   - FPS*: estimativa de alterações da janela ativa, não confirmação de FPS renderizado. Seleciona o adaptador do monitor.
     Atualizações acumuladas, agrupadas e regiões que atravessam a borda da janela interrompem a amostra.
     Calcula somente intervalos consecutivos confiáveis na janela de 1 s, exigindo 250 ms observados.
     Incerteza contínua deixa `---`; mesmo com esses filtros, metadados do compositor não identificam o processo produtor.
   - Ping: amostras separadas por IP. `ms?` indica destino UDP provável (2 s de liderança com vantagem de 25%),
     sem confirmar identidade de servidor; `ms*` é o alvo configurado. IP e diagnóstico disponíveis no menu.
   - Recarregar configuração aplica a detecção de rede. Posição limitada à tela e DPI por monitor V2 com fallback legado.
   - Sem dados, prova incompleta retorna 2 (INCONCLUSIVO); divergência retorna 1; aprovação completa retorna 0.
     FPS exige pares simultâneos em pelo menos 80% das amostras, com tolerância de 5% e teto do monitor.
   - Scripts só podem encerrar a cópia de teste dentro do projeto; overlay instalado/execução desconhecida é preservado.
   - `build.bat` sem avisos e `tests/verificar.ps1` completo: **Tudo OK**, incluindo 27 regressões novas.
     Prova sintética: 30/60/90/120/144 FPS, erro máximo observado 0,7 quadro (0,6%); parado com ruído: 0.
     Gabarito independente por tempos de desenhos GDI, nas mesmas janelas de 1 s; não é prova de apresentação real.
     Ping contra Windows em três alvos: diferenças 0/-1/0 ms. Janela: 33,1 MB e 0,049% CPU (sem admin/PresentMon).
     Foco, clique atravessando, atalho e instância única aprovados. Revisão independente concluída com achados corrigidos.
     Projeto C# sem package.json: lint/build npm não se aplicam; compilador C# trata avisos como erro.
   - Revalidar jogos/anticheat, consumo total com admin e telas de escalas diferentes no ambiente de uso.
     O teste histórico de 1 h acima não comprova estabilidade desta revisão.

7. Distribuição pública 0.1.0 (15/10/2026) — **prévia pública; instalação administrativa real pendente**:
   - ZIP Windows x64 pré-compilado com PresentMon 2.6.0 original, licenças, guia e manifesto SHA-256.
   - Instalador cria atalhos no menu Iniciar e registro de desinstalação; atualização mantém preferências.
     Executável antigo sem identidade de produto exige renomear a pasta antiga antes de instalar.
   - Configuração inicial criada exclusivamente, sem sobrescrever preferências; ancestrais bloqueados contra
     troca por junções durante a criação, inclusive quando o instalador já é aberto como administrador.
     O aplicativo elevado continua apenas lendo a configuração do usuário.
   - Falhas de atualização/restauração e remoção verificadas com bloqueio real de arquivo em pasta isolada.
     Remoção conserva preferências e logs; falha de recuperação conserva backup para restauração manual.
   - `tests/TesteDistribuicao.ps1`: pacote, manifesto, caminhos especiais, colisão, junção, recuperação e scripts.
     Gate completo `tests/verificar.ps1`: Tudo OK. Instalação/UAC/atalhos/remoção reais não executados nesta sessão.
   - A versão tem download pronto para uso, mas não representa homologação em todos os jogos nem estabilidade final.
   - PR #5 integrado em d128e39; release `v0.1.0` publicada como prévia. Download público sem autenticação
     conferido contra o SHA-256, manifesto íntegro (14 arquivos) e revisão limpa d128e39c022dd9577f54b5c057da156166ef8687.
     O executável efetivamente baixado passou no gate completo: Tudo OK. Distribuição: 32 verificações aprovadas.
     README público conferido no GitHub, com imagens carregadas e link direto do pacote.

8. Correção da primeira instalação (15/10/2026) — **0.1.1, instalação real em revalidação**:
   - Daniel reproduziu falha do instalador: `Add-Type` não encontrou o arquivo `.cs` em `C:\WINDOWS\TEMP`.
     Reprodução em PowerShell 5.1 sem instalar: saída 1 antes da correção e 0 depois.
   - Os dois auxiliares C# existentes foram extraídos sem mudança de comportamento para `tools/Distribuicao.cs`.
     `build.bat` gera `tools/Overlay.Distribuicao.dll`; instalar/iniciar/remover apenas carregam a DLL pronta.
   - O pacote exige a DLL e inclui seu hash no manifesto. A versão 0.1.0 permanece histórica;
     0.1.1 corrige a distribuição sem mudar o cálculo de FPS/ping.
   - `TesteDistribuicao.ps1`: regressões em processo novo do PowerShell 5.1,
     TEMP apontando para um arquivo e compilação proibida, com criação segura de configuração e API de fechamento.
     Inicialização da configuração do usuário também conferida com `C:\WINDOWS\TEMP` sem alterar preferências existentes.
   - DLL carregada por bytes para não manter bloqueio de arquivo; regressão remove a mesma instalação
     isolada da qual o auxiliar foi carregado, em processo novo do PowerShell 5.1.
   - Build sem avisos e gate completo: Tudo OK; 35 regressões de distribuição e revisão independente aprovadas.
     PR #7 integrado em ea31ca2 e prévia `v0.1.1` publicada. ZIP baixado sem autenticação, SHA-256/manifesto
     conferidos (15 arquivos), auxiliares executados em PS5 novo com TEMP inválido. Nova instalação com UAC pendente.
