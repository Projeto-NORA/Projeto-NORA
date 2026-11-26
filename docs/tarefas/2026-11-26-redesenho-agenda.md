# Redesenho da Agenda do OBS — 2026-11-26

Parte "OBS: Agenda" do redesenho dos desktops. Pedido geral, itens E1 a E5 e as regras da seção A em
[2026-11-26-redesenho-desktops-dados-capturas.md](2026-11-26-redesenho-desktops-dados-capturas.md); como fazer e os nomes dos componentes em
[2026-11-26-redesenho-especificacao.md](2026-11-26-redesenho-especificacao.md) (seção "Agenda") e em [../design.md](../design.md).

## Skills e comandos invocados

- Fluxo de redesenho por grupos de telas (um agente por grupo, em worktree próprio); skills do usuário `list-generator` e `selecao-listagem` como referência de listagem e seleção.

## Onde o trabalho acontece

Worktree `.claude/worktrees/wf_ca153789-8ce-3`, branch `worktree-wf_ca153789-8ce-3`, partindo da fundação (`claude/desktop-redesign-ui` em `04ec09d`). Pendências reservadas a este grupo: P-100 a P-104.

## O que foi pedido, item por item (grupo "OBS: Agenda")

- **Cabeçalho:** pesquisa em linha (filtra as consultas listadas); alternador `Dia | Semana | Mês | Ano` em linha, lembrado nas preferências; em `PageActions` o botão **+ Agendar nova consulta** (texto mantido) que abre o agendamento em pop-up (`ShowForm` + `FormGrid`): com profissional ativo, especialidade, profissional e horário livre; sem profissional ativo, os campos de texto livre atuais. Regras, leituras e mensagens do agendamento preservadas (catalog-required, horário ocupado, sobreposição em outra UBS…); o erro aparece dentro do pop-up.
- **Navegação de período** (‹ ›, "Hoje") e o rótulo do período no topo da tela, discretos, sem card.
- **Dia:** a lista das consultas do dia em ordem de horário.
- **Semana:** grade com os sete dias no topo e as horas na lateral (só a faixa de horas com consulta ou o horário de atendimento, com rolagem); cada consulta é um bloco no seu dia e horário, colorido pela situação, com nome e horário; clicar seleciona, duplo clique abre a ficha; consultas simultâneas lado a lado.
- **Mês:** o calendário de hoje, sem card. Dia selecionado: fundo azul bem claro na célula e círculo azul bebê em volta do número (legível); hover azul bebê; hoje com contorno; fora do mês esmaecido; bolinhas de situação mantidas. Clicar no dia mostra abaixo a lista do dia.
- **Ano:** 12 meses em grade 4×3, cada um um mini-calendário com o nome do mês e os dias com consulta marcados; clicar num mês abre a visão Mês nele; clicar num dia abre a visão Dia nele. Ler o ano inteiro sem estourar 366 dias por pedido e sem repetir leituras a cada ciclo (guardar o que já leu e reler só o necessário).
- **Lista do dia** (Dia e Mês): `OpenList` sem título de data; item em duas linhas (paciente; "08:20 às 08:40") e, à direita, `StatusTag` (Agendada, Chamada, Concluída, Falta, Cancelada) nas cores do tema. Balão conforme a situação: Chamar, Concluir, Registrar falta e Cancelar (lixeira, com a confirmação atual); seleção de vários só oferece o que vale para todos.
- **Duplo clique:** ficha em linhas, em seções (Paciente: nome, CPF; Consulta: especialidade, profissional, consultório, data, horário, situação; Registro: marcada por, presença confirmada, histórico se a API trouxer).
- **Legenda** de cores pequena, numa linha, só nas visões que usam cor.
- Regras gerais A (sem título repetido, sem card, balão, F7 sem "Atualizar", cor só por tema, aviso de UBS, estados vazio/carregando/erro, tema escuro) valem para toda a tela.

## Objetivo

Agenda do OBS no padrão novo (cabeçalho com pesquisa e período, quatro visões, listagem aberta com balão, agendamento em pop-up, ficha em seções), sem perder nenhuma regra de negócio do agendamento.

## Restrições

- Mexer só nas telas da Agenda e nas checagens dela; não alterar `Themes/*`, `ShellWindow*`, componentes compartilhados, `Nora.Api`, `APPNORA`, `scripts/` nem `docs/` além deste arquivo. Extensões compartilhadas só se indispensáveis, e listadas.
- Não tocar em `FormGrid.ColumnsFor`. Sem push, sem `git stash`, sem abrir os programas na tela do usuário.
- Preservar chamadas de API, confirmações, mensagens de erro e textos de acessibilidade que continuarem fazendo sentido.

