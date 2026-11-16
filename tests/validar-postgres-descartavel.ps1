param(
    [string] $PostgreSqlBin = 'C:\Program Files\PostgreSQL\18\bin',
    [ValidateRange(1024, 65535)] [int] $Port = 55439,
    [switch] $IncludeUnitDatabases
)

$ErrorActionPreference = 'Stop'
$noraRoot = Split-Path -Parent $PSScriptRoot
$noraCheckDll = Join-Path $noraRoot '.build\Nora.Api.Checks\bin\Release\net10.0\Nora.Api.Checks.dll'
if (-not (Test-Path -LiteralPath $noraCheckDll)) { throw 'Compile NORA.slnx em Release antes de validar.' }
$noraInit = Join-Path $PostgreSqlBin 'initdb.exe'
$noraCtl = Join-Path $PostgreSqlBin 'pg_ctl.exe'
if (-not (Test-Path -LiteralPath $noraInit)) { throw 'Informe os binários PostgreSQL18.' }
$noraDirectory = Join-Path $noraRoot ('.build\postgres-validation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $noraDirectory | Out-Null
$noraData = Join-Path $noraDirectory 'data'
$noraLog = Join-Path $noraDirectory 'server.log'
$noraOldConnection = $env:NORA_TEST_CONNECTION
$noraSavedUnits = @{}
foreach ($noraLetter in @('A','B','C','D','E','F')) {
    $noraVariable = "NORA_TEST_UNIT_${noraLetter}_CONNECTION"
    $noraSavedUnits[$noraVariable] = [Environment]::GetEnvironmentVariable($noraVariable, 'Process')
}
$noraStarted = $false
try {
    & $noraInit -D $noraData -U nora_checks -A trust --encoding=UTF8 --locale=C
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar cluster descartável.' }
    # Aguarde apenas pg_ctl; esperar a árvore inteira aguardaria também o servidor.
    $noraControl = Start-Process -FilePath $noraCtl -ArgumentList @('start', '-D', ('"' + $noraData + '"'), '-l', ('"' + $noraLog + '"'), '-o', ('"-p ' + $Port + ' -h 127.0.0.1 -c max_connections=16 -c shared_buffers=16MB"'), '-w', '-t', '30') -WindowStyle Hidden -PassThru
    if (-not $noraControl.WaitForExit(40000)) { throw 'Controle do cluster descartável excedeu o prazo.' }
    if ($noraControl.ExitCode -ne 0) { throw 'Cluster descartável não iniciou. Confira a porta e o log local.' }
    $noraControl.Dispose()
    $noraStarted = $true
    $env:NORA_TEST_CONNECTION = "Host=127.0.0.1;Port=$Port;Database=postgres;Username=nora_checks"
    if ($IncludeUnitDatabases) {
        $noraCreateDb = Join-Path $PostgreSqlBin 'createdb.exe'
        if (-not (Test-Path -LiteralPath $noraCreateDb)) { throw 'O utilitário createdb.exe não foi encontrado.' }
        foreach ($noraLetter in @('A','B','C','D','E','F')) {
            $noraUnitDb = 'nora_unit_' + $noraLetter.ToLowerInvariant()
            & $noraCreateDb -h 127.0.0.1 -p $Port -U nora_checks $noraUnitDb
            if ($LASTEXITCODE -ne 0) { throw "Falha ao criar banco descartável $noraUnitDb." }
            [Environment]::SetEnvironmentVariable("NORA_TEST_UNIT_${noraLetter}_CONNECTION", "Host=127.0.0.1;Port=$Port;Database=$noraUnitDb;Username=nora_checks", 'Process')
        }
    } else {
        foreach ($noraVariable in $noraSavedUnits.Keys) { [Environment]::SetEnvironmentVariable($noraVariable, $null, 'Process') }
    }
    & dotnet $noraCheckDll
    if ($LASTEXITCODE -ne 0) { throw 'Checks HTTP/PostgreSQL falharam.' }
}
finally {
    $env:NORA_TEST_CONNECTION = $noraOldConnection
    foreach ($noraVariable in $noraSavedUnits.Keys) { [Environment]::SetEnvironmentVariable($noraVariable, $noraSavedUnits[$noraVariable], 'Process') }
    if ($noraStarted) {
        $noraControl = Start-Process -FilePath $noraCtl -ArgumentList @('stop', '-D', ('"' + $noraData + '"'), '-m', 'fast', '-w', '-t', '30') -WindowStyle Hidden -PassThru
        if (-not $noraControl.WaitForExit(40000) -or $noraControl.ExitCode -ne 0) { Write-Warning 'Instância descartável não confirmou encerramento; confira o log local.' }
        $noraControl.Dispose()
    }
    Write-Host "Evidência preservada em $noraDirectory. Nenhuma base de uso foi acessada."
}
