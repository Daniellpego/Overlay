# Verificação com tudo ligado (PresentMon + sessão ETW de rede). Precisa de administrador: rode verificar-admin.bat.
# Mede CPU e RAM somando Overlay + PresentMon, confere as sessões ETW e se elas somem ao fechar.
# Para uma medida realista, deixe algo apresentando frames (jogo, vídeo, navegador animado) durante os 30 s.
param([string]$Exe = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Overlay.exe'))
$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { Write-Host 'Rode como administrador (verificar-admin.bat).'; exit 1 }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FecharOverlay {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    public static void Pedir(uint alvo) {
        EnumWindows((h, l) => { uint pid; GetWindowThreadProcessId(h, out pid); if (pid == alvo) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); return true; }, IntPtr.Zero);
    }
}
'@

$falhas = 0
function Checar($nome, $ok, $detalhe) {
    if ($ok) { Write-Host "[OK]    $nome $detalhe" } else { Write-Host "[FALHA] $nome $detalhe"; $script:falhas++ }
}
function Sessoes { (logman query -ets) -join "`n" }

# Mesma regra do src\Pastas.cs: cópia instalada em Program Files grava em <pasta>\log; fora dela, ao lado do exe.
$pastaExe = Split-Path (Resolve-Path $Exe) -Parent
$instalado = $pastaExe.StartsWith($env:ProgramFiles.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
$log = if ($instalado) { Join-Path $pastaExe 'log\overlay.log' } else { Join-Path $pastaExe 'overlay.log' }
Write-Host "Testando $Exe (log: $log)"
$linhasAntes = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }

$exeTeste = (Resolve-Path $Exe).Path
$raizTeste = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\') + '\'
if (-not $exeTeste.StartsWith($raizTeste, [StringComparison]::OrdinalIgnoreCase) -or $instalado) {
    throw 'Verificação recusada: use somente uma cópia de teste dentro do projeto, fora de Program Files.'
}
foreach ($p in Get-CimInstance Win32_Process -Filter "Name='Overlay.exe'") {
    if (-not $p.ExecutablePath -or $p.ExecutablePath -ine $exeTeste) {
        Write-Host 'INCONCLUSIVO: overlay de uso aberto; preservado. Testes administrativos não executados.'
        exit 2
    }
    $processoTeste = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
    if ($processoTeste) {
        [FecharOverlay]::Pedir([uint32]$p.ProcessId)
        if (-not $processoTeste.WaitForExit(5000)) { throw 'Cópia de teste não encerrou; teste cancelado.' }
    }
}
$overlay = Start-Process $Exe -PassThru
Write-Host 'Aguardando 10 s para tudo subir...'
Start-Sleep -Seconds 10

$filho = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($overlay.Id)" | Where-Object { $_.Name -like 'PresentMon*' }
Checar 'PresentMon rodando como filho' ($null -ne $filho) ''
$sessoes = Sessoes
Checar 'sessao ETW OverlayFps ativa' ($sessoes -match 'OverlayFps') ''
Checar 'sessao ETW OverlayRede ativa' ($sessoes -match 'OverlayRede') ''

$processos = @($overlay) + @(if ($filho) { Get-Process -Id $filho.ProcessId })
$cpu0 = ($processos | ForEach-Object { $_.Refresh(); $_.TotalProcessorTime.TotalMilliseconds } | Measure-Object -Sum).Sum
Write-Host 'Medindo CPU por 30 s (deixe um jogo/video rodando se puder)...'
Start-Sleep -Seconds 30
$cpu1 = ($processos | ForEach-Object { $_.Refresh(); $_.TotalProcessorTime.TotalMilliseconds } | Measure-Object -Sum).Sum
$cpuPct = (($cpu1 - $cpu0) / 30000) / [Environment]::ProcessorCount * 100
$ramMb = [math]::Round((($processos | ForEach-Object { $_.WorkingSet64 } | Measure-Object -Sum).Sum) / 1MB, 1)
Checar 'CPU total (Overlay + PresentMon) < 1%' ($cpuPct -lt 1) ("({0:N3}%)" -f $cpuPct)
Checar 'RAM total (Overlay + PresentMon) < 50 MB' ($ramMb -lt 50) "($ramMb MB)"

$novas = if (Test-Path $log) { Get-Content $log | Select-Object -Skip $linhasAntes } else { @() }
$errosEtw = $novas | Where-Object { $_ -match 'OpenTrace|ProcessTrace|StartTrace|EnableTraceEx2|PresentMon saiu|Hash do PresentMon' }
Checar 'sem erros de ETW/PresentMon no overlay.log' ($errosEtw.Count -eq 0) ($errosEtw -join ' / ')

[FecharOverlay]::Pedir([uint32]$overlay.Id)
Checar 'overlay fecha pelo WM_CLOSE' ($overlay.WaitForExit(5000)) ''
Start-Sleep -Seconds 1
if ($filho) { Checar 'PresentMon encerrado junto' (-not (Get-Process -Id $filho.ProcessId -ErrorAction SilentlyContinue)) '' }
$sessoes = Sessoes
Checar 'sessao OverlayFps encerrada ao fechar' (-not ($sessoes -match 'OverlayFps')) ''
Checar 'sessao OverlayRede encerrada ao fechar' (-not ($sessoes -match 'OverlayRede')) ''

if ($falhas -gt 0) { Write-Host "$falhas falha(s)"; exit 1 } else { Write-Host 'Tudo OK (admin)' }
