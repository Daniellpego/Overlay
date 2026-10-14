# Diagnóstico do "acesso negado" ao ligar o FPS/ping. Rode por diagnosticar.bat (pede administrador).
# Só lê informações e cria/para sessões ETW de teste por alguns segundos; não mexe no jogo nem em configurações.
# Resultado em .tmp\diagnostico.txt (o Claude lê esse arquivo).
$raiz = Split-Path $PSScriptRoot -Parent
$saida = Join-Path $raiz '.tmp\diagnostico.txt'
New-Item -ItemType Directory -Force (Split-Path $saida) | Out-Null
$linhas = New-Object System.Collections.Generic.List[string]
function Anotar($texto) { $linhas.Add([string]$texto); Write-Host $texto }

Anotar "Diagnostico ETW - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal $id
Anotar "Usuario do processo: $($id.Name)"
Anotar "Elevado (admin): $($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
Anotar "Nivel de integridade: $((whoami /groups | Select-String 'Mandatory Label|Rótulo Obrigatório|Rotulo Obrigatorio' | Select-Object -First 1).Line.Trim())"
$protecao = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue
Anotar "TypeOfAdminApprovalMode (2 = Protecao de administrador): $($protecao.TypeOfAdminApprovalMode)"
Anotar "FC27 aberto: $([bool](Get-Process FC27 -ErrorAction SilentlyContinue))"
Anotar "EAAntiCheat.GameService rodando: $([bool](Get-Process EAAntiCheat.GameService -ErrorAction SilentlyContinue))"
$sessoes = logman query -ets 2>&1
Anotar "Sessoes ETW ativas (linhas): $(@($sessoes).Count)"

function TestarLogman($nome, $argumentos) {
    $r = & logman start $nome @argumentos -ets 2>&1
    $ok = $LASTEXITCODE -eq 0
    Anotar ("[{0}] {1}: {2}" -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $nome, (($r | Out-String).Trim() -replace '\s+', ' '))
    if ($ok) { & logman stop $nome -ets 2>&1 | Out-Null }
}
TestarLogman 'DiagOverlayVazia' @('-rt')
TestarLogman 'DiagOverlayRede' @('-p', 'Microsoft-Windows-Kernel-Network', '0x10', '-rt')
TestarLogman 'DiagOverlayDxgi' @('-p', 'Microsoft-Windows-DXGI', '-rt')

$pm = 'C:\Program Files\Overlay\tools\PresentMon\PresentMon-2.6.0-x64.exe'
if (-not (Test-Path $pm)) { $pm = Join-Path $raiz 'tools\PresentMon\PresentMon-2.6.0-x64.exe' }
if (Test-Path $pm) {
    $r = & $pm --output_stdout --no_console_stats --timed 3 --terminate_after_timed --session_name DiagOverlayPM --stop_existing_session 2>&1 | Select-Object -First 4
    Anotar "PresentMon (3 s, codigo $LASTEXITCODE): $(($r | Out-String).Trim() -replace '\s+', ' ')"
} else { Anotar 'PresentMon nao encontrado' }

Get-WinEvent -LogName 'Microsoft-Windows-Kernel-EventTracing/Admin' -MaxEvents 8 -ErrorAction SilentlyContinue |
    Where-Object { $_.TimeCreated -gt (Get-Date).AddMinutes(-2) } |
    ForEach-Object { Anotar "Evento ETW $($_.Id) $($_.TimeCreated.ToString('HH:mm:ss')): $(($_.Message -split "`n")[0])" }

[IO.File]::WriteAllLines($saida, $linhas, (New-Object Text.UTF8Encoding $false))
Write-Host ''
Write-Host "Pronto. Resultado salvo em $saida - pode fechar esta janela e avisar o Claude."
