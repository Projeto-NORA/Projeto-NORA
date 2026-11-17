#Requires -Version 7.0
<#
.SYNOPSIS
  Gera os três aplicativos do NORA prontos para enviar: OBS-NORA.exe, Painel-Municipal.exe (Windows, sem precisar
  instalar o .NET) e NORA-paciente.apk (Android). Guia em docs/distribuicao.md.

.PARAMETER Saida
  Pasta de destino. Padrão: dist\<data-hora> na raiz do repositório (fora do Git).

.PARAMETER SemAndroid
  Pula o APK (por exemplo, numa máquina sem JDK/Android SDK).

.PARAMETER Release
  Gera o NORA-paciente.apk de RELEASE, assinado com a chave própria (em vez do APK de depuração). Exige o arquivo de propriedades da
  chave (storeFile, storePassword, keyAlias, keyPassword), procurado em: variável NORA_ANDROID_SIGNING, APPNORA\keystore.properties e
  %USERPROFILE%\NORA-assinatura\keystore.properties. Para criar a chave: scripts\criar-chave-android.ps1. Guia em docs/distribuicao.md.
#>
param(
    [string] $Saida,
    [switch] $SemAndroid,
    [switch] $Release
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
if (-not $Saida) { $Saida = Join-Path $raiz ('dist\' + (Get-Date -Format 'yyyy-MM-dd_HHmm')) }
New-Item -ItemType Directory -Force $Saida | Out-Null

# -Release: confere a chave ANTES de publicar os desktops (falhar depois de 10 minutos de publish seria cruel).
$propriedadesChave = $null
if ($Release -and -not $SemAndroid) {
    $propriedadesChave = @($env:NORA_ANDROID_SIGNING,
        (Join-Path $raiz 'APPNORA\keystore.properties'),
        (Join-Path $env:USERPROFILE 'NORA-assinatura\keystore.properties')) |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
    if (-not $propriedadesChave) {
        Write-Host ('FALHOU  Falta a chave de assinatura para o APK de release. Rode scripts\criar-chave-android.ps1 (cria %USERPROFILE%\NORA-assinatura\keystore.properties) ' +
            'ou aponte NORA_ANDROID_SIGNING para o seu keystore.properties. Sem -Release o script gera o APK de depuração.') -ForegroundColor Red
        exit 1
    }
    $propriedadesChave = (Resolve-Path -LiteralPath $propriedadesChave).Path
}

$passos = @('Publicando OBS-NORA', 'Publicando Painel-Municipal')
if (-not $SemAndroid) { $passos += $(if ($Release) { 'Gerando o APK de release assinado do app do paciente' } else { 'Gerando o APK do app do paciente' }) }
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
        $tarefa = ':app:assembleDebug'
        if ($Release) {
            $env:NORA_ANDROID_SIGNING = $propriedadesChave   # o Gradle lê o arquivo por esta variável (só neste processo)
            $tarefa = ':app:assembleRelease'
            Feito "Chave: $propriedadesChave"
        }
        Push-Location (Join-Path $raiz 'APPNORA')
        try {
            # Depuração: assinado com a chave de depuração do SDK, instala em qualquer Android e aceita só HTTPS (HTTP apenas para o
            # emulador). Release: assinado com a chave própria, com R8 (docs/distribuicao.md).
            & .\gradlew.bat $tarefa --console=plain -q
            if ($LASTEXITCODE -ne 0) { throw "Falha no Gradle (código $LASTEXITCODE)." }
        } finally { Pop-Location }
        $apk = Join-Path $raiz $(if ($Release) { 'APPNORA\app\build\outputs\apk\release\app-release.apk' } else { 'APPNORA\app\build\outputs\apk\debug\app-debug.apk' })
        if (-not (Test-Path $apk)) { throw "O Gradle não gerou $apk (o release sem chave sai como app-release-unsigned.apk)." }
        if ($Release -and $env:ANDROID_HOME) {
            $apksigner = Get-ChildItem (Join-Path $env:ANDROID_HOME 'build-tools') -Filter apksigner.bat -Recurse -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1
            if ($apksigner) {
                & $apksigner.FullName verify $apk | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'O apksigner não aprovou a assinatura do APK de release.' }
                Feito 'Assinatura conferida com o apksigner'
            }
        }
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
