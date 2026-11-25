# Execução e validação

## Validação do fechamento das pendências — 2026-11-17

Código do fechamento (score em pontos de 0 a 1000, prioridade na Vaga Relâmpago pelo score, contas de equipe sem CPF, P3, P4 e P5, marcar a qualquer horário), conferido de novo em 2026-11-18, depois das correções das voltas 1 a 3 da revisão, com PostgreSQL 18 descartável, sem commit:

| Verificação | Resultado |
| --- | --- |
| Build Release de `NORA.slnx` | 0 erros, 0 avisos |
| `validar-postgres-descartavel.ps1 -IncludeUnitDatabases` (porta 55840) | todas verdes (273 PASS e nenhum FAIL no log): HTTP 34/34, fluxos 28/28 (+ bancos por UBS PASS), plataforma 19/19, revisão 17/17, comunicação e Jornada 16/16, agenda 21/21, Família 21/21 + 14/14 + 7/7 + 5/5, Google e push 15/15, computadores do OBS 13/13, chamar próximo 8/8, serviços e horários 7/7, conflito entre UBS 7/7, aprovação das vagas 8/8, caixa de saída do e-mail 5/5, volta à fila 5/5, desempenho 6/6, fechamento 16/16 |
| `Nora.Desktop.Checks` (sem API) | 181/181 |
| `validar-clientes-descartavel.ps1` (E2E desktop com banco, porta 55842, só WPF) | 184/184 na repetição; a primeira passada deu 183/184, ver a observação sobre a corrida do teste |
| Android: `assembleDebug`, `testDebugUnitTest`, `lintDebug`, `assembleDebugAndroidTest` | 295 testes locais reexecutados com `--rerun` (0 falhas, 0 pulados), lint 0 erros e 7 avisos antigos (versões de plugins e `UnitsMap.kt`), APKs gerados |
| Android: `validateDebugScreenshotTest` | 55 prévias. Três das Jornada (`JourneyExamsLight`, `JourneyOneUnitDownLight`, `JourneyEmptyDark`) falharam pela mudança esperada de texto do score com mais de uma UBS ("A ordem usa o score de cada UBS..."); as referências (ignoradas pelo Git) foram atualizadas com `updateDebugScreenshotTest`, conferidas na imagem e a validação seguinte passou 55/55; na rodada de 2026-11-18 o `validateDebugScreenshotTest` sozinho, com `--rerun`, passou 55/55 sem precisar atualizar nada |
| Android: `connectedDebugAndroidTest` no `Medium_Phone_API_37.0` (sem janela) | 48 testes: 46 aprovados e 2 pulados por assumption (`patientUsesRealDisposablePostgresApi` sem `noraApiUrl` e `VisualCaptureTest` sem `noraCaptureVisual=true`). Na primeira passada os 2 de `LocalNetworkPermissionTest` também ficaram pulados porque a permissão `ACCESS_LOCAL_NETWORK` já estava concedida no AVD; com o app reinstalado eles rodaram sozinhos e passaram 2/2. Na rodada de 2026-11-18, com o app já instalado, o XML trouxe 48 testes e só esses 2 de assumption (`patientUsesRealDisposablePostgresApi` e `VisualCaptureTest`) como não executados, os outros 46 aprovados (inclusive `LocalNetworkPermissionTest` 2/2 e `BookingDatePickerTest` 3/3) |

Observações: na primeira passada do E2E de 2026-11-18, a verificação "Secretaria: Visão geral mostra os totais novos e avisa quantas UBS ficaram fora por não terem base" falhou uma vez ("O aviso continuou depois de a base ser criada") e passou em cinco repetições da suíte sem API. A causa estava no teste, não no produto: `ProvisionSelectedUnitDatabaseAsync` dispara `ReloadNetwork` em segundo plano e o teste conferia o aviso sem esperar essa releitura terminar. O teste agora espera `Settle(vm)` antes de conferir (`tests/Nora.Desktop.Checks/SecretariaChecks.cs`), e o E2E repetido passou 184/184. `validar-clientes-descartavel.ps1` exige PowerShell 7 (`pwsh`); no Windows PowerShell 5.1 ele recusa por causa do `#requires`. Antes do E2E rodou `gradlew --stop` e havia cerca de 6 GB de RAM livre; o E2E passou sem erro de memória. O script do servidor avisou que a instância descartável não confirmou o encerramento; depois da rodada não restou nenhum PostgreSQL nas portas 5584x. Emulador desligado e daemons do Gradle parados ao fim.

## Volta 2 — 2026-11-17 (R-030, otimização P1/P2 e achados da revisão)

Tarefa `docs/tarefas/2026-11-17-r030-otimizacao-volta2.md`. Depois da volta à fila (R-030), da pré-checagem do passe do relâmpago (P1), do `FOR SHARE` nas leituras (P2, ver [desempenho](desempenho.md)), do `device.dat` que só é descartado por dado inválido, da prévia do Chamar próximo com o horário e da repetição em `40P01`, com PostgreSQL 18 descartável:

| Verificação | Resultado |
| --- | --- |
| Build Release de `NORA.slnx` | 0 erros, 0 avisos |
| `validar-postgres-descartavel.ps1 -IncludeUnitDatabases` (porta 55791) | HTTP 32/32; fluxos 28/28; plataforma 19/19; revisão 17/17; comunicação e Jornada 16/16; agenda 21/21; Família 21/21; revisão da Família 14/14; decisões 7/7; lacunas 5/5; Google e push 15/15; computadores do OBS 13/13; chamar próximo 8/8; serviços e horários 7/7; conflito entre UBS 7/7; aprovação das vagas 8/8; caixa de saída do e-mail 5/5; **volta à fila 5/5**; **desempenho e concorrência 6/6** |
| `Nora.Desktop.Checks` (sem API) | 173/173 |
| `validar-clientes-descartavel.ps1` (E2E desktop com banco, porta 55792) | 176/176 |

