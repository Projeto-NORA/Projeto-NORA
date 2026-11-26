# Redesenho do Painel da UBS e dos Indicadores (OBS) — 2026-11-26

Parte do redesenho dos desktops ([pedido](2026-11-26-redesenho-desktops-dados-capturas.md), [especificação](2026-11-26-redesenho-especificacao.md)). Este arquivo registra só o grupo **painel-indicadores**: Painel da UBS (rota `home`) e Indicadores > Desempenho (rota `indicators`) do OBS. Outros agentes fazem os outros grupos ao mesmo tempo, cada um nas suas telas.

## Skills e comandos invocados

- Fluxo de trabalho do orquestrador (agente do grupo `painel-indicadores`); nenhuma skill de usuário chamada diretamente. Lidas como referência de padrão as skills `list-generator` e `selecao-listagem` (as duas telas não têm listagem de registros, então só o espírito vale: sem card em volta, sem título repetido).

## O que foi pedido, item por item

Itens do pedido: **B1 a B8**, **G1** e a parte do cliente de **F9**, mais os itens A (geral) aplicados às duas telas.

- **B1** Sem "UBS da operação" e sem o banner "Painel operacional / Sua unidade em um só lugar"; a tela começa em "Resumo do dia" (o texto acompanha o período: semana, mês, ano, últimos 5 anos, todo o período).
- **B2** Cabeçalho: pesquisa em linha (filtra cards, painéis de gráfico e linhas de tabela, sem acento e sem caixa) e combo **Período** em linha: Dia, Semana, Mês, Ano, 5 anos e Máximo (1, 7, 30, 365, 1.826 e 7.305 dias).
- **B3** `ViewModeToggle` dentro de `PageActions`, à direita.
- **B4** Gráficos mais vivos, cancelado em amarelo; no modo gráficos não há "ver dados em tabela"; no modo cards há cards e tabela.
- **B5** "Atenção da equipe" só no modo cards.
- **B6** "Fluxo da unidade" (pacientes cadastrados, na fila, vagas ativas) entra no mesmo grupo do resumo.
- **B7** Todo gráfico com título e Linha | Pizza à direita (grade de `ChartPanel`); pop-up do card igual.
- **B8** Pop-up do gráfico só com o título (sem "Valor atual").
- **G1** Indicadores: mesmo `ViewModeToggle`; "Período" em linha com as seis opções mais "Personalizado" (datas e demais filtros no botão Filtros; personalizado até 20 anos, com limite próprio da UBS); cards resumidos e coloridos (comparecimento, concluídas, faltas, canceladas, espera média, ocupação da agenda); tabela por especialidade sem card; metas e score num bloco curto; modo gráficos em grade de `ChartPanel`; sem o título "Indicadores da UBS" dentro da tela.
- **F9 (cliente)** O texto do erro 400 de período do `NoraApiClient` fala do limite de 20 anos; o tooltip "até 366 dias" some.
- **Itens A** que valem aqui: nenhum título repetido, sem card em volta de listagem, F7 (sem botão Atualizar), cards coloridos, hover azul, cor só por `DynamicResource`, `ObsUnitNoticeView` mantido, estados vazio/carregando/erro, tema escuro.

## Objetivo

Painel da UBS e Indicadores no padrão novo do OBS, usando só os componentes da fundação, com regra de negócio e chamadas de API preservadas.

## Restrições

- Só os arquivos das duas telas e as checagens delas; sem mexer em `Themes/*`, `ShellWindow*`, componentes compartilhados, API, APPNORA, scripts ou outros docs.
- Pendências novas com os números P-090 a P-094.

## O que mudou em cada tela

### Painel da UBS (`OperationalOverviewView`)

