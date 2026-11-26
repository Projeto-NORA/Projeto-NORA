# Redesenho: Configurações, Exclusão programada, Dados do perfil, Exames e vacinas e tela de acesso — 2026-11-26

Parte do [redesenho dos desktops](2026-11-26-redesenho-desktops-dados-capturas.md) (itens A1…A20) conforme a [especificação](2026-11-26-redesenho-especificacao.md). Grupo de telas "configuracoes-ficha-acesso": as que o OBS e o Painel Municipal compartilham e que nenhum outro grupo cobre. Pendências reservadas: P-125 a P-129.

## Skills e comandos invocados

- Nenhum comando do usuário nesta etapa. Tarefa dada por um fluxo de oito agentes em paralelo; este agente trabalha numa pasta própria (git worktree) e numa branch própria (`worktree-wf_ca153789-8ce-8`), a partir da fundação (`claude/desktop-redesign-ui`, trazida por fast-forward em `04ec09d`). Consultadas as skills `list-generator` e `selecao-listagem` (balão, seleção, rolagem aninhada).

## O que foi pedido para este grupo (fiel ao enunciado)

- **Configurações:** sem título repetido; seções sem card pesado (título de seção discreto e um fio); cards de escolha de tema mantidos com o hover azul bebê; nada de botão de atualizar; formulários (trocar senha, nome) em `FormGrid` com o primário à direita. A opção de confirmação ao fechar, os atalhos e as preferências continuam funcionando.
- **Exclusão programada** (dentro de Configurações): a lista de cadastros inativos vira `OpenList` com balão (cancelar a exclusão de um cadastro etc., com lixeira nas ações destrutivas) e ficha; a política geral continua num bloco curto com `FormGrid`.
- **Dados do perfil** (`StaffProfileWindow`): passa ao padrão dos pop-ups da marca (sem moldura, título, X no canto superior direito, fundo da janela escurecido, primário à direita), com nome, e-mail e função em linhas e a troca de senha num bloco próprio.
- **Ficha do paciente, aba "Exames e vacinas"** (`HealthRecordsView`): as listas de exames e de vacinas viram `OpenList` com balão (marcar como realizado, cancelar e anular com lixeira, e as confirmações e motivos atuais em pop-up); "Registrar exame" e "Registrar vacina" viram "+ Novo exame" e "+ Nova vacina" que abrem o formulário com `FormGrid` (um pop-up por vez: a ficha já é um pop-up); some o botão "Atualizar"; "Mostrar anulados" continua. Todas as regras e validações preservadas.
- **Tela de acesso (login) dos dois programas e troca obrigatória de senha:** não vira grade (é login, não cadastro). Só conferir o visual com a fundação nova (hover, textos do login salvo, botões da janela) e corrigir o que estiver desalinhado.
- **`ObsUnitNoticeView`:** aviso curto com o botão "Escolher UBS", no visual de `NoticeItem`.
- Itens A do pedido geral valem para todas as telas do grupo (sem título repetido, sem card em listagem e formulário, ações só no balão, lixeira branca em fundo vermelho, ficha em linhas ao duplo clique, hover azul bebê, sem "Atualizar", cor só por tema, estados vazio e carregando, regra de negócio preservada).

## Objetivo

As telas do grupo no padrão novo da fundação, sem perder nenhuma regra, mensagem ou nome de automação que continue fazendo sentido, com checagens adaptadas e novas e capturas conferidas nos dois temas.

## Restrições

- Mexer só nos arquivos deste grupo e nas checagens delas; não alterar temas, a janela principal, os componentes compartilhados da fundação, a API, o app, os scripts nem outros documentos (exceto este).
- Não tocar em `FormGrid.ColumnsFor`. Sem `git stash`, sem push, sem abrir os programas na tela do usuário.

## O que mudou, tela por tela

### Configurações (`SettingsView`)

