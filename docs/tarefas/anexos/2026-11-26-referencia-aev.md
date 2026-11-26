# Referência de interface: AEV (Software-WPF) — levantamento de 26/11/2026

O usuário pediu que o OBS e o Painel sigam "o meu software WPF". Os padrões que ele descreve (botões na barra de título, listagem solta com balão de ações, lixeira branca em fundo vermelho) estão no **AEV**, de onde saíram as skills `list-generator`, `selecao-listagem` e `navbar-generator`. O sistema de estoque (`OliverImportsBusinnes/Sistema-Gerenciamento-Estoque`) foi lido também e serve só para a **ficha em linhas horizontais** (ver item 7).

**Código lido (somente leitura):** `C:\Users\Pichau\source\repos\AEV-autoescola-Virtual\Software-WPF\Software WPF\` (commit `ae13ed7`, 17/11/2026, branch `main`). Os caminhos abaixo são relativos a essa pasta. Quem implementa pode e deve abrir esses arquivos para copiar os números exatos.

## 1. Barra de título (`MainWindow.xaml`)

- `WindowChrome`: `CaptionHeight="35"`, `ResizeBorderThickness="7"`, `CornerRadius="0"`, `GlassFrameThickness="0"`, `UseAeroCaptionButtons="False"` (linhas 26-32). Cantos do Windows 11 por DWM (`MainWindow.Win32.cs:25,95`).
- Uma linha de 35 px no topo da janela inteira. Colunas `[*, Auto, Auto, Auto]`: ferramentas à esquerda, depois minimizar, maximizar, fechar (106-111). O centro é vazio e arrasta a janela (duplo clique maximiza; `MainWindow.xaml.cs:59-81`).
- A barra tem a **mesma cor da barra lateral**, para as duas lerem como uma superfície só.
- Botões da janela: 46×35, fundo transparente, sem foco de teclado, `WindowChrome.IsHitTestVisibleInChrome="True"` (35-66). Botões de ferramenta: 36×35, desabilitado com opacidade 0,38, dica mostrada mesmo desabilitado (67-79).
- Ícones desenhados, nunca fonte de ícones (113-204):
  - barra lateral: `Border` 13×13 com borda 1 e canto 1,1 mais uma `Line` vertical em x=4,5;
  - pesquisar: `Ellipse` 12×12 com traço 1,9 mais uma `Line` de (10,9;10,9) a (13,1;13,1);
  - voltar: `M 7.75,3.25 L 3.25,8 L 7.75,12.75 M 3.25,8 L 15.25,8` (16×16, traço 1,2, pontas redondas);
  - avançar: `M 8.25,3.25 L 12.75,8 L 8.25,12.75 M 12.75,8 L 0.75,8`;
  - minimizar `M 1,5 L 9,5`; maximizar `Border` 10×10 de borda 0,9; restaurar dois `Border` 7,8×7,8 sobrepostos; fechar `M 1,1 L 9,9 M 9,1 L 1,9` com hover `#E3262E`.
- **Pesquisar** abre a busca **na tela atual** (Ctrl+F): faixa flutuante com "Localizar:", campo, "N de M", Anterior, Próximo e Fechar (`Views/Options.Pesquisa.cs:16-22`, `Generic/PesquisaNaTela.cs`). Não é pesquisa global.
- Voltar e avançar: duas pilhas de rotas; navegar para uma rota nova limpa a de avanço (`Views/Options.Navegacao.cs:14-15, 277-301, 381-408`).

## 2. Título da tela

Não existe caminho "Listagem • Alunos" no AEV: cada página tem o título grande no corpo. O formato `Subtela • Área` na barra do cabeçalho é pedido novo do usuário para o NORA.

## 3. Barra lateral e perfil

