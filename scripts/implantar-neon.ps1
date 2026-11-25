#requires -Version 7
<#
.SYNOPSIS
  Prepara o banco do NORA no Neon a partir deste computador: migra, confere que nada se perdeu e cadastra o município e o OBS.

.DESCRIPTION
  Roda na raiz do repositório, numa janela do PowerShell 7:  pwsh -File scripts\implantar-neon.ps1

  Passos (cada um aparece na tela, marcado quando termina):
    1. pede a URI do Neon (conexão DIRETA, sem "-pooler"); a entrada é oculta e nada é gravado em arquivo;
    2. conta as linhas de todas as tabelas (antes);
    3. compila a API em Release;
    4. aplica as migrações do banco central (--migrate) e dos bancos das UBS (--migrate-all-units);
    5. conta de novo e recusa o resultado se alguma tabela tiver menos linhas;
    6. cadastra o município e o OBS com --provision (as perguntas, inclusive a senha oculta, são da própria API);
    7. deixa a conexão no formato do Npgsql na área de transferência para colar no Render (ConnectionStrings__Nora).

  As migrações são aditivas e podem ser repetidas. A senha nunca passa pelo chat nem pelo Git.
#>
[CmdletBinding()]
param(
    # Só converte a URI do Neon para o formato do Npgsql e copia para a área de transferência (para colar no Render). Não toca no banco.
    [switch] $SoConexao,
    # Migra e confere, mas não oferece o cadastro do município e do OBS.
    [switch] $PularCadastro
)

$ErrorActionPreference = 'Stop'

function ConvertTo-NpgsqlConnection {
    param([Parameter(Mandatory)] [string] $Uri)
    $parsed = [Uri]$Uri.Trim()
    if ($parsed.Scheme -notin 'postgres', 'postgresql') { throw 'Isso não parece uma URI do Neon (postgresql://...).' }
    if ($parsed.Host -like '*-pooler*') { throw 'Use a conexão direta: no Neon, desligue "Connection pooling" e copie de novo.' }
    $user, $password = $parsed.UserInfo.Split(':', 2) | ForEach-Object { [Uri]::UnescapeDataString($_) }
    $database = $parsed.AbsolutePath.TrimStart('/')
    $port = if ($parsed.Port -gt 0) { $parsed.Port } else { 5432 }
    if (-not $user -or -not $password -or -not $database) { throw 'A URI precisa ter usuário, senha e banco.' }
    $quoted = if ($password -match '[;"''=\s]') { '"' + $password.Replace('"', '""') + '"' } else { $password }
    [pscustomobject]@{
        Host = $parsed.Host; Port = $port; Database = $database; User = $user; Password = $password
        Npgsql = "Host=$($parsed.Host);Port=$port;Database=$database;Username=$user;Password=$quoted;SSL Mode=VerifyFull;Channel Binding=Require"
    }
}