- Sem card: cada seção (Foto de perfil, Aparência, Preferências da interface, Atalhos do teclado, Restaurar preferências padrão) tem um título discreto (15, semi-negrito) e um fio de 1 px (`DividerBrush`). Os blocos de escolha de tema continuam como blocos (hover azul bebê, `ButtonHoverBrush`); as linhas das preferências passam a `HoverBrush` no hover (A14).
- Saíram o texto de apresentação de Aparência e o texto "Foto de perfil" repetido dentro da seção; ficam só instruções curtas (atalhos). O nome da tela fica no cabeçalho.
- Layout: duas colunas (preferências à esquerda; atalhos e Exclusão programada à direita, o que equilibra as alturas); abaixo de 870 px a coluna da direita desce para baixo da esquerda, como antes. `ShortcutCard` virou `StackPanel` (nada de card).
- Nomes de automação e `x:Name` mantidos (`SettingsAvatar`, `PreferencesColumn`, `ShortcutColumn`, `ShortcutCard`, `ShortcutFeedback`, `RestoreConfirmation`, `ConfirmRestore`, "Aparência clara/escura/do sistema").

### Exclusão programada (`RetentionSettingsView`)

- Seção sem card, com o mesmo título discreto e fio. Política geral num bloco curto em `FormGrid` (a caixa "Nunca excluir sozinho"; ao desmarcar entra o prazo em dias e o primário "Salvar prazo"/"Salvar preferência" à direita). Nomes `NeverDelete`, `PolicyDays`, `PolicyActions`, `SavePolicy`, `Controls`, `Feedback`, `Retry` mantidos.
- A lista de cadastros desativados (antes um combo) é uma `OpenList` com item em duas linhas (nome; "Situação") e etiqueta à direita (Elegível azul, Programada rosa, Cancelada amarela, Protegido neutro). Balão com **Agendar exclusão** (cadastro elegível) e **Cancelar exclusão** (só com exclusão programada), ambos `ListActionDangerButton` (lixeira branca em vermelho). Ações valem para um item; com vários selecionados somem.
- **Agendar** pede o prazo num pop-up `DialogService.ShowForm` com `FormGrid` (30 dias de partida, de 1 a 36.500); depois vem a confirmação de sempre ("Agendar a exclusão de … para daqui a N dias?"). Recusar a confirmação não agenda e mantém o pop-up aberto. **Cancelar exclusão** e **Salvar prazo** usam a confirmação de sempre.
- Duplo clique, segundo clique e Enter abrem a ficha em linhas (`ItemDetailsWindow`: cadastro, tipo, situação, detalhe, exclusão programada para, pode ser excluído).
- As confirmações passaram do `MessageBox` do Windows para o diálogo da marca (`Confirmation.Ask`), com o mesmo texto.
- Releitura: ao entrar na tela, a cada 20 s (quieta: sem desabilitar controles, sem mexer no prazo que a pessoa está digitando, falha em silêncio, lista só trocada se mudou) e logo depois de cada ação. A lista rola sozinha e passa a roda do mouse à rolagem da tela no começo e no fim (`NestedListScroll`).
- Estados: "Nenhum cadastro desativado." (vazio), passo escrito com indicador na cor da marca (carregando), mensagem e "Tentar novamente" (erro).

### Dados do perfil (`StaffProfileWindow`)

- Pop-up da marca no mesmo desenho do `FormDialog`: sem moldura, cantos 16, título e X no canto superior direito (`IsCancel`, Esc), arrasta pelo título, e o fundo da janela principal escurece enquanto está aberto (a própria janela liga e desliga o `IDialogHost.SetDimmed`, porque quem a abre, `ShellWindow.OnOpenProfile`, chama `ShowDialog` direto e é arquivo da janela principal).
- Nome completo (editável), e-mail (só leitura), função e acesso em linhas (rótulo à esquerda, valor à direita, fio entre as linhas). "Salvar nome" à direita. A troca de senha é um bloco próprio ("Alterar senha") com o `ChangePasswordView` em `FormGrid`. Nomes `FullName`, `Email`, `ScopeText`, `ProfileFields`, `ProfileStatus`, `ProfileProgress` mantidos; o "Fechar" de baixo saiu (o X o substitui).

### Troca de senha (`ChangePasswordView`) e troca obrigatória (`PasswordChangeRequiredView`)

- `ChangePasswordView`: três campos em `FormGrid` (largura igual) e o primário "Salvar nova senha" embaixo à direita; propriedade `Secondary` para um botão à esquerda do primário. Sem largura fixa de 420.
- Troca obrigatória: sem card e sem o título "Defina uma nova senha" (o cabeçalho já diz "Definir nova senha"); fica a instrução curta e o formulário; **Sair** à esquerda do primário.

