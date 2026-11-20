# Distribuição dos aplicativos (OBS, Painel Municipal e app do paciente)

Como gerar os três aplicativos, entregar às pessoas do piloto e o que cada uma faz para abrir. A API precisa estar no ar com HTTPS antes (ver [hospedagem.md](hospedagem.md)). Os aplicativos não guardam o endereço da API dentro deles: cada pessoa digita o endereço na primeira entrada.

| Aplicativo | Quem usa | Arquivo enviado | Onde roda |
| --- | --- | --- | --- |
| OBS-NORA | atendentes e administradores das UBS | `OBS-NORA.zip` (contém `OBS-NORA.exe`) | Windows 10 ou 11, 64 bits |
| Painel Municipal | Secretaria (gestão da rede) | `Painel-Municipal.zip` (contém `Painel-Municipal.exe`) | Windows 10 ou 11, 64 bits |
| App do paciente | pacientes | `NORA-paciente.apk` | Android 7.0 ou mais novo (minSdk 24). **Não há versão para iPhone.** |

## 1. Gerar os arquivos (no seu computador)

Precisa do .NET 10 SDK e, para o APK, do JDK 21 e do Android SDK, os mesmos de [execucao.md](execucao.md). Na raiz do repositório:

```powershell
$env:JAVA_HOME = 'C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot'
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
pwsh -File scripts\empacotar-clientes.ps1
```

Para o APK de release assinado, veja a seção 6 (`-Release`). O script mostra cada passo enquanto trabalha, `[1/4] Publicando OBS-NORA...`, e marca `OK` ao concluir.

- **Onde fica o resultado:** `dist\<data-hora>\`, fora do Git.
- **Desktop:** cada `.exe` é um arquivo único com o .NET embutido, então quem recebe não instala nada. Cada `.zip` tem por volta de 70 a 150 MB.
- **Sem Android na máquina:** use `-SemAndroid` para gerar só os desktops.

## 2. Enviar

O melhor caminho é uma pasta do Google Drive ou do OneDrive com o link compartilhado só com os participantes. E-mail costuma bloquear `.exe` e `.apk`. O WhatsApp aceita, mas o arquivo grande demora.

Junto com o link, mande três informações: o endereço da API (`https://<sua-api>.onrender.com`), o usuário e a senha provisória de cada pessoa da equipe. Para pacientes, mande só o endereço. A senha nunca vai no mesmo grupo que o link.

## 3. Abrir no Windows (OBS e Painel Municipal)

1. **Abrir:** clique com o botão direito no `.zip` → **Extrair tudo** e abra o `.exe` da pasta extraída.
2. **Aviso do Windows:** como o executável não tem assinatura digital paga, aparece "O Windows protegeu o computador". Clique em **Mais informações** → **Executar assim mesmo**. Isso é esperado num protótipo.
3. **Primeira entrada:** abra **Servidor (avançado)** (já vem aberto quando ainda não há endereço) e, em **Endereço do servidor**, digite `https://<sua-api>.onrender.com`, depois o e-mail e a senha provisória (a equipe entra só por e-mail, não por CPF). O sistema pede para trocar a senha no primeiro acesso.
4. **Só no OBS — computador ativado:**
   - O **administrador** da UBS entra em qualquer computador.
   - A **atendente** só entra num computador ativado: o administrador ativa em Configurações da UBS → Computadores, ou gera um código `XXXX-XXXX` que a atendente digita em "Código de ativação".
   - Depois de ativado, a opção "Manter conectado neste computador" fica disponível.
5. **Dica para não digitar o endereço:** quem preferir pode definir a variável de ambiente do Windows `NORA_API_URL` com o endereço. O campo já vem preenchido.

As preferências ficam em `%LOCALAPPDATA%\NORA\`. Apagar essa pasta volta o aplicativo ao estado inicial, inclusive a ativação do computador.

## 4. Instalar no Android (app do paciente)

1. Baixe o `NORA-paciente.apk` pelo link e toque no arquivo.
2. O Android pede para permitir **instalar apps desconhecidos** a partir do aplicativo usado para abrir o arquivo (Drive, Arquivos ou o navegador): **Configurações** → permitir.
3. O Play Protect pode avisar que o app não é conhecido. Toque em **Mais detalhes** → **Instalar mesmo assim**.
4. **Ao abrir:** no **Endereço do serviço NORA**, digite `https://<sua-api>.onrender.com`. O app só aceita HTTPS.
5. **Criar conta:** em **Criar conta** a pessoa informa nome, CPF e senha, escolhe a UBS e confirma o código de 6 dígitos que chega no e-mail.
   - O CPF e o e-mail precisam ter sido cadastrados antes pela UBS, no OBS.
