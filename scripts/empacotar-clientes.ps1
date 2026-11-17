#Requires -Version 7.0
<#
.SYNOPSIS
  Gera os três aplicativos do NORA prontos para enviar: OBS-NORA.exe, Painel-Municipal.exe (Windows, sem precisar
  instalar o .NET) e NORA-paciente.apk (Android). Guia em docs/distribuicao.md.

.PARAMETER Saida
  Pasta de destino. Padrão: dist\<data-hora> na raiz do repositório (fora do Git).

.PARAMETER SemAndroid
  Pula o APK (por exemplo, numa máquina sem JDK/Android SDK).
#>
param(
    [string] $Saida,
    [switch] $SemAndroid
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
if (-not $Saida) { $Saida = Join-Path $raiz ('dist\' + (Get-Date -Format 'yyyy-MM-dd_HHmm')) }
New-Item -ItemType Directory -Force $Saida | Out-Null

$passos = @('Publicando OBS-NORA', 'Publicando Painel-Municipal')
if (-not $SemAndroid) { $passos += 'Gerando o APK do app do paciente' }
$passos += 'Compactando para envio'
$atual = 0
function Passo([string] $nome) {
    $script:atual++
    Write-Host ("[{0}/{1}] {2}..." -f $script:atual, $passos.Count, $nome) -ForegroundColor Cyan
}
function Feito([string] $detalhe) { Write-Host ("      OK  {0}" -f $detalhe) -ForegroundColor Green }

function Publicar([string] $projeto, [string] $nome) {
    $destino = Join-Path $Saida $nome
    # Arquivo único, com o runtime do .NET embutido: quem recebe não precisa instalar nada.
    & dotnet publish (Join-Path $raiz "$projeto\$projeto.csproj") -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None `
        -o $destino --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar $projeto (código $LASTEXITCODE)." }
    $exe = Get-ChildItem $destino -Filter *.exe | Select-Object -First 1
    if (-not $exe) { throw "O publish de $projeto não gerou .exe." }
    Feito ("{0} ({1:N0} MB)" -f $exe.FullName, ($exe.Length / 1MB))
}

try {
    Passo $passos[0]; Publicar 'OBS-NORA' 'OBS-NORA'
    Passo $passos[1]; Publicar 'Painel-Municipal' 'Painel-Municipal'

    if (-not $SemAndroid) {
        Passo $passos[2]
        if (-not $env:JAVA_HOME) { throw 'Defina JAVA_HOME (JDK 21) e ANDROID_HOME antes, ou rode com -SemAndroid.' }
        Push-Location (Join-Path $raiz 'APPNORA')
        try {
            # APK de depuração: assinado com a chave de depuração do SDK, instala em qualquer Android e aceita só HTTPS
            # (HTTP apenas para o emulador). O APK de release exige chave de assinatura própria (ver docs/distribuicao.md).
            & .\gradlew.bat :app:assembleDebug --console=plain -q
            if ($LASTEXITCODE -ne 0) { throw "Falha no Gradle (código $LASTEXITCODE)." }
        } finally { Pop-Location }
        $apk = Join-Path $raiz 'APPNORA\app\build\outputs\apk\debug\app-debug.apk'
        Copy-Item $apk (Join-Path $Saida 'NORA-paciente.apk') -Force
        Feito (Join-Path $Saida 'NORA-paciente.apk')
    }

    Passo $passos[-1]
    foreach ($nome in 'OBS-NORA', 'Painel-Municipal') {
        $zip = Join-Path $Saida "$nome.zip"
        Compress-Archive -Path (Join-Path $Saida "$nome\*") -DestinationPath $zip -Force
        Feito $zip
    }
    Write-Host "`nPronto. Envie os arquivos .zip (desktop) e o .apk (Android) de $Saida" -ForegroundColor Green
} catch {
    Write-Host ("      FALHOU  {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 1
}
