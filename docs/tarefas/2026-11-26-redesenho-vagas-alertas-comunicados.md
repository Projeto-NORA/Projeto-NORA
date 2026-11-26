# Redesenho do OBS: Vaga Relâmpago, Alertas e Comunicados — 2026-11-26

Parte do redesenho dos desktops ([pedido](2026-11-26-redesenho-desktops-dados-capturas.md) e [especificação](2026-11-26-redesenho-especificacao.md)). Este grupo é de um fluxo de oito agentes em paralelo; cada um mexe só nas suas telas. Branch deste grupo: `worktree-wf_ca153789-8ce-4` (parte de `claude/desktop-redesign-ui`, que traz a fundação).

## Skills e comandos invocados

- Nenhum comando de usuário nesta tarefa. Foram lidas as skills `list-generator` e `selecao-listagem` (padrões da listagem aberta, do balão e da seleção) e o `CLAUDE.md` global.

## O que foi pedido (itens F1 a F3 e G2, mais os itens A que valem para toda tela)

- **F1** Vaga Relâmpago (Em andamento e Encerradas): sem título e sem os totais (abertas, em oferta, aceitas, recusadas, vencidas).
- **F2** Filtros em cima à direita (botão Filtros do cabeçalho: especialidade, situação, pesquisa).
- **F3** Listagem no padrão novo: `OpenList` na tela inteira; título com horário e especialidade; linha `profissional | paciente | prazo` (contagem regressiva por segundo, em vermelho nos últimos 2 minutos); etiqueta de situação à direita; só leitura, sem balão; duplo clique ou Enter abre a ficha em linhas (resumo e sequência de ofertas). O temporizador de 1 s não recria a lista nem derruba a seleção.
- **G2** Alertas: só a `OpenList` (tipo e especialidade; valor, limite e "desde"; etiqueta do tipo), sem títulos nem textos explicativos; "Nenhum alerta" (com a hora) e falha de leitura continuam distintos; pesquisa em linha.
- **G2** Comunicados: só a `OpenList` (título; publicação e validade; etiqueta "Não lido" ou "Lido em …"); balão com "Marcar como lido" (vale para vários); duplo clique abre o texto completo (texto puro, quebras de linha preservadas); o distintivo de não vistos no menu continua.
- **A (geral)**: nenhum título repetido nem texto de apresentação; sem card em volta de listagem; ações só no balão; sem botão "Atualizar" (F7: a tela relê ao entrar, no ciclo de 20 s e depois de cada ação); cores por `DynamicResource`; aviso `ObsUnitNoticeView` mantido; estados vazio/carregando/erro; regra de negócio, API e textos de acessibilidade preservados.

## Objetivo

As três telas do OBS no padrão novo da fundação, validadas por build, checagens e capturas dos dois temas.

## Restrições

- Só as telas deste grupo; não mexer em `Themes/*`, `ShellWindow*`, componentes da fundação, API, app, scripts e `docs/` (exceto este arquivo).
- Extensão de arquivo compartilhado só se indispensável, compatível e listada no retorno.
- Pendências novas usam P-105 a P-109.
- Não rodar `desktop-startup.ps1`, não abrir os programas, não usar `git stash`, não fazer push.

## O que mudou, tela por tela

### Vaga Relâmpago (Em andamento e Encerradas)

