# Arquitetura da primeira etapa

Estado em 2026-11-15: os três clientes usam só o fluxo por UBS (o legado global foi removido deles e as rotas antigas ficam bloqueadas na API), cada UBS tem banco criado pela própria API, e a API calcula score e indicadores. As seções abaixo mantêm o texto de etapas anteriores; as seções "Banco de cada UBS", "Score e indicadores" e "Estrutura dos clientes", no fim, descrevem o estado atual e prevalecem onde houver divergência.

## Aplicativos e responsabilidades

`APPNORA` é o cliente Android Kotlin/Jetpack Compose. `MainActivity` inicializa `SettingsStore`, mantém `PatientController` em `AndroidViewModel` apenas em memória e apresenta `NoraApp`; `NoraContent` organiza Início e Configurações, com retorno nativo e estados de rolagem independentes. `preferences/UserSettings.kt` define os valores locais, `SettingsStore.kt` encapsula persistência e estado observável, e `ui/NoraTheme.kt` aplica as paletas e a aparência das barras do sistema.

`OBS-NORA` e `Painel-Municipal` são executáveis WPF .NET 10. Cada `App` instancia diretamente `Nora.Desktop.Views.ShellWindow` com seu perfil e a atribui a `Application.MainWindow`. A herança anterior em outra assembly impedia o carregamento do XAML na abertura dos executáveis; foi removida após reprodução nos dois clientes (P-008). As soluções individuais continuam válidas. `NORA.slnx` reúne os dois executáveis, a biblioteca, a API e os harnesses de verificações.

`Nora.Desktop` concentra as responsabilidades comuns:

- `Models`: preferências, perfis e descritores de navegação/cards/linhas.
- `ViewModels`: estado e comandos do shell; navegação, seleção e configuração.
- `Views`: shell, Home e Configurações; integração visual de teclado, animação e confirmação de fechamento.
- `Services`: persistência JSON, temas do Windows e tela cheia.
- `Generics`: notificação de propriedades e comando reutilizável.
- `Themes`: paletas Clara/Escura e templates compartilhados de controles.

A API usa PostgreSQL 18 via Npgsql10.0.3. Models define contratos; Services valida contas/autenticação/fila; DAOs usa SQL parametrizado e transações; Database contém helper e migrações aditivas incorporadas. Sessões revogáveis são persistidas pelo hash do token. WorkflowDao serializa oferta/resposta/expiração com advisory lock PostgreSQL. OfferExpirationWorker verifica prazo a cada15s; operações também verificam antes de responder. Nenhuma interface acessa banco diretamente. Contratos em `api.md`.

`Nora.Desktop/Services/NoraApiClient.cs` encapsula HTTP e guarda token somente em memória. `WorkspaceViewModel` publica dados após recarga completa, controla busy/erros e limpa dados ao sair ou receber401; WorkspaceView contém login, cadastro, pacientes/fila/vagas e Municipal agregado. O endpoint pode vir de NORA_API_URL ou da tela; senhas não são preferências.

`WorkspaceViewModel.Listings.cs` concentra seleção contextual, filtro de cadastro, mutação de acesso e composição de fichas `ItemDetails`/`DetailField`. `ItemDetailsViewModel` controla cópia explícita e feedback através de `IClipboardService`; a implementação Windows usa o clipboard do sistema. Views identificam linha/duplo clique e apresentam ficha modal; Themes mantém o template de item e a barra flutuante. Presença da Fila usa o paciente recebido na mesma recarga, sem consultas por linha. A listagem operacional de pacientes não é enviada ao Municipal; a consulta nominal de referência por UBS possui rota própria para a gestão.

A migração 004 adiciona estado de conta. `WorkflowDao` aplica desativação e avanço FIFO em uma transação sob advisory lock; `SessionDao` revalida acesso antes de criar sessão, e operações do paciente revalidam sessão sob o lock da fila. Reativação não recupera sessões nem entradas canceladas. Os contratos acrescentam estado/data ao paciente; telefone e presença anteriores continuam compatíveis.

Android usa `api/NoraApi.kt` como boundary HTTP, PatientController para estado/busy/erros e ApiAddress para validar HTTPS. A interface recebe estado/callbacks sem SQL. Endpoint validado é preferência local; identidade/token só memória. Release permite HTTPS, debug permite HTTP apenas no host emulador10.0.2.2. Ambos desativam redirecionamentos HTTP automáticos.

## Identidade e temas

