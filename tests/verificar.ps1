# Verificação automática do overlay: abre o Overlay.exe, confere a janela e fecha.
# Uso: powershell -ExecutionPolicy Bypass -File tests\verificar.ps1 [-Exe caminho\Overlay.exe]
param([string]$Exe = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Overlay.exe'))
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
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$pastaTemp = Join-Path $raiz '.tmp'
New-Item -ItemType Directory -Force $pastaTemp | Out-Null
$exeTeste = Join-Path $pastaTemp 'TesteCalculadoraFps.exe'
& $csc /nologo /warnaserror+ /codepage:65001 "/out:$exeTeste" (Join-Path $raiz 'src\CalculadoraFps.cs') (Join-Path $PSScriptRoot 'TesteCalculadoraFps.cs') | Write-Host
Checar 'compila testes de FPS' ($LASTEXITCODE -eq 0) ''
& $exeTeste | Write-Host
Checar 'testes de calculo de FPS' ($LASTEXITCODE -eq 0) ''

Get-Process Overlay -ErrorAction SilentlyContinue | Stop-Process -Force
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

$proc2 = Start-Process $exe -PassThru
Start-Sleep -Seconds 1
Checar 'segunda instancia fecha sozinha' ($proc2.HasExited) ''

Stop-Process -Id $proc.Id -Force
Write-Host "Print: $print"
if ($falhas -gt 0) { Write-Host "$falhas falha(s)"; exit 1 } else { Write-Host 'Tudo OK' }
