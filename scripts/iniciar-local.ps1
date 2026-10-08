param(
    [string] $PostgreSqlBin = 'C:\Program Files\PostgreSQL\18\bin',
    [ValidatePattern('^[a-z][a-z0-9_]{0,62}$')]
    [string] $Database = 'nora',
    [ValidateRange(1, 65535)]
    [int] $Port = 5433,
    [string] $User = 'postgres',
    [switch] $ProvisionAccounts
)

$ErrorActionPreference = 'Stop'
$noraRoot = Split-Path -Parent $PSScriptRoot
$noraApi = Join-Path $noraRoot '.build\Nora.Api\bin\Release\net10.0\Nora.Api.dll'
if (-not (Test-Path -LiteralPath $noraApi)) { throw 'Compile NORA.slnx em Release antes de iniciar.' }
$noraPsql = Join-Path $PostgreSqlBin 'psql.exe'
$noraCreatedb = Join-Path $PostgreSqlBin 'createdb.exe'
if (-not (Test-Path -LiteralPath $noraPsql)) { throw 'Informe a pasta bin do PostgreSQL 18.' }
$noraPreviousConnection = $env:ConnectionStrings__Nora
$noraPreviousPassword = $env:PGPASSWORD
$noraPreviousHosts = $env:AllowedHosts
$noraPassword = Read-Host 'Senha do usuário PostgreSQL (entrada oculta)' -AsSecureString
try {
    $env:PGPASSWORD = [System.Net.NetworkCredential]::new('', $noraPassword).Password
    $noraExists = & $noraPsql -w -h 127.0.0.1 -p $Port -U $User -d postgres -At -c "SELECT 1 FROM pg_database WHERE datname='$Database'"
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível autenticar no PostgreSQL; nenhum schema NORA foi alterado.' }
    if ($noraExists -ne '1') {
        & $noraCreatedb -w -h 127.0.0.1 -p $Port -U $User --encoding=UTF8 $Database
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível criar a base NORA.' }
    }
    $noraConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $noraConnection['Host'] = '127.0.0.1'
    $noraConnection['Port'] = $Port
    $noraConnection['Database'] = $Database
    $noraConnection['Username'] = $User
    $noraConnection['Password'] = $env:PGPASSWORD
    $env:ConnectionStrings__Nora = $noraConnection.ConnectionString
    # A senha permanece somente no ambiente do processo; nenhum arquivo privado é sobrescrito.
    $env:PGPASSWORD = $noraPreviousPassword
    $env:AllowedHosts = 'localhost;127.0.0.1;[::1];10.0.2.2'
    & dotnet $noraApi --migrate
    if ($LASTEXITCODE -ne 0) { throw 'Falha na migração aditiva. Não iniciar a API antes de investigar.' }
    if ($ProvisionAccounts) {
        Write-Host 'Cadastre primeiro OBS (obs), depois Municipal (municipal). Use o nome completo aprovado. O paciente será cadastrado pela interface OBS.'
        for ($noraAccountIndex = 0; $noraAccountIndex -lt 2; $noraAccountIndex++) {
            & dotnet $noraApi --provision
            if ($LASTEXITCODE -ne 0) { throw 'Falha no provisionamento. Contas já existentes foram preservadas.' }
        }
    }
    Write-Host 'API local em http://127.0.0.1:5080. No emulador Android debug, use http://10.0.2.2:5080. Ctrl+C encerra.'
    & dotnet $noraApi
    if ($LASTEXITCODE -ne 0) { throw 'A API encerrou com erro.' }
}
finally {
    $env:ConnectionStrings__Nora = $noraPreviousConnection
    $env:PGPASSWORD = $noraPreviousPassword
    $env:AllowedHosts = $noraPreviousHosts
    $noraPassword.Dispose()
    $noraConnection = $null
}