## Checklist

- [x] Fundação trazida (fast-forward) e solução compilando
- [x] Modelo da agenda: cache por dia, leitura do ano em blocos de até 6 meses, período lembrado
- [x] Cabeçalho: pesquisa, alternador de período, + Agendar nova consulta (pop-up)
- [x] Navegação ‹ › Hoje e rótulo do período
- [x] Visão Dia
- [x] Visão Semana (grade com blocos)
- [x] Visão Mês (círculo no dia selecionado, hover, hoje, lista do dia)
- [x] Visão Ano (4×3 com mini-calendários)
- [x] Balão: Chamar, Concluir, Registrar falta, Cancelar (lixeira) e lote com uma confirmação
- [x] Ficha em seções (Consultório e histórico de mudanças: a API não traz, ver P-100)
- [x] Legenda numa linha só nas visões com cor
- [x] Sem botão Atualizar; sem hex fixo nas telas
- [x] Checagens adaptadas e novas; build com 0 erros; sem FAIL novo
- [x] Capturas nos dois temas (com dados, vazio, balão, pop-up, ficha) conferidas
- [x] Loop de verificação sem achados

## O que mudou, tela a tela

- **Cabeçalho (`AgendaView.xaml`):** `PageFilterGroup` de pesquisa (filtra Dia, Mês e a grade da Semana, que compartilham a mesma lista; some no Ano, que não lista consultas), `SegmentedToggle` Dia | Semana | Mês | Ano em linha (`AgendaViewMode`, lembrado em `DesktopPreferences.AgendaPeriod` por `ShellViewModel.AgendaPeriod.cs`) e `PageActions` com **+ Agendar nova consulta**. Nenhum título repetido, nenhum card, nenhum botão Atualizar.
- **Período:** Hoje, ‹ e › juntos e o rótulo do período depois deles, no topo da tela, sem card; a legenda de cores fica numa linha, em `Viewbox` (encolhe em vez de quebrar na janela de 980 px), e só aparece onde há cor de situação (Dia, Semana e Mês).
- **Dia:** `OpenList` das consultas do dia em ordem de horário, item em duas linhas (paciente; "08:20 às 08:40") e `StatusTag` à direita (`AppointmentToneConverter`).
- **Semana:** `AgendaWeekPanel` (painel próprio de uma lista de seleção) com os sete dias no topo, horas na lateral (só a faixa com consulta ou horário de atendimento, no mínimo 6 h, com rolagem), blocos coloridos pela situação, simultâneas lado a lado; clique seleciona, duplo clique abre a ficha. Cabeçalho do dia abre a visão Dia.
- **Mês:** calendário sem card; dia selecionado com fundo azul bem claro e círculo azul bebê no número, hover azul bebê, hoje com contorno, fora do mês esmaecido, bolinhas de situação; abaixo, a lista do dia.
- **Ano:** 12 mini-calendários 4×3 (`AgendaYearMonth`), dias com consulta marcados por um ponto azul e, com mais consultas, por um fundo azul claro (3 a 5) ou forte (6 ou mais); mês abre a visão Mês, dia abre a visão Dia.
- **Agendar:** pop-up (`DialogService.ShowForm` + `AgendaBookingForm`, `FormGrid` em 2 colunas): com profissional ativo, paciente, especialidade, profissional e horário livre; sem profissional ativo, os campos de texto livre. O erro (horário ocupado, sobreposição, `catalog-required`…) aparece dentro do pop-up, que só fecha quando a consulta é marcada.
- **Balão:** Chamar, Concluir, Registrar falta e Cancelar (lixeira branca em fundo vermelho, com a confirmação de antes), só o que vale para todas as consultas selecionadas; lote com uma confirmação.
- **Ficha:** seções Paciente (nome, CPF), Consulta (especialidade, profissional, data, horário, situação) e Registro (marcada por, presença confirmada).
- **Dados:** cache de consultas por dia; Dia, Semana e Mês leem o calendário do mês (uma requisição); o Ano lê os 12 meses em pedidos de até 6 meses (abaixo dos 366 dias aceitos) e, nos ciclos de 20 s, só o mês escolhido, o de hoje e o que passou de 5 minutos. A tela relê ao entrar, no ciclo e depois de cada ação.

## Decisões onde a especificação não dizia

