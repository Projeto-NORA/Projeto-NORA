#Requires -Version 7.0
<#
.SYNOPSIS
  Cria a chave de assinatura do app Android do NORA (P-048) FORA do repositório e grava o keystore.properties que o Gradle lê.

.DESCRIPTION
  Gera, com o keytool do JDK, uma chave RSA 4096 com validade de 25 anos e escreve ao lado dela um keystore.properties
  (storeFile, storePassword, keyAlias, keyPassword). As senhas são pedidas com Read-Host -AsSecureString: nunca vão em argumento
  de linha de comando nem aparecem na tela. Nada disso entra no Git.

  ATENÇÃO: a chave e a senha precisam de BACKUP (pasta cifrada, cofre de senhas, pen drive guardado). Quem perde a chave não consegue
  mais publicar atualizações do mesmo app: o Android só atualiza um app por cima quando a assinatura é a mesma.

  Depois de criada, `scripts\empacotar-clientes.ps1 -Release` já encontra o arquivo no caminho padrão.

.PARAMETER Pasta
  Onde guardar a chave. Padrão: %USERPROFILE%\NORA-assinatura (fora do repositório).

.PARAMETER Alias
  Apelido da chave dentro do keystore. Padrão: nora.

.PARAMETER Nome
  Nome do titular gravado no certificado (CN). Padrão: NORA.

.PARAMETER Forcar
  Recria mesmo se já existir uma chave na pasta. Isso APAGA a chave anterior: só use sabendo que nenhum app assinado por ela foi distribuído.
#>
param(
    [string] $Pasta = (Join-Path $env:USERPROFILE 'NORA-assinatura'),
    [string] $Alias = 'nora',
    [string] $Nome = 'NORA',
    [switch] $Forcar
)
$ErrorActionPreference = 'Stop'

function ParaTexto([securestring] $s) {
    $p = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($s)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringUni($p) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($p) }
}

try {
    $raiz = Split-Path -Parent $PSScriptRoot
    $pastaCheia = [IO.Path]::GetFullPath($Pasta)
    if ($pastaCheia.StartsWith($raiz, [StringComparison]::OrdinalIgnoreCase)) {
        throw "A pasta '$pastaCheia' fica dentro do repositório. Escolha uma pasta fora dele (a chave nunca vai para o Git)."
    }
    $keytool = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\keytool.exe' } else { (Get-Command keytool -ErrorAction SilentlyContinue).Source }
    if (-not $keytool -or -not (Test-Path $keytool)) { throw 'keytool não encontrado. Defina JAVA_HOME (JDK 21) ou coloque o keytool no PATH.' }

    $jks = Join-Path $pastaCheia 'nora-release.jks'
    $props = Join-Path $pastaCheia 'keystore.properties'
    if ((Test-Path $jks) -and -not $Forcar) {
        throw "Já existe uma chave em $jks. Não vou sobrescrever (perder a chave impede atualizar o app). Use -Forcar só se tiver certeza."
    }
    New-Item -ItemType Directory -Force $pastaCheia | Out-Null

    Write-Host "[1/3] Senhas da chave (mínimo de 8 caracteres; não aparecem na tela)..." -ForegroundColor Cyan
    $s1 = Read-Host 'Senha do keystore' -AsSecureString
    $s2 = Read-Host 'Repita a senha' -AsSecureString
    $senha = ParaTexto $s1
    if ($senha -ne (ParaTexto $s2)) { throw 'As senhas não conferem.' }
    if ($senha.Length -lt 8) { throw 'Use uma senha de pelo menos 8 caracteres.' }
    if ($senha -match '["\r\n\]') { throw 'Evite aspas, barra invertida e quebra de linha na senha (quebram o keystore.properties).' }

    Write-Host "[2/3] Gerando a chave RSA 4096 (validade de 25 anos)..." -ForegroundColor Cyan
    if (Test-Path $jks) { Remove-Item $jks -Force }
    # As senhas vão pelo stdin do keytool, não em argumento visível na lista de processos. Com a mesma senha para o keystore e a chave
    # (PKCS12 exige isso), o keytool pergunta uma vez e confirma.
    $entrada = "$senha`n$senha`n"
    $entrada | & $keytool -genkeypair -keystore $jks -storetype PKCS12 -alias $Alias -keyalg RSA -keysize 4096 -validity 9125 `
        -dname "CN=$Nome, O=NORA" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $jks)) { throw "O keytool falhou (código $LASTEXITCODE)." }

    Write-Host "[3/3] Gravando o keystore.properties ao lado da chave..." -ForegroundColor Cyan
    @(
        '# Assinatura de release do NORA. Fora do Git. FAÇA BACKUP deste arquivo e do .jks.',
        'storeFile=nora-release.jks',
        "storePassword=$senha",
        "keyAlias=$Alias",
        "keyPassword=$senha"
    ) | Set-Content -Path $props -Encoding ascii
    # Só o dono lê os arquivos (herança removida; efeito no Windows e, onde existir, no chmod).
    foreach ($f in $jks, $props) {
        & icacls $f /inheritance:r /grant:r "$($env:USERNAME):(F)" 2>&1 | Out-Null
    }

    Write-Host "      OK  $jks" -ForegroundColor Green
    Write-Host "      OK  $props" -ForegroundColor Green
    Write-Host ''
    Write-Host 'GUARDE UM BACKUP AGORA da pasta inteira (nora-release.jks + keystore.properties) em outro lugar seguro.' -ForegroundColor Yellow
    Write-Host 'Se a chave ou a senha se perderem, não será possível atualizar o app já instalado: todos teriam de desinstalar e reinstalar.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Para gerar o APK assinado:  pwsh -File scripts\empacotar-clientes.ps1 -Release' -ForegroundColor Green
    Write-Host "O script procura $props sozinho; para outro lugar, defina NORA_ANDROID_SIGNING com o caminho do keystore.properties." -ForegroundColor Green
} catch {
    Write-Host ("      FALHOU  {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 1
}
