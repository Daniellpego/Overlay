# Overlay (horário, ping, FPS)

Overlay leve p/ jogos Windows 11. Escopo, restrições, critérios de falha em `CONTRATO.md`; ler antes de mudar comportamento.

## Comandos
- Compilar: `cmd /c build.bat` (gera `Overlay.exe` na raiz; aviso = erro).
- Verificar: `powershell -ExecutionPolicy Bypass -File tests\verificar.ps1` (abre, confere janela, fecha).
  Sandbox Claude Code: antes definir `TEMP`/`TMP` = `E:\overlay\.tmp` (Add-Type não escreve no TEMP padrão).
- PresentMon: `tools\baixar-presentmon.ps1` (confere SHA-256 + assinatura Intel).
- Etapa pronta só com: compilar sem erro + `tests\verificar.ps1` dando "Tudo OK".
- Com admin (Daniel roda): `verificar-admin.bat` — CPU/RAM de Overlay + PresentMon, sessões ETW sobem e somem ao fechar.
- Instalar/atualizar a cópia de uso (Program Files): `instalar.bat` (admin). Só ela pode virar tarefa de logon.
- Uso real: `iniciar.bat` (UAC/admin, exigido pelo PresentMon e pelo ETW de rede). Sessão do Claude Code não é admin → FPS real e detecção de servidor só o Daniel valida. Lógica testável sem admin fica em classes puras (`CalculadoraFps`, `DetectorServidor`); testes em `tests\*.cs`, compilados pelo `verificar.ps1`.
- `MonitorRede` usa offsets x64 fixos de structs ETW (build `/platform:x64`); conferir contra o SDK antes de mexer — offset errado derruba o processo.

## Convenções
- C# 5 (csc .NET Framework 4.x): sem `$""`, `?.`, `nameof`, propriedades `=>`.
- Código e textos em português, sem abreviação obscura.
- `.bat` e `.ps1` exigem CRLF (cmd quebra com LF).
- Nada injeta ou lê memória do jogo; o overlay não abre handle nele (anticheat). Única exceção aceita: handle de consulta do PresentMon (CONTRATO.md).
- Tudo que roda elevado: exe do Windows por caminho absoluto (`Environment.SystemDirectory`); PresentMon com nome fixo e SHA-256 conferido antes de cada execução.
- P/Invoke de função com versão A/W: sempre `EntryPoint = "...W"` + `CharSet.Unicode` (o padrão Ansi quebrou o OpenTrace em silêncio).
- Valor inválido no `config.ini` → mantém padrão; overlay nunca deixa de abrir por config.
- Erro não tratado → `overlay.log`, nunca caixa de diálogo no meio do jogo.

## Forma de trabalhar
- Responder ao Daniel SEMPRE em português do Brasil (texto, recaps, perguntas e botões), mesmo com ferramentas/relatórios em inglês.
- Uma etapa do `CONTRATO.md` por vez, commit ao fim de cada.
- Prints de teste (`tests\ultimo-print.png`) capturam tela do Daniel: não comentar nada além do overlay.
- Testes NUNCA fecham o overlay instalado do Daniel (derrubaram duas vezes no meio do jogo): só a cópia de teste da pasta.
- Medir o plano B do FPS na tela real: compilar `tests\MedirTela.cs` com `src\ContadorTela.cs` e rodar `MedirTela.exe 10 FC27` (não precisa admin).
- Validação pelo Daniel: botões neutros (sem resposta de sucesso pré-preenchida) e pedir o `overlay.log`; nunca apagar o log antes de ele testar.
- Commit: mensagem via arquivo (`git commit -F`), UTF-8 sem BOM — aspas na mensagem quebram o argumento no PowerShell 5.1.

## Regras aprendidas
- Ao iniciar, ler esta seção. Daniel corrigiu algo → acrescentar `N. [categoria] SEMPRE/NUNCA <ação> porque <motivo>.`
1. [winforms] SEMPRE primeiro UpdateLayeredWindow em OnShown + sincronizar `Bounds` porque Show() do WinForms sobrescreve posição e tamanho.
2. [distribuição] NUNCA compilar auxiliares com Add-Type durante instalação/início/remoção; o Windows PowerShell 5.1 falha com TEMP indisponível. Distribuir a DLL compilada e testar esses auxiliares com TEMP inválido.