- Consultas simultâneas dividem a coluna em faixas iguais; quando a faixa fica estreita (menos de 86 px) o bloco mostra só o nome (cortado) e o horário e os detalhes ficam na dica e na ficha.
- A mensagem de horários livres que falham e a do cadastro indisponível deixaram de citar "Atualizar agenda": a busca é repetida a cada leitura da agenda.
- O Ano marca a quantidade em vez das bolinhas de situação: 1–2 consultas um ponto azul sob o número, 3–5 o ponto e um fundo azul claro, 6 ou mais um fundo azul forte; por isso não tem legenda (ajustado na revisão: antes 1–2 consultas só tingiam o fundo e quase não apareciam no tema claro).
- A pesquisa fica escondida no Ano (não há lista).
- Abrir um mês no Ano vai para o dia de hoje quando é o mês atual, senão para o dia 1.

## Revisão crítica (26/11/2026)

Feita em `worktree-wf_ca153789-8ce-13`, a partir do trabalho do autor (`c801d78`, fast-forward). Cada frase do pedido do grupo, os itens A que valem para a tela e E1 a E5 foram conferidos contra o código e as capturas.

**Conferido e mantido:** cabeçalho (pesquisa em linha que filtra Dia, Mês e a grade da Semana; alternador Dia | Semana | Mês | Ano lembrado; **+ Agendar nova consulta** à direita), navegação de período sem card, Dia em ordem de horário, Semana com faixa de horas e rolagem e simultâneas lado a lado, Mês com fundo azul claro e círculo azul bebê no dia escolhido, hoje com contorno e fora do mês esmaecido, Ano 4×3 lido em pedidos de até 6 meses com cache, lista sem título de data com `StatusTag`, balão por situação com lote em uma confirmação, lixeira branca em fundo vermelho, ficha em seções, pop-up de agendar com o erro dentro, sem botão Atualizar, sem cor fixa, aviso de UBS, estados vazio, "nada encontrado" e carregando. Nenhuma checagem foi apagada; as afrouxadas só perderam o que deixou de existir (o ícone de confirmação na lista, cujo contraste era conferido, e as capturas da lista antiga).

**Achados e correções:**

1. **Ano: dia com 1 ou 2 consultas quase invisível no tema claro** (o fundo `ToneBlueSurfaceBrush` quase some sobre o fundo da tela). Agora um ponto azul sob o número marca o dia com consulta; com 3 a 5 entra também o fundo azul claro com o número em azul; com 6 ou mais, o fundo azul forte. O número subiu um pouco para o ponto não encostar, e os meses ficaram mais justos para o ano continuar cabendo sem rolagem em 1280×740.
2. **Mês: com a janela mínima (980×640) a lista do dia ficava com pouco mais de uma linha**; a célula do dia passou de 50 para 46 px (o círculo e as bolinhas continuam inteiros).
3. **Código sem uso:** `SelectAgendaDayCommand` e `SelectAgendaWeekCommand` (dos antigos botões Dia/Semana) ficaram sem uso na tela; saíram do `WorkspaceViewModel.cs`, e as três checagens que os usavam passaram a escolher a visão por `AgendaViewMode`.
4. **Mini-calendários do Ano ficavam na memória** depois de sair do Ano e eram remarcados (504 dias) a cada leitura; agora saem ao trocar de visão, como o comentário já prometia.
5. **Texto sem UBS desatualizado:** "Escolha uma UBS no Painel da UBS…" mandava a pessoa a um lugar que não tem mais a escolha (A4 levou-a para o pop-up "Trocar perfil" e para o botão Escolher UBS do aviso). Na Agenda virou "Escolha a UBS em que você vai trabalhar para ver a agenda." (e "… antes de agendar." no erro). As outras telas têm o mesmo texto (P-102).
6. **`Loaded` repetido** assinaria a rolagem da Semana duas vezes; agora assina uma vez só.
7. **Checagens novas:** a seleção de três consultas e o balão sobrevivem à releitura do ciclo de 20 s que recria a lista inteira; captura da ficha no tema escuro.
8. **Seta › andava debaixo do mouse no Dia:** o rótulo ficava entre ‹ e › e muda de largura de um dia para outro ("Sábado, 28 de novembro" é mais curto que "Sexta-feira, 27 de novembro"), então clicar › seguidas vezes errava o botão. A navegação passou a Hoje, ‹ e › juntos e o rótulo depois (como no calendário do celular e do Google); checagem nova no Dia confere que os três botões ficam no mesmo lugar em sete dias seguidos e que Hoje volta ao dia de hoje.

## Mudanças em arquivos compartilhados

