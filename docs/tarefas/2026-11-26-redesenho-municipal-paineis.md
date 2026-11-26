# Redesenho do Painel Municipal: painéis de números — 2026-11-26

Parte do redesenho descrito em [2026-11-26-redesenho-desktops-dados-capturas.md](2026-11-26-redesenho-desktops-dados-capturas.md) (itens A e I1) e na [especificação](2026-11-26-redesenho-especificacao.md), seção "Painel Municipal". Branch de trabalho: a deste worktree, a partir de `claude/desktop-redesign-ui` (fundação pronta, `04ec09d`). Pendências reservadas a este grupo: **P-115 a P-119**.

## Skills e comandos invocados

- Nenhuma skill invocada pelo usuário. Seguidas como referência: `list-generator`, `selecao-listagem` (listagem aberta e seleção) e as regras globais do ambiente (documento da tarefa antes de executar, pendências em `docs/PENDENCIAS.md`).

## O que foi pedido (telas deste grupo)

Grupo **municipal-paineis**: Visão geral, Indicadores (Desempenho, Vaga Relâmpago, Alertas, Relatórios e metas), Capacidade, Desempenho das unidades e Demanda. Itens **A (geral)** e **I1**.

- **A1** sem título repetido dentro da tela, sem banner, sem texto de apresentação.
- **A5/A10** sem card em volta de listagem; listagem solta, em duas linhas, na tela inteira.
- **A8/A9** ações só no balão com item selecionado; duplo clique abre a ficha em linhas.
- **A11/F7** nenhum botão "Atualizar" (nem "Ler o cadastro de novo"): a tela relê ao entrar, no ciclo de 20 s e depois de cada ação.
- **A12/A13** formulário em `FormGrid`, sem card, primário à direita; "+ Novo …" e alternadores no cabeçalho (`PageActions`).
- **A14/A15/A19/A20** hover azul bebê, lixeira branca em fundo vermelho para remover, cards com cor, sem card dentro de pop-up.
- **Visão geral**: sai o banner "Toda a rede em um só lugar" e o botão "Atualizar dados"; cabeçalho com pesquisa em linha, "Período" em linha (Dia, Semana, Mês, Ano, 5 anos, Máximo) e `ViewModeToggle`; modo cards = cards coloridos e tabelas sem card; modo gráficos = grade de `ChartPanel` (Linha | Pizza), sem tabelas.
- **Indicadores › Desempenho**: mesmo `ViewModeToggle`; "Período" em linha com as seis opções mais "Personalizado" (datas e demais filtros no botão Filtros); `MaxRangeDays` e `ValidateDateRange` aceitam até 7.305 dias (20 anos) com a mensagem atualizada, sem mudar a assinatura (o método é compartilhado com a UBS).
- **Vaga Relâmpago da rede, Alertas, Relatórios e metas, Capacidade, Desempenho das unidades e Demanda**: sem títulos repetidos, sem botões de atualizar, cards coloridos, filtros no cabeçalho, tabelas de números sem card; alertas da rede e metas (registros com ações) em `OpenList` + balão + ficha; cadastro de meta em pop-up; exportação CSV como botão em `PageActions`.
- **Pop-up do gráfico do card**: conferir que já não mostra "Valor atual" nas telas da Secretaria.

## Objetivo

As oito rotas do Painel Municipal de números (`home`, `units:performance`, `specialties:demand`, `capacity`, `indicators`, `slots`, `alerts`, `reports`) no visual novo da fundação, sem mudar regra de negócio, chamadas de API, confirmações nem mensagens de erro.

## Restrições

- Mexer só nos arquivos deste grupo (views, code-behind e as partes do ViewModel dessas telas, checagens delas). Não alterar `Themes/*`, `ShellWindow*`, componentes compartilhados da fundação, `Nora.Api`, `APPNORA`, `scripts/`, `docs/` (exceto este arquivo) nem `FormGrid.ColumnsFor`.
- Outro agente mexe nos blocos das outras rotas de `MunicipalView.xaml.cs`; os blocos deste grupo são `home`, `units:performance`, `capacity`, `specialties:demand`, `indicators`, `slots`, `alerts`, `reports`.
- Sem `git stash`, sem push, sem abrir os programas na tela do usuário.

## O que mudou em cada tela

Todas as telas deixam de ter título repetido, banner e botão de atualizar. Os filtros, o alternador e as ações vão para o cabeçalho (`PageFilterGroup`, `PageActions`). Nada de card em volta de tabela ou lista; cartões de número têm tom por sentido (concluídas verde, faltas rosa, cancelamentos amarelo, comparecimento azul, esperas violeta, ofertas turquesa).

