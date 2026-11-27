# Redesenho do OBS e do Painel Municipal, dados fictícios e capturas — 2026-11-26

## Skills e comandos invocados

- `/aura-retomar-execucao:aura` (carrega `protocolo-tarefa-complexa`, `diagnosing-bugs`, `modo-loop-verificacao` e chama a `graphify`).

## Onde o trabalho acontece

Worktree `.claude/worktrees/desktop-redesign-ui`, branch `claude/desktop-redesign-ui`, criada de `origin/main` em `8386b04` (25/11/2026, com os ajustes da Luana). O worktree anterior da sessão (`hospital-simulation-setup-34fbf4`) ficou na branch `codex/cadastro-medicos-http` com quatro arquivos alterados e não commitados por outro chat; não foi tocado.

## O que foi pedido, item por item

Vale para o **OBS e para o Painel Municipal** em tudo o que os dois têm em comum; o que não existir no Painel é ignorado. O software WPF do usuário é a referência para qualquer decisão; dúvida que sobrar vai ao usuário.

### A. Geral (os dois desktops)

- **A1** Título da tela só na barra de cima, como está hoje. Em subtela: `Listagem • Pacientes` (subtela, ponto preto, tela principal). Nenhum outro título repetido dentro da tela (sai o banner "Painel operacional / Sua unidade em um só lugar" e semelhantes).
- **A2** Login salvo no app e nos dois desktops: sem marcar nada vale 1 dia; marcando manter por 30 dias vale 30 dias. A forma fica a critério da IA.
- **A3** Barra de título (a de minimizar, maximizar e fechar): sai o texto "NORA OBS" do canto superior esquerdo. Entram ali os botões abrir/fechar a barra de navegação, voltar, avançar e pesquisar, como no app do Claude (imagem 1). A marca continua só na barra de navegação.
- **A4** O bloco "UBS da operação / Escolher ou trocar UBS" sai de todas as telas. No menu do perfil (canto inferior esquerdo) entra **Trocar perfil**, acima de Configurações: abre um pop-up com os perfis que a pessoa pode acessar; ela escolhe um e clica em **Trocar**; o X no canto superior direito desiste.
- **A5** Listagens abertas: sem card nem divisão atrás; os itens ficam soltos na tela.
- **A6** Sai o chip do canto superior direito ("Jacyra Vieira Baracho", "Gestão conectada") de todas as telas. No lugar entram os filtros quando a tela precisar; senão, nada.
- **A7** Tela que só tem o filtro de pesquisa: em vez do botão "Filtros", o campo de pesquisa direto, com a lupa à direita.
- **A8** Os botões de ação só aparecem com um item selecionado, numa faixa na parte de baixo da tela. Em todas as listagens, idêntico ao software WPF.
- **A9** Duplo clique no item abre um pop-up de detalhes organizado em linhas horizontais (sem cards), como no software WPF.
- **A10** Listagem na tela inteira, com itens menores e só as informações principais.
- **A11** Nenhuma tela tem botão "Atualizar": o programa atualiza sozinho.
- **A12** Cadastros: campos lado a lado em grade (cerca de 300 px por campo; 2 colunas em 1366×768, 3 em telas maiores, 1 em telas pequenas; as colunas esticam por igual; o campo sozinho na última linha fica do mesmo tamanho dos outros, no meio); botão de salvar à direita. Vale para todos os cadastros. O cadastro fica livre na tela, sem card.
- **A13** Botões "novo/nova…" em cima à direita (à direita dos filtros; sem filtros, o mais à direita possível).
- **A14** Hover de todos os botões em azul bebê claro (hoje branco/cinza), nos dois softwares.
- **A15** Remover ou desativar é sempre um botão de lixeira branca em fundo vermelho, como no software WPF.
- **A16** A seta de voltar ao lado do título da tela deixa de existir (vai para a barra de título).
- **A17** O botão "Configurações" do rodapé da navegação some (fica só no menu do perfil).
- **A18** Menu do perfil com ícones: Configurações (engrenagem), Sair da conta (porta), Dados do perfil (pessoa).
- **A19** Cards com cor (a maioria).
- **A20** Preferência geral: sem cards; um pop-up já é o card, então nada de card dentro de pop-up.

### B. Painel da UBS (OBS)