- `Nora.Desktop/Models/DesktopPreferences.cs`: propriedade nova `AgendaPeriod` (padrão "Mês"; arquivos antigos continuam válidos).
- `Nora.Desktop/ViewModels/ShellViewModel.AgendaPeriod.cs` (arquivo novo, parcial do `ShellViewModel`): lê e grava a visão lembrada; nenhum membro existente foi alterado.
- `Nora.Desktop/Models/ApiModels.cs`: removido `AppointmentInfo.AgendaTimeDisplay`, que só a Agenda antiga usava e ficou sem uso.
- `Nora.Desktop/ViewModels/WorkspaceViewModel.UnitData.cs`: a leitura da agenda passa a devolver o pacote do cache (`AgendaRead`) em vez do mês avulso; o resto do arquivo não mudou.
- Checagens de outros assuntos adaptadas ao controle novo (nunca apagadas): `ListingChecks.cs`, `ReviewFixesChecks.cs`, `SchedulingChecks.cs`, `AgendaQueueChecks.cs`.
- Revisão: `Nora.Desktop/ViewModels/WorkspaceViewModel.cs` perdeu `SelectAgendaDayCommand` e `SelectAgendaWeekCommand` (só a Agenda antiga os usava; duas linhas na criação e duas declarações); `tests/Nora.Desktop.Checks/UnitFlowChecks.cs` e `ReviewFixesChecks.cs` trocaram uma linha cada (`vm.AgendaViewMode = "Dia"` no lugar do comando).

## Pendências (P-100 a P-104 reservadas)

- **P-100** · A API não informa o consultório da consulta nem um histórico de mudanças de situação, então a ficha não mostra esses dois campos (o pedido os cita "se a API trouxer"). Quando a API trouxer, acrescentar em `BuildAppointmentDetails` (`WorkspaceViewModel.Listings.cs`).
- **P-101** (teste sugerido ao usuário) · Olhar a Semana com três ou mais consultas no mesmo horário na janela mínima (980 px): as faixas ficam estreitas e o bloco mostra só o início do nome; conferir se isso basta ou se prefere blocos sobrepostos em cascata.
- **P-102** · Outras telas do OBS ainda dizem "Escolha uma UBS no Painel da UBS…" (estados e erros em `WorkspaceViewModel.Catalog.cs`, `Family.cs`, `Lightning.cs`, `UnitMetrics.cs`, `UnitProfile.cs`, `UnitScheduling.cs`, `Devices.cs` e os textos "Entre no Painel da UBS…" de `WorkspaceViewModel.cs`), mas a escolha da UBS saiu do Painel (A4: pop-up "Trocar perfil" e botão Escolher UBS do aviso). Na Agenda o texto foi corrigido; nas outras, alinhar na consolidação (são de outros grupos).

## Estado

- 26/11/2026: a Agenda foi redesenhada por inteiro (cabeçalho, as quatro visões, agendamento em pop-up, balão, ficha em seções, legenda). O estado das etapas anteriores deste arquivo (só leitura) foi substituído por este.
- Validação real: `dotnet build NORA.slnx --configuration Release` com 0 erros; suíte completa de checagens com **252 PASS e 1 FAIL**, o conhecido "NORA OBS: faixa de retorno aparece em todas as abas … home" (P-066, só falha na suíte completa). As checagens com "Agenda" no nome passam 24 de 24 (15 delas "Agenda OBS", 8 novas em `AgendaViewsChecks.cs`). Numa rodada anterior, a checagem "NORA Municipal: login, dados do transporte fixture…" falhou uma vez com a máquina carregada e passou sozinha e na rodada seguinte (instável por tempo, não relacionada à Agenda).
- Capturas (claro e escuro, com dados, vazio, balão, lote, pop-up de agendar nas duas formas, ficha, janela mínima de 980 px) em `.build/redesenho/agenda/obs-agenda-*.png`, todas abertas e conferidas: legenda numa linha, nada cortado, lixeira branca em fundo vermelho, sem título repetido nem card.
- Verificação final: pedido conferido item a item contra a lista acima; varredura de handlers duplicados, assinaturas removidas na descarga da tela, hex fixo, restos do Atualizar e código sem uso sem achados.
- Revisão (26/11/2026, depois dos 8 achados acima): build Release com 0 erros; suíte completa com **252 PASS e 1 FAIL** (o mesmo P-066 da linha de base; 253 checagens = 245 da fundação + 8 novas da Agenda, nenhuma apagada); filtro "Agenda" com 24 de 24. Capturas refeitas em `.build/redesenho/agenda/` e abertas (Dia, Semana, Mês e Ano nos dois temas, janela mínima, vazio, carregando, balão, lote, pop-up de agendar pelo catálogo e em texto livre, ficha clara e escura). Uma volta inteira de conferência sem achado novo depois da correção 8.
