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

O script mostra cada passo enquanto trabalha, `[1/4] Publicando OBS-NORA...`, e marca `OK` ao concluir.

- **Onde fica o resultado:** `dist\<data-hora>\`, fora do Git.
- **Desktop:** cada `.exe` é um arquivo único com o .NET embutido, então quem recebe não instala nada. Cada `.zip` tem por volta de 70 a 150 MB.
- **Sem Android na máquina:** use `-SemAndroid` para gerar só os desktops.

## 2. Enviar

O melhor caminho é uma pasta do Google Drive ou do OneDrive com o link compartilhado só com os participantes. E-mail costuma bloquear `.exe` e `.apk`. O WhatsApp aceita, mas o arquivo grande demora.

Junto com o link, mande três informações: o endereço da API (`https://<sua-api>.onrender.com`), o usuário e a senha provisória de cada pessoa da equipe. Para pacientes, mande só o endereço. A senha nunca vai no mesmo grupo que o link.

## 3. Abrir no Windows (OBS e Painel Municipal)

1. **Abrir:** clique com o botão direito no `.zip` → **Extrair tudo** e abra o `.exe` da pasta extraída.
2. **Aviso do Windows:** como o executável não tem assinatura digital paga, aparece "O Windows protegeu o computador". Clique em **Mais informações** → **Executar assim mesmo**. Isso é esperado num protótipo.
3. **Primeira entrada:** em **Endereço da API**, digite `https://<sua-api>.onrender.com`, depois o CPF ou e-mail e a senha provisória. O sistema pede para trocar a senha no primeiro acesso.
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

## Limites desta forma de entrega

- **APK de depuração:** é o caminho mais simples para um protótipo. Instala em qualquer Android e só fala com HTTPS.
  - **Para publicar na Play Store** é preciso um APK/AAB de release, assinado com uma chave própria guardada em segredo. Essa assinatura ainda não está configurada no projeto, e o build de release (com R8 ligado) não foi testado num aparelho. Fica registrado em `PENDENCIAS.md`.
- **Executáveis sem assinatura digital:** o aviso do SmartScreen aparece até alguém comprar um certificado de assinatura de código. Isso não muda o funcionamento.
- **iPhone:** não é atendido. O app do paciente existe só para Android.