### Exames e vacinas (`HealthRecordsView` e `HealthRecordsViewModel`)

- Duas `OpenListLow` (Exames e Vacinas) com item em duas linhas (nome; dados informados com " | ": data, observação, anulação, dose) e etiqueta de situação à direita (Solicitado azul, Realizado e Aplicada verde, Cancelado amarelo, Anulado neutro). Nomes de automação das listas mantidos ("Exames do paciente", "Vacinas do paciente").
- Balão: **Marcar como realizado** (só exame solicitado), **Cancelar exame** (lixeira, só solicitado) e **Anular** (lixeira, não anulado); na lista de vacinas, **Anular**. Só um balão por vez (selecionar uma vacina solta o exame, como antes). Cadastro desativado: nenhuma ação, aviso no visual de `NoticeItem`.
- "Registrar exame" e "Registrar vacina" viraram **Novo exame** e **Nova vacina** (`NewItemButton`, à direita do "Mostrar anulados"). **Decisão:** como a ficha já é um pop-up, o formulário abre **no lugar das listas, dentro dela** (um pop-up por vez), com título, X, `FormGrid`, **Cancelar** e o primário à direita. O mesmo vale para a data de "Marcar como realizado" e para o motivo de "Anular" (a confirmação de sempre continua sendo um diálogo da marca por cima). Esc e Cancelar descartam o rascunho e voltam às listas; erro mantém o formulário aberto com o aviso junto ao campo.
- Duplo clique, segundo clique e Enter abrem a **ficha em linhas do registro**, também no lugar das listas (uma por vez), com "Voltar às listas".
- Sem botão "Atualizar": a aba lê ao abrir, a cada 20 s (`AutoRefreshAsync`, quieta, só com as listas à mostra e a ficha livre; não liga o indicador de leitura, não pisca o balão, só troca a lista se mudou), depois de cada ação e ao marcar "Mostrar anulados". Os textos que citavam "Use Atualizar" passam a dizer que o NORA tenta de novo sozinho. Indicador na cor da marca enquanto lê ou grava.
- `RunAsync` devolve se concluiu; as operações públicas devolvem `Task<bool>` e, dando certo com um formulário aberto, voltam às listas.

### Tela de acesso (login) e telas protegidas

- `DesktopAccessView` (login dos dois programas): continua cartão centralizado (não é grade). Conferido com a fundação nova (botões da janela, texto do login salvo "Sem marcar, você continua conectado por 1 dia neste computador.", sem o nome do programa na barra de título, janela mínima 980×640). Corrigido: a marca "N" desenhada trocada pela logo (`BrandMarkImage`) e a caixa "Manter conectado por 30 dias" colada ao texto (`HorizontalAlignment="Left"`).
- `OperationalAccessView` (tela protegida sem sessão, só em prévias e testes): sem o título e a descrição repetidos e sem o cartão com hex fixos; fica um `NoticeItem` com o aviso e o botão "Ir para acesso". Saíram `AccessRequiredTitle` e `AccessRequiredDescription` do `WorkspaceViewModel` (sem uso).
- `ObsUnitNoticeView`: `NoticeItem` amarelo curto com o texto e o botão "Escolher UBS" à direita.

## Arquivos

Telas: `SettingsView`, `RetentionSettingsView`, `StaffProfileWindow`, `ChangePasswordView`, `PasswordChangeRequiredView`, `HealthRecordsView`, `DesktopAccessView`, `OperationalAccessView`, `ObsUnitNoticeView` (`.xaml` e `.cs`). Novos: `Views/HealthRecordConverters.cs` (linha de dados com " | " e tom da etiqueta), `Views/NestedListScroll.cs` (roda do mouse numa lista dentro de tela que rola). ViewModels e modelos: `HealthRecordsViewModel`, `Models/RetentionModels.cs` (etiqueta e tom), `WorkspaceViewModel.cs` (remoção das duas propriedades sem uso). Checagens: `tests/Nora.Desktop.Checks/ConfigAccessChecks.cs` (novo), `ApiFixture.Retention.cs` (novo: política, cadastros desativados e agendamentos na fixture) e adaptações em `ApiFixture.cs` (uma linha), `CommunityChecks.cs`, `ListingChecks.cs` e `PasswordChecks.cs` (uma linha que chama as checagens novas).