Os testes que esperavam `completed` depois de recusa ou vencimento foram atualizados para o novo roteiro (R-030): `PlatformChecks` (UBS 2: o P2 recusa, volta à fila e depois cancela a própria entrada), `FamilyChecks.Gaps` (a entrada do dependente fica `waiting`), `WorkflowChecks` (resumo com 3 pessoas aguardando e a nova inclusão de quem voltou à fila é 409) e `CallNextChecks` (a ordem em que as vagas voltam à fila).
## Validação final antes da apresentação — 2026-11-17, 17h28

Código com R-030 (recusa ou vencimento devolve à fila), otimização P1 e P2, correções das voltas 2 e 3 da revisão, P-046, P-048 e o botão "Configurar" em fonte ampliada:

| Verificação | Resultado |
| --- | --- |
| Build Release de `NORA.slnx` | 0 erros, 0 avisos |
| `Nora.Desktop.Checks` (sem API) | 173/173 |
| `validar-clientes-descartavel.ps1` (E2E desktop com banco, porta 55774) | 176/176 |
| `validar-postgres-descartavel.ps1 -IncludeUnitDatabases` (porta 55764) | todas verdes: HTTP 32/32, fluxos 28/28, plataforma 19/19, revisão 17/17, comunicação 16/16, agenda 21/21, Família 21/21 + 14/14 + 7/7 + 5/5, Google e push 15/15, computadores 13/13, chamar próximo 8/8, horários 7/7, conflito entre UBS 7/7, aprovação 8/8, caixa de saída 5/5, volta à fila 5/5, desempenho 6/6 |
| Android | 285 testes locais, lint 0 erros, 54 prévias, 45 instrumentados (43 aprovados e 2 pulados por assumption) no `Medium_Phone_API_37.0` |

Observação: uma rodada anterior do E2E falhou numa verificação de gráfico do OBS com "Insufficient memory to continue the execution of the program", com só 1,9 GB de RAM livre (emulador, daemons do Gradle e agentes ao mesmo tempo). A mesma verificação passou na suíte do desktop, e o E2E repetido com 18 GB livres passou 176/176. Para rodar o E2E, feche o emulador e rode `gradlew --stop` antes.

## Estado das validações — 2026-11-17 (rodada final)

Conferido no código da rodada final (P-014, Chamar próximo, serviços e horários da UBS, conflito entre UBS, aprovação das vagas e caixa de saída do e-mail, mais as 12 correções da revisão adversarial), antes do commit, com PostgreSQL 18 descartável:

| Verificação | Resultado |
| --- | --- |
| Build Release de `NORA.slnx` | 0 erros, 0 avisos |
| `Nora.Desktop.Checks` (sem API) | 170/170 |
| `validar-postgres-descartavel.ps1 -IncludeUnitDatabases` (porta 55762) | HTTP 32/32; fluxos 28/28; bancos por UBS PASS; plataforma 19/19; revisão 17/17; comunicação e Jornada 16/16; agenda 21/21; Família 21/21; revisão da Família 14/14; decisões 7/7; lacunas 5/5; Google e push 15/15; computadores do OBS 13/13; chamar próximo 8/8; serviços e horários 7/7; conflito entre UBS 7/7; aprovação das vagas 8/8; caixa de saída do e-mail 5/5 |
| `validar-clientes-descartavel.ps1` (E2E desktop com banco, porta 55772) | 173/173 |
| Android (commit `dfadb0e`) | 285 testes locais, lint 0 erros e 6 avisos antigos, 54 prévias, 41 instrumentados + 2 pulados por assumption no `Medium_Phone_API_37.0`; o E2E `-Android` passou com o código de `09fdd87` |
| `scripts/demo-local.ps1` | sobe banco, migrações e API em pasta temporária; `/health/ready` responde e o host `10.0.2.2` do emulador é aceito |
| `Nora.Api.Checks --demo-bootstrap` (`NORA_DEMO_CONNECTION=... dotnet Nora.Api.Checks.dll --demo-bootstrap`) | prepara **só o banco da demonstração** (`scripts/demo-local.ps1`, porta 55432, só aceita `127.0.0.1`), pelo host da API em processo: UBS Escola Centro, especialidade Clínica geral, contas de demonstração (Secretaria e administrador da UBS **sem CPF**, entram só por e-mail; paciente com CPF), cadastro de referência, vínculo e acesso à UBS dos administradores OBS que já existirem. Precisa do central na 022 e das UBS na 017. Idempotente; senhas aleatórias em `%LOCALAPPDATA%\NORA\demo\acesso-demo.txt` (fora do Git), nunca impressas; não envia e-mail. Detalhes em `tarefas/2026-11-17-bootstrap-demo-app.md` |

Versões de banco desta rodada: central até a **022** (equipe sem CPF, P-053), UBS até a **017** (índices da fila, P5). Rode `Nora.Api --migrate` e depois `Nora.Api --migrate-all-units`, nessa ordem (a migração das UBS exige o central na 021 ou mais). A segunda volta da revisão adversarial rodou depois (P-050 resolvida, 5 achados corrigidos).

