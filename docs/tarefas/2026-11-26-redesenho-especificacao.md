# Especificação do redesenho dos desktops — 2026-11-26

Complementa o pedido em [2026-11-26-redesenho-desktops-dados-capturas.md](2026-11-26-redesenho-desktops-dados-capturas.md) (itens A1…J3). Aqui ficam **como** cada item é feito e os nomes que todas as partes usam. Quem implementa uma tela lê o pedido, esta especificação e os dois anexos:

- [anexos/2026-11-26-mapa-desktops-nora.md](anexos/2026-11-26-mapa-desktops-nora.md): onde está cada coisa no código de partida (`8386b04`).
- [anexos/2026-11-26-referencia-aev.md](anexos/2026-11-26-referencia-aev.md): como o software de referência (AEV) faz cada padrão.

As skills do usuário `list-generator` (com `references/implementacao-wpf.md` e `references/armadilhas.md`), `selecao-listagem` e `navbar-generator`, em `~/.claude/plugins/marketplaces/meus-plugins-locais/my-skills/skills/`, são a descrição oficial dos padrões de listagem, seleção e barra lateral. Onde esta especificação não disser um número, vale o da skill.

## Regras para todas as partes

1. **Uma biblioteca, dois programas.** Quase tudo fica em `Nora.Desktop` e vale para o OBS e para o Painel. Diferença entre eles é decidida por `DesktopProfile`, nunca por cópia.
2. **Cor só por `DynamicResource`** dos temas. Toda chave nova entra em `LightTheme.xaml` **e** `DarkTheme.xaml` (uma checagem exige as mesmas chaves nos dois).
3. **Sem hex nas telas.** As cores fixas da Agenda e do `OutcomePie` viram chaves do tema.
4. **Comportamento preservado.** O redesenho muda forma e lugar; regra de negócio, chamadas de API, confirmações, mensagens de erro e ciclo de atualização continuam os mesmos, salvo o que o pedido muda.
5. **Acessibilidade preservada.** Todo controle interativo novo tem `AutomationProperties.Name` e funciona por teclado; `Esc` fecha pop-up e desfaz seleção.
6. **Operação demorada mostra o que faz** (regra do usuário): passos e indicador na cor da marca continuam onde já existem.
7. **Checagens acompanham a tela.** Cada parte atualiza as checagens de `tests/Nora.Desktop.Checks` que cobrem o que ela mudou e acrescenta as do comportamento novo. Não se apaga checagem para passar: adapta-se ao controle novo.
8. **Sem lixo:** estilo, propriedade, método ou arquivo que ficar sem uso depois da mudança é removido na mesma parte.

## Fundação (compartilhada)

### F1 — Barra de título própria (A3, A16)

- `ShellWindow` passa a usar `WindowChrome` (`System.Windows.Shell`): `CaptionHeight` igual à altura da barra, `ResizeBorderThickness="6"`, `GlassFrameThickness="0"`, `UseAeroCaptionButtons="False"`. Os cantos continuam vindo do Windows 11 (`ApplyWindowCorners`). `Title` continua `Profile.Name` (barra de tarefas e `tests/desktop-startup.ps1`), mas **não é desenhado**.
- Nova linha no topo da janela, acima da barra lateral e do conteúdo, `x:Name="TitleBar"`, na ordem:
  1. `ToggleSidebarButton` — abre e fecha a barra de navegação (também Ctrl+B);
  2. `NavBackButton` — voltar (Alt+←), desabilitado sem histórico;
  3. `NavForwardButton` — avançar (Alt+→), desabilitado sem o que avançar;
  4. `TitleSearchButton` — pesquisar (Ctrl+K);
  5. área livre que arrasta a janela (duplo clique maximiza);
  6. `MinimizeButton`, `MaximizeButton` (alterna com restaurar) e `CloseButton` (hover vermelho).
