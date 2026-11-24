# Simulação do NORA na escola

Como preparar, instalar e conduzir a simulação do "hospital" NORA na escola: os **funcionários** são os pacientes (usam o app no celular), os **alunos** operam o OBS nos computadores da escola e o Painel Municipal só é usado na preparação. O cronograma da simulação de 1 hora, com o papel de cada um dos 10 pacientes, está em Word: [cronograma-simulacao-escola.docx](../../Mateus-Projects/Claude-PLANS/Projeto-NORA/cronograma-simulacao-escola.docx) (fora do repositório, na pasta de planos).

## Entrega pelo GitHub Releases: funciona?

Funciona, com um ajuste. O repositório `Projeto-NORA/Projeto-NORA` é **privado**, e o release de um repositório privado só abre para quem tem conta no GitHub com acesso à organização. Um funcionário que abrir o link veria "404".

**Caminho recomendado:** um repositório **público só de instaladores**, `Projeto-NORA/NORA-downloads`, sem nenhum código, com o `NORA-paciente.apk` anexado ao release. O código continua privado e o funcionário baixa sem conta nem senha. O endereço abaixo sempre entrega o APK do release mais novo, então um único QR code serve para sempre:

```text
https://github.com/Projeto-NORA/NORA-downloads/releases/latest/download/NORA-paciente.apk
```

| Opção | Vantagem | Problema |
| --- | --- | --- |
| **Repositório público só de instaladores (recomendado)** | link fixo, QR code, versões guardadas, sem login | criar o repositório uma vez |
| Release do repositório privado | já existe | o funcionário não consegue baixar |
| Tornar o repositório principal público | um lugar só | expõe todo o código |
| Google Drive / WhatsApp | conhecido | o Drive às vezes pede login e mostra prévia em vez de baixar; sem controle de versão |
| Play Store (teste interno) | instala como app normal | conta de desenvolvedor paga, AAB e revisão: exagero para 10 pessoas |

Os desktops (`OBS-NORA.zip` e `Painel-Municipal.zip`) **não** precisam ir para o público: só você instala, em 1 ou 2 computadores da escola. Leve-os num pen drive a partir da pasta `dist\`.

## O que precisa estar pronto antes

1. **API no ar (P-049).** Em 24/11/2026, `https://nora-api-kw8x.onrender.com/health/ready` não respondia. Sem a API, nada funciona. Conclua o roteiro de [hospedagem.md](hospedagem.md): `scripts\implantar-neon.ps1`, conexão nova em `ConnectionStrings__Nora` no Render e `/health/ready` respondendo `ready`.
2. **E-mail funcionando (Brevo).** O "Criar conta" do app manda um código por e-mail. Faça um cadastro de teste com o seu e-mail antes do dia.
3. **Celulares Android.** Não existe app para iPhone. Pergunte antes quem tem Android; quem tiver iPhone faz dupla ou usa um aparelho emprestado.
4. **Internet da escola.** Teste no Wi-Fi da escola se `onrender.com` e `github.com` abrem; tenha o 4G como reserva.
5. **Autorização (LGPD).** Combine com a escola e os funcionários: é uma simulação, usa **CPF fictício válido** (gerado por um gerador de CPF de teste), nome e e-mail reais, nenhum dado de saúde real, e tudo é apagado no fim (ver [hospedagem.md](hospedagem.md), "Antes de pôr dados reais").

## Passo a passo da instalação

### 1. Gerar os arquivos (no seu PC, uma vez)

Na raiz do repositório, com a branch `main` atualizada:

```powershell
$env:JAVA_HOME = 'C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot'
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
pwsh -File scripts\empacotar-clientes.ps1
```