**Fechamento de 2026-11-17 (servidor).** Suíte completa `tests/validar-postgres-descartavel.ps1 -IncludeUnitDatabases` (porta 55802): HTTP 34/34, PostgreSQL e fluxos 28/28, plataforma 19/19, revisão 17/17, comunicação e Jornada 16/16, agenda 21/21, Família 21/21 + 14/14 + 7/7 + 5/5, Google e push 15/15, computadores do OBS 13/13, chamar próximo 8/8, serviços e horários 7/7, conflito entre UBS 7/7, aprovação das vagas 8/8, caixa de saída do e-mail 5/5, volta à fila 5/5, desempenho 6/6 e **fechamento 16/16** (`--closing-only`: score em pontos, prioridade da Vaga Relâmpago, equipe sem CPF, P3, P4, P5). Build Release de `Nora.Api` e `tests/Nora.Api.Checks` com 0 erros e 0 avisos. Bancos: central **022**, UBS **017**; rode `--migrate` e `--migrate-all-units`.

## Estado das validações — 2026-11-16

Números da Fase 3 (agenda pelo catálogo, capacidade, metas, alertas, comunicação, Jornada, Família, troca de e-mail, desativação e manutenção), no código dos commits `75a6a06` (API), `fffca9b` (desktop) e `ca0e380` (app), já com as correções da revisão adversarial, e com PostgreSQL 18.6 descartável. São os resultados que cada área relatou na última rodada; uma rodada integrada final pode atualizá-los. As seções seguintes são registros datados de rodadas anteriores e não descrevem o estado atual (a rodada de 2026-11-15, no HEAD `3d46a78`, tinha desktop 103/103, E2E 104/104 e Android 84/84 locais com 28 previews).

| Verificação | Resultado |
| --- | --- |
| Build Release da solução `NORA.slnx` (`Nora.Api`, `Nora.Api.Checks`, `Nora.Desktop`, `Nora.Desktop.Checks`, `OBS-NORA` e `Painel-Municipal`) | 0 erros, 0 avisos |
| API HTTP (`Nora.Api.Checks`) | 32/32 (inclui o 503 `google-disabled` do login pelo Google sem configuração) |
| PostgreSQL 18: fluxos | 28/28 |
| PostgreSQL 18: bancos separados por UBS | PASS |
| PostgreSQL 18: plataforma (banco da UBS, contas, score, indicadores, histórico, ações do paciente) | 19/19 |
| PostgreSQL 18: revisão crítica do back | 17/17 |
| PostgreSQL 18: comunicação e Jornada (`CommunityChecks`) | 16/16 |
| PostgreSQL 18: agenda, capacidade, metas e alertas (`SchedulingChecks`) | 21/21 |
| PostgreSQL 18: Família, e-mail da conta e manutenção (`FamilyChecks`) | 21/21 |
| PostgreSQL 18: correções da revisão da Família (`FamilyChecks.RunReviewAsync`) | 14/14 |
| PostgreSQL 18: decisões da Família (`FamilyChecks.RunDecisionsAsync`) | 7/7 |
| PostgreSQL 18: lacunas da Família, P-043 (`FamilyChecks.RunGapsAsync`: 429 das escritas delegadas, corrida encerrar × operação delegada, recusa de oferta pelo responsável) | 5/5 |
| PostgreSQL 18: Entrar com Google e push, P-042 (`GooglePushChecks`) | 15/15 |
| Suíte desktop (`Nora.Desktop.Checks`) | 140/140 |
| Abertura dos executáveis (`tests/desktop-startup.ps1`) | 2/2 |
| E2E desktop com banco, por UBS (`tests/validar-clientes-descartavel.ps1`) | 141/141 em 2026-11-16, depois de todas as correções da revisão (inclui Secretaria, OBS, cadastro, fila, agenda, vaga e ofertas, indicadores, profissional, grade, bloqueio e agenda pelo catálogo) |
| Android: testes locais | 225, 0 falhas (2026-11-16, depois de P-045/P-040) |
| Android: previews `validateDebugScreenshotTest` | 48 |
| Android: lint | 0 erros, 6 avisos antigos (versões disponíveis e `UnitsMap.kt`) |
| Android: testes instrumentados (`connectedDebugAndroidTest`, emulador `Medium_Phone_API_37.0`) | 30: 28 aprovados, 2 pulados por assumption (`patientUsesRealDisposablePostgresApi` sem `noraApiUrl`, `VisualCaptureTest` sem `noraCaptureVisual=true`) |
| E2E com banco e `-Android` (`validar-clientes-descartavel.ps1 -Android`, porta 55740) | desktop 150/150; Android 29 aprovados, 1 pulado (`VisualCaptureTest`), incluindo `patientUsesRealDisposablePostgresApi` |

**API em 2026-11-16 (P-042 e P-043).** As linhas da API acima são da rodada desta data no HEAD `c3c2bcc` com as alterações da tarefa `docs/tarefas/2026-11-16-api-google-push.md`: `dotnet build` Release com `-m:1` de `Nora.Api` e de `tests/Nora.Api.Checks` com 0 erros e 0 avisos, e `validar-postgres-descartavel.ps1 -IncludeUnitDatabases` verde nas portas 55750 e 55751, com os mesmos números nas duas (evidências em `.build/postgres-validation-25b7c4f534ae494c8255d1871f293826` e `.build/postgres-validation-497f5e9d02cb4d0c97e82555b606f93f`).

