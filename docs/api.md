# API NORA

## Aplicação e persistência

Nora.Api usa ASP.NET Core .NET10 e Npgsql10.0.3 com PostgreSQL 18. NoraApiHost constrói pipeline; endpoints delegam a serviços e DAOs. DatabaseHelper abre/fecha conexões, parametriza valores e valida schema. Nenhum cliente acessa PostgreSQL diretamente.

Migrações centrais incorporadas 001_accounts.sql a 014_accounts_units_history.sql criam schema, contas, sessões, entradas, vagas, ofertas e códigos temporários de recuperação. A quarta acrescenta `accounts.is_active`, inicialmente verdadeiro, preservando cadastros anteriores. A quinta guarda apenas o hash HMAC do código e os dados mínimos para expiração, tentativas e uso único. A sexta acrescenta endereço opcional do paciente, preservando cadastros que ainda não possuem CEP. A sétima cria o cadastro de unidades e coordenadas. A oitava cria vínculos explícitos entre pacientes e unidades, sem associar cadastros anteriores automaticamente. A nona cria concessões de unidades a contas OBS e histórico de concessões e revogações. A décima registra consultas da gestão aos cadastros de uma unidade. A décima primeira guarda desafios temporários de cadastro de pacientes e a décima segunda permite auditar a consulta de agendas pela gestão. A décima terceira registra os vínculos criados entre pacientes e UBS. A décima quarta acrescenta `accounts.must_change_password` (senha provisória com troca obrigatória), `health_units.database_ready_at` (banco da UBS pronto), `patient_unit_link_events.action` (`linked`/`unlinked`) e as tabelas `staff_account_events` (criação, desativação, reativação e redefinição de senha de contas OBS) e `unit_database_events` (banco de UBS criado), com índices de tempo para o histórico. São aditivas/reaplicáveis, executadas explicitamente por --migrate com transação/lock PostgreSQL. Abrir API não altera schema implicitamente.

## Configuração privada e contas

Configure ConnectionStrings__Nora no ambiente do processo ou ConnectionStrings:Nora em user-secrets. Nora__Schema é opcional (padrão nora). Nunca versionar conexão com senha. Instância de uso: PostgreSQL 18, usuário postgres; conexão/provisionamento definitivo ainda em P-012.

Após build Release, scripts/iniciar-local.ps1 -ProvisionAccounts solicita senha PostgreSQL oculta, cria base nora se ausente, aplica migrações e pede provisionamento OBS/Municipal. Nome aprovado: Mateus Todeschini Monteiro. A equipe registra o paciente na UBS pelo OBS; o próprio paciente cria sua conta após confirmar o e-mail registrado. Não há seed pessoal. O banco armazena hash PBKDF2 ASP.NET Identity; contas duplicadas são preservadas e retornam conflito.

```powershell
dotnet build NORA.slnx --configuration Release -m:1 --disable-build-servers
.\scripts\iniciar-local.ps1 -ProvisionAccounts
# Nas próximas aberturas:
.\scripts\iniciar-local.ps1
```

Também é possível usar dotnet <Nora.Api.dll> --migrate e --provision com conexão privada configurada; provisionamento exige terminal interativo. API padrão escuta http://127.0.0.1:5080, sobrescrito por --urls/ASPNETCORE_URLS. Android debug usa http://10.0.2.2:5080 e AllowedHosts correspondente. Acesso externo exige HTTPS e configuração de hosts/deploy; CORS não é aberto.

O bootstrap lê configuração pelo diretório da DLL, independentemente da pasta atual do terminal. Assim, iniciar pelos scripts da raiz continua carregando `appsettings.json` e a porta 5080 documentada. Testes de hosts usam content root próprio e portas aleatórias explicitamente.

## Sessões e autorização

Login recebe scope (obs/municipal/patient), identifier (CPF ou e-mail) e password. CPF formatado é normalizado e tem os dígitos verificadores conferidos; e-mail vai para minúsculas e tem formato conferido. Essa validação não consulta nem comprova uma identidade perante a Receita Federal. CPF/e-mail são únicos dentro do escopo, permitindo contas separadas.

Token aleatório opaco dura8h, usa header Authorization: Bearer <token>; somente SHA256 fica no banco. Logout revoga sessão. Clientes guardam token/identidade em memória, exigindo novo login ao reabrir. Login limitado a30 tentativas por minuto/IP. O usuário devolvido no login e em `GET /api/auth/me` traz `mustChangePassword`: enquanto for verdadeiro (conta criada ou redefinida pela Secretaria com senha provisória), toda rota autenticada, exceto `GET /api/auth/me`, `POST /api/auth/logout` e `POST /api/auth/password`, responde 403 com `code` `password-change-required`. Todo executor de operação sobre uma UBS (OBS, leitura nominal da Secretaria, ações do paciente e remoção de vínculo) repete, dentro da transação central, a revalidação de conta e sessão com `FOR SHARE` em ambas as linhas: conta ativa, sessão não revogada nem vencida e sem troca de senha pendente. Uma requisição já autenticada que esteja em andamento faz a desativação da conta, a redefinição de senha ou a revogação da sessão esperarem o seu fim; uma requisição que começa depois disso é recusada com 401 (conta ou sessão inválidas) ou 403 `password-change-required` (troca obrigatória pendente). OBS aceita atendente/administrador autorizado à unidade; Municipal pode consultar dados identificáveis e agregados da rede; paciente acessa somente seus dados nas unidades vinculadas.

## Contratos HTTP

As rotas globais históricas `/api/patients`, `/api/queue`, `/api/slots`, `/api/patient/overview` e `/api/offers` na tabela abaixo estão bloqueadas por padrão; ficam registradas apenas para os testes legados em Development. Os contratos atuais por UBS estão descritos nas seções seguintes.

