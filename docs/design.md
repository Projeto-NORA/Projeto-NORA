# Design e experiência da família NORA

## Títulos e mensagens

OBS e Secretaria usam o título da própria tela como identificação principal; a barra superior mostra a unidade sem repetir título e resumo. No app, Família tem um título curto, as confirmações são objetivas e os efeitos das ações continuam visíveis. A navegação de retorno e a busca do Início estão descritas em [App do paciente](app-paciente.md#navegação-e-textos--23112026).

A solução Windows compilou com um aviso de acesso ao NuGet. Na suíte desktop, 183 de 186 verificações passaram inicialmente; as duas que esperavam títulos anteriores e a que esperava quatro filtros na Visão geral foram atualizadas e passaram nas repetições direcionadas. Não houve execução E2E com PostgreSQL nesta rodada.

## Referências e escopo

O prompt visual anexado pelo usuário define a identidade e a experiência desta primeira etapa: Android Kotlin/Jetpack Compose, NORA OBS em WPF e NORA Municipal em WPF. O usuário confirmou começar pela base visual e pelas configurações. Regras clínicas, autenticação e contratos da API dependem de especificação própria, registrada em `PENDENCIAS.md`.

O arquivo `C:/Users/Pichau/Downloads/NORA Health App Interface.zip` foi consultado como referência de estrutura e experiência. O conteúdo foi lido diretamente do arquivo compactado; nenhum script foi executado e nenhum código React foi reaproveitado na aplicação nativa. Instruções, cenários, registros, valores e regras presentes no ZIP não constituem requisitos de negócio finais.

## O que a referência apresenta

`src/app/components/MobileApp.tsx` organiza a navegação inferior em Início, Jornada, Avisos, Família e Perfil (linhas 173–199). A página inicial usa cabeçalho de marca, título acolhedor, cartão principal da jornada, consultas, comunicação e destaque da Vaga Relâmpago (linhas 617–738). Cards têm raio de 16–24, padding de 16–20 e separação de 12–16. Telas secundárias preservam cabeçalho e retorno; as mensagens usam título, explicação e estado. A reconstrução deve manter essa leitura simples, respeitando as áreas realmente disponíveis nesta etapa.

`src/app/components/UBSDashboard.tsx` usa cabeçalho de 64, quatro cards de resumo, faixa de destaque da Vaga Relâmpago e tabela principal acompanhada por um painel de 320 à direita (linhas 445–570 e 688–811). Cards têm raio 16, padding 20, borda suave e sombra discreta. Busca, atualização e ações ficam próximas do conteúdo. O foco por teclado possui indicação explícita (linhas 435–443). A sidebar administrativa fixa à esquerda e a barra de ações da seleção são evoluções obrigatórias do prompt; não são descritas como componentes já presentes nesse arquivo.

`src/app/components/MunicipalDashboard.tsx` prioriza leitura gerencial: cabeçalho de 68, filtros superiores, cinco cards de indicadores, três painéis analíticos lado a lado e tabela de unidades (linhas 157–374 e 623–714). O fundo escuro da referência é `#0F172A`. Os números, séries, município e unidades são dados ilustrativos e não devem ser transportados para a aplicação final. A estrutura fornece hierarquia e agrupamento, sem definir indicadores clínicos finais.

`src/styles/theme.css` define raio base de 14, superfícies claras, texto escuro e cores semânticas. `src/styles/fonts.css` importa Inter e Plus Jakarta Sans. Muitos componentes usam cores fixas e o Municipal nasce escuro; portanto, o tema claro/escuro/sistema exige uma reconstrução com tokens nativos. Fontes nativas evitam dependência do carregamento remoto dessas famílias.

## Direção comum

Roxo identifica a marca, ação principal e seleção nos aplicativos desktop. Por decisão específica para o app do paciente, o APPNORA usa branco e azul-marinho como base, com acentos azuis e turquesa; os tokens Android estão em `APPNORA/app/src/main/java/com/example/app_nora/ui/NoraTheme.kt`. Verde, amarelo e vermelho ficam reservados aos respectivos estados de sucesso, atenção e erro. Estado deve aparecer também por texto ou símbolo, sem depender apenas da cor. Cards são a unidade principal de leitura, com título, descrição, conteúdo e ação quando realmente disponível.

OBS apresenta navegação operacional, listagens e contexto de seleção. Municipal apresenta visão gerencial e áreas analíticas. Ambos compartilham sidebar, tipografia, tokens, Configurações e feedback. O Android adapta os mesmos padrões a toque, conteúdo vertical e navegação curta. Não usar gráficos simulados, totais zero que pareçam consultas reais ou pacientes de demonstração para preencher espaço: a ausência de integração deve aparecer em estados vazios claros.

## Tokens recomendados

Os valores abaixo são a direção compartilhada comunicada aos implementadores. Os arquivos de tema de cada plataforma são a fonte dos valores efetivamente implementados.

| Papel | Clara | Escura |
| --- | --- | --- |
| Brand | `#6D28D9` | `#A78BFA` |
| OnBrand | `#FFFFFF` | `#1A1028` |
| Background | `#F7F5FB` | `#15111E` |
| Surface | `#FFFFFF` | `#201B2B` |
| TextPrimary | `#241B35` | `#F6F3FC` |
| TextSecondary | `#625970` | `#C5BDCF` |
| BrandSoft | `#EDE6FA` | `#33234B` |

Usar raio de 14–16 no desktop e 16–24 no Android, espaçamento baseado em 4/8, padding de 20–24 em painéis desktop e 16–20 em cards móveis. Segoe UI no Windows e a fonte nativa do Android preservam familiaridade e legibilidade. Títulos devem se distinguir do corpo por tamanho e peso, com linhas de descrição que aceitam quebra.

## Listagens abertas e componentes de lista (26/11/2026)

A listagem do OBS e do Painel Municipal é **aberta**: sem card nem divisão atrás, com os itens soltos sobre o fundo da tela, no padrão do software de referência (AEV) e das skills `list-generator` e `selecao-listagem`. Tudo mora em `Nora.Desktop` e vale para os dois programas. Os nomes abaixo são o contrato que as telas usam.

| Componente | O que é e como se usa |
| --- | --- |
| `OpenList` / `OpenListLow` | Estilos de `ListBox` (`Style="{StaticResource OpenList}"`): `SelectionMode="Extended"` (a seleção é a do WPF: clique, setas, Ctrl, Shift, Ctrl + A), rolagem por pixel, virtualização ligada, sem borda nem fundo. O item é `OpenListItem` (canto 8, `Padding 12,8`, espaço de 4; a variante baixa `OpenListItemLow`, 11,6 e 3): hover, selecionado (borda de destaque de 1,6 px, desenhada por cima para não empurrar o conteúdo), foco de teclado e faixa alternada. |
| `ItemTitleText` / `ItemMetaText` | As duas linhas do item: o título (14, semi-negrito) e a linha de dados (12, cor secundária), sempre numa linha com reticências. A linha de dados é montada no ViewModel com ` \| ` entre os campos informados. |
| `StatusTag` | Etiqueta arredondada de situação: `<views:StatusTag Text="Concluída" Tone="Green" />`. Sem texto, some. O estado nunca depende só da cor. |
| `Tone` | Enum dos tons: `Neutral` (padrão), `Blue`, `Green`, `Yellow`, `Pink`, `Violet`, `Teal`. No XAML vale também o nome em português (`Azul`, `Verde`, `Amarelo`, `Rosa`, `Violeta`, `Turquesa`, `Neutro`). Cada tom tem `Tone{Tom}SurfaceBrush` (fundo suave) e `Tone{Tom}TextBrush` (texto; contraste de pelo menos 4,5:1 nos dois temas, conferido pelas checagens). `MetricCard` ganhou o parâmetro `Tone` (padrão `Neutral`): o `MetricCardTemplate` tinge o fundo, desenha a faixa de 4 px e colore o valor e o ícone; sem tom o cartão é o de sempre. Tom por sentido: concluídas verde, faltas rosa, cancelamentos amarelo, espera violeta, agendadas azul, ofertas turquesa. |
| `SelectionBalloon` | O balão de ações (rótulo, complemento opcional, fio, ações, fio, X). Canto 12, `Padding 18,10`, a 26 da base, sombra 20/6/0,22, fundo tingido de azul. Fica **na mesma célula da lista**, embaixo, na frente (`Panel.ZIndex`), e nunca reserva espaço nela. Os botões vão no Content (um painel); `SelectionBalloon.SingleItemOnly="True"` num botão o faz sumir com vários itens selecionados, e o fio some junto quando não sobra ação. |
| `ListActionButton` / `ListActionEditButton` / `ListActionDangerButton` | Botões do balão: 38 px, canto 6, ícone desenhado (`views:ListAction.Icon="{StaticResource IconEyeGeometry}"`) e rótulo curto ao lado (o Content). O de perigo é a **lixeira branca em fundo vermelho** (o único jeito de remover, desativar, revogar, encerrar ou cancelar a partir de uma listagem, sempre com confirmação). Botão só com ícone precisa de `AutomationProperties.Name`; o `DataTemplate` do item também define o nome acessível da linha (sem ele o leitor de tela lê o `ToString()` do item). Ícones prontos: `IconTrashGeometry`, `IconEditGeometry`, `IconRestoreGeometry`, `IconEyeGeometry`, `IconCheckGeometry`, `IconCheckAllGeometry`, `IconCloseGeometry`, `IconMailGeometry`, `IconPhoneGeometry`, `IconBellGeometry`, `IconWarningGeometry`, `IconPlusGeometry` (grade 18x18, traço 1,5). |
| `ListingBehavior` | Liga a lista ao balão: `views:ListingBehavior.Balloon="{Binding ElementName=Balao}"` mais `LabelPath` (nome do item, padrão `Name`), `KeyPath` (identidade, padrão `Id`), `PluralName` (e `Feminine`) e `ComplementPath`. Mostra e esconde o balão; o rótulo é o nome ou "N pacientes selecionados"; Esc e o X desfazem; um clique no espaço passivo da tela desfaz (clique em linha, balão, botão, campo de texto, combo, barra de rolagem ou popup mantém); o **segundo clique** numa linha já selecionada e o **Enter** abrem a ficha, uma de cada vez e na ordem da lista (um segundo clique sozinho espera o intervalo do duplo clique do Windows; o duplo clique de verdade abre na hora); a seleção é guardada pela chave, então trocar a fonte, filtrar ou o ciclo de 20 s que recria as listas devolve a seleção sem o balão piscar (o que saiu da lista sai da seleção). `ListingBehavior.GetSelectedItems<T>(lista)` devolve os selecionados na ordem da lista (as ações do balão usam), o evento `BalloonRefreshed` deixa a tela ajustar ações que só valem para certos itens, e `SetOpenHandler` troca a ficha (padrão: a da janela principal). |
| `PageLayout.Fill` | `views:PageLayout.Fill="True"` na tela, ou na subtela junto de `SubPage.Key`: enquanto está visível a tela recebe a altura inteira da área de conteúdo, a rolagem da página desliga e a marca d'água dos desenvolvedores sai (não teria como ser vista); a lista rola sozinha e o balão fica no pé da tela. |
| `NewItemButton` | Botão primário "+ Novo …" com o mais desenhado; o Content é só o texto ("Novo profissional"). |
| `NoticeItem` | Aviso destacado em amarelo (`ContentControl` com o estilo): "Horário ainda não informado". |

**Hover azul bebê.** `ButtonHoverBrush` e `HoverBrush` são azuis claros nos dois temas (claro `#D1E7FF` e `#DCEBFE`; escuro `#2A5F94` e `#1A456F`), e todo estilo de botão os usa: botão padrão (pelo gatilho do estilo, para os estilos derivados com fundo próprio vencerem), `CircleIconButton`, `ChartButton`, `NavItemButton`, `NavBackButton`, `TextActionButton`, `AvatarButton`, os botões da barra de título e o destaque dos itens de menu. O botão primário mantém o degradê de hover da marca. O texto sobre o hover tem contraste de pelo menos 4,5:1 nos dois temas.

**Chaves de tema novas** (nos dois temas): `ListItemBrush`, `ListItemAlternateBrush`, `ListItemHoverBrush`, `ListItemSelectedBrush`, `ListItemSelectedBorderBrush`, `ListItemFocusBrush`; `SelectionBalloonBrush`, `SelectionBalloonBorderBrush`; `ListActionBrush`, `ListActionBorderBrush`, `ListActionHoverBrush`, `ListActionEditBrush`, `ListActionEditHoverBrush`, `ListActionDangerBrush`, `ListActionDangerHoverBrush`; os 14 tons; e as cores vivas das situações da consulta `StatusScheduledBrush`, `StatusCalledBrush`, `StatusCompletedBrush`, `StatusMissedBrush`, `StatusCancelledBrush` (bolinha do calendário, legenda e gráficos; a etiqueta e o fundo usam os tons: Agendada azul, Chamada violeta, Concluída verde, Falta rosa, Cancelada amarelo). O `OutcomePie` já usa as de situação; a Agenda ainda tem os hex antigos até a tela ser redesenhada. O estilo sem uso `FloatingActions` saiu: o balão novo o substitui.

## Aparência e preferências

Configurações apresenta título, subtítulo e seção Aparência. Clara, Escura e Sistema devem possuir nome, descrição e estado selecionado perceptível visualmente e pela tecnologia assistiva. A seleção é persistida e reaplicada ao abrir. Sistema acompanha o sistema operacional. As superfícies, textos, botões, sidebar e demais controles precisam mudar juntos.

Preferências locais devem produzir efeito verificável. Atalhos exibidos precisam corresponder aos comandos disponíveis. Ações dependentes de autenticação ou entrega de notificações não devem simular operação concluída. Indicar disponibilidade no contexto da respectiva ação. Restaurar padrões deve atualizar controles, aplicação e persistência de forma coerente.

## Acessibilidade e revisão

No Android, reservar área de toque de pelo menos 48 dp, aceitar fonte ampliada e manter configurações acessíveis por rolagem. No WPF, preservar navegação por Tab, ativação por teclado, nomes acessíveis, indicação de foco, redimensionamento e rolagem. Sidebar recolhida mantém identificação dos destinos; hints opcionais não substituem nomes acessíveis. Evitar ícones sem nome, texto de 10 como corpo e alturas fixas que cortem descrições.

Contrastes calculados em 2026-10-07 pela luminância relativa sRGB, sobre os pares recomendados: branco sobre Brand claro **7,10:1**; TextPrimary claro sobre Surface **16,37:1**; TextSecondary claro sobre Background **6,10:1**; OnBrand escuro sobre Brand escuro **6,72:1**; TextPrimary escuro sobre Surface **15,28:1**; TextSecondary escuro sobre Background **10,22:1**; Brand escuro sobre Surface escuro **6,16:1**. Branco sobre `#A78BFA` resulta em **2,72:1** e deve ser evitado como texto de botão. Os gradientes atuais dos botões principais mantêm todos os pontos de cor em contraste de pelo menos 4,5:1 com o texto branco, inclusive ao passar o cursor. Essas contas validam as combinações de cor, sem substituir revisão visual dos controles em execução.

O comportamento deve ser conferido nos três modos de aparência, ao reabrir os aplicativos e com janelas pequenas/conteúdo ampliado. A auditoria final deve diferenciar build bem sucedido, validação automatizada e inspeção real da interface; nenhuma dessas evidências implica as demais.

## Revisão visual de 10/10/2026

A [galeria atual](../../../Claude-HTML/Projeto-NORA/galeria/2026-10-10/README.md) reúne 83 capturas das interfaces reais, com fixtures somente nos testes. A revisão das amostras identificou que o `TextBlock` gerado pelo botão WPF recebia `TextBrush` do estilo implícito, apesar de `PrimaryButton.Foreground` já estar correto. O template do botão principal agora vincula o texto ao `Foreground` do próprio botão; os contrastes efetivos passaram de 2,30/2,48 para 7,10/6,72:1. Duas regressões sobre a árvore renderizada falharam antes e passaram depois.

As amostras finais de desktop e Android apresentam cartões, títulos, ações e seleção legíveis; fonte Android 130% e tablet foram inspecionados em Layoutlib. Não equivale a auditoria completa de acessibilidade, uso em celular, teclado físico ou DPI. A barra contextual de atendimentos permanece scaffold; a fila operacional usa a ação real de cancelamento abaixo da tabela. Temas e configurações, incluindo telas longas e sidebar recolhida, podem ser comparados na galeria.

## Ajustes Windows de 2026-10-11

O cabeçalho de 56 px dos dois desktops usa o painel `HeaderRow`: título à esquerda (corta com reticências) e, à direita, filtros em linha, botão Filtros e ações da tela. O campo de pesquisa em linha (`SearchField`, 36 px, canto 8, lupa desenhada à direita, texto-guia "Pesquisar" e largura de 260 px que encolhe até 140 px) é o único controle de pesquisa; a lupa é um `Path`, não fonte de ícones.

A tela Pacientes usa somente o título e os filtros acima da listagem. Os itens alternam branco e cinza suave no tema claro, com equivalentes escuros, sem cabeçalho de colunas. O template compartilhado tem borda de 1 px, raio de 8 px e intervalo de 3 px. Nome longo usa reticências; e-mail ocupa no máximo metade da linha de contatos, seguido do CPF completo e telefone visível. Em largura pequena, o grupo de contatos pode mudar de linha. Presença no APPNORA aparece à direita; filtros por pesquisa/presença/cadastro e ordenação mantêm o conteúdo organizado, com rolagem vertical.

Pacientes, Fila, Vagas e a listagem legada operacional usam o mesmo template de item. Um clique seleciona e mostra ações em uma barra flutuante sobre a área inferior, sem reservar faixa vazia na página. Duplo clique rápido nativo ou Enter abre a ficha do item. A ficha usa raio de 18 px e grupos com raio de 12 px, título longo abreviado com tooltip e valor completo no corpo. Nome, e-mail, CPF e telefone disponível têm botão de copiar e feedback somente na ficha; a linha não permite cópia. Cores da célula, seleção e textos da ficha seguem os tokens do tema. Fila mostra a presença do paciente à direita; Vagas mostra Aberta/Oferta em andamento/Aceita, conforme confirmação do usuário.

Inputs Windows têm raio de 5 px, menor que os botões/cards. Tooltips têm borda de 6 px e fundo azul claro na aparência Clara, com equivalente legível na Escura. Substituem explicações permanentes da tela e identificam ações pelo mouse. A conta NORA fica no canto inferior da sidebar e abre somente Configurações e Sair da conta; o primeiro botão da barra de título alterna a sidebar.

As janelas OBS e Municipal têm barra de título própria (`WindowChrome`, 35 px, cor `TitleBarBrush` igual ao topo da barra lateral, ícones desenhados na cor do texto, hover `HoverBrush` e só o Fechar em vermelho), mas o Windows continua dono da moldura: arrastar, encaixar e cantos. Com `WindowStyle="None"` a janela maximizada é ajustada na resposta a `WM_GETMINMAXINFO` para ocupar exatamente a área de trabalho do monitor (a moldura padrão a faria vazar 9 px), e a região retangular que o `WindowChrome` aplica à janela restaurada é removida para o Windows 11 poder arredondar os cantos. Restauradas, as janelas solicitam cantos pequenos ao DWM. Maximizada/tela cheia solicita cantos retos. O raio final é definido pelo compositor Windows, não por recorte do conteúdo WPF. Conferência de preferência DWM e render do conteúdo são evidências distintas de captura da moldura na tela.

## Legibilidade e ícones do NORA Municipal

Os cartões do Municipal usam fundo e borda para delimitar o conteúdo. A sombra do estilo compartilhado fica desativada nesse perfil porque o efeito do WPF aplicado ao cartão inteiro também desativa o ClearType de seus textos. Assim, rótulos, títulos e controles dos cartões podem usar a renderização de texto do Windows da mesma forma que a barra lateral.

O menu lateral apresenta três desenhos vetoriais na cor dos rótulos de cada tema. Unidades usa o desenho de uma UBS, Especialidades usa um estetoscópio e Relatórios e metas usa uma folha com gráfico de barras.