**PostgreSQL 18 portátil.** A máquina Pichau só tinha o PostgreSQL 17; o 18.6 foi instalado como binários oficiais (EDB, `postgresql-18.6-5-windows-x64-binaries.zip`) em `%LOCALAPPDATA%\Programs\PostgreSQL\18`, sem serviço do Windows nem senha, e o 17 não foi tocado. Os scripts `tests/validar-postgres-descartavel.ps1`, `tests/validar-clientes-descartavel.ps1` e `scripts/iniciar-local.ps1` aceitam `-PostgreSqlBin <pasta>\bin` (padrão `C:\Program Files\PostgreSQL\18\bin`) e `-Port` (padrões 55439, 55440 e 5433). O cluster descartável de `validar-postgres-descartavel.ps1` sobe com `max_connections=16`, o que obriga o pool de cada UBS a ser enxuto.

```powershell
pwsh -NoProfile -File tests/validar-postgres-descartavel.ps1 -PostgreSqlBin "$env:LOCALAPPDATA\Programs\PostgreSQL\18\bin" -IncludeUnitDatabases
pwsh -NoProfile -File tests/validar-clientes-descartavel.ps1 -PostgreSqlBin "$env:LOCALAPPDATA\Programs\PostgreSQL\18\bin"
```

**Partes da suíte da API.** Sem argumentos, `Nora.Api.Checks` roda tudo: as verificações HTTP e, quando `NORA_TEST_CONNECTION` aponta para um PostgreSQL (o `validar-postgres-descartavel.ps1` define essa variável e, com `-IncludeUnitDatabases`, as dos bancos de UBS), fluxos, bancos separados, plataforma, revisão, comunicação e Jornada, agenda, Família, as correções da revisão da Família, as decisões e as lacunas da Família, o Entrar com Google e push (com um Google falso, sem rede), a volta à fila, o desempenho (P1 e P2), os computadores do OBS, o chamar próximo, os serviços e horários da UBS, o conflito entre UBS, a aprovação das vagas, a caixa de saída do e-mail e o fechamento; sem a variável, as partes de banco são puladas com `SKIP`. Para repetir só uma parte, com a mesma variável definida, passe uma das opções (com `dotnet run`, depois de `--`) `--review-only`, `--community-only`, `--scheduling-only`, `--family-only`, `--family-review-only`, `--family-decisions-only`, `--family-gaps-only`, `--google-push-only`, `--workflow-only`, `--closing-only` (fechamento de 2026-11-17: score em pontos, prioridade da Vaga Relâmpago, equipe sem CPF, P3, P4 e P5), `--queue-return-only` (R-030: volta à fila na mesma posição), `--performance-only` (P1 e P2 de [desempenho](desempenho.md), com as linhas `MEDIDA`, e o impasse 40P01 da volta 2), `--devices-only`, `--call-next-only`, `--slot-approval-only`, `--unit-profile-only`, `--cross-unit-only` ou `--email-outbox-only`. `--client-fixture` não é teste: sobe a fixture de API usada pelo E2E dos clientes. `--demo-bootstrap` também não é teste: prepara o banco da demonstração (ver a tabela acima); se as contas de demonstração já existem e o `acesso-demo.txt` com a senha sumiu, ele recusa rodar e não sobrescreve o arquivo.

**E2E por UBS.** `validar-clientes-descartavel.ps1` sobe a fixture por UBS (`NORA_TEST_REGISTRATION=1` e `NORA_E2E_UNIT_REGISTRY=1`, com bancos de UBS descartáveis) e o desktop exercita OBS e Secretaria contra a API real. Com `-Android` o script também instala e roda os testes instrumentados, o que exige emulador. O script não liga o emulador: suba-o antes (ver `android-emulador.md`) e confira `adb devices`. Em 2026-11-16 a rodada `-Android` foi feita com a API e o desktop do HEAD `09fdd87` compilados num worktree temporário (outro agente alterava a API neste) e com o APPNORA deste worktree; a primeira tentativa teve uma falha intermitente do desktop (P-047) e a repetição passou inteira. Dois cuidados do Android 17 (API 37) apareceram nessa rodada: o app precisa de `ACCESS_LOCAL_NETWORK` para falar com `10.0.2.2` (o teste a concede com `pm grant`), e a oferta da Vaga Relâmpago só existe depois do ciclo de 15 s do `UnitLightningWorker` (o teste espera até 45 s).

**Banco da UBS em produção.** A API cria os bancos das UBS com um usuário PostgreSQL que tenha `CREATEDB`. Rode `Nora.Api --migrate` (central até a 024) e depois `Nora.Api --migrate-all-units` (UBS prontas até a 018) antes de receber tráfego. As duas migrações preparam as políticas de exclusão programada. Uma UBS desatualizada responde 503 `unit-database-outdated`. Confira que `max_connections` comporta `pool central + nº de UBS × MaxPoolSize + folga`. O script `scripts/iniciar-local.ps1` executa as duas migrações. Veja [a documentação da API](api.md).

## Integração por UBS — 2026-11-09

Com dados sintéticos, a solução Release compilou sem avisos ou erros; passaram 28 verificações PostgreSQL de fluxos, a conferência de bancos separados e o teste focado do OBS para cadastro, estado local e painel. O teste integrado executou OBS, gestão e o controlador Android com API, bancos descartáveis e e-mail simulado, incluindo cadastro, agenda, fila, aceite de Vaga Relâmpago e conferência de vínculos. Interações da interface Android, inclusive resultado parcial do vínculo, foram exercitadas separadamente com dados sintéticos; o fluxo completo de cadastro pela tela até o e-mail real ainda não foi testado. A localização física, o envio real de e-mail e a hospedagem continuam sem verificação. A suíte WPF ampla passou 43/66: muitas falhas procuram os controles e contratos globais anteriores, enquanto outros casos de apresentação e rolagem ainda precisam de análise; não é uma validação completa da nova interface.

