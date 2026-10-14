# Instala (ou atualiza) o overlay em "C:\Program Files\Overlay", pasta que só administrador altera.
# É essa cópia que deve rodar elevada e iniciar com o Windows; E:\overlay continua sendo a pasta de desenvolvimento.
# config.ini da cópia instalada fica em %LOCALAPPDATA%\Overlay (editável sem admin; o overlay só lê dali).
# overlay.log fica em "C:\Program Files\Overlay\log" (o overlay roda elevado e só escreve em pasta de admin).
# Uso: botão direito > "Executar com o PowerShell" não serve (precisa admin). Rode instalar.bat na raiz do projeto.
$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw 'Rode como administrador (use instalar.bat).' }

$raiz = Split-Path $PSScriptRoot -Parent
$destino = Join-Path $env:ProgramFiles 'Overlay'
$presentMon = 'PresentMon-2.6.0-x64.exe'
$sha256 = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF'

$exe = Join-Path $raiz 'Overlay.exe'
if (-not (Test-Path $exe)) { throw 'Overlay.exe nao encontrado. Rode build.bat antes.' }
$pm = Join-Path $raiz "tools\PresentMon\$presentMon"
if (-not (Test-Path $pm) -or (Get-FileHash $pm -Algorithm SHA256).Hash -ne $sha256) {
    throw 'PresentMon ausente ou com hash errado. Rode tools\baixar-presentmon.ps1.'
}

# Fecha o overlay em execução pedindo para a janela fechar (assim ele encerra as sessões ETW direito).
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FecharOverlay {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    // A janela do overlay é "tool window" sem título (MainWindowHandle pode vir zerado): manda WM_CLOSE a todas as do processo.
    public static void Pedir(uint alvo) {
        EnumWindows((h, l) => { uint pid; GetWindowThreadProcessId(h, out pid); if (pid == alvo) PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); return true; }, IntPtr.Zero);
    }
}
'@
foreach ($p in Get-Process Overlay -ErrorAction SilentlyContinue) {
    [FecharOverlay]::Pedir([uint32]$p.Id)
    if (-not $p.WaitForExit(5000)) { $p.Kill(); $p.WaitForExit() }
}

New-Item -ItemType Directory -Force (Join-Path $destino 'tools\PresentMon') | Out-Null
Copy-Item $exe $destino -Force
Copy-Item $pm (Join-Path $destino 'tools\PresentMon') -Force

# Configuração do usuário: copia o config.ini do projeto só se ainda não existir (não apaga ajustes feitos).
$dados = Join-Path $env:LOCALAPPDATA 'Overlay'
New-Item -ItemType Directory -Force $dados | Out-Null
$configDestino = Join-Path $dados 'config.ini'
if (-not (Test-Path $configDestino)) { Copy-Item (Join-Path $raiz 'config.ini') $configDestino }

# Recria a tarefa "Iniciar com o Windows" apontando para a cópia instalada (substitui uma tarefa antiga que apontasse para E:\overlay).
$exeInstalado = Join-Path $destino 'Overlay.exe'
$tarefa = 'Overlay Horario Ping FPS'
# Get-ScheduledTask em vez de "schtasks /Query": com $ErrorActionPreference = 'Stop', o stderr do schtasks
# (tarefa inexistente, caso da primeira instalação) interromperia o script no PowerShell 5.1.
if (Get-ScheduledTask -TaskName $tarefa -ErrorAction SilentlyContinue) {
    $p = Start-Process $exeInstalado -ArgumentList '--ativar-inicio' -Wait -PassThru
    if ($p.ExitCode -eq 0) { Write-Host 'Tarefa de logon atualizada para a copia instalada.' }
    else { Write-Host "Nao foi possivel atualizar a tarefa de logon; veja $destino\log\overlay.log." }
}

Start-Process $exeInstalado -WorkingDirectory $destino
Write-Host "Overlay instalado em $destino e iniciado."
Write-Host "Config: $configDestino"