## Checklist

- [x] Documentos lidos, build e checagens de base (244 PASS e 1 FAIL conhecido, o P-066)
- [x] Configurações
- [x] Exclusão programada
- [x] Dados do perfil
- [x] Exames e vacinas
- [x] Troca obrigatória de senha e `ChangePasswordView`
- [x] Tela de acesso (conferência visual) e `OperationalAccessView`
- [x] `ObsUnitNoticeView` no visual de `NoticeItem`
- [x] Checagens adaptadas e novas
- [x] Capturas nos dois temas, com dados e vazio, conferidas
- [x] Loop de verificação final sem achados
- [x] Commit na branch do grupo

## Estado

- 26/11/2026: fundação trazida por fast-forward (`04ec09d`); linha de base: build com 0 erros, checagens **244 PASS e 1 FAIL** (o P-066, que só falha na suíte completa).
- 26/11/2026: telas redesenhadas; checagens novas em `ConfigAccessChecks.cs` (13: Configurações e Exclusão programada nos dois perfis, Exclusão programada vazia e com falha, Exames e vacinas, Dados do perfil nos dois perfis, troca obrigatória nos dois perfis, tela de acesso nos dois perfis, aviso de UBS e tela protegida) e adaptadas as que dependiam do desenho antigo (Jornada da ficha: balão e formulário no lugar das listas; orientação do OBS desconectado: a tela protegida não repete mais o nome da área).
- **Resultado final:** build da solução com **0 erros**; checagens do desktop **256 PASS de 258**: o P-066 (conhecido) e "Janela: maximizada ocupa a área de trabalho", que falha só quando outros programas disputam o desktop (várias suítes WPF rodam ao mesmo tempo nesta máquina) e **passa sozinha**. O "Enter respeita o seletor aberto…" falhou numa rodada pelo mesmo motivo e também passa sozinho.
- Capturas conferidas (claro e escuro, com dados e vazio, janela mínima) em `.build/redesenho/configuracoes-ficha-acesso/`: `{obs,municipal}-configuracoes-*`, `-exclusao-programada-{claro,escuro}-0N`, `obs-exclusao-programada-vazia-*`, `-dados-do-perfil-*`, `-troca-obrigatoria-*` (e `-janela-minima`), `-acesso-{claro,escuro,janela-minima}`, `obs-aviso-ubs-*`, `obs-acesso-protegido-claro`, `obs-ficha-exames-vacinas-*`, `obs-ficha-novo-exame-*`, `obs-ficha-nova-vacina-claro`, `obs-ficha-anular-claro`, `obs-ficha-registro-claro`.
- Loop de verificação: conferido item a item contra o pedido; varridos compilação e referências (chaves de recurso, nomes de automação), lógica (listas vazias, seleção, duplo clique, formulário aberto durante a releitura), temporizadores (parados no `Unloaded`), handlers (registrados uma vez) e código sem uso (saíram `AccessRequiredTitle` e `AccessRequiredDescription`). Achados corrigidos durante o trabalho: marca d'água da janela aparecendo atrás do cartão de login, caixa do login longe do texto, linha de status do pop-up Dados do perfil ocupando espaço vazio, indicador de leitura ligando na releitura automática (os botões do balão piscavam desabilitados) e o balão que só atualizava depois de um quadro quando a lista era trocada (ajuste só nas checagens, que não passam quadros).

## Decisões onde a especificação não dizia

