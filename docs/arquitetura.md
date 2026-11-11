# Arquitetura da primeira etapa

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

OBS cadastra pacientes e opera fila/ofertas por entrada. Android mostra apenas acompanhamento da própria conta e responde à oferta. Municipal consulta totais agregados e, sob solicitação, cadastros de referência e agenda da UBS selecionada. Estados vazios e indisponibilidade são explícitos. Notificações externas e séries/filtros analíticos adicionais ainda não foram implementados; não há gráficos fictícios.

Preferências de notificação Android são locais, sem envio externo. Logout funciona para contas conectadas; reabrir exige login. A base definitiva e suas contas aguardam configuração privada PostgreSQL (P-012). Testes usam schemas aleatórios em instância descartável, sem modificar bases de uso.

## Painel operacional da UBS

O perfil operacional do OBS organiza Painel da UBS, Fila, Agenda, Pacientes, Vaga Relâmpago, Indicadores e Alertas. `OperationalOverviewView`, `AgendaView` e `OperationalInsightsView` formam as superfícies novas; `WorkspaceView` continua sendo a fronteira dos dados e ações HTTP existentes. A agenda utiliza os dados da UBS confirmada; indicadores e alertas continuam identificando séries e operações que a API ainda não fornece.

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

`UnitDatabaseRouter` obtém conexões somente de configuração privada no servidor e verifica `nora_unit.identity` ao abrir cada banco. `UnitMigrationDao` inicializa a identidade da UBS numa migração explícita separada do banco central; o servidor recusa um banco inicializado para outra UBS ou que contenha o schema central configurado. Os recursos de migração central e de UBS são filtrados separadamente. `UnitPatientRegistryDao` mantém o cadastro de referência e registra o autor em transação na base da unidade; as novas rotas OBS exigem concessão para a unidade e mantêm um bloqueio da concessão durante a operação, para coordenar revogação. O OBS consome essas rotas no cadastro de referência. Não há criação automática de bancos ou migração de cadastros legados; fila e vagas antigas ainda não consomem a infraestrutura. Testes em PostgreSQL descartável verificaram duas bases diferentes e a rejeição de identidade incorreta.

A gestão consulta o cadastro de referência de uma UBS pelo banco associado à unidade. `UnitAccessService` verifica a função municipal, confirma a unidade e grava uma ocorrência de consulta antes de retornar dados. A tela limpa os registros ao selecionar outra UBS ou sair.

## Agenda por UBS

A migração de UBS 005 persiste consultas e histórico de transições em cada banco. `AppointmentDao` impede horários sobrepostos para o mesmo paciente ou profissional com bloqueio transacional e mantém eventos de autoria. `UnitAccessService` aplica vínculo ao OBS e ao paciente; a consulta do paciente usa o CPF obtido da conta autenticada. A tela do OBS permite listar, criar e alterar situações; o Android permite consultar a própria agenda da UBS vinculada. A agenda se conecta às novas rotas de fila e vagas por UBS; o fluxo global antigo está bloqueado por padrão.

## Fila e ofertas por UBS

`UnitWaitingListDao`, `UnitLightningDao` e `UnitLightningDispatcher` mantêm fila, vagas e ofertas no banco da UBS; vínculos e estado de contas são conferidos no banco central. O processamento serializa a distribuição por unidade, seleciona a mesma especialidade por ordem de entrada, mantém dez minutos de prazo e registra aceite como consulta. O OBS e o Android usam as rotas por UBS. `UnitNetworkSummaryDao` agrega as unidades para a Secretaria, com CPF contado uma vez e opção de filtro por unidade. As rotas globais antigas são bloqueadas por padrão e o worker antigo só pode ser habilitado em Development para testes.