- A ordem é a que o usuário pediu (alternar, voltar, avançar, pesquisar), diferente da do AEV (alternar, pesquisar, voltar, avançar).
- Medidas e ícones do AEV (ver anexo, item 1): barra de 35 px, botões de ferramenta 36×35 e da janela 46×35, ícones desenhados com `Path`/`Line`, desabilitado com opacidade 0,38, dica mostrada também quando desabilitado. Todos com `WindowChrome.IsHitTestVisibleInChrome="True"`.
- **Cor:** a barra usa a mesma superfície da barra lateral (chave nova `TitleBarBrush`, claro e escuro), para as duas lerem como uma peça só; ícones na cor do texto; hover em azul bebê (`HoverBrush`); só o fechar tem hover vermelho com ícone branco.
- Janela maximizada não pode vazar para fora do monitor nem cobrir a barra de tarefas (tratar a margem do `WindowChrome` em `StateChanged`).
- `FullScreenService` (F11) continua funcionando: em tela cheia a barra de título some, como hoje.
- Na tela de acesso (`ShowCompactAccess`) e na troca obrigatória de senha aparecem só os três botões da janela.
- A confirmação "Fechar NORA OBS?" continua valendo para o `CloseButton` e para Alt+F4.

### F2 — Navegação: avançar, recolher e pesquisar (A3)

- `ShellViewModel` ganha a pilha de avanço: `ForwardCommand`, `CanGoForward`, `ForwardTip`. Navegar para uma rota nova limpa a pilha de avanço; voltar empilha a rota atual nela. Os limites de 50 rotas valem para as duas pilhas.
- `SidebarExpanded` passa a recolher de verdade: fechada, a barra lateral e o divisor somem e o conteúdo ocupa a largura toda; aberta, volta à largura salva. O estado continua persistido. Animação curta, como na `navbar-generator`.
- `TitleSearchButton` abre a **busca na tela atual**, a mesma do Ctrl+F que já existe (`ShellWindow.Search.cs`, `Generics/ScreenSearch.cs`). É o que o botão faz no AEV. A faixa de busca passa a abrir logo abaixo da barra de título, alinhada à direita do conteúdo.

### F3 — Cabeçalho da tela (A1, A6, A7, A13, A16)

- Saem a seta `BackButton` e o chip da UBS / "Gestão conectada".
- **Título:** `PageLabel` vira o caminho. Rota de subtela: `Subtela • Área` (ex.: `Listagem • Pacientes`, `Em andamento • Vaga Relâmpago`, `Grade semanal • Informações da UBS`). Rota sem subtela: só o título (`Painel da UBS`, `Agenda`). O separador é ` • ` (U+2022) na cor do texto.
- **Lado direito**, nesta ordem, tudo alinhado à direita:
  1. filtros em linha;
  2. botão **Filtros** (só se houver grupo de filtros que não seja em linha);
  3. ações da tela (alternador cards/gráficos, botão "+ Novo …").
- `PageFilterGroup` ganha `Inline` (bool). Grupo só de pesquisa (`ItemsTarget`) é **sempre em linha**: vira um campo "Pesquisar" com a lupa à direita (`SearchField`). Grupo com `Inline="True"` mostra o próprio conteúdo no cabeçalho (ex.: o combo de período). Os demais continuam no pop-up do botão Filtros.
- Novo `PageActions` (um `Decorator`, mesmo mecanismo de registro do `PageFilterGroup`): o conteúdo é hospedado no canto direito do cabeçalho enquanto a tela está visível. Fora da janela principal (prévias e testes) o conteúdo aparece na própria tela.
- Com a janela estreita, a pesquisa encolhe antes de qualquer botão sumir; o título corta com reticências.

### F4 — Menu do perfil e "Trocar perfil" (A4, A17, A18)

- Sai o botão "Configurações" do rodapé da barra lateral.
- Menu do perfil (clique no nome, canto inferior esquerdo), de cima para baixo, todos com ícone:
  1. **Trocar perfil** (ícone de troca) — só no OBS;
  2. **Configurações** (engrenagem);
  3. **Dados do perfil** (pessoa);
  4. **Sair da conta** (porta com seta).