Os desktops consomem tokens por `DynamicResource`. `ThemeService` troca o dicionário da paleta, lê `AppsUseLightTheme` no Windows e observa mudanças de preferências do sistema. O callback revalida a opção escolhida ao executar no Dispatcher, preservando uma escolha manual feita enquanto o evento estava enfileirado. A assinatura do evento é encerrada com a janela.

No Android, `NoraTheme` combina a opção persistida com `isSystemInDarkTheme()`, aplicando Material 3 e contraste coerente. A seleção de aparência possui semântica de radio button. A mudança também atualiza a cor dos ícones das barras do dispositivo.

## Limites da fundação

OBS cadastra pacientes e opera fila/ofertas por entrada. Android mostra apenas acompanhamento da própria conta e responde à oferta. Municipal consulta totais agregados e, sob solicitação, cadastros de referência e agenda da UBS selecionada. Estados vazios e indisponibilidade são explícitos. Indicadores e séries por UBS e por rede existem; push por servidor e a avaliação dos alertas ainda não. Não há gráficos fictícios.

Preferências de notificação Android são locais, sem envio externo. Logout funciona para contas conectadas; reabrir exige login. A base definitiva e suas contas aguardam configuração privada PostgreSQL (P-012). Testes usam schemas aleatórios em instância descartável, sem modificar bases de uso.

## Painel operacional da UBS

O perfil operacional do OBS organiza Painel da UBS, Fila, Agenda, Pacientes, Vaga Relâmpago, Indicadores e Alertas. `OperationalOverviewView`, `AgendaView` e `OperationalInsightsView` formam as superfícies novas; `WorkspaceView` continua sendo a fronteira dos dados e ações HTTP existentes. A agenda e os indicadores usam os dados da UBS confirmada; os alertas listam critérios e ainda não são avaliados.

O usuário autenticado aparece pela identidade retornada na sessão, com avatar de iniciais. O vínculo e a filtragem real da unidade ainda pertencem ao servidor: a API possui cadastro de unidades e vínculos de contas, as rotas atuais operam por UBS; rotas globais antigas estão bloqueadas por padrão. Detalhes e próximos passos estão em `obs-painel-operacional.md`.

## Arquivos locais e build

`Directory.Build.props` centraliza os outputs .NET em `.build/<projeto>/`, excluindo artefatos antigos da compilação. `.gitignore` cobre novos outputs, sessões locais do compilador Kotlin em `.kotlin/`, estado de IDE e segredos. Os 37 arquivos `obj/`, `.vs/` e `*.csproj.user` antes rastreados foram retirados somente do índice Git em 2026-10-08. As cópias locais foram preservadas e seus hashes conferidos; `.gitignore` evita reintrodução (P-002 resolvida).

A migração `003_patient_contact_presence.sql` acrescenta telefone opcional em accounts e presença recente por sessão. A API calcula `isOnline` com o relógio do servidor e o estado de autenticação; o OBS nunca infere presença pela existência do cadastro. `WorkspaceViewModel` controla atualização periódica de 20 segundos com exclusão de operação concorrente e encerra o timer ao sair/fechar. Views/Themes concentram a listagem e controles arredondados, mantendo HTTP em NoraApiClient e SQL nos DAOs.

No Android, `NoraApp` observa lifecycle STARTED e sessão para iniciar `PatientPresenceService` quando a tela está visível; deixar o estado visível não encerra o serviço. O serviço foreground não exportado mantém heartbeat de 30 segundos e notificação fixa, retorna START_NOT_STICKY e encerra no logout ou callback de remoção da tarefa. `PatientController` serializa presença com mutex e geração, preserva somente a sessão corrente e ignora respostas antigas que poderiam repor dados após401. O controller retido pela Activity conserva identidade somente em memória em rotação; token não é enviado em Intents nem salvo. O offline final é uma tentativa com timeout, com prazo de presença da API como garantia em perda de conexão/processo. Esta política de segundo plano foi confirmada pelo usuário em 2026-10-11.

## Gestão / Secretaria

O perfil municipal usa `MunicipalView` para navegar pelas seções de gestão. Os valores disponíveis continuam vindos de `WorkspaceViewModel` e do endpoint agregado existente; a interface identifica explicitamente filtros, indicadores e ações ainda não fornecidos pela API. Os detalhes, limites e dependências estão em `secretaria.md`.

