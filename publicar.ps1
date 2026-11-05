<#
.SYNOPSIS
  Gera o instalador do Menage (Velopack) em uma pasta LOCAL. Por padrão não envia nada para lugar nenhum.
.PARAMETER Canal
  Canal do feed: omitido = estável (releases.win.json); 'beta' = releases.beta.json (quem marcou "canal beta" no Sobre recebe).
.PARAMETER Notas
  Arquivo .md com as notas da versão (aparecem em Sobre / Atualizações).
.PARAMETER PastaDeRede
  OPCIONAL. Pasta de rede (ex.: \\servidor\menage\releases) para onde copiar os pacotes depois de gerados.
  Só copia se você passar este parâmetro e confirmar; use -WhatIf para ver o que seria copiado sem copiar.
.EXAMPLE
  .\publicar.ps1 -Versao 1.0.1
.EXAMPLE
  .\publicar.ps1 -Versao 1.1.0-beta.1 -Canal beta -Notas notas.md
.EXAMPLE
  .\publicar.ps1 -Versao 1.0.1 -PastaDeRede \\servidor\menage\releases -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')][string]$Versao,
    # Mantenha a mesma pasta entre versões: é dela que o vpk tira os pacotes anteriores para gerar o delta.
    [string]$Saida,
    [string]$Canal,
    [string]$Notas,
    [string]$PastaDeRede
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not $Saida) { $Saida = Join-Path $PSScriptRoot 'artifacts' | Join-Path -ChildPath 'releases' }

# Id sem ponto e diferente de "Menage": o Velopack instala em %LOCALAPPDATA%\<id> e o desinstalador apaga
# essa pasta inteira, e %LOCALAPPDATA%\Menage é onde ficam os dados do usuário.
$idPacote = 'MenageApp'
$publicacao = Join-Path $PSScriptRoot "artifacts\publish\$Versao"

dotnet tool restore | Out-Null
if ($LASTEXITCODE) { throw 'dotnet tool restore falhou' }

# Versão numérica para o assembly (sem o sufixo -beta.N).
$versaoNumerica = ($Versao -split '-')[0]
dotnet publish src/Menage.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$versaoNumerica -o $publicacao
if ($LASTEXITCODE) { throw 'dotnet publish falhou' }

$argumentos = @(
    'vpk', 'pack', '--packId', $idPacote, '--packVersion', $Versao, '--packTitle', 'Menage', '--packAuthors', 'WMW',
    '--packDir', $publicacao, '--mainExe', 'Menage.exe', '--runtime', 'win-x64', '--shortcuts', 'Desktop,StartMenuRoot',
    '--outputDir', $Saida)
if ($Canal) { $argumentos += @('--channel', $Canal) }
if ($Notas) { $argumentos += @('--releaseNotes', (Resolve-Path $Notas).Path) }
dotnet @argumentos
if ($LASTEXITCODE) { throw 'vpk pack falhou' }

Write-Host "Pronto. Instalador e pacotes em: $Saida"

if ($PastaDeRede) {
    # Copia só o que o app baixa (feeds, pacotes) e o Setup; /E sem /MIR: nunca apaga versões antigas da rede.
    if ($PSCmdlet.ShouldProcess($PastaDeRede, "Copiar o conteúdo de $Saida")) {
        robocopy $Saida $PastaDeRede /E /R:2 /W:2 /NP | Out-Host
        if ($LASTEXITCODE -ge 8) { throw "robocopy falhou (código $LASTEXITCODE)" }
        Write-Host "Copiado para: $PastaDeRede"
    }
}
else {
    Write-Host 'Nada foi enviado. Para publicar na rede: -PastaDeRede \\servidor\menage\releases (use -WhatIf para ensaiar).'
}
