<#
.SYNOPSIS
  Tira as capturas reais do OBS-NORA e do Painel Municipal ligados a uma API local com dados de demonstração.
.DESCRIPTION
  Lê a senha de demonstração do arquivo de acesso (fora do Git), define as variáveis NORA_CAPTURA_* só para este processo e roda
  tests/Nora.Desktop.Checks com --capturas-reais. Só lê da API; não grava nada nela.
.EXAMPLE
  pwsh -File scripts\capturas-reais.ps1 -Saida .build\capturas-teste -Perfil ambos
#>
param(
    [string]$Saida = (Join-Path $PSScriptRoot '..\.build\capturas-teste'),
    [ValidateSet('obs', 'municipal', 'ambos')][string]$Perfil = 'ambos',
    [string]$Url = 'http://127.0.0.1:5190',
    [string]$ArquivoAcesso = (Join-Path $env:LOCALAPPDATA 'NORA\capturas-demo\acesso-demo.txt'),
    [string]$EmailObs = 'admin.demo@example.test',
    [string]$EmailMunicipal = 'secretaria.demo@example.test',
    [string]$Ubs = 'Jacyra',
    [switch]$SemCompilar
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$linha = Get-Content -LiteralPath $ArquivoAcesso -Encoding UTF8 | Where-Object { $_ -like 'Senha*' } | Select-Object -First 1
if (-not $linha) { throw "Linha 'Senha' não encontrada em $ArquivoAcesso." }
$senha = ($linha -replace '^[^:]*:\s*', '').Trim()
$env:NORA_CAPTURA_URL = $Url
$env:NORA_CAPTURA_OBS_EMAIL = $EmailObs
$env:NORA_CAPTURA_OBS_SENHA = $senha
$env:NORA_CAPTURA_MUN_EMAIL = $EmailMunicipal
$env:NORA_CAPTURA_MUN_SENHA = $senha
$env:NORA_CAPTURA_UBS = $Ubs
New-Item -ItemType Directory -Force -Path $Saida | Out-Null
$Saida = (Resolve-Path $Saida).Path
$projeto = Join-Path $repo 'tests\Nora.Desktop.Checks'
if (-not $SemCompilar) { dotnet build $projeto -c Release -v q | Out-Host; if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar.' } }
dotnet run --project $projeto -c Release --no-build -- --capturas-reais --saida $Saida --perfil $Perfil
exit $LASTEXITCODE