O resumo municipal inclui totais de vagas e estados das ofertas existentes no banco. `MunicipalCsv` transforma o resumo recebido em CSV agregado, e a tela de Relatórios permite escolher o destino; campos ausentes em APIs antigas permanecem indisponíveis. Nenhuma confirmação clínica é inferida de um aceite.

A Secretaria seleciona localmente as coortes de vagas agregadas retornadas pela API; o CSV utiliza a mesma seleção e identifica o escopo distinto dos totais atuais de pacientes e fila. O servidor calcula as janelas a partir de `slots.created_at` e classifica as ofertas associadas pelo estado atual.

`NetworkMapView` usa o componente WPF Microsoft WebView2 para abrir o mapa público incorporável do OpenStreetMap somente após ação do usuário. A navegação principal é limitada à página do mapa e novas janelas são bloqueadas. A tela não repassa dados de pacientes; utiliza as coordenadas cadastradas pela Secretaria para mostrar a unidade selecionada.

O cadastro de UBS usa `HealthUnitService` e `HealthUnitDao`, com validação e rotas autenticadas para o escopo municipal; a migração 007 persiste nome, região, endereço e coordenadas. `WorkspaceViewModel` mantém a lista e o formulário em memória e limpa ambos no logout. O mapa recebe apenas as coordenadas da unidade selecionada, sem enviar nome, endereço ou pacientes ao provedor. A Secretaria pode conceder e revogar vínculos de contas OBS às UBS.

O paciente autenticado pode consultar as unidades vinculadas ao seu cadastro em `/api/patient/units`, usando a tabela `patient_units` da migração 008. O cadastro inicial confirma vínculos após verificar CPF e e-mail; a edição posterior ainda não foi implementada. O Android carrega as unidades ao abrir UBS, permite selecionar uma unidade, mostra nome, região e endereço e abre as coordenadas no mapa. Oferece atualização e limpa lista e seleção ao encerrar a sessão. Esse vínculo não estabelece isolamento operacional das contas OBS.

## Preparação de bancos por unidade

`UnitDatabaseRouter` obtém conexões somente de configuração privada no servidor e verifica `nora_unit.identity` ao abrir cada banco. `UnitMigrationDao` inicializa a identidade da UBS numa migração explícita separada do banco central; o servidor recusa um banco inicializado para outra UBS ou que contenha o schema central configurado. Os recursos de migração central e de UBS são filtrados separadamente. `UnitPatientRegistryDao` mantém o cadastro de referência e registra o autor em transação na base da unidade; as novas rotas OBS exigem concessão para a unidade e mantêm um bloqueio da concessão durante a operação, para coordenar revogação. O OBS consome essas rotas no cadastro de referência. A criação automática de bancos existe desde 2026-11 (ver "Banco de cada UBS"); a migração de cadastros globais antigos para as UBS continua fora do escopo. Testes em PostgreSQL descartável verificaram duas bases diferentes e a rejeição de identidade incorreta.

A gestão consulta o cadastro de referência de uma UBS pelo banco associado à unidade. `UnitAccessService` verifica a função municipal, confirma a unidade e grava uma ocorrência de consulta antes de retornar dados. A tela limpa os registros ao selecionar outra UBS ou sair.

## Agenda por UBS

A migração de UBS 005 persiste consultas e histórico de transições em cada banco. `AppointmentDao` impede horários sobrepostos para o mesmo paciente ou profissional com bloqueio transacional e mantém eventos de autoria. `UnitAccessService` aplica vínculo ao OBS e ao paciente; a consulta do paciente usa o CPF obtido da conta autenticada. A tela do OBS permite listar, criar e alterar situações; o Android permite consultar a própria agenda da UBS vinculada. A agenda se conecta às novas rotas de fila e vagas por UBS; o fluxo global antigo está bloqueado por padrão.

## Fila e ofertas por UBS

`UnitWaitingListDao`, `UnitLightningDao` e `UnitLightningDispatcher` mantêm fila, vagas e ofertas no banco da UBS; vínculos e estado de contas são conferidos no banco central. O processamento serializa a distribuição por unidade, seleciona a mesma especialidade por ordem de entrada, mantém dez minutos de prazo e registra aceite como consulta. O OBS e o Android usam as rotas por UBS. `UnitNetworkSummaryDao` agrega as unidades para a Secretaria, com CPF contado uma vez e opção de filtro por unidade. As rotas globais antigas são bloqueadas por padrão e o worker antigo só pode ser habilitado em Development para testes.

## Banco de cada UBS — 2026-11-15

