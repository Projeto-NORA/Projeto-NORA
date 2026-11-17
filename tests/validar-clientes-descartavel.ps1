#Requires -Version 7.0
param(
    [string] $PostgreSqlBin = 'C:\Program Files\PostgreSQL\18\bin',
    [ValidateRange(1024, 65535)] [int] $Port = 55440,
    [switch] $Android,
    [string] $JavaHome = $(if ($env:JAVA_HOME) { $env:JAVA_HOME } else { 'C:\Program Files\Android\Android Studio\jbr' }),
    [string] $AndroidSdk = $(if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }),
    [string] $PreviewDirectory = '.build\previews-e2e',
    [ValidateRange(60, 1800)] [int] $TestTimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
$noraRoot = Split-Path -Parent $PSScriptRoot
$noraApiChecks = Join-Path $noraRoot '.build\Nora.Api.Checks\bin\Release\net10.0\Nora.Api.Checks.dll'
$noraDesktopChecks = Join-Path $noraRoot '.build\Nora.Desktop.Checks\bin\Release\net10.0-windows\Nora.Desktop.Checks.dll'
$noraInit = Join-Path $PostgreSqlBin 'initdb.exe'
$noraCtl = Join-Path $PostgreSqlBin 'pg_ctl.exe'
$noraMetadata = Join-Path $noraRoot '.build\client-fixture.json'
$noraStop = Join-Path $noraRoot '.build\client-fixture.stop'
foreach ($noraRequired in @($noraApiChecks, $noraDesktopChecks, $noraInit, $noraCtl)) {
    if (-not (Test-Path -LiteralPath $noraRequired)) { throw "Arquivo necessário ausente: $noraRequired. Compile Release e confira os binários PostgreSQL18." }
}
if (Test-Path -LiteralPath $noraMetadata) { throw 'Já existe metadata de uma fixture. Confira a execução anterior antes de iniciar outra; nenhum processo foi encerrado.' }
if ($Android) {
    $noraJavaRelease = Join-Path $JavaHome 'release'
    if (-not (Test-Path -LiteralPath $noraJavaRelease) -or -not (Select-String -LiteralPath $noraJavaRelease -Pattern '^JAVA_VERSION="21\.' -Quiet)) { throw 'Informe -JavaHome com JDK21; o Android Studio inclui esse runtime em jbr.' }
    if (-not (Test-Path -LiteralPath (Join-Path $AndroidSdk 'platform-tools\adb.exe'))) { throw 'Informe -AndroidSdk com SDK Android instalado.' }
}
$noraDirectory = Join-Path $noraRoot ('.build\clients-validation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $noraDirectory | Out-Null
$noraData = Join-Path $noraDirectory 'data'
$noraPreview = if ([IO.Path]::IsPathRooted($PreviewDirectory)) { $PreviewDirectory } else { Join-Path $noraRoot $PreviewDirectory }
$noraEnvironmentNames = @('NORA_TEST_CONNECTION', 'NORA_TEST_REGISTRATION', 'NORA_TEST_UNIT_A_CONNECTION', 'NORA_TEST_UNIT_B_CONNECTION', 'NORA_TEST_UNIT_C_CONNECTION', 'NORA_E2E_URL', 'NORA_E2E_UNIT_REGISTRY', 'NORA_E2E_IDENTIFIER', 'NORA_E2E_PASSWORD', 'NORA_E2E_ATTENDANT_IDENTIFIER', 'NORA_E2E_ATTENDANT_PASSWORD', 'JAVA_HOME', 'ANDROID_HOME', 'ANDROID_SDK_ROOT', 'GRADLE_USER_HOME')
$noraPreviousEnvironment = @{}
foreach ($noraName in $noraEnvironmentNames) { $noraPreviousEnvironment[$noraName] = [Environment]::GetEnvironmentVariable($noraName, 'Process') }
$noraStarted = $false
$noraStartAttempted = $false
$noraFixture = $null
$noraExitCode = 1

function Start-NoraCheckProcess([string] $File, [string[]] $Arguments, [string] $Name, [string] $WorkingDirectory = $noraRoot) {
    Start-Process -FilePath $File -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $noraDirectory "$Name.stdout.log") -RedirectStandardError (Join-Path $noraDirectory "$Name.stderr.log")
}
function Wait-NoraCheckProcess($Process, [int] $Seconds, [string] $Name) {
    if (-not $Process.WaitForExit($Seconds * 1000)) {
        # Encerra exclusivamente o processo e descendentes criados por este harness.
        $Process.Kill($true)
        $Process.WaitForExit(10000) | Out-Null
        throw "$Name excedeu o prazo; confira os logs da execução."
    }
    $Process.ExitCode
}

