param([switch]$VerificarPacote)
$ErrorActionPreference = 'Stop'
try {
    Import-Module (Join-Path $PSScriptRoot 'distribuicao.psm1') -Force -DisableNameChecking
    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { throw 'Use o Windows x64 e o PowerShell de 64 bits.' }
    $raiz = Split-Path $PSScriptRoot -Parent
    $exigir = -not (Test-Path -LiteralPath (Join-Path $raiz 'src\Program.cs'))
    $pacote = Test-PacoteOverlay $raiz -ExigirManifesto:$exigir
    if ($VerificarPacote) { Write-Output ("Pacote Overlay {0}: integridade OK ({1} arquivos)." -f $pacote.Versao,$pacote.Arquivos); exit 0 }

    Initialize-ConfigOverlay $raiz
    if (-not (Test-AdminOverlay)) {
        $argumentos = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"{0}"' -f $PSCommandPath))
        $elevado = Start-Process -FilePath (Get-PowerShellOverlay) -Verb RunAs -ArgumentList $argumentos -Wait -PassThru
        exit $elevado.ExitCode
    }

    $destino = Get-RaizOverlayInstalado
    $exeInstalado = Get-CaminhoFilhoOverlay $destino 'Overlay.exe'
    Test-DestinoOverlay $destino
    $chave = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Overlay'
    if (Test-Path -LiteralPath $chave) {
        $anterior = Get-ItemProperty -LiteralPath $chave
        if ($anterior.InstallLocation -ne $destino) { throw 'Ja existe outro aplicativo registrado como Overlay. A instalacao foi cancelada.' }
    }
    $tarefa = Get-ScheduledTask -TaskName 'Overlay Horario Ping FPS' -ErrorAction SilentlyContinue
    if ($tarefa) {
        $permitidos = @($exeInstalado,(Join-Path $raiz 'Overlay.exe'))
        if (@($tarefa.Actions).Count -ne 1 -or $permitidos -notcontains $tarefa.Actions[0].Execute.Trim('"')) {
            throw 'A tarefa de inicio existente pertence a outro caminho. Nenhuma tarefa foi alterada.'
        }
    }
    Stop-ProcessosOverlay @($exeInstalado,(Join-Path $raiz 'Overlay.exe'))
    Copy-ProgramaOverlay $raiz $destino
    New-AtalhosOverlay $destino

    New-Item -Path $chave -Force | Out-Null
    $desinstalar = '"' + (Get-PowerShellOverlay) + '" -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $destino 'tools\desinstalar.ps1') + '"'
    foreach ($par in @{
        DisplayName='Overlay'; DisplayVersion=$pacote.Versao; Publisher='Daniellpego';
        InstallLocation=$destino; DisplayIcon=($exeInstalado + ',0'); UninstallString=$desinstalar;
        URLInfoAbout='https://github.com/Daniellpego/overlay'
    }.GetEnumerator()) { New-ItemProperty -LiteralPath $chave -Name $par.Key -Value $par.Value -PropertyType String -Force | Out-Null }
    New-ItemProperty -LiteralPath $chave -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $chave -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

    if ($tarefa) {
        $processo = Start-Process -FilePath $exeInstalado -ArgumentList '--ativar-inicio' -Wait -PassThru
        if ($processo.ExitCode -ne 0) { throw 'Arquivos instalados, mas o inicio automatico nao foi atualizado. Confira o menu da bandeja e o log.' }
    }
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show(("Overlay {0} instalado!`r`n`r`nAbra Overlay pelo menu Iniciar.`r`nCtrl + Shift + F10 oculta ou mostra a faixa.`r`n`r`nSuas preferencias existentes foram preservadas." -f $pacote.Versao), 'Overlay')
} catch {
    if ($VerificarPacote) { Write-Error $_.Exception.Message; exit 1 }
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Instalacao do Overlay')
    exit 1
}
