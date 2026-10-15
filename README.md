# Overlay

Overlay discreto para Windows 11 com **horário, ping e FPS**, feito em C# e .NET Framework, sem instalar SDK para compilar.

Fica no canto da tela, permite que os cliques atravessem a janela e não toma o foco do jogo. O projeto está em desenvolvimento; compatibilidade e consumo durante partidas ainda precisam de validação por jogo.

## Recursos

- FPS via eventos ETW do Windows, utilizando o PresentMon da Intel.
- Estimativa alternativa pela Desktop Duplication quando a leitura normal fica indisponível.
- Ping ICMP para alvo configurado ou destino UDP provável da janela ativa.
- Horário, cores por faixa, posição, fonte e monitor configuráveis.
- Atalho para ocultar/mostrar: `Ctrl+Shift+F10` por padrão.
- Menu na bandeja com diagnóstico, IP medido e recarga da configuração.
- Início com o Windows pela cópia instalada em Program Files.

## Instalação

Requisitos: Windows 11 x64, Windows PowerShell e .NET Framework 4.x, com `csc.exe` em `Microsoft.NET/Framework64/v4.0.30319`.

```powershell
git clone https://github.com/Daniellpego/overlay.git
cd overlay
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\baixar-presentmon.ps1
.\build.bat
.\instalar.bat
```

O download do PresentMon verifica SHA-256 e assinatura da Intel. O instalador pede elevação pelo Windows e inicia a cópia de uso em `%ProgramFiles%\Overlay`.

Nas próximas vezes, use `iniciar.bat` ou configure **Iniciar com o Windows** no menu da bandeja. A medição normal por ETW exige administrador.

## Configuração

A cópia instalada lê `%LOCALAPPDATA%\Overlay\config.ini`. A cópia de desenvolvimento lê o arquivo ao lado do executável.

Use **Abrir config.ini** no menu da bandeja, salve suas mudanças e escolha **Recarregar config.ini**. Veja as opções comentadas em [config.ini](config.ini).

| Indicação | Significado |
|---|---|
| `FPS` | Taxa de apresentações medida pelo PresentMon |
| `FPS*` | Estimativa de alterações visíveis da janela ativa, limitada pela frequência do monitor |
| `ms?` | Ping para um destino UDP provável; a identidade do servidor não é confirmada |
| `ms*` | Ping para o alvo configurado, como `1.1.1.1` |
| `ADM` | A medição normal precisa de administrador |
| `---` | Dados ausentes ou insuficientes |

## Limites e estado da validação

O overlay não injeta código nem lê a memória do jogo. O PresentMon abre handles de consulta limitada nos processos que apresentam frames. Essa abordagem não garante aceitação por todos os anticheats; alguns podem bloquear os eventos ETW.

A alternativa `FPS*` estima mudanças da tela. Outras janelas, o compositor e regiões agrupadas podem impedir uma medição confiável; amostras ambíguas são descartadas. O ping ICMP também pode diferir da latência exibida pelo jogo.

Compilação, regressões de lógica, prova sintética de 30 a 144 FPS e testes locais da janela passaram. Isso não equivale a homologação em todos os jogos. Consumo conjunto com PresentMon, impacto durante partidas, estabilidade prolongada da revisão atual e monitores com escalas diferentes continuam pendentes.

A meta é menos de 1% de CPU e 50 MB de RAM no conjunto. Os testes leves já realizados não comprovam essa meta durante partidas. Detalhes e histórico de evidências estão em [CONTRATO.md](CONTRATO.md).

## Desenvolvimento e testes

```powershell
.\build.bat
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\verificar.ps1
```

O compilador trata avisos como erros. Não há dependências npm nem etapa de lint JavaScript. Se o `Add-Type` não conseguir usar a pasta temporária do ambiente, configure `TEMP` e `TMP` para a pasta `.tmp` do projeto antes dos testes.

Os testes visuais abrem janelas de teste e exigem uma sessão gráfica interativa. Se encontrarem outra cópia do overlay, preservam essa execução e informam quando a verificação ficar incompleta.

Para validação manual durante uma partida, use `provar-no-jogo.bat`. Para verificar consumo conjunto e sessões ETW em uma cópia de teste, use `verificar-admin.bat`. Ambos exigem administrador.

Resultados: `0` significa aprovação, `1` indica falha e `2` significa inconclusivo nas provas que distinguem dados insuficientes.

## Estrutura

- `src/`: janela, métricas, configuração e integração com APIs do Windows.
- `tests/`: regressões, provas visuais e ferramentas de diagnóstico.
- `tools/`: download verificado do PresentMon e instalação.
- [ACTIVE_WORK.md](ACTIVE_WORK.md): estado do trabalho.
- [CLAUDE.md](CLAUDE.md): convenções para agentes e desenvolvimento.

Executáveis, logs, capturas de tela e a pasta temporária ficam fora do versionamento.