O resultado fica em `dist\<data-hora>\`: `OBS-NORA.zip`, `Painel-Municipal.zip` e `NORA-paciente.apk`. Use o APK de depuração (o padrão): é o que já foi conferido com HTTPS. Gere sempre no mesmo PC, porque um APK gerado em outro computador não instala por cima (ver [distribuicao.md](distribuicao.md), seção 5).

### 2. Publicar o APK no GitHub (uma vez; depois só um release novo)

```powershell
gh repo create Projeto-NORA/NORA-downloads --public --add-readme --description "Instaladores do NORA"
$pasta = (Get-ChildItem dist -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
gh release create v1.0-escola "$pasta\NORA-paciente.apk" --repo Projeto-NORA/NORA-downloads --title "NORA - simulação na escola" --notes "App do paciente para Android 7 ou mais novo."
```

O `--add-readme` existe porque o GitHub não cria release num repositório vazio. Para uma versão nova, gere de novo e rode só o `gh release create` com outro nome (`v1.1-escola`); o link `latest` passa a apontar para ela.

Transforme o link do `latest` (acima) em QR code e ponha num slide ou numa folha impressa.

### 3. Preparar a rede no Painel Municipal (você, uma vez)

Entre com a conta `municipal` (criada pelo `--provision` do `implantar-neon.ps1`):

1. Cadastre a UBS da simulação, por exemplo "UBS Escola", com endereço e coordenadas da escola.
2. Confira as especialidades da rede: **Clínica Geral**, **Pediatria** e **Odontologia**.
3. Dê à conta OBS acesso à "UBS Escola". Se cada aluno for ter sua própria conta, crie as contas OBS aqui e dê o acesso também.

### 4. Preparar a UBS no OBS (você, como administrador)

1. Extraia o `OBS-NORA.zip` em cada computador da escola e abra o `OBS-NORA.exe` (no aviso do Windows: **Mais informações → Executar assim mesmo**).
2. Entre com a conta de administrador. Em **Informações da UBS → Computadores**, ative este computador e gere um código para o outro.
3. Em **Informações da UBS**:
   - **Atendimento ao público:** horário do dia da simulação.
   - **Profissionais:** um por especialidade (nomes fictícios, por exemplo "Dra. Ana – Clínica Geral").
   - **Consultórios:** "Consultório 1", "2" e "3".
   - **Grade:** no dia da simulação, de **pelo menos 3 horas depois do fim da apresentação** até o fim da tarde, com atendimentos de 20 minutos. Assim o botão "Confirmar presença" aparece (ele só existe de 48 h até 2 h antes da consulta) e há horário livre para as Vagas Relâmpago (que fecham quando faltam menos de 10 min).
   - **Aprovação das vagas reaproveitadas:** ligada, para o aluno mostrar o "Aprovar e ofertar".
4. Faça um ensaio completo com o seu celular (cadastro, criar conta, agendar, cancelar, Vaga Relâmpago) e depois desative esse paciente de teste.

### 5. Instalar o app no celular do funcionário

Mande o link (ou o QR code) **na véspera**, para ninguém perder tempo na apresentação:

1. Abra o link no celular. O download do `NORA-paciente.apk` começa sozinho.
2. Toque no arquivo baixado. O Android pede para **permitir instalar apps desconhecidos** para o navegador: **Configurações → permitir**, e volte.
3. Se o Play Protect avisar, toque em **Mais detalhes → Instalar mesmo assim**.
4. Abra o NORA e aceite as **notificações** (é por elas que a Vaga Relâmpago avisa).
5. **Não crie a conta ainda:** a conta só pode ser criada depois que a recepção (OBS) cadastrar a pessoa, durante a simulação.

### 6. No dia, antes de começar

| Quando | O quê |
| --- | --- |
| 30 min antes | Abra `https://nora-api-kw8x.onrender.com/health/ready` e espere `ready` (a API gratuita dorme e leva cerca de 1 min para acordar). |
| 25 min antes | Abra o OBS nos dois computadores e entre com as contas; projete o computador da Regulação no telão. |
| 20 min antes | Confira se todos os funcionários instalaram o app; quem não instalou usa o QR code agora. |
| 10 min antes | Distribua os cartões de papel de cada paciente (estão no cronograma). |
| Durante | Mantenha uma aba do navegador no `/health/ready`; se o app disser "Não foi possível conectar", atualize essa aba e espere. |

## Depois da simulação

1. Desative os 10 pacientes no OBS (ou apague a UBS da simulação) se o piloto não continuar.
2. Faça o backup e apague os dados no prazo combinado (ver [hospedagem.md](hospedagem.md), "Encerrar o piloto").
3. Os funcionários podem desinstalar o NORA normalmente.