| Método e rota | Acesso | Resultado |
| --- | --- | --- |
| GET/HEAD /health/live | Público | 200 status alive, scope process; HEAD sem corpo |
| GET /health/ready | Público | 200 se conexão/tabela accounts acessíveis;503 indisponível |
| POST /api/auth/login | Público limitado | 200 token/expiresAt/user;401 credencial inválida |
| POST /api/auth/password-reset/request | Público limitado | 202 challengeId/expiresInSeconds; resposta não confirma se o CPF tem cadastro |
| POST /api/auth/password-reset/confirm | Público limitado | 200 codeValid; código incorreto, expirado ou usado retorna `false` |
| GET /api/address/cep/{cep} | Público limitado | 200 endereço do CEP;404 inexistente; consulta somente os oito dígitos |
| GET /api/auth/me | Sessão | 200 id/fullName/role/scope/mustChangePassword |
| POST /api/auth/logout | Sessão | 204 |
| POST /api/auth/password | Sessão limitada (10 por 10 min por conta autenticada) | 204; corpo `currentPassword` e `newPassword` (8 a 256); senha atual incorreta → 400 `invalid-current-password`; nova igual à atual → 400; limpa a troca obrigatória e revoga as outras sessões da conta |
| GET /api/patients | OBS | 200 lista id/fullName/cpf/email/phone/address/isOnline/isActive/createdAt, por cadastro mais recente |
| POST /api/patients | OBS | 201; fullName/cpf/email/password validados; phone e address opcionais |
| PATCH /api/patients/{id}/active | OBS atendente/administrador | 204; `isActive` booleano obrigatório; altera acesso e pendências atomicamente |
| GET /api/queue | OBS | 200 ordem enteredAt/id |
| POST /api/queue | OBS | 201 patientId; rejeita duplicidade ativa |
| POST /api/queue/{id}/cancel | OBS | 204 waiting/offered; libera próxima oferta |
| GET /api/slots | OBS | 200 vagas |
| POST /api/slots | OBS | 201 label de1 a160 caracteres |
| POST /api/patient/presence | Paciente | 204; atualiza presença somente da sessão autenticada |
| POST /api/patient/presence/offline | Paciente | 204; limpa presença somente da sessão autenticada |
| GET /api/registration/units | Público limitado | Catálogo de identificador, nome e região das UBS para o cadastro |
| POST /api/registration/start | Público limitado | Confere CPF nas UBS escolhidas e retorna opções mascaradas de e-mail |
| POST /api/registration/send-code | Público limitado | Envia código ao e-mail cadastrado escolhido |
| POST /api/registration/confirm | Público limitado | Confirma código, cria a conta e vincula somente UBS com cadastro correspondente |
| GET /api/patient/units | Paciente | 200 objeto `units` com nome, região, endereço e coordenadas apenas das unidades vinculadas ao paciente autenticado |
| GET /api/patient/overview | Paciente | 200 user/queue/offers próprios |
| POST /api/offers/{id}/respond | Paciente | 200 accept booleano explícito;409 repetida/vencida |
| GET /api/obs/units/{unitId}/registry | OBS autorizado à UBS | Lista os cadastros de referência da unidade no banco próprio, cada um com `attendance` (score dos últimos 12 meses) |
| GET /api/obs/units/{unitId}/metrics?from=&to= | OBS autorizado à UBS | 200 indicadores da UBS na janela (consultas, espera, vagas, especialidades, série diária) |
| GET /api/obs/units/{unitId}/lightning-slots/{slotId}/offers | OBS autorizado à UBS | 200 ofertas da vaga em ordem cronológica: id, patientName, status, expiresAt (sem CPF); 404 vaga inexistente |
| POST /api/obs/units/{unitId}/registry | OBS autorizado à UBS | Cadastra CPF, nome e e-mail de referência; registra o autor |
| GET /api/obs/units | OBS | Lista unidades concedidas à conta autenticada |
| GET /api/municipal/staff | Municipal | Lista contas OBS, ativas e inativas: id, fullName, role, email, isActive, mustChangePassword |
| POST /api/municipal/staff | Municipal limitado (30 por 10 min/IP) | 201 conta OBS criada com senha provisória e troca obrigatória; 409 CPF/e-mail duplicado |
| PATCH /api/municipal/staff/{id}/active | Municipal limitado | 204; `isActive` e `confirmed: true` obrigatórios; desativar revoga as sessões e preserva as concessões |
| POST /api/municipal/staff/{id}/password | Municipal limitado | 204; `password` e `confirmed: true`; nova senha provisória com troca obrigatória e sessões revogadas |
| GET /api/municipal/staff/{id}/units | Municipal | Lista unidades concedidas à conta OBS |
| PUT/DELETE /api/municipal/staff/{id}/units/{unitId} | Municipal | Concede ou revoga acesso e registra autor e data |
| GET /api/municipal/units/{unitId}/registry | Municipal | Lista os cadastros de referência da UBS selecionada (mesmos campos da listagem do OBS, sem `attendance`) e registra a consulta |
| GET /api/municipal/units | Municipal | 200 unidades com nome, região, endereço, coordenadas e `databaseState` |
| POST /api/municipal/units | Municipal limitado (20 por 10 min/IP, contador compartilhado com a nova tentativa de banco) | 201 unidade cadastrada; cria o banco da UBS e informa `databaseState`; 429 acima do limite |
| POST /api/municipal/units/{id}/database | Municipal limitado (20 por 10 min/IP) | 200 unidade com o banco pronto (tentativa idempotente); 503 `database-provisioning-failed` |
| PUT /api/municipal/units/{id} | Municipal | 200 unidade alterada; 404 se inexistente |
| GET /api/municipal/summary | Municipal | 200 patients/inactivePatients/waitingPatients/waitingEntries/unitsWithoutDatabase/openSlots/acceptedOffers e contagens agregadas de vagas e ofertas; sem indivíduos |
| GET /api/municipal/metrics?from=&to=&unitId=&region=&specialty= | Municipal | 200 indicadores da rede (totais, por UBS, por região, por especialidade, série diária) |
| GET /api/municipal/history?from=&to=&unitId=&limit=&before= | Municipal | 200 histórico de ações da gestão, mais recentes primeiro |
| GET /api/patient/units/{unitId}/attendance | Paciente vinculado | 200 score do próprio paciente nos últimos 12 meses |
| POST /api/patient/units/{unitId}/waiting-list/{id}/cancel | Paciente vinculado | 204; `confirmed: true`; só a própria entrada ativa |
| POST /api/patient/units/{unitId}/unlink | Paciente vinculado limitado (10 por 10 min por conta autenticada) | 200 `cancelledAppointments` e `cancelledEntries`; remove o vínculo |

Fila: waiting/offered/completed/cancelled. Vaga: open/offered/accepted/closed (a vaga fica `closed` quando faltam menos de dez minutos para a consulta ou ela já começou, e não é mais ofertada). Oferta: pending/accepted/declined/expired/cancelled. Aguardando inclui waiting/offered; vagas abertas inclui open/offered; aceites conta accepted. Datas UTC no servidor, horário local nos clientes. O resumo municipal também fornece `registeredSlots` (vagas já criadas), `createdOffers` (ofertas criadas), `declinedOffers`, `expiredOffers`, `pendingOffers` e `cancelledOffers`. `pendingOffers` é estado atual; os demais são totais históricos. Uma oferta criada não prova envio de notificação e um aceite não confirma atendimento ou reaproveitamento clínico. Totais não são métricas de atendimento realizado.