function Find-Psql {
    $candidates = @(
        (Get-Command psql -ErrorAction SilentlyContinue)?.Source,
        (Join-Path $env:LOCALAPPDATA 'Programs\PostgreSQL\18\bin\psql.exe'),
        'C:\Tools\postgresql-18\bin\psql.exe',
        'C:\Program Files\PostgreSQL\18\bin\psql.exe', 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
    )
    $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

# O psql portátil do Windows não enxerga a loja de certificados do sistema (sslrootcert=system falha com "certificate verify failed").
# Exportamos as raízes que o próprio Windows confia para um arquivo temporário: a verificação completa do certificado continua valendo.
function New-WindowsRootBundle {
    $certs = @(Get-ChildItem Cert:\LocalMachine\Root, Cert:\CurrentUser\Root -ErrorAction SilentlyContinue |
        Where-Object { $_.NotAfter -gt (Get-Date) } | Sort-Object Thumbprint -Unique)
    if ($certs.Count -eq 0) { throw 'Não encontrei certificados raiz confiáveis no Windows.' }
    $text = [Text.StringBuilder]::new()
    foreach ($cert in $certs) {
        [void]$text.Append("-----BEGIN CERTIFICATE-----`n").Append([Convert]::ToBase64String($cert.RawData, 'InsertLineBreaks').Replace("`r`n", "`n")).Append("`n-----END CERTIFICATE-----`n")
    }
    $path = Join-Path ([IO.Path]::GetTempPath()) ("nora-raizes-$([guid]::NewGuid().ToString('N')).pem")
    [IO.File]::WriteAllText($path, $text.ToString(), [Text.Encoding]::ASCII)
    $path
}

function Write-Step([string] $Text) { Write-Host "→ $Text" -ForegroundColor Yellow }
function Write-Done([string] $Text) { Write-Host "✓ $Text" -ForegroundColor Green }

function Get-TableCounts([string] $Psql) {
    $sql = @"
SELECT table_schema||'.'||table_name, (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text
FROM information_schema.tables WHERE table_type='BASE TABLE' AND table_schema NOT IN ('pg_catalog','information_schema') ORDER BY 1
"@
    $rows = & $Psql -X -A -t -F '|' -c $sql
    if ($LASTEXITCODE) { throw 'Não foi possível ler o banco. Confira a URI e se o Neon está acessível.' }
    $map = @{}
    foreach ($row in $rows) { if ($row) { $key, $value = $row.Split('|'); $map[$key] = [long]$value } }
    $map
}

function Invoke-NoraApi([string[]] $Arguments) {
    dotnet run --project (Join-Path $script:Repo 'Nora.Api') -c Release --no-build -- @Arguments
    if ($LASTEXITCODE) { throw "A API terminou com erro ($($Arguments -join ' '))." }
}

if ($MyInvocation.InvocationName -eq '.') { return }   # permite carregar só as funções para teste

$script:Repo = Split-Path -Parent $PSScriptRoot
$rootBundle = $null
try {
    Write-Step 'Cole a URI do Neon (postgresql://...) e tecle Enter. Nada aparece na tela.'
    $connection = ConvertTo-NpgsqlConnection (Read-Host -MaskInput 'URI do Neon')
    Write-Done "Conexão direta reconhecida: $($connection.Host) (banco $($connection.Database))."

    if ($SoConexao) {
        Set-Clipboard -Value $connection.Npgsql
        Write-Done 'A conexão no formato do Npgsql está na área de transferência.'
        Write-Host '   Cole no Render: nora-api > Environment > ConnectionStrings__Nora > Save, rebuild, and deploy.' -ForegroundColor Cyan
        return
    }

    $psql = Find-Psql
    $env:PGHOST = $connection.Host; $env:PGPORT = "$($connection.Port)"; $env:PGDATABASE = $connection.Database
    $env:PGUSER = $connection.User; $env:PGPASSWORD = $connection.Password
    $env:PGSSLMODE = 'verify-full'; $env:PGCHANNELBINDING = 'require'
    $before = $null
    if ($psql) {
        try {
            $rootBundle = New-WindowsRootBundle
            $env:PGSSLROOTCERT = $rootBundle
            Write-Step 'Contando as linhas de todas as tabelas (antes)…'
            $before = Get-TableCounts $psql
            Write-Done "$($before.Count) tabelas lidas."
        }
        catch {
            Write-Host "! Não consegui contar as linhas: $($_.Exception.Message)" -ForegroundColor DarkYellow
            Write-Host '  As migrações só acrescentam colunas e tabelas, mas sem a contagem não há conferência final de que nada se perdeu.' -ForegroundColor DarkYellow
            if ((Read-Host 'Seguir mesmo assim? [s/N]') -notmatch '^[sSyY]') { Write-Host 'Parei sem alterar nada.' -ForegroundColor Red; exit 1 }
            $before = $null
        }
    }
    else { Write-Host '! psql não encontrado: a conferência de linhas será pulada (as migrações só acrescentam).' -ForegroundColor DarkYellow }

    Write-Step 'Compilando a API em Release…'
    dotnet build (Join-Path $script:Repo 'Nora.Api') -c Release --nologo -v q
    if ($LASTEXITCODE) { throw 'A compilação falhou.' }
    Write-Done 'API compilada.'

    $env:ConnectionStrings__Nora = $connection.Npgsql
    Write-Step 'Aplicando as migrações do banco central (--migrate)…'
    Invoke-NoraApi '--migrate'
    Write-Done 'Banco central migrado.'
    Write-Step 'Atualizando os bancos das UBS (--migrate-all-units)…'
    Invoke-NoraApi '--migrate-all-units'
    Write-Done 'Bancos das UBS atualizados.'

    if ($before) {
        Write-Step 'Conferindo que nenhuma tabela perdeu linhas…'
        $after = Get-TableCounts $psql
        $lost = @($before.Keys | Where-Object { -not $after.ContainsKey($_) -or $after[$_] -lt $before[$_] })
        if ($lost.Count) { Write-Host "ATENÇÃO: tabelas com menos linhas ou sumidas: $($lost -join ', ')" -ForegroundColor Red; exit 1 }
        $new = @($after.Keys | Where-Object { -not $before.ContainsKey($_) })
        Write-Done "Nenhuma tabela perdeu linhas ($($before.Count) antes, $($after.Count) depois; novas: $(if ($new.Count) { $new -join ', ' } else { 'nenhuma' }))."
    }

    if (-not $PularCadastro) {
        foreach ($item in @(@{ Nome = 'o MUNICÍPIO (escopo: municipal)'; Escopo = 'municipal' }, @{ Nome = 'o OBS da primeira UBS (escopo: obs)'; Escopo = 'obs' })) {
            if ((Read-Host "Cadastrar $($item.Nome) agora? [S/n]") -match '^[nN]') { continue }
            Write-Host "   Quando a API perguntar 'Escopo', digite: $($item.Escopo)" -ForegroundColor Cyan
            Invoke-NoraApi '--provision'
            Write-Done "Cadastro de $($item.Escopo) concluído."
        }
    }

    Set-Clipboard -Value $connection.Npgsql
    Write-Done 'PRONTO. A conexão no formato certo está na área de transferência.'
    Write-Host '   Cole no Render: nora-api > Environment > ConnectionStrings__Nora > Save, rebuild, and deploy.' -ForegroundColor Cyan
}
finally {
    Remove-Item Env:ConnectionStrings__Nora, Env:PGPASSWORD, Env:PGSSLROOTCERT -ErrorAction SilentlyContinue
    if ($rootBundle) { Remove-Item -LiteralPath $rootBundle -Force -ErrorAction SilentlyContinue }
}