## WPF

Requer Windows com .NET SDK 10. Foi usado SDK 10.0.401. As duas soluções individuais continuam disponíveis; a solução integrada inclui os executáveis, `Nora.Desktop` e o harness sem pacotes de testes externos.

```powershell
dotnet build NORA.slnx --configuration Release --nologo -m:1
dotnet run --project OBS-NORA/OBS-NORA.csproj
dotnet run --project Painel-Municipal/Painel-Municipal.csproj
dotnet run --configuration Release --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj
.\tests\desktop-startup.ps1
```

No sandbox desta sessão, a tentativa inicial de build da solução com paralelismo padrão falhou sem diagnóstico de compilação. A execução sequencial com `-m:1 --disable-build-servers` passou com zero erros e avisos. Os builds individuais também passaram. `dotnet --info` encontrou restrição de acesso ao Service Control Manager; isso não impediu os builds.

Os checks verificam preferências, corrupção de arquivo, isolamento dos aplicativos, erros de armazenamento, tema dinâmico, contraste, evento atrasado do Windows, navegação, restauração, seleção acessível de aparência, tabela/contexto, rolagem e binding. Renderizam as telas nativas para PNG, com janelas fora da área visível. O parâmetro `--preview-dir <pasta>` após `--` escolhe a pasta das prévias. Preferências do usuário são preservadas: os testes usam diretórios temporários próprios.

O harness também salva `obs-registration-fixture-light.png` com o cadastro expandido e `obs-settings-light.png` com Configurações. A captura de cadastro usa transporte HTTP sintético, sem dados pessoais nem acesso ao banco de uso. Essas prévias renderizam o conteúdo WPF. O harness também abre/renderiza o menu da conta e tooltips e confere a preferência de cantos pelo DWM; não comprova a moldura física em múltiplos monitores/DPI.

Na retomada de 2026-10-07, passaram **23/23** checks, incluindo acionamento dos botões por `ButtonAutomationPeer` e diálogo nativo de fechamento/cancelamento. O teste separado `tests/desktop-startup.ps1` abre os executáveis reais Release, exige janela principal com título correto e responsiva, e encerra somente os processos que criou. Esse teste reproduziu crash nos dois clientes antes da correção de P-008 e passou nos dois depois. A aplicação OBS também foi mantida aberta e seu processo confirmou `Responding=True`.

O helper de controle Windows retornou timeout ao iniciar o executável e depois indicou solicitação ativa mesmo após reset. A abertura real foi validada pelo processo e pelo teste de regressão; não houve validação completa de cliques/atalhos físicos, DPI ou múltiplos monitores (P-007). Prévias nativas desta retomada estão em `.build/previews-retomada`.

Na conferência complementar, a regressão de navegação falhou antes da correção de P-010 e passou depois: **24/24 checks**, incluindo navegação entre telas no topo nos dois perfis, com e sem animação, além da medição dos botões no diálogo de fechamento. Build integrado Release e teste de abertura dos dois executáveis atualizados passaram; prévias em `.build/previews-conferencia`. Uma tentativa de build encontrou a DLL bloqueada pelo OBS aberto na etapa anterior; encerrada somente essa instância de teste, a nova execução concluiu com zero erros/avisos.

## API

Execução e contratos estão em `api.md`. A solução integrada agora compila também `Nora.Api` e `Nora.Api.Checks`. A revisão integrada confirmou Release com zero erros/avisos e **20/20** verificações HTTP reais, sem banco ou autenticação simulados.

Para repetir os fluxos persistidos sem acessar bases de uso, compile Release e execute os harnesses com PowerShell7:

```powershell
pwsh -NoProfile -File tests/validar-postgres-descartavel.ps1
# Com emulador já iniciado e boot concluído:
pwsh -NoProfile -File tests/validar-clientes-descartavel.ps1 -Android -JavaHome 'C:/Program Files/Android/Android Studio/jbr'
```

O primeiro cria cluster PostgreSQL 18 e valida HTTP, migrações, autorização e fluxo. O segundo cria outro cluster/schema, inicia API com contas sintéticas, testa WPF pelos controles nativos e depois Android conectado com argumento `noraApiUrl`. Os scripts preservam logs em `.build/` e encerram somente instâncias próprias. Não executar concorrentes: metadata de fixture é compartilhada; o harness recusa uma já existente. Sem `-Android`, o segundo valida somente WPF. O JDK21 precisa ser explícito quando `JAVA_HOME` pessoal aponta para outra versão.

Em 2026-11-09, a solução Release compilou sem erros ou avisos. `validar-postgres-descartavel.ps1 -IncludeUnitDatabases` passou 28/28 fluxos PostgreSQL, mais a validação de bancos separados, incluindo autorização e auditoria da correção do e-mail. O teste WPF focado no cadastro e correção passou com API simulada. Em outra execução, o cadastro no OBS e o controlador Android passaram contra API e bancos descartáveis, usando e-mail simulado; o teste de seleção de UBS na interface Android passou separadamente. Isso não substitui o teste da jornada inteira na tela com envio real de e-mail. Evidências em `.build/postgres-validation-59dcfb79a9e24c289aee1f55496e77bd` e `.build/pg-disposable-518ec2452d6549c09b533ce1888c22c4`.

