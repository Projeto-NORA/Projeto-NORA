# Hospedagem do NORA para o piloto (escola simulando a rede de saúde)

Este documento responde onde hospedar a API e o banco do NORA para um piloto com dados de uma escola, por um período curto, e explica o passo a passo. Os três aplicativos (OBS, Painel Municipal e o app Android) **não** são hospedados: eles rodam nos computadores e celulares e só precisam do endereço HTTPS da API (ver [distribuicao.md](distribuicao.md)).

Os limites citados foram conferidos em 2026-11-17 nas páginas oficiais: [Render Free](https://render.com/docs/free), [planos do Neon](https://neon.com/docs/introduction/plans) e [bancos no Neon](https://neon.com/docs/manage/databases). Para a Hostinger KVM 1 não há página oficial com os números completos; os valores abaixo vêm de listagens de terceiros e precisam ser conferidos no painel da Hostinger. Esses limites mudam, então vale reconferir no dia.

## O que precisa ficar no ar

| Peça | O que é | Observação |
| --- | --- | --- |
| `Nora.Api` | servidor .NET 10 (ASP.NET) | Tem três rotinas em segundo plano: a Vaga Relâmpago a cada 15 s, a limpeza a cada 6 h e o push a cada 30 s (o push só roda com o Firebase configurado). |
| PostgreSQL | um banco **central** e **um banco por UBS** | A API cria o banco da UBS sozinha com `CREATE DATABASE` quando a Secretaria cadastra a UBS. O usuário do banco precisa da permissão `CREATEDB`. |
| SMTP | envio de e-mail | O cadastro do paciente no app manda um código por e-mail. Sem SMTP, o app não cria conta. |
| HTTPS | certificado | O app Android em Release só aceita HTTPS. Os desktops aceitam HTTPS ou `http://localhost`. |

O volume de dados de uma escola é pequeno: alguns milhares de pessoas ocupam dezenas de megabytes. Espaço não é problema em nenhuma das opções.

## Comparação

| | Render (API) + Neon (banco) | Hostinger KVM 1 (tudo na VPS) |
| --- | --- | --- |
| Custo | gratuito | já pago (dividido com o amigo) |
| Recursos | Render: instância gratuita com 512 MB. Neon: 1 GB por projeto e 100 CU-hora por mês | 1 vCPU, 4 GB de RAM e 50 GB NVMe, divididos com o que o amigo já roda |
| HTTPS | automático (`https://<nome>.onrender.com`) | você configura um domínio ou `sslip.io` e um proxy com certificado (Caddy) |
| API dormindo | **sim**: depois de 15 min sem acesso, a próxima requisição espera cerca de 1 min | não: fica sempre ligada |
| Rotinas em segundo plano | param enquanto a API dorme e voltam ao acordar | rodam sempre |
| Banco dormindo | o Neon suspende depois de 5 min sem uso; acordar leva poucos segundos | não dorme |
| Manutenção | quase nenhuma | você cuida de atualização, firewall, backup e de não atrapalhar o serviço do amigo |
| Risco ao amigo | nenhum | dividir CPU e RAM; portas 80/443 talvez já usadas por ele |
| Prazo | Neon não expira. Render gratuito dá 750 h por mês, o bastante para um serviço ligado o mês inteiro | enquanto o plano estiver pago |

### Render + Neon tem algum problema grande?

Para um protótipo, não. Existem quatro cuidados, e nenhum deles impede o uso.

1. **Primeira requisição lenta.** Depois de 15 min parada, a API leva cerca de 1 min para acordar e o Neon mais alguns segundos.
   - **Antes de apresentar:** abra `https://<sua-api>.onrender.com/health/ready` uns 3 min antes e espere a resposta `ready`.
   - **Durante o piloto:** as pessoas vão ver essa espera de vez em quando.
2. **Vaga Relâmpago "pausada" enquanto a API dorme.** A oferta de 10 min só vence e passa para o próximo da fila quando a API está acordada.
   - Numa apresentação não faz diferença, porque ela fica acordada.
   - Num piloto de dias, uma oferta pode ficar parada até alguém abrir um aplicativo.
3. **Não use o PostgreSQL gratuito do Render.** Ele expira em 30 dias, não tem backup e permite um só banco. O NORA cria um banco por UBS. Por isso o banco fica no Neon.
4. **Cota de computação do Neon (100 CU-hora por mês).** Enquanto a API está acordada, ela consulta o banco a cada 15 s e o Neon não suspende.
   - Com a API dormindo quando ninguém usa, a cota sobra.
   - Não ligue um "despertador" que acesse a API 24 h por dia: isso gastaria a cota do Neon em cerca de duas semanas. Se quiser deixá-la acordada, faça isso só no dia da apresentação.

Dois cuidados técnicos que o passo a passo já resolve:

- **Usar a conexão direta do Neon**, não a que tem `-pooler` no nome. A criação do banco da UBS usa uma trava de sessão, que não funciona atrás do pooler.
- **Pôr o banco e a API na mesma região.** O Render não tem servidor no Brasil. Com a API em Virginia ou Ohio e o banco em São Paulo, cada tela faria várias idas e voltas entre países. Por isso o Neon fica em **US East**, ao lado do Render.

### E a Hostinger?

A KVM 1 aguenta o NORA com folga. PostgreSQL e API juntos usam bem menos de 1 GB de RAM, e os dados da escola cabem em poucos megabytes.

- **A favor:** a API não dorme, as rotinas rodam sempre e não há espera de 1 min.
- **Contra:**
  - você assume a manutenção (atualizações, firewall, backup);
  - precisa de HTTPS com domínio;
  - divide CPU e memória com o serviço do amigo. Se ele já usa as portas 80/443, o NORA precisa entrar atrás do proxy que ele já tem.

### Recomendação

- **Para o piloto: Render + Neon**, como você prefere. Não há problema grande para um protótipo; os cuidados acima resolvem.
- **Hostinger como plano B**, só se a espera de 1 min incomodar no piloto ou se a Vaga Relâmpago precisar andar com todo mundo fora do app. O passo a passo está no fim.
- **Para apresentações:** rode tudo no notebook com `scripts/demo-local.ps1` (banco, API, OBS, Painel e o app no emulador Android em `http://10.0.2.2`), sem depender de internet nem de deploy. O app num celular de verdade só entra com a API hospedada, porque fora do emulador ele exige HTTPS.

## Antes de pôr dados reais da escola (LGPD)

Usar dados reais de pessoas, ainda mais de alunos menores de idade, é tratamento de dados pessoais pela LGPD. Mesmo num teste, combine com a escola:

- **Consentimento** de quem participa, e dos responsáveis no caso de menores, explicando que é uma simulação, por quanto tempo os dados ficam guardados e quando serão apagados.
- **Só o necessário:** nome, CPF e e-mail bastam para a demonstração. Não lance exames, vacinas ou qualquer dado de saúde real; na Jornada, use registros inventados.
- **CPF:** o NORA confere os dígitos do CPF. Se preferir não usar o CPF real, use CPFs fictícios válidos com os nomes reais, combinados com cada participante.
- **E-mail:** precisa ser real, porque o código do cadastro chega por ele.
- **Onde ficam os dados:** com Render e Neon em US East, os dados ficam nos Estados Unidos. Diga isso aos participantes. Na Hostinger, depende da região da VPS.
- **Fim do piloto:** faça o backup (ver "Encerrar o piloto"), guarde-o em local protegido pelo tempo combinado e apague os serviços.

## Passo a passo: Render + Neon

### 1. Banco no Neon

1. Crie a conta em [neon.com](https://neon.com) e um projeto chamado `nora-piloto`:
   - **PostgreSQL 18**;
   - região **AWS US East (N. Virginia)**, a mesma que você vai escolher no Render.
2. Em **Connect**, desligue **Connection pooling**. Copie a conexão direta: o host **não** pode ter `-pooler`.
3. Copie a URL direta exibida pelo Neon, que começa com `postgresql://`, e cole o valor completo na variável `ConnectionStrings__Nora` do Render. A API converte a URL para o formato do Npgsql. Para o domínio do Neon, a opção padrão `sslmode=require` também valida o certificado e o nome do servidor. Os parâmetros `sslmode` e `channel_binding` são reconhecidos.

   Se uma credencial tiver aparecido em um log antigo, a equipe responsável deve substituí-la no Neon e atualizar o valor no Render antes do novo deploy. A API informa configuração inválida sem mostrar seu valor no diagnóstico.

4. O usuário padrão do projeto (`neondb_owner`) já pode criar bancos com `CREATE DATABASE`. É isso que a API faz ao cadastrar uma UBS. O Neon permite até 500 bancos por branch.

### 2. Preparar o banco a partir do seu computador

No SQL Editor do Neon, selecione a mesma branch e o mesmo banco da conexão usada no Render. A [consulta de conferência](../scripts/conferir-neon-nora.sql) mostra se o banco escolhido tem a tabela de contas e quantas contas ativas da Secretaria e de administrador do OBS existem. Também mostra a quantidade de UBS e especialidades ativas. A consulta é somente leitura e funciona antes da primeira migração.

**Caminho curto (recomendado):** `pwsh -File scripts\implantar-neon.ps1`, na raiz do repositório, com o PowerShell 7. O script pede a URI do Neon (entrada oculta, sem gravar em arquivo), converte para o formato do Npgsql, conta as linhas de todas as tabelas, compila a API, aplica `--migrate` e `--migrate-all-units`, recusa o resultado se alguma tabela perder linhas, oferece o `--provision` do município e do OBS e deixa a conexão pronta na área de transferência para colar no Render. O texto abaixo é o mesmo roteiro feito à mão.

- **Só a conexão para o Render:** `pwsh -File scripts\implantar-neon.ps1 -SoConexao` converte a URI e copia a conexão do Npgsql para a área de transferência, sem tocar no banco. Serve quando o banco já foi migrado e falta só colar em `ConnectionStrings__Nora`. Com `-PularCadastro`, o script migra e confere, mas não oferece o `--provision`.
- **Certificado do Neon no psql:** o psql portátil do Windows não lê a loja de certificados do sistema, e `sslrootcert=system` falha com `certificate verify failed`. Por isso o script exporta as raízes confiáveis do Windows para um arquivo temporário, usa esse arquivo em `PGSSLROOTCERT` (com `verify-full`) e o apaga no fim. A API não precisa disso: o .NET já usa a loja do Windows.

A migração e as contas iniciais rodam do seu PC contra o Neon. O `--provision` pede dados no terminal e por isso não roda no Render. Na raiz do repositório, numa janela do PowerShell. Para o escopo `municipal`, informe o nome do município e depois nome completo, e-mail e senha do funcionário. Para `obs`, informe nome, e-mail e senha. A migração 023 deve ser aplicada antes do provisionamento. **A equipe (Secretaria e OBS) não tem CPF e entra só por e-mail**; as contas que já existiam perdem o CPF na migração 022:

```powershell
$env:ConnectionStrings__Nora = "Host=<host>;Port=5432;Database=neondb;Username=<usuario>;Password=<senha>;SSL Mode=VerifyFull;Channel Binding=Require"
dotnet run --project Nora.Api -c Release -- --migrate            # aplica as migrações do banco central até a 027
dotnet run --project Nora.Api -c Release -- --provision          # escopo "municipal": conta da Secretaria
dotnet run --project Nora.Api -c Release -- --provision          # escopo "obs": administrador da primeira UBS
```

A variável vale só naquela janela. Feche-a no fim. Depois que a Secretaria cadastrar as UBS no Painel Municipal, a API cria os bancos delas sozinha. Ao atualizar a API mais tarde, rode `--migrate` e em seguida `--migrate-all-units`, nessa ordem. Quem perder a senha de uma conta da Secretaria ou do OBS pede a redefinição à TI, que roda `dotnet run --project Nora.Api -c Release -- --reset-password` com a mesma variável (ver [secretaria.md](secretaria.md)).

O comando `--provision` grava a conta no banco configurado no momento da execução. Se o PowerShell estava conectado ao Neon e mostrou **Conta criada**, a conta já está naquele banco. Se a conexão apontava para um banco local, é preciso criar a conta no Neon com o script acima. Para usar o mesmo acesso em outros computadores, aponte o Painel Municipal e o OBS para a mesma API publicada no Render. No Painel Municipal, conceda à conta OBS acesso à UBS antes de operar a unidade.

### 3. Chaves e e-mail

Gere três chaves aleatórias. Cada comando abaixo imprime uma chave nova; guarde-as num gerenciador de senhas:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

**E-mail no Render: use o Brevo na porta 2525.** O Render Free **bloqueia a saída pelas portas 25, 465 e 587** ([fonte](https://render.com/docs/free)), justamente as do SMTP, então o Gmail na 587 não funciona lá e o cadastro do paciente pelo app ficaria sem código. Crie uma conta gratuita no [Brevo](https://www.brevo.com/free-smtp-server) (300 e-mails por dia), valide o remetente e gere uma **chave SMTP** (não é a senha da conta). Use host `smtp-relay.brevo.com`, porta `2525`, usuário = o login SMTP que o Brevo mostra, senha = a chave SMTP. A API usa STARTTLS, que funciona na 2525. Na demonstração local (`scripts/demo-local.ps1`) e na Hostinger o Gmail com senha de app na 587 continua valendo.

### 4. API no Render

1. **Publique o código no GitHub.** O repositório já é privado; o Render lê repositório privado depois que você autoriza o GitHub.
2. **Crie o serviço:** em [render.com](https://render.com), **New → Web Service**, escolha o repositório e configure:
   - **Language:** Docker. O `Dockerfile` da raiz já compila e roda a API.
   - **Branch:** `main`.
   - **Region:** Virginia (US East), a mesma do Neon.
   - **Instance type:** Free.
   - **Health Check Path:** `/health/live`.
3. **Variáveis de ambiente** (aba **Environment**). Todas são segredo ou configuração e nenhuma vai para o Git:

   | Variável | Valor |
   | --- | --- |
   | `ConnectionStrings__Nora` | a conexão do passo 1 |
   | `Nora__Registration__HashKey` | chave 1 |
   | `Nora__PasswordReset__HashKey` | chave 2 |
   | `Nora__EmailChange__HashKey` | chave 3 |
   | `Nora__Smtp__Host` | `smtp-relay.brevo.com` |
   | `Nora__Smtp__Port` | `2525` (o Render Free bloqueia a 587) |
   | `Nora__Smtp__Username` | o login SMTP do Brevo |
   | `Nora__Smtp__Password` | a chave SMTP do Brevo |
   | `Nora__Smtp__FromAddress` | o remetente validado no Brevo |
   | `Nora__ForwardedHeaders__KnownNetworks` | `10.0.0.0/8` |
   | `ALLOWED_HOSTS` | opcional no Render (a imagem usa o `RENDER_EXTERNAL_HOSTNAME`, que o Render define sozinho); sobrescreve com o domínio público, vários separados por `;` |

   **Sobre o endereço e o host:** o `appsettings.json` que vai na imagem manda escutar só em `127.0.0.1:5080` e aceitar só `localhost`. O `Dockerfile` já troca isso na subida: escuta em `0.0.0.0` na porta que o Render informa em `PORT` e aceita apenas o domínio do serviço (`<nome>.onrender.com`). Não ponha `AllowedHosts=*` para "resolver": isso desliga a conferência do host. Se o deploy subir mas o `/health/live` do Render falhar com 400, ligue o log do filtro de host só na primeira subida, com a variável `Logging__LogLevel__Microsoft.AspNetCore.HostFiltering=Information` (a imagem usa `Warning` para o ASP.NET e a mensagem com o host recusado pode ficar abaixo desse nível), leia o host recebido nos logs e acrescente-o em `ALLOWED_HOSTS`, separado por `;`. Depois volte a variável ao normal. Essa variável tem ponto no nome, e só chega à API porque o `ENTRYPOINT` do `Dockerfile` usa o `bash` (o `/bin/sh` da imagem é o `dash`, que descarta nomes de variável com ponto); não troque o shell do `ENTRYPOINT` de volta para `sh`. Se mesmo assim a mensagem não aparecer nos logs, ou se o Render recusar esse nome de variável, o caminho que não depende dela é comparar `curl -i https://<nome>.onrender.com/health/live` com o domínio exato do serviço e pôr esse domínio em `ALLOWED_HOSTS`.

   **Sobre `KnownNetworks`:** o Render entrega as requisições por um proxy interno. Sem essa linha, todos os usuários parecem vir do mesmo IP e dividem o limite de tentativas de login. Os logs **não** mostram o IP de origem, então confira por comportamento: duas pessoas em redes diferentes (por exemplo, um computador e um celular no 4G) erram a senha da mesma conta até o limite de tentativas; a segunda, de outro IP, **não** pode herdar o bloqueio da primeira. Se herdar, o proxy está fora da faixa: troque `KnownNetworks` pela faixa do proxy do Render (consulte a documentação do Render) e repita o teste.

4. **Deploy.** Ao terminar, abra `https://<nome>.onrender.com/health/ready`. A resposta `ready` confirma a API e o banco.

#### Alternativa: pelo Blueprint (`render.yaml`)

O `render.yaml` da raiz substitui os itens 2 (criar o serviço) e 3 (variáveis) do passo 4 e a geração das chaves da seção 3. As seções 1 (Neon) e 2 (`--migrate` e `--provision` no seu computador) continuam obrigatórias, e a conferência de `/health/ready` do item 4 também: sem a migração e a conta inicial a API sobe, mas `/health/ready` falha e ninguém consegue entrar. Em **New → Blueprint**, escolha o repositório. O Render cria o serviço (Docker, plano Free, Virginia, `main`, health check `/health/live`) e **gera sozinho** as três chaves (`Nora__Registration__HashKey`, `Nora__PasswordReset__HashKey`, `Nora__EmailChange__HashKey`), então o comando do PowerShell da seção 3 **não vale** nesse caminho. Já vêm preenchidos o host SMTP do Brevo, a porta `2525` e `KnownNetworks`. A tela pede só quatro valores: a conexão do Neon (`ConnectionStrings__Nora`), o usuário, a senha e o remetente do SMTP (`Nora__Smtp__Username`, `Nora__Smtp__Password` e `Nora__Smtp__FromAddress`). `ALLOWED_HOSTS` não aparece na tela; se for preciso (health check com 400 por host), acrescente-o depois em **Environment** do serviço. O que vem depois (deploy e `/health/ready`) é igual. O primeiro deploy pelo Blueprint também precisa das conferências de P-049.

### 5. Ligar os aplicativos

O endereço que vai nos três aplicativos é `https://<nome>.onrender.com`. Como gerar e entregar os executáveis está em [distribuicao.md](distribuicao.md).

### 6. Ordem do primeiro uso

1. **Secretaria (Painel Municipal):** entra com a conta `municipal`, cadastra as UBS (a API cria o banco de cada uma), as especialidades e as contas OBS das atendentes, e dá a cada conta acesso às UBS.
2. **Administrador da UBS (OBS):** entra em qualquer computador. Em Configurações da UBS → Computadores, ativa o computador da recepção ou gera um código para os outros. Também preenche horário de funcionamento, serviços, profissionais, consultórios e a grade.
3. **Atendente (OBS):** entra num computador ativado e cadastra os pacientes, com CPF e um e-mail real.
4. **Paciente (app):** escolhe "Criar conta", informa nome, CPF e senha, escolhe a UBS e confirma o código que chegou no e-mail.

## Plano B: tudo na Hostinger KVM 1

Use este caminho se a espera do Render atrapalhar. Antes, combine com seu amigo:

- o que já roda na VPS e se as portas 80/443 já estão em uso;
- se dá para usar Docker.

1. **Endereço com HTTPS:** com um domínio, crie um registro `A` apontando para o IP da VPS. Sem domínio, use `nora.<IP-com-hífens>.sslip.io`; o `sslip.io` responde com o próprio IP e o Caddy consegue o certificado.
2. **Arquivo `/opt/nora/docker-compose.yml`:** três serviços, `postgres:18` com volume próprio, a API construída pelo `Dockerfile` do repositório e o `caddy:2` na frente:
   - o PostgreSQL **não** publica porta para a internet: se for rodar `--migrate` e `--provision` do seu PC por túnel SSH (passo 4), publique-a só no loopback do VPS (`ports: ["127.0.0.1:5432:5432"]`), que não a expõe;
   - a API recebe as mesmas variáveis da tabela do Render, com `ConnectionStrings__Nora` apontando para o serviço `postgres`, mas com duas diferenças: `ALLOWED_HOSTS` é o domínio público (aqui não existe o `RENDER_EXTERNAL_HOSTNAME`) e **`Nora__ForwardedHeaders__KnownNetworks` não é `10.0.0.0/8`**, e sim a sub-rede da rede do compose, explicada abaixo;
   - declare uma rede própria com sub-rede fixa (`networks: nora: ipam: config: - subnet: 172.30.0.0/24`) e ligue API, Caddy e `postgres` a ela. O Caddy (e um Nginx do host que encaminhe para a porta publicada, que chega pelo gateway da rede) alcança a API por esse endereço, fora da faixa `10.0.0.0/8`; use `172.30.0.0/24` em `KnownNetworks`. Com a faixa errada a API não confia no `X-Forwarded-For`, vê todo mundo com o IP do Caddy e uma só pessoa errando a senha bloqueia o login de todos (limite de 30 por minuto) e a ativação de computadores do OBS (10 por 10 minutos). Depois do primeiro acesso, confira nos logs ou testando que um segundo usuário, de outro IP, não herda o bloqueio do primeiro;
   - o Caddy faz `reverse_proxy api:8080`.
3. **Se o amigo já tiver um Nginx ou Traefik nas portas 80/443,** não suba o Caddy: crie no proxy dele um site novo para o domínio do NORA, apontando para a porta da API, que deve ser publicada só em `127.0.0.1` (`ports: ["127.0.0.1:8080:8080"]`). O site precisa repassar o `Host` original e o IP do cliente, senão a API responde 400 a tudo ou enxerga todos com o mesmo IP. No Nginx: `proxy_set_header Host $host; proxy_set_header X-Forwarded-For $remote_addr; proxy_set_header X-Forwarded-Proto $scheme;` (no Traefik isso já é o padrão). Sem o `Host`, o filtro `ALLOWED_HOSTS` devolve 400 inclusive em `/health/live`; sem o `X-Forwarded-For`, todos dividem o limite de logins e de ativações do OBS. O IP de origem do proxy (aqui, o gateway da rede do compose) tem de cair em `Nora__ForwardedHeaders__KnownNetworks`.
4. **Migração e contas:** rode `--migrate` e `--provision` como no passo 2 do Render, de dentro do contêiner da API (`docker compose exec api dotnet Nora.Api.dll --migrate`, e depois `--provision`) ou do seu PC por um túnel SSH (`ssh -L 5433:localhost:5432`), que só funciona com o `127.0.0.1:5432:5432` do passo 2. Não abra a porta do banco para a internet.
5. **Proteção da VPS:** firewall só com 22, 80 e 443, e usuário do banco com senha forte.

## Encerrar o piloto (backup e desligamento)

1. **Backup.** Com o cliente do PostgreSQL 18 instalado no PC, rode um `pg_dump` por banco: o central e um `nora_ubs_<id>` por UBS. A lista dos bancos sai de `SELECT datname FROM pg_database`.

   ```powershell
   & "C:\Users\Pichau\AppData\Local\Programs\PostgreSQL\18\bin\pg_dump.exe" "host=<host> dbname=neondb user=<usuario> sslmode=verify-full sslrootcert=system" -Fc -f nora-central.dump
   ```

   Repita trocando `dbname` e o nome do arquivo para cada banco de UBS. Guarde os arquivos `.dump` criptografados (por exemplo, num `.zip` com senha forte ou num cofre). A guarda e o descarte seguem o que foi combinado com a escola.
2. **Desligar.** Apague o serviço no Render e o projeto no Neon. Na Hostinger, `docker compose down -v` apaga também o volume do banco. A partir daí os aplicativos não conectam mais, porque o endereço deixa de existir.
3. **Restaurar no futuro**, se precisar: `pg_restore` em um PostgreSQL 18 novo, banco por banco, e a API apontando para ele.

## Fontes

- [Render — Free instances](https://render.com/docs/free)
- [Neon — Plans](https://neon.com/docs/introduction/plans)
- [Neon — Manage databases](https://neon.com/docs/manage/databases)
- [Hostinger KVM 1 (dados de terceiros)](https://www.vpsbenchmarks.com/hosters/hostinger/plans/kvm-1)