- `UnitLightningSlotsView` é uma `Grid` com `PageLayout.Fill`: aviso da UBS, filtros, a `OpenList` na tela inteira e, embaixo, o rodapé do histórico. Saíram o título, o texto, o botão de atualizar, a faixa com os cinco totais, o cartão "Filtros" e a `DataGrid`.
- Item: título = horário da vaga + especialidade; linha de dados = `profissional | paciente | prazo xx:xx` (só o que está informado; o prazo é um `Run` que muda a cada segundo e fica vermelho e em negrito nos últimos 2 minutos); `StatusTag` à direita (Aberta azul, Em oferta turquesa, Aceita verde, Encerrada neutra, "Horário ocupado" e "Menos de 10 min" amarelas).
- Filtros (especialidade, situação, pesquisa) num `PageFilterGroup` de chave `slots` (botão Filtros do cabeçalho); "Limpar filtros" os restaura (chave nova em `WorkspaceViewModel.PageFilters.cs`).
- Só leitura: não há balão visível. O `ListingBehavior` exige um balão, então a lista usa um `SelectionBalloon` dentro de um `Border` sempre recolhido (comentário no XAML); assim valem segundo clique, Enter, Esc e a seleção que sobrevive à releitura. O abridor de ficha é trocado por um que abre a ficha da vaga (`shell.OpenItemDetailsAsync(row.Slot)`, que lê as ofertas antes).
- Estável com o temporizador de 1 s e o ciclo de 20 s: `SlotPanelRow` ganhou `Id` (chave) e passou a ser **reaproveitada** quando a vaga não mudou; `SlotPanelRows` e `FilteredSlotPanelRows` são atualizadas no lugar (`Replace`), então a lista da tela não recebe evento nenhum quando nada mudou. Só o `DeadlineText` de cada linha muda por segundo.
- Estados: "Nenhuma vaga cadastrada nesta UBS até agora." (UBS sem vaga), "Nenhuma vaga em andamento." / "Nenhuma vaga encerrada." (subtela vazia) e "Nenhuma vaga encontrada para esta busca." (filtro escondeu tudo) são distintos (`SlotsEmptyText`); indicador na cor da marca com "Lendo as vagas da UBS…" enquanto a primeira leitura não chega.
- Removidos: `LoadUnitLightningSlotsAsync`, `SelectedUnitSlot`, `SelectedSlotRow`, `ReselectSlot`, `HasNoFilteredSlots`, os cinco `Panel*Count` e `OffersSummary`/`TimeDisplay` da linha. "Carregar mais histórico" continua (é ação de quem lê, não "atualizar"): rodapé discreto com a frase e o botão.

### Alertas

- `UnitAlertsView` (hospedada direto pela `HomeView`, com a nova `AlertsPage`): `OpenList` na tela inteira; item = `tipo · especialidade` (a especialidade só quando existe), `Valor: … | Limite: … | Desde: …` (o "desde" só quando existe) e `StatusTag` do tipo (Fila amarela, Espera violeta, Faltas rosa, Ocupação azul, Meta turquesa). Pesquisa em linha no cabeçalho (`PageFilterGroup` de pesquisa); sem botão Filtros.
- Estados da lista: "Nenhum alerta. Nenhum critério (…) passou do limite na leitura das HH:mm." (com o visto verde), "Não foi possível avaliar os alertas agora…" (falha sem leitura anterior), "Nenhum alerta encontrado para esta busca." e, com alertas na tela e a última avaliação falhando, um aviso amarelo acima da lista. Sem alerta nem texto explicativo; saíram o título "O que passou do limite", o cabeçalho da UBS e a nota "Avaliado às …".
- `AlertRow` virou `partial` (os textos da listagem estão em `Models/AlertRow.Display.cs`) e ganhou `Kind` opcional no fim do construtor.
- F7: saíram o botão e `RefreshAlertsAsync`. A tela de alertas passa a reavaliar a cada ciclo (a leitura vale por 15 s com a tela aberta e continua 5 minutos fora dela, só para o aviso do menu).

### Comunicados

- `AnnouncementsView`: só a `OpenList` na tela inteira; item = título, `Publicado em … | Vale até …` (a validade só quando existe), `StatusTag` "Não lido" (azul) ou "Lido em …" (verde). Pesquisa em linha. Saíram o título, o resumo, a nota de rodapé, o cartão de estado e os cartões de cada comunicado.
- Balão "Marcar como lido" (vale para vários): `MarkAnnouncementsReadAsync` marca só os não lidos numa única ação, sem recriar a lista; o botão só aparece quando há algum não lido entre os selecionados e o balão é avisado (`RefreshActions`) porque, com a área de ações escondida, ele não percebe sozinho um botão que reaparece.
- Duplo clique, segundo clique ou Enter abre a ficha (`BuildDetails` ganhou o caso do comunicado, que não depende de UBS ativa): situação, publicação, validade e o **texto completo** como campo `Block` (rótulo em cima, texto embaixo, alinhado à esquerda, quebras de linha preservadas, rola quando passa da janela, botão de copiar).
- Estados: "Nenhum comunicado em vigor para as UBS desta conta.", "Carregando os comunicados…" com indicador, "Não foi possível ler os comunicados agora. Saia desta tela e volte para ler de novo." (primeira leitura que falhou), aviso amarelo acima da lista quando uma leitura posterior falha e "Nenhum comunicado encontrado para esta busca.". O distintivo de não vistos do menu não foi tocado.
- F7: saíram o botão e `LoadAnnouncementsAsync`; a leitura completa continua ao abrir a tela, e a incremental no ciclo.