Na integração da agenda, Release passou sem erros ou avisos e o teste PostgreSQL descartável passou 28/28 fluxos e o teste separado de bancos por UBS. As verificações incluem agendamento concorrente, sobreposição, transições, confirmação e autoria do cancelamento, acesso do paciente apenas à própria consulta e recusa sem vínculo. O OBS passou no teste focado do calendário e criou, chamou e cancelou uma consulta pela API com banco descartável. Android compilou e seus testes locais passaram. Evidência PostgreSQL em `.build/postgres-validation-69d06333927847c5ba995da9e1ed3238`.

No emulador Android isolado, o teste integrado também cadastrou o paciente, consultou sua agenda na UBS vinculada, recusou acesso à UBS sem vínculo e cancelou a consulta pela API. Testes instrumentados separados confirmaram que a agenda é limpa ao trocar de UBS ou sair e que a interface pede confirmação antes de cancelar. O código de cadastro foi enviado por serviço de e-mail simulado; a jornada completa por toques com e-mail real ainda não foi validada.

A consulta da agenda pela Secretaria foi validada com API/PostgreSQL descartáveis, incluindo registro do acesso e recusa ao perfil OBS; o teste WPF com transporte simulado verificou carregamento e limpeza ao trocar de unidade. Em teste WPF com a API real de teste, a gestão também recebeu a consulta criada e cancelada no OBS. Evidência PostgreSQL em `.build/postgres-validation-abc3e573483d44319bf9558c3c9f9591`.

Em2026-10-08, baseline .NET Release zero erros/avisos, HTTP 20/20 e PostgreSQL 15/15. E2E WPF 34/34 passou. Bootstrap da DLL iniciada da raiz reproduziu porta 5000 antes da correção de content root; depois carregou appsettings do diretório da DLL, escutou 5080 e respondeu liveness real. Rodada final Android passou10/10 instrumentados sem skips, incluindo integração PostgreSQL, e7/7 testes locais reexecutados. Lint zero erros/um aviso de versão disponível. Limpeza preservou37 arquivos locais por hashes e retirou somente do Git; build final Release passou. Publicação no documento da retomada de2026-10-08.

## Android

O escopo e o estado atual das telas do aplicativo de pacientes estão em [App do paciente](app-paciente.md).

O projeto mantém AGP 9.4.1, Gradle 9.6, compile/target SDK 37 e min SDK 24. Usa Compose BOM 2026.09.00, plugin Compose Kotlin 2.4.10 e Activity Compose 1.13.0. O Java 21 instalado substituiu o critério Java 25 do template, cujo provisionamento via Foojay retornava HTTP 400. JDK 21 atende ao mínimo 17 documentado para AGP 9.4.

Abra `APPNORA` no Android Studio e configure o SDK local, ou defina `ANDROID_HOME` na sessão. Não versionar `local.properties`. Na sessão de validação, `JAVA_HOME` apontou para o Java 21 instalado e `GRADLE_USER_HOME` para `.build/gradle-user` do repositório.

```powershell
Set-Location APPNORA
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug :app:assembleDebugAndroidTest --console=plain
# Requer dispositivo/emulador conectado:
.\gradlew.bat :app:connectedDebugAndroidTest --console=plain
```

Artefatos: `APPNORA/app/build/outputs/apk/debug/app-debug.apk` e APK de testes em `APPNORA/app/build/outputs/apk/androidTest/debug/`. Relatórios de lint e testes locais ficam em `APPNORA/app/build/reports/`.

Na etapa inicial, build do app/testes, três testes locais e lint passaram; quatro casos instrumentados apenas compilaram e `connectedDebugAndroidTest` falhou com “No connected devices”. Essa evidência histórica não descreve o ambiente atual do notebook.

Na retomada de 2026-10-07, o AVD `Pixel_4` API31 iniciou com WHPX já instalado fora do sandbox. O comando integrado de build, testes locais, lint e `connectedDebugAndroidTest` concluiu com sucesso: **3/3 testes locais e 4/4 instrumentados**, zero falhas/erros/skips. Lint com zero erros e um aviso de versão de dependência disponível. Não foi preciso instalar driver nem alterar/reiniciar Windows. Relatório instrumentado em `APPNORA/app/build/outputs/androidTest-results/connected/debug/`.

Os testes instrumentados confirmam interação, rolagem e persistência em novas instâncias do armazenamento. A conferência extra de reabertura e captura de tela ficou limitada por crashes/ANRs do `System UI` do AVD, inclusive após reinicialização com dois núcleos e GPU software. Não foi registrado crash do processo NORA. Ainda faltam reabertura/render estáveis, tema Sistema, fonte ampliada e layout em telas maiores (P-003). O passo a passo para executar no Android Studio e criar um novo AVD está em `android-emulador.md`; o AVD existente não foi apagado.

## Galeria visual de 10/10/2026

As fotos atuais estão em `Documentos/Claude-HTML/Projeto-NORA/galeria/2026-10-10/index.html`, um HTML offline com imagens originais e fontes identificadas. São 68 renders WPF e 15 Compose/Layoutlib; não substituem E2E banco/dispositivo. A máquina Pichau desta rodada possui JDK21 em `C:/Program Files/Eclipse Adoptium/jdk-21.0.12.101-hotspot` e SDK em `C:/Users/Pichau/AppData/Local/Android/Sdk`. O JBR atual do Android Studio é Java25, portanto use o JDK21 explícito. PostgreSQL18 e aceleração do emulador não estão disponíveis aqui; a validação histórica no notebook mateu continua válida apenas como histórico.

