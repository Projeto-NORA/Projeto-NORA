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

Os desktops (`OBS-NORA.zip` e `Painel-Municipal.zip`) vão **no mesmo release** (decisão do usuário em 24/11/2026): você baixa nos computadores da escola, sem pen drive. Como o repositório é público, qualquer pessoa conseguiria baixar os dois programas. Eles não têm senha nem segredo dentro (só o endereço da API, que já é público), e sem conta ninguém entra; mesmo assim, **apague esses dois arquivos do release depois da simulação** (ver "Depois da simulação").

Links diretos, que sempre pegam a versão mais nova do release:

```text
https://github.com/Projeto-NORA/NORA-downloads/releases/latest/download/OBS-NORA.zip
https://github.com/Projeto-NORA/NORA-downloads/releases/latest/download/Painel-Municipal.zip
```

Publicados em 24/11/2026 no release `v1.0-escola` (cada zip com cerca de 59 MB). Conferido: o download pelo link, sem login, tem o mesmo SHA-256 do arquivo gerado.

## O que precisa estar pronto antes

1. **API no ar (P-049).** Após a integração da correção da URL do Neon em 24/11/2026, o [endpoint de prontidão](https://nora-api-kw8x.onrender.com/health/ready) respondeu `ready`. Confira os logins e a UBS Escola pelo roteiro de [hospedagem.md](hospedagem.md) antes da apresentação.
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

Se for gerar só o APK direto pelo Gradle, passe o endereço da API: `.\gradlew.bat :app:assembleDebug "-PNORA_API_URL=https://nora-api-kw8x.onrender.com"` (dentro de `APPNORA`). Sem essa propriedade o build de depuração não aponta para o Render. O script `empacotar-clientes.ps1` já passa o endereço sozinho.

O resultado fica em `dist\<data-hora>\`: `OBS-NORA.zip`, `Painel-Municipal.zip` e `NORA-paciente.apk`. Use o APK de depuração (o padrão): é o que já foi conferido com HTTPS. Gere sempre no mesmo PC, porque um APK gerado em outro computador não instala por cima (ver [distribuicao.md](distribuicao.md), seção 5).

### 2. Publicar o APK no GitHub (uma vez; depois só um release novo)

**Já feito em 24/11/2026:** o repositório público `Projeto-NORA/NORA-downloads` existe e o release `v1.0-escola` tem o `NORA-paciente.apk` (build de depuração apontando para `https://nora-api-kw8x.onrender.com`). O link `latest` acima baixa sem login. Os comandos abaixo servem para publicar uma **versão nova**: pule o `gh repo create` e troque o nome do release.

```powershell
gh repo create Projeto-NORA/NORA-downloads --public --add-readme --description "Instaladores do NORA"
$pasta = (Get-ChildItem dist -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
gh release create v1.0-escola "$pasta\NORA-paciente.apk" --repo Projeto-NORA/NORA-downloads --title "NORA - simulação na escola" --notes "App do paciente para Android 7 ou mais novo."
```

O `--add-readme` existe porque o GitHub não cria release num repositório vazio. Para uma versão nova, gere de novo e rode só o `gh release create` com outro nome (`v1.1-escola`); o link `latest` passa a apontar para ela.

**QR Codes:** a imagem com os três (App do Paciente, OBS-NORA e Painel Municipal, lado a lado, cada um com o nome em cima) está em `Mateus-Projects/Claude-PLANS/Projeto-NORA/qrcodes-instaladores-nora.png`, gerada em 24/11/2026. Cada QR foi lido de volta por um leitor automático e abre o link `latest/download/<arquivo>` correspondente; os três downloads responderam HTTP 200 sem login. Como os links são `latest`, a imagem continua valendo quando houver release novo. O roteiro do zero, para quem não programa, está em `Claude-PLANS/Projeto-NORA/passo-a-passo-final-simulacao.md`.

### 3. Preparar a rede no Painel Municipal (você, uma vez)

Entre com a conta `municipal` (criada pelo `--provision` do `implantar-neon.ps1`):

1. Cadastre a UBS da simulação, por exemplo "UBS Escola", com endereço e coordenadas da escola.
2. Se for preparar a UBS pela tela, confira as especialidades **Clínica Geral**, **Pediatria** e **Odontologia**. O script da próxima etapa já cuida do catálogo da rede.
3. Dê à conta OBS acesso à "UBS Escola". Se cada aluno for ter sua própria conta, crie as contas OBS aqui e dê o acesso também.

### 4. Preparar a UBS no OBS (você, como administrador)

1. Em cada computador da escola, baixe o `OBS-NORA.zip` pelo link acima, clique com o botão direito → **Extrair tudo** e abra o `OBS-NORA.exe` (no aviso do Windows: **Mais informações → Executar assim mesmo**). O Painel Municipal é opcional: só para mostrar na tela.
2. Entre com a conta de administrador. Em **Informações da UBS → Computadores**, ative este computador e gere um código para o outro.
3. Para montar o catálogo completo, abra o PowerShell 7 na raiz do repositório com a `main` atualizada. Rode o comando com a API pronta e a conta de administrador já vinculada à UBS.

   ```powershell
   pwsh -File scripts\cadastrar-medicos-simulacao.ps1 -Ubs 'UBS Escola'
   ```

   O script pede os e-mails e as senhas da Secretaria e do administrador do OBS. Ele prepara **20 especialidades**, **40 profissionais fictícios** (dois por especialidade, sem número de conselho) e **20 consultórios** (um por especialidade). A grade padrão tem faixas de segunda a sexta, das 08h às 12h para o primeiro profissional e das 13h às 17h para o segundo, com consultas de 20 minutos.

   Se o navegador mostrar `ready` e o script informar um desafio HTTP 403, confira se está usando a versão atualizada do script. Ela identifica o cliente NORA nas chamadas e mostra o `CF-Ray` quando o Cloudflare do Render intercepta uma requisição. Esse identificador permite investigar o bloqueio no suporte do Render.

   Se precisar ajustar toda a grade pela tela, acrescente `-SemGrade` ao comando. Se interromper a execução com a grade padrão, repita o mesmo comando para completar os dias ausentes. Grades personalizadas são preservadas.

4. Em **Informações da UBS**, preencha ou confira:
   - **Atendimento ao público:** horário do dia da simulação.
   - **Profissionais e consultórios:** confira o catálogo criado pelo script. Se fizer pela tela, cadastre pelo menos um profissional fictício e um consultório por especialidade usada no roteiro.
   - **Grade:** deixe horários livres que comecem **pelo menos 3 horas depois do fim da apresentação**. Ajuste as faixas no OBS se a grade padrão não cobrir esse período. Assim o botão **Confirmar presença** aparece durante o exercício, pois fica disponível de 48 h a 2 h antes da consulta. A Vaga Relâmpago fecha quando faltam menos de 10 min.
   - **Aprovação das vagas reaproveitadas:** ligada, para o aluno mostrar o "Aprovar e ofertar".
5. Faça um ensaio completo com o seu celular (cadastro, criar conta, agendar, cancelar, Vaga Relâmpago) e depois desative esse paciente de teste.

### 5. Instalar o app no celular do funcionário

Mande o link (ou o QR code) **na véspera**, para ninguém perder tempo na apresentação:

1. Abra o link no celular. O download do `NORA-paciente.apk` começa sozinho.
2. Toque no arquivo baixado. O Android pede para **permitir instalar apps desconhecidos** para o navegador: **Configurações → permitir**, e volte.
3. Se o Play Protect avisar, toque em **Mais detalhes → Instalar mesmo assim**.
4. Abra o NORA e aceite as **notificações** (é por elas que a Vaga Relâmpago avisa).
5. **Não crie a conta ainda:** a conta só pode ser criada depois que a recepção (OBS) cadastrar a pessoa, durante a simulação.

Na simulação, a recepção registra o nome, o CPF fictício válido e o e-mail do paciente no OBS. Depois, o paciente toca em **Criar conta** no app, informa o mesmo CPF, escolhe a UBS, cria uma senha e confirma o código de seis dígitos enviado ao e-mail cadastrado pela recepção. O **CPF** vincula a conta ao cadastro da UBS. O e-mail recebe o código de confirmação.

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
4. Tire os desktops do release público, para ninguém mais baixar os programas:

   ```powershell
   gh release delete-asset v1.0-escola OBS-NORA.zip --repo Projeto-NORA/NORA-downloads --yes
   gh release delete-asset v1.0-escola Painel-Municipal.zip --repo Projeto-NORA/NORA-downloads --yes
   ```

## Dados criados em casa, uso na escola

Os dados não ficam no seu computador, e sim no banco Neon. O que você criar em casa aparece na escola sem copiar nada:

- **Pelo PowerShell (em casa):** a conta da Secretaria (`municipal`) e a do administrador do OBS, com `--provision` (o `scripts\implantar-neon.ps1` já oferece).
- **Pelas telas (em casa):** a UBS e as contas extras no Painel Municipal, além dos horários de atendimento e da aprovação das vagas no OBS. Use o script do passo 4 para preparar especialidades, profissionais, consultórios e grade, ou cadastre esses itens pelas telas.
- **Entre uma vez em casa com cada conta.** Se o sistema pedir a troca da senha provisória, troque lá, para não perder tempo diante dos funcionários.
- **Na escola, use a conta de administrador do OBS:** ela entra em qualquer computador. A atendente só entra em computador ativado, e a ativação é por máquina (Informações da UBS → Computadores, cerca de 1 minuto cada).
- **Os pacientes continuam sendo cadastrados na hora**, porque o app só cria a conta depois do cadastro pela recepção.
- **Sem a API no ar (P-049) o login não funciona, em casa ou na escola.**
