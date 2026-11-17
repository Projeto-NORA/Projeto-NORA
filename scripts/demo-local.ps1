#Requires -Version 7.0
<#
.SYNOPSIS
  Liga o NORA inteiro no notebook para apresentação: PostgreSQL 18 próprio da demo, API em http://127.0.0.1:<Porta>,
  OBS-NORA, Painel Municipal e, com -Android, o app no emulador. Os dados da demo ficam guardados entre execuções.

.DESCRIPTION
  Primeira execução: cria o banco da demo, gera as chaves da API, pede o e-mail do Gmail e a senha de app (guardados
  cifrados com a proteção do Windows, só para este usuário, em %LOCALAPPDATA%\NORA\demo), aplica as migrações e
  pede para criar as contas iniciais (Secretaria e administrador da UBS). Nada disso vai para o Git.
  Roteiro e explicação em docs/distribuicao.md e no roteiro da apresentação.

.EXAMPLE
  pwsh -File scripts\demo-local.ps1 -Android          # liga tudo, inclusive emulador + app
  pwsh -File scripts\demo-local.ps1 -Parar            # desliga o banco da demo (feche a janela da API)
  pwsh -File scripts\demo-local.ps1 -Zerar            # apaga os dados da demo e começa do zero (pede confirmação)
#>
param(
    [int] $Porta = 5080,
    [int] $PortaBanco = 55432,
    [string] $Pasta = (Join-Path $env:LOCALAPPDATA 'NORA\demo'),
    [string] $PostgreSqlBin = 'C:\Users\Pichau\AppData\Local\Programs\PostgreSQL\18\bin',
    [string] $Avd = 'Medium_Phone_API_37.0',
    [switch] $Android,
    [switch] $SemClientes,
    [switch] $Recompilar,
    [switch] $Parar,
    [switch] $Zerar
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$dados = Join-Path $Pasta 'pgdata'
$api = Join-Path $Pasta 'api'
$segredos = Join-Path $Pasta 'segredos.xml'
$contasCriadas = Join-Path $Pasta 'contas-criadas.txt'
$banco = 'nora_demo'
$usuarioBanco = 'nora_demo'

if (-not (Test-Path (Join-Path $PostgreSqlBin 'pg_ctl.exe'))) {
    $achado = Get-ChildItem 'C:\Program Files\PostgreSQL\18\bin\pg_ctl.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($achado) { $PostgreSqlBin = $achado.DirectoryName } else { throw "PostgreSQL 18 não encontrado em $PostgreSqlBin. Informe -PostgreSqlBin." }
}
function Pg([string] $exe) { Join-Path $PostgreSqlBin $exe }

# pg_ctl deixa o servidor herdando as saídas; rodar sem pipe (janela oculta + log em arquivo) evita travar o script.
function PgCtl([string[]] $argumentos) {
    # Sem -Wait: o Start-Process -Wait espera também os filhos, e o postgres iniciado pelo pg_ctl nunca termina.
    $p = Start-Process (Pg 'pg_ctl.exe') -ArgumentList $argumentos -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(60000)) { $p.Kill(); return 1 }
    return $p.ExitCode
}
function BancoLigado { (PgCtl @('status', '-D', "`"$dados`"")) -eq 0 }

if ($Parar) {
    if (Test-Path $dados) { if (BancoLigado) { PgCtl @('stop', '-D', "`"$dados`"", '-m', 'fast', '-w') | Out-Null } }
    Write-Host 'Banco da demo desligado. Feche a janela da API e os aplicativos.' -ForegroundColor Green
    return
}
if ($Zerar) {
    $ok = Read-Host "Isto apaga TODOS os dados da demo em $Pasta (contas, UBS, pacientes). Digite APAGAR para confirmar"
    if ($ok -ne 'APAGAR') { Write-Host 'Nada foi apagado.'; return }
    if ((Test-Path $dados) -and (BancoLigado)) { PgCtl @('stop', '-D', "`"$dados`"", '-m', 'fast', '-w') | Out-Null }
    Remove-Item $Pasta -Recurse -Force
    Write-Host 'Dados da demo apagados. Rode o script de novo para começar do zero.' -ForegroundColor Green
    return
}

$passos = [System.Collections.Generic.List[string]]::new()
$passos.AddRange([string[]]@('Ligando o banco da demo', 'Preparando a API', 'Conferindo chaves e e-mail', 'Atualizando o banco', 'Conferindo contas iniciais', 'Ligando a API'))
if (-not $SemClientes) { $passos.Add('Abrindo OBS e Painel Municipal') }
if ($Android) { $passos.Add('Ligando o emulador e instalando o app') }
$atual = 0
function Passo([string] $nome) { $script:atual++; Write-Host ("[{0}/{1}] {2}..." -f $script:atual, $passos.Count, $nome) -ForegroundColor Cyan }
function Feito([string] $detalhe) { Write-Host ("      OK  {0}" -f $detalhe) -ForegroundColor Green }

try {
    New-Item -ItemType Directory -Force $Pasta | Out-Null

    Passo 'Ligando o banco da demo'
    if (-not (Test-Path (Join-Path $dados 'PG_VERSION'))) {
        & (Pg 'initdb.exe') -D $dados -U $usuarioBanco -A trust --encoding=UTF8 --locale=C *> (Join-Path $Pasta 'initdb.log')
        if ($LASTEXITCODE -ne 0) { throw "initdb falhou; veja $Pasta\initdb.log" }
    }
    if (-not (BancoLigado)) {
        $codigo = PgCtl @('start', '-D', "`"$dados`"", '-l', "`"$(Join-Path $Pasta 'postgres.log')`"", '-o', "`"-p $PortaBanco -h 127.0.0.1`"", '-w', '-t', '30')
        if ($codigo -ne 0) { throw "O PostgreSQL da demo não subiu; veja $Pasta\postgres.log" }
    }
    $existe = & (Pg 'psql.exe') -h 127.0.0.1 -p $PortaBanco -U $usuarioBanco -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$banco'"
    if ($existe -ne '1') { & (Pg 'createdb.exe') -h 127.0.0.1 -p $PortaBanco -U $usuarioBanco $banco; if ($LASTEXITCODE -ne 0) { throw 'Não foi possível criar o banco da demo.' } }
    Feito "PostgreSQL 18 em 127.0.0.1:$PortaBanco (dados em $dados)"

    Passo 'Preparando a API'
    $dll = Join-Path $api 'Nora.Api.dll'
    if ($Recompilar -or -not (Test-Path $dll)) {
        & dotnet publish (Join-Path $raiz 'Nora.Api\Nora.Api.csproj') -c Release -o $api --nologo -v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a API.' }
    }
    Feito $dll

    Passo 'Conferindo chaves e e-mail'
    if (-not (Test-Path $segredos)) {
        function NovaChave { ConvertTo-SecureString ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))) -AsPlainText -Force }
        Write-Host '      O cadastro do paciente envia um código por e-mail (Gmail com verificação em 2 etapas + senha de app).'
        $email = Read-Host '      E-mail do Gmail que vai enviar os códigos'
        $senhaApp = Read-Host '      Senha de app do Gmail (16 letras; não aparece na tela)' -AsSecureString
        @{ Registration = (NovaChave); Reset = (NovaChave); EmailChange = (NovaChave); SmtpUser = $email; SmtpPassword = $senhaApp } | Export-Clixml $segredos
    }
    $s = Import-Clixml $segredos
    function Texto($seguro) { [System.Net.NetworkCredential]::new('', $seguro).Password }
    $env:ConnectionStrings__Nora = "Host=127.0.0.1;Port=$PortaBanco;Database=$banco;Username=$usuarioBanco"
    $env:Nora__Registration__HashKey = Texto $s.Registration
    $env:Nora__PasswordReset__HashKey = Texto $s.Reset
    $env:Nora__EmailChange__HashKey = Texto $s.EmailChange
    $env:Nora__Smtp__Host = 'smtp.gmail.com'
    $env:Nora__Smtp__Port = '587'
    $env:Nora__Smtp__Username = $s.SmtpUser
    $env:Nora__Smtp__Password = Texto $s.SmtpPassword
    $env:Nora__Smtp__FromAddress = $s.SmtpUser
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    # O appsettings.json da API fixa Urls e AllowedHosts; só as variáveis SEM prefixo vencem o arquivo.
    # 10.0.2.2 é como o emulador Android enxerga este computador; sem ele o app recebe 400 (host inválido).
    $env:Urls = "http://127.0.0.1:$Porta"
    $env:AllowedHosts = 'localhost;127.0.0.1;[::1];10.0.2.2'
    Feito "chaves e e-mail ($($s.SmtpUser)) guardados cifrados em $segredos"

    Passo 'Atualizando o banco'
    & dotnet $dll --migrate; if ($LASTEXITCODE -ne 0) { throw 'Falha na migração do banco central.' }
    & dotnet $dll --migrate-all-units; if ($LASTEXITCODE -ne 0) { throw 'Falha na migração dos bancos das UBS.' }
    Feito 'banco central e bancos das UBS na versão atual'

    Passo 'Conferindo contas iniciais'
    if (-not (Test-Path $contasCriadas)) {
        Write-Host '      Vamos criar duas contas. Responda "municipal" na primeira (Secretaria) e "obs" na segunda (administrador da UBS).' -ForegroundColor Yellow
        & dotnet $dll --provision; if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar a conta da Secretaria.' }
        & dotnet $dll --provision; if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar a conta do administrador.' }
        Set-Content $contasCriadas (Get-Date -Format 's')
    }
    Feito 'contas iniciais existem'

    Passo 'Ligando a API'
    $vivo = $false
    try { $vivo = (Invoke-RestMethod "http://127.0.0.1:$Porta/health/live" -TimeoutSec 2).status -eq 'alive' } catch { }
    if (-not $vivo) {
        # Janela própria com o log da API; as variáveis acima são herdadas por ela.
        Start-Process pwsh -ArgumentList '-NoExit', '-Command', "`$Host.UI.RawUI.WindowTitle='NORA API (demo) - feche para desligar'; dotnet '$dll'"
    }
    $limite = (Get-Date).AddSeconds(60); $pronto = $false
    while (-not $pronto -and (Get-Date) -lt $limite) {
        try { $pronto = (Invoke-RestMethod "http://127.0.0.1:$Porta/health/ready" -TimeoutSec 3).status -eq 'ready' } catch { Start-Sleep -Milliseconds 700 }
    }
    if (-not $pronto) { throw 'A API não respondeu em 60 s; veja a janela "NORA API (demo)".' }
    Feito "API pronta em http://127.0.0.1:$Porta"

    if (-not $SemClientes) {
        Passo 'Abrindo OBS e Painel Municipal'
        $env:NORA_API_URL = "http://127.0.0.1:$Porta"
        foreach ($p in 'OBS-NORA', 'Painel-Municipal') {
            Start-Process dotnet -ArgumentList 'run', '--project', "`"$(Join-Path $raiz "$p\$p.csproj")`"", '-c', 'Release' -WindowStyle Minimized
        }
        Feito 'abrindo (a primeira vez compila e leva cerca de 1 minuto); o endereço da API já vem preenchido'
    }

    if ($Android) {
        Passo 'Ligando o emulador e instalando o app'
        $sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
        $adb = Join-Path $sdk 'platform-tools\adb.exe'
        $aparelhos = (& $adb devices) -match "`tdevice$"
        if (-not $aparelhos) {
            Start-Process (Join-Path $sdk 'emulator\emulator.exe') -ArgumentList '-avd', $Avd, '-no-snapshot-save' -WindowStyle Normal
            & $adb wait-for-device
            $limite = (Get-Date).AddMinutes(3)
            while (((& $adb shell getprop sys.boot_completed) -join '').Trim() -ne '1' -and (Get-Date) -lt $limite) { Start-Sleep 2 }
        }
        $apk = Join-Path $raiz 'APPNORA\app\build\outputs\apk\debug\app-debug.apk'
        if ($Recompilar -or -not (Test-Path $apk)) {
            if (-not $env:JAVA_HOME) { throw 'Defina JAVA_HOME (JDK 21) para compilar o app.' }
            Push-Location (Join-Path $raiz 'APPNORA'); try { & .\gradlew.bat :app:assembleDebug --console=plain -q; if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o app.' } } finally { Pop-Location }
        }
        & $adb install -r $apk | Out-Null
        Feito "app instalado; no app use o endereço http://10.0.2.2:$Porta e permita o acesso à rede local quando ele pedir"
    }

    Write-Host "`nTudo no ar. Para desligar: feche a janela da API e rode scripts\demo-local.ps1 -Parar" -ForegroundColor Green
} catch {
    Write-Host ("      FALHOU  {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 1
}
