# Overlay (horário, ping, FPS)

Overlay leve p/ jogos Windows 11. Escopo, restrições, critérios de falha em `CONTRATO.md`; ler antes de mudar comportamento.

## Comandos
- Compilar: `cmd /c build.bat` (gera `Overlay.exe` na raiz; aviso = erro).
- Verificar: `powershell -ExecutionPolicy Bypass -File tests\verificar.ps1` (abre, confere janela, fecha).
  Sandbox Claude Code: antes definir `TEMP`/`TMP` = `E:\overlay\.tmp` (Add-Type não escreve no TEMP padrão).
- PresentMon: `tools\baixar-presentmon.ps1` (confere SHA-256 + assinatura Intel).
- Etapa pronta só com: compilar sem erro + `tests\verificar.ps1` dando "Tudo OK".

## Convenções
- C# 5 (csc .NET Framework 4.x): sem `$""`, `?.`, `nameof`, propriedades `=>`.
- Código e textos em português, sem abreviação obscura.
- `.bat` e `.ps1` exigem CRLF (cmd quebra com LF).
- Nada injeta, lê memória ou abre handle no processo do jogo (anticheat).
- Valor inválido no `config.ini` → mantém padrão; overlay nunca deixa de abrir por config.
- Erro não tratado → `overlay.log`, nunca caixa de diálogo no meio do jogo.

## Forma de trabalhar
- Uma etapa do `CONTRATO.md` por vez, commit ao fim de cada.
- Prints de teste (`tests\ultimo-print.png`) capturam tela do Daniel: não comentar nada além do overlay.

## Regras aprendidas
- Ao iniciar, ler esta seção. Daniel corrigiu algo → acrescentar `N. [categoria] SEMPRE/NUNCA <ação> porque <motivo>.`
1. [winforms] SEMPRE primeiro UpdateLayeredWindow em OnShown + sincronizar `Bounds` porque Show() do WinForms sobrescreve posição e tamanho.