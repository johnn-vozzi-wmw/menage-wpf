<#
.SYNOPSIS
  Gera o instalador do Menage (Velopack) em uma pasta LOCAL. Não envia nada para lugar nenhum.
.EXAMPLE
  .\publicar.ps1 -Versao 1.0.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Versao,
    # Mantenha a mesma pasta entre versões: é dela que o vpk tira os pacotes anteriores para gerar o delta.
    [string]$Saida
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

dotnet publish src/Menage.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Versao -o $publicacao
if ($LASTEXITCODE) { throw 'dotnet publish falhou' }

dotnet vpk pack --packId $idPacote --packVersion $Versao --packTitle 'Menage' --packAuthors 'WMW' `
    --packDir $publicacao --mainExe Menage.exe --runtime win-x64 --shortcuts 'Desktop,StartMenuRoot' --outputDir $Saida
if ($LASTEXITCODE) { throw 'vpk pack falhou' }

Write-Host "Pronto. Instalador e pacotes em: $Saida"