- **Trocar perfil** abre um pop-up da marca (`SwitchProfileDialog`): título "Trocar perfil", X no canto superior direito, lista das UBS que a conta pode acessar (nome e região; a ativa vem marcada e selecionada), e o rodapé com **Sugerir pela localização** à esquerda do botão primário **Trocar**. "Trocar" confirma a UBS escolhida (`ConfirmObsUnitAsync`); X e Esc fecham sem trocar. "Sugerir pela localização" mantém o fluxo atual (confirmação, `ObsLocationDialog`, `SuggestClosestObsUnit`) e só **seleciona** a sugestão na lista.
- Some `ObsUnitPickerView` e o cartão "UBS da operação" de todas as telas. Onde hoje aparece `ObsUnitNoticeView` ("Escolher UBS no Painel"), o botão passa a abrir o pop-up.
- Conta com mais de uma UBS e nenhuma ativa (`NeedsObsUnitChoice`): o pop-up abre sozinho depois do login.
- O nome da UBS ativa continua visível no cartão do perfil (segunda linha), que é onde a pessoa confere em qual UBS está.

### F5 — Estilos (A14, A15, A19, A20)

- **Hover azul bebê:** `ButtonHoverBrush` e `HoverBrush` passam a um azul claro nos dois temas (claro: azul bebê; escuro: azul translúcido equivalente), e todos os estilos de botão (`Button`, `CircleIconButton`, `NavItemButton`, `TextActionButton`, botões da barra de título, dias da agenda) usam essas duas chaves. Botão primário mantém o degradê de hover da marca.
- **Perigo:** `ListActionDangerButton` — lixeira **branca** em fundo **vermelho** (`ErrorBrush`), hover um tom mais escuro, medidas do AEV. É o único jeito de remover, desativar, revogar, encerrar ou cancelar a partir de uma listagem. Toda ação dessas continua pedindo confirmação.
- **Cards com cor:** `MetricCard` ganha um tom (`Tone`: Azul, Verde, Amarelo, Rosa, Violeta, Turquesa, Neutro) que tinge o fundo de leve e colore o ícone e o valor. Cada tela escolhe o tom pelo sentido (concluídas verde, faltas rosa, cancelamentos amarelo, espera violeta, agendadas azul, ofertas turquesa).
- **Menos cards:** some o `Card` em volta de listagens e de formulários. Pop-up não tem card dentro.

### F6 — Componentes novos

