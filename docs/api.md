# API NORA

## Aplicação e persistência

Nora.Api usa ASP.NET Core .NET10 e Npgsql10.0.3 com PostgreSQL 18. NoraApiHost constrói pipeline; endpoints delegam a serviços e DAOs. DatabaseHelper abre/fecha conexões, parametriza valores e valida schema. Nenhum cliente acessa PostgreSQL diretamente.

Migrações incorporadas 001_accounts.sql, 002_workflow.sql, 003_patient_contact_presence.sql e 004_patient_account_state.sql criam schema, contas, sessões, entradas, vagas e ofertas com índices/constraints. A quarta acrescenta `accounts.is_active`, inicialmente verdadeiro, preservando cadastros anteriores. São aditivas/reaplicáveis, executadas explicitamente por --migrate com transação/lock PostgreSQL. Abrir API não altera schema implicitamente.

## Configuração privada e contas

Configure ConnectionStrings__Nora no ambiente do processo ou ConnectionStrings:Nora em user-secrets. Nora__Schema é opcional (padrão nora). Nunca versionar conexão com senha. Instância de uso: PostgreSQL 18, usuário postgres; conexão/provisionamento definitivo ainda em P-012.

Após build Release, scripts/iniciar-local.ps1 -ProvisionAccounts solicita senha PostgreSQL oculta, cria base nora se ausente, aplica migrações e pede provisionamento OBS/Municipal. Nome aprovado: Mateus Todeschini Monteiro. Paciente é cadastrado pela tela OBS com dados recebidos privadamente. Não há seed pessoal. O banco armazena hash PBKDF2 ASP.NET Identity; contas duplicadas são preservadas e retornam conflito.

```powershell
dotnet build NORA.slnx --configuration Release -m:1 --disable-build-servers
.\scripts\iniciar-local.ps1 -ProvisionAccounts
# Nas próximas aberturas:
.\scripts\iniciar-local.ps1
```

Também é possível usar dotnet <Nora.Api.dll> --migrate e --provision com conexão privada configurada; provisionamento exige terminal interativo. API padrão escuta http://127.0.0.1:5080, sobrescrito por --urls/ASPNETCORE_URLS. Android debug usa http://10.0.2.2:5080 e AllowedHosts correspondente. Acesso externo exige HTTPS e configuração de hosts/deploy; CORS não é aberto.

O bootstrap lê configuração pelo diretório da DLL, independentemente da pasta atual do terminal. Assim, iniciar pelos scripts da raiz continua carregando `appsettings.json` e a porta 5080 documentada. Testes de hosts usam content root próprio e portas aleatórias explicitamente.

## Sessões e autorização

Login recebe scope (obs/municipal/patient), identifier (CPF ou e-mail) e password. CPF formatado é normalizado; e-mail vai para minúsculas. CPF/e-mail são únicos dentro do escopo, permitindo contas separadas.

Token aleatório opaco dura8h, usa header Authorization: Bearer <token>; somente SHA256 fica no banco. Logout revoga sessão. Clientes guardam token/identidade em memória, exigindo novo login ao reabrir. Login limitado a30 tentativas por minuto/IP. OBS aceita atendente/administrador; Municipal só agregados; paciente só própria fila/ofertas.

## Contratos HTTP

| Método e rota | Acesso | Resultado |
| --- | --- | --- |
| GET/HEAD /health/live | Público | 200 status alive, scope process; HEAD sem corpo |
| GET /health/ready | Público | 200 se conexão/tabela accounts acessíveis;503 indisponível |
| POST /api/auth/login | Público limitado | 200 token/expiresAt/user;401 credencial inválida |
| GET /api/auth/me | Sessão | 200 id/fullName/role/scope |
| POST /api/auth/logout | Sessão | 204 |
| GET /api/patients | OBS | 200 lista id/fullName/cpf/email/phone/isOnline/isActive/createdAt, por cadastro mais recente |
| POST /api/patients | OBS | 201; fullName/cpf/email/password validados; phone opcional |
| PATCH /api/patients/{id}/active | OBS atendente/administrador | 204; `isActive` booleano obrigatório; altera acesso e pendências atomicamente |
| GET /api/queue | OBS | 200 ordem enteredAt/id |
| POST /api/queue | OBS | 201 patientId; rejeita duplicidade ativa |
| POST /api/queue/{id}/cancel | OBS | 204 waiting/offered; libera próxima oferta |
| GET /api/slots | OBS | 200 vagas |
| POST /api/slots | OBS | 201 label de1 a160 caracteres |
| POST /api/patient/presence | Paciente | 204; atualiza presença somente da sessão autenticada |
| POST /api/patient/presence/offline | Paciente | 204; limpa presença somente da sessão autenticada |
| GET /api/patient/overview | Paciente | 200 user/queue/offers próprios |
| POST /api/offers/{id}/respond | Paciente | 200 accept booleano explícito;409 repetida/vencida |
| GET /api/municipal/summary | Municipal | 200 patients/waitingPatients/openSlots/acceptedOffers; sem indivíduos |

