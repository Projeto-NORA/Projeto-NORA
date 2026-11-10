# API NORA

## Aplicação e persistência

Nora.Api usa ASP.NET Core .NET10 e Npgsql10.0.3 com PostgreSQL 18. NoraApiHost constrói pipeline; endpoints delegam a serviços e DAOs. DatabaseHelper abre/fecha conexões, parametriza valores e valida schema. Nenhum cliente acessa PostgreSQL diretamente.

Migrações centrais incorporadas 001_accounts.sql a 013_patient_unit_link_events.sql criam schema, contas, sessões, entradas, vagas, ofertas e códigos temporários de recuperação. A quarta acrescenta `accounts.is_active`, inicialmente verdadeiro, preservando cadastros anteriores. A quinta guarda apenas o hash HMAC do código e os dados mínimos para expiração, tentativas e uso único. A sexta acrescenta endereço opcional do paciente, preservando cadastros que ainda não possuem CEP. A sétima cria o cadastro de unidades e coordenadas. A oitava cria vínculos explícitos entre pacientes e unidades, sem associar cadastros anteriores automaticamente. A nona cria concessões de unidades a contas OBS e histórico de concessões e revogações. A décima registra consultas da gestão aos cadastros de uma unidade. A décima primeira guarda desafios temporários de cadastro de pacientes e a décima segunda permite auditar a consulta de agendas pela gestão. São aditivas/reaplicáveis, executadas explicitamente por --migrate com transação/lock PostgreSQL. Abrir API não altera schema implicitamente.

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

Token aleatório opaco dura8h, usa header Authorization: Bearer <token>; somente SHA256 fica no banco. Logout revoga sessão. Clientes guardam token/identidade em memória, exigindo novo login ao reabrir. Login limitado a30 tentativas por minuto/IP. OBS aceita atendente/administrador autorizado à unidade; Municipal pode consultar dados identificáveis e agregados da rede; paciente acessa somente seus dados nas unidades vinculadas.

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
| GET /api/auth/me | Sessão | 200 id/fullName/role/scope |
| POST /api/auth/logout | Sessão | 204 |
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
| GET /api/obs/units/{unitId}/registry | OBS autorizado à UBS | Lista os cadastros de referência da unidade no banco próprio |
| POST /api/obs/units/{unitId}/registry | OBS autorizado à UBS | Cadastra CPF, nome e e-mail de referência; registra o autor |
| GET /api/obs/units | OBS | Lista unidades concedidas à conta autenticada |
| GET /api/municipal/staff | Municipal | Lista contas OBS ativas, com nome e perfil |
| GET /api/municipal/staff/{id}/units | Municipal | Lista unidades concedidas à conta OBS |
| PUT/DELETE /api/municipal/staff/{id}/units/{unitId} | Municipal | Concede ou revoga acesso e registra autor e data |
| GET /api/municipal/units/{unitId}/registry | Municipal | Lista CPF, nome e e-mail de referência da UBS selecionada e registra a consulta |
| GET /api/municipal/units | Municipal | 200 unidades com nome, região, endereço e coordenadas |
| POST /api/municipal/units | Municipal | 201 unidade cadastrada |
| PUT /api/municipal/units/{id} | Municipal | 200 unidade alterada; 404 se inexistente |
| GET /api/municipal/summary | Municipal | 200 patients/waitingPatients/openSlots/acceptedOffers e contagens agregadas de vagas e ofertas; sem indivíduos |

Fila: waiting/offered/completed/cancelled. Vaga: open/offered/accepted. Oferta: pending/accepted/declined/expired/cancelled. Aguardando inclui waiting/offered; vagas abertas inclui open/offered; aceites conta accepted. Datas UTC no servidor, horário local nos clientes. O resumo municipal também fornece `registeredSlots` (vagas já criadas), `createdOffers` (ofertas criadas), `declinedOffers`, `expiredOffers`, `pendingOffers` e `cancelledOffers`. `pendingOffers` é estado atual; os demais são totais históricos. Uma oferta criada não prova envio de notificação e um aceite não confirma atendimento ou reaproveitamento clínico. Totais não são métricas de atendimento realizado.

