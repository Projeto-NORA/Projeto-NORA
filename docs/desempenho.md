# Desempenho

Análise de 2026-11-17 (skill de otimização, etapas 1 a 5) e o que foi aplicado no mesmo dia. O gargalo do NORA é o **banco** (idas, locks de linha e commits com WAL na central), não a CPU da API nem a rede. Para poucas UBS e poucos pacientes nada aparece; o custo cresce com o histórico, o número de pacientes vinculados e o número de computadores do OBS abertos.

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
| G2 | `GET /registry` (`UnitRegistryReadService`) | lista o cadastro inteiro da UBS, abre 2 conexões extras (central e UBS) e agrega 12 meses de consultas a cada chamada | 3/min por OBS | E: O(pacientes) + O(consultas de 12 meses) | Aberto (P3): cache de 30 a 60 s ou ler o cadastro a cada 5 min no OBS; muda o que o usuário vê, decisão dele |
| G3 | `UnitAccessService.ExecuteAsync` e `ExecutePatientAsync` nas rotas de **leitura** | `FOR UPDATE` na concessão ou no vínculo: os 5 `GET` paralelos do mesmo usuário se enfileiravam e cada um assumia XID e escrevia WAL | 5 por ciclo de 20 s por OBS; 1 por UBS por 30 s por paciente | M: a latência do ciclo era a soma dos 5, não o máximo | **Aplicado (P2)**: `FOR SHARE` nas rotas de leitura |
| G4 | `GET waiting-list` e `GET lightning-slots` | devolvem todo o histórico (nada sai nunca) e as vagas abrem outra conexão central | 3/min por OBS cada | E: cresce sem limite com o tempo | Aberto (P4): filtrar ou paginar; muda contrato |
| G5 | `ListForPatientAsync` (polling do app) | `WHERE q.cpf=$1` sem predicado de status; os índices de `waiting_list` são parciais; não há índice em `waiting_list(cpf)` | 2/min por paciente por UBS | E: varredura que cresce com o histórico | Aberto (P5): índice `waiting_list(cpf)` exige migração de UBS 017 e `--migrate-all-units` (uma UBS sem a migração responde 503) |
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

## Medições (PostgreSQL 18, cluster descartável, `max_connections=16`, 20 pacientes vinculados, máquina de desenvolvimento)

`Nora.Api.Checks --performance-only`. Comandos SQL contados pelo medidor `db.client.operation.duration` do Npgsql (inclui `BEGIN` e `COMMIT`); XIDs pela diferença de `txid_current()` no banco central.

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
