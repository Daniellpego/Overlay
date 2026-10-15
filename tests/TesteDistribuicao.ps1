$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $raiz 'tools\distribuicao.psm1') -Force -DisableNameChecking
$falhas = 0
function Checar([string]$Nome, [bool]$Ok) {
    if ($Ok) { Write-Output ("[OK] " + $Nome) }
    else { Write-Output ("[FALHA] " + $Nome); $script:falhas++ }
}
function Recusa([scriptblock]$Acao) { try { & $Acao | Out-Null; $false } catch { $true } }

# Lista vazia carrega a API de fechamento sem autorizar encerrar nenhuma copia.
Stop-ProcessosOverlay @()
Checar 'API de fechamento carrega sem tocar em processos de usuario' $true

$resultado = & (Join-Path $raiz 'tools\empacotar.ps1') -SemCompilar -PermitirTrabalhoLocal
$pastaTeste = Join-Path $raiz (".tmp\Teste de pacote d'usuario & acentos-ação-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $pastaTeste -Force | Out-Null
Expand-Archive -LiteralPath $resultado.Zip -DestinationPath $pastaTeste
$pacote = Join-Path $pastaTeste ('Overlay-v' + $resultado.Versao + '-win-x64')
$verificacao = Test-PacoteOverlay $pacote -ExigirManifesto
Checar 'ZIP extraido preserva integridade em pasta com espacos, apostrofo, & e acentos' ($verificacao.Versao -eq '0.1.1')
Checar 'todos os arquivos de uso acompanham o ZIP' ($verificacao.Arquivos -eq @(Get-ArquivosOverlay).Count)

# Processo novo do PowerShell 5.1, sem tipos em cache, TEMP impossivel de usar como pasta.
$tempBloqueado = Join-Path $pastaTeste 'temp-e-um-arquivo'
[IO.File]::WriteAllText($tempBloqueado,'nao e uma pasta')
$testeSemTemp = Join-Path $pastaTeste 'testar-sem-temp.ps1'
$codigoTeste = @'
param([string]$Raiz,[string]$Pasta,[string]$TempBloqueado,[switch]$Remover)
$ErrorActionPreference = 'Stop'
$env:TEMP=$TempBloqueado
$env:TMP=$TempBloqueado
function Add-Type { throw 'Compilacao em tempo de execucao proibida neste teste.' }
Import-Module (Join-Path $Raiz 'tools/distribuicao.psm1') -Force -DisableNameChecking
Stop-ProcessosOverlay @()
Write-ConfigInicialOverlay (Join-Path $Raiz 'config.ini') $Pasta
if ((Get-FileHash -LiteralPath (Join-Path $Pasta 'config.ini')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Raiz 'config.ini')).Hash) { throw 'Configuracao nao inicializada.' }
if ($Remover) {
    Remove-ProgramaOverlay $Raiz
    if (Test-Path -LiteralPath (Join-Path $Raiz 'tools/Overlay.Distribuicao.dll')) { throw 'A propria DLL ficou bloqueada durante a remocao.' }
    if (Test-Path -LiteralPath (Join-Path $Raiz 'Overlay.exe')) { throw 'Remocao nao foi concluida.' }
}
Write-Output 'Auxiliares OK sem compilador e sem TEMP.'
'@
[IO.File]::WriteAllText($testeSemTemp,($codigoTeste -replace '\r?\n',"`r`n"),[Text.UTF8Encoding]::new($true))
$saidaSemTemp = & (Get-PowerShellOverlay) -NoProfile -ExecutionPolicy Bypass -File $testeSemTemp -Raiz $pacote -Pasta (Join-Path $pastaTeste 'perfil sem TEMP') -TempBloqueado $tempBloqueado
Checar 'ZIP carrega auxiliares e cria config no PowerShell 5.1 sem compilacao e com TEMP invalido' ($LASTEXITCODE -eq 0 -and ($saidaSemTemp -join '') -match 'Auxiliares OK')
Checar 'DLL precompilada acompanha o pacote' (Test-Path -LiteralPath (Join-Path $pacote 'tools/Overlay.Distribuicao.dll'))
$destinoAutoRemocao = Join-Path $pastaTeste 'instalacao com DLL carregada'
Copy-ProgramaOverlay $pacote $destinoAutoRemocao
$saidaAutoRemocao = & (Get-PowerShellOverlay) -NoProfile -ExecutionPolicy Bypass -File $testeSemTemp -Raiz $destinoAutoRemocao -Pasta (Join-Path $pastaTeste 'perfil da remocao') -TempBloqueado $tempBloqueado -Remover
Checar 'PowerShell 5.1 remove a propria DLL carregada sem bloqueio de arquivo' ($LASTEXITCODE -eq 0 -and ($saidaAutoRemocao -join '') -match 'Auxiliares OK' -and -not (Test-Path -LiteralPath (Join-Path $destinoAutoRemocao 'Overlay.exe')))
Checar 'pacote nao inclui codigo, testes, logs nem temporarios' (-not (Test-Path (Join-Path $pacote 'src')) -and -not (Test-Path (Join-Path $pacote 'tests')) -and -not (Test-Path (Join-Path $pacote '.tmp')) -and -not (Test-Path (Join-Path $pacote 'overlay.log')))
$hashEsperado = ([IO.File]::ReadAllText($resultado.Zip + '.sha256') -split '\s+')[0]
Checar 'SHA-256 publicado corresponde ao ZIP' ((Get-FileHash -LiteralPath $resultado.Zip -Algorithm SHA256).Hash -eq $hashEsperado)
Checar 'caminho que escapa da raiz e recusado' (Recusa { Get-CaminhoFilhoOverlay $pacote '..\fora.txt' })
$manifestoCaminho = Join-Path $pacote 'manifesto.json'
$manifestoOriginal = [IO.File]::ReadAllText($manifestoCaminho)
$manifesto = $manifestoOriginal | ConvertFrom-Json
$manifesto.arquivos[0].caminho = '../fora.exe'
[IO.File]::WriteAllText($manifestoCaminho,($manifesto | ConvertTo-Json -Depth 5))
Checar 'manifesto com caminho arbitrario e recusado' (Recusa { Test-PacoteOverlay $pacote -ExigirManifesto })
[IO.File]::WriteAllText($manifestoCaminho,$manifestoOriginal)
$config = Join-Path $pacote 'config.ini'
$configOriginal = [IO.File]::ReadAllBytes($config)
[IO.File]::AppendAllText($config,"`r`n# adulterado")
Checar 'arquivo alterado depois do empacotamento e recusado' (Recusa { Test-PacoteOverlay $pacote -ExigirManifesto })
[IO.File]::WriteAllBytes($config,$configOriginal)
$guia = Join-Path $pacote 'LEIA-ME.txt'
$guiaOriginal = [IO.File]::ReadAllBytes($guia)
Remove-Item -LiteralPath $guia
Checar 'arquivo ausente e recusado' (Recusa { Test-PacoteOverlay $pacote -ExigirManifesto })
[IO.File]::WriteAllBytes($guia,$guiaOriginal)