Prazo10min para todos. Transações/advisory lock serializam fila, ofertas, respostas/cancelamento/expiração. Prazo conferido nas operações e worker15s. Aceite vencido/limite não confirma. Recusa/expiração conclui ciclo da entrada e passa ao próximo; nova inclusão cria outro ciclo administrativo.

## Limites de taxa, IP do cliente e proxy reverso

Os limites das rotas públicas são por IP (`context.Connection.RemoteIpAddress`). Os das rotas autenticadas `POST /api/auth/password` e `POST /api/patient/units/{unitId}/unlink` são **por conta autenticada** (10 por 10 minutos cada): o limitador do ASP.NET Core roda antes da autenticação e só conhece o IP, que atrás de NAT ou proxy é compartilhado por muitas pessoas, então um filtro de endpoint (`AccountRateLimiter`), depois do filtro de sessão, usa o id da conta já validado, sem consulta extra ao banco. Uma conta esgotada recebe 429 sem afetar as demais, e trocar de sessão não zera o contador (o estado fica em memória do processo; reiniciar a API o zera).

`X-Forwarded-For` e `X-Forwarded-Proto` são **ignorados por padrão**, para que um cliente não forje o IP usado pelos limites. Atrás de um proxy reverso (por exemplo no Render), declare os endereços confiáveis em `Nora:ForwardedHeaders:KnownProxies` (IPs) e/ou `Nora:ForwardedHeaders:KnownNetworks` (CIDR, ex.: `10.0.0.0/8`); em variáveis de ambiente, `Nora__ForwardedHeaders__KnownProxies__0=...` ou uma lista separada por vírgula. Só então o middleware é ligado, honra o cabeçalho apenas de conexões vindas desses endereços e lê só a entrada mais próxima (`ForwardLimit` 1). Endereço ou rede inválidos impedem a inicialização. Não defina `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: ele ligaria o tratamento do cabeçalho sem a lista de proxies.

## Erros e verificação

Cache-Control no-store, sem header Server. Erros application/problem+json com type/title/status/detail/code/traceId, sem ecoar URL/payload/stack trace. Códigos:400 invalid-input,401 unauthorized,403 forbidden,404 not-found,405 method-not-allowed,409 conflict,429 too-many-requests,500 internal-error,503 service-unavailable. Códigos específicos, sempre com o mesmo status: 400 `invalid-current-password` (senha atual incorreta na troca de senha; nunca 401, para que os clientes não encerrem a sessão), 403 `password-change-required` (senha provisória ainda não trocada) 503 `database-provisioning-failed` (não foi possível criar ou preparar o banco da UBS) e 503 `unit-database-outdated` (o banco da UBS não está na versão atual do schema; ver Comandos de migração). Clientes não exibem corpos arbitrários do servidor.

Nora.Api.Checks verifica HTTP real Development/Production. NORA_TEST_CONNECTION deve apontar somente a PostgreSQL descartável: cria schema aleatório, migra duas vezes, testa login/autorização/cadastro/fila/concorrência/expiração/persistência e remove só esse schema. Sem variável, parte PostgreSQL explicitamente SKIP. As verificações de peso e de score de comparecimento são unitárias e rodam sem banco. `PlatformChecks` (só com `NORA_TEST_CONNECTION`) cobre, num schema próprio e com relógio de teste, a criação automática do banco da UBS (incluindo a recusa sem CREATEDB e os comandos `--migrate-unit`/`--migrate-all-units`), contas e troca de senha, origem dos eventos, score, indicadores, resumo, histórico e as ações do paciente; os bancos de UBS criados por ele ficam apenas no cluster descartável. `tests/validar-postgres-descartavel.ps1 -IncludeUnitDatabases` prepara esse cluster. `tests/validar-clientes-descartavel.ps1 -Android` sobe a fixture com `NORA_TEST_REGISTRATION=1` (três "UBS Cadastro E2E" e `/__checks/registration-code`, com bancos de UBS locais descartáveis).

## CEP e endereço de paciente

`GET /api/address/cep/{cep}` aceita somente oito dígitos, limita a 30 consultas por minuto por IP e consulta o ViaCEP no servidor com timeout curto. A resposta contém CEP, logradouro, bairro, cidade, UF e IBGE quando disponíveis; CEP inexistente retorna 404. O resultado encontrado fica em cache temporário de 24 horas. A API envia ao ViaCEP somente o CEP — nunca nome, CPF, e-mail, telefone, número ou token de sessão.

No OBS, o endereço é opcional no cadastro. Ao preenchê-lo, CEP, logradouro, cidade e UF precisam ser válidos; número e complemento continuam opcionais. O endereço integra o cadastro local consultado pelo OBS e pela gestão autorizada; o aplicativo não o exibe nesta etapa.

## Recuperação de senha do paciente

`POST /api/auth/password-reset/request` recebe `{ "cpf": "..." }`. Para CPF válido, o servidor procura somente uma conta ativa do escopo `patient` e envia um código numérico de seis dígitos ao e-mail cadastrado. A resposta `202` é equivalente para CPF desconhecido, envio limitado ou válido, evitando confirmar a existência de uma conta. O código vence em dez minutos, só pode ser usado uma vez, aceita no máximo cinco tentativas e é persistido apenas como HMAC protegido por `Nora:PasswordReset:HashKey`. A redefinição altera o hash da senha e revoga as sessões existentes.

`POST /api/auth/password-reset/confirm` recebe `{ "challengeId": "...", "code": "123456", "newPassword": "..." }`. `codeValid=false` cobre código incorreto, expirado, consumido ou indisponível; o cliente pode então pedir novo CPF ou reenviar. A solicitação está limitada por IP a cinco por dez minutos e por conta a cinco por hora. A confirmação tem limite por IP a vinte por dez minutos; as tentativas do código são contabilizadas no banco dentro de transação.

O envio acontece somente no servidor, por SMTP autenticado com TLS, porta 587. Defina os segredos no ambiente ou no user-secrets da API; não grave credenciais no app ou no Git. As chaves são `Nora__Smtp__Host`, `Nora__Smtp__Port`, `Nora__Smtp__Username`, `Nora__Smtp__Password`, `Nora__Smtp__FromAddress`, opcionalmente `Nora__Smtp__FromName`, e `Nora__PasswordReset__HashKey` (texto aleatório com pelo menos 32 bytes). Depois de configurar a conexão PostgreSQL e o SMTP, aplique as migrações com `Nora.Api --migrate` antes de iniciar a API.

Modo --client-fixture cria schema descartável para clientes, prepara contas sintéticas, publica metadata sem credenciais em .build/client-fixture.json e aguarda .build/client-fixture.stop. Ao encerrar remove seu schema. Nunca usar conexão de produção nos harnesses. Resultados atuais em execucao.md e tarefa de retomada.

## Telefone e presença no APPNORA

O telefone do paciente é opcional. Aceita de 8 a 15 dígitos, com prefixo internacional `+` opcional; espaços, parênteses, hífen e ponto são removidos na normalização. A migração aditiva preserva os cadastros anteriores com telefone nulo. O OBS envia o telefone no cadastro e apresenta o contato abaixo do nome, junto do e-mail e do CPF completo, conforme confirmação do usuário em 2026-10-12.

`isOnline` indica presença recente no APPNORA, não habilitação do cadastro nem atividade clínica. A API calcula o estado quando existe ao menos uma sessão do paciente não revogada, não vencida e com presença recebida há menos de 90 segundos. A presença/offline é vinculada ao token da própria sessão: encerrar um dispositivo não apaga a presença dos outros. Perda de rede, suspensão ou encerramento forçado são detectados pelo prazo; não há garantia de detecção instantânea. O resumo municipal não inclui telefone ou presença individual; a consulta de referência por UBS retorna nome, CPF e e-mail.

## Desativação e reativação de paciente

No fluxo global legado, indisponível em produção, administrador e atendente OBS podiam alterar `isActive`. Desativar bloqueia novos logins, revoga sessões e limpa presença. Na mesma transação da fila, cancela entradas waiting/offered e ofertas pending, libera as vagas e avança ao próximo paciente por FIFO. Cadastro, entradas concluídas e ofertas aceitas permanecem no histórico. Reativar permite novo login; sessões revogadas e pendências canceladas não são restauradas.

Login revalida a conta sob lock antes de inserir a sessão. Overview/resposta de oferta revalidam a sessão dentro da transação da fila, protegendo operações que já passaram pela autenticação antes da desativação. Inclusão de paciente desativado retorna409; token revogado ou conta desativada retorna401. `isActive` é separado de `isOnline`; um cadastro ativo pode estar offline. `createdAt` é a data UTC já persistida no cadastro.

## Filtro de período de Vaga Relâmpago

O resumo municipal inclui `lightningCohorts`, com períodos `days` de 7, 30 e 90 dias corridos a partir do horário do servidor. Cada coorte agrega vagas criadas dentro da janela e ofertas vinculadas a elas, classificadas pelo estado atual. Não são contagens de eventos que ocorreram durante a janela; o retorno não inclui dados pessoais. Os totais gerais mantêm a semântica anterior.

## Unidades de saúde

A Secretaria cadastra e corrige unidades por `POST /api/municipal/units` e `PUT /api/municipal/units/{id}`. O corpo contém `name`, `region`, `address`, `latitude` e `longitude`; coordenadas são obrigatórias, finitas e limitadas a latitude de -85 a 85 e longitude de -180 a 180. `GET` retorna a lista para o perfil municipal. O banco é a fonte das coordenadas; não há geocodificação automática, exclusão, associação de contas ou dados assistenciais por UBS. É necessário executar `--migrate` para aplicar a migração 007 antes de usar as rotas.

O paciente recebe apenas UBS vinculadas em `patient_units`. A migração 008 cria a estrutura sem vínculos automáticos; o cadastro público descrito abaixo cria os vínculos após confirmação do e-mail. O paciente também pode solicitar vínculos posteriores com conferência do CPF e remover o vínculo (ver Ações do paciente na UBS). Um teste com PostgreSQL descartável verificou que outro paciente sem vínculo recebe lista vazia.

A migração 009 mantém concessões de UBS a contas OBS e histórico de concessão/revogação com autor e data; contas anteriores não recebem acesso automaticamente. A Secretaria pode consultar contas e conceder ou revogar vínculos pela seção Usuários e histórico. A interface passou em teste WPF com API simulada. As rotas e a persistência passaram em teste com PostgreSQL 18 descartável, incluindo concessão, revogação e registro do autor. As rotas antigas globais não aplicam concessões e ficam bloqueadas por padrão; somente uma fixture de desenvolvimento as habilita explicitamente.

## Preparação de bancos separados para UBS

A fonte de identidade, sessões e cadastro de unidades permanece no banco central existente. A infraestrutura `UnitDatabaseRouter` permite configurar uma conexão PostgreSQL distinta para cada UBS pela chave privada `Nora:UnitDatabases:<id-sem-hífens>`; em variáveis de ambiente .NET, use `Nora__UnitDatabases__<id-sem-hífens>`. A chave deve conter uma connection string Npgsql, definida no ambiente seguro do servidor, sem registrá-la no Git. A chave explícita é opcional: veja a criação automática do banco em Banco da UBS criado automaticamente.

Após configurar a conexão, execute `Nora.Api --migrate-unit=<id-da-UBS>` uma vez por UBS. O comando é explícito e distinto de `--migrate`, que continua responsável apenas pelo banco central. A migração da UBS cria `nora_unit.identity`, que vincula aquele banco a um único identificador de unidade; abertura e reaplicação rejeitam identidade diferente. A migração recusa um banco contendo a tabela central de contas no schema configurado. O pool de conexões das UBS está descrito em Dimensionamento de conexões. Duas bases PostgreSQL descartáveis foram usadas para verificar a separação de cadastros, auditoria de criação, duplicidade de CPF, reaplicação e recusa de configuração trocada.

O cadastro de referência retornado pelas rotas de listagem tem, por registro: `cpf`, `fullName`, `email`, `phone` (nulo quando ausente), `address` (`cep`, `street`, `number`, `complement`, `neighborhood`, `city`, `state`, `ibge`; nulo quando ausente), `isActive` (situação local na UBS), `isOnline` (presença recente no aplicativo), `createdAt` (UTC) e, só na listagem do OBS, `attendance`.

A migração também cria o cadastro de referência da UBS, com CPF, nome, e-mail, telefone e endereço opcionais e registro do autor da criação. As rotas de cadastro e listagem validam a autorização da conta OBS para a UBS, validam dados e impedem CPF duplicado. A concessão da unidade fica bloqueada durante a operação para que uma revogação aguarde sua conclusão. O OBS permite escolher e confirmar uma UBS autorizada e criar ou listar seus cadastros de referência. A gestão pode consultar o cadastro da unidade selecionada em Unidades; a consulta é auditada e os dados são limpos ao mudar a seleção ou sair. O cadastro público confere o CPF nesses bancos. Agenda, fila e vagas por unidade usam esses bancos. As rotas de referência passaram em teste HTTP com PostgreSQL descartável, incluindo acesso negado antes da concessão e após revogação. As rotas operacionais antigas são globais e ficam bloqueadas por padrão. Nenhuma migração existente move dados antigos automaticamente entre bancos; a estratégia de migração dos dados legados ainda precisa ser definida e validada.

## Cadastro do paciente pelo aplicativo

Após entrar, o paciente pode solicitar novos vínculos por `POST /api/patient/units/link`, enviando até vinte identificadores de UBS. A API usa o CPF da sessão e confere cada banco: apenas cadastros locais ativos são vinculados. O resultado informa unidades vinculadas e não encontradas ou inativas; uma base indisponível interrompe a solicitação. A operação é limitada por IP e sincronizada com a atualização do cadastro local. Não exige outro código, pois a conta já foi ativada com o e-mail confirmado no cadastro de uma UBS, conforme a regra de um e-mail escolhido para o cadastro. A migração central 013 registra os vínculos criados a partir de sua instalação; solicitações repetidas não duplicam eventos. A migração não reconstrói eventos anteriores.

O aplicativo obtém o catálogo público limitado a identificador, nome e região por `GET /api/registration/units`. `POST /api/registration/start` recebe `fullName`, `cpf`, `password` e `unitIds`, entre uma e vinte unidades. Após validar os dados e consultar todos os bancos escolhidos, retorna um identificador temporário e opções de e-mails cadastrados parcialmente mascarados. Se nenhuma UBS tiver o CPF, retorna lista vazia e não cria conta. Se uma base estiver indisponível, a operação falha em vez de classificá-la como sem vínculo.

`POST /api/registration/send-code` recebe `challengeId` e `emailOptionId` e envia um código ao endereço previamente cadastrado na UBS; o endereço escolhido será o e-mail da conta. `POST /api/registration/confirm` recebe `challengeId` e `code`. Somente um código correto e não expirado cria a conta e os vínculos na mesma transação central. O retorno informa `linkedUnits` e `unmatchedUnits`; o código não pode ser reutilizado. A criação concorrente respeita a unicidade de CPF e e-mail. Os dados de referência permanecem nas bases de UBS.

O desafio vence em 15 minutos, o código em até dez minutos; são permitidos até três envios e cinco tentativas por código, além dos limites de requisição por IP e de cinco desafios por CPF a cada hora. A senha é persistida apenas como hash; o código e a opção de e-mail são armazenados como HMAC. É necessário configurar uma chave aleatória com pelo menos 32 bytes em `Nora__Registration__HashKey` e o SMTP já descrito em recuperação de senha. Sem esses itens o cadastro retorna indisponibilidade. O controlador Android passou com API e bancos separados descartáveis e e-mail simulado; entrega real, configuração de proxy/limites no Render e fluxo completo pela tela em dispositivo ainda precisam de verificação.

Quando o e-mail de referência estiver incorreto, atendente ou administrador da UBS autorizada pode corrigi-lo em `PATCH /api/obs/units/{unitId}/registry/email`, com corpo `cpf` e `email`. A mudança exige a migração de UBS 004 e registra o autor e o instante no histórico local. A alteração não muda automaticamente o e-mail de uma conta do aplicativo já criada; aplica-se ao cadastro de referência usado nas próximas verificações.

`PUT /api/obs/units/{unitId}/registry` atualiza nome, e-mail, telefone e endereço do cadastro localizado pelo CPF, preservando sua situação. A edição é auditada e usa o mesmo bloqueio da confirmação de cadastro no aplicativo; alterar o e-mail de referência não altera automaticamente o e-mail de uma conta já existente.

A migração de UBS 009 acrescenta situação local ao cadastro. `PATCH /api/obs/units/{unitId}/registry/active` recebe `cpf`, `isActive` e `confirmed`; exige acesso OBS e confirmação. Desativar bloqueia o paciente apenas nessa UBS, cancela entradas e ofertas pendentes dessa unidade e registra a pessoa responsável. A conta, os vínculos com outras UBS e as consultas existentes são preservados. Reativar devolve o acesso à unidade, sem restaurar entradas canceladas. A lista de UBS do aplicativo oculta unidades onde o cadastro está inativo; cada operação também verifica o estado no servidor.

## Agenda por UBS

A migração de UBS 005 cria consultas e seus eventos. O OBS autorizado consulta `GET /api/obs/units/{unitId}/appointments?from=...&to=...`, cria por `POST` com CPF do cadastro local, especialidade, profissional, início e fim e altera situação por `PATCH /api/obs/units/{unitId}/appointments/{id}/status`. Datas incluem fuso e o limite de consulta é 366 dias. Não são permitidos horários sobrepostos para o mesmo paciente ou nome de profissional na UBS; a proteção é transacional. A consulta inicia `scheduled`; transições aceitas são `scheduled` para `called`, `missed` ou `cancelled`, e `called` para `completed`, `missed` ou `cancelled`. Cancelamento exige `confirmed: true`. Cada mudança registra autor e instante na mesma transação da UBS.

O paciente consulta `GET /api/patient/units/{unitId}/appointments?from=...&to=...`: o servidor confere o vínculo e usa o CPF da sessão, retornando apenas as consultas desse paciente. Pode cancelar sua própria consulta por `POST /api/patient/units/{unitId}/appointments/{id}/cancel`, com `confirmed: true`; o evento identifica o paciente como autor. A Secretaria consulta a agenda por UBS em `GET /api/municipal/units/{unitId}/appointments`, com intervalo `from` e `to`; cada consulta nominal é auditada pela migração central 012. Uma consulta cancelada pode gerar Vaga Relâmpago após confirmação, e o aceite cria um novo agendamento. O agendamento pelo próprio paciente ainda não foi implementado.

A verificação de conflito é feita dentro do banco da UBS; sobreposições entre UBS diferentes ainda não são detectadas. Profissional é identificado pelo nome informado, pois ainda não há cadastro de profissionais integrado.

## Preparação da fila por UBS

A migração de UBS 006 prepara uma fila persistida por CPF e especialidade no próprio banco da unidade. As rotas `GET/POST /api/obs/units/{unitId}/waiting-list` listam ou incluem entradas; `POST /api/obs/units/{unitId}/waiting-list/{id}/cancel` exige `confirmed: true`. Apenas contas OBS com acesso à UBS podem operar, e inclusão e cancelamento registram autoria. A mesma pessoa pode aguardar especialidades diferentes, mas não pode ter duas entradas ativas para a mesma especialidade na mesma UBS. A fila é ordenada por data de entrada e identificador.

A tela de fila do OBS e o acompanhamento do paciente usam essa fila por unidade. As rotas globais antigas estão bloqueadas por padrão. A validação completa do fluxo ainda está em andamento; não iniciar testes oficiais.

A migração de UBS 007 prepara tabelas de vagas e ofertas locais. A criação interna de uma vaga exige confirmação, consulta cancelada, horário futuro e unicidade por consulta; ela preserva especialidade e profissional. Um distribuidor interno seleciona pacientes ativos com vínculo na UBS e a mesma especialidade por ordem de entrada, oferecendo por dez minutos e avançando após expiração. A migração 008 distingue eventos automáticos de ações da equipe. A resposta aceita ou recusa a oferta; o aceite cria uma consulta para o paciente na mesma transação. As rotas por UBS permitem que o OBS crie e consulte vagas e o paciente consulte e responda às próprias ofertas; uma verificação periódica trata expirações e tenta redistribuí-las. As telas do OBS e do aplicativo usam as rotas por unidade; a validação completa ainda está em andamento.

Uma vaga aberta ou ofertada reserva o intervalo do profissional na UBS: uma consulta comum não pode sobrepô-la. A criação interna da vaga também recusa o reaproveitamento se o horário já tiver sido ocupado por outra consulta ou vaga.

Uma oferta somente é iniciada se ainda restarem dez minutos completos antes do início da consulta; vagas abertas com menos tempo são encerradas. A conferência de elegibilidade é feita no banco central com vínculos e contas ativas bloqueados durante a operação da UBS. A API tenta redistribuir após inclusão ou cancelamento da fila, criação da vaga e recusa; o processamento periódico serve também para expirações e recuperação após falhas.

## Resumo da rede

`GET /api/municipal/summary` agrega os bancos das UBS e conta cada CPF uma única vez. O parâmetro opcional `unitId` restringe o resumo a uma unidade; filas, vagas, ofertas e coortes são calculadas a partir dos dados locais. Se um banco necessário estiver indisponível, o resumo falha em vez de retornar totais parciais (503). Unidades com `databaseState` `pending` não entram no resumo e são contadas em `unitsWithoutDatabase`. As contagens de pessoas usam CPF distinto na rede: `patients` conta quem tem ao menos um cadastro ativo em alguma UBS; `inactivePatients`, quem só tem cadastros desativados; `waitingPatients`, as pessoas aguardando (waiting/offered) mesmo que estejam em várias filas; e `waitingEntries`, o total de entradas ativas (o número que `waitingPatients` devolvia antes). Os campos novos ficam no fim da resposta; clientes antigos continuam lendo por nome. No caminho legado (`LegacyEnabled`) os campos novos vêm zerados. As rotas globais históricas ficam bloqueadas por padrão e só podem ser habilitadas explicitamente em ambiente Development para fixtures antigas; não são opção de implantação.

## Banco da UBS criado automaticamente

A conexão de cada UBS é resolvida no servidor. A chave explícita `Nora:UnitDatabases:<id-sem-hífens>` (variável `Nora__UnitDatabases__<id-sem-hífens>`) continua válida e tem precedência. Sem ela, se `Nora:UnitDatabases:AutoProvision` for verdadeiro (padrão `true`), a conexão é derivada da conexão central trocando só o nome do banco para `<prefixo><id-sem-hífens>`, com o prefixo em `Nora:UnitDatabases:NamePrefix` (padrão `nora_ubs_`, validado por `^[a-z][a-z0-9_]{0,30}$`; prefixo inválido impede a inicialização). Com `AutoProvision` falso só valem as chaves explícitas. `AutoProvision` e `NamePrefix` ficam no mesmo bloco das chaves de UBS, mas nunca são confundidos com um identificador de unidade.

`POST /api/municipal/units` grava a UBS e, em seguida, cria o banco se ele não existir (`CREATE DATABASE` fora de transação, com identificador montado apenas do prefixo validado e do GUID em hexadecimal), aplica as migrações de UBS, preenche `health_units.database_ready_at` e registra o evento `unit-database-created` (uma única vez). **O usuário PostgreSQL da API precisa da permissão CREATEDB**; sem ela (SQLSTATE 42501) ou diante de qualquer falha, a UBS continua cadastrada com `databaseState` `pending` e a resposta continua 201. A falha é registrada sem connection string e só com o SQLSTATE.

`POST /api/municipal/units/{id}/database` repete a tentativa de forma idempotente: se o banco já abre, com a identidade certa e o schema na versão atual, retorna logo (só garante a marcação), sem `CREATE DATABASE` nem reaplicar migrações; caso contrário cria o banco se faltar, aplica as migrações (que tomam um lock exclusivo) e só marca/registra na primeira vez. Sucesso → 200 com a unidade; falha → 503 com `code` `database-provisioning-failed`; unidade inexistente → 404. É limitada a 20 chamadas por 10 minutos por IP; o cadastro de UBS (`POST /api/municipal/units`), que também cria um banco, usa o mesmo limitador e o mesmo contador.

`databaseState` aparece em `GET`, `POST` e `PUT /api/municipal/units` (e nas listas de unidades por conta). `ready` significa chave explícita configurada ou `database_ready_at` preenchido; `pending`, o contrário. UBS `pending` ficam fora do catálogo público (`/api/registration/units`), do vínculo pelo aplicativo (cadastro e `POST /api/patient/units/link` respondem 400 se a lista incluir uma UBS inexistente ou sem banco pronto), do resumo e dos indicadores, que informam `unitsWithoutDatabase`, e da verificação periódica de ofertas. Um banco `ready` que esteja fora do ar continua respondendo 503. As listas de unidades de uma conta OBS (`/api/obs/units`) e da Secretaria continuam trazendo as unidades `pending`, com o estado, para que a interface avise.

Comandos de migração: `--migrate` (banco central), `--migrate-unit=<id>` (uma UBS; passa a preencher `database_ready_at`) e `--migrate-all-units` (aplica as migrações de UBS em todas as unidades `ready`; imprime quantas foram atualizadas e sai com código 1 se alguma falhar). A migração 010 de UBS precisa chegar aos bancos existentes por `--migrate-all-units`.

A migração de UBS 011 cria `nora_unit.schema_version` e `UnitMigrationDao` carimba nela, ao fim de cada migração, o maior número de migração aplicado (hoje 011). A API confere esse carimbo na primeira abertura de cada banco de UBS por processo; um banco migrado por versão anterior (sem a tabela ou com número menor) é tratado como desatualizado, e as rotas daquela UBS respondem 503 com `code` `unit-database-outdated`, em vez de um erro de SQL genérico (por exemplo, coluna `source` ausente nos indicadores). Ao subir, um serviço em segundo plano confere cada UBS `ready` e registra um aviso (`O banco da UBS <id> está com o schema desatualizado; execute Nora.Api --migrate-all-units`, sem connection string nem dado pessoal); a subida nunca é impedida. Um banco desatualizado é conferido de novo a cada abertura, então volta a funcionar assim que `--migrate-all-units` (ou `--migrate-unit`, ou a nova tentativa de banco) o atualiza, sem reiniciar a API. Enquanto houver UBS `ready` desatualizada, o resumo e os indicadores da rede também respondem 503 `unit-database-outdated` (sem total parcial). O primeiro deploy desta versão deve rodar `--migrate-all-units` antes de receber tráfego; `scripts/iniciar-local.ps1` já o executa depois de `--migrate`.

As restrições CHECK das migrações de UBS 004, 008 e 010 passam por `nora_unit.ensure_check`: sem alteração quando já existem com os mesmos valores e validadas; recriadas quando faltam ou diferem, adicionadas `NOT VALID` e depois validadas, para não bloquear escritas em tabelas com dados.

## Dimensionamento de conexões

Cada UBS com banco derivado tem um pool Npgsql próprio. Em vez de herdar o pool da conexão central (que, sem `Maximum Pool Size`, vale 100 e pode trazer um `Minimum Pool Size` alto), a conexão derivada usa `Maximum Pool Size` de `Nora:UnitDatabases:MaxPoolSize` (inteiro de 1 a 100; padrão 3; valor inválido impede a inicialização), `Minimum Pool Size` 0, `Connection Idle Lifetime` de 60 s e `Connection Pruning Interval` de 10 s. A verificação periódica de ofertas visita todas as UBS prontas a cada 15 s, uma de cada vez; com essa poda, as conexões das unidades quietas fecham entre as visitas em vez de ficarem abertas. Chaves explícitas (`Nora:UnitDatabases:<id>`) mantêm a própria connection string, com o máximo limitado a dez.

O teto de conexões que a API pode abrir é a soma dos pools, e o PostgreSQL precisa comportá-lo:

```text
max_connections ≥ pool central + (nº de UBS × MaxPoolSize) + folga
```

O pool central é o `Maximum Pool Size` da connection string `Nora` (padrão do Npgsql: 100; defina um valor explícito, como 20). A folga cobre `superuser_reserved_connections` (3 por padrão), manutenção, migrações, o provisionamento de bancos e ferramentas de backup. Exemplo: 20 + 90 UBS × 3 + 20 = 310; o padrão `max_connections=100` não basta para 90 UBS e deve ser elevado ou substituído por pooling externo. O teto só é atingido se todas as UBS receberem carga simultânea; no uso normal o número real é bem menor, mas o dimensionamento deve prever o pior caso.

Alternativa: PgBouncer em modo `transaction`, com `default_pool_size` pequeno por banco; as conexões lógicas da API deixam de ser conexões do servidor. Os travamentos da API usam `pg_advisory_xact_lock` (por transação), compatíveis com esse modo, mas o provisionamento de bancos usa `pg_advisory_lock` de sessão e `CREATE DATABASE`, portanto a conexão central usada para criar bancos deve falar direto com o PostgreSQL, não pelo PgBouncer.

## Origem dos eventos de consulta e score de comparecimento

A migração de UBS 010 acrescenta `appointment_events.source` (`staff`, `system` ou `patient`; padrão `staff`). A mudança de situação feita pelo paciente no aplicativo e a consulta criada pelo aceite de uma Vaga Relâmpago gravam `patient`; o OBS grava `staff`. Eventos anteriores à migração permanecem `staff` e, portanto, não pesam no score (limitação aceita).

O score é `concluídas ÷ (concluídas + faltas + soma dos pesos dos cancelamentos)`; sem consulta encerrada (denominador zero) o valor é nulo. Só pesam cancelamentos de origem `patient`; cancelamentos feitos pela equipe, consultas `scheduled` e `called` não entram. O peso é uma fração de falta pela antecedência, medida entre o instante do cancelamento e `starts_at`, com cada faixa incluindo o limite inferior: menos de 1 h = 1,0; de 1 h a menos de 2 h = 0,9; de 2 h a menos de 6 h = 0,8; de 6 h a menos de 12 h = 0,6; de 12 h a menos de 24 h = 0,4; de 24 h a menos de 48 h = 0,2; de 48 h a menos de 72 h = 0,1; de 72 h a menos de 96 h = 0,02; 96 h ou mais = 0; antecedência negativa pesa 1,0. O período é sempre pela data marcada (`starts_at`). Pesos e scores são arredondados em quatro casas.

## Indicadores

`GET /api/obs/units/{unitId}/metrics?from=&to=` (OBS com concessão à UBS) e `GET /api/municipal/metrics?from=&to=&unitId=&region=&specialty=` (Secretaria). A janela é `from` ≤ `starts_at` < `to`, com datas que incluem o fuso; `to` deve ser posterior a `from` e o intervalo tem no máximo 366 dias (400 fora disso). Nenhuma resposta traz dado pessoal.

Resposta da UBS: `from`, `to`; `appointments` (`scheduled`, `called`, `completed`, `missed`, `cancelled`, `cancelledByPatient`, `cancellationWeight`, `attendanceScore` de 0 a 1 ou nulo), contagem por situação atual das consultas marcadas na janela; `waiting` (`activeEntries`, `waitingPatients` com CPF distinto, `acceptedEntries`, `averageWaitMinutes`, `longestActiveWaitMinutes`; nulos sem dados); `lightning` (`registeredSlots`, `acceptedOffers`, `declinedOffers`, `expiredOffers` das vagas criadas na janela); `bySpecialty` (`specialty`, `completed`, `missed`, `cancelled`, `cancellationWeight`, `attendanceScore`, `activeEntries`, `averageWaitMinutes`); e `daily` (`date` `yyyy-MM-dd`, `scheduled`, `completed`, `missed`, `cancelled`, só dias com dado). Na série diária `scheduled` é a quantidade de consultas ainda na situação `scheduled`, como no resumo.

A espera média é o tempo entre `entered_at` e a saída da fila por oferta aceita, nas entradas cuja saída ocorreu na janela; recusa, expiração e cancelamento não entram. `longestActiveWaitMinutes` é a maior espera atual entre as entradas ativas. A especialidade é texto livre: os grupos usam `lower(btrim(specialty))` e a resposta traz a grafia mais frequente do grupo. O dia da série diária é calculado no fuso `Nora:TimeZone` (padrão `America/Sao_Paulo`), não em UTC. As quatro leituras de cada UBS saem em uma única viagem ao banco.

Resposta da rede: `from`, `to`; `totals` (`appointments`, `waiting`, `lightning`, no mesmo formato, com `waitingPatients` distinto por CPF na rede); `byUnit` (`unitId`, `name`, `region`, `appointments`, `waiting`, `lightning`); `byRegion` (`region`, `appointments`, `waiting`, `lightning`; regiões agrupadas sem diferenciar maiúsculas); `bySpecialty`; `daily`; e `unitsWithoutDatabase`. Os filtros `unitId` (404 se inexistente), `region` (sem diferenciar maiúsculas) e `specialty` restringem tudo, inclusive as vagas. Unidades `pending` ficam fora e são contadas em `unitsWithoutDatabase`; um banco `ready` indisponível responde 503, sem total parcial.

## Score por paciente

`GET /api/obs/units/{unitId}/registry` traz em cada registro `attendance` (`completed`, `missed`, `cancelledByPatient`, `cancellationWeight`, `score`), calculado em uma única consulta agrupada. A janela é a dos últimos 12 meses pela data marcada, sem limite superior (cancelamentos futuros entram). Paciente sem consulta encerrada recebe contagens zeradas e `score` nulo. A rota municipal de cadastro não recebe o campo. `GET /api/patient/units/{unitId}/attendance` devolve o mesmo cálculo só do próprio paciente (CPF da sessão), com `windowMonths` 12.

## Histórico de ações

`GET /api/municipal/history?from=&to=&unitId=&limit=&before=` une as concessões e revogações de UBS, as consultas nominais da gestão, as ações sobre contas OBS e a criação de bancos de UBS. Todos os parâmetros são opcionais; `limit` padrão 100 e máximo 200 (400 fora de 1 a 200), `before` é um cursor por instante (somente eventos estritamente anteriores) e a ordem é do mais recente ao mais antigo. Cada item traz `occurredAt`, `kind`, `actorName`, `unitId`, `unitName` e `accountName`: `staff-granted`, `staff-revoked`, `registry-read`, `appointment-read`, `staff-created`, `staff-deactivated`, `staff-reactivated`, `staff-password-reset` e `unit-database-created`. Não há CPF nem e-mail. Eventos que compartilham o mesmo instante podem ser omitidos ao paginar pelo cursor.

## Contas e troca de senha

`POST /api/auth/password` (qualquer perfil) recebe `{ "currentPassword": "...", "newPassword": "..." }`. Senha atual incorreta → 400 com `code` `invalid-current-password` (não 401: os clientes encerram a sessão em 401); nova senha fora de 8 a 256 caracteres ou igual à atual → 400. Em caso de sucesso (204) grava o hash, limpa `must_change_password` e revoga as outras sessões da conta; a atual continua.

A Secretaria cria contas de atendente ou administrador em `POST /api/municipal/staff` com `{ "fullName", "cpf", "email", "password", "role" }` (`role` `attendant` ou `administrator`; mesmas validações de CPF, e-mail e senha do provisionamento; duplicidade → 409). A conta nasce com `must_change_password` verdadeiro: a senha é provisória. `PATCH /api/municipal/staff/{id}/active` recebe `{ "isActive": bool, "confirmed": true }` (`confirmed` obrigatório e verdadeiro); desativar revoga as sessões e impede o login, e as concessões de UBS são preservadas. `POST /api/municipal/staff/{id}/password` recebe `{ "password": "...", "confirmed": true }`, define uma nova senha provisória e revoga as sessões; é a recuperação de contas OBS, que não têm e-mail de recuperação. Cada ação grava um evento em `staff_account_events` na mesma transação. As três rotas respondem 404 para contas que não sejam OBS.

## Ações do paciente na UBS

`POST /api/patient/units/{unitId}/waiting-list/{id}/cancel` com `{ "confirmed": true }` cancela a própria entrada (CPF da sessão) em `waiting` ou `offered`, cancela a oferta pendente dela e dispara a redistribuição da vaga, como o cancelamento pela equipe; o evento de fila grava origem `patient`. Entrada de outra pessoa ou inexistente → 404; já encerrada → 409; sem `confirmed` verdadeiro → 400.

`POST /api/patient/units/{unitId}/unlink` com `{ "confirmed": true }` cancela as consultas futuras `scheduled`/`called` do paciente (eventos de origem `patient`, que pesam no score pela tabela normal), cancela as entradas e as ofertas ativas dele na UBS com redistribuição das vagas, remove a linha de `patient_units` e grava o evento `unlinked`. Devolve `{ "cancelledAppointments": n, "cancelledEntries": n }`. Funciona mesmo que o cadastro do paciente naquela UBS esteja desativado. Depois disso as rotas da UBS respondem 403 ao paciente, e ele pode vincular de novo pelo fluxo existente. A remoção do vínculo e as alterações na UBS vivem em bancos diferentes e não têm commit atômico. A API remove o vínculo (e grava o evento) na transação central ainda aberta, roda a ação na UBS e só então confirma a central. Se o banco da UBS ou qualquer instrução central antes do commit da UBS falhar, nada é cancelado e o vínculo é mantido. Se o commit central falhar depois de a UBS confirmar, as consultas e entradas ficam canceladas e o vínculo continua; **repetir a chamada é idempotente e conclui** (remove o vínculo, grava um único evento `unlinked`), mas as contagens devolvidas pela repetição referem-se apenas ao que ainda estava ativo (podem ser 0 e 0).

## Ofertas de uma vaga

`GET /api/obs/units/{unitId}/lightning-slots/{slotId}/offers` devolve as ofertas da vaga em ordem cronológica, cada uma com `id`, `patientName`, `status` (`pending`, `accepted`, `declined`, `expired`, `cancelled`) e `expiresAt`, sem CPF.