Fila: waiting/offered/completed/cancelled. Vaga: open/offered/accepted. Oferta: pending/accepted/declined/expired/cancelled. Aguardando inclui waiting/offered; vagas abertas inclui open/offered; aceites conta accepted. Datas UTC no servidor, horário local nos clientes. Totais não são métricas de atendimento realizado.

Prazo10min para todos. Transações/advisory lock serializam fila, ofertas, respostas/cancelamento/expiração. Prazo conferido nas operações e worker15s. Aceite vencido/limite não confirma. Recusa/expiração conclui ciclo da entrada e passa ao próximo; nova inclusão cria outro ciclo administrativo.

## Erros e verificação

Cache-Control no-store, sem header Server. Erros application/problem+json com type/title/status/detail/code/traceId, sem ecoar URL/payload/stack trace. Códigos:400 invalid-input,401 unauthorized,403 forbidden,404 not-found,405 method-not-allowed,409 conflict,429 too-many-requests,500 internal-error,503 database-unavailable. Clientes não exibem corpos arbitrários do servidor.

Nora.Api.Checks verifica HTTP real Development/Production. NORA_TEST_CONNECTION deve apontar somente a PostgreSQL descartável: cria schema aleatório, migra duas vezes, testa login/autorização/cadastro/fila/concorrência/expiração/persistência e remove só esse schema. Sem variável, parte PostgreSQL explicitamente SKIP.

Modo --client-fixture cria schema descartável para clientes, prepara contas sintéticas, publica metadata sem credenciais em .build/client-fixture.json e aguarda .build/client-fixture.stop. Ao encerrar remove seu schema. Nunca usar conexão de produção nos harnesses. Resultados atuais em execucao.md e tarefa de retomada.

## Telefone e presença no APPNORA

O telefone do paciente é opcional. Aceita de 8 a 15 dígitos, com prefixo internacional `+` opcional; espaços, parênteses, hífen e ponto são removidos na normalização. A migração aditiva preserva os cadastros anteriores com telefone nulo. O OBS envia o telefone no cadastro e apresenta o contato abaixo do nome, junto do e-mail e do CPF completo, conforme confirmação do usuário em 2026-10-12.

`isOnline` indica presença recente no APPNORA, não habilitação do cadastro nem atividade clínica. A API calcula o estado quando existe ao menos uma sessão do paciente não revogada, não vencida e com presença recebida há menos de 90 segundos. A presença/offline é vinculada ao token da própria sessão: encerrar um dispositivo não apaga a presença dos outros. Perda de rede, suspensão ou encerramento forçado são detectados pelo prazo; não há garantia de detecção instantânea. O painel Municipal continua recebendo somente agregados, sem telefone/presença individual.

## Desativação e reativação de paciente

Política confirmada em 2026-10-12: administrador e atendente OBS podem alterar `isActive`. Desativar bloqueia novos logins, revoga sessões e limpa presença. Na mesma transação da fila, cancela entradas waiting/offered e ofertas pending, libera as vagas e avança ao próximo paciente por FIFO. Cadastro, entradas concluídas e ofertas aceitas permanecem no histórico. Reativar permite novo login; sessões revogadas e pendências canceladas não são restauradas.

Login revalida a conta sob lock antes de inserir a sessão. Overview/resposta de oferta revalidam a sessão dentro da transação da fila, protegendo operações que já passaram pela autenticação antes da desativação. Inclusão de paciente desativado retorna409; token revogado ou conta desativada retorna401. `isActive` é separado de `isOnline`; um cadastro ativo pode estar offline. `createdAt` é a data UTC já persistida no cadastro.