$destino = Join-Path $pastaTeste 'instalacao isolada'
Copy-ProgramaOverlay $pacote $destino
Checar 'copia do programa e validada sem tocar em Program Files' ((Test-PacoteOverlay $destino -ExigirManifesto).Versao -eq $resultado.Versao)
$configUsuario = Join-Path $pastaTeste 'preferencias-do-usuario.ini'
[IO.File]::WriteAllText($configUsuario,'canto = inferior-esquerdo')
Copy-ProgramaOverlay $pacote $destino
Checar 'atualizacao nao altera preferencias fora da pasta do programa' ([IO.File]::ReadAllText($configUsuario) -eq 'canto = inferior-esquerdo')
Checar 'atualizacao remove suas pastas temporarias' (@(Get-ChildItem -LiteralPath $destino -Directory -Force | Where-Object {$_.Name -like '.preparar-*' -or $_.Name -like '.anterior-*'}).Count -eq 0)

$pastaConfig = Join-Path $pastaTeste 'perfil/configuracao'
Write-ConfigInicialOverlay $config $pastaConfig
Checar 'primeira configuracao usa criacao exclusiva segura, inclusive para chamada elevada' ((Get-FileHash -LiteralPath (Join-Path $pastaConfig 'config.ini')).Hash -eq (Get-FileHash -LiteralPath $config).Hash)
[IO.File]::WriteAllText((Join-Path $pastaConfig 'config.ini'),'canto = inferior-esquerdo')
Write-ConfigInicialOverlay $config $pastaConfig
Checar 'inicializacao preserva configuracao existente' ([IO.File]::ReadAllText((Join-Path $pastaConfig 'config.ini')) -eq 'canto = inferior-esquerdo')
$juncao = Join-Path $pastaTeste 'juncao'
New-Item -ItemType Junction -Path $juncao -Target $pastaConfig | Out-Null
Checar 'criacao segura recusa ancestral que seja juncao' (Recusa { Write-ConfigInicialOverlay $config (Join-Path $juncao 'nova') })
Checar 'juncao recusada nao cria pasta no alvo' (-not (Test-Path -LiteralPath (Join-Path $pastaConfig 'nova')))
$colisao = Join-Path $pastaTeste 'outro produto'
New-Item -ItemType Directory -Path $colisao | Out-Null
Copy-Item -LiteralPath (Get-PowerShellOverlay) -Destination (Join-Path $colisao 'Overlay.exe')
$hashOutro = (Get-FileHash -LiteralPath (Join-Path $colisao 'Overlay.exe')).Hash
Checar 'instalacao recusa executavel de outro produto antes de copiar' (Recusa { Copy-ProgramaOverlay $pacote $colisao })
Checar 'colisao preserva executavel e nao cria arquivos' ((Get-FileHash -LiteralPath (Join-Path $colisao 'Overlay.exe')).Hash -eq $hashOutro -and @(Get-ChildItem -LiteralPath $colisao).Count -eq 1)

