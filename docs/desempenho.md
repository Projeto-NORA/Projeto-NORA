# Desempenho

Análise de 2026-11-17 (skill de otimização, etapas 1 a 5) e o que foi aplicado no mesmo dia (P1 e P2 de manhã; P3, P4 e P5 à noite, por decisão do usuário). O gargalo do NORA é o **banco** (idas, locks de linha e commits com WAL na central), não a CPU da API nem a rede. Para poucas UBS e poucos pacientes nada aparece; o custo cresce com o histórico, o número de pacientes vinculados e o número de computadores do OBS abertos.

## Caminhos quentes

| Origem | Frequência | O que faz |
| --- | --- | --- |
| `UnitLightningWorker` | 15 s por UBS | lista as UBS prontas (1 consulta central) e, para cada uma em série, `UnitLightningDispatcher.AdvanceAsync` |
| `MaintenanceWorker` | 1 min após subir e a cada 6 h | 4 `DELETE` em lote e o reconciliador de e-mail; barato, com índices |
| `PushWorker` | 30 s | desligado sem credenciais FCM |
| OBS (WPF), tela inicial | 20 s por computador | 5 `GET` em paralelo (registry, waiting-list, lightning-slots, appointments de hoje, active-rooms) e comunicados incrementais; a cada 5 min alertas, indicadores e capacidade |
| App Android (presença) | 30 s por paciente | presença (escrita), `lightning-offers` por UBS vinculada, comunicados e, com família, dependentes e as ofertas deles |
| Conflito entre UBS (`CrossUnitOverlapService`) | só em escrita | 1 abertura de conexão por outra UBS onde o CPF tem cadastro, em série |

## Tabela de gastos

M = medido, E = estimado por leitura do código. "Aplicado" diz o que entrou em 2026-11-17.

| # | Onde | Padrão | Frequência | Custo | Situação |
| --- | --- | --- | --- | --- | --- |
| G1 | `UnitLightningDispatcher.AdvanceAsync` | cada passe abria transação central, `FOR SHARE` em todas as contas e vínculos elegíveis da UBS, advisory lock da UBS e 6 `UPDATE`/`INSERT` de expiração, **mesmo sem nada a fazer** | 240 passes/h por UBS | M: 12 comandos SQL e 1 XID da central por passe ocioso | **Aplicado (P1)**: pré-checagem só de leitura |
| G2 | `GET /registry` (`UnitRegistryReadService`) | lista o cadastro inteiro da UBS, abre 2 conexões extras (central e UBS) e agrega 12 meses de consultas a cada chamada | 3/min por OBS | E: O(pacientes) + O(consultas de 12 meses) | **Aplicado (P3, 2026-11-17)**: a presença é lida na conexão central do executor (sem 2ª conexão); cache fica de fora |
| G3 | `UnitAccessService.ExecuteAsync` e `ExecutePatientAsync` nas rotas de **leitura** | `FOR UPDATE` na concessão ou no vínculo: os 5 `GET` paralelos do mesmo usuário se enfileiravam e cada um assumia XID e escrevia WAL | 5 por ciclo de 20 s por OBS; 1 por UBS por 30 s por paciente | M: a latência do ciclo era a soma dos 5, não o máximo | **Aplicado (P2)**: `FOR SHARE` nas rotas de leitura |
| G4 | `GET waiting-list` e `GET lightning-slots` | devolvem todo o histórico (nada sai nunca) e as vagas abrem outra conexão central | 3/min por OBS cada | E: cresce sem limite com o tempo | **Aplicado (P4, 2026-11-17)**: ativos sempre + histórico paginado com cursor; muda contrato |
| G5 | `ListForPatientAsync` (polling do app) | `WHERE q.cpf=$1` sem predicado de status; os índices de `waiting_list` são parciais; não há índice em `waiting_list(cpf)` | 2/min por paciente por UBS | E: varredura que cresce com o histórico | **Aplicado (P5, 2026-11-17)**: migração de UBS 017 (índice por CPF e por data de entrada); exige `--migrate-all-units` (uma UBS sem a migração responde 503) |
| G6 | `UnitDatabaseRouter.OpenAsync` | `SELECT unit_id FROM nora_unit.identity` em toda abertura | ~3 por GET de registry | E: 1 ida (0,1 a 0,3 ms local) | Aberto (P7): toca invariante de isolamento entre UBS |
| G7 | `HealthUnitService.ListReadyAsync` | lista todas as UBS a cada 15 s (worker), 30 s (push) e por `GET /units` | ~6/min fixos | E: pequeno (dezenas de linhas) | Aberto (P8): cache de 5 a 15 s |
| G8 | `CrossUnitOverlapService` | abre conexão em cada outra UBS, em série, dentro da transação central | só escrita | E: k × (abertura + identidade + `EXISTS` indexado) | Aberto (P9): `Task.WhenAll`; ganho só com CPF em muitas UBS |
| G9 | pool por UBS `MaxPoolSize=3`, `ConnectionIdleLifetime=60 s` | o worker visita cada UBS a cada 15 s: a conexão nunca fecha; 5 `GET` paralelos de um OBS disputam 3 conexões | contínuo | E: aparece como latência, não como erro | Aberto (P6): só configuração (`Nora__UnitDatabases__MaxPoolSize`), se a medição mostrar fila |

