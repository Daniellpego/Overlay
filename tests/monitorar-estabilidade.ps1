# Observa o overlay instalado (sem mexer nele) por N minutos: a cada 30 s grava se está vivo, CPU, RAM privada,
# handles e threads; a cada 5 min tira um print só da janela do overlay. Resultado em .tmp\estabilidade.csv e .tmp\estab-*.png.
# Não precisa de admin (usa os contadores de desempenho do Windows). Uso: monitorar-estabilidade.ps1 -Minutos 60
param([int]$Minutos = 60)
$raiz = Split-Path $PSScriptRoot -Parent
$tmp = Join-Path $raiz '.tmp'
$csv = Join-Path $tmp 'estabilidade.csv'
New-Item -ItemType Directory -Force $tmp | Out-Null
Remove-Item (Join-Path $tmp 'estab-*.png') -ErrorAction SilentlyContinue

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class JanelaOverlay {
  delegate bool P(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Ri,B; }
  [DllImport("user32.dll")] static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static R Achar(uint alvo){ R res = new R(); EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==alvo && IsWindowVisible(h)){ GetWindowRect(h,out res);} return true;}, IntPtr.Zero); return res; }
}
'@
Add-Type -AssemblyName System.Drawing
[void][JanelaOverlay]::SetProcessDPIAware()

'hora;pid;vivo;cpu_pct;ram_privada_mb;handles;threads;fc_aberto' | Set-Content $csv -Encoding UTF8
$nucleos = [Environment]::ProcessorCount
$fim = (Get-Date).AddMinutes($Minutos)
$amostra = 0
while ((Get-Date) -lt $fim) {
    $p = Get-CimInstance Win32_Process -Filter "Name='Overlay.exe'" | Select-Object -First 1
    $fc = [bool](Get-Process FC27 -ErrorAction SilentlyContinue)
    if ($p) {
        $perf = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$($p.ProcessId)"
        $cpu = [math]::Round($perf.PercentProcessorTime / $nucleos, 2)
        $ram = [math]::Round($perf.WorkingSetPrivate / 1MB, 1)
        "{0};{1};sim;{2};{3};{4};{5};{6}" -f (Get-Date -Format 'HH:mm:ss'), $p.ProcessId, $cpu, $ram, $perf.HandleCount, $perf.ThreadCount, $fc | Add-Content $csv
        if ($amostra % 10 -eq 0) {
            $r = [JanelaOverlay]::Achar([uint32]$p.ProcessId)
            if ($r.Ri -gt $r.L) {
                $bmp = New-Object System.Drawing.Bitmap ($r.Ri - $r.L), ($r.B - $r.T)
                $g = [System.Drawing.Graphics]::FromImage($bmp)
                $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
                $bmp.Save((Join-Path $tmp ("estab-{0}.png" -f (Get-Date -Format 'HHmm'))))
                $g.Dispose(); $bmp.Dispose()
            }
        }
    } else {
        "{0};;NAO;;;;;{1}" -f (Get-Date -Format 'HH:mm:ss'), $fc | Add-Content $csv
    }
    $amostra++
    Start-Sleep -Seconds 30
}
'fim' | Add-Content $csv