function Wait-NoraFixtureMetadata($Process) {
    $noraReadyDeadline = [DateTime]::UtcNow.AddSeconds(90)
    $noraInfo = $null
    while ($null -eq $noraInfo) {
        if ($Process.HasExited) { throw 'Fixture encerrou antes de publicar metadata; confira fixture.stderr.log.' }
        if ([DateTime]::UtcNow -ge $noraReadyDeadline) { throw 'Fixture não ficou pronta no prazo.' }
        if (Test-Path -LiteralPath $noraMetadata) {
            # O arquivo pode ter surgido antes de WriteAllText concluir a escrita.
            try { $noraInfo = Get-Content -LiteralPath $noraMetadata -Raw | ConvertFrom-Json } catch { }
        }
        if ($null -eq $noraInfo) { Start-Sleep -Milliseconds 250 }
    }
    $noraUri = [uri]$noraInfo.address
    if (-not $noraUri.IsLoopback -or $noraUri.Scheme -ne 'http' -or $noraInfo.schema -notmatch '^nora_clients_[a-f0-9]{32}$') { throw 'Metadata não corresponde à fixture local descartável.' }
    $noraInfo
}
# Cria três bancos locais descartáveis (um por UBS "Cadastro E2E") e aponta as variáveis da fixture para eles. A fixture vincula cada banco a
# uma única UBS, então cada subida da fixture precisa de bancos novos (sufixo diferente): reaproveitar um banco de outra execução é recusado.
function New-NoraUnitDatabases([string] $Suffix) {
    $noraCreateDb = Join-Path $PostgreSqlBin 'createdb.exe'
    if (-not (Test-Path -LiteralPath $noraCreateDb)) { throw 'O utilitário createdb.exe não foi encontrado.' }
    foreach ($noraLetter in @('A', 'B', 'C')) {
        $noraUnitDb = 'nora_unit_' + $noraLetter.ToLowerInvariant() + $Suffix
        & $noraCreateDb -h 127.0.0.1 -p $Port -U nora_checks $noraUnitDb
        if ($LASTEXITCODE -ne 0) { throw "Falha ao criar banco descartável $noraUnitDb." }
        [Environment]::SetEnvironmentVariable("NORA_TEST_UNIT_${noraLetter}_CONNECTION", "Host=127.0.0.1;Port=$Port;Database=$noraUnitDb;Username=nora_checks", 'Process')
    }
}
# Encerra a fixture pelo arquivo de parada e confere a limpeza; usada ao trocar a fixture do desktop pela do Android.
function Stop-NoraFixtureProcess($Process) {
    if (-not $Process.HasExited) {
        [IO.File]::WriteAllText($noraStop, 'Encerrar fixture criada pelo harness.')
        if (-not $Process.WaitForExit(40000)) { $Process.Kill($true); $Process.WaitForExit(10000) | Out-Null; throw 'Fixture não confirmou a limpeza no prazo.' }
    }
    if ($Process.ExitCode -ne 0) { throw 'Fixture encerrou com erro; confira os logs da limpeza.' }
    if (Test-Path -LiteralPath $noraStop) { Remove-Item -LiteralPath $noraStop }
    $Process.Dispose()
}