Sem achado de código desnecessário em métodos de DAO ou serviço criados nas últimas rodadas (todos têm chamador). Limpezas pequenas sem risco, mas sem ganho para a apresentação, ficam para depois: `GuardianDisplay`, `ThisDeviceDisplay`, o recurso `InverseBoolVisibility` e os `x:Name` `KeepSignedInBox` e `ActivationInput`, todos sem uso. Rede: as respostas JSON não têm compressão nem `ETag`; em LAN é irrelevante, em hospedagem remota o registry e as vagas pesam.

## P1: passe ocioso do relâmpago (aplicado)

`AdvanceAsync` agora começa por `HasWorkAsync`, só leitura e sem lock. O passe completo roda se existir ao menos uma destas condições (é tudo o que ele altera):

1. vaga `open` ou `offered` (distribuição, fechamento `too-late`);
2. oferta `pending` (a expiração e a volta à fila dependem dela);
3. vaga `awaiting_approval` com início até 1 h à frente (fecha por `approval-timeout`);
4. com a aprovação das vagas ligada (`slot_approval_since`), consulta cancelada no futuro (a mais de 1 h), sem vaga, cancelada depois de a chave ser ligada, cujo horário o profissional não ocupou de novo: o **mesmo** `WHERE` da criação das propostas (`ProposalWhere`).

Fora isso o passe sai com 1 comando (2 com a verificação de identidade da UBS) e sem abrir transação central, sem `FOR SHARE` nos vínculos e sem advisory lock. A leitura central da chave de aprovação só acontece quando há consulta cancelada candidata. Uma vaga ou oferta que nasça logo depois da leitura é pega pela ação que a criou (`TryAdvanceAsync`, 12 pontos) ou pelo próximo passe de 15 s; a pré-checagem preserva a expiração, porque oferta pendente sempre passa.

## P2: `FOR SHARE` nas rotas de leitura (aplicado)

`UnitAccessService.ExecuteAsync(..., read: true)` e `ExecutePatientAsync(..., read: true)` seguram a concessão (`staff_units`) ou o vínculo (`patient_units`) em `FOR SHARE`. Rotas marcadas: registry, waiting-list, lightning-slots, ofertas de uma vaga, appointments e active-rooms do OBS; appointments, lightning-offers e waiting-list do paciente; a prévia do Chamar próximo. Por que é seguro: a revogação da concessão é um `DELETE` e sair da UBS é `FOR UPDATE` + `DELETE`, ambos conflitam com `FOR SHARE` e continuam esperando a leitura terminar; escritas seguem em `FOR UPDATE`; a ordem de locks (conta e sessão, depois concessão) não mudou, então nenhum ciclo novo é possível (só deixaram de existir esperas entre leitores). Os testes de `PerformanceChecks` provam com uma transação que segura `FOR SHARE` à mão: outra leitura passa, a revogação e a escrita esperam, sair da UBS espera.

## P3: conexão central do executor no registry (aplicado)

`UnitRegistryReadService.ListAsync` agora recebe a conexão central da transação do executor de acesso e lê a presença dos pacientes nela (antes abria uma 2ª conexão central dentro da rota, que já segurava a primeira). Por chamada ao `GET /registry` o OBS usa uma conexão a menos do pool central e uma ida a menos ao servidor; com o pool central cheio (muitos computadores abertos) a rota deixa de esperar a si mesma. A listagem da Secretaria (`ExecuteManagementReadAsync`, sem conexão para repassar) continua abrindo a própria. Teste: `ClosingChecks` roda a rota com o pool central limitado a **1 conexão** (antes 503 por falta de conexão, depois 200). Medida (5 pacientes, pool de 1): ~3 ms por chamada.

## P4: histórico paginado de fila e vagas (aplicado)