- **Cabeçalho:** `HeaderSearchBox` (campo Pesquisar com lupa, 260 px que encolhe até 140) e combo **Período** em dois `PageFilterGroup Inline`, e o `ViewModeToggle` (`ScreenKey="home"`) em `PageActions`. Não há botão Filtros nem Atualizar. O nome da tela fica só no cabeçalho da janela.
- **Modo cards:** "Resumo do dia" (o título acompanha o período) com **nove cards coloridos no mesmo grupo**: concluídas (verde), faltas (rosa), cancelamentos (amarelo), agendadas (azul), tempo médio de espera (violeta), ofertas em andamento (turquesa) e o fluxo da unidade: pacientes cadastrados (azul), na fila (violeta) e vagas ativas (turquesa). Depois, "Consultórios ativos" (bloco turquesa), a **tabela das consultas do período** (dia, semana ou mês por linha conforme o tamanho do período; sem card em volta) e "Atenção da equipe" com o botão para a central de alertas. O card continua abrindo o pop-up do gráfico (só título e Linha | Pizza).
- **Modo gráficos:** grade de `ChartPanel` (um por indicador que tem dado): Consultas por situação (linha com as três situações e pizza das encerradas), Vaga Relâmpago (pizza), Espera por especialidade, Ocupação da agenda (pizza), Consultas por especialidade e Pacientes cadastrados. Sem tabela, sem "Atenção da equipe", sem consultórios. O desenho escolhido (Linha ou Pizza) sobrevive ao ciclo de 20 s.
- **Pesquisa:** filtra cards (título, valor e dica), painéis de gráfico (título, linhas e fatias), linhas da tabela (período e números) e os blocos Consultórios ativos e Atenção da equipe (por título), sem acento e sem caixa. Sem resultado: "Nenhum item do painel corresponde à pesquisa." / "Nenhum gráfico corresponde à pesquisa.".
- **Período:** Dia usa a lista de hoje (atualizada a cada 20 s); Semana, Mês, Ano, 5 anos e Máximo usam os indicadores da janela escolhida (uma leitura por mudança, resposta atrasada descartada, Dia e Mês compartilham os mesmos 30 dias sem nova leitura). Períodos longos agrupam o eixo por semana (até 400 dias) ou mês (`ChartData`); em janelas acima de 400 dias o eixo começa no primeiro dia com consulta.

### Indicadores > Desempenho (`UnitIndicatorsView`, hospedada por `OperationalInsightsView`)

- **Cabeçalho:** combo **Período** em linha (Dia, Semana, Mês, Ano, 5 anos, Máximo, Personalizado), botão **Filtros** (datas do personalizado com Aplicar período, especialidade e situação da tabela) e `ViewModeToggle` (`ScreenKey="indicators"`). O personalizado aceita até 20 anos (7.305 dias) com limite próprio da UBS (`ValidateUnitRange`); a validação de 366 dias da Secretaria (`Metrics.cs`) não foi tocada.
- **Modo cards:** seis cards resumidos (comparecimento turquesa, concluídas verde, faltas rosa, cancelamentos amarelo, espera média violeta, ocupação da agenda azul), a nota "Como é calculado o comparecimento", a tabela por especialidade sem card e o bloco curto de metas da Secretaria.
- **Modo gráficos:** Consultas por situação, Comparecimento, Vaga Relâmpago, Espera por especialidade, Ocupação da agenda e Consultas por especialidade.
- **Saíram:** o título "Indicadores da UBS" (e a linha de subtítulo), "Visão geral", os cards de Vaga Relâmpago e de capacidade, "Maior espera atual", "Série no tempo", a tabela de dados do gráfico e o `ObsMetricCard` sem uso.

### Cliente da API

- `NoraApiClient.InvalidInputMessage`: o erro 400 de período dos indicadores (da rede e de cada UBS) fala do limite de 20 anos; o histórico de acessos continua em 366 dias.
- Janela de 20 anos: a meia-noite local de 20 anos atrás pode ter outro deslocamento (horário de verão antigo) e a janela passaria de 7.305 dias por uma hora, o que a API recusa; `ClampWindowStart` ajusta o início para valer exatamente o limite (Painel e Indicadores).

## Decisões onde a especificação não dizia

- **Dia no Painel:** os quatro números de consulta vêm da lista de hoje; a espera média, a linha dos gráficos (últimos 7 dias, para dar contexto) e as pizzas de Vaga Relâmpago e por especialidade olham os últimos 30 dias, como o Painel sempre fez. Cada card ou nota diz qual janela usa.
- **"Na fila":** o fluxo chamava `QueueWaitingCount`, que não existe (ligação quebrada, o número aparecia vazio) e o card "Ofertas em andamento" também. Agora "Ofertas em andamento" usa `OffersRunningCount` e "Na fila" usa as pessoas aguardando dos indicadores (`WaitingPatients`).
- **Colunas dos cards:** novo painel `TileGridPanel` escolhe as colunas pela largura (mínimo de 250 px) e usa uma a menos quando a última linha ficaria quase vazia (9 cards viram 3 x 3).
- **Espera por especialidade:** com uma especialidade só não há linha; fica só a pizza da fila.
- **Gráficos dentro de cards:** o `ChartPanel` não tem moldura, então cada painel da grade fica num `Card` (é gráfico, não listagem).
- **Capacidade dos Indicadores:** os três cards de capacidade viraram um (ocupação da agenda); `UnitCapacityCards` ficou sem uso (P-090).