### Compartilhado (menor extensão possível)

- `Models/ItemDetails.cs` e `Views/ItemDetailsWindow.xaml`: `DetailField.Block` (opcional, padrão falso) e o desenho do campo em bloco.
- `Views/WorkspaceView.xaml`: as duas `StackPanel` de raiz viraram `Grid` (e o cartão de login `VerticalAlignment="Top"`): sem isso a lista da Vaga Relâmpago (e a de Pacientes) não recebe a altura da área de conteúdo.
- `Views/HomeView.xaml`: `AlertsPage`; `Views/OperationalInsightsView.xaml`: saiu a linha que hospedava os alertas. `Views/SearchEmptyNotice.cs` (novo): aviso "nenhum … encontrado para esta busca" para listas com pesquisa em linha.
- `ViewModels/WorkspaceViewModel.PageFilters.cs`: chave `slots`.
- Checagens adaptadas (comportamento novo, nenhuma apagada): `CommunityChecks`, `EndToEndChecks`, `ListingChecks`, `UnitFlowChecks`, `RetryTextsChecks`, `ReviewFixesChecks`, `SchedulingInsightsChecks`; `Program.cs` chama o novo `VagasAlertasComunicadosChecks.cs` (6 checagens).

## Decisões onde a especificação não dizia

- A etiqueta da vaga é a situação **da vaga**; "Recusada" e "Vencida" são desfechos de **oferta** e ficam na ficha e nos filtros (P-108 pergunta se o usuário quer vê-los também na etiqueta).
- Tom das etiquetas: Aberta azul, Em oferta turquesa (o pedido dá "ofertas turquesa"), Aceita verde, Encerrada neutra; alerta por tipo; comunicado não lido azul e lido verde.
- Pesquisa de Vagas dentro do botão Filtros (como o pedido diz: "especialidade, situação, pesquisa" no botão); Alertas e Comunicados só têm pesquisa, então o campo com lupa fica direto no cabeçalho.
- Alertas reavaliam a cada ciclo com a tela aberta (F7 pede que a tela atualize sozinha; antes só a cada 5 minutos).
- O texto de falha de comunicados não cita botão; diz para sair da tela e voltar, que é o que de fato refaz a leitura completa.

## Validação

- `dotnet build NORA.slnx --configuration Release -m:1`: 0 erros.
- Checagens do desktop: ver o final deste arquivo (números da última rodada).
- Capturas (claro e escuro, com dados, vazio, seleção, ficha, filtros, falha, janela de 980): `.build/redesenho/vagas-alertas-comunicados/`.

## Pendências (P-105 a P-109)

- **P-105** `WorkspaceViewModel.IsAlertsPage` (e a sua entrada na lista de notificações) ficou sem uso depois que os alertas deixaram de ser hospedados pela `OperationalInsightsView`; remover na consolidação, junto com `PageTitle`/`PageSubtitle` se o agente de Indicadores também os aposentar.
- **P-106** `SelectionBalloon` só percebe a mudança de visibilidade de um botão que já está na tela. Se uma tela ajusta botões no `BalloonRefreshed` (como Comunicados) e nenhuma ação estava visível, a área de ações continua escondida; a tela precisa chamar `balloon.RefreshActions()` depois de ajustar (Comunicados chama). Vale avisar quem mais usa `BalloonRefreshed` (Agenda, Pacientes) ou ajustar o componente.
- **P-107** (teste que só o usuário faz) Abrir Vaga Relâmpago > Em andamento com uma oferta correndo e conferir o prazo andando a cada segundo e ficando vermelho nos últimos 2 minutos, sem a lista piscar e sem perder a seleção; abrir a ficha por duplo clique e por Enter. Em Comunicados, selecionar vários com Ctrl/Shift, clicar "Marcar como lido" e abrir um comunicado com várias linhas. Defeito = piscada, seleção perdida, texto cortado.
- **P-108** Decisão do usuário: etiquetar também "Recusada"/"Vencida" nas vagas encerradas (hoje a etiqueta é a situação da vaga, e o desfecho de cada oferta está na ficha)?
- **P-109** Lista só de leitura que precisa do `ListingBehavior` (segundo clique, Enter, seleção por chave) usa hoje um balão sempre escondido. Se aparecerem mais listas assim, tornar o balão opcional no `ListingBehavior`.