- Largura 210, alça de 6 px, fecha abaixo de 105, máximo 50% da janela, animação de 0,3 s (`Views/Options.BarraLateral.cs:21-26`), como na skill `navbar-generator`.
- Rodapé do perfil: botão de 60 px com foto 34×34, nome (13 SemiBold) e segunda linha (12). Clique abre um `Popup` acima (`Placement="Top"`, `StaysOpen="False"`, `Fade`): `Border` de 220 px, canto 10, `Padding 6`, sombra `BlurRadius 16 / ShadowDepth 4 / Opacity 0.35`. Itens de 42 px, canto 8, 14 SemiBold, **ícone `Path` 17×17 à esquerda do texto** (`Views/Options.xaml:15-72, 290-371`). "Sair" pede confirmação. Não há "Trocar perfil" no AEV (pedido novo).

## 4. Listagem em tela inteira (`Views/ListUsers.xaml`)

- Página em três linhas `Auto / Auto / *`: cabeçalho, filtros, lista. Margens `20,10,20,6`, `20,10,20,10` e `20,0,20,10`.
- A lista fica **solta sobre o fundo da página**, sem card: `ListBox` com `CompactSelectableListStyle` (`Generic/Estilos.Listagens.xaml:421-431`: fundo transparente, sem borda, `AlternationCount 2`, rolagem vertical automática). `SelectionMode` vira `Extended` em `Generic/ListaSelecionavel.cs:47-50`.
- Item `CompactListItemContainerStyle` (357-412): `Padding 12,8`, `Margin 0,0,0,4`, canto 8, borda 1 px; hover, selecionado (borda da cor de destaque com 1,6 px), foco de teclado (borda 1,4) e faixa alternada. Variante baixa `MinimalListItemContainerStyle`: `Padding 11,6`, `Margin 0,0,0,3`.
- Cores claro / escuro: fundo `#FFFFFF` / `#35353B`; alternada `#F1F6FC` / `#39393F`; hover `#E9F1FA` / `#404047`; selecionado `#DCE9FD` / `#4C4C57`; foco `#5B9BD5` / `#6E9BD4`.
- Template em duas linhas: bolinha de situação + nome (16 Bold) + selo; embaixo a linha de contato (12, cor secundária), montada no ViewModel com `"  |  "` entre os campos (`ViewModels/UsuarioListItemViewModel.cs:31`). No NORA a linha **omite** o campo vazio (pedido do usuário).
- Botão novo: `"+  Nova aula"`, 150×40, `PrimaryActionButtonStyle`, na primeira linha, à direita (`Views/ClassManager.xaml:50-53`).
- Estado vazio: texto centralizado 14, cor secundária (`ListUsers.xaml:224-226`).
- Filtrar mantém a seleção (`ListaSelecionavel.TrocarItensMantendoSelecao`, 125-134); clicar fora da lista limpa (143-215).

## 5. Balão de ações (`Generic/BalaoDeAcoes.cs`, `Estilos.Listagens.xaml:590-696`)

- É um controle na **mesma célula da lista**, `HorizontalAlignment=Center`, `VerticalAlignment=Bottom`, `Collapsed` por padrão. Não reserva espaço. Sem animação.
- Conteúdo: rótulo (13 SemiBold; nome do item ou "N usuários selecionados"), complemento opcional, fio vertical de 1 px, botões, fio, X de 30×30.
- Com vários selecionados: "Editar" some; ação que não serve a todos some (não fica desabilitada); a confirmação é uma só para o lote.
- Números no AEV de hoje: canto 4, `Padding 14,7`, fundo cinza em degradê. A skill `list-generator` fixa os números mais novos (canto 12, `Padding 18,10`, base 26, sombra `20/6/0.22`, fundo tingido de azul `#DCEBFF` / `#2A3A52`, borda `#8FB8E6` / `#3F5A7D`). **No NORA valem os da skill.**

## 6. Botões de ação da lista (`Estilos.Listagens.xaml:224-354`)