## Checklist

- [x] Painel: cabeçalho (pesquisa, período, alternador)
- [x] Painel: modo cards (resumo + fluxo, consultórios, tabela, atenção)
- [x] Painel: modo gráficos (grade de `ChartPanel`)
- [x] Painel: pop-up do card sem "Valor atual" (conferido em captura)
- [x] Indicadores: período em linha, Filtros, alternador
- [x] Indicadores: cards, tabela, metas, gráficos
- [x] NoraApiClient: texto do erro de período
- [x] Checagens adaptadas e novas
- [x] Capturas nos dois temas (com dados, vazio, janela mínima, pop-up do gráfico, Filtros)
- [x] Loop de verificação (uma volta final sem achados novos)

## Validação (resultado real)

- `dotnet build NORA.slnx --configuration Release --nologo -m:1 -v q`: 0 erros (só o aviso CS8604 que já existia em `WorkspaceViewModel.UnitProfile.cs`).
- Checagens do desktop (`Nora.Desktop.Checks.dll`, suíte completa): **250 PASS e 1 FAIL**, o conhecido P-066 ("NORA OBS: faixa de retorno aparece em todas as abas … home"), que já falhava na linha de base da fundação (244 PASS e 1 FAIL). Checagens novas: cabeçalho e cards do Painel; períodos e agrupamento; pesquisa; alternador e desenho escolhido; grade de cards, eixo longo, janela de 20 anos e texto do erro 400; capturas. Adaptadas: as de Indicadores (períodos, cards, gráficos, vazio), a espera média do Painel, a de capacidade e metas, e três de abas (`ListingChecks` e `UnitFlowChecks`) que procuravam o título "Indicadores da UBS" e o nome da UBS no banner.
- Capturas: `.build/redesenho/painel-indicadores/` (nomes `painel-*` e `indicadores-*`, claro e escuro, cards e gráficos, vazio, Semana e Ano, pesquisa, janela mínima de 980 px, pop-up do gráfico do card e botão Filtros com Personalizado). Conferidas uma a uma: sem corte, sem título repetido, sem card atrás de listagem, hover e cores nos dois temas.
- Não rodei `tests/desktop-startup.ps1` nem abri os programas na tela do usuário (regra deste fluxo).

## Pendências (números reservados P-090 a P-094)

- **P-090** · `UnitCapacityCards`, `BuildCapacityCards` (lado UBS) e `ApplyUnitCapacityAndGoals` (parte de capacidade) em `WorkspaceViewModel.UnitScheduling.cs`/`WorkspaceViewModel.cs` ficaram sem uso na tela, porque a ocupação da agenda agora vem do card resumido. Remover na consolidação, junto de quem for dono de `UnitScheduling.cs`.
- **P-091** · Teste só do usuário, ao longo do tempo: abrir o OBS e, no Painel da UBS e em Indicadores, trocar o período (inclusive 5 anos e Máximo), pesquisar, alternar cards e gráficos e olhar a janela no tamanho mínimo; observar se algum número destoa do que a Secretaria vê.
- **P-092** · Confirmar com o usuário que "Na fila" deve mostrar as pessoas aguardando (`waitingPatients`) e não as entradas (`activeEntries`).
- **P-093** · Os períodos de 5 anos, Máximo e personalizado acima de 366 dias só funcionam com a API que já aceita 20 anos (F9, outro grupo). Reconferir depois da implantação; com a API antiga a tela mostra o erro de período.
- **P-094** · Confirmar com o usuário a regra do Dia no Painel (espera e gráficos olhando os últimos 30 dias e 7 dias, ver "Decisões").

## Estado

- 26/11/2026: fundação juntada (fast-forward), build da linha de base com 0 erros, pedido lido e este arquivo criado.
- 26/11/2026: implementação das duas telas, do cabeçalho, do modo gráficos, da janela de 20 anos e do texto do erro 400; checagens adaptadas e novas; capturas conferidas nos dois temas; suíte completa com 250 PASS e 1 FAIL (P-066, preexistente). Arquivos novos: `TileGridPanel`, `HeaderSearchBox`, `UnitChartGrid`, `UnitChartCard` (modelo), `WorkspaceViewModel.PanelOverview.cs` e `WorkspaceViewModel.ChartCards.cs`.