# Falha real de compartilhamento depois de arquivos anteriores ja terem sido substituidos.
[IO.File]::AppendAllText((Join-Path $destino 'config.ini'),"`r`n# versao anterior")
$hashesAntes = @{}
foreach ($nome in Get-ArquivosOverlay) { $hashesAntes[$nome] = (Get-FileHash -LiteralPath (Join-Path $destino $nome)).Hash }
$trava = [IO.File]::Open((Join-Path $destino 'LEIA-ME.txt'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { Checar 'falha no meio da atualizacao e comunicada' (Recusa { Copy-ProgramaOverlay $pacote $destino }) }
finally { $trava.Dispose() }
$restaurou = $true
foreach ($nome in $hashesAntes.Keys) { if ((Get-FileHash -LiteralPath (Join-Path $destino $nome)).Hash -ne $hashesAntes[$nome]) { $restaurou = $false } }
Checar 'atualizacao interrompida restaura todos os hashes anteriores' $restaurou

$script:finalizouRemocao = $false
$trava = [IO.File]::Open((Join-Path $destino 'LEIA-ME.txt'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { Checar 'falha real na exclusao e comunicada' (Recusa { Remove-ProgramaOverlay $destino { $script:finalizouRemocao = $true } }) }
finally { $trava.Dispose() }
Checar 'falha na remocao preserva registros e atalhos sem executar finalizacao' (-not $script:finalizouRemocao)
$restaurou = $true
foreach ($nome in $hashesAntes.Keys) { if ((Get-FileHash -LiteralPath (Join-Path $destino $nome)).Hash -ne $hashesAntes[$nome]) { $restaurou = $false } }
Checar 'remocao interrompida restaura programa inclusive desinstalador' $restaurou
New-Item -ItemType Directory -Path (Join-Path $destino 'log') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $destino 'log/overlay.log'),'preservar')
Remove-ProgramaOverlay $destino { $script:finalizouRemocao = $true }
Checar 'remocao bem sucedida finaliza e preserva logs e preferencias' ($script:finalizouRemocao -and -not (Test-Path -LiteralPath (Join-Path $destino 'Overlay.exe')) -and [IO.File]::ReadAllText((Join-Path $destino 'log/overlay.log')) -eq 'preservar' -and [IO.File]::ReadAllText($configUsuario) -eq 'canto = inferior-esquerdo')
Copy-ProgramaOverlay $pacote $destino
Checar 'reinstalacao aceita seus logs preservados' ((Test-PacoteOverlay $destino -ExigirManifesto).Versao -eq $resultado.Versao -and [IO.File]::ReadAllText((Join-Path $destino 'log/overlay.log')) -eq 'preservar')
Remove-Item -LiteralPath (Join-Path $destino 'Overlay.exe')
$script:finalizouRemocao = $false
Remove-ProgramaOverlay $destino { $script:finalizouRemocao = $true }
Checar 'instalacao sem executavel pode ser removida pela identidade do manifesto' ($script:finalizouRemocao -and -not (Test-Path -LiteralPath (Join-Path $destino 'tools/desinstalar.ps1')))

$ps = Get-PowerShellOverlay
$saidaVerificacao = & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pacote 'tools/instalar.ps1') -VerificarPacote
Checar 'instalador do ZIP verifica pacote em processo separado sem elevar nem instalar' ($LASTEXITCODE -eq 0 -and ($saidaVerificacao -join '') -match 'integridade OK')

$tokens = $null; $erros = $null
foreach ($arquivo in @('instalar.ps1','iniciar.ps1','desinstalar.ps1','empacotar.ps1','distribuicao.psm1')) {
    [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $raiz ('tools\' + $arquivo)),[ref]$tokens,[ref]$erros)
    Checar ("PowerShell valido: " + $arquivo) ($erros.Count -eq 0)
}
Write-Output ("Distribuicao: " + $falhas + ' falha(s)')
if ($falhas -gt 0) { exit 1 }