| Nome | O que é |
| --- | --- |
| `OpenList` / `OpenListItem` | `ListBox` com `SelectionMode="Extended"`, sem borda nem fundo, rolagem por pixel; item arredondado (canto 8, `Padding 12,8`, `Margin 0,0,0,4`), hover e seleção do AEV. É a "Forma A" da `list-generator`. |
| `ItemTitleText` / `ItemMetaText` | As duas linhas do item: título em destaque e a linha de dados com ` | ` entre os campos informados. A linha de dados é montada no ViewModel (`MetaLine`), omitindo o que estiver vazio. |
| `StatusTag` | Etiqueta arredondada de situação, com tons do tema (Agendada, Chamada, Concluída, Falta, Cancelada, Ativo, Inativo…). |
| `SelectionBalloon` | O balão de ações: rótulo do item ou "N itens selecionados", fio, ações, fio, X. Flutua sobre a lista, embaixo, sem reservar espaço. Números e cores da skill `list-generator` (canto 12, `Padding 18,10`, base 26, sombra `20/6/0.22`, fundo tingido de azul). |
| `ListActionButton` / `ListActionEditButton` / `ListActionDangerButton` | Botões do balão, 38 px de altura, canto 6, ícone desenhado e rótulo curto ao lado (como o `RotuloDeAcao` do AEV). O de perigo é a lixeira branca em fundo vermelho. |
| `ListingBehavior` | Liga uma `OpenList` a um `SelectionBalloon`: mostra e esconde, rótulo, `Esc`, segundo clique e Enter abrindo a ficha (uma por vez), seleção preservada por chave ao recarregar ou filtrar. |
| `PageLayout.Fill` | Propriedade anexada: a tela (ou a subtela visível) recebe a altura da área de conteúdo e a rolagem externa desliga, para a lista rolar sozinha e o balão ficar no pé da tela. |
| `FormGrid` | Painel de formulário **já criado** em `Nora.Desktop/Views/FormGrid.cs`: colunas = quantas de 300 px couberem (mínimo 1, máximo 3), todas com a mesma largura; última linha incompleta fica centralizada com campos do mesmo tamanho. `FormGrid.FullRow="True"` para o campo que ocupa a linha toda. O método `ColumnsFor` está reservado para o usuário escrever (`TODO(human)`); enquanto isso devolve 1 coluna. **Ninguém mais altera esse método.** |
| `DialogService.ShowForm` + `FormDialog` | Pop-up de cadastro da marca: título, X, corpo com `FormGrid`, rodapé com o botão primário à direita. Devolve se salvou. |
| `ItemDetailsWindow` (refeita) | Ficha em **linhas horizontais**: rótulo à esquerda, valor à direita, fio entre as linhas, botão de copiar; sem cards. Abas continuam (paciente tem "Exames e vacinas"). |
| `NewItemButton` | Botão primário "+ Novo …" com o símbolo de mais desenhado. |
| `SegmentedToggle` | Alternador de segmentos (Linha | Pizza; Dia | Semana | Mês | Ano). |
| `ChartPanel` | Um gráfico com título e, à direita do título, o alternador Linha | Pizza. Usado na grade de gráficos e no pop-up do card. |
| `ViewModeToggle` | Botão do cabeçalho que alterna cards e gráficos; o ícone mostra o modo atual (cards) e troca para o de gráfico ao clicar. A escolha é lembrada por tela nas preferências. |
| `NoticeItem` | Item de aviso destacado em amarelo (ex.: "Horário ainda não informado"). |

### F7 — Sem botão "Atualizar" (A11)

Saem todos os botões de atualizar (lista no anexo do mapa, seção 10). O ciclo de 20 s, a leitura ao entrar na tela e a releitura depois de cada ação continuam. Depois de gravar algo, a tela relê na hora, sem esperar o ciclo. A faixa "dados desatualizados" continua avisando quando a leitura automática falha.

### F8 — Login salvo (A2)

- **Desktops:** a sessão é sempre gravada em `session.dat` (DPAPI) com o vencimento que a API devolveu: 24 h sem a caixa, 30 dias com "Manter conectado por 30 dias". Fechar o programa não apaga a sessão; "Sair da conta" apaga e avisa a API.
- **API:** sessão de paciente passa a 24 h por padrão e 30 dias com `keepSignedIn` (hoje 8 h, sem opção).
- **App:** caixa "Manter conectado por 30 dias" no login; token, vencimento e identidade guardados cifrados com chave do Android Keystore; ao abrir, o app confere a sessão em `/api/auth/me` e entra direto; "Sair" e sessão recusada apagam tudo. Vale também para o login com Google.

### F9 — Período longo nos indicadores (B2)

`MetricsService.MaxWindowDays` passa a aceitar até 20 anos nos indicadores (as listas de consultas e bloqueios continuam em 366 dias). Opções do período: **Dia** (hoje), **Semana** (7 dias), **Mês** (30), **Ano** (365), **5 anos** (1.826) e **Máximo** (20 anos). Em períodos longos o gráfico agrupa os pontos por semana ou por mês para continuar legível.

## Telas do OBS

### Painel da UBS (B1–B8)