`GET waiting-list` e `GET lightning-slots` devolviam toda a tabela, inclusive o histórico que só cresce. Agora trazem **todos os ativos** e uma página do histórico (padrão 100, até 500) com cursor opaco (contrato em [api](api.md)); o cliente pede `Carregar mais histórico` só quando quer. Medida com 20.000 entradas de fila e 5.000 vagas encerradas (`ClosingChecks`, o histórico inteiro equivale à resposta de antes):

| Rota | Antes (histórico inteiro) | Depois (1ª página) |
| --- | --- | --- |
| waiting-list | 20.114 itens, 3.929 KiB, ~334 ms | 102 itens, 19,6 KiB, ~4 ms |
| lightning-slots | 5.008 itens, 1.840 KiB, ~140 ms | 101 itens, 37 KiB, ~12 ms |

A cada 20 s por computador do OBS. O custo da página não cresce com o histórico (índice `waiting_list(entered_at DESC, id DESC)` e `lightning_slots(created_at)`).

## P5: índice por CPF na fila (aplicado)

Migração de UBS **017** (`UnitSchema.ExpectedVersion` = 17): `waiting_list(cpf, entered_at, id)` e `waiting_list(entered_at DESC, id DESC)`. A lista por CPF (app do paciente, 2 por minuto por UBS vinculada, e responsável) também passou a ter consulta própria (`WHERE q.cpf=$1`, antes `($1='' OR q.cpf=$1)`, que não deixa o planejador usar índice). Medida com `EXPLAIN (ANALYZE)` em 20.000 entradas e 2.000 CPFs: **sem o índice 1,4 ms (Seq Scan)**, **com o índice 0,04 ms (Bitmap Heap Scan)**, cerca de 35 vezes; sem o índice o custo cresce com o histórico, com ele não. Depois de atualizar a API rode `--migrate` e `--migrate-all-units`.

## Prioridade por pontos na Vaga Relâmpago (custo)

`LightningEligibility.PickAsync`/`PreviewAsync` buscam os candidatos da especialidade na ordem de entrada e, com 2 ou mais, fazem **uma** consulta agrupada de comparecimento só desses CPFs (índice `appointments(cpf, starts_at)`), na mesma transação; um candidato só não gasta a consulta extra. O passe ocioso do P1 não muda (a pré-checagem sai antes).

## Medições (PostgreSQL 18, cluster descartável, `max_connections=16`, 20 pacientes vinculados, máquina de desenvolvimento)

`Nora.Api.Checks --performance-only`. Nas medições históricas abaixo, os comandos eram contados pelo medidor `db.client.operation.duration` do Npgsql e os XIDs pela diferença de `txid_current()`. O teste atual mede os comandos do próprio passe usando a atividade de diagnóstico do Npgsql e o banco identificado por `db.namespace`. A verificação confirma captura com um comando de controle e exige zero comandos na central durante o passe ocioso.

| Medida | Antes | Depois |
| --- | --- | --- |
| Passe ocioso, comandos SQL | 12 por passe (3.600 em 300 passes) | 2 por passe (600 em 300 passes) |
| Passe ocioso, XIDs consumidos na central | 1 por passe (300 em 300 passes) | 0 |
| Passe ocioso, tempo | 4,43 / 8,00 / 22,15 ms por passe (3 execuções) | 0,68 / 0,68 / 0,69 ms por passe (3 execuções, máquina parada) |
| 5 `GET` do OBS em paralelo, 40 rodadas | 7,7 / 9,7 / 13,0 / 18,3 ms por rodada (4 execuções) | 5,8 / 6,2 / 5,8 ms por rodada (3 execuções, máquina parada) |
| Leitura enquanto outra leitura segura a concessão | bloqueada (estourou 5 s) | passa |
| Revogação enquanto uma leitura está em andamento | espera | espera (garantia mantida) |

Os tempos oscilam com a carga da máquina (e, com poucos dados e um banco local, o ganho de latência das leituras é pequeno: o ganho de P2 é a ausência da fila entre leitores, que cresce com o número de computadores do OBS e com a lentidão de cada leitura); as contagens de comandos e de XIDs e os testes de bloqueio são determinísticos.

## Como repetir

```powershell
pwsh -NoProfile -File tests/validar-postgres-descartavel.ps1 -PostgreSqlBin "$env:LOCALAPPDATA\Programs\PostgreSQL\18\bin" -IncludeUnitDatabases
# só as medições (com NORA_TEST_CONNECTION apontando para o PostgreSQL descartável):
dotnet .build/Nora.Api.Checks/bin/Release/net10.0/Nora.Api.Checks.dll --performance-only
```

Para olhar uma instrução a instrução, use `log_min_duration_statement=0` no cluster descartável por 60 s sem cliente e conte as linhas por banco.
