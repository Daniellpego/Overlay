Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

function Get-ArquivosOverlay {
    @('Overlay.exe','config.ini','LEIA-ME.txt','instalar.bat','iniciar.bat','desinstalar.bat',
      'tools/instalar.ps1','tools/iniciar.ps1','tools/desinstalar.ps1','tools/distribuicao.psm1','tools/Overlay.Distribuicao.dll',
      'tools/PresentMon/PresentMon-2.6.0-x64.exe','third-party/PresentMon/LICENSE.txt',
      'third-party/PresentMon/THIRD_PARTY.txt','third-party/PresentMon/ORIGEM.md')
}

function Get-RaizOverlayInstalado {
    Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'Overlay'
}

function Get-PowerShellOverlay {
    Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
}

function Test-AdminOverlay {
    $identidade = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        ([Security.Principal.WindowsPrincipal]$identidade).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } finally { $identidade.Dispose() }
}

function Initialize-NativoOverlay {
    # Somente carrega o auxiliar distribuido; nao usa compilador nem a pasta TEMP.
    $dll = Get-CaminhoFilhoOverlay $PSScriptRoot 'Overlay.Distribuicao.dll'
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw 'Auxiliar do Overlay ausente. Extraia todo o ZIP novamente ou execute build.bat no codigo-fonte.'
    }
    # Carregar em memoria evita manter a propria DLL bloqueada na atualizacao/remocao.
    [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))
}

function Get-CaminhoFilhoOverlay([string]$Raiz, [string]$Relativo) {
    $base = [IO.Path]::GetFullPath($Raiz).TrimEnd('\')
    $caminho = [IO.Path]::GetFullPath((Join-Path $base $Relativo))
    if (-not $caminho.StartsWith($base + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Caminho fora da pasta esperada.'
    }
    $atual = $caminho
    while ($atual.Length -ge $base.Length) {
        if (Test-Path -LiteralPath $atual) {
            $item = Get-Item -LiteralPath $atual -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'A pasta contem um link ou juncao. Extraia o pacote em uma pasta local comum.'
            }
        }
        if ($atual -eq $base) { break }
        $atual = Split-Path $atual -Parent
    }
    $caminho
}

function Test-PacoteOverlay([string]$Raiz, [switch]$ExigirManifesto) {
    $arquivos = @(Get-ArquivosOverlay)
    foreach ($relativo in $arquivos) {
        $caminho = Get-CaminhoFilhoOverlay $Raiz $relativo
        if (-not (Test-Path -LiteralPath $caminho -PathType Leaf)) { throw "Pacote incompleto: $relativo. Extraia todo o ZIP novamente." }
    }
    $pm = Get-CaminhoFilhoOverlay $Raiz 'tools/PresentMon/PresentMon-2.6.0-x64.exe'
    if ((Get-FileHash -LiteralPath $pm -Algorithm SHA256).Hash -ne 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF') {
        throw 'O PresentMon nao corresponde ao arquivo oficial esperado.'
    }
    $exe = Get-CaminhoFilhoOverlay $Raiz 'Overlay.exe'
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($info.ProductName -ne 'Overlay' -or $info.ProductVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Overlay.exe nao tem a identidade e versao esperadas. Baixe o pacote novamente.'
    }
    $manifesto = Get-CaminhoFilhoOverlay $Raiz 'manifesto.json'
    if (Test-Path -LiteralPath $manifesto) {
        $dados = [IO.File]::ReadAllText($manifesto) | ConvertFrom-Json
        if ($dados.versao -ne $info.ProductVersion -or @($dados.arquivos).Count -ne $arquivos.Count) { throw 'Manifesto de integridade invalido.' }
        $vistos = @{}
        foreach ($entrada in $dados.arquivos) {
            $relativo = [string]$entrada.caminho
            if ($arquivos -cnotcontains $relativo -or $vistos.ContainsKey($relativo)) { throw 'Arquivo inesperado ou duplicado no manifesto.' }
            $vistos[$relativo] = $true
            $caminho = Get-CaminhoFilhoOverlay $Raiz $relativo
            if ((Get-Item -LiteralPath $caminho).Length -ne $entrada.tamanho -or
                (Get-FileHash -LiteralPath $caminho -Algorithm SHA256).Hash -ne $entrada.sha256) {
                throw "Integridade do pacote falhou: $relativo. Baixe e extraia novamente."
            }
        }
    } elseif ($ExigirManifesto) { throw 'manifesto.json ausente. Extraia todo o ZIP novamente.' }
    [pscustomobject]@{ Versao = $info.ProductVersion; Raiz = [IO.Path]::GetFullPath($Raiz); Arquivos = $arquivos.Count }
}

function Initialize-ConfigOverlay([string]$Raiz) {
    $pasta = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Overlay'
    Write-ConfigInicialOverlay (Get-CaminhoFilhoOverlay $Raiz 'config.ini') $pasta
}

function Write-ConfigInicialOverlay([string]$Origem, [string]$Pasta) {
    if (-not ('OverlayDistribuicao.Configuracao' -as [type])) {
        Initialize-NativoOverlay
    }
    [OverlayDistribuicao.Configuracao]::Criar($Origem, $Pasta)
}

function Test-DestinoOverlay([string]$Raiz) {
    $exe = Get-CaminhoFilhoOverlay $Raiz 'Overlay.exe'
    if (-not (Test-Path -LiteralPath $Raiz)) { return }
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        $manifesto = Get-CaminhoFilhoOverlay $Raiz 'manifesto.json'
        if (Test-Path -LiteralPath $manifesto -PathType Leaf) {
            $dados = [IO.File]::ReadAllText($manifesto) | ConvertFrom-Json
            $esperados = @(Get-ArquivosOverlay)
            if ($dados.produto -eq 'Overlay' -and $dados.versao -match '^\d+\.\d+\.\d+$' -and @($dados.arquivos).Count -eq $esperados.Count) {
                $nomes = @($dados.arquivos | ForEach-Object { $_.caminho } | Select-Object -Unique)
                if ($nomes.Count -eq $esperados.Count -and @(Compare-Object $nomes $esperados).Count -eq 0) { return }
            }
            throw 'Manifesto de instalacao desconhecido; nenhum arquivo foi alterado.'
        }
        # Uma desinstalacao conserva somente o log; esse arquivo nunca e sobrescrito pelo instalador.
        $itens = @(Get-ChildItem -LiteralPath $Raiz -Force)
        if ($itens.Count -eq 1 -and $itens[0].Name -eq 'log' -and $itens[0].PSIsContainer) {
            $null = Get-CaminhoFilhoOverlay $Raiz 'log/overlay.log'
            $logs = @(Get-ChildItem -LiteralPath $itens[0].FullName -Force)
            if ($logs.Count -eq 0 -or ($logs.Count -eq 1 -and $logs[0].Name -eq 'overlay.log' -and -not $logs[0].PSIsContainer)) { return }
        }
        if ($itens.Count -gt 0) { throw 'A pasta Overlay ja contem arquivos de origem desconhecida. Escolha outra pasta para esses arquivos antes de instalar.' }
        return
    }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($info.ProductName -ne 'Overlay' -or $info.ProductVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw 'A pasta de destino contem outro aplicativo ou uma versao antiga sem identificacao. Feche-o e renomeie a pasta antiga antes de instalar; nenhum arquivo foi substituido.'
    }
}