- **B1** Sem "UBS da operação" e sem o banner; o topo passa a ser o "Resumo do dia".
- **B2** Em cima à direita: pesquisa e período (combo com dia, semana, mês, ano, 5 anos e máximo).
- **B3** À direita da pesquisa, um botão que alterna cards e gráficos (ícone de card ↔ ícone de gráfico).
- **B4** Gráficos mais bonitos e vívidos; "cancelado" em amarelo. No modo gráficos não há "ver dados em tabela". No modo cards há cards e tabelas.
- **B5** "Atenção da equipe" só no modo cards; o que não cabe em gráfico não aparece no modo gráficos.
- **B6** "Fluxo da unidade" sai da parte de baixo e vai para o resumo do dia.
- **B7** Cada card que comporta tem gráfico de linha e de pizza. Todo gráfico tem o título e, à direita dele, "Linha | Pizza" para alternar; idem nos pop-ups abertos pelo card.
- **B8** No pop-up do gráfico fica só o título: sai "Valor atual: 32" (imagem 2).

### C. Pacientes (OBS)

- **C1** Listagem: sai o botão "Cadastrar paciente" e o "Escolher UBS"; lista na tela inteira.
- **C2** Item: nome; embaixo `CPF | e-mail | telefone`, só os informados; sem ícone de pessoa.
- **C3** Cadastro: sai o título "Pacientes" e o trocar UBS; "Cadastrar referência na UBS" na grade de A12; botão **Cadastrar paciente** embaixo à direita; o endereço continua como seção opcional.

### D. Família (OBS)

- **D1** No menu, abaixo de Família, o item novo **Vínculos aprovados**, com a listagem deles.
- **D2** Família passa a ter uma listagem só (os pedidos).
- **D3** O motivo da recusa só aparece no pop-up aberto por "Recusar pedido": campo "Motivo da recusa (opcional, até 280 caracteres)", botões Enviar e Fechar; depois, a mensagem do que aconteceu (aprovou; recusou; recusou e o motivo foi enviado).

### E. Agenda (OBS)

- **E1** Filtros: busca na listagem e período dia/semana/mês/ano, como o calendário do celular. Dia: o que há no dia. Semana: tabela com os dias em cima e as consultas por hora. Mês: como hoje. Ano: os 12 meses em 4×3.
- **E2** "Agendar nova consulta" mantém o texto e fica em cima à direita.
- **E3** Dia selecionado: fundo azul bem claro e um círculo azul bebê no número; hover em azul bebê (hoje o dia fica todo azul-escuro e o número some, imagem 3).
- **E4** Lista do dia: sem o título do dia; item em duas linhas (nome; "08:20 às 08:40") e, à direita, a situação como etiqueta colorida.
- **E5** Duplo clique: pop-up de detalhes mais estilizado, em linhas horizontais.

### F. Vaga Relâmpago (Em andamento e Encerradas)

- **F1** Sem o título e sem os totais (abertas, em oferta, aceitas, recusadas, vencidas).
- **F2** Filtros em cima à direita.
- **F3** Listagem no padrão novo.

### G. Indicadores, Alertas e Comunicados

- **G1** Indicadores: mesma alternância cards/gráficos do Painel; cards bem resumidos.
- **G2** Alertas e Comunicados: só a listagem, sem textos.

### H. Informações da UBS (OBS)

- **H1** Atendimento: sem "Fechado"; caixa de seleção à esquerda de cada dia (marcada mostra os campos de abre e fecha); dois campos cerca de 1,2× mais baixos com "às" entre eles; sem o segundo intervalo; "Serviços oferecidos" continua; sem "Especialidades com horário na grade"; sem card e sem o título "Atendimento ao público"; o aviso "Ainda não informado" vai para o topo como item destacado em amarelo.
- **H2** Profissionais: listagem na tela inteira; **+ Novo profissional** em cima à direita abre pop-up com o cadastro; sem o aviso "da lista da rede"; explicar ao usuário o que é o conselho profissional.
- **H3** Consultórios: igual a Profissionais.
- **H4** Grade semanal: **Novo horário** em cima à direita abre pop-up (sem card dentro), com "Salvar horário na grade" à direita; botões dos dias da semana (segunda selecionada primeiro) e, abaixo, a listagem do dia.
- **H5** Bloqueios: sem o título de baixo; listagem padrão; remover pela faixa de ações (lixeira); **Novo bloqueio** em cima à direita abre pop-up com **Cadastrar bloqueio** embaixo à direita.
- **H6** A tela Computadores é excluída.

