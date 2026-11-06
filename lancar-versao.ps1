<#
.SYNOPSIS
  Integra branches na main, valida, gera o instalador (Velopack), testa a instalacao, cria tag e Release.
  Reproduz o roteiro usado para a 1.3.1. Detalhes em docs\LANCAR-VERSAO.md.
.PARAMETER Versao
  Ex.: 1.3.2. Omitida: sugere o proximo patch da ultima tag v* e pede confirmacao.
.PARAMETER Branches
  Nomes das branches remotas (sem "origin/") a mesclar. Omitido: lista as nao mescladas (menos tmp/*) e voce marca.
.PARAMETER Saida
  Pasta nova dos pacotes. Padrao: C:\mt\rel<versao sem pontos>.
.PARAMETER PastaAnterior
  Pasta com os pacotes anteriores (copiados para Saida, para gerar deltas). Padrao: a ultima C:\mt\rel* existente.
.PARAMETER PularTestes
  Nao roda a suite completa (o build com -warnaserror continua).
.PARAMETER SemPush
  Nao envia nada ao origin (nem main, nem tag) e nao cria Release.
.PARAMETER Simular
  So mostra o que faria; nao altera nada.
.EXAMPLE
  .\lancar-versao.ps1 -Versao 1.3.2 -Branches feature/a,feature/b
.EXAMPLE
  .\lancar-versao.ps1 -Simular
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Versao,
    [string[]]$Branches,
    [string]$Saida,
    [string]$PastaAnterior,
    [switch]$PularTestes,
    [switch]$SemPush,
    [switch]$Simular
)
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
Set-Location $raiz
$resumo = New-Object System.Collections.Generic.List[string]

function Passo($t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Falha($t) { Write-Host "ERRO: $t" -ForegroundColor Red; exit 1 }
function Sim($t) { Write-Host "[SIMULAR] $t" -ForegroundColor Yellow }
function Gx { & git.exe @args; if ($LASTEXITCODE -ne 0) { Falha "git $($args -join ' ') falhou (codigo $LASTEXITCODE)." } }
function GxSaida { $o = & git.exe @args; if ($LASTEXITCODE -ne 0) { Falha "git $($args -join ' ') falhou." }; return $o }
function Confirmar($pergunta) {
    if ($Simular) { Sim "perguntaria: $pergunta"; return $true }
    $r = Read-Host "$pergunta (S/N)"
    return ($r -match '^[sSyY]')
}
function ProibirGmv($caminho) {
    if ($caminho -match '^[A-Za-z]:\\wmw\\GMV') { Falha "Caminho proibido (C:\wmw\GMV*): $caminho" }
}

# ---- 1. Verificacoes ----
Passo '1. Verificando repositorio'
if (-not (Test-Path (Join-Path $raiz 'Menage.sln'))) { Falha 'Rode o script na raiz do repositorio do Menage.' }
$atual = (GxSaida rev-parse --abbrev-ref HEAD | Select-Object -First 1).Trim()
if ($atual -ne 'main') { Falha "Voce esta na branch '$atual'. Va para a main (git switch main)." }
if (GxSaida status --porcelain) { Falha 'Ha alteracoes nao commitadas. Commit ou descarte antes.' }
Gx fetch --all --prune --tags
$atras = [int](GxSaida rev-list --count 'main..origin/main')
$adiante = [int](GxSaida rev-list --count 'origin/main..main')
if ($atras -gt 0 -or $adiante -gt 0) { Falha "main fora de sincronia com origin/main (atras $atras, adiante $adiante). Faca pull/push antes." }
Write-Host 'main limpa e sincronizada com origin/main.'

# ---- Versao ----
$ultima = (GxSaida tag -l 'v*' --sort=-v:refname | Select-Object -First 1)
if (-not $Versao) {
    if ($ultima -match '^v(\d+)\.(\d+)\.(\d+)$') { $sug = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)" } else { $sug = '1.0.0' }
    Write-Host "Ultima tag: $ultima"
    if (-not (Confirmar "Usar a versao $sug")) { Falha 'Informe a versao com -Versao.' }
    $Versao = $sug
}
if (GxSaida tag -l "v$Versao") { Falha "A tag v$Versao ja existe." }
if (-not $Saida) { $Saida = "C:\mt\rel$($Versao -replace '\.', '')" }
ProibirGmv $Saida
if (-not $PastaAnterior) {
    $c = Get-ChildItem 'C:\mt' -Directory -Filter 'rel*' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $Saida } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($c) { $PastaAnterior = $c.FullName }
}
if ($PastaAnterior) { ProibirGmv $PastaAnterior }
Write-Host "Versao: $Versao | Saida: $Saida | Pacotes anteriores: $PastaAnterior"
if ((Test-Path $Saida) -and (Get-ChildItem $Saida -Force | Select-Object -First 1)) { Falha "A pasta de saida '$Saida' ja existe e nao esta vazia. Use outra." }

# ---- 2. Branches ----
Passo '2. Branches a integrar'
if (-not $Branches) {
    $nao = GxSaida branch -r --no-merged origin/main | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -notmatch '->' -and $_ -like 'origin/*' -and $_ -notlike 'origin/tmp/*' } |
        ForEach-Object { $_.Substring(7) }
    $nao = @($nao)
    if ($nao.Count -eq 0) { Write-Host 'Nenhuma branch remota pendente.' }
    else {
        for ($i = 0; $i -lt $nao.Count; $i++) {
            $n = (GxSaida rev-list --count "origin/main..origin/$($nao[$i])")
            Write-Host ("  [{0}] {1}  ({2} commits)" -f ($i + 1), $nao[$i], $n)
        }
        $r = Read-Host 'Numeros a EXCLUIR separados por virgula (Enter = todas entram)'
        $fora = @($r -split '[,\s]+' | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ })
        $Branches = @(for ($i = 0; $i -lt $nao.Count; $i++) { if ($fora -notcontains ($i + 1)) { $nao[$i] } })
    }
}
$Branches = @($Branches | ForEach-Object { $_ -replace '^origin/', '' })
foreach ($b in $Branches) { if (-not (GxSaida branch -r --list "origin/$b")) { Falha "Branch remota origin/$b nao existe." } }
Write-Host ("Entram: " + ($(if ($Branches) { $Branches -join ', ' } else { '(nenhuma)' })))

# ---- 3. Merges ----
Passo '3. Merges (--no-ff)'
foreach ($b in $Branches) {
    if ($Simular) { Sim "git merge --no-ff origin/$b"; continue }
    $msg = Join-Path $env:TEMP "lancar-merge-$([guid]::NewGuid().ToString('N')).txt"
    [System.IO.File]::WriteAllText($msg, "Integra $b`n")
    & git merge --no-ff -F $msg "origin/$b"
    $cod = $LASTEXITCODE
    [System.IO.File]::Delete($msg)
    if ($cod -ne 0) {
        $arqs = @(& git diff --name-only --diff-filter=U)
        & git merge --abort
        Write-Host "Conflito ao mesclar origin/$b. Merge abortado; a main continua como estava (merges anteriores desta execucao permanecem locais: confira com git log)." -ForegroundColor Red
        $arqs | ForEach-Object { Write-Host "  conflito: $_" -ForegroundColor Red }
        exit 1
    }
    $resumo.Add("Mesclada: $b")
}

# ---- 4. Build e testes ----
Passo '4. Build -warnaserror'
if ($Simular) { Sim 'dotnet build Menage.sln -warnaserror' }
else { & dotnet build Menage.sln -warnaserror -v q; if ($LASTEXITCODE) { Falha 'Build falhou. Corrija e rode de novo (os merges ja feitos continuam locais).' } }
if ($PularTestes) { Write-Host 'Testes pulados (-PularTestes).' -ForegroundColor Yellow; $resumo.Add('Testes: PULADOS') }
elseif ($Simular) { Sim 'dotnet test Menage.sln --no-build' }
else {
    Passo '4b. Suite completa (pode levar alguns minutos)'
    & dotnet test Menage.sln --no-build
    if ($LASTEXITCODE) { Falha 'Ha testes falhando.' }
    $resumo.Add('Testes: aprovados')
}

# ---- 5. Push da main ----
Passo '5. Push da main'
if ($SemPush) { Write-Host 'Pulado (-SemPush).' -ForegroundColor Yellow; $resumo.Add('Push: PULADO') }
elseif (Confirmar 'Enviar a main ao origin agora') {
    if (-not $Simular) { Gx push origin main; $resumo.Add('main enviada') } else { Sim 'git push origin main' }
} else { Falha 'Cancelado antes do push (os merges ficaram locais).' }

# ---- 6. Instalador ----
Passo "6. Gerando instalador $Versao"
if ($Simular) {
    Sim "copiar '$PastaAnterior' para '$Saida'"
    Sim "publicar.ps1 -Versao $Versao -Saida $Saida"
} else {
    [System.IO.Directory]::CreateDirectory($Saida) | Out-Null
    if ($PastaAnterior -and (Test-Path $PastaAnterior)) { Copy-Item (Join-Path $PastaAnterior '*') $Saida -Recurse }
    else { Write-Host 'Sem pacotes anteriores: nao havera deltas.' -ForegroundColor Yellow }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $raiz 'publicar.ps1') -Versao $Versao -Saida $Saida
    if ($LASTEXITCODE) { Falha 'publicar.ps1 falhou.' }
}
$setup = Join-Path $Saida 'MenageApp-win-Setup.exe'
if (-not $Simular) {
    if (-not (Test-Path $setup)) { Falha "Setup nao encontrado em $setup." }
    if (-not (Test-Path (Join-Path $Saida "MenageApp-$Versao-full.nupkg"))) { Falha "Pacote $Versao-full.nupkg nao foi gerado." }
}

# ---- 7. Teste de instalacao ----
Passo '7. Teste rapido de instalacao'
if ($Simular) { Sim 'instalar Setup.exe em C:\mt\ti<id>, abrir o app, conferir versao, fechar so esse processo e desinstalar' }
else {
    $alvo = 'C:\mt\ti' + (Get-Random -Maximum 99999)
    ProibirGmv $alvo
    & $setup --silent --installto $alvo | Out-Null
    $exe = Join-Path $alvo 'current\Menage.exe'
    if (-not (Test-Path $exe)) { Falha "Instalacao de teste nao criou $exe." }
    $fv = (Get-Item $exe).VersionInfo.FileVersion
    if (-not $fv.StartsWith($Versao)) { Falha "Versao instalada $fv diferente de $Versao." }
    $p = Start-Process $exe -PassThru
    $abriu = $false
    for ($i = 0; $i -lt 30 -and -not $p.HasExited; $i++) {
        Start-Sleep -Milliseconds 500; $p.Refresh()
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $abriu = $true; break }
    }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    & (Join-Path $alvo 'Update.exe') --uninstall --silent | Out-Null
    if (-not $abriu) { Falha 'O app instalado nao abriu uma janela em 15 s.' }
    Write-Host "Instalou, abriu e fechou (versao $fv)." -ForegroundColor Green
    $resumo.Add("Teste de instalacao OK ($fv)")
}

# ---- 8. Tag e Release ----
Passo '8. Tag e GitHub Release'
$arquivos = @()
if (Test-Path $Saida) {
    $arquivos = @(Get-ChildItem $Saida -File | Where-Object { $_.Name -like '*.nupkg' -or $_.Name -like '*Setup.exe' -or $_.Name -eq 'RELEASES' -or $_.Name -like '*.json' } | ForEach-Object { $_.FullName })
}
if ($SemPush) { Write-Host 'Pulado (-SemPush).' -ForegroundColor Yellow }
elseif (Confirmar "Criar a tag v$Versao, enviar e criar o Release") {
    if ($Simular) { Sim "git tag v$Versao; git push origin v$Versao" }
    else { Gx tag "v$Versao"; Gx push origin "v$Versao"; $resumo.Add("Tag v$Versao enviada") }
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    $logado = $false
    if ($gh) { & gh auth status *> $null; $logado = ($LASTEXITCODE -eq 0) }
    if ($logado) {
        if ($Simular) { Sim "gh release create v$Versao <$($arquivos.Count) arquivos>" }
        else {
            & gh release create "v$Versao" @arquivos --title "Menage $Versao" --notes "Menage $Versao"
            if ($LASTEXITCODE) { Falha 'gh release create falhou.' }
            $resumo.Add('Release criado')
        }
    } else {
        Write-Host 'gh ausente ou nao autenticado. Passos manuais:' -ForegroundColor Yellow
        Write-Host '  1) gh auth login   (ou crie pelo site: GitHub > Releases > Draft a new release)'
        Write-Host "  2) Tag v$Versao, anexe: Setup.exe, todos os .nupkg, RELEASES e *.json de $Saida"
        $resumo.Add('Release: MANUAL (gh nao autenticado)')
    }
} else { Write-Host 'Tag e Release nao criados.'; $resumo.Add('Tag/Release: nao criados') }

# ---- Resumo ----
Passo 'Resumo'
$resumo | ForEach-Object { Write-Host "  - $_" }
Write-Host "Setup: $setup"
if (Test-Path $setup) { Write-Host ("Tamanho: {0:N1} MB" -f ((Get-Item $setup).Length / 1MB)) }
if ($Simular) { Write-Host 'Era so simulacao: nada foi alterado.' -ForegroundColor Yellow }