- Base 38×38, canto 6, `Margin 0,0,8,0`; com rótulo curto ao lado do ícone (`Generic/RotuloDeAcao.cs`: `MinWidth 38`, `Padding 10,0,12,0`, texto 13 SemiBold a 7 px do ícone).
- Neutro: contorno 1,4 px, hover `#E8EEF6` / `#44444C`. Editar: fundo azul, traço branco.
- **Perigo:** fundo `#C02626` / `#A83A3A`, hover `#A11D1D` / `#BF4545`, **lixeira de traço branco** (`IconTrashGeometry`, `Estilos.Icones.xaml:98-115`: `M 3.2,4.6 L 14.8,4.6 M 7,4.6 L 7,3 L 11,3 L 11,4.6 M 4.8,4.6 L 5.6,15.2 L 12.4,15.2 L 13.2,4.6 M 7.6,7.4 L 7.8,12.6 M 10.4,7.4 L 10.2,12.6`, 18×18, traço 1,5). Sempre com confirmação.

## 7. Ficha de detalhe

- Abre no segundo clique numa linha já selecionada e no Enter; várias selecionadas abrem em fila, uma por vez (`ListaSelecionavel.cs:47-103`).
- No AEV é uma janela sem moldura que cobre a principal, com fundo escurecido, X vermelho 34×34, Esc e clique fora fechando; os campos têm **rótulo em caixa alta em cima e valor embaixo**.
- O usuário pediu a ficha "em linhas horizontais": é o desenho do sistema de estoque (`Sistema-Gerenciamento-Estoque/Views/TelaEstoque.xaml:1120-1333`): cada campo é uma linha com **rótulo à esquerda (13,5, cor secundária), valor à direita (14,5 SemiBold) e botão de copiar 28×28**, `Margin 0,10`, separadas por um fio de 1 px. **No NORA vale esse desenho.**

## 8. Pop-up de cadastro (`Views/ClassManager.xaml:199-354`, `Generic/ModalStyles.xaml`)

- Cartão centralizado (`MaxWidth 720`, canto 8), cabeçalho com título 22 Bold e X à direita, corpo com rolagem, rodapé com **Cancelar** e o **primário à direita** (142×42).
- No AEV os campos ficam num `Grid` fixo de duas colunas. A grade que se adapta à largura (300 px por campo) é pedido novo; a conta é a mesma de `Generic/GradeAdaptavel.cs:53-62`, usada lá para cards.
- Campo: rótulo 14 SemiBold; caixa de 44 px de altura mínima, canto 6; o foco muda só a cor da borda.

## 9. Hover

Não há um recurso chamado "azul bebê". Os azuis claros do AEV: `#E6F2FF` (hover de filtro), `#D1EAFF` (hover de item de combo), `#BFDBFE` (hover do botão de informação), `#E9F1FA` (hover de linha). No NORA o hover dos botões usa essa família.

## 10. Cards e gráficos

- Card de indicador: canto 12, `Padding 16,12`, **faixa vertical de 4 px na cor do indicador**, rótulo 12,5 SemiBold e valor 32 SemiBold na mesma cor (`Views/Dashboard.xaml:17-51`). Cores: azul `#2446B8`, ciano `#27AEEB`, verde `#16B889`, amarelo `#F3B61F`, vermelho `#E3262E`; no tema claro o número usa tons mais escuros para manter o contraste.
- Gráficos do AEV usam LiveCharts2. O NORA desenha os seus sem pacote externo; a paleta vívida a seguir é a do AEV: azul `#3B82F6`, âmbar `#F3B61F`, verde `#16B889`, vermelho `#E3262E`, ciano `#27AEEB`, laranja `#F97316`.
- Alternância cards ↔ gráficos e Linha | Pizza não existem no AEV (pedido novo).

## 11. Atualização

As listagens do AEV não têm botão "Atualizar": um temporizador recarrega a tela quando os dados mudam, e Ctrl+R recarrega na mão.

## O que não existe no AEV (e é desenho novo no NORA)

Caminho `Subtela • Área`, "Trocar perfil", lupa no campo de pesquisa, grade de formulário por largura, calendário com dia em círculo, horário de funcionamento por dia, alternadores cards/gráficos e Linha/Pizza.
