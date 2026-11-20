# Cronograma de 3 dias: hospedagem, layout e testes (2026-11-19)

## Skills e comandos invocados

- `/aura-retomar-execucao:aura`, que carrega `protocolo-tarefa-complexa`, `diagnosing-bugs` e `modo-loop-verificacao`
  (lidas por inteiro) e pede a `graphify` nas tarefas de código.

## O que foi pedido

1. "me envie todas as pendencias em aberto desse chat".
2. "quero finalizar toda a conexão com o Render e o Neon".
3. "depois o layout de todas as paginas".
4. "e por fim fazer os testes".
5. "tudo isso em 3 dias, entao quero que vc finalize tudo pra mim".
6. "se por acaso vc nao conseguir alguma coisa em especificio me envie o passo a passo da resolução desse problema".
7. "faça um cronograma pra mim de trabalho durante esses dias, 3h de trabalho em cada dia".
8. "Faça um HTML contendo o passo a passo com esse cronograma. Deve ser algo bem explicativo mas o menor possivel e o
   mais rapido de leitura possivel".

Respostas do usuário em 19/11:

- **Layout:** fazer todos os ajustes marcados como "Recomendo" nos dois relatórios de 18/11. A Luana também está mexendo
  nas telas. Na junção, o que ela mexeu fica por cima: onde as duas mudanças não se encostam, as duas ficam; onde
  conflitam, vale a dela.
- **Neon:** o `--migrate` e os dois `--provision` ainda não rodaram.
- **Início:** os 3 dias são 20, 21 e 22/11, com 3 h por dia.

### Pedido seguinte (19/11, na fila depois do layout)

9. Depois de implementar todas as recomendações, atualizar os dois HTMLs de layout (os de `Downloads`, editados no
   mesmo caminho). Cada tela atual aparece em comparação com a antiga, com recomendações novas (C/V/F) feitas sobre as
   telas novas. Cada tela tem um botão "voltar para a antiga": marcado, as marcações e a observação daquela tela valem
   para a antiga; sem ele, valem para a nova. Isso vale para a lista e para as instruções. São as telas dos dois
   softwares e do app.
10. Estender a skill `gerar-html` com esse recurso, aplicá-lo nos dois HTMLs e fazer commit e push no repositório
    Claude (autorizado).
11. Tirar cerca de 10 % do texto das telas: textos soltos ou avulsos que dá para retirar e simplificar. O critério do
    que é importante fica com a IA, que pergunta se não tiver noção.

## Objetivo

O usuário termina em 3 dias, com 3 h por dia, a hospedagem (Neon + Brevo + Render), a revisão do layout e os testes. A
IA faz agora tudo o que não depende de credencial, conta ou mão humana (o layout recomendado e os testes automáticos) e
entrega um HTML curto com o cronograma e o passo a passo do resto.

## Restrições

- A IA não digita senha, chave nem conexão com senha. Também não cria conta: senha e código ficam com o usuário.
- Nada de segredo no Git nem no HTML.
- Não tocar na API da porta 62489 nem nos PostgreSQL 55441 e 55432. Não usar `git stash` puro.
- Commit e push pela regra de 18/11 (com certeza, a IA faz; sem certeza, avisa), sem `Co-Authored-By` de IA. Merge e push na `main` só com autorização.
- Avisar o chat do Paulo antes de usar a placa de vídeo em algo pesado (o emulador não é usado nesta tarefa).
- Junção com a Luana: o lado dela vence os conflitos.

## Checklist

- [x] Lista das pendências abertas enviada (P-042, P-049, P-056, P-003, P-007 e a seção de integração).
- [x] Hospedagem: passo a passo de Neon (`--migrate`, `--provision` x2), Brevo, Render (Blueprint) e conferências da P-049.
- [x] Layout: 70 ajustes recomendados do app e 113 dos desktops implementados, com build e testes verdes (exceto MUN-JANELA-C2, decisão do usuário, P-058).
- [x] Testes automáticos completos depois do layout (API, desktop, desktop com banco, Android local, prévias).
- [x] Passo a passo da junção com a Luana (o lado dela vence).
- [x] HTML com o cronograma de 20 a 22/11 (3 h por dia), validado offline em celular e desktop.
- [x] Skill `gerar-html` com `previous` ("Voltar para a tela antiga") e `comparisons`, publicada (Claude `b8e2dd0`).
- [x] Cerca de 10 % menos texto nas telas do app e dos desktops, medido antes e depois.
- [x] Capturas novas, recomendações novas sobre as telas novas e os dois HTMLs de `Downloads` atualizados (antes e
      depois, com a volta à tela antiga), validados.
