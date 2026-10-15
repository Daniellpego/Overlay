# Verificação automática do overlay: abre o Overlay.exe, confere a janela e fecha.
# Uso: powershell -ExecutionPolicy Bypass -File tests\verificar.ps1 [-Exe caminho\Overlay.exe]
param([string]$Exe = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Overlay.exe'), [switch]$SomenteLogica)
$ErrorActionPreference = 'Stop'
$exe = $Exe

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class W {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct PT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(PT p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static void CtrlShiftF10() {
        keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x10, 0, 0, UIntPtr.Zero); keybd_event(0x79, 0, 0, UIntPtr.Zero);
        keybd_event(0x79, 0, 2, UIntPtr.Zero); keybd_event(0x10, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public static List<IntPtr> Janelas(uint pid) {
        var lista = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) lista.Add(h); return true; }, IntPtr.Zero);
        return lista;
    }
}
'@
[W]::SetProcessDPIAware() | Out-Null

$falhas = 0
function Checar($nome, $ok, $detalhe) {
    if ($ok) { Write-Host "[OK]    $nome $detalhe" } else { Write-Host "[FALHA] $nome $detalhe"; $script:falhas++ }
}

# Testes de lógica pura (sem janela nem admin).
$raiz = Split-Path $PSScriptRoot -Parent
$exeTeste = (Resolve-Path $exe).Path
$raizTeste = [IO.Path]::GetFullPath($raiz).TrimEnd('\') + '\'
if (-not $exeTeste.StartsWith($raizTeste, [StringComparison]::OrdinalIgnoreCase) -or
    $exeTeste.StartsWith($env:ProgramFiles.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Verificação recusada: nunca testar nem fechar o overlay instalado.'
}
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pastaTemp = Join-Path $raiz '.tmp'
New-Item -ItemType Directory -Force $pastaTemp | Out-Null
function RodarTeste($nome, $fontes) {
    $saida = Join-Path $pastaTemp "$nome.exe"
    $arquivos = $fontes | ForEach-Object { Join-Path $raiz $_ }
    & $csc /nologo /platform:x64 /warnaserror+ /codepage:65001 "/main:$nome" "/out:$saida" $arquivos | Write-Host
    Checar "compila $nome" ($LASTEXITCODE -eq 0) ''
    if ($LASTEXITCODE -ne 0) { return }
    & $saida | Write-Host
    Checar $nome ($LASTEXITCODE -eq 0) ''
}
RodarTeste 'TesteCalculadoraFps' @('src\CalculadoraFps.cs', 'tests\TesteCalculadoraFps.cs')
RodarTeste 'TesteRegressoes' @('src\*.cs', 'tests\TesteRegressoes.cs')
RodarTeste 'TesteRede' @('src\DetectorServidor.cs', 'src\MedidorPing.cs', 'src\Log.cs', 'src\Pastas.cs', 'tests\TesteRede.cs')
RodarTeste 'TesteSessaoEtw' @('src\MonitorRede.cs', 'src\SessaoEtw.cs', 'src\DetectorServidor.cs', 'src\Log.cs', 'src\Pastas.cs', 'tests\TesteSessaoEtw.cs')
if (-not $SomenteLogica) {
    RodarTeste 'ProvaFpsTela' @('src\ContadorTela.cs', 'src\Native.cs', 'src\Log.cs', 'src\Pastas.cs', 'tests\ProvaFpsTela.cs')
} else { Write-Host '[PULADO] prova grafica: SomenteLogica preserva a partida e suas janelas.' }
RodarTeste 'ProvaPing' @('src\MedidorPing.cs', 'src\DetectorServidor.cs', 'src\Log.cs', 'src\Pastas.cs', 'tests\ProvaPing.cs')
RodarTeste 'TesteEtapa4' @('src\Atalho.cs', 'src\Faixas.cs', 'src\Config.cs', 'src\InicioAutomatico.cs', 'src\Log.cs', 'src\Pastas.cs', 'tests\TesteEtapa4.cs')

if ($SomenteLogica) {
    if ($falhas -gt 0) { Write-Host "$falhas falha(s)"; exit 1 }
    Write-Host 'INCONCLUSIVO para o gate completo: logica aprovada; provas graficas nao executadas.'
    exit 2
}

# Só fecha cópias de teste desta pasta. NUNCA fecha o overlay do Daniel (instalado ou de caminho desconhecido):
# em 2026-10-14 este script derrubou o overlay instalado duas vezes no meio do jogo.
$exeTeste = (Resolve-Path $exe).Path
foreach ($outro in Get-CimInstance Win32_Process -Filter "Name='Overlay.exe'") {
    if ($outro.ExecutablePath -and $outro.ExecutablePath -ieq $exeTeste) {
        Stop-Process -Id $outro.ProcessId -Force -ErrorAction SilentlyContinue
        continue
    }
    # O mutex de instância única faria a cópia de teste sair na hora; sem fechar o do Daniel, os testes da janela ficam para depois.
    Write-Host '[PULADO] testes da janela: ha um overlay seu aberto (instalado). Feche pela bandeja > Sair e rode de novo.'
    if ($falhas -gt 0) { Write-Host "$falhas falha(s)"; exit 1 } else { Write-Host 'INCONCLUSIVO: lógica aprovada; janela não verificada.'; exit 2 }
}
$focoAntes = [W]::GetForegroundWindow()
$proc = Start-Process $exe -PassThru
Start-Sleep -Seconds 2

$janelas = [W]::Janelas([uint32]$proc.Id)
Checar 'janela visivel' ($janelas.Count -ge 1) "($($janelas.Count) janela(s))"
$h = $janelas | Select-Object -First 1

$ex = [W]::GetWindowLong($h, -20)
Checar 'click-through (WS_EX_TRANSPARENT)' (($ex -band 0x20) -ne 0) ''
Checar 'layered (WS_EX_LAYERED)'           (($ex -band 0x80000) -ne 0) ''
Checar 'sempre no topo (WS_EX_TOPMOST)'    (($ex -band 0x8) -ne 0) ''
Checar 'fora da barra/Alt+Tab (TOOLWINDOW)' (($ex -band 0x80) -ne 0) ''
Checar 'nao rouba foco (NOACTIVATE)'       (($ex -band 0x08000000) -ne 0) ''
Checar 'foco continua com quem tinha'      ([W]::GetForegroundWindow() -eq $focoAntes) ''

$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
Checar 'tamanho pequeno' ((($r.R - $r.L) -lt 600) -and (($r.B - $r.T) -lt 100)) "($($r.R - $r.L)x$($r.B - $r.T) em $($r.L),$($r.T))"

$centro = New-Object W+PT
$centro.X = [int](($r.L + $r.R) / 2); $centro.Y = [int](($r.T + $r.B) / 2)
Checar 'clique atravessa (outra janela recebe)' ([W]::WindowFromPoint($centro) -ne $h) ''

# Print da regiao do overlay para conferencia visual.
Add-Type -AssemblyName System.Drawing
$w = $r.R - $r.L + 40; $hh = $r.B - $r.T + 40
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L - 20, $r.T - 20, 0, 0, $bmp.Size)
$print = Join-Path $PSScriptRoot 'ultimo-print.png'
$bmp.Save($print); $g.Dispose(); $bmp.Dispose()

# Uso de recursos depois de ~6 s rodando.
$proc.Refresh(); $cpu0 = $proc.TotalProcessorTime
Start-Sleep -Seconds 4
$proc.Refresh()
$cpuPct = (($proc.TotalProcessorTime - $cpu0).TotalMilliseconds / 4000) / [Environment]::ProcessorCount * 100
$ramMb = [math]::Round($proc.WorkingSet64 / 1MB, 1)
Checar 'CPU < 1%' ($cpuPct -lt 1) ("({0:N3}%)" -f $cpuPct)
Checar 'RAM < 50 MB' ($ramMb -lt 50) "($ramMb MB)"

# Atalho padrão do config.ini (Ctrl+Shift+F10) oculta e mostra de novo.
[W]::CtrlShiftF10(); Start-Sleep -Milliseconds 700
Checar 'atalho oculta o overlay' (-not [W]::IsWindowVisible($h)) ''
[W]::CtrlShiftF10(); Start-Sleep -Milliseconds 700
Checar 'atalho mostra de novo' ([W]::IsWindowVisible($h)) ''
$r2 = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r2) | Out-Null
Checar 'volta no mesmo lugar e tamanho' (($r2.L -eq $r.L) -and ($r2.T -eq $r.T) -and ($r2.R -eq $r.R)) "($($r2.L),$($r2.T))"

$proc2 = Start-Process $exe -PassThru
Start-Sleep -Seconds 1
Checar 'segunda instancia fecha sozinha' ($proc2.HasExited) ''

Stop-Process -Id $proc.Id -Force
Write-Host "Print: $print"
if ($falhas -gt 0) { Write-Host "$falhas falha(s)"; exit 1 } else { Write-Host 'Tudo OK' }