### I. Painel Municipal

- **I1** Aplicar tudo o que for comum; dizer ao usuário o que faltou ele analisar no Painel.

### J. Dados, capturas e slides

- **J1** Script de inserts que mantém os dados atuais e acrescenta dados fictícios bonitos para as capturas.
- **J2** Capturas de cada tela do OBS, do Painel e do app, com foco nas mais importantes; entregar uma pasta normal (não zip) e um arquivo Excel com as mesmas imagens.
- **J3** Indicar as 10 melhores imagens, que parte de cada uma mostrar, 4 slides com 3 imagens cada e as 20 melhores.

## Objetivo

OBS e Painel Municipal com o visual e o comportamento do software WPF de referência (barra de título com navegação, listagens abertas com faixa de ações e ficha de detalhe, cadastros em grade, sem títulos repetidos), login persistente nos três clientes, dados fictícios para capturas e o material de imagens para os slides.

## Restrições

- Fazer com calma e revisar cada parte contra o pedido; perguntar em caso de dúvida.
- Analisar o software WPF de referência antes de decidir ou de perguntar.
- Manter os dados que já existem (o script só acrescenta).
- O que não existir no Painel Municipal não é inventado lá.

## Decisões do usuário (26/11/2026)

- **Dados fictícios:** entram no **Neon de produção**. Como a IA não digita senha em serviço de produção, o script é escrito para qualquer banco NORA: a IA roda num banco local para tirar as capturas e o usuário roda no Neon.
- **Prazo:** sem pressa; seguir a ordem do pedido, tela por tela, validando cada uma.
- **Luana:** não está mexendo nos desktops; a branch segue a partir da `main` de 25/11.
- **"Sugerir UBS pela localização":** vai para o pop-up "Trocar perfil", como botão à esquerda do botão **Trocar**.

## O que a análise mostrou (e muda o tamanho do trabalho)

- A barra de título é a nativa do Windows (`ShellWindow.xaml:1`, sem `WindowChrome`). Para pôr botões nela é preciso uma barra própria, e isso toca `FullScreenService`, `ApplyWindowCorners` e `tests/desktop-startup.ps1` (que lê o título da janela).
- Não existe "avançar" na navegação (`ShellViewModel.cs:145-197` só tem a pilha de voltar). Ctrl+B não recolhe a barra lateral de verdade.
- Quase toda a interface dos dois programas está em `Nora.Desktop`; o Painel Municipal é montado em C# (`MunicipalView.xaml.cs`).
- Não há componente de listagem com seleção, faixa de ações nem painel de formulário em grade; o estilo `FloatingActions` existe sem uso. Não há estilo de botão de perigo.
- **API:** o período dos indicadores é limitado a 366 dias (`MetricsService.cs:25`), então "5 anos" e "máximo" exigem mudar a API. A sessão do paciente dura 8 h e o app guarda o token só na memória (`PatientController.kt:81`), então o login salvo no app exige mudar a API e o app. Nos desktops, sem marcar a caixa a sessão não é gravada em disco (`DesktopAccess.cs:86`).
- As checagens de `tests/Nora.Desktop.Checks` (185 casos) dependem de nomes e textos dos controles: mudam junto com as telas.

## Checklist

- [x] Análise do NORA (shell, telas, listas, filtros, gráficos, sessão) e do software WPF de referência
- [x] Dúvidas levadas ao usuário e respondidas
- [x] A (geral) · [x] B (Painel da UBS) · [x] C (Pacientes) · [x] D (Família) · [x] E (Agenda) · [x] F (Vaga Relâmpago) · [x] G (Indicadores, Alertas, Comunicados) · [x] H (Informações da UBS) · [x] I (Painel Municipal) — revisados por grupo; a verificação final com dez revisores foi cortada a pedido do usuário (prazo de 2 h)
- [x] A2 login salvo no app Android (falta rodar os testes instrumentados no emulador, P-078, e conferir contra a API publicada, P-079)
- [x] J1 script de dados (validado em PostgreSQL local; falta o usuário rodar no Neon, P-081) · [x] J2 capturas, pasta e Excel · [x] J3 indicação das imagens
- [ ] Validação: build da solução, checagens de desktop e de API, suíte do app, abertura real dos dois programas
- [ ] Loop de verificação até uma volta sem achados
- [ ] Documentação (`obs-painel-operacional.md`, `secretaria.md`, `design.md`, `app-paciente.md`) e `PENDENCIAS.md`