- Sai o banner e o cartão da UBS. A tela começa em **"Resumo do dia"** (o texto acompanha o período: "Resumo da semana", "do mês", "do ano", "dos últimos 5 anos", "de todo o período").
- Cabeçalho: pesquisa em linha (filtra cards, gráficos e linhas de tabela pelo texto), combo **Período** em linha e o `ViewModeToggle`.
- **Modo cards:** os cards coloridos do resumo, com "Fluxo da unidade" (pacientes cadastrados, na fila, vagas ativas) incorporado ao mesmo grupo; "Consultórios ativos"; a tabela das consultas do período; "Atenção da equipe".
- **Modo gráficos:** uma grade de `ChartPanel`, um por indicador que tem dado para gráfico (consultas por situação ao longo do tempo, distribuição concluídas/faltas/canceladas, ofertas de Vaga Relâmpago, espera). Sem tabela, sem "Atenção da equipe".
- Gráficos vívidos: linha grossa com área em degradê e pontos, pizza em rosca com fatias destacadas; concluídas verde, faltas rosa-vermelho, **canceladas amarelo**, agendadas azul. As cores entram como chaves do tema.
- Pop-up do card: só o título e o alternador Linha | Pizza; sai "Valor atual: N".

### Pacientes (C1–C3)

- **Listagem:** `OpenList` na tela inteira (`PageLayout.Fill`). Item: nome; embaixo `CPF | e-mail | telefone`, só os informados; `StatusTag` "Desativado" quando for o caso. Cabeçalho: botão Filtros (busca, situação, presença, ordenação). Balão: Ver ficha, Editar ficha, Corrigir e-mail, e Desativar (lixeira) ou Reativar. Duplo clique abre a ficha.
- **Cadastro:** sem título e sem card; "Cadastrar referência na UBS" em `FormGrid`; endereço como seção opcional; **Cadastrar paciente** embaixo à direita.

### Família e Vínculos aprovados (D1–D3)

- Menu de Pacientes: Listagem, Cadastro, Família, **Vínculos aprovados** (rota `family:approved`; Família é `family:requests`).
- **Família:** uma `OpenList` com os pedidos pendentes (responsável → dependente; CPF dos dois na linha de dados). Balão: Aprovar, Recusar.
- **Recusar** abre pop-up com "Motivo da recusa (opcional, até 280 caracteres)", botões **Enviar** e **Fechar**. Depois: "Pedido recusado." ou "Pedido recusado e motivo enviado ao responsável."; aprovar mostra "Pedido aprovado.".
- **Vínculos aprovados:** `OpenList`; balão com Encerrar vínculo (lixeira).

### Agenda (E1–E5)

- Cabeçalho: pesquisa em linha, `SegmentedToggle` **Dia | Semana | Mês | Ano** em linha e **+ Agendar nova consulta** (abre o agendamento em pop-up).
- **Dia:** a lista das consultas do dia, em ordem de horário.
- **Semana:** tabela com os sete dias no topo e as horas na lateral; cada consulta no seu horário, colorida pela situação.
- **Mês:** o calendário atual. Dia selecionado: fundo azul bem claro e círculo azul bebê no número; hover azul bebê; hoje com contorno. As bolinhas de situação continuam.
- **Ano:** os 12 meses em 4×3, cada um um mini-calendário com os dias que têm consulta marcados; clicar num mês abre o mês.
- Lista do dia (em Dia e abaixo do calendário em Mês): sem título de data. Item em duas linhas (nome; "08:20 às 08:40") e `StatusTag` à direita. Balão: Chamar, Concluir, Registrar falta, Cancelar (lixeira), conforme a situação.
- Duplo clique: ficha em linhas (paciente, CPF, especialidade, profissional, consultório, horário, situação, marcada por, presença).

### Vaga Relâmpago — Em andamento e Encerradas (F1–F3)

Sem título e sem a faixa de totais. Filtros (especialidade, situação, pesquisa) no botão Filtros. `OpenList`: horário e especialidade; embaixo `profissional | paciente | prazo`; `StatusTag` à direita. Só leitura: sem balão; duplo clique abre a ficha com as ofertas.

### Indicadores, Alertas e Comunicados (G1, G2)