- [x] Documentação e `docs/PENDENCIAS.md` atualizadas.

## Estado

- 19/11: as skills foram lidas, junto com as pendências, o guia `docs/hospedagem.md` e o histórico da etapa do Neon.
  No Neon já existem a conta própria do NORA e o projeto `nameless-dawn-60085750` (PostgreSQL 18, AWS US East 1), com a
  conexão direta. Nenhum comando rodou contra ele.
- Cronograma escrito: plano em `Mateus-Projects/Claude-PLANS/Projeto-NORA/cronograma-3-dias.md`, versão visual em
  `Mateus-Projects/Claude-HTML/Projeto-NORA/Cronograma NORA 3 dias.html` (índices atualizados). Conferido com
  Playwright e Edge, offline, em 390 px claro e 1366 px escuro: sem rolagem horizontal, 13 passos com marcação
  salva depois de recarregar, 9 botões de copiar, tema alternável, zero recurso externo e zero erro no console.
- Layout: os 183 ajustes recomendados (70 do app e 113 dos desktops) foram divididos em grupos de arquivos disjuntos
  (`grupos.py` no scratchpad). O workflow `nora-layout-recomendados` faz o resto: implementa em paralelo; depois, para
  cada plataforma, integra os ajustes gerais, compila e testa; por fim, um revisor adversarial roda em voltas até não
  achar nada.
- 19/11, resultado do `nora-layout-recomendados` (25 agentes): 101 arquivos alterados. O app ficou verde em build,
  testes unitários, lint, `assembleDebugAndroidTest` e prévias. Os desktops ficaram verdes no build (0 erros, 0 avisos)
  e em `Nora.Desktop.Checks` (181/181). Foram 4 voltas de revisão por plataforma, todas com achados corrigidos, e a
  última correção ainda não foi revisada. Ficaram de fora 11 itens do desktop, porque pediam arquivos fora do grupo ou
  estado novo no ViewModel: MUN-USUARIOS-V3, OBS-ALERTAS-F1, MUN-MAPA-V1, MUN-VISAO-GERAL-C2, MUN-UNIDADES-V2,
  MUN-ESPECIALIDADES-V1, MUN-CAPACIDADE-F1, OBS-FILA-F1, as frases de vazio da Capacidade, MUN-RELATORIOS-C1 e
  MUN-JANELA-C1 (P-057). MUN-JANELA-C2 (recolher a barra lateral) é decisão do usuário. A GERAL-V6 do app pede
  conferência em aparelho real.
- Texto visível medido depois do layout (`contar_texto.py`): desktop 12.408, app 4.092, total 16.500 palavras. A base
  de 26c56d7 era 16.200. A meta do corte é ficar em torno de 14.850.
- Workflow `nora-texto-e-pendentes-layout` (run `wf_69e83eb2-6a4`): faz os 11 itens restantes do desktop, corta o
  texto em 7 grupos de arquivos disjuntos (2 do app e 5 do desktop), integra e testa cada plataforma e revisa em até 5
  voltas, até uma volta sem achados.
- 20/11, resultado do `nora-texto-e-pendentes-layout` (30 agentes):
  - Os 11 itens do desktop foram feitos; MUN-RELATORIOS-C1 e MUN-JANELA-C1 também, então a P-057 pode ser fechada.
  - Texto visível: desktop 10.787, app 3.603, total 14.390 palavras. São 12,8 % a menos que depois do layout e 11,2 %
    a menos que a base.
  - App verde em build, 299 testes unitários, lint e prévias. Desktops verdes em build (0 erros, 0 avisos) e em 181/181.
  - Foram 5 voltas de revisão por plataforma, com achados caindo de bloqueio para sugestão. Nenhuma volta terminou limpa.
  - Docs a atualizar, apontadas pelos agentes: `docs/configuracoes.md:32` e `docs/app-paciente.md` (linhas 37, 77, 95
    e 105).