## Estado

- 26/11/2026: skills carregadas, branch e worktree criados, pedido registrado.
- 26/11/2026: análise concluída. Três levantamentos: o código do NORA ([anexo](anexos/2026-11-26-mapa-desktops-nora.md)), o sistema de estoque e o AEV ([anexo](anexos/2026-11-26-referencia-aev.md)). O padrão que o usuário descreve vem do **AEV**, não do estoque; do estoque fica só a ficha em linhas horizontais. Grafo do código montado pelo graphify (8.337 nós; `graph.html` em `Mateus-Projects/Claude-HTML/Projeto-NORA/graphify/`).
- 26/11/2026: [especificação](2026-11-26-redesenho-especificacao.md) escrita (fundação F1–F9, telas, dados e ordem de execução).
- **Linha de base antes de qualquer mudança:** `dotnet build NORA.slnx --configuration Release -m:1` com 0 erros; `Nora.Desktop.Checks` com **189 de 191** (falhavam "faixa de retorno … home" e "cada aba operacional renderiza somente o conteúdo selecionado … agenda").
- 26/11/2026: `Nora.Desktop/Views/FormGrid.cs` criado (painel dos cadastros). O método `ColumnsFor` foi deixado para o usuário escrever; devolve 1 coluna enquanto isso.
- 26/11/2026: **fundação em execução** por um fluxo de seis agentes em série nesta pasta (barra de título e navegação; cabeçalho; estilos e listagem; pop-ups, ficha e gráficos; perfil e sessão; revisão). Cada etapa faz commit na branch. Para saber até onde chegou: `git log --oneline` a partir de `ecc4ba1`.
- **Falta depois da fundação:** telas do OBS e do Painel, login salvo no app e período longo na API (F8 e F9), script de dados, integração, revisão item a item, capturas, Excel e documentação.
- 26/11/2026: **F1 e F2 feitas** (barra de título própria com alternar menu, voltar, avançar e pesquisar; avançar e Alt + →; barra lateral que recolhe de verdade; cabeçalho sem a seta de voltar). Build com 0 erros; checagens do desktop 201 PASS e 1 FAIL conhecido (P-066); a segunda falha da linha de base (aba Agenda) era expectativa velha do texto "Agenda" e foi adaptada. Falta, dessa parte, só o teste manual na tela real (P-067 em `PENDENCIAS.md`).
- 26/11/2026: **F3 feita** (cabeçalho da tela; itens A1, A6, A7 e A13 na parte compartilhada). `PageLabel` virou o caminho `Subtela • Área` nas subtelas e só o título nas demais, nos dois perfis; saiu o chip da UBS / "Gestão conectada" (o nome da UBS segue no cartão do perfil). Novo painel `HeaderRow` (título à esquerda; à direita filtros em linha, botão Filtros e ações; a pesquisa encolhe de 260 para 140 px antes de o título cortar, e botões nunca somem). `PageFilterGroup` ganhou `Inline`; grupo só de pesquisa é sempre o campo `SearchField` (lupa desenhada à direita, texto-guia "Pesquisar"); o botão Filtros só aparece com grupo que não é em linha. Novo decorator `PageActions` (mesmo mecanismo). As telas não foram redesenhadas. Checagens novas em `HeaderChecks.cs` (6: títulos nos dois perfis, sem chip, Agenda/catálogo só com pesquisa e Pacientes/UBS com Filtros, ordem e janela estreita, ações somem e voltam com a tela, XAML, fora da janela principal); a checagem do catálogo foi adaptada ao campo em linha. Falta, dessa parte, só o teste manual na tela real (P-068 em `PENDENCIAS.md`).
- 26/11/2026: **F5 e a parte de listagem de F6 feitas** (estilos e componentes de lista; itens A5, A8, A14, A15, A19 e A20 na parte compartilhada). Hover azul bebê (`ButtonHoverBrush` e `HoverBrush`) em todos os estilos de botão, com o primário mantendo o degradê da marca; chaves novas de tema (listagem, balão, botões de ação, 7 tons, situações da consulta); `OpenList`/`OpenListItem` (e a variante baixa), `ItemTitleText`/`ItemMetaText`, `StatusTag` (+ enum `Tone`, que `MetricCard` ganhou), `SelectionBalloon`, `ListActionButton`/`ListActionEditButton`/`ListActionDangerButton` (+ `ListAction.Icon` e 12 ícones), `ListingBehavior`, `PageLayout.Fill`, `NewItemButton` e `NoticeItem`; `OutcomePie` passa a usar as chaves de situação; `FloatingActions` saiu. Nenhuma tela foi redesenhada. Checagens novas em `ListingComponentsChecks.cs` (16). Falta, dessa parte, só o teste manual na tela real (P-069 em `PENDENCIAS.md`). Resumo dos nomes e do uso em `docs/design.md`.
- 26/11/2026: **F6 (restante) feita** (itens A9, A20, B3, B4, B7 e B8 na parte compartilhada). `DialogService.ShowForm` + `FormDialog` (pop-up de cadastro da marca: título, X, corpo com rolagem, primário à direita e secundário à esquerda; falso mantém aberto com o erro, verdadeiro fecha; Esc e X fecham sem salvar; Enter avança e, no último campo, salva); `ItemDetailsWindow` em linhas horizontais, sem cards, com `Actions` no rodapé e altura do conteúdo; `SegmentedToggle`; `ChartPanel` (título e Linha | Pizza à direita; o alternador some quando só há um tipo de dado) com `LineChartView` (linha grossa, área em degradê, pontos, grade discreta, dica) e rosca nova em `OutcomePie` (fio entre as fatias, total no meio, dica de valor e percentual, legenda com valor e percentual); `ChartData` (agrupamento do eixo por dia até 92 dias, por semana até 400 e por mês acima, pronto para a etapa do período longo); `IndicatorChartWindow` só com o título, o alternador e o X (sem "Valor atual", `IndicatorChart` sem o valor); `ViewModeToggle` com a escolha lembrada por tela em `DesktopPreferences.ViewModes`. O expansor de tabela saiu do `EvolutionChartView` e virou o `ChartDataTable`, que as três telas que mostram o gráfico trazem até serem redesenhadas. Pizza com cores por chave de tema (canceladas amarelo) e `ChartBrush1..6` mais vivas. Build com 0 erros; checagens do desktop **235 PASS e 1 FAIL** (o P-066, da linha de base); 12 checagens novas em `ChartAndDialogChecks.cs` (10 casos, dois rodam nos dois perfis) e 2 adaptadas (as de Indicadores do OBS e da Secretaria, que contavam barras e procuravam o expansor dentro do gráfico). Falta, dessa parte, só o teste manual na tela real (P-070 em `PENDENCIAS.md`).
- 26/11/2026: **F4 e a parte de desktop de F8 feitas** (itens A2 nos desktops, A4, A17 e A18). Saiu o botão Configurações do rodapé; o menu do perfil tem Trocar perfil (só no OBS), Configurações, Dados do perfil e Sair da conta com ícones desenhados. Novo `SwitchProfileDialog` (título, X, lista das UBS com nome e região, ativa marcada e selecionada, "Sugerir pela localização" à esquerda do primário "Trocar"; lê a lista ao abrir com indicador, mostra a falha dentro do pop-up e fecha se a sessão cair); "Trocar" usa `ConfirmObsUnitAsync` e fica desabilitado sem seleção ou com a UBS já ativa. `ObsUnitPickerView` e os blocos "UBS da operação" / "Escolher ou trocar UBS" saíram; o aviso `ObsUnitNoticeView` (agora também no Painel da UBS e em Pacientes) tem o botão "Escolher UBS" que abre o pop-up; conta com várias UBS e nenhuma ativa abre o pop-up sozinha depois do login e de restaurar a sessão (`ProfileChoicePending`). Login salvo: a sessão é sempre gravada em `session.dat` com o vencimento da API (24 h; 30 dias com a caixa), com o texto de ajuda "Sem marcar, você continua conectado por 1 dia neste computador."; a API não mudou (já devolvia 24 h e 30 dias para desktop) e a exigência de computador ativado do OBS continua (reabrir confere o computador). Build com 0 erros; checagens do desktop **242 PASS e 1 FAIL** (o P-066, da linha de base). Checagens novas em `ProfileChecks.cs` (menu com quatro itens e ícones no OBS e três na Secretaria e sem o botão no rodapé; pop-up lista, troca, X, Esc e mesma UBS; falhas, sem UBS e sessão recusada com o pop-up aberto; Sugerir pela localização; abertura sozinha depois do login e da restauração; login salvo nos dois perfis; login salvo com computadores ligados) e adaptadas as de `UnitFlowChecks`, `ApiClientChecks` e `PatientInterfaceChecks`. Falta, dessa parte, só o teste manual na tela real (P-072) e a decisão do usuário sobre o menu com a barra lateral recolhida (P-071). Checklist: [x] menu e rodapé · [x] pop-up Trocar perfil · [x] remoção dos blocos e do seletor · [x] abertura sozinha · [x] login salvo · [x] checagens · [x] build e documentação.
- 26/11/2026: **revisão crítica da fundação (F1 a F6 e a parte de desktop de F8)** feita contra a especificação, frase por frase, e corrigida. Todos os nomes prometidos existem (barra de título, `ForwardCommand`/`CanGoForward`/`ForwardTip`, `PageActions`, `PageFilterGroup.Inline`, `SearchField`, `OpenList`/`OpenListItem`, `ItemTitleText`/`ItemMetaText`, `StatusTag`, `SelectionBalloon`, `ListAction*`, `ListingBehavior`, `PageLayout.Fill`, `NewItemButton`, `NoticeItem`, `DialogService.ShowForm`/`FormDialog`, `SegmentedToggle`, `ChartPanel`, `ViewModeToggle`, `SwitchProfileDialog`, `MetricCard.Tone`). Achados e correções: (1) `ViewModeToggle` sem `ScreenKey` lia e gravava o modo da rota aberta mesmo escondido (todas as telas carregam juntas ao abrir a janela), então o botão de uma tela assumia o modo de outra; agora a chave da rota só vale com o botão à vista, e ele relê o modo ao aparecer (checagem nova, que falha com o código antigo); (2) "Limpar filtros" tinha deixado de apagar a pesquisa da tela quando ela passou para o cabeçalho (ex.: Cadastros de referência do Painel); voltou a apagar, como antes (checagem nova); (3) `PageLayout.Fill` numa página com subtelas aberta sem subtela escolhida (atalho ou página restaurada, todas empilhadas) desligaria a rolagem e cortaria as outras; agora a página rola nesse caso (`ShellViewModel.ShowsAllSubPages`; checagem estendida); (4) hover dos blocos de aparência das Configurações passou ao azul bebê (A14); (5) `ListingBehavior` deixou de percorrer a lista inteira para cada item desfeito da seleção; (6) documentação corrigida (a pesquisa em linha não é limpa ao sair da tela; ligações `ElementName` entre a tela e o `PageActions` valem nos dois sentidos, com checagem nova). Decisão em aberto registrada: P-073 (Ctrl+K no botão de pesquisa). Build com 0 erros; checagens do desktop **244 PASS e 1 FAIL** (o P-066, da linha de base); com a galeria, **248 PASS e 1 FAIL** (o mesmo). Capturas em `.build/redesenho/fundacao/` conferidas nos dois temas (barra de título com 4 + 3 botões e sem o nome do programa, caminho "Subtela • Área", campo Pesquisar com a lupa à direita, menu do perfil com ícones, pop-up Trocar perfil, janela mínima e barra lateral recolhida).
- 26/11/2026: `FormGrid.ColumnsFor` escrito pela IA a pedido do usuário (`floor((largura + espaço) / (campo + espaço))`, de 1 a 3).
- 26/11/2026: **API, app e script de dados** feitos em pastas isoladas, cada um com revisão crítica, e **juntados nesta branch**: API (`worktree-wf_5640a03a-b07-5`: sessão do paciente de 24 h ou 30 dias com `keepSignedIn`, também no Google; indicadores em até 7.305 dias; suíte da API toda verde), app (`worktree-wf_5640a03a-b07-4`: caixa "Manter conectado por 30 dias", sessão cifrada com chave do Android Keystore, restauração ao abrir; 343 testes locais, 58 prévias; instrumentados só compilados) e script (`worktree-wf_5640a03a-b07-6`: `scripts/popular-dados-demonstracao.ps1`, só acrescenta, repetível, com `-Simular` e `-Remover`; 18.108 linhas em 18 s no teste com 3 UBS extras; suíte da API com 285 verificações e 0 falhas). Conflitos só em documentação; as pendências que colidiam foram renumeradas: as do app viraram **P-078 a P-080** e as do script **P-081 a P-085** (as da API ficaram P-075 a P-077).
- 26/11/2026: **telas em execução** em oito grupos paralelos, cada um em pasta isolada a partir desta branch, com revisor próprio (Painel da UBS e Indicadores; Pacientes, Família e Vínculos aprovados; Agenda; Vaga Relâmpago, Alertas e Comunicados; Informações da UBS; Painel Municipal em dois grupos; Configurações, ficha e acesso). Cada grupo escreve `docs/tarefas/2026-11-26-redesenho-<grupo>.md` e entrega numa branch `worktree-wf_ca153789-8ce-N`.
- **Falta depois das telas:** juntar as oito branches, compilar e rodar todas as checagens; revisão item a item contra o pedido; banco local com os dados fictícios; capturas (OBS, Painel e app), pasta e Excel; indicação das imagens; consolidar `PENDENCIAS.md` e a documentação por assunto; decisões do usuário P-071 (menu do perfil com a barra recolhida), P-073 (Ctrl+K), P-077 (capacidade em período longo) e P-085 (`-Remover` de UBS fictícia com histórico de acesso).
- 26/11/2026 (20h–21h10): o usuário pediu para finalizar em 2 horas, sem rodadas extras de checagem. As oito branches de telas foram juntadas; build com 0 erros e `Nora.Desktop.Checks` com **304 de 305** (só a P-066 de antes). A verificação final com dez revisores foi interrompida. Banco e API locais com os dados fictícios (porta 55490 e `http://127.0.0.1:5190`; `%LOCALAPPDATA%\NORA\capturas-demo\LEIA-ME.txt`), com **5 UBS fictícias** a pedido do usuário, mais 6 contas de equipe e 6 bloqueios. Nova ferramenta `scripts/capturas-reais.ps1` (modo `--capturas-reais` de `tests/Nora.Desktop.Checks`) percorre o menu dos dois programas e fotografa cada tela, o modo gráficos, o balão, a ficha e os pop-ups; a rota "Nova conta" é pulada porque abre um pop-up que bloqueia a navegação. **Entregue** em `Mateus-Projects/Claude-HTML/Projeto-NORA/capturas-2026-11/`: `desktop/` (98 imagens, 2400×1350), `app/` (16, emulador), `20-melhores/` e `NORA-capturas-2026-11.xlsx` (todas as capturas, 20 melhores, 10 principais com o recorte e 4 slides). O que é fictício e o que é real está em `docs/dados-demonstracao.md`.
- **Não verificado:** a revisão item a item final do código integrado (cortada pelo prazo); os testes manuais na tela real (P-067 a P-072); a concessão de acesso às UBS das contas fictícias no banco local não funcionou (elas aparecem como "Sem UBS"); `docs/PENDENCIAS.md` ainda não recebeu as pendências que os oito grupos deixaram nos seus arquivos `docs/tarefas/2026-11-26-redesenho-<grupo>.md` (faixas P-090 a P-129).

