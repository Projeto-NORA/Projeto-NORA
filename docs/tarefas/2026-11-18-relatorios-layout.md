# Relatórios de layout — app e os dois softwares desktop (2026-11-18)

## Skills e comandos invocados

- `/gerar-html` (skill de relatório HTML de revisão de telas).
- `/aura-retomar-execucao:aura` veio escrito dentro dos argumentos do `/gerar-html`. A skill só roda quando o
  usuário a chama diretamente (`disable-model-invocation`), então não foi carregada nesta tarefa; a revisão
  em volta completa da própria `gerar-html` continua valendo.

## O que foi pedido

1. "vou fazer isso depois": a hospedagem (migração no Neon, Brevo, Render, P-049) fica para depois.
2. "finalizar a parte de layout dos dois software desktop e do aplicativo".
3. "Quero que você faça um HTML para o aplicativo e o outro para os dois softwares": dois relatórios, um do
   app Android (APPNORA) e um que reúne o OBS-NORA e o Painel-Municipal.
4. "Use de exemplo o HTML que vc fez para o Project-Global-Vistos": o modelo é
   `Mateus-Projects/Claude-HTML/Project-Black-Gold-Vistos/Telas do painel 2026-11-05 (recomendações e modelos).html`
   (foto de cada tela nos dois temas, recomendações marcáveis C/V/F, observação por tela, cópia marcada).

## Objetivo

Dois HTMLs offline em `Mateus-Projects/Claude-HTML/Projeto-NORA/` em que o usuário marca, para cada tela atual,
os ajustes de layout que quer. As marcações viram a entrada da tarefa seguinte, que implementa o layout. A
skill `gerar-html` diz que as marcações não autorizam implementar sozinho.

## Restrições

- Fotos reais renderizadas pelo próprio código de produção (harness WPF e prévias Compose/Layoutlib), com
  dados sintéticos; nada de mockup apresentado como captura.
- Sem senha, token ou dado pessoal real nas imagens e no JSON.
- Não tocar na API da porta 62489 nem nos PostgreSQL 55441 e 55432.
- Não usar a placa de vídeo em trabalho pesado sem avisar o chat do Paulo (o emulador não é necessário aqui).
- HTML fora do repositório; o repositório guarda só fontes, documento da tarefa e links.

## Checklist

- [x] Capturas atuais do desktop (OBS e Secretaria/Painel-Municipal) nos temas claro e escuro: 186/186, 567 PNG.
- [x] Capturas atuais do app nos temas claro e escuro: 102 prévias, update e validate passaram.
- [x] Inventário de telas: app 35 telas (16 com comparação de estado); desktop 18 do OBS e 14 do Painel-Municipal
      (com comparações). Ficaram fora estados que só existem num tema (senha exigida, alertas com falha, menus) e o
      teclado/DPI físico (P-003, P-007).
- [x] Recomendações por tela: app 101 + 16 gerais; desktop 142 + 21 gerais; cada uma feita por um analista e
      conferida por um revisor adversarial (imagem e código); duas telas sem ajuste (APP-CONFIG-GOOGLE, OBS-BASE-INDISPONIVEL).
- [x] HTML do app: `Mateus-Projects/Claude-HTML/Projeto-NORA/Layout do app NORA 2026-11-18.html` (9,1 MB, 102 imagens).
- [x] HTML dos dois softwares: `Layout do desktop NORA 2026-11-18.html` (8,8 MB, 96 imagens).
- [x] `validar_relatorio.cjs`: 13/13 nos dois, na versão final (seleção, notas, cópia, download, offline, temas,
      imagens, 1366/900/390 px, zero recurso externo).
- [x] Índice `Claude-HTML/README.md`, seção nova em `docs/design.md` com os links, P-056 em `docs/PENDENCIAS.md`.

## Estado

- Desktop: `Nora.Desktop.Checks --visual-gallery --secretaria-gallery` com `--preview-dir .build/relatorio-layout-2026-11-18/desktop`
  passou 186/186 e gerou 567 PNG (OBS e Secretaria, claro e escuro). As páginas longas saem em partes de rolagem;
  o montador do scratchpad junta as partes de cada tela, recorta só a coluna de conteúdo (sem barra lateral, cabeçalho
  e barra de rolagem) e acrescenta só as linhas novas de cada parte. Quando duas partes vizinhas não se encaixam
  (algo além da rolagem mudou, ou as posições não se sobrepõem), a parte entra inteira depois de uma faixa cinza de
  6 px, e o relatório avisa. A janela inteira com a navegação aparece numa tela própria (OBS-JANELA, MUN-JANELA).
- App: as prévias Compose (`NoraVisualPreviews.kt`) tinham 55, a maioria num tema só. Foram acrescentados 47 pares
  de tema oposto (`NNp Par claro/escuro de NN`, mesmo corpo com `Appearance.LIGHT` e `DARK` trocados): 102 prévias,
  `updateDebugScreenshotTest` e `validateDebugScreenshotTest` passaram. Para o HTML as imagens perdem só as linhas
  finais de fundo liso do canvas e são reduzidas para 540 px de largura.
- Análise: workflow com um analista e um revisor adversarial por grupo de telas (app em 3 grupos; desktop em 4).
- Correções durante a revisão do próprio HTML: a foto do celular esticava até a largura do cartão (o CSS da skill usa
  `width:100%`), então cada tela de 540 px vai centrada numa moldura de 1100 px na cor do cartão; a paleta base da skill
  é verde, então os dois relatórios receberam todos os tokens do NORA (fundo, cartão, texto, linha, destaque); o motivo
  da OBS-FILA-C1 passou a dizer em que foto aparece "Cardiologia"/"cardiologia".
- Documentação: `docs/design.md` dizia que a marca dos desktops era roxa; o código usa azul-marinho desde 2026-11-15
  (`49a22d1`) e a tabela de tokens foi atualizada (a roxa ficou como histórico). Os links do `README.md`, de
  `docs/design.md` e de `docs/android-emulador.md` ainda apontavam para `Documentos/Claude-HTML` e passaram para
  `Mateus-Projects/Claude-HTML`.
- Dados de cada relatório (com `kind` e `evidence` de cada proposta) em `docs/tarefas/2026-11-18-relatorios-layout/`.
- Sem commit: aguardando o usuário. A implementação do layout é a próxima tarefa e depende das marcações (P-056).