- Revisão de fechamento `nora-revisao-fechamento-layout` (run `wf_1321dd8d-c40`): só bloqueio ou risco confirmado trava
  a entrega. Cada achado é conferido por um segundo revisor antes de corrigir, e as sugestões ficam registradas.
- 20/11, a revisão de fechamento ficou limpa nas duas plataformas na volta 2. Na volta 1 houve um risco confirmado e
  corrigido em cada uma: no app, Atendimento e Jornada mostravam erro durante a releitura; no desktop, a falha dos
  profissionais aparecia na caixa de especialidades da fila.
- Defeito antigo das verificações, achado e corrigido:
  - "faixa de retorno … em todas as abas" falhava conforme a ordem das verificações, inclusive em `26c56d7`
    (reproduzido num worktree temporário).
  - A causa: `TextBlock.Text` vem vazio para conteúdo feito de `Run`. A verificação passou a usar `FullTexts`.
  - `--check-filter` aceita `a|b` para isolar dependências de ordem.
- Suíte completa (20/11):
  - API com PostgreSQL descartável (porta 55439): 272 verificações somadas no log, sem falha, saída 0.
  - Clientes e desktop com banco (porta 55440): 185/185.
  - Desktop sem banco: 182/182.
- `docs/PENDENCIAS.md`: P-056 e P-057 resolvidas; P-058 (barra lateral, decisão do usuário) e P-059 (problemas
  anteriores) abertas. A seção "Testes sugeridos ao usuário" foi criada com a P-003, a P-007 e a P-060 (borda dupla
  no celular).
- Workflow `nora-sugestoes-fechamento` (`wf_25a640c0-753`): corrige as sugestões da revisão no código novo e três
  itens antigos da P-059, e depois revisa.
- 20/11, resultado das sugestões:
  - 8 itens do app e 6 do desktop feitos. Revisão limpa na volta 2 (app) e na volta 1 (desktop); no app, 2 riscos
    confirmados na volta 1 foram corrigidos.
  - Validação: app com 302 testes unitários, lint e prévias verdes; desktop 184/184.
  - Da P-059 sobrou só a diferença de regra da API (especialidade livre). Os outros três itens foram corrigidos:
    `VisualCaptureTest`, score para o leitor de tela e carregamento da Vaga Relâmpago.
  - Ajuste meu depois disso: "Atualizar unidades" e "Atualizar" ficam desligados durante a leitura das UBS, porque
    `loadUnits` não faz nada nesse momento. Validado: app verde.
- Fotos de 20/11 em `.build/relatorio-layout-2026-11-20/`:
  - Desktop pelo harness (189/189): a lista de partes foi refeita a partir dos arquivos reais (10 telas mudaram de
    tamanho). Seis têm emenda declarada.
  - App: as 102 prévias atuais.
- Análise nova (`wf_2097aabe-f5e`), com analista e revisor adversarial em 8 grupos:
  - App: 38 itens (35 recomendados, 6 C), mais 1 geral.
  - Desktop: 60 itens (55 recomendados, 14 C), mais 1 geral.
  - Os códigos continuam a numeração de 18/11.
- HTMLs de antes e depois gerados pela skill `gerar-html` (`previous` com `<CODE>-ANTIGA` e as fotos de 18/11):
  - Validados com 14/14 cada. Conferi à mão a caixa de volta marcada (`volta-app.png`).
  - Gravados em `Mateus-Projects/Claude-HTML/Projeto-NORA/Layout do {app,desktop} NORA 2026-11-20.html` e por cima de
    `Downloads/Layout do {app,desktop} NORA 2026-11-18.html`. As versões de 18/11 ficam guardadas em Claude-HTML.
  - O índice foi atualizado.
- Cronograma: a seção "O que a IA mudou" foi preenchida no HTML e no plano. Validado de novo (390 e 1366 px, 13
  passos, 9 botões de copiar, 0 externo, 0 erro).
- Docs por assunto atualizadas: `app-paciente.md`, `configuracoes.md`, `design.md`, `obs-painel-operacional.md`,
  `secretaria.md` e `execucao.md`.
- Suíte com banco repetida depois das correções finais: desktop com banco 187/187 (as duas a mais são as verificações novas da fila), saída 0.
