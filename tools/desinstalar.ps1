param([switch]$Confirmado, [switch]$VerificarInstalacao)
$ErrorActionPreference = 'Stop'
try {
    Import-Module (Join-Path $PSScriptRoot 'distribuicao.psm1') -Force -DisableNameChecking
    if (-not [Environment]::Is64BitProcess) { throw 'Use o PowerShell de 64 bits.' }
    $raiz = Get-RaizOverlayInstalado
    $exe = Get-CaminhoFilhoOverlay $raiz 'Overlay.exe'
    $chave = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Overlay'
    if (-not (Test-Path -LiteralPath $raiz) -and -not (Test-Path -LiteralPath $chave)) {
        Write-Output 'O Overlay nao esta instalado neste caminho.'
        exit 0
    }
    Test-DestinoOverlay $raiz
    if ($VerificarInstalacao) { Write-Output ("Instalacao identificada: " + $raiz); exit 0 }
    Add-Type -AssemblyName System.Windows.Forms
    if (-not $Confirmado) {
        $resposta = [Windows.Forms.MessageBox]::Show('Remover o Overlay? Suas preferencias e logs serao preservados.', 'Desinstalar Overlay', [Windows.Forms.MessageBoxButtons]::YesNo, [Windows.Forms.MessageBoxIcon]::Question, [Windows.Forms.MessageBoxDefaultButton]::Button2)
        if ($resposta -ne [Windows.Forms.DialogResult]::Yes) { exit 0 }
    }
    if (-not (Test-AdminOverlay)) {
        $argumentos = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $PSCommandPath),'-Confirmado')
        $elevado = Start-Process -FilePath (Get-PowerShellOverlay) -Verb RunAs -ArgumentList $argumentos -Wait -PassThru
        exit $elevado.ExitCode
    }
    # Todas as exclusoes abaixo se limitam a arquivos conhecidos dentro do destino fixo de instalacao.
    $esperado = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'Overlay'))
    if ([IO.Path]::GetFullPath($raiz) -ne $esperado) { throw 'Destino de remocao inesperado.' }
    $tarefa = Get-ScheduledTask -TaskName 'Overlay Horario Ping FPS' -ErrorAction SilentlyContinue
    if ($tarefa) {
        if (@($tarefa.Actions).Count -ne 1 -or $tarefa.Actions[0].Execute.Trim('"') -ne $exe) { throw 'A tarefa existente pertence a outro caminho; remocao cancelada.' }
    }
    if (Test-Path -LiteralPath $chave) {
        if ((Get-ItemProperty -LiteralPath $chave).InstallLocation -ne $raiz) { throw 'Registro pertence a outro caminho; remocao cancelada.' }
    }
    $null = Get-CaminhoFilhoOverlay (Get-PastaAtalhosOverlay) 'Overlay.lnk'
    $null = Get-CaminhoFilhoOverlay (Get-PastaAtalhosOverlay) 'Desinstalar Overlay.lnk'
    Stop-ProcessosOverlay @($exe)
    Remove-ProgramaOverlay $raiz {
        # Os arquivos podem ser restaurados se esta etapa falhar.
        if ($tarefa) { Unregister-ScheduledTask -TaskName $tarefa.TaskName -Confirm:$false }
        Remove-TemporariosOverlay (Get-PastaAtalhosOverlay) @('Overlay.lnk','Desinstalar Overlay.lnk')
        if (Test-Path -LiteralPath $chave) { Remove-Item -LiteralPath $chave }
    }
    [void][Windows.Forms.MessageBox]::Show('Overlay removido. Preferencias e logs foram preservados.', 'Overlay')
} catch {
    if ($VerificarInstalacao) { Write-Error $_.Exception.Message; exit 1 }
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Desinstalacao do Overlay')
    exit 1
}