try {
    $noraInitialize = Start-NoraCheckProcess $noraInit @('-D', ('"' + $noraData + '"'), '-U', 'nora_checks', '-A', 'trust', '--encoding=UTF8', '--locale=C') 'initdb'
    try {
        if ((Wait-NoraCheckProcess $noraInitialize 60 'initdb') -ne 0) { throw 'Falha ao preparar cluster descartável.' }
    } finally { $noraInitialize.Dispose() }
    # Não use Start-Process -Wait: ele aguardaria também a árvore do servidor.
    $noraStartAttempted = $true
    $noraControl = Start-NoraCheckProcess $noraCtl @('start', '-D', ('"' + $noraData + '"'), '-l', ('"' + (Join-Path $noraDirectory 'server.log') + '"'), '-o', ('"-p ' + $Port + ' -h 127.0.0.1 -c max_connections=80 -c shared_buffers=16MB"'), '-w', '-t', '30') 'pg-start'
    try {
        if ((Wait-NoraCheckProcess $noraControl 40 'pg_ctl start') -ne 0) { throw 'Cluster descartável não iniciou. Confira a porta e os logs.' }
    } finally { $noraControl.Dispose() }
    $noraStarted = $true
    $env:NORA_TEST_CONNECTION = "Host=127.0.0.1;Port=$Port;Database=postgres;Username=nora_checks"
    # A fixture roda no modo por UBS (três "UBS Cadastro E2E", bancos separados, conta OBS com as três concedidas): é o único fluxo que o E2E do desktop exercita.
    New-NoraUnitDatabases ''
    $env:NORA_TEST_REGISTRATION = '1'
    $env:NORA_E2E_UNIT_REGISTRY = '1'
    $noraFixture = Start-NoraCheckProcess 'dotnet' @(('"' + $noraApiChecks + '"'), '--client-fixture') 'fixture'
    $noraFixtureInfo = Wait-NoraFixtureMetadata $noraFixture
    $noraEndpoint = [uri]$noraFixtureInfo.address
    Copy-Item -LiteralPath $noraMetadata -Destination (Join-Path $noraDirectory 'fixture.json')
    $env:NORA_E2E_URL = $noraEndpoint.AbsoluteUri
    $env:NORA_E2E_IDENTIFIER = 'operator@example.test'
    $env:NORA_E2E_PASSWORD = 'Disposable-test-Account!29'
    # Conta de atendente da fixture (concessão nas três UBS): o E2E de computadores do OBS ativa o computador pelo operador e entra com ela.
    $env:NORA_E2E_ATTENDANT_IDENTIFIER = 'attendant@example.test'
    $env:NORA_E2E_ATTENDANT_PASSWORD = 'Disposable-test-Account!29'
    $noraDesktop = Start-NoraCheckProcess 'dotnet' @(('"' + $noraDesktopChecks + '"'), '--preview-dir', ('"' + $noraPreview + '"')) 'desktop'
    try { $noraExitCode = Wait-NoraCheckProcess $noraDesktop $TestTimeoutSeconds 'Checks WPF' } finally { $noraDesktop.Dispose() }
    if ($noraExitCode -ne 0) { throw 'Checks WPF/E2E falharam; confira desktop.stdout.log e desktop.stderr.log.' }
    if ($Android) {
        # O teste real do app usa os dados originais da fixture (o E2E do desktop os alterou), então ela é reiniciada com bancos novos por UBS.
        Stop-NoraFixtureProcess $noraFixture
        $noraFixture = $null
        New-NoraUnitDatabases '_android'
        $noraFixture = Start-NoraCheckProcess 'dotnet' @(('"' + $noraApiChecks + '"'), '--client-fixture') 'fixture-android'
        $noraFixtureInfo = Wait-NoraFixtureMetadata $noraFixture
        $noraEndpoint = [uri]$noraFixtureInfo.address
        Copy-Item -LiteralPath $noraMetadata -Destination (Join-Path $noraDirectory 'fixture-android.json')
        $env:JAVA_HOME = $JavaHome
        $env:ANDROID_HOME = $AndroidSdk
        $env:ANDROID_SDK_ROOT = $AndroidSdk
        $env:GRADLE_USER_HOME = Join-Path $noraRoot '.build\gradle-user'
        $noraGradle = Join-Path $noraRoot 'APPNORA\gradlew.bat'
        $noraAndroidUrl = 'http://10.0.2.2:' + $noraEndpoint.Port
        $noraGradleCommand = '""' + $noraGradle + '" :app:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.noraApiUrl=' + $noraAndroidUrl + ' --console=plain --no-daemon"'
        $noraAndroid = Start-NoraCheckProcess $env:ComSpec @('/d', '/s', '/c', $noraGradleCommand) 'android' (Join-Path $noraRoot 'APPNORA')
        try { $noraExitCode = Wait-NoraCheckProcess $noraAndroid $TestTimeoutSeconds 'Checks Android conectado' } finally { $noraAndroid.Dispose() }
        if ($noraExitCode -ne 0) { throw 'Checks Android/E2E falharam; confira os logs e relatórios instrumentados.' }
    }
    Write-Host 'Clientes/API/PostgreSQL descartáveis validados. Nenhuma conta pessoal foi usada.'
} catch {
    if ($noraExitCode -eq 0) { $noraExitCode = 1 }
    Write-Warning $_.Exception.Message
} finally {
    if ($null -ne $noraFixture) {
        try {
            if (-not $noraFixture.HasExited) {
                [IO.File]::WriteAllText($noraStop, 'Encerrar fixture criada pelo harness.')
                if (-not $noraFixture.WaitForExit(40000)) {
                    Write-Warning 'Fixture não confirmou limpeza; seu processo será encerrado. Evidência do cluster foi preservada.'
                    $noraFixture.Kill($true)
                    $noraFixture.WaitForExit(10000) | Out-Null
                    $noraExitCode = 1
                }
            }
            if ($noraFixture.ExitCode -ne 0) { $noraExitCode = 1; Write-Warning 'Fixture encerrou com erro; confira os logs da limpeza.' }
            if (Test-Path -LiteralPath $noraStop) { Remove-Item -LiteralPath $noraStop }
        } catch { $noraExitCode = 1; Write-Warning 'Não foi possível confirmar o encerramento da fixture criada.' }
        finally { $noraFixture.Dispose() }
    }
    if ($noraStarted -or ($noraStartAttempted -and (Test-Path -LiteralPath (Join-Path $noraData 'postmaster.pid')))) {
        try {
            $noraControl = Start-NoraCheckProcess $noraCtl @('stop', '-D', ('"' + $noraData + '"'), '-m', 'fast', '-w', '-t', '30') 'pg-stop'
            try {
                if ((Wait-NoraCheckProcess $noraControl 40 'pg_ctl stop') -ne 0) { $noraExitCode = 1; Write-Warning 'Cluster criado não confirmou encerramento; confira os logs.' }
            } finally { $noraControl.Dispose() }
        } catch { $noraExitCode = 1; Write-Warning 'Falha ao encerrar o cluster descartável criado; confira seu diretório de evidência.' }
    }
    foreach ($noraName in $noraEnvironmentNames) { [Environment]::SetEnvironmentVariable($noraName, $noraPreviousEnvironment[$noraName], 'Process') }
    Write-Host "Logs e cluster descartável preservados em $noraDirectory. Prévias WPF em $noraPreview."
}
exit $noraExitCode
