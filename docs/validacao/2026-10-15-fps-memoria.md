# Correção do CSV e alocações do parser — candidata 0.1.2

## Problema observado

A cópia instalada 0.1.1 registrou um cabeçalho com `CPUStartTimeInMs`, enquanto o parser só aceitava `CPUStartTime`. O PresentMon continuava aberto, mas suas amostras eram recusadas. A falta de dados acionava a captura alternativa da tela.

Na observação de 30 segundos com FIFA aberto, foram medidos 75,5 MB de working set no Overlay e 12,8 MB no PresentMon, somando 88,3 MB; CPU média conjunta 0,291%. Essa observação não foi um benchmark contínuo de partida e não é a medição da candidata.

## Mudança

- Aceita `CPUStartTimeInMs`, `CPUStartTime` e `TimeInMs` legado, nessa ordem de preferência. Mantém os tempos em milissegundos, conforme a [documentação oficial do PresentMon 2.6.0](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md#csv-columns).
- Descarta outros PIDs antes de criar strings e extrai somente os campos utilizados. A rotina anterior fazia `Split` de todas as colunas de todos os processos.
- Poda filas de superfícies expiradas para não reter swapchains encerradas até uma troca de janela.
- Mantém os filtros de janela temporal, processo alvo, superfície principal e amostra mínima. A captura Direct3D não foi alterada.

Quando os dados normais voltam, o comportamento já existente em `OverlayForm.LerFps` encerra e libera o contador alternativo. A correção permite voltar a esse caminho, mas a economia de memória nativa precisa ser medida no uso real.

## Provas

Seis casos de CSV falhavam antes e passaram depois: cabeçalho real em 30/60/144/240 FPS, formato legado e preferência da coluna explícita em ms. Também foram conferidos PID inválido/overflow/espaços e liberação de superfícies encerradas (100 superfícies históricas, 1 fila retida ao final, leitura de 60 FPS preservada).

O ensaio [`MedirParserFps.cs`](../../tests/MedirParserFps.cs) fornece a mesma entrada às duas revisões, em processos separados. São 200 mil linhas pré-geradas; 90% pertencem a outro PID. Ambas as revisões recebem um cabeçalho compatível (`CPUStartTime`) e terminam com 60 FPS. Isso evita comparar o parser antigo sem processar nada contra o novo trabalhando.

| Métrica do ensaio | Antes | Candidata |
| :--- | ---: | ---: |
| Bytes gerenciados alocados | 143.805.640 | 2.045.216 |
| Contagem de coletas gen0, incluindo coleta final do ensaio | 23 | 1 |
| Tempo observado, sem garantia de desempenho | 60 ms | 15 ms |
| FPS calculado | 60 | 60 |

Redução de aproximadamente **98,6% nas alocações deste ensaio**, medida por `AppDomain.MonitoringTotalAllocatedMemorySize`. Essa API informa alocações cumulativas, não RAM residente; veja a [documentação da Microsoft](https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.monitoringtotalallocatedmemorysize?view=netframework-4.8.1). O `GC.Collect` está apenas no ensaio para delimitar os contadores; não foi adicionado ao aplicativo.

Não significa reduzir a RAM total de 88 MB para 2 MB, nem prova latência ou consumo durante partidas.

## Validação nesta sessão

Build sem avisos, verificação lógica (104 aprovações, zero falhas), 35 verificações de distribuição e revisão independente aprovados. A cópia instalada e o FIFA foram preservados.

`tests/verificar.ps1 -SomenteLogica` pula a prova gráfica e os testes da janela; retorna **2 — INCONCLUSIVO para o gate completo**, mesmo com lógica aprovada. Não substitui o gate integral. O uso desse modo permite testar a correção enquanto a cópia instalada está em uso.

Pendências para integração/publicação: gate completo com o Overlay de uso fechado, atualização administrativa e nova medição durante partida com o FIFA em primeiro plano. A candidata não foi anunciada como release pública nem como redução comprovada da RAM em jogo.