- **Indicadores:** `ViewModeToggle` como no Painel; período em linha; no modo cards, só os essenciais (comparecimento, concluídas, faltas, canceladas, espera média, ocupação da agenda) e a tabela por especialidade; no modo gráficos, a grade de `ChartPanel`. Metas e score continuam num bloco curto.
- **Alertas:** só a `OpenList` de alertas (tipo e especialidade; embaixo valor e limite; etiqueta de gravidade).
- **Comunicados:** só a `OpenList` (título; embaixo publicação e validade; etiqueta "Não lido"). Balão: Marcar como lido. Duplo clique abre o texto.

### Informações da UBS (H1–H6)

- **Atendimento:** sem card e sem título. `NoticeItem` amarelo no topo enquanto o horário não foi informado. Uma linha por dia: caixa de seleção com o nome do dia; marcada, mostra `Abre` **às** `Fecha` (campos mais baixos); desmarcada, o dia fica fechado e sem campos. Um intervalo só por dia. "Serviços oferecidos" continua. **Salvar** embaixo à direita.
- **Profissionais / Consultórios:** `OpenList` na tela inteira; **+ Novo profissional** / **+ Novo consultório** no cabeçalho abrem `FormDialog`. Balão: Editar e Desativar (lixeira) ou Reativar.
- **Grade semanal:** sete botões (Segunda … Domingo; segunda selecionada ao abrir) e, abaixo, a `OpenList` dos horários do dia. **+ Novo horário** no cabeçalho abre `FormDialog` com **Salvar horário na grade** à direita. Balão: Editar, Remover (lixeira).
- **Bloqueios:** `OpenList`; **+ Novo bloqueio** abre `FormDialog` com **Cadastrar bloqueio**. Balão: Remover (lixeira).
- **Computadores:** saem a subtela, o item do menu e o código de interface que só ela usa. A ativação por computador continua desligada no servidor, como já estava.

## Painel Municipal (I1)

Tudo o que é da fundação vale igual. Por tela:

- **Visão geral:** sai o banner "Toda a rede em um só lugar" e o botão de atualizar; `ViewModeToggle`, período e pesquisa como no Painel da UBS; cards coloridos.
- **Indicadores (Desempenho, Vaga Relâmpago, Alertas, Relatórios e metas), Capacidade, Desempenho das unidades, Demanda:** cards coloridos, `ViewModeToggle` onde há cards e gráficos, filtros no cabeçalho, tabelas e listas sem card.
- **Listagens (Cadastro de UBS, Cadastros de referência, Catálogo de especialidades, Contas, Acessos às UBS, Comunicação, Histórico, Alertas):** `OpenList` + balão + ficha; "+ Novo …" no cabeçalho.
- **Cadastros (UBS, Nova conta, Comunicado, Metas):** `FormGrid`, botão primário à direita, sem card.

## Dados, capturas e slides (J1–J3)

- **Script de dados** `scripts/popular-dados-demonstracao.ps1`: fala com a API (login da Secretaria e do administrador do OBS com senha digitada às escondidas) e, onde a API não permite (consultas em datas passadas e suas situações), usa um arquivo SQL parametrizado aplicado pelo `psql` no banco da UBS. Só acrescenta; pode ser rodado de novo sem duplicar. Mostra cada passo.
- **Capturas:** tiradas pelo harness (`Nora.Desktop.Checks --visual-gallery`) e do app pelo emulador, com o banco local populado pelo mesmo script.
- **Entrega:** pasta `Mateus-Projects/Claude-HTML/Projeto-NORA/capturas-2026-11/` com as imagens, um `.xlsx` com as mesmas imagens e a lista das 20 melhores, das 10 principais e dos 4 slides.

## Ordem de execução

1. Fundação F1–F7 (em série, na branch principal do trabalho).
2. Em paralelo, cada uma na sua pasta: as telas do OBS (cinco grupos), as do Painel (dois grupos), F8 + F9 (API e app) e o script de dados.
3. Integração: juntar, compilar, rodar todas as checagens, abrir os dois programas.
4. Revisão item a item contra o pedido, com correção, até uma volta sem achados.
5. Dados locais, capturas, Excel e indicação das imagens.
6. Documentação e pendências.
