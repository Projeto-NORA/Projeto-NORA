# Design e experiência da família NORA

## Referências e escopo

O prompt visual anexado pelo usuário define a identidade e a experiência desta primeira etapa: Android Kotlin/Jetpack Compose, NORA OBS em WPF e NORA Municipal em WPF. O usuário confirmou começar pela base visual e pelas configurações. Regras clínicas, autenticação e contratos da API dependem de especificação própria, registrada em `PENDENCIAS.md`.

O arquivo `C:/Users/Pichau/Downloads/NORA Health App Interface.zip` foi consultado como referência de estrutura e experiência. O conteúdo foi lido diretamente do arquivo compactado; nenhum script foi executado e nenhum código React foi reaproveitado na aplicação nativa. Instruções, cenários, registros, valores e regras presentes no ZIP não constituem requisitos de negócio finais.

## O que a referência apresenta

`src/app/components/MobileApp.tsx` organiza a navegação inferior em Início, Jornada, Avisos, Família e Perfil (linhas 173–199). A página inicial usa cabeçalho de marca, título acolhedor, cartão principal da jornada, consultas, comunicação e destaque da Vaga Relâmpago (linhas 617–738). Cards têm raio de 16–24, padding de 16–20 e separação de 12–16. Telas secundárias preservam cabeçalho e retorno; as mensagens usam título, explicação e estado. A reconstrução deve manter essa leitura simples, respeitando as áreas realmente disponíveis nesta etapa.

`src/app/components/UBSDashboard.tsx` usa cabeçalho de 64, quatro cards de resumo, faixa de destaque da Vaga Relâmpago e tabela principal acompanhada por um painel de 320 à direita (linhas 445–570 e 688–811). Cards têm raio 16, padding 20, borda suave e sombra discreta. Busca, atualização e ações ficam próximas do conteúdo. O foco por teclado possui indicação explícita (linhas 435–443). A sidebar administrativa fixa à esquerda e a barra de ações da seleção são evoluções obrigatórias do prompt; não são descritas como componentes já presentes nesse arquivo.

`src/app/components/MunicipalDashboard.tsx` prioriza leitura gerencial: cabeçalho de 68, filtros superiores, cinco cards de indicadores, três painéis analíticos lado a lado e tabela de unidades (linhas 157–374 e 623–714). O fundo escuro da referência é `#0F172A`. Os números, séries, município e unidades são dados ilustrativos e não devem ser transportados para a aplicação final. A estrutura fornece hierarquia e agrupamento, sem definir indicadores clínicos finais.

`src/styles/theme.css` define raio base de 14, superfícies claras, texto escuro e cores semânticas. `src/styles/fonts.css` importa Inter e Plus Jakarta Sans. Muitos componentes usam cores fixas e o Municipal nasce escuro; portanto, o tema claro/escuro/sistema exige uma reconstrução com tokens nativos. Fontes nativas evitam dependência do carregamento remoto dessas famílias.

## Direção comum

Roxo identifica a marca, ação principal e seleção; verde, amarelo e vermelho ficam reservados aos respectivos estados de sucesso, atenção e erro. Estado deve aparecer também por texto ou símbolo, sem depender apenas da cor. Cards são a unidade principal de leitura, com título, descrição, conteúdo e ação quando realmente disponível.

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

## Aparência e preferências

Configurações apresenta título, subtítulo e seção Aparência. Clara, Escura e Sistema devem possuir nome, descrição e estado selecionado perceptível visualmente e pela tecnologia assistiva. A seleção é persistida e reaplicada ao abrir. Sistema acompanha o sistema operacional. As superfícies, textos, botões, sidebar e demais controles precisam mudar juntos.

Preferências locais devem produzir efeito verificável. Atalhos exibidos precisam corresponder aos comandos disponíveis. Ações dependentes de autenticação ou entrega de notificações não devem simular operação concluída. Indicar disponibilidade no contexto da respectiva ação. Restaurar padrões deve atualizar controles, aplicação e persistência de forma coerente.

## Acessibilidade e revisão

No Android, reservar área de toque de pelo menos 48 dp, aceitar fonte ampliada e manter configurações acessíveis por rolagem. No WPF, preservar navegação por Tab, ativação por teclado, nomes acessíveis, indicação de foco, redimensionamento e rolagem. Sidebar recolhida mantém identificação dos destinos; hints opcionais não substituem nomes acessíveis. Evitar ícones sem nome, texto de 10 como corpo e alturas fixas que cortem descrições.