- **Visão geral (`home`)**: "Rede agora" (os totais de agora, 4 cartões) e "Resumo do mês/da semana/…" (os seis cartões do período) mais o comparativo por região, tabela solta. Cabeçalho: pesquisa (filtra cartões, gráficos e linhas pelo título), Período em linha, Filtros (UBS, região, especialidade) e o alternador. Modo gráficos: grade de gráficos em 2 colunas (1 em janela estreita), sem tabelas: Consultas por situação (Linha | Pizza), Comparecimento, Consultas por região, Consultas por especialidade, Espera na fila (Linha | Pizza), Ofertas de Vaga Relâmpago, Ocupação da agenda e Cadastros de pacientes; o que não tem dado fica de fora (só o das consultas fica sempre e explica o motivo). A escolha cards/gráficos é lembrada por tela.
- **Indicadores › Desempenho (`indicators`)**: igual, com o Período de sete opções (Personalizado abre as datas e o "Aplicar período" no botão Filtros) e a tabela "Consultas por dia" (expansor, no modo cards) no lugar do antigo expansor do gráfico.
- **Vaga Relâmpago (`slots`)**: oito cartões coloridos (`NetworkLightningCards`), Período da vaga em linha e UBS no botão Filtros; a faixa "Atualizar vagas" e o texto de abertura saíram; o aviso de leitura (data de criação, oferta automática, aceite não confirma o atendimento) ficou numa linha curta abaixo dos cartões.
- **Alertas (`alerts`)**: `OpenList` na tela inteira (`PageLayout.Fill`), item em duas linhas ("Fila alta | Cardiologia"; "UBS | valor | limite | desde"), etiqueta da categoria (Fila, Espera, Faltas, Ocupação, Meta), balão com "Ver detalhes" e ficha no duplo clique/Enter. Pesquisa no cabeçalho; UBS e região no botão Filtros. "Nenhum alerta" é informação; falha ou UBS sem base é aviso amarelo.
- **Relatórios e metas (`reports`)**: metas em `OpenList` (uma linha por métrica, etiqueta Definida / Herdada / Sem meta), balão com "Editar meta" (pop-up de cadastro com `FormGrid`, "Salvar meta" à direita, fechar sem salvar não deixa rascunho) e "Remover" (lixeira branca em fundo vermelho; só quando todas as metas selecionadas têm meta gravada; uma única confirmação para várias); ficha no duplo clique. Escopo das metas em linha no cabeçalho; "Exportar CSV" em `PageActions`; "Situação por UBS" é tabela solta.
- **Capacidade (`capacity`)**: cartões coloridos, "Por UBS" e "Por região" soltos, cadastro de uma UBS em leitura (profissionais, consultórios e grade) sem o botão "Ler o cadastro de novo". Pesquisa filtra todas as tabelas.
- **Desempenho das unidades (`units:performance`) e Demanda (`specialties:demand`)**: só a tabela (sem card, sem título repetido), pesquisa e Período em linha.

## Decisões onde a especificação não dizia

- **Pesquisa em linha das telas de números** é uma só (`WorkspaceViewModel.NetworkSearch`, campo `NetworkSearchField`, ligação `NetworkSearchBinding`), em vez do `ListSearchFilter`, que só aceita uma lista por campo e a Visão geral tem cartões, gráficos e tabelas. "Limpar filtros" também a apaga. Cada tela diz os campos: título e valor nos cartões, título nos gráficos, nome/região nas tabelas.
- **Período**: Dia = 1, Semana = 7, Mês = 30 (o padrão de antes), Ano = 365, 5 anos = 1.826, Máximo = 7.305 dias. "Personalizado" só aparece onde há as datas (Indicadores) ou enquanto o período personalizado vale.
- **Limite de 20 anos**: `ValidateDateRange` (assinatura de quatro parâmetros) vale 7.305 dias; ganhou uma sobrecarga com o limite em dias e `MaxListRangeDays` (366) para a lista de consultas de uma UBS da Secretaria (`Units.cs`), que a API continua limitando a 366 dias. A janela enviada à API nunca passa de 7.305 dias em tempo absoluto (um período de 20 anos que atravessa um horário de verão antigo ganharia 1 h).
- **Alertas e metas sem rota própria de ficha**: o `BuildDetails` do shell só conhece a UBS; as duas telas abrem a ficha pelo `ListingBehavior.SetOpenHandler` com `ItemDetailsWindow` próprio, para não editar o `ShellWindow` nem o `WorkspaceViewModel.Listings`.
- **Altura da tela de alertas**: o contêiner da Secretaria (`MunicipalView`) empilha as telas sem limitar a altura; a tela de alertas mede a área de conteúdo e ajusta a própria altura (mais `PageLayout.Fill`). Se o outro agente trocar o contêiner por um painel que entregue a altura, o ajuste continua inofensivo.
- **Retentativa sem botão**: a falha da avaliação dos alertas libera a próxima leitura do ciclo de 20 s (antes esperava 5 minutos) e a mensagem diz "A tela tenta de novo sozinha."; o cadastro da Capacidade já era relido pelo ciclo.
- **Gráficos em cartão**: cada gráfico da grade fica num cartão (são painéis de dados, não listagem nem formulário).
- **Título das rotas `units` e `specialties`**: o `Heading("Unidades de saúde", …)` e o `Heading("Especialidades", …)` que abriam essas duas rotas (e apareciam em cima de Desempenho das unidades e de Demanda) saíram de `MunicipalView.Render`, porque o item A1 vale para as minhas telas. São linhas compartilhadas com o grupo das outras subtelas dessas rotas: se esse grupo também as editou, a integração resolve o conflito mantendo as chamadas sem `Heading`.
- **Capturas de pop-up**: a checagem espera a animação de entrada terminar e captura a raiz da janela (a moldura tem margem de sombra e cortava a borda direita e a de baixo).