- **Formulário da aba Exames e vacinas abre no lugar das listas, dentro da ficha** (e não como segundo pop-up por cima), por causa do "um pop-up por vez". O mesmo vale para a data de "Marcar como realizado" e o motivo de "Anular". A confirmação continua sendo o diálogo da marca por cima (não é formulário).
- **Ficha do exame ou da vacina (duplo clique)** também abre em linhas no lugar das listas, com "Voltar às listas" (abrir uma segunda `ItemDetailsWindow` empilharia dois pop-ups).
- **Agendar exclusão e Cancelar exclusão** são ambos `ListActionDangerButton` (lixeira): a regra A manda cancelar e remover sempre em vermelho, e agendar leva à exclusão definitiva. Os rótulos os distinguem.
- **Ações de um item por vez** nos balões de Exclusão programada e de Exames e vacinas (somem com vários selecionados). Não há lote porque cada ação pede dado próprio (prazo, data, motivo).
- **Exclusão programada na coluna da direita** de Configurações (abaixo dos atalhos): equilibra as alturas das duas colunas.
- **Seção "Exclusão programada"** (e não "Exclusão de itens desativados"), como o usuário chama a tela; o título das confirmações segue o mesmo nome.
- **Política geral:** `FormGrid` com as linhas inteiras (`FullRow`): é um bloco curto de um campo só, e um campo sozinho centralizado no meio da página ficaria deslocado do texto ao redor.
- **Dados do perfil escurece o fundo sozinho** (a janela liga e desliga o `SetDimmed`), porque quem a abre é a janela principal (`ShellWindow.OnOpenProfile`, que não era para mexer) e chama `ShowDialog` direto.
- **Tela de acesso:** continua cartão centralizado (não é cadastro); só trocada a marca desenhada pela logo, colada a caixa ao texto e pintado o fundo (`BackgroundGradientBrush`), porque a marca d'água dos desenvolvedores, que mora na rolagem da janela principal, aparecia atrás do cartão.

## Mudanças em arquivos compartilhados (para a integração)

- `tests/Nora.Desktop.Checks/ApiFixture.cs`: uma linha (a leitura da exclusão programada passa para `HandleRetentionAsync`, em `ApiFixture.Retention.cs`, novo).
- `tests/Nora.Desktop.Checks/ListingChecks.cs`: três linhas da checagem "OBS desconectado: cada aba mantém sua própria orientação" (Pacientes, Vagas e Informações da UBS: o texto esperado passa a ser o aviso da tela protegida, e não mais o nome da área, que agora fica só no cabeçalho).
- `tests/Nora.Desktop.Checks/CommunityChecks.cs`: a checagem da Jornada (balão, formulário do motivo, nomes das listas).
- `tests/Nora.Desktop.Checks/PasswordChecks.cs`: uma linha que chama `RunConfigAccessChecks` ao fim de `RunPasswordChecks`.
- `Nora.Desktop/ViewModels/WorkspaceViewModel.cs`: saem `AccessRequiredTitle` e `AccessRequiredDescription` (e seus `nameof`) por falta de uso. Nenhum arquivo de tema, da janela principal ou de componente da fundação foi alterado.

## Pendências (P-125 a P-129, para consolidar em `PENDENCIAS.md`)

- **P-125** (teste sugerido ao usuário): conferir na tela real, nos dois programas, Configurações (hover azul bebê nos blocos de tema, Exclusão programada na coluna da direita com um cadastro desativado: balão, Agendar exclusão com o pop-up do prazo, ficha ao Enter), o pop-up Dados do perfil (X, Esc, fundo escurecido, troca de senha), a aba Exames e vacinas (Novo exame, Marcar como realizado, Anular, duplo clique, 20 s sem piscar) e o login (cartão, logo, caixa "Manter conectado por 30 dias"). Defeito: algo cortado, sobreposto, balão que pisca ou some, ou fundo do login diferente do resto.
- **P-126** (decisão do usuário): o balão de Exclusão programada e o de Exames e vacinas valem para um item por vez. Se quiser lote (por exemplo, cancelar a exclusão de vários cadastros de uma vez, com uma confirmação só), é uma tarefa nova.
- **P-127** (arquivo da janela principal): a marca d'água "Elaborado por…" mora na rolagem da página e aparece atrás de telas que ficam por cima dela na janela principal (a de acesso já se protege pintando o próprio fundo). Esconder a marca d'água enquanto `ShowCompactAccess` for verdadeiro em `ShellWindow.xaml` resolve na origem.
- **P-128** (arquivo da janela principal): `ShellWindow.OnOpenProfile` abre `StaffProfileWindow` com `ShowDialog()` direto; hoje o escurecimento do fundo é feito pela própria janela. Se a integração preferir o padrão de `DialogService.ShowModal(dialog, this)`, basta trocar essa linha e tirar o `Loaded`/`Closed` de `StaffProfileWindow`.
- **P-129** (checagens): a suíte completa tem duas checagens que dependem do foco e do monitor reais ("Enter respeita o seletor aberto…" e "Janela: maximizada ocupa a área de trabalho…") e falham quando outros programas disputam o desktop; passam sozinhas. Vale isolá-las do foco real.
