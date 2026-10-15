$ErrorActionPreference = 'Stop'
try {
    Import-Module (Join-Path $PSScriptRoot 'distribuicao.psm1') -Force -DisableNameChecking
    $raiz = Get-RaizOverlayInstalado
    if (-not (Test-Path -LiteralPath (Join-Path $raiz 'Overlay.exe'))) { $raiz = Split-Path $PSScriptRoot -Parent }
    $exe = Get-CaminhoFilhoOverlay $raiz 'Overlay.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Overlay nao encontrado. Extraia o ZIP e execute instalar.bat.' }
    Initialize-ConfigOverlay $raiz
    if (Test-AdminOverlay) { Start-Process -FilePath $exe -WorkingDirectory $raiz }
    else { Start-Process -FilePath $exe -WorkingDirectory $raiz -Verb RunAs }
} catch {
    if ($_.Exception.NativeErrorCode -eq 1223) { exit 0 }
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Nao foi possivel abrir o Overlay')
    exit 1
}
