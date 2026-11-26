# MAPA DO CÓDIGO ATUAL — desktops NORA (somente leitura)

Raiz = worktree desktop-redesign-ui. Todos os caminhos são relativos a ela. Quase toda a UI vive em `Nora.Desktop`. `OBS-NORA` e `Painel-Municipal` são só a casca de partida (cerca de 25 linhas de C# cada). Não rodei build nem testes. Contagens e comportamentos vêm da leitura do código.

Convenções: "VM" = `Nora.Desktop/ViewModels`. `WorkspaceViewModel` é uma classe parcial com 20 arquivos. Toda tela herda `DataContext = WorkspaceViewModel`, exceto a ficha, Configurações e o shell. Os números de linha foram conferidos por leitura direta ou grep.

---

## 1. Shell da janela

**Compartilhado:** é um só `ShellWindow` em `Nora.Desktop`, usado pelos dois apps. O que muda é o `DesktopProfile`.
- `OBS-NORA/App.xaml.cs:12` faz `new ShellWindow(DesktopProfile.Operational, devices: new ObsDeviceStore(), compactAccess: true)`.
- `Painel-Municipal/App.xaml.cs:38` faz o mesmo com `DesktopProfile.Municipal`, usando a pasta `%LOCALAPPDATA%\NORA\municipal`.
- Os dois `App.xaml` são vazios (1 linha).
- O `ThemeService` injeta `Themes/Controls.xaml` e o tema claro ou escuro nos recursos da aplicação (`Nora.Desktop/Services/ThemeService.cs:17,28-36`).

**Barra de título: é a nativa do Windows, não há WindowChrome próprio.**
- `Nora.Desktop/Views/ShellWindow.xaml:1`: a janela usa o estilo padrão, com `Title="{Binding Profile.Name}"`. Os textos "NORA OBS" e "NORA Municipal" vêm de `Nora.Desktop/Models/DesktopPreferences.cs:22-23`.
- Os botões minimizar, maximizar e fechar são os do Windows. Não existe `WindowChrome` em nenhum arquivo.
- Cantos arredondados vêm do DWM do Windows 11 (`Nora.Desktop/Views/ShellWindow.xaml.cs:121-130`).
- A tela cheia (F11) troca para `WindowStyle.None` em `Nora.Desktop/Services/FullScreenService.cs:21-38`.
- Tamanho inicial 1320x860, mínimo 980x640 (`ShellWindow.xaml:1`).
- Confirmação ao fechar ("Fechar NORA OBS?"): `ShellWindow.xaml.cs:158-162`, controlada pela preferência `ConfirmClose`.
- Os diálogos filhos (`NoraDialog`, `IndicatorChartWindow`, `ItemDetailsWindow`) usam `WindowStyle="None"` com `AllowsTransparency` e têm cabeçalho próprio (`NoraDialog.xaml:1`, `IndicatorChartWindow.xaml:1`, `ItemDetailsWindow.xaml:1`).

**Barra lateral** (`Nora.Desktop/Views/ShellWindow.xaml`):
- Coluna de largura variável: `:13` (limites 208 a 380), com `GridSplitter` em `:99`. A largura persiste em `ShellViewModel.cs:55-75`.
- Fundo `SidebarGradientBrush` (`:17`).
- Marca (`:22-26`):
  - Imagem `BrandMarkImage`, que troca com o tema.
  - Texto fixo "NORA".
  - Linha de baixo `{Binding WorkspaceName}`: "OPERAÇÃO UBS" ou "GESTÃO / SECRETARIA" (`ShellViewModel.cs:23`).
- "Voltar ao menu" (`:27-29`), visível só em submenu. O comando é `MainMenuCommand` (`ShellViewModel.cs:173`).
- Rótulo de seção `SidebarHeading` (`:30`), definido em `ShellViewModel.cs:153` (nome da área ativa em maiúsculas, ou `NavigationHeading`: "OPERAÇÃO DA UNIDADE" / "GESTÃO DA REDE").
- Lista de itens: `ItemsControl x:Name="SidebarList"` em `:54-96`, ligado a `SidebarItems` (`ShellViewModel.cs:152`).
  - Cada item é um `Button` com o estilo `NavItemButton`.
  - Ícone: glifo "Segoe MDL2 Assets", ou um `Path` vetorial só para Unidades, Especialidades e Relatórios (`ShellWindow.xaml:3-5`, gatilhos em `:73-92`).
  - Distintivo numérico (`:69`) e seta de submenu (`:70`).
  - A animação de deslize ao trocar de nível está em `ShellWindow.xaml.cs:59-66`.
- Rodapé (`:32-53`):
  - Divisor.
  - Botão "Configurações" (`:34`, rota `settings`).
  - Linha do perfil: `AvatarButton` em `:39` (clique abre o menu de foto) e `AccountButton` em `:42`. Ela mostra `Workspace.AccountName` e `Workspace.UnitLabel`.
- Estilos dos itens de navegação: `Themes/Controls.xaml:506-535` (`NavItemButton`, `NavBackButton`).
- Item selecionado: `Tag=IsSelected` e uma barrinha lateral ("Accent") no template do estilo.
- O estado `SidebarExpanded` (`ShellViewModel.cs:54`) só é persistido. Nenhuma view o liga ao layout. O atalho Ctrl+B apenas alterna esse valor (`ShellWindow.xaml.cs:144`). Não há recolhimento real da barra.
- A barra e o divisor somem quando `ShowPasswordRequired` ou `ShowCompactAccess` são verdadeiros (`ShellWindow.xaml:18,100,104`).

**Cabeçalho de cada tela** (`ShellWindow.xaml:111-141`, `Border x:Name="PageHeader"`, altura 56):
- Seta de voltar: `BackButton` em `:116`, estilo `CircleIconButton`, glifo E72B, comando `BackCommand` (`ShellViewModel.cs:117`), tooltip `BackTip`. Atalho Alt+←: `ShellWindow.xaml.cs:135`.
- Título: `{Binding PageLabel}` em `:117`.
- Chip à direita: `:118` mostra `Workspace.UnitLabel` com bolinha verde se autenticado.
  - No OBS, `UnitLabel` é o nome da UBS ativa ("Nenhuma unidade selecionada" ou "Unidade não conectada").
  - Na Secretaria, "Gestão conectada" ou "Gestão desconectada".
  - Definição: `WorkspaceViewModel.cs:131-133`.
- Botão "Filtros": `FiltersButton` em `:119-128`, `Visibility="Collapsed"` por padrão. O popup `FiltersPopup` fica em `:129-139` (largura 450, altura máx. 440).
- Barra de progresso fina: `:142`.
- O cabeçalho some em `ShowCompactAccess` (`:112`).

**Faixas abaixo do cabeçalho:**
- Faixa de retorno (sucesso, erro, info): `:145-168`.
- Aviso de dados desatualizados: `:169-175`.
- Rodapé de erro de preferências: `:196`.

**Navegação** (`ShellViewModel.cs`):
- Rotas são strings `"página"` ou `"página:subaba"`. `Navigate` está em `:182-208`, `SplitRoute` em `:167`, `Route` em `:155`.
- Dois níveis: área com `Children` (`NavigationItem`, `Models/DesktopPreferences.cs:30-50`). `ActiveArea` e `SidebarItems` em `:149-152`.
- Histórico: lista `history` (máx. 50) em `:145,194-197`. `GoBack` em `:175`. `CanGoBack` em `:154`.
- **Não existe "avançar".** Não há pilha de avanço, nem botão, nem atalho. O grep por forward/avançar não achou nada.
- Montagem do menu: `BuildOperationalNavigation` em `:212-234`, `BuildMunicipalNavigation` em `:235-257`.
- **Título do cabeçalho:** `PageLabel` (`:32`) vem de `RouteTitle(Route)` (`:160-166`), que usa `NavigationItem.Title` do item da rota. Casos especiais: "Configurações" e "Definir nova senha".
- `SectionSummary` (`:33`) existe com subtítulos por rota, mas **nenhum XAML o liga**.
- Os títulos dentro das telas são independentes:
  - OBS: `PageTitle` fixo em cada `.xaml`, e `WorkspaceViewModel.PageTitle` (`WorkspaceViewModel.cs:182`).
  - Secretaria: `MunicipalView.Heading` (`MunicipalView.xaml.cs:60`).
- **Troca de conteúdo:** `ShellWindow.xaml:178-183` tem `PageContainer`, com `SubPage.Current` ligado à subaba.
  - Contém `HomeView` (`:180`) e `SettingsView` (`:181`).
  - `HomeView` (`Views/HomeView.xaml:14-31`) escolhe o `DataTemplate` por gatilhos de dados (templates em `:3-11`):
    - Secretaria: `MunicipalView`.
    - OBS: `OperationalOverviewView`, `AgendaView`, `OperationalInsightsView`, `UnitSettingsView`, `AnnouncementsView`, `FamilyView`.
    - Padrão: `WorkspaceView` (contém Pacientes e Vaga Relâmpago).
    - Acesso exigido: `OperationalAccessView`.
- Subabas por visibilidade: `Views/SubPage.cs:8-37`. É uma propriedade anexada: `SubPage.Current` na janela e `SubPage.Key="list,register"` nas partes.
- Visibilidade de seções: `WorkspaceViewModel.cs:134,145-152`.
- **Login:** nos apps reais `compactAccess:true` (`ShellWindow.xaml.cs:28-32`). A janela mostra `DesktopAccessView` (`ShellWindow.xaml:191-195`, host `DesktopAccessHost`), sem barra nem cabeçalho (`ShellViewModel.Access.cs:6`). O login embutido em `WorkspaceView.xaml:19-80` só aparece fora desse modo (testes e prévias).

---

## 2. Menu do perfil

- Montado em XAML, num `ContextMenu` dentro do `AccountButton`: `Nora.Desktop/Views/ShellWindow.xaml:42-51`. Estilo `NoraContextMenu` (`Themes/Controls.xaml:576`). O estilo de `MenuItem` está em `:587`.
- Abre acima do botão: `OnAccountMenu` em `ShellWindow.xaml.cs:87-93` (`PlacementMode.Top`).
- Itens (`ShellWindow.xaml:45-47`):
  - "Configurações", `NavigateCommand` com `settings`.
  - "Dados do perfil", `Click=OnOpenProfile` (`ShellWindow.xaml.cs:151`), habilitado se autenticado. Abre `StaffProfileWindow`: nome, e-mail, função, salvar nome e `ChangePasswordView` (`Views/StaffProfileWindow.xaml:1-22`, `.cs:12`).
  - "Sair da conta", `Workspace.LogoutCommand` (`WorkspaceViewModel.cs:78`, `LogoutAsync` em `:293-303`).
- O botão do avatar tem outro menu, montado em código (`Views/AccountAvatar.xaml.cs:21-30`): "Alterar foto" e "Remover foto". É chamado por `ShellWindow.xaml.cs:72`, e também por `SettingsView.xaml:57` e `SettingsView.xaml.cs:16`.

---

## 3. Seleção e troca de UBS no OBS

**Controle:** `Views/ObsUnitPickerView.xaml` (13 linhas) e `.cs` (33 linhas).
- Texto-guia `ObsUnitPrompt` (`:4`).
- `ComboBox` de `ObsUnits` com `CandidateObsUnit` e `SelectorLabel` (`:6`).
- Atualizar lista: `IconRefreshButton` em `:8`.
- "Confirmar UBS" em `:9`, com `OnConfirm`.
- "Sugerir UBS pela localização" em `:10`, com `OnLocate` (`.cs:26-32`). Primeiro pede confirmação ("Acessar localização"). Depois abre `ObsLocationDialog`, um WebView2 com geolocalização (`Views/ObsLocationDialog.xaml.cs:52-125`). Por fim chama `vm.SuggestClosestObsUnit(lat, lon)`.

**Onde aparece:**
- Painel da UBS: `OperationalOverviewView.xaml:20-26`. O cartão "UBS da operação" mostra `ActiveObsUnitLabel` e "Região:" e tem o `Expander` "Escolher ou trocar UBS" (`:24`).
- Pacientes (subaba Listagem, e na página toda): `ObsUnitRegistryView.xaml:20-22`, com o mesmo `Expander`.
- Telas que dependem de UBS mostram só o aviso `ObsUnitNoticeView.xaml` (botão "Escolher UBS no Painel", que navega para `home`). Usam o aviso: Agenda (`AgendaView.xaml:8`), Família (`FamilyView.xaml:9`), Alertas (`UnitAlertsView.xaml:4`), Indicadores (`UnitIndicatorsView.xaml:5`), Vaga Relâmpago (`UnitLightningSlotsView.xaml:8`) e Informações da UBS (`UnitSettingsView.xaml:11`).

**Quem troca:** `WorkspaceViewModel`, parcial `WorkspaceViewModel.ObsUnits.cs`.
- `ConfirmObsUnitAsync` em `:188`, `ActiveObsUnit` (propriedade, ~`:84`) e `ObsUnitSelectionOpen` em `:23`.
- `CandidateObsUnit` em `:77`, `NeedsObsUnitChoice` em `:30`, `CanConfirmObsUnit` em `:31`.
- `InitializeObsUnitsAsync` em `:149-163`: com uma só UBS autorizada, ela é escolhida sozinha.
- `LoadObsUnitsAsync` em `:165`.
- Se a UBS ativa deixa de ser autorizada: `:155,173`.
- Reset e saída: `:322-331`.
- Ao trocar a UBS, a ficha aberta fecha: `ShellWindow.xaml.cs:109-120`.
- Na Secretaria, o login pode pedir a unidade (`LoginUnits`, `WorkspaceViewModel.DesktopAccess.cs:11-17`).

---

## 4. Telas (rotas)

O ViewModel é sempre `WorkspaceViewModel`. A coluna mostra o arquivo parcial principal, ou `ShellViewModel` para o shell. Linhas de rota: `ShellViewModel.cs:212-257`.

**OBS** (`BuildOperationalNavigation`):

| Menu | Rota | View (XAML / code-behind) | VM parcial |
|---|---|---|---|
| Painel da UBS | `home` | `Views/OperationalOverviewView.xaml` (82) / `.cs` (8) | `UnitMetrics.cs`, `UnitData.cs`, `ObsUnits.cs` |
| Pacientes > Listagem | `patients:list` | `Views/ObsUnitRegistryView.xaml:46-65` / `.cs` (73), hospedada em `WorkspaceView.xaml:86` | `ObsUnits.cs` |
| Pacientes > Cadastro | `patients:register` | `ObsUnitRegistryView.xaml:25-45` | `ObsUnits.cs` |
| Pacientes > Família | `family` | `Views/FamilyView.xaml` (66) / `.cs` (42) | `Family.cs` |
| Agenda | `agenda` | `Views/AgendaView.xaml` (63) / `.cs` (31) | `Appointments.cs`, `Booking.cs` |
| Vaga Relâmpago > Em andamento / Encerradas | `slots:live` / `slots:closed` | `Views/UnitLightningSlotsView.xaml` (62) / `.cs` (16), hospedada em `WorkspaceView.xaml:87` | `Lightning.cs` |
| Indicadores > Desempenho | `indicators` | `Views/OperationalInsightsView.xaml` (7) → `UnitIndicatorsView.xaml` (83) / `.cs` (12) | `UnitMetrics.cs`, `UnitScheduling.cs` |
| Indicadores > Alertas | `alerts` | `OperationalInsightsView` → `Views/UnitAlertsView.xaml` (41) / `.cs` (12) | `UnitData.cs` |
| Comunicados | `announcements` | `Views/AnnouncementsView.xaml` (48) / `.cs` (16) | `Announcements.cs` |
| Informações da UBS | `unit-settings:*` | `Views/UnitSettingsView.xaml` (272) / `.cs` (72) | `Catalog.cs` (profissionais, consultórios, grade, bloqueios), `UnitProfile.cs`, `Devices.cs` |
| Configurações | `settings` | `Views/SettingsView.xaml` (130) / `.cs` (79) | `ShellViewModel` (aparência, atalhos), mais `RetentionSettingsView` (`SettingsView.xaml:94`, `WorkspaceViewModel.Retention.cs`) |

Subabas de Informações da UBS, pela chave `SubPage.Key` no `UnitSettingsView.xaml`:
- Atendimento (`profile`) em `:23`.
- Computadores (`devices`) em `:65`.
- Profissionais (`professionals`) em `:109`.
- Consultórios (`rooms`) em `:155`.
- Grade semanal (`schedule`) em `:184`.
- Bloqueios (`blocks`) em `:234`.
- A ordem do menu está em `ShellViewModel.cs:228-233`. O conteúdo de "Computadores" só aparece com `ShowDevicesSection`, que depende de `ComputersEnabled` (padrão desligado, `Services/ObsDeviceStore.cs:42-47`). O item do menu existe de qualquer forma.

O filtro Em andamento / Encerradas da Vaga Relâmpago é feito no ViewModel: `SlotPanelScope` em `WorkspaceViewModel.Lightning.cs:151-158` ("live" ou "closed"). A tela não usa `SubPage.Key`. A rota define `SlotPanelScope` em `ShellViewModel.cs:199`.

**Painel Municipal.** Tudo é montado em C# por `MunicipalView.Render()` (`Views/MunicipalView.xaml.cs:105-132`; o XAML tem só um `StackPanel`).

| Menu | Rota | Montagem (linha em `MunicipalView.xaml.cs`) e view | VM parcial |
|---|---|---|---|
| Visão geral | `home` | `:108-114` (banner "Toda a rede em um só lugar" + botão "Atualizar dados" + `NetworkOverviewView.xaml`, 11) | `WorkspaceViewModel.cs`, `Metrics.cs` |
| Unidades > Cadastro de UBS | `units:registry` | `:115`, `HealthUnitsView.xaml` (57) | `Units.cs` |
| Unidades > Desempenho | `units:performance` | `NetworkUnitsPerformanceView.xaml` (25) | `Metrics.cs` |
| Unidades > Cadastros de referência | `units:reference` | `UnitRegistryView.xaml` (48) | `NetworkScheduling.cs`, `Units.cs` |
| Unidades > Mapa da rede | `network` | `:117`, `NetworkMapView.xaml` (17) / `.cs` (97) | `Units.cs` |
| Unidades > Capacidade | `capacity` | `:120`, `CapacityView.xaml` (87) | `NetworkScheduling.cs` |
| Especialidades > Catálogo / Demanda | `specialties:catalog` / `:demand` | `:116`, `SpecialtyCatalogView.xaml` (34), `NetworkSpecialtiesView.xaml` (24) | `NetworkScheduling.cs`, `Metrics.cs` |
| Indicadores > Desempenho | `indicators` | `:118`, `NetworkIndicatorsView.xaml` (41) + `NetworkFiltersView.xaml` (31) | `Metrics.cs` |
| Indicadores > Vaga Relâmpago | `slots` | `:119`, `MunicipalLightningHeroView.xaml` + `LightningFiltersView.xaml` + cartões criados no código | `WorkspaceViewModel.cs:210-231` |
| Indicadores > Alertas | `alerts` | `:121`, `NetworkAlertsView.xaml` (37) | `NetworkScheduling.cs` |
| Indicadores > Relatórios e metas | `reports` | `:122-127`, `GoalsView.xaml` (60) e exportação CSV (`:90-104`) | `Metrics.cs`, `Services/MunicipalCsv.cs` |
| Comunicação | `communication` | `:128`, `CommunicationView.xaml` (142) | `Communication.cs`, `Messages.cs` |
| Usuários > Contas / Nova conta / Acessos às UBS / Histórico | `users:accounts` / `:create` / `:access` / `:history` | `:129`, `StaffAccountsView.xaml` (37), `StaffCreateView.xaml`, `StaffAccessView.xaml`, `HistoryView.xaml` (33) | `Staff.cs`, `History.cs` |
| Configurações | `settings` | `SettingsView.xaml` | igual ao OBS |

---

## 5. Listagens hoje

**Controles usados** (a fotografia é mista):
- `ListBox` com item rico:
  - Pacientes: `ObsUnitRegistryView.xaml:54`, estilo de item `PatientRow` em `:3-14` (linhas alternadas por `AlternationIndex`).
  - Consultas da Agenda: `AgendaView.xaml:40-55`, o item é um `Border` colorido por status.
  - Unidades da Secretaria: `HealthUnitsView.xaml`.
- `DataGrid`:
  - Família: `FamilyView.xaml:22,51`.
  - Vagas: `UnitLightningSlotsView.xaml:33`.
  - Profissionais, consultórios, grade, bloqueios e computadores: `UnitSettingsView.xaml:69,113,159,188,238`.
  - Indicadores por especialidade: `UnitIndicatorsView.xaml:49`.
  - Tabelas da Secretaria: Contas, Comunicados, Especialidades, Região.
- `ItemsControl` sem seleção: alertas (`UnitAlertsView.xaml:29`) e comunicados (`AnnouncementsView.xaml:20`).

**Estilos globais de lista** (`Themes/Controls.xaml`):
- `ListBox` em `:276`, `ListBoxItem` em `:282` (borda arredondada 8, selecionado em `BrandSoftBrush`).
- `DataGrid` em `:356` (`RowHeight=48`, só leitura, seleção de linha única).
- `DataGridColumnHeader` em `:375`, `DataGridRow` em `:383` (cada linha é um quadro arredondado com margem 3), `DataGridCell` em `:398`.
- O "card" em volta é `Style="{StaticResource Card}"` (`Controls.xaml:14-31`). Tem fundo `CardGradientBrush`, raio 16, padding 24 e sombra. Na Secretaria a sombra é removida por gatilho (`:25-29`). Há 68 usos nos `.xaml`.

**Ações:** botões num `WrapPanel` logo abaixo da lista, sem seleção múltipla.
- Pacientes (`ObsUnitRegistryView.xaml:58`): "Ver ficha", "Editar ficha" e botão de ativar/desativar (conteúdo `ObsRegistryActiveAction`). O fluxo está em `ObsUnitRegistryView.xaml.cs:56-72` (`OnSetActive`):
  - Busca o impacto, `PreviewObsDeactivationAsync`.
  - Mostra a confirmação, `DeactivationConfirmationText`.
  - Chama `SetObsRegistryActiveAsync`.
- Corrigir e-mail: `Expander` em `ObsUnitRegistryView.xaml:59-63`, confirmação em `.cs:36-41`.
- Agenda (`AgendaView.xaml:57`): Chamar, Concluir, Registrar falta e Cancelar consulta. O handler `OnStatus` está em `AgendaView.xaml.cs:25-30`. Só "Cancelar consulta" pede confirmação. Habilitação por `CanCallAppointment` e demais, em `WorkspaceViewModel.Appointments.cs:61-62`.
- Família: `FamilyView.xaml:35-38` (Aprovar e Recusar, mais "Motivo da recusa" em `:39-41`) e `:60-62` (Encerrar vínculo). Cada ação pede confirmação, handlers em `FamilyView.xaml.cs:15-41`.
- Profissionais: `UnitSettingsView.xaml:122-126`. Consultórios: `:166-170`. Grade: `:197-201`. Bloqueios: `:243-245`.
- Computadores: `:80-83`.
- Comunicados: o botão "Marcar como lido" fica dentro de cada item (`AnnouncementsView.xaml:32`).

**Ficha de detalhe:**
- Duplo clique e Enter:
  - `ListBox`: `ObsUnitRegistryView.xaml.cs:46-55`, `AgendaView.xaml.cs:14-23`.
  - `DataGrid`: helper `Views/ListingInteraction.cs:21-36`, usado em `UnitLightningSlotsView.xaml.cs:14-15`.
- Quem abre: `ShellWindow.OpenItemDetails` (`ShellWindow.xaml.cs:95-108`).
  - Monta a ficha com `WorkspaceViewModel.BuildDetails` (`WorkspaceViewModel.Listings.cs:48-56`: paciente, consulta e vaga) ou `BuildDetailsAsync` (`:13`, só vaga).
  - Para paciente, acrescenta a aba "Exames e vacinas" (`HealthRecordsView.xaml`, `HealthRecordsViewModel.cs`).
- A janela é `Views/ItemDetailsWindow.xaml` (57 linhas, 580x640, sem moldura) com `ItemDetailsViewModel` (49 linhas). Mostra campos agrupados com botão de copiar, e fecha em `ShellWindow.xaml.cs:109`.

**Componentes genéricos já existentes:**
- Diálogo e confirmação modais da marca:
  - `Services/DialogService.cs:24-55` (`Show`, `Ask`, `Info`, `Success`, `Warning`, `Error`, `ShowModal`).
  - `Services/Confirmation.cs:6-12` (`Confirmation.Ask`, com `Handler` para testes).
  - `Views/NoraDialog.xaml` (ícone por tipo, botão primário e secundário).
  - O escurecimento do fundo é feito por `ShellWindow.SetDimmed` (`ShellWindow.xaml.cs:165-172`) e `DialogOverlay` (`ShellWindow.xaml:202`).
- Estilo `FloatingActions` (`Controls.xaml:405-410`): **está definido, mas nenhum arquivo o usa**. Não há faixa de ações flutuante implementada.
- `ProgressStepTemplate` (`Controls.xaml:460`) é usado nas listas de passos.
- Busca na lista: `Views/ListSearchFilter.cs`.
- Enter vai ao próximo campo: `Views/EnterNavigation.cs`.
- **Não existe** um componente de listagem reutilizável com seleção Ctrl/Shift, nem balão de ações.

---

## 6. Filtros

**Componente do botão:** o botão e o popup ficam no cabeçalho (`ShellWindow.xaml:119-139`). A lógica está em `Views/ShellWindow.Filters.cs`:
- `RegisterPageFilter` / `UnregisterPageFilter` / `UpdatePageFilters` em `:6-28`. O botão aparece quando há `PageFilterGroup` visível com `Fields`.
- `OnOpenFilters` em `:36-57`. Monta o painel clonando o conteúdo de cada grupo. `HorizontalOffset` alinha o popup à direita do botão (`:51`).
- `OnFilterPopupKeyDown` (Esc) em `:59`. `OnCloseFilters` em `:67`. `OnClearFilters` em `:84`.
- "Limpar filtros" (`ShellWindow.xaml:133`) chama `WorkspaceViewModel.ClearPageFiltersAsync` (`WorkspaceViewModel.PageFilters.cs:5-60`), que zera por `FilterKey`.

**Como cada tela declara:** `Views/PageFilterGroup.cs:1-57`, um `Decorator` com `Label`, `FilterKey`, `SearchFields`, `ItemsTarget` e `Fields`.
- Modo A, só pesquisa: `ItemsTarget` + `SearchFields` geram um `ListSearchFilter` (caixa "Buscar na lista", `ListSearchFilter.cs:26`).
- Modo B, filtros reais: o conteúdo XAML entra como filho do grupo.
- Sem a janela principal, o conteúdo aparece embutido na própria tela (`PageFilterGroup.cs:30-33,43-48`).

**Telas com filtros reais** (filho XAML, `FilterKey`):
- Pacientes: busca + situação + presença + ordenação (`ObsUnitRegistryView.xaml:49`, "patients").
- Indicadores da UBS: período, especialidade, situação, datas personalizadas e aplicar (`UnitIndicatorsView.xaml:6`, "unitMetrics").
- Secretaria: `NetworkFiltersView.xaml:3` (networkMetrics), `LightningFiltersView.xaml:3` (networkLightning), `NetworkOverviewView.xaml:3` (networkOverview), `NetworkAlertsView.xaml:7` (networkAlerts), `HealthUnitsView.xaml:18` (units), `HistoryView.xaml:6` (history), `UnitRegistryView.xaml:24` (municipalAgenda).

**Telas só com pesquisa** (`ItemsTarget`):
- Agenda: `AgendaView.xaml:39`.
- Família, duas listas: `FamilyView.xaml:21,50`.
- Alertas da UBS: `UnitAlertsView.xaml:12`.
- Comunicados: `AnnouncementsView.xaml:19`.
- Informações da UBS, quatro listas: `UnitSettingsView.xaml:112,158,187,237`.
- Secretaria: `SpecialtyCatalogView.xaml:18`, `StaffAccountsView.xaml:10`, `CommunicationView.xaml:89,129`, `UnitRegistryView.xaml:8`.

**Sem `PageFilterGroup` (filtros embutidos na própria tela):** Vaga Relâmpago do OBS usa Especialidade, Situação e Pesquisar num cartão "Filtros" (`UnitLightningSlotsView.xaml:24-32`). O botão "Filtros" do cabeçalho não aparece nessa tela.

**Outra busca:** busca global da tela com Ctrl+F (`Views/ShellWindow.Search.cs`, `Generics/ScreenSearch.cs`). É uma barra criada em código.

---

## 7. Gráficos e cards

**Gráficos:**
- Evolução diária: `Views/EvolutionChartView.xaml` (22) / `.cs` (143). É um `Canvas` com `Rectangle`, `Polyline` e `Line` desenhados em código.
  - Barras agrupadas até 31 dias, linhas acima disso (`.cs:19,90,113-134`).
  - Séries em `.cs:21-26`: Concluídas = `SuccessBrush`, Faltas = `ErrorBrush` (tracejada 5,3), Canceladas = `WarningBrush` (tracejada 1,5/3).
  - Dica por dia: `:136-141`.
  - Usado em `OperationalOverviewView.xaml:54` (`PanelChart`, 7 dias), `UnitIndicatorsView.xaml:66` (`UnitChart`) e `NetworkIndicatorsView.xaml` (`NetworkChart`).
  - Modelo de dados: `ViewModels/ChartSeries.cs:21-51`.
- **Expansor "ver dados em tabela":** `EvolutionChartView.xaml:8-20`. Cabeçalho "Ver os dados do gráfico em tabela", com `DataGrid` de Data, Concluídas, Faltas e Canceladas ligado a `Data.Rows`.
- Pizza e rosca: `Views/OutcomePie.cs` (57), um `FrameworkElement` com `OnRender`.
  - Modo consultas encerradas: cores fixas em hexadecimal (`:37`): `#137A52`, `#B4233E`, `#C68800`.
  - Modo `Slices`: usa `ChartBrush1..6` do tema (`:30`). `HoleRatio` faz a rosca.
  - Usada em `UnitIndicatorsView.xaml:34-35`: "Distribuição das consultas encerradas", com legenda textual (`:35`).
- **Pop-up do cartão:**
  - Botão: `Controls.xaml:556-564` (`ChartButton`, glifo E9D2) e `ChartCommands.Show` (`Views/SubPage.cs:188`).
  - Tratador: `ShellWindow.xaml.cs:73-86` (`OnShowChart`). Constrói os dados em `WorkspaceViewModel.Charts.cs:8-61`.
  - Janela: `Views/IndicatorChartWindow.xaml` (30) / `.cs` (105), 640x520, sem moldura.
  - Abas "Linha" e "Pizza" em `.xaml:12-17`.
  - Texto "Valor atual: N" em `.cs:22`.
  - Linha desenhada em `DrawLine` (`.cs:59-90`) com `ChartBrush1`. Pizza com rosca de `HoleRatio=0.55` (`.xaml:22`).
  - Legenda em `.cs:44-57`.
  - Se não há série, a linha mostra o valor por especialidade (`Charts.cs:31-43`).
- Paleta de séries nos temas: `ChartBrush1..6` em `Themes/LightTheme.xaml:36-41` e `Themes/DarkTheme.xaml:36-41`.

**Cartões:**
- `MetricCardTemplate` (`Controls.xaml:424-436`): título, valor 28pt, dica e mini botão de gráfico. Usado nas listas de `MetricCard`. Painéis de 3 ou 4 colunas: `ThreeColumnPanel` / `FourColumnPanel` (`:482-483`).
- Cartão local `ObsMetricCard` em `UnitIndicatorsView.xaml:2`.
- **Painel da UBS** (`OperationalOverviewView.xaml`):
  - Banner: `:4-19`. Textos "PAINEL OPERACIONAL" (`:10`) e "Sua unidade em um só lugar" (`:11`). Fundo `BrandGradientBrush`, com duas elipses decorativas (`:7-8`) e o `UnitLabel` num chip (`:14-16`).
  - "UBS da operação": `:20-26`.
  - **"Resumo do dia":** `:28-47`. São 6 cartões com botão de gráfico: Ofertas em andamento, Consultas concluídas, Tempo médio de espera, Consultas agendadas, Faltas e Cancelamentos. Mais o cartão "Consultórios ativos" (`:36-46`).
  - "Consultas dos últimos 7 dias": `:51-56`.
  - **"Atenção da equipe":** `:57-72`, com botão "Ver central de alertas".
  - **"Fluxo da unidade":** `:75-80` (pacientes, na fila, vagas ativas).
- Banner da Secretaria: `MunicipalView.xaml.cs:109-113` ("Toda a rede em um só lugar", código puro).

---

## 8. Período dos indicadores (cliente e API)

**Cliente:**
- Chamadas: `Nora.Desktop/Services/NoraApiClient.cs:224-225` (`UnitMetricsAsync`, `GET api/obs/units/{id}/metrics?from=&to=`, datas ISO "O") e `:234-240` (`MetricsAsync`, `GET api/municipal/metrics`, mais `unitId`, `region` e `specialty` opcionais).
- OBS: opções `UnitPeriodOptions` em `WorkspaceViewModel.UnitMetrics.cs:39-40`: Hoje(1), 7, 14, 30, 90, 180, 365 e Personalizado(0). O padrão é 30 (`:25`).
  - Janela rolante: `endExclusive = hoje+1 dia; start = endExclusive − dias` (`:163`). O intervalo personalizado soma +1 dia ao fim (`:161`).
  - Painel: sempre 30 dias, com gráfico dos últimos 7. Constantes `PanelDays=30` e `PanelChartDays=7` em `:22`, `BuildPanelRequest` em `:168-171`.
  - Releitura a cada 5 minutos: `MetricsRefreshAge`, `UnitMetrics.cs:23,115-116`.
- Secretaria: `PeriodOptions` em `WorkspaceViewModel.Metrics.cs:31-32` (7, 30, 90, Personalizado). Janela montada em `TryBuildMetricsRequest` (`:130-140`).
- Validação local do intervalo personalizado: `Metrics.cs:20` (`MaxRangeDays = 366`) e `:121-128` (`ValidateDateRange`, mensagem "O período pode ter no máximo 366 dias."). Reusada em `UnitMetrics.cs:147`.
- Tooltip: "até 366 dias" em `UnitIndicatorsView.xaml:18`.

**API (Nora.Api):**
- Endpoints: `Nora.Api/ApiEndpoints.cs:78-79` (OBS, passa por `UnitAccessService`) e `:254-255` (municipal).
- O limite está em `Nora.Api/Services/MetricsService.cs:25` (`MaxWindowDays = 366`) e `:87-90` (`ValidateWindow`). O intervalo é inválido se `to <= from` ou `to - from > 366 dias`. Lança `ApiInputException`. É chamado em `:30` (unidade) e `:40` (rede).
- Mesmo limite de 366 dias em outros pontos: `Nora.Api/DAOs/AppointmentDao.cs:47,64` (listas de consultas) e `Nora.Api/ApiEndpoints.Scheduling.cs:113-114` (bloqueios).
- Disponibilidade de horários usa `MaxAvailabilityDays` (`SchedulingService.cs:113`).

---

## 9. Sessão e login salvo

**Desktops** (o fluxo real é `DesktopAccessView`):
- Tela: `Views/DesktopAccessView.xaml:12` tem o `CheckBox "Manter conectado por 30 dias"` ligado a `RememberDesktopSession` (`WorkspaceViewModel.DesktopAccess.cs:14`). O texto "30 dias" está escrito no XAML. O rótulo do `WorkspaceView` (`:41`) é outro: "Manter conectado neste computador".
- Cliente envia `desktopSession=true` e `keepSignedIn`: `Services/NoraApiClient.cs:61-68`. O login é feito em `WorkspaceViewModel.DesktopAccess.cs:33-89` (`LoginDesktopAsync`). O corpo inclui o nome da máquina.
- Se `session.KeepSignedIn` volta verdadeiro, grava `session.dat` (`DesktopAccess.cs:84-86`). Senão, apaga (`:86`).
- Arquivo: `%LOCALAPPDATA%\NORA\OBS\session.dat` (OBS) ou `...\NORA\municipal\session.dat` (Painel). Protegido por DPAPI. Contém endpoint, token e vencimento (`Services/ObsDeviceStore.cs:11,34,54,73`). Nada vai em texto.
- **Fechar e reabrir sem marcar a caixa:**
  - O token só existe em memória (`NoraApiClient.cs:21`, comentário "Session tokens remain in memory").
  - Nesse caso `session.dat` foi apagado no login (`DesktopAccess.cs:86`), então `RestoreDesktopAccessAsync` (`:91-133`) não acha nada (`:93`) e mostra a tela de login.
  - Ao fechar, `Dispose` chama `SignOut` (`WorkspaceViewModel.cs:466,451`). Isso limpa o token local e **não avisa a API**, e a sessão dura até vencer.
  - `LogoutAsync` (`:293`) é o único caminho que chama a API e apaga o arquivo.
- Com a caixa marcada: ao abrir, `ShellWindow.xaml.cs:43` chama `Workspace.OpenAsync` (`WorkspaceViewModel.Devices.cs:60`). Ele chama `RestoreDesktopAccessAsync`, que:
  - Confere vencimento e endpoint (`:95-99`).
  - Chama `GET api/auth/me` (`:114-117`).
  - Em caso de 401, apaga o arquivo e mostra "A sessão salva terminou..." (`:119-125`).
  - Em erro de rede, mantém o arquivo (`:126`).
- Computadores ativados (`HasDeviceSupport`) só existem se `ComputersEnabled=true`. O padrão é falso (`ObsDeviceStore.cs:42-47` e `WorkspaceViewModel.Devices.cs:36`). Por isso o "manter conectado" funciona sem computador no OBS atual.

**Quem decide 24 h e 30 dias: a API, não o cliente.**
- `Nora.Api/Services/AuthService.cs:98-99`: expiração padrão `AddHours(desktop ? 24 : 8)` e manter conectado `AddDays(desktop ? 30 : KeepSignedInDays)` (padrão 30, intervalo 1 a 90, `Services/ObsDeviceOptions.cs:15-24`).
- `Nora.Api/DAOs/SessionDao.cs:49-50`: `keep = KeepSignedIn && (device != null || (Desktop && (Scope=="municipal" || !DevicesEnabled)))` e `expires = keep ? KeepSignedInExpiry : DefaultExpiry`.
- O cliente só manda o booleano e recebe `ExpiresAt` e `KeepSignedIn` (a duração realmente aplicada). Não há renovação deslizante: `SessionDao.cs:114` só confere `expires_at>$2`.
- Resumo: desktop sem marcar = 24 h. Desktop marcado = 30 dias. Login que não é de desktop = 8 h. Esse login não-desktop também fica em `NoraApiClient.cs:67`, só para OBS.

**Android (Kotlin):**
- Token em memória apenas. `APPNORA/app/src/main/java/com/example/app_nora/api/PatientController.kt:81` ("token e identidade nunca vão para disco") e `:219-223` (`startSession`, que só grava o endereço).
- Só o endereço do serviço vai em `SharedPreferences "nora_connection"` (`PatientController.kt:88-90`). Outras preferências ficam em `preferences/SettingsStore.kt`.
- O controlador vive em `PatientViewModel` (`MainActivity.kt:129-130`). Sobrevive a rotação, mas **não** a morte do processo nem a fechar o app.
- **Não há opção "manter conectado"**: o grep por manter/keep/lembrar não achou nada relacionado à sessão.
- A API cria sessão de paciente com **8 h**, via `AuthService.LoginAsync` → `CreateSessionAsync(desktop=false)` (`AuthService.cs:98`). `keepSignedIn` não vale para paciente: `SessionDao.cs:49` exige computador ou desktop, e paciente tem os dois ausentes. O login por Google usa a mesma sessão (`PatientController.kt:206-212`).

---

## 10. Botão "Atualizar"

**Estilo `IconRefreshButton`** (`Themes/Controls.xaml:103-120`): mostra "↻" 23pt, gira 360° ao clicar (0,55 s), tooltip padrão "Atualizar". Há também botões "Atualizar ..." de texto comum.

Ocorrências por tela:
- Agenda: `AgendaView.xaml:38` (nome "Atualizar agenda", `Click=OnLoad`). Fica dentro do bloco do dia selecionado.
- Pacientes, Listagem: `ObsUnitRegistryView.xaml:48` ("Atualizar cadastros da UBS").
- Seletor de UBS: `ObsUnitPickerView.xaml:8` ("Atualizar lista de UBS").
- Família: `FamilyView.xaml:11` ("Atualizar pedidos").
- Vaga Relâmpago: `UnitLightningSlotsView.xaml:15` ("Atualizar vagas da UBS"). O tooltip diz que o painel também atualiza sozinho a cada 20 s.
- Alertas da UBS: `UnitAlertsView.xaml:6`.
- Comunicados: `AnnouncementsView.xaml:6`.
- Informações da UBS: `UnitSettingsView.xaml:19` ("Atualizar informações da UBS") e `:67` ("Atualizar computadores").
- Ficha do paciente: `HealthRecordsView.xaml:39` (botão simples "Atualizar").
- Secretaria:
  - Visão geral: `MunicipalView.xaml.cs:112` ("Atualizar dados", botão criado em código, ligado a `RefreshCommand`).
  - Vaga Relâmpago da rede: `MunicipalLightningHeroView.xaml:3` ("Atualizar vagas", `RefreshCommand`).
  - Unidades: `HealthUnitsView.xaml:17` e `NetworkMapView.xaml:6` ("Atualizar unidades").
  - Especialidades: `SpecialtyCatalogView.xaml:17` ("Atualizar lista").
  - Metas: `GoalsView.xaml:10` ("Atualizar metas").
  - Alertas da rede: `NetworkAlertsView.xaml:18`.
  - Comunicação: `CommunicationView.xaml:87` ("Atualizar lista").
  - Usuários, botões comuns sem ícone: `StaffAccountsView.xaml:7` ("Atualizar contas e UBS") e `StaffAccessView.xaml:7` ("Atualizar vínculos").
- Sem botão de atualizar: Indicadores do OBS e da Secretaria (os filtros reconsultam), Capacidade e Configurações.

**Ciclo automático:**
- `DispatcherTimer` de **20 s**: `WorkspaceViewModel.cs:69-70`. Inicia após o login (`:280`, `DesktopAccess.cs:148`, `Devices.cs:134`, `Account.cs:42`) e para em `SignOut` (`:453`).
- Cada tick chama `RefreshAutomaticallyAsync` (`:407-408`), que chama `RefreshAsync(AutoScopeFor(Section))`. Roda em segundo plano, sem barra de progresso, e é pulado se há ação do usuário (`:370-396`).
- Dados lidos por ciclo: `ScopeFor` / `AutoScopeFor` em `WorkspaceViewModel.UnitMetrics.cs:122-140`.
  - Indicadores, alertas e painel só se a última tentativa tem mais de 5 min (`:23,115-116`).
  - Catálogo em `unit-settings`/`agenda` a cada 5 min.
- Comunicados são lidos de forma incremental a cada ciclo (`WorkspaceViewModel.cs:333-341`).
- Ao abrir uma seção: `OnSectionEntered` em `:411-426` e `EnterMunicipalSection` em `:429-440`.
- Contagem regressiva de ofertas: timer de 1 s em `WorkspaceViewModel.Lightning.cs:190`.
- O botão manual `RefreshCommand` (`WorkspaceViewModel.cs:72-77`) relê tudo da seção, com `manual: true`, e mostra "Dados atualizados pela API.".
- Falha em segundo plano marca "dados desatualizados" (`WorkspaceViewModel.Messages.cs:22-23`, faixa em `ShellWindow.xaml:169`).

---

## 11. Estilos globais

**Arquivos:**
- `Nora.Desktop/Themes/Controls.xaml` (610 linhas): estilos e templates.
- `Nora.Desktop/Themes/LightTheme.xaml` (42) e `DarkTheme.xaml` (42): só cores (`SolidColorBrush`/`LinearGradientBrush`) e a imagem de marca. O teste exige as mesmas chaves nos dois (`tests/Nora.Desktop.Checks/Program.cs`, "Temas claro e escuro expõem as mesmas chaves").
- Troca de tema em tempo de execução: `ThemeService.Apply` (`Services/ThemeService.cs:28-36`) substitui o dicionário de paleta. Os estilos usam `DynamicResource`.

**Botões:**
- Botão padrão: `Controls.xaml:32-70`. Fundo `SurfaceBrush`, raio 9, `MinHeight=44`.
  - Hover: camada `ButtonHoverBrush` (`:47`, `Visibility` ligado ao `IsMouseOver` em `:53`).
  - Pressionado: opacidade 0,75. Foco: borda 2px `AccentTextBrush`. Desabilitado: opacidade 0,5.
- `PrimaryButton` (`:71-89`): fundo `BrandGradientBrush`, hover `BrandGradientHoverBrush`.
- `ObsPrimaryButton` (`:90-102`): template próprio sobre o primário, com `BrandGradientHoverBrush` no hover (`:96`).
- `BigPrimaryButton` (`:501-505`).
- `IconRefreshButton` (`:103-120`), `TextActionButton` (`:121-136`), `NavItemButton`/`NavBackButton` (`:506-535`), `CircleIconButton` (`:536-555`), `ChartButton` (`:556-564`), `AvatarButton` (`:565-575`).

**Cores de hover:**
- `ButtonHoverBrush`: claro `#1405284A`, escuro `#20E1F0FF` (`LightTheme.xaml:15`, `DarkTheme.xaml:15`).
- `HoverBrush`: claro `#EEF3F9`, escuro `#123352` (`:34`), usado em navegação e `CircleIconButton`.
- `BrandGradientHoverBrush` (`:26`) nos dois temas.
- Seleção e destaque: `BrandSoftBrush`.
- Estados: `SuccessBrush`/`ErrorBrush`/`WarningBrush`/`InfoBrush` e suas `*SurfaceBrush`.
- Gráficos: `ChartBrush1..6`.

**Cards:** `Card` em `Controls.xaml:14-31`. Há outros estilos de texto:
- `Muted` (`:8`), `PageTitle` (`:488`), `PageSubtitle` (`:491`), `SectionTitle` (`:494`), `FieldLabelText` (`:497`).
- Erro e aviso de campo: `FieldError` (`:411`) e `FieldWarning` (`:417`).
- Espaçamentos nomeados: `SectionSpacing`, `BlockSpacing` (`:486-487`).

**Estilo de perigo ou lixeira: não encontrado.** Nenhum estilo "danger". Ações destrutivas (Cancelar consulta, Recusar, Encerrar vínculo, Revogar, Remover horário ou bloqueio) usam o botão padrão, ou o `ObsPrimaryButton`, e dependem de diálogo de confirmação. O único glifo de lixeira (E74D) está em `Views/RetentionSettingsView.xaml:8`.

**Cores de status da agenda:** são fixas, direto no XAML, sem token. Legenda em `AgendaView.xaml:30-34`, fundos dos itens em `:42`, marcadores `Ellipse` em `:37`. Valores: Agendada `#E8D8FF`, Chamada `#FFF2C6`, Concluída `#DCF4E5`, Falta `#FFE4D2`, Cancelada `#FFE0E5`. O texto dos itens é escuro (`#102A43`), mesmo no tema escuro.

---

## 12. Agenda do OBS (`Views/AgendaView.xaml`, 63 linhas, e `.cs`, 31)

- Estilos locais: `AgendaSegment` (`:3`) e `AgendaDayButton` (`:4`).
  - Dia selecionado: gatilho `IsSelected` com `BrandGradientBrush` e texto `BrandTextBrush`.
  - Hoje: borda 2px `BrandBrush`.
  - Fora do mês: opacidade 0,45.
- Alternância "Dia | Semana" (comandos `SelectAgendaDayCommand` / `SelectAgendaWeekCommand`): `:7`.
- Botão "+ Agendar nova consulta": `:9`. O painel colapsado `BookingPanel` fica em `:10-26` e abre por `OnToggleBooking` (`.cs:13`).
  - Com profissional ativo: Especialidade, Profissional, lista de horários livres e botão Agendar (`:14-23`).
  - Sem profissional ativo: campos de texto livre, com horário inicial e final em HH:mm (`:25`).
- Calendário mensal, card em `:27-60`:
  - Navegação ‹ › (`:27`, `OnPrevious`/`OnNext`, `MoveAgenda`, `WorkspaceViewModel.Appointments.cs:139`). Título `AgendaRangeLabel` (`:43`).
  - Legenda `WrapPanel` em `:28-35`.
  - Cabeçalho Seg..Dom em `:36`.
  - Grade de 42 dias, `UniformGrid Columns=7` (`:37`), ligada a `AgendaDays`. Cada dia é um botão com número e bolinhas por status (`HasScheduled`/`HasCalled`/`HasCompleted`/`HasMissed`/`HasCancelled`). Modelo em `Models/AgendaCalendarDay.cs:6-38`.
  - Construção dos dias: `Appointments.cs:84` (`BuildAgendaDays`). Seleção em `:133`. O intervalo exibido tem no máximo 42 dias, uma requisição (`:72`).
- Lista do dia (dentro do bloco `:38-59`):
  - Data selecionada (`SelectedAgendaDateLabel`), UBS e região.
  - Botão atualizar (`:38`).
  - `PageFilterGroup` de pesquisa (`:39`).
  - `ListBox` de consultas `AppointmentsFilterList` (`:40-55`): nome, dia, horário, CPF, especialidade, profissional, situação, "Marcada por" e confirmação de presença.
  - Botões de status (`:57`).
- Ficha da consulta: duplo clique ou Enter (`AgendaView.xaml.cs:14-23`), montada por `BuildAppointmentDetails` (`WorkspaceViewModel.Listings.cs:71`).

---

## 13. Informações da UBS → Atendimento

**Tela:** `UnitSettingsView.xaml:23-61`.
- Linhas por dia: `ItemsControl OpeningDays` em `:30-49`. Cada linha tem rótulo do dia (140 px), `CheckBox "Fechado"` (`:37`) e, se não está fechado, 4 caixas HH:mm (`:38-43`): "Abre" / "Fecha" / "e Abre" / "Fecha".
- Mensagem de erro por dia: `:45`.
- Serviços: `TextBox` multilinha com contador (`:50-53`).
- Especialidades da grade: só leitura (`:54-56`).
- Botão "Salvar atendimento ao público": `:58`, `OnSaveProfile`.

**Modelo:** `OpeningDayEditor` em `ViewModels/WorkspaceViewModel.UnitProfile.cs:9-54`.
- `IsClosed` e `HasIntervals` em `:15-16`. `Opens1/Closes1/Opens2/Closes2` em `:17-20`.
- `Load` em `:25`: pega até 2 intervalos ordenados (`.Take(2)`). Sem intervalos = fechado.
- `Read` em `:36-53`: valida HH:mm, fim depois do início, segundo intervalo sem sobreposição com o primeiro (encostar pode) e exige pelo menos um intervalo se não for "Fechado".
- Mensagens: "informe ao menos um intervalo ou marque Fechado" (`:48`) e "os dois intervalos não podem se sobrepor" (`:50`).
- `OpeningDays` tem 7 itens, com `Weekday` 0 = domingo (`:99-104`).
- Salvar: `SaveProfileAsync` em `:138-164`. É uma operação única que **substitui tudo**. Só o administrador edita (`CanEditProfile`, `:72`). O texto de serviços tem no máximo 1.000 caracteres (`:62,129-135`).

**Contrato da API (cliente):**
- `GET api/obs/units/{unitId}/profile` e `PUT api/obs/units/{unitId}/profile` (`Services/NoraApiClient.cs:102-103`).
- Rotas no servidor: `Nora.Api/ApiEndpoints.Round.cs:48,50`. A autorização do PUT usa `SchedulingAccessService`.
- Modelos (`Models/RoundModels.cs:56-61`):
  - `OpeningInterval(int Weekday, string Opens, string Closes)`.
  - `UnitProfile(Guid UnitId, bool Informed, OpeningInterval[] OpeningHours, string? Services, ProfileSpecialty[] Specialties, DateTimeOffset? UpdatedAt)`.
  - `UnitProfileDraft(OpeningInterval[] OpeningHours, string? Services)`.
- Ou seja, a lista é plana: vários `OpeningInterval` por dia, e o editor limita a 2. A regra de dois intervalos é duplicada no cliente e na API.

---

## 14. Formulários de cadastro

- **Não existe painel de layout responsivo próprio.** Não há `Panel` customizado: o grep por `MeasureOverride`/`ArrangeOverride`/`class : Panel` não achou nada em `Nora.Desktop`.
- Disposição atual:
  - Formulários de cadastro principais: `StackPanel` vertical com rótulo `TextBlock` seguido de `TextBox`, um campo por linha. Exemplos: paciente em `ObsUnitRegistryView.xaml:25-45` (`MaxWidth=720`, alinhado à esquerda), UBS em `HealthUnitsView.xaml:3-14`, nova conta em `StaffCreateView.xaml`, perfil em `StaffProfileWindow.xaml`.
  - Estilo de campo: `TextBox` padrão com `MinHeight=42` e margem. Em `WorkspaceView.xaml:2-17` os estilos de `TextBox` e `PasswordBox` são locais.
  - Formulários de Informações da UBS: `StackPanel` com `MaxWidth` (420 a 760) dentro de `Border` `SurfaceAltBrush`, **com `WrapPanel` de campos de largura fixa** (`StackPanel Width="240/220/200/180/110/..."`). Exemplos:
    - Grade: `UnitSettingsView.xaml:205-217`.
    - Bloqueios: `:259-269`.
    - Conselho profissional em `Grid` de colunas fixas (110, *, 80): `:136-141`.
    - Especialidades em `WrapPanel` de `CheckBox` (`:144-146`).
  - Filtros de período usam `WrapPanel` com larguras 150 a 190 (`UnitIndicatorsView.xaml:8-19`).
  - Linhas de ação e grids usam `UniformGrid` de colunas fixas: 3 ou 4 (`Controls.xaml:482-483`), 4 em `OperationalOverviewView.xaml:29`, 5 em `UnitLightningSlotsView.xaml:17`, 7 no calendário.
  - Os campos obrigatórios ou de erro usam `FieldError` abaixo do campo.
  - O Enter move para o próximo campo: `Views/EnterNavigation.cs` (grupo via `enter:EnterNavigation.Group`).

---

## 15. Testes de interface (`tests/Nora.Desktop.Checks`)

- Projeto: `Nora.Desktop.Checks.csproj` é um **Exe** (net10.0-windows, WPF), sem framework de teste externo. O ponto de entrada é `Program.cs:18-196`. Cada caso é `Check("nome", () => ...)` (`:198-204`), com `PASS`/`FAIL`, e o resumo final é "N/M verificações passaram." (`:196`).
- **Quantidade:** contei 185 ocorrências literais de `Check("` em 31 arquivos. O total real de PASS por execução varia (há verificações condicionais por argumento, e algumas são geradas em laços). A documentação registra 23/23 em 2026-10-07, 55/55 e 58/58 em 2026-10-13 e 184/184 no E2E com banco em 2026-11-18 (`docs/execucao.md:12,83,231`).
- **Cobertura:** preferências, tema e contraste, navegação em dois níveis, filtros de página, login, listagens e fichas, Agenda, Família, Comunicados, Secretaria, indicadores, etc. A maioria usa uma API HTTP sintética em memória (`ApiFixture*.cs`) ou o banco descartável nos scripts de validação.
- **Dependência de nomes:** alta.
  - Acha controles por `AutomationProperties.Name` ("Menu da conta NORA", "Senha de acesso", "E-mail", "Retorno da ação", "Aba Dados", "Ficha de detalhes", "Fechar detalhes", "Pedidos de família pendentes" e muitos outros), por exemplo em `ApiClientChecks.cs:163,214,234,235,266,272,392`, `CommunityChecks.cs:180-184`, `ListingChecks.cs:134,192,213`, `EnterNavigationChecks.cs:94-114` e `InterfaceChecks.cs:120`.
  - Usa `FindName` com nomes `x:Name` do shell (`Sidebar`, `PageHeader`, `PageScroll`, `FiltersPopup`) em `InterfaceChecks.cs:115,176,222`, `DesktopAccessChecks.cs:39,63-64` e `ApiClientChecks.cs:268`. Também usa nomes dentro de templates, como "Frame" e "Tile" (`InterfaceChecks.cs:43,152`).
  - Depende de rótulos de texto dos botões ("Entrar", "Salvar..."), do tipo do controle (`DataGrid`, `ListBox`, `Button`) e do pai visual (`InterfaceChecks.cs:79,85,347-357`).
  - **Não usa `AutomationId`:** o grep por `AutomationId` não achou ocorrências.
  - Aciona botões por `ButtonAutomationPeer` (`docs/execucao.md:128`).
- **Capturas de tela:** sim.
  - Mecanismo: `RenderTargetBitmap` + `PngBitmapEncoder` do conteúdo WPF, com janelas fora da área visível (`InterfaceChecks.cs:363-365`). Não é captura do desktop real.
  - Pasta: `--preview-dir <pasta>` (`InterfaceChecks.cs:22`, `VisualGalleryChecks.cs:21`).
  - `--visual-gallery` ativa a galeria em `VisualGalleryChecks.cs:137,182`. Gera `visual-<perfil>-<tela>-<tema>-<posição>.png` e `visual-captures.json` com tamanho, tema e origem dos dados (`:149-163`). Recusa captura se algum `PasswordBox` está preenchido. Confere dimensões e transparência do rodapé (`:166-176`).
  - `--secretaria-gallery`, `--changed-secretaria`, `--map-online` e `--secretaria-live-dark/light` estão em `Program.cs:23,193-195` (`SecretaryGalleryChecks.cs`, `LiveMapPreview.cs`).
  - Galeria HTML: `tests/gerar-galeria-visual.ps1` e `tests/atualizar-fotos-galeria.ps1`.
- **Comandos reais** (README.md:62-69 e docs/execucao.md:113-119):
  - `dotnet build NORA.slnx --configuration Release --nologo -m:1` compila a solução (no sandbox, `-m:1 --disable-build-servers` foi o que passou, `docs/execucao.md:122`).
  - `dotnet run --configuration Release --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj` roda as checagens de interface (README.md:65 usa sem `--configuration`).
  - `--check-filter <texto>` filtra por parte do nome (`Program.cs:25-26`). Exemplo: `dotnet run --configuration Release --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj -- --check-filter Navegação`.
  - Galeria: `dotnet .build/Nora.Desktop.Checks/bin/Release/net10.0-windows/Nora.Desktop.Checks.dll --preview-dir .build/<pasta>/desktop --visual-gallery` (`docs/execucao.md:187`).
  - `.\tests\desktop-startup.ps1` abre os dois executáveis Release (`.build\<app>\bin\Release\net10.0-windows\<app>.exe`) e exige a janela com o título "NORA OBS" ou "NORA Municipal" (`desktop-startup.ps1:12-30`).
  - E2E contra banco descartável: `pwsh -NoProfile -File tests/validar-clientes-descartavel.ps1 -PostgreSqlBin "<...>\PostgreSQL\18\bin"` (`docs/execucao.md:97`), e com `-Android` para incluir o app.
  - `tests/validar-postgres-descartavel.ps1 -IncludeUnitDatabases` valida a API.
  - Saída do build: `Directory.Build.props` manda tudo para `.build/<projeto>/bin|obj`.
  - O `desktop-startup.ps1` depende de os executáveis Release existirem, então compile antes. Mudar o título da janela quebraria esse script.

---

## 16. Tamanho (linhas)

**Totais em `Nora.Desktop`:**
- Views `.xaml`: 2.427 linhas.
- Views `.cs`: 2.603 linhas.
- ViewModels `.cs`: 6.688 linhas (`WorkspaceViewModel*.cs` somam 5.935).
- Themes: 694 linhas (`Controls.xaml` 610).
- Models, Services e Generics: 2.189 linhas.
- Shell completo (`ShellWindow.xaml` + `.cs` + `.Filters` + `.Search`): 606 linhas.
- `OBS-NORA` e `Painel-Municipal`: cerca de 25 linhas de C# cada.

**Maiores XAML** (`Nora.Desktop/Views`):
- `UnitSettingsView.xaml` 272.
- `ShellWindow.xaml` 204.
- `HealthRecordsView.xaml` 158.
- `CommunicationView.xaml` 142.
- `SettingsView.xaml` 130.
- `CapacityView.xaml` 87.
- `UnitIndicatorsView.xaml` 83.
- `OperationalOverviewView.xaml` 82.
- `ObsUnitRegistryView.xaml` 66.
- `FamilyView.xaml` 66.
- `AgendaView.xaml` 63.
- `UnitLightningSlotsView.xaml` 62.
- `GoalsView.xaml` 60.
- `HealthUnitsView.xaml` 57.
- `ItemDetailsWindow.xaml` 57.
- Muitos têm uma linha por controle e comprimento horizontal enorme. A contagem de linhas subestima o tamanho real (por exemplo `AgendaView.xaml` e `OperationalOverviewView.xaml`).

**Maiores code-behind e C#:**
- `ShellWindow.xaml.cs` 172.
- `MunicipalView.xaml.cs` 132 (monta as telas da Secretaria).
- `RetentionSettingsView.xaml.cs` 183.
- `EvolutionChartView.xaml.cs` 143.
- `IndicatorChartWindow.xaml.cs` 105.
- `NetworkMapView.xaml.cs` 97.
- `ObsLocationDialog.xaml.cs` 83.
- `SettingsView.xaml.cs` 79.
- `ShellWindow.Search.cs` 139 e `.Filters.cs` 91.

**Maiores ViewModels** (`Nora.Desktop/ViewModels`):
- `WorkspaceViewModel.Catalog.cs` 526.
- `WorkspaceViewModel.NetworkScheduling.cs` 520.
- `WorkspaceViewModel.cs` 467.
- `WorkspaceViewModel.Devices.cs` 427.
- `WorkspaceViewModel.ObsUnits.cs` 333.
- `HealthRecordsViewModel.cs` 324.
- `WorkspaceViewModel.Communication.cs` 324.
- `WorkspaceViewModel.UnitMetrics.cs` 294.
- `ShellViewModel.cs` 285.
- `WorkspaceViewModel.Units.cs` 277.
- `WorkspaceViewModel.Metrics.cs` 269.
- `WorkspaceViewModel.Appointments.cs` 260.
- `WorkspaceViewModel.UnitData.cs` 253.
- `WorkspaceViewModel.Lightning.cs` 249.

**Outros:**
- `Services/NoraApiClient.cs` 514.
- `tests/Nora.Desktop.Checks`: maiores `ReviewFixesChecks.cs` 510, `ApiFixture.cs` 510, `ObsDeviceChecks.cs` 494, `ApiClientChecks.cs` 416, `UnitIndicatorsChecks.cs` 389, `InterfaceChecks.cs` 377.

---

## Pontos de atenção para o redesenho (fatos, sem proposta)

- A barra de título é nativa. Qualquer barra própria exigiria tocar `ShellWindow.xaml:1`, `FullScreenService.cs`, `ApplyWindowCorners` (`ShellWindow.xaml.cs:121`) e o `desktop-startup.ps1`, que lê `MainWindowTitle`.
- "NORA OPERAÇÃO UBS" é a composição de dois textos (`ShellWindow.xaml:25`, `ShellViewModel.cs:23`).
- `SidebarExpanded` e o atalho Ctrl+B não afetam o layout. A barra só tem largura variável de 208 a 380.
- `SectionSummary` e `FloatingActions` existem sem uso.
- Na Secretaria, as telas são montadas em código (`MunicipalView.xaml.cs`), não em XAML.
- A chave `SubPage.Key` filtra por visibilidade. A Vaga Relâmpago filtra no ViewModel.
- O botão "Filtros" só aparece com `PageFilterGroup`. A Vaga Relâmpago do OBS não usa.
- Cores de status da Agenda e do `OutcomePie` estão fixas, fora dos temas.
- Os testes dependem de `AutomationProperties.Name`, `x:Name` e do texto dos botões. Renomear esses itens quebra os checks.
- `ObsDeviceStore.ComputersEnabled` é falso por padrão, mas a subaba "Computadores" e as chamadas relacionadas continuam no código e no menu.
- No Android, o token não persiste. Nos desktops, a duração real (24 h, 30 dias, 8 h) é decidida só na API (`AuthService.cs:98-99`). O rótulo "30 dias" da tela é texto fixo.