Prazo10min para todos. Transações/advisory lock serializam fila, ofertas, respostas/cancelamento/expiração. Prazo conferido nas operações e worker15s. Aceite vencido/limite não confirma. Recusa/expiração conclui ciclo da entrada e passa ao próximo; nova inclusão cria outro ciclo administrativo.

## Erros e verificação

Cache-Control no-store, sem header Server. Erros application/problem+json com type/title/status/detail/code/traceId, sem ecoar URL/payload/stack trace. Códigos:400 invalid-input,401 unauthorized,403 forbidden,404 not-found,405 method-not-allowed,409 conflict,429 too-many-requests,500 internal-error,503 service-unavailable. Clientes não exibem corpos arbitrários do servidor.

Nora.Api.Checks verifica HTTP real Development/Production. NORA_TEST_CONNECTION deve apontar somente a PostgreSQL descartável: cria schema aleatório, migra duas vezes, testa login/autorização/cadastro/fila/concorrência/expiração/persistência e remove só esse schema. Sem variável, parte PostgreSQL explicitamente SKIP.

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

O paciente recebe apenas UBS vinculadas em `patient_units`. A migração 008 cria a estrutura sem vínculos automáticos; o cadastro público descrito abaixo cria os vínculos após confirmação do e-mail. O paciente também pode solicitar vínculos posteriores com conferência do CPF; a remoção voluntária ainda não está implementada. Um teste com PostgreSQL descartável verificou que outro paciente sem vínculo recebe lista vazia.

A migração 009 mantém concessões de UBS a contas OBS e histórico de concessão/revogação com autor e data; contas anteriores não recebem acesso automaticamente. A Secretaria pode consultar contas e conceder ou revogar vínculos pela seção Usuários e histórico. A interface passou em teste WPF com API simulada. As rotas e a persistência passaram em teste com PostgreSQL 18 descartável, incluindo concessão, revogação e registro do autor. As rotas antigas globais não aplicam concessões e ficam bloqueadas por padrão; somente uma fixture de desenvolvimento as habilita explicitamente.

## Preparação de bancos separados para UBS

A fonte de identidade, sessões e cadastro de unidades permanece no banco central existente. A infraestrutura `UnitDatabaseRouter` permite configurar uma conexão PostgreSQL distinta para cada UBS pela chave privada `Nora:UnitDatabases:<id-sem-hífens>`; em variáveis de ambiente .NET, use `Nora__UnitDatabases__<id-sem-hífens>`. A chave deve conter uma connection string Npgsql, definida no ambiente seguro do servidor, sem registrá-la no Git. Cada banco deve ser criado previamente e ter conexão própria configurada; cadastrar a UBS na Secretaria ainda não cria o banco nem configura a conexão.

Após configurar a conexão, execute `Nora.Api --migrate-unit=<id-da-UBS>` uma vez por UBS. O comando é explícito e distinto de `--migrate`, que continua responsável apenas pelo banco central. A migração da UBS cria `nora_unit.identity`, que vincula aquele banco a um único identificador de unidade; abertura e reaplicação rejeitam identidade diferente. A migração recusa um banco contendo a tabela central de contas no schema configurado. O pool é limitado a dez conexões por UBS. Duas bases PostgreSQL descartáveis foram usadas para verificar a separação de cadastros, auditoria de criação, duplicidade de CPF, reaplicação e recusa de configuração trocada.

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

`GET /api/municipal/summary` agrega os bancos das UBS e conta cada CPF uma única vez. O parâmetro opcional `unitId` restringe o resumo a uma unidade; filas, vagas, ofertas e coortes são calculadas a partir dos dados locais. Se um banco necessário estiver indisponível, o resumo falha em vez de retornar totais parciais. As rotas globais históricas ficam bloqueadas por padrão e só podem ser habilitadas explicitamente em ambiente Development para fixtures antigas; não são opção de implantação.