```powershell
# Na raiz, depois de compilar os checks desktop:
dotnet .build/Nora.Desktop.Checks/bin/Release/net10.0-windows/Nora.Desktop.Checks.dll --preview-dir .build/galeria-visual-2026-10-10/desktop --visual-gallery
# Na pasta APPNORA com JAVA_HOME/ANDROID_HOME configurados:
.\gradlew.bat :app:updateDebugScreenshotTest :app:validateDebugScreenshotTest --console=plain
# De volta à raiz, exporta os previews para PNG legíveis e manifesto Android:
pwsh -NoProfile -File tests/exportar-capturas-android.ps1
# Com ambos os visual-captures.json e PNG disponíveis:
pwsh -NoProfile -File tests/gerar-galeria-visual.ps1
```

O harness desktop usa preferências temporárias e respostas HTTP sintéticas em memória; recusa capturar PasswordBox preenchido. Android `screenshotTest` contém os 15 previews de componentes de produção, com estados sintéticos sem rede. Os PNG de referência ficam localmente em `APPNORA/app/src/screenshotTestDebug/reference/`, ignorados no Git; o relatório fica em `APPNORA/app/build/reports/screenshotTest/`. `update` gera referências e `validate` compara; a comparação da referência recém-gerada confirma render consistente, sem substituir inspeção visual. Os PNG legíveis e os manifestos de origem são intermediários em `.build/galeria-visual-2026-10-10/{desktop,android}`. A galeria preserva as cópias sem edição e seus hashes. O teste instrumentado opt-in `VisualCaptureTest` compila para futuras capturas locais, não foi executado nesta máquina e não faz conexão HTTP.

Resultados: desktop39/39; Android15/15 renders/comparações,7/7 unitários, lint zero erros/um aviso de versão; APK e APK de testes compilados. Build integrado Release zero erros/quatro avisos NU1900 de consulta de vulnerabilidades bloqueada pela rede. A inspeção manual das imagens foi realizada antes da entrega. Configuração pessoal, AVD e bases de uso foram preservados.

## Fontes de compatibilidade