`POST /api/municipal/units` grava a UBS e `UnitDatabaseProvisioner` cria o banco `<prefixo><id sem hífens>` (prefixo `Nora:UnitDatabases:NamePrefix`, padrão `nora_ubs_`), aplica as migrações de UBS e marca `health_units.database_ready_at`; o usuário PostgreSQL da API precisa da permissão `CREATEDB`. O banco nasce sem `CONNECT` para PUBLIC. Falha na criação deixa a UBS `pending` (fora do catálogo público, do resumo e das ofertas), com nova tentativa idempotente. A chave explícita `Nora:UnitDatabases:<id>` continua valendo e tem precedência. Cada UBS com banco derivado tem pool Npgsql próprio (`MaxPoolSize` padrão 3, mínimo 0, ociosidade de 60 s), e o PostgreSQL precisa comportar `pool central + nº de UBS × MaxPoolSize + folga` conexões (fórmula e exemplo em `api.md`).

`nora_unit.schema_version` guarda o maior número de migração aplicado (hoje 011); um banco desatualizado faz as rotas da UBS responderem 503 `unit-database-outdated` até a migração. `Nora.Api --migrate-all-units` atualiza todas as UBS `ready` e `scripts/iniciar-local.ps1` o executa depois de `--migrate`; o primeiro deploy desta versão deve rodá-lo antes de receber tráfego. Cada executor de UBS revalida conta e sessão dentro da transação, e as rotas autenticadas têm limites por conta (`ForwardedHeaders` só com proxy declarado).

## Score e indicadores

`Nora.Api/Services/AttendanceScore.cs` define o score de comparecimento: `concluídas ÷ (concluídas + faltas + soma dos pesos dos cancelamentos)`, nulo quando o denominador é zero. `CancellationWeight` converte a antecedência do cancelamento em fração de falta, cada faixa incluindo o limite inferior: menos de 1 h = 1,0; 1 h a 2 h = 0,9; 2 h a 6 h = 0,8; 6 h a 12 h = 0,6; 12 h a 24 h = 0,4; 24 h a 48 h = 0,2; 48 h a 72 h = 0,1; 72 h a 96 h = 0,02; 96 h ou mais = 0; antecedência negativa pesa como falta. Só pesam cancelamentos feitos pelo paciente no app, identificados por `appointment_events.source = 'patient'` (migração de UBS 010); consultas agendadas ou chamadas e cancelamentos da equipe não entram. O período é pela data marcada, e o score por paciente usa os últimos 12 meses.

`UnitMetricsDao` e `MetricsService` calculam, por UBS e para a rede, contagens por situação, espera média (da entrada na fila até a oferta aceita), série diária, quebras por UBS, região e especialidade e vagas do período, sem dado pessoal. `HistoryDao` une os eventos de acesso, contas e bancos no histórico paginado por cursor. O score por paciente sai agrupado na lista do cadastro de referência do OBS e na rota do próprio paciente. Contratos em `api.md`.

## Estrutura dos clientes

`WorkspaceViewModel` foi dividido em classes parciais por assunto: `.Account` (sessão e troca de senha), `.Alerts` (critérios de alerta), `.Appointments`, `.History`, `.Lightning`, `.Listings` (fichas), `.Messages` (faixa única de retorno), `.Metrics` e `.UnitMetrics` (indicadores da rede e da UBS), `.ObsUnits`, `.Staff`, `.UnitData` e `.Units`. Views novas do desktop: `AlertCriteriaView`, `ChangePasswordView`, `PasswordChangeRequiredView`, `EvolutionChartView` (série diária), `UnitIndicatorsView`, `Network*View` (filtros, indicadores, especialidades e desempenho por UBS), `ObsUnit*View` (escolha, aviso e cadastro da UBS), `HistoryView`, `Staff*View` (contas OBS), `ScoreNoteView` e `UnitQueueView`/`UnitLightningSlotsView`/`AgendaView`. Serviços novos: `Confirmation` e `BrazilianDataValidator`.

No Android, `api/ApiMessages.kt` traduz status e `code` do `problem+json` em mensagens por contexto; `api/PatientLogic.kt` concentra regras puras (próxima consulta, ofertas abertas, score); `api/LightningNotifier.kt` dispara a notificação local; `PatientPresenceService` mantém presença e consulta ofertas no mesmo ciclo de 30 s. Telas separadas em `OffersUi`, `AppointmentsUi`, `QueueUi`, `AttendanceUi`, `JourneyUi`, `PasswordUi`, `UnlinkUi` e `UnitsMap`.