## Validação

Executada neste worktree (Release), depois da última alteração de código.

- `dotnet build NORA.slnx --configuration Release --nologo -m:1 -v q`: **0 erros** (2 avisos que já existiam: `ApiEndpoints.Photo.cs` e `WorkspaceViewModel.UnitProfile.cs`).
- Checagens completas (`Nora.Desktop.Checks.dll`): **252 PASS e 1 FAIL**, o conhecido "NORA OBS: faixa de retorno aparece em todas as abas … home" (P-066, só falha na suíte completa). Linha de base da fundação: 244 PASS e 1 FAIL; as 8 checagens novas deste grupo (`MunicipalPanelsChecks.cs`) estão nos 252.
- Com a máquina dividida com sete outras suítes, uma rodada anterior mostrou também "Filtros: indicador acompanha abertura e fechamento nos dois perfis"; ela passa sozinha e passou na rodada seguinte da suíte completa (ver P-118).
- Capturas (RenderTargetBitmap, `--preview-dir .build/redesenho/municipal-paineis --check-filter "Painel Municipal (painéis)"`), em `.build/redesenho/municipal-paineis/`: Visão geral (cards, gráficos, busca vazia, vazio, claro e escuro), Indicadores (cards e gráficos, claro e escuro), Desempenho das unidades, Demanda, Capacidade, Vaga Relâmpago, Alertas (balão, ficha, vazio, falha, escuro), Relatórios e metas (balão, pop-up de meta com erro e em espera, lixeira, escuro) e a largura mínima de 980 de todas as rotas no tema escuro. Abertas e conferidas: sem título repetido, sem card atrás de lista, balão sem faixa vazia, cabeçalho à direita e leitura nos dois temas.
- Conferência item a item contra o pedido (A1, A5, A8–A15, A19, A20, F7, I1): feita; sem botão de atualizar em nenhuma das oito rotas (checado por automação em `MunicipalPanelsChecks`), `MaxRangeDays` em 7.305 dias com a mensagem nova, cartões coloridos por tom, cor só por `DynamicResource` (nenhum hex nas telas do grupo).
- Pop-up do gráfico do card: já não mostra "Valor atual" (foi tratado na fundação; as telas da Secretaria usam o mesmo `ChartCommands.Show`).

## Pendências deste grupo

Números reservados: P-115 a P-119. Nada foi gravado em `docs/PENDENCIAS.md` (a consolidação é feita depois); texto pronto para copiar:

- **P-115** · 2026-11-26 · `MunicipalView.Render`: as rotas `units` e `specialties` perderam o `Heading` na mesma linha em que o outro grupo mexe nas subtelas de cadastro; conferir na integração que nenhuma delas voltou a mostrar título repetido (Desempenho das unidades e Demanda não podem ter).
- **P-116** · 2026-11-26 · A tela de Alertas ajusta a própria altura à área de conteúdo (o contêiner da Secretaria empilha as telas sem limitar a altura). Se o contêiner passar a entregar a altura, o ajuste (`NetworkAlertsView.FillArea`) pode sair.
- **P-117** · 2026-11-26 · Teste sugerido ao usuário: abrir o Painel Municipal com os dados de demonstração e conferir a Visão geral em Cards e em Gráficos (Período Dia a Máximo), Alertas com balão, e "Editar meta"/"Remover" em Relatórios e metas, em monitor com escala 125% e 150%. Defeito: texto cortado, balão sobre a lista de metas fora do lugar ou qualquer botão de atualizar.
- **P-118** · 2026-11-26 · A checagem "Filtros: indicador acompanha abertura e fechamento nos dois perfis" falhou uma vez na suíte completa com várias suítes rodando juntas (a seta não acompanhou a abertura) e passou sozinha e na rodada seguinte; observar se volta a falhar e, se sim, esperar a seta com `PumpUntil`.
- **P-119** · 2026-11-26 · Em janelas de 980 px a tabela "Situação por UBS" (Relatórios e metas) é mais larga que a área e rola na horizontal; avaliar se vale compactar as colunas.