- 26/11/2026 (23h30): **4 vídeos** pedidos pelo usuário, gravados pelo novo modo `--videos-reais` de `tests/Nora.Desktop.Checks` (`RealVideos.cs`: 1920×1080, 30 quadros/s, cursor desenhado, MP4 pelo ffmpeg; variável `NORA_FFMPEG` opcional) contra a API local: 1) Painel Municipal, Visão geral em cards e gráficos; 2) cadastro e listagem de UBS; 3) OBS, cadastro e listagem de pacientes; 4) OBS, Painel da UBS em cards e gráficos. Entregues em `Mateus-Projects/Claude-HTML/Projeto-NORA/capturas-2026-11/videos/`; conferidos por quadros extraídos de cada vídeo.

- 26/11/2026 (23h50): a pedido do usuário, os 4 vídeos foram acelerados para 2x (ffmpeg `setpts=0.5*PTS`, 30 quadros/s, metade da duração); os originais ficaram em `videos/originais-1x/`. O `OBS-NORA.zip` do release v1.2-escola foi baixado pelo link `latest` e conferido: é o build novo (26/11 23h06, com `SwitchProfileDialog`, `FormGrid` e `ViewModeToggle`). O APK da v1.2 também mudou em relação à v1.1 (login salvo por 1 ou 30 dias).
