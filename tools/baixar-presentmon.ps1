# Baixa o PresentMon oficial da Intel (GitHub GameTechDev/PresentMon) e confere hash e assinatura.
# O .exe não vai para o git; rode este script numa máquina nova.
$ErrorActionPreference = 'Stop'
$versao = '2.6.0'
$sha256 = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'
$url = "https://github.com/GameTechDev/PresentMon/releases/download/v$versao/PresentMon-$versao-x64.exe"
$destino = Join-Path $PSScriptRoot "PresentMon\PresentMon-$versao-x64.exe"

New-Item -ItemType Directory -Force (Split-Path $destino) | Out-Null
if (-not (Test-Path $destino)) { Invoke-WebRequest $url -OutFile $destino -UseBasicParsing }

if ((Get-FileHash $destino -Algorithm SHA256).Hash -ne $sha256) {
    Remove-Item $destino
    throw 'Hash do PresentMon nao confere; arquivo removido.'
}
$assinatura = Get-AuthenticodeSignature $destino
if ($assinatura.Status -ne 'Valid' -or $assinatura.SignerCertificate.Subject -notmatch 'O=Intel Corporation') {
    throw "Assinatura invalida: $($assinatura.Status)"
}
Write-Host "PresentMon $versao OK em $destino"
