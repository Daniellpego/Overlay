param([switch]$SemCompilar, [switch]$PermitirTrabalhoLocal)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'distribuicao.psm1') -Force -DisableNameChecking
$raiz = Split-Path $PSScriptRoot -Parent
if (-not $SemCompilar) {
    & (Join-Path $raiz 'build.bat')
    if ($LASTEXITCODE -ne 0) { throw 'Compilacao falhou; nenhum pacote foi publicado.' }
}
$pacoteInfo = Test-PacoteOverlay $raiz
$estado = @(git -C $raiz status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel consultar o repositorio.' }
if ($estado.Count -gt 0 -and -not $PermitirTrabalhoLocal) { throw 'A arvore de trabalho precisa estar limpa para gerar a distribuicao oficial.' }
$revisao = (git -C $raiz rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel identificar a revisao.' }
$pm = Get-CaminhoFilhoOverlay $raiz 'tools/PresentMon/PresentMon-2.6.0-x64.exe'
$assinatura = Get-AuthenticodeSignature -LiteralPath $pm
if ($assinatura.Status -ne 'Valid' -or $assinatura.SignerCertificate.Subject -notmatch 'O=Intel Corporation') {
    throw 'A assinatura original da Intel nao foi validada. Pacote nao gerado.'
}
$nome = 'Overlay-v' + $pacoteInfo.Versao + '-win-x64'
$temporaria = Join-Path $raiz ('.tmp\pacotes\' + [Guid]::NewGuid().ToString('N'))
$pasta = Join-Path $temporaria $nome
$entradas = @()
foreach ($relativo in Get-ArquivosOverlay) {
    $destino = Get-CaminhoFilhoOverlay $pasta $relativo
    New-Item -ItemType Directory -Path (Split-Path $destino -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Get-CaminhoFilhoOverlay $raiz $relativo) -Destination $destino
    $entradas += [pscustomobject]@{ caminho=$relativo; tamanho=(Get-Item -LiteralPath $destino).Length; sha256=(Get-FileHash -LiteralPath $destino -Algorithm SHA256).Hash }
}
$manifesto = [ordered]@{
    produto='Overlay'; versao=$pacoteInfo.Versao; arquitetura='x64'; revisao=$revisao;
    trabalho_local=($estado.Count -gt 0); criado_utc=[DateTime]::UtcNow.ToString('o'); arquivos=$entradas
}
[IO.File]::WriteAllText((Join-Path $pasta 'manifesto.json'),($manifesto | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$null = Test-PacoteOverlay $pasta -ExigirManifesto
$saida = Join-Path $raiz '.tmp\releases'
New-Item -ItemType Directory -Path $saida -Force | Out-Null
$zip = Join-Path $saida ($nome + '.zip')
Compress-Archive -LiteralPath $pasta -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText(($zip + '.sha256'),($hash + '  ' + [IO.Path]::GetFileName($zip) + "`r`n"),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Zip=$zip; Sha256=$hash; Versao=$pacoteInfo.Versao; Revisao=$revisao; TrabalhoLocal=($estado.Count -gt 0) }