6. **Permissões:**
   - Aceite as **notificações** para receber os avisos de Vaga Relâmpago e comunicados.
   - A permissão de **rede local** só aparece se o endereço for um IP privado. Com o Render, não aparece.

## 5. Mandar uma versão nova

- **Desktop:** gere de novo, envie o `.zip`, e a pessoa substitui a pasta antiga. As preferências e a ativação do computador ficam guardadas.
- **Android:** gere de novo e envie o `.apk`. A pessoa instala por cima e não perde nada.
  - **Cuidado:** o APK é assinado com a chave de depuração **deste computador**. Um APK gerado em outro PC tem outra assinatura, e o Android recusa a atualização ("app não instalado"). Nesse caso a pessoa desinstala o NORA e instala o novo. Gere sempre no mesmo PC.

## 6. APK de release assinado com chave própria (P-048)

O APK de release usa o R8 (código reduzido e ofuscado) e é assinado com uma chave **sua**, guardada fora do Git. É o caminho para a Play Store e para ter uma assinatura que não dependa de um computador específico. O build de depuração das seções acima **não muda**.

**1. Criar a chave (uma vez só):**

```powershell
$env:JAVA_HOME = 'C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot'
pwsh -File scripts\criar-chave-android.ps1
```

O script pede a senha duas vezes (sem mostrá-la), gera uma chave RSA 4096 com validade de 25 anos e grava `nora-release.jks` e `keystore.properties` em `%USERPROFILE%\NORA-assinatura\`, **fora do repositório**. Ele recusa uma pasta dentro do repositório e não sobrescreve uma chave existente sem `-Forcar`.

**2. Faça backup agora** da pasta `NORA-assinatura` inteira (cofre de senhas, pen drive guardado, pasta cifrada). **Se a chave ou a senha se perderem, não há como atualizar o app já instalado nem na Play Store:** todos teriam de desinstalar e instalar um app novo. Nunca coloque esses arquivos no Git (o `.gitignore` do APPNORA já ignora `keystore.properties`, `*.jks` e `*.keystore`).

**3. Gerar o APK de release:**

```powershell
pwsh -File scripts\empacotar-clientes.ps1 -Release
```

O script confere a chave antes de publicar os desktops, gera `NORA-paciente.apk` assinado (`assembleRelease`), confere a assinatura com o `apksigner` e o copia para `dist\<data-hora>\`. Sem o arquivo da chave ele para com uma mensagem clara. Só o APK: em `APPNORA`, `.\gradlew.bat :app:assembleRelease` (com a chave encontrada) gera `app\build\outputs\apk\release\app-release.apk`.

**Como o Gradle acha a chave** (`APPNORA/app/build.gradle.kts`): o arquivo `keystore.properties` (`storeFile`, `storePassword`, `keyAlias`, `keyPassword`; um `storeFile` relativo vale a partir da pasta do arquivo) é procurado na variável de ambiente ou propriedade Gradle `NORA_ANDROID_SIGNING` (caminho do arquivo) e, sem ela, em `APPNORA/keystore.properties`. O `empacotar-clientes.ps1 -Release` também procura em `%USERPROFILE%\NORA-assinatura\`. **Sem o arquivo**, `assembleRelease` continua gerando `app-release-unsigned.apk`, como antes, e o build de depuração não é afetado.

**R8 conferido num aparelho (2026-11-17, emulador Android 17):** o primeiro release **caía ao abrir** com `IllegalAccessError: Class kotlin.sequences.d extended by class qh1 is inaccessible` (o R8 renomeava partes da fachada `kotlin.sequences.SequencesKt` para pacotes diferentes). Corrigido com `-keepnames class kotlin.** { *; }` em `APPNORA/app/src/main/keepRules/rules.keep`. Com a regra, o release abre, mostra entrada, Ajuda, Configurações (trocando o tema) e Criar conta, aceita os extras de push/rotas sem cair e trata a falha de conexão ("Não foi possível conectar...") sem exceção no logcat (sem `ClassNotFound`, `NoSuchMethod` nem erro de serialização). **Não conferido:** o release falando com a API real e com login (a chamada autenticada), Firebase/push com credenciais reais e o login com o Google. Antes de publicar, repita a conferência com a API no ar.

## Limites desta forma de entrega

- **APK de depuração:** é o caminho mais simples para um protótipo. Instala em qualquer Android e só fala com HTTPS.
  - **Para publicar na Play Store** use o APK de release assinado com a chave própria (seção 6). A assinatura está configurada e o release foi aberto e navegado no emulador; falta conferi-lo com a API real, e a Play Store pede ainda um AAB (`bundleRelease`), não gerado aqui.
- **Executáveis sem assinatura digital:** o aviso do SmartScreen aparece até alguém comprar um certificado de assinatura de código. Isso não muda o funcionamento.
- **iPhone:** não é atendido. O app do paciente existe só para Android.