- [AGP 9.4 e requisitos de Gradle/JDK/SDK](https://developer.android.com/build/releases/agp-9-4-0-release-notes).
- [Kotlin integrado ao AGP](https://developer.android.com/build/migrate-to-built-in-kotlin).
- [Dependências e compilador Compose](https://developer.android.com/develop/ui/compose/setup-compose-dependencies-and-compiler).
- [AndroidX Activity](https://developer.android.com/jetpack/androidx/releases/activity).

## Ajustes OBS/Pacientes — 2026-10-11

Baseline atual: solução Release zero erros/avisos, 33/33 checks WPF e abertura 2/2 dos executáveis. Depois dos ajustes, a rodada integrada passou 43/43 checks WPF: telefone, filtros, presença sintética, atualização automática sem apagar seleção/rascunho ou impedir edição, erro/recuperação/401, menu Configurações/Sair, tooltips, zebra, contatos longos, rolagem e preferências DWM de janela restaurada/maximizada/tela cheia.

PostgreSQL18 descartável passou HTTP20/20 e fluxos18/18, incluindo telefone válido/inválido, compatibilidade de cadastros anteriores, migração reaplicável, presença por sessão, timeout/múltiplos dispositivos/logout e ordem recente. Evidência `.build/postgres-validation-59e0c47d0a054bf9bc452e1201d33182`; cluster próprio encerrado. A primeira tentativa foi limitada pelo socket loopback do sandbox e repetida com execução local autorizada.

O ciclo de diagnóstico usou testes vermelhos antes das correções: seleção na Fila, aviso após reconexão, input desabilitado durante polling, padding duplicado dos inputs e contraste do botão. A rodada final em área cliente real acrescentou a ausência de rolagem externa no tamanho mínimo e reproduziu perda do offset interno no polling sem mudanças. A altura da lista passou a reservar o espaço de título/filtros/cadastro; coleções iguais não são limpas/reinseridas e a lista filtrada só é notificada quando os registros mudam. Após essas correções, solução Release zero erros/avisos, 43/43 desktop sem E2E e abertura 2/2 passaram. A rodada com PostgreSQL acrescenta o check E2E (44 no total).

A última inspeção revelou rótulos do cadastro herdando preto do Expander; a regressão nova falhou em 43/44 sem E2E antes da correção. Expander passou a herdar TextBrush e o cadastro foi renderizado/conferido nos três temas. Rodada final WPF com PostgreSQL passou **45/45**, solução Release zero erros/avisos, evidência `.build/clients-validation-0198b1ae92f24cd1b30a367cd9ff0072`; cluster próprio encerrado. Prévias completas em `.build/previews-2026-10-11-final`, inclusive cadastro, zebra, menu, tooltip e configurações.

Android app/APK de testes, 7/7 locais e lint passaram (zero erros/um aviso já conhecido de versão disponível). A execução integrada Pixel_4 API31 passou **14/14 instrumentados, zero falhas/erros/skips**, incluindo HTTP real/PostgreSQL, heartbeat após um intervalo real em segundo plano, toque na notificação preservando Activity/ViewModel, remoção da tarefa e guard de geração/401. Evidência `.build/clients-validation-212d7f97123440a9aa3e84aebe7b7cec` e XML em `APPNORA/app/build/outputs/androidTest-results/connected/debug/`. Essa execução antecedeu somente a correção WPF do Expander; Android não foi alterado depois. P-003/P-007 preservam os limites de aparelhos, recriação de Activity, DPI/monitores e teclado físico.

## Listagens e desativação — concluídas em 2026-10-13

Release sequencial com `--disable-build-servers` passou com zero erros/avisos. Desktop52/52 sem E2E e53/53 com API/PostgreSQL descartáveis passaram: itens compartilhados, contatos completos/limite de e-mail, seleção/barra sobreposta, duplo clique/Enter, X/Escape, cópia isolada, três temas e confirmação de desativação/reativação. Abertura real OBS/Municipal2/2 passou. Prévias usuais e extremas em `.build/previews-2026-10-13-final` e `.build/previews-2026-10-13-e2e`.

`tests/validar-postgres-descartavel.ps1` passou HTTP20/20 e PostgreSQL23/23; migração004, autorização/decisão explícita, revogação, login/fila/presença bloqueados, preservação do histórico, avanço FIFO e corridas foram exercitados. Evidência `.build/postgres-validation-f45660f6faf04ce4bcaf8e6c253ebb27`. `tests/validar-clientes-descartavel.ps1 -PreviewDirectory .build/previews-2026-10-13-e2e` passou53/53; logs em `.build/clients-validation-b4ce79e39f9b4b7bab651fb422a04d0f`. Clusters próprios encerrados, sem base de uso ou contas pessoais.

Contraste da seleção e confirmação durante polling tiveram regressões focadas0/1 antes das correções. Um timeout posterior veio do pump do harness, que encerrava o frame acima da prioridade das continuações assíncronas de inspeção modal; SystemIdle permitiu essas continuações, sem alterar produção ou enfraquecer as assertions. Foco1/1 e suíte completa ficaram verdes. `--check-filter <trecho>` permite reproduzir o caso e falha se nenhum teste corresponde. Android não foi reexecutado nesta etapa Windows; limites físicos e provisionamento continuam nas pendências anteriores.

## Integração dos dois computadores — 2026-10-13

Preservados commit local f3ec187 e remotos3fa7e58/5396387/8b80b2a. Conflitos dos testes combinaram identidade da galeria, perfis reais e ações flutuantes; pendências locais P-017/P-018 passaram a P-019/P-020 para manter os registros remotos. Caso da galeria OBS falhou57/58 e1/2 focado por atribuir o mesmo item já selecionado ao retornar à Fila; harness passou a refazer seleção e exigir ações antes do cancelamento. Nenhuma mudança de produção foi necessária nessa correção.

Após integração, Release0/0, desktop58/58 com `--visual-gallery` e55/55 com API/PostgreSQL passaram. Evidência E2E `.build/clients-validation-556a01fb04ca4ee3a3ab8bcd4d8f163f`; API não mudou em relação ao código já validado HTTP20/20 e PostgreSQL23/23. Galeria histórica83/83 PNG conferidos por SHA256, sem edição;68 renders atuais adicionais em `.build/previews-2026-10-13-integrated`. [Imagens atuais de pacientes/detalhes](galeria/2026-10-13/README.md) foram copiadas sem edição.

Android foi reexecutado após o merge: assembleDebug/assembleDebugAndroidTest, unitários7/7, screenshot update/validate15/15 sem skips e lint zero erros/um aviso de versão Kotlin. JDK21 do Android Studio, SDK local e GRADLE_USER_HOME dentro de .build; nenhuma atualização global de ferramenta. Não se repetiu connectedDebugAndroidTest nesta integração. `dotnet restore NORA.slnx --force-evaluate --no-cache` e `dotnet list NORA.slnx package --vulnerable --include-transitive --no-restore` passaram: fontes acessíveis, auditoria habilitada e nenhuma vulnerabilidade conhecida informada; P-017 resolvida.

## Atualização das fotos da galeria — 2026-10-13

O site fornecido corresponde ao HTML histórico de83 itens. `tests/atualizar-fotos-galeria.ps1` preserva CSS/JavaScript/ordem/rótulos e troca somente as68 fotos Windows afetadas pelo shell/listagens, atualizando origem/dimensões/hashes e datas; Android15 permanece idêntico. Destino em `Documentos/Claude-HTML/Projeto-NORA/galeria/2026-10-13-atualizada`. O script valida todas as fontes antes da cópia, restringe caminhos ao projeto e protege a árvore histórica e a origem.

Recaptura pelo harness: `dotnet .build/Nora.Desktop.Checks/bin/Release/net10.0-windows/Nora.Desktop.Checks.dll --preview-dir .build/gallery-updated-2026-10-13/desktop --visual-gallery`. As fotos1280×820 excediam o cliente real nesta resolução e tinham rodapé transparente; guard PNG/alpha reproduziu0/2 antes da correção. A galeria agora usa1280×740, mantém980×640 nos quatro casos mínimos e registra dimensões efetivas. Não há edição de bitmap; apenas render nativo correto. Gerador: `pwsh -NoProfile -File tests/atualizar-fotos-galeria.ps1`, com `-SourceHtml` opcional para o HTML fornecido.

Durante a recaptura, seletor da Fila foi reproduzido mostrando record interno. Usuário autorizou correção mínima: ContentTemplateSelector repassa ItemTemplateSelector; regressões3/6 antes/6/6 depois e seis fotos Fila revisadas. Rodada final Release0/0, desktop64/64 com galeria,61/61 com API/PostgreSQL e abertura2/2 passou. E2E em `.build/clients-validation-4ad1b014808946d89886921a6124d350`, sem contas pessoais/base de uso. Browser Playwright14/14 passou após fotos finais;83 PNG/hash/dimensões e15Android preservados conferidos. Downloads recebeu HTML/assets/ZIP, com backup do original. Detalhes em `tarefas/2026-10-13-atualizar-fotos-galeria.md`; P-021/P-022 resolvidas.