Contrastes calculados em 2026-10-07 pela luminância relativa sRGB, sobre os pares recomendados: branco sobre Brand claro **7,10:1**; TextPrimary claro sobre Surface **16,37:1**; TextSecondary claro sobre Background **6,10:1**; OnBrand escuro sobre Brand escuro **6,72:1**; TextPrimary escuro sobre Surface **15,28:1**; TextSecondary escuro sobre Background **10,22:1**; Brand escuro sobre Surface escuro **6,16:1**. Branco sobre `#A78BFA` resulta em **2,72:1** e deve ser evitado como texto de botão. Essas contas validam as combinações de cor, sem substituir revisão visual dos controles em execução.

O comportamento deve ser conferido nos três modos de aparência, ao reabrir os aplicativos e com janelas pequenas/conteúdo ampliado. A auditoria final deve diferenciar build bem sucedido, validação automatizada e inspeção real da interface; nenhuma dessas evidências implica as demais.

## Revisão visual de 10/10/2026

A [galeria atual](galeria/2026-10-10/README.md) reúne 83 capturas das interfaces reais, com fixtures somente nos testes. A revisão das amostras identificou que o `TextBlock` gerado pelo botão WPF recebia `TextBrush` do estilo implícito, apesar de `PrimaryButton.Foreground` já estar correto. O template do botão principal agora vincula o texto ao `Foreground` do próprio botão; os contrastes efetivos passaram de 2,30/2,48 para 7,10/6,72:1. Duas regressões sobre a árvore renderizada falharam antes e passaram depois.

As amostras finais de desktop e Android apresentam cartões, títulos, ações e seleção legíveis; fonte Android 130% e tablet foram inspecionados em Layoutlib. Não equivale a auditoria completa de acessibilidade, uso em celular, teclado físico ou DPI. A barra contextual de atendimentos permanece scaffold; a fila operacional usa a ação real de cancelamento abaixo da tabela. Temas e configurações, incluindo telas longas e sidebar recolhida, podem ser comparados na galeria.

## Ajustes Windows de 2026-10-11

A tela Pacientes usa somente o título e os filtros acima da listagem. Os itens alternam branco e cinza suave no tema claro, com equivalentes escuros, sem cabeçalho de colunas. O template compartilhado tem borda de 1 px, raio de 8 px e intervalo de 3 px. Nome longo usa reticências; e-mail ocupa no máximo metade da linha de contatos, seguido do CPF completo e telefone visível. Em largura pequena, o grupo de contatos pode mudar de linha. Presença no APPNORA aparece à direita; filtros por pesquisa/presença/cadastro e ordenação mantêm o conteúdo organizado, com rolagem vertical.

Pacientes, Fila, Vagas e a listagem legada operacional usam o mesmo template de item. Um clique seleciona e mostra ações em uma barra flutuante sobre a área inferior, sem reservar faixa vazia na página. Duplo clique rápido nativo ou Enter abre a ficha do item. A ficha usa raio de 18 px e grupos com raio de 12 px, título longo abreviado com tooltip e valor completo no corpo. Nome, e-mail, CPF e telefone disponível têm botão de copiar e feedback somente na ficha; a linha não permite cópia. Cores da célula, seleção e textos da ficha seguem os tokens do tema. Fila mostra a presença do paciente à direita; Vagas mostra Aberta/Oferta em andamento/Aceita, conforme confirmação do usuário.

Inputs Windows têm raio de 5 px, menor que os botões/cards. Tooltips têm borda de 6 px e fundo azul claro na aparência Clara, com equivalente legível na Escura. Substituem explicações permanentes da tela e identificam ações pelo mouse. A conta NORA fica no canto inferior da sidebar e abre somente Configurações e Sair da conta; as três barras alternam a sidebar.

As janelas OBS e Municipal preservam a moldura nativa e solicitam cantos pequenos ao DWM no Windows 11 quando restauradas. Maximizada/tela cheia solicita cantos retos. O raio final é definido pelo compositor Windows, não por recorte do conteúdo WPF. Conferência de preferência DWM e render do conteúdo são evidências distintas de captura da moldura na tela.