function Stop-ProcessosOverlay([string[]]$Caminhos) {
    if (-not ('OverlayDistribuicao.Janelas' -as [type])) {
        Initialize-NativoOverlay
    }
    foreach ($registro in Get-CimInstance Win32_Process -Filter "Name='Overlay.exe'") {
        if (-not $registro.ExecutablePath -or $Caminhos -notcontains $registro.ExecutablePath) { continue }
        $processo = Get-Process -Id $registro.ProcessId -ErrorAction SilentlyContinue
        if (-not $processo) { continue }
        try {
            [OverlayDistribuicao.Janelas]::Fechar([uint32]$processo.Id)
            if (-not $processo.WaitForExit(8000)) { throw 'Feche o Overlay pela bandeja antes de continuar. Nenhum processo foi encerrado a forca.' }
        } finally { $processo.Dispose() }
    }
}

function Remove-TemporariosOverlay([string]$Raiz, [string[]]$Arquivos) {
    # Somente arquivos conhecidos; nunca faz exclusao recursiva de uma pasta calculada.
    $pastas = @{}
    foreach ($relativo in $Arquivos) {
        $caminho = Get-CaminhoFilhoOverlay $Raiz $relativo
        if (Test-Path -LiteralPath $caminho -PathType Leaf) { Remove-Item -LiteralPath $caminho -Force }
        $pasta = Split-Path $caminho -Parent
        while ($pasta.Length -ge $Raiz.TrimEnd('\').Length) {
            $pastas[$pasta] = $true
            if ($pasta -eq $Raiz.TrimEnd('\')) { break }
            $pasta = Split-Path $pasta -Parent
        }
    }
    foreach ($pasta in ($pastas.Keys | Sort-Object Length -Descending)) {
        if (Test-Path -LiteralPath $pasta -PathType Container) {
            try { [IO.Directory]::Delete($pasta, $false) } catch [IO.IOException] { }
        }
    }
}

function Copy-ProgramaOverlay([string]$Origem, [string]$Destino) {
    Test-DestinoOverlay $Destino
    $null = Get-CaminhoFilhoOverlay $Destino 'Overlay.exe'
    $arquivos = @(Get-ArquivosOverlay)
    if (Test-Path -LiteralPath (Join-Path $Origem 'manifesto.json')) { $arquivos += 'manifesto.json' }
    $etapa = Join-Path $Destino ('.preparar-' + [Guid]::NewGuid().ToString('N'))
    $backup = Join-Path $Destino ('.anterior-' + [Guid]::NewGuid().ToString('N'))
    $tocados = @()
    $anteriores = @{}
    $concluiu = $false
    $recuperou = $false
    try {
        foreach ($relativo in $arquivos) {
            $arquivo = Get-CaminhoFilhoOverlay $etapa $relativo
            New-Item -ItemType Directory -Path (Split-Path $arquivo -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Get-CaminhoFilhoOverlay $Origem $relativo) -Destination $arquivo
        }
        $null = Test-PacoteOverlay $etapa -ExigirManifesto:($arquivos -contains 'manifesto.json')
        foreach ($relativo in $arquivos) {
            $arquivo = Get-CaminhoFilhoOverlay $Destino $relativo
            if (Test-Path -LiteralPath $arquivo) {
                $anterior = Get-CaminhoFilhoOverlay $backup $relativo
                New-Item -ItemType Directory -Path (Split-Path $anterior -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $arquivo -Destination $anterior
                $anteriores[$relativo] = $true
            }
            $tocados += $relativo
            New-Item -ItemType Directory -Path (Split-Path $arquivo -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Get-CaminhoFilhoOverlay $etapa $relativo) -Destination $arquivo -Force
        }
        $concluiu = $true
    } catch {
        foreach ($relativo in $tocados) {
            $arquivo = Get-CaminhoFilhoOverlay $Destino $relativo
            if ($anteriores.ContainsKey($relativo)) {
                $copia = Get-CaminhoFilhoOverlay $backup $relativo
                if (-not (Test-Path -LiteralPath $arquivo) -or (Get-FileHash -LiteralPath $arquivo).Hash -ne (Get-FileHash -LiteralPath $copia).Hash) {
                    Copy-Item -LiteralPath $copia -Destination $arquivo -Force
                }
            } elseif (Test-Path -LiteralPath $arquivo -PathType Leaf) { Remove-Item -LiteralPath $arquivo -Force }
        }
        $recuperou = $true
        throw
    } finally {
        Remove-TemporariosOverlay $etapa $arquivos
        # Se a restauracao falhar, conservar o backup para recuperacao manual.
        if ($concluiu -or $recuperou) { Remove-TemporariosOverlay $backup $arquivos }
    }
}

function Remove-ProgramaOverlay([string]$Raiz, [scriptblock]$Finalizar = {}) {
    Test-DestinoOverlay $Raiz
    $arquivos = @(Get-ArquivosOverlay) + @('manifesto.json')
    $backup = Join-Path $Raiz ('.remocao-' + [Guid]::NewGuid().ToString('N'))
    $salvos = @()
    $concluiu = $false
    $recuperou = $false
    try {
        foreach ($relativo in $arquivos) {
            $arquivo = Get-CaminhoFilhoOverlay $Raiz $relativo
            if (-not (Test-Path -LiteralPath $arquivo -PathType Leaf)) { continue }
            $copia = Get-CaminhoFilhoOverlay $backup $relativo
            New-Item -ItemType Directory -Path (Split-Path $copia -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $arquivo -Destination $copia
            $salvos += $relativo
        }
        Remove-TemporariosOverlay $Raiz $salvos
        & $Finalizar
        $concluiu = $true
    } catch {
        foreach ($relativo in $salvos) {
            $arquivo = Get-CaminhoFilhoOverlay $Raiz $relativo
            if (Test-Path -LiteralPath $arquivo -PathType Leaf) { continue }
            New-Item -ItemType Directory -Path (Split-Path $arquivo -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Get-CaminhoFilhoOverlay $backup $relativo) -Destination $arquivo
        }
        $recuperou = $true
        throw
    } finally {
        # Se a restauracao falhar, conservar backup para recuperacao manual.
        if ($concluiu -or $recuperou) { Remove-TemporariosOverlay $backup $salvos }
    }
}

function Get-PastaAtalhosOverlay {
    Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) 'Overlay'
}

function New-AtalhosOverlay([string]$Raiz) {
    $pasta = Get-PastaAtalhosOverlay
    $null = Get-CaminhoFilhoOverlay $pasta 'Overlay.lnk'
    New-Item -ItemType Directory -Path $pasta -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    try {
        foreach ($nome in @('Overlay','Desinstalar Overlay')) {
            $script = if ($nome -eq 'Overlay') { 'iniciar.ps1' } else { 'desinstalar.ps1' }
            $atalho = $shell.CreateShortcut((Join-Path $pasta ($nome + '.lnk')))
            $atalho.TargetPath = Get-PowerShellOverlay
            $atalho.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $Raiz ('tools\' + $script)) + '"'
            $atalho.WorkingDirectory = $Raiz
            $atalho.IconLocation = (Join-Path $Raiz 'Overlay.exe') + ',0'
            $atalho.Description = $nome
            $atalho.Save()
        }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}

Export-ModuleMember -Function Get-ArquivosOverlay,Get-RaizOverlayInstalado,Get-PowerShellOverlay,Test-AdminOverlay,Get-CaminhoFilhoOverlay,Test-PacoteOverlay,Initialize-ConfigOverlay,Write-ConfigInicialOverlay,Test-DestinoOverlay,Stop-ProcessosOverlay,Copy-ProgramaOverlay,Remove-ProgramaOverlay,Remove-TemporariosOverlay,Get-PastaAtalhosOverlay,New-AtalhosOverlay
