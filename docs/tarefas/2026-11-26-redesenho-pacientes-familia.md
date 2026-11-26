# Redesenho: Pacientes, Família e Vínculos aprovados (OBS) — 2026-11-26

Grupo "pacientes-familia" do redesenho dos desktops. Pedido geral em [2026-11-26-redesenho-desktops-dados-capturas.md](2026-11-26-redesenho-desktops-dados-capturas.md) (itens A, C1–C3 e D1–D3) e especificação em [2026-11-26-redesenho-especificacao.md](2026-11-26-redesenho-especificacao.md). Pendências reservadas a este grupo: P-095 a P-099.

## Skills e comandos invocados

- Tarefa delegada por fluxo de trabalho (um de oito agentes em paralelo, cada um num grupo de telas). Sem skill digitada pelo usuário nesta etapa; as skills `list-generator` e `selecao-listagem` foram lidas como referência.

## O que foi pedido, item por item

Todo o grupo vale para o OBS (o Painel Municipal não tem estas telas).

- **A (geral), nas minhas telas:** sem título repetido, sem banner nem texto de apresentação; sem card em volta de listagem e de formulário; listagem = `OpenList` na tela inteira, itens em duas linhas e `StatusTag` à direita; ações só no `SelectionBalloon` (perigo = lixeira branca em fundo vermelho, com a confirmação que já existe; ação que não serve a vários itens some com seleção múltipla; lote pede uma confirmação); duplo clique, segundo clique e Enter abrem a ficha em linhas; botões "+ Novo …" e alternadores no cabeçalho dentro de `PageActions`; filtros no botão Filtros; sem botão Atualizar (a tela relê ao entrar, no ciclo de 20 s e depois de cada ação); cores só por `DynamicResource`; aviso `ObsUnitNoticeView` mantido; estados vazio, carregando e erro; seleção e balão não piscam no ciclo de 20 s; tema escuro conferido; regra de negócio, API, confirmações, mensagens de erro e textos de acessibilidade preservados.
- **C1 Listagem:** sai o botão "Cadastrar paciente", o título e o botão de atualizar; `OpenList` na tela inteira. Filtros (busca, situação, presença, ordenação) no botão Filtros.
- **C2 Item:** nome; embaixo `CPF | e-mail | telefone`, só os informados; sem ícone de pessoa; `StatusTag` "Desativado" quando for o caso. Balão: Ver ficha, Editar ficha, Corrigir e-mail (vira pop-up com o campo e a confirmação que já existe), Desativar (lixeira, com a prévia de impacto e a confirmação atuais) ou Reativar. Duplo clique abre a ficha em linhas com a aba "Exames e vacinas".
- **C3 Cadastro:** sem título e sem card; "Cadastrar referência na UBS" em `FormGrid` (nome completo, CPF, e-mail, telefone lado a lado; endereço como seção opcional, também em `FormGrid`, com a consulta automática de CEP preservada); botão primário "Cadastrar paciente" embaixo à direita; Enter continua avançando de campo.
- **D1 Menu:** Pacientes = Listagem, Cadastro, Família e, abaixo de Família, o item novo "Vínculos aprovados"; rotas que não quebrem a rota salva nas preferências nem os atalhos; títulos "Família • Pacientes" e "Vínculos aprovados • Pacientes".
- **D2 Família:** uma `OpenList` com os pedidos pendentes (responsável → dependente no título; CPFs e data do pedido na linha de dados), do mais antigo ao mais novo; balão Aprovar e Recusar. Aprovar mantém a confirmação atual e mostra "Pedido aprovado.".
- **D3 Recusar:** pop-up (`ShowForm`) com "Motivo da recusa (opcional, até 280 caracteres)" (contador, sem quebra de linha), botões "Enviar" (primário, à direita) e "Fechar"; depois a faixa diz "Pedido recusado." ou "Pedido recusado e motivo enviado ao responsável."; o campo de motivo não aparece mais na tela.
- **Vínculos aprovados:** `OpenList` (responsável → dependente; CPFs; desde quando); balão com Encerrar vínculo (lixeira, com a confirmação atual).
- Some o segundo campo Pesquisar da Família: cada tela fica com uma lista e uma pesquisa.

## Objetivo

Pacientes (listagem e cadastro), Família e Vínculos aprovados do OBS no padrão novo do redesenho, sem perder nenhuma regra de negócio.

## Restrições

- Mexer só nos arquivos deste grupo (`ObsUnitRegistryView`, `FamilyView`, a parte de Pacientes do `WorkspaceView.xaml`, as partes de cadastro/família/ficha dos `WorkspaceViewModel.*` e, no `ShellViewModel`, só o menu de Pacientes) e nas checagens que cobrem essas telas.
- Não alterar temas, `ShellWindow*`, componentes da fundação, `Nora.Api`, `APPNORA`, `scripts/` nem outros docs (consolidação feita depois).
- Não tocar em `FormGrid.ColumnsFor`. Sem push, sem `git stash`, sem abrir os programas na tela do usuário.

## O que mudou em cada tela

### Pacientes • Listagem (`Views/ObsUnitRegistryView.xaml`, subtela `list`)

- `OpenList` na tela inteira (`PageLayout.Fill`), sem card, sem título, sem botão "Cadastrar paciente" e sem botão de atualizar. Item em duas linhas: nome (`ItemTitleText`) e `CPF | e-mail | telefone` (`ItemMetaText`, montado em `UnitPatientRecord.MetaLine`, só com o informado); `StatusTag` "Desativado" à direita (`StatusText`). O score deixou de aparecer na lista (continua na ficha e na ordenação "Maior/Menor score").
- Filtros (busca, situação, presença, ordenação) num `PageFilterGroup` do botão Filtros.
- Balão: **Ver ficha**, **Editar ficha** (leva ao Cadastro com a ficha aberta para edição), **Corrigir e-mail**, **Desativar** (`ListActionDangerButton`, com a prévia de quantas consultas futuras serão canceladas e a confirmação de antes) ou **Reativar** (um ou outro, conforme a situação do paciente selecionado). Todas valem para um item só e somem com seleção múltipla. Com a lista abaixo de 860 px os botões ficam só com o ícone (nome e dica continuam) para o balão caber na janela de 980.
- **Corrigir e-mail** abre pop-up (`ObsEmailCorrectionForm` em `DialogService.ShowForm`) com o nome, o CPF e o campo; a confirmação que já existia vem ao enviar, e o pop-up só fecha se a API aceitar (o erro fica dentro dele).
- Duplo clique, segundo clique e Enter abrem a ficha em linhas (aba "Exames e vacinas" incluída), pelo `ListingBehavior`.
- Estados: "Carregando os pacientes…" (indicador na cor da marca) até a primeira leitura; "Nenhum paciente cadastrado nesta UBS." (sem nenhum cadastro) diferente de "Nenhum paciente encontrado para esta busca."; sem UBS ativa só o aviso `ObsUnitNoticeView` (botão "Escolher UBS"). Seleção e balão sobrevivem à releitura de 20 s sem piscar (seleção por CPF).

### Pacientes • Cadastro (subtela `register`)

- Sem título da tela, sem card e sem texto de apresentação (a frase "Nome, CPF e e-mail são obrigatórios; o resto é opcional." saiu na revisão: os rótulos já dizem o que é opcional). Cabeçalho do formulário "Cadastrar referência na UBS" (ou "Editar cadastro da UBS" ao editar), nome completo, CPF, e-mail e telefone num `FormGrid`; "Endereço (opcional)" em outro `FormGrid` (CEP, logradouro, número, complemento, bairro, cidade, UF), com a consulta automática de CEP preservada (600 ms depois dos 8 dígitos).
- Botão primário **Cadastrar paciente** embaixo à direita ("Salvar alterações" ao editar; "Cancelar edição" ao lado só na edição). Enter continua avançando de campo.
- O painel colapsável do cadastro (`ObsRegistryDraftOpen`) saiu: a subtela é o próprio formulário.

### Menu de Pacientes e rotas (`ShellViewModel`, só o menu e as rotas de Família)

- Listagem (`patients:list`), Cadastro (`patients:register`), Família (`family:requests`) e **Vínculos aprovados** (`family:approved`, ícone de marca de verificação). Títulos "Família • Pacientes" e "Vínculos aprovados • Pacientes".
- Rotas antigas valem: o atalho `family` (e a página salva `family` nas preferências) abre Família (`family:requests`), para a página não empilhar as duas listagens.

### Família (subtela `requests`)

- Uma `OpenList` com os pedidos pendentes, do mais antigo ao mais novo: título "responsável → dependente", linha de dados "CPF do responsável … | CPF do dependente … | Pedido em dd/MM/aaaa HH:mm". Só uma pesquisa (no cabeçalho, `ItemsTarget`), sem o segundo campo Pesquisar e sem título de seção nem campo de motivo na tela.
- Balão: **Aprovar** (confirmação de antes, que lembra de conferir os documentos; depois "Pedido aprovado.") e **Recusar** (pop-up `FamilyRejectForm`: o pedido, "Motivo da recusa (opcional, até 280 caracteres)" sem quebra de linha, contador "n/280", **Enviar** à direita e **Fechar**). Faixa de retorno: "Pedido recusado." ou "Pedido recusado e motivo enviado ao responsável."; o pop-up só fecha se a API aceitar.
- Estados: indicador e "Carregando os pedidos…", "Nenhum pedido aguardando decisão nesta UBS.", "Nenhum pedido encontrado para esta busca." (pesquisa sem resultado), aviso de lista cortada e de leitura que falhou (agora dizendo que a tela tenta de novo sozinha, sem apontar um botão Atualizar que não existe).
- Duplo clique abre a ficha em linhas do pedido (responsável, CPF, dependente, CPF, data do pedido).

### Vínculos aprovados (subtela `approved`)

- `OpenList` com "responsável → dependente" e "CPF … | CPF … | Desde dd/MM/aaaa HH:mm" (data da aprovação). Balão: **Encerrar vínculo** (`ListActionDangerButton`, com a confirmação que explica o efeito). Mesma pesquisa, estados e ficha.

### Outras mudanças desta parte

- Saíram `LoadFamilyAsync`, `LoadObsRegistryAsync`, `FamilySummary`, `ObsRegistryActiveAction`, `HasSelectedObsRegistry` e `ObsRegistryDraftOpen` (sem uso depois do redesenho); a releitura é a automática (entrada na tela, ciclo de 20 s e depois de cada ação).
- Mensagens de erro da API que mandavam "Atualize a lista" (`family-link-closed`, pedido não encontrado, paciente sem cadastro ativo e prévia de desativação) passam a dizer que a lista se atualiza sozinha; a do score ausente na ficha idem.
- `WorkspaceView.xaml`: a raiz virou `Grid` (era `StackPanel`) para a listagem com `PageLayout.Fill` receber a altura da tela.

## Checklist

- [x] Fundação mesclada (fast-forward) e build inicial com 0 erros
- [x] Pacientes: Listagem (C1, C2)
- [x] Pacientes: Cadastro (C3)
- [x] Menu: Família e Vínculos aprovados (D1)
- [x] Família: lista, Aprovar, Recusar em pop-up (D2, D3)
- [x] Vínculos aprovados (D1)
- [x] Checagens adaptadas e novas; build com 0 erros; sem FAIL novo
- [x] Capturas nos dois temas (com dados, vazio, balão, pop-up, ficha) conferidas
- [x] Loop de verificação sem achados
- [x] Commit na branch

## Estado

- 26/11/2026: fundação mesclada em fast-forward; pedido, especificação, mapa, referência do AEV e `docs/design.md` lidos.
- 26/11/2026: telas implementadas (listagem, cadastro, Família, Vínculos aprovados), checagens adaptadas (`CommunityFamilyChecks`, `ListingChecks`, `MessageChecks`, `PatientInterfaceChecks`, `ReviewFixesChecks`, `ReviewLoop2Checks`, `UnitFlowChecks`, `UnitIndicatorsChecks`) e novas (`PatientsFamilyChecks.cs`: listagem, cadastro, Corrigir e-mail em pop-up, ficha em linhas, menu e rotas, Família com seleção no ciclo, pesquisa sem resultado e janela de 980).
- **Validação real:** `dotnet build NORA.slnx --configuration Release -m:1`: 0 erros. Checagens do desktop (suíte completa): **248 PASS e 1 FAIL**, o conhecido da linha de base (P-066, "faixa de retorno aparece em todas as abas … home", só falha na suíte completa); nenhum FAIL novo. Filtros `OBS Pacientes` (5 de 5) e `OBS Família` (2 de 2) passam sozinhos.
- **Capturas** (em `.build/redesenho/pacientes-familia/`, abertas e conferidas, claro e escuro, com dados, vazio, item selecionado com balão, pop-ups e ficha): `pacientes-listagem-claro.png`, `pacientes-listagem-selecionado-claro.png`, `pacientes-listagem-selecionado-escuro.png`, `pacientes-listagem-980-claro.png`, `pacientes-listagem-vazia-escuro.png`, `pacientes-cadastro-1280-claro-01.png`, `pacientes-cadastro-980-escuro-01.png`, `pacientes-corrigir-email-erro-claro.png`, `pacientes-ficha-claro.png`, `familia-selecionado-claro.png`, `familia-selecionado-escuro.png`, `familia-980-selecionado-claro.png`, `familia-ficha-claro.png`, `obs-familia-recusar-light.png`, `obs-vinculos-balao-light.png`.

## Decisões onde a especificação não dizia

- O cabeçalho "Cadastrar referência na UBS" fica no formulário (o pedido C3 manda essa seção na grade); sai só o título da tela.
- "Editar ficha" continua levando ao Cadastro (a edição é o formulário em grade), com "Cancelar edição" para voltar a um cadastro novo.
- Com a janela estreita (lista abaixo de 860 px) o balão de Pacientes mostra só os ícones, mantendo nome de automação e dica.
- Família e Vínculos aprovados guardam a rota salva `family` como Família (pedidos); a rota `family:approved` só vem do menu.
- A ficha em linhas também abre para pedidos e vínculos de Família (duplo clique), por coerência com as outras listagens.
- Recusar: a pergunta "Recusar pedido de família" que vinha depois do clique foi substituída pelo próprio pop-up (abrir "Recusar" e apertar "Enviar" já são os dois passos da confirmação, e o pop-up repete o pedido e que o responsável poderá pedir de novo). Aprovar, Desativar, Reativar, Corrigir e-mail e Encerrar vínculo mantêm a pergunta de antes.
- A rota salva `patients` (sem subtela) continua abrindo as duas subtelas empilhadas, como a fundação faz com toda página de subtelas aberta sem subtela; só `family` foi ligada a uma subtela, porque as duas listagens da Família ficam na mesma célula.

## Revisão (26/11/2026)

Revisão crítica do grupo contra o pedido (itens A, C1–C3, D1–D3), a especificação e o bloco de regras das telas, com correção na mesma branch. Achados e correções:

1. **Texto de apresentação no Cadastro:** a frase "Nome, CPF e e-mail são obrigatórios; o resto é opcional." ficava sob o cabeçalho do formulário (A1 proíbe texto de apresentação). Saiu; os rótulos já dizem o que é opcional.
2. **Corrigir e-mail reabria com o texto digitado antes:** fechar o pop-up sem corrigir e abri-lo de novo trazia o que tinha sido digitado, não o e-mail do cadastro. Agora o pop-up abre sempre com o e-mail atual (checagem nova).
3. **Trocar de UBS com Família aberta:** sem o botão Atualizar, os pedidos da UBS nova só apareciam no ciclo de 20 s. A troca de UBS passa a ler também os pedidos e vínculos quando a página aberta é Família ou Vínculos aprovados (leitura tolerante, não derruba a troca); com a UBS escolhida, o estado das duas listas passa de vazio a "Carregando…" na hora (checagem nova).
4. **Aviso de lista cortada da Família** mandava "Decida estes e os seguintes aparecem em seguida"; reescrito para "Conforme estes forem decididos, os próximos aparecem sozinhos.".
5. **Indicador de carregamento da Família** ocupava a largura toda; ficou com 220 px à esquerda, igual ao de Pacientes.
6. **Ficha do paciente:** o CPF estava no grupo "Contato"; passou para "Identificação".
7. **Checagem da rota salva** deixava o tema da aplicação no escuro para as capturas seguintes (a janela reaberta aplica o tema dela); agora a janela da checagem volta ao claro.
8. **Conflitos na integração:** `WorkspaceView.xaml` ficou idêntico ao da branch de Vaga Relâmpago (mesma troca de `StackPanel` por `Grid`, mesmo comentário) e a linha de `ResetUnitState` que a branch de Informações da UBS também muda ficou intocada (o "voltar a Carregando" foi para uma linha própria), para a junção não conflitar.
9. **Capturas que faltavam:** Vínculos aprovados com dados e com o balão (lixeira) nos dois temas, pop-up Recusar nos dois temas e Família sem pedidos nos dois temas (checagem estendida, também confere que Fechar não recusa).

Conferido sem achado: listagem aberta na tela inteira com uma rolagem só; balão por cima da lista sem reservar espaço; seleção e balão sem piscar no ciclo (checagem); Desativar/Reativar e Encerrar vínculo com a lixeira e as confirmações de antes; ações de item único somem com seleção múltipla; Recusar e Corrigir e-mail mostram o erro dentro do pop-up e só fecham com a API aceitando; rota salva e atalho `family` abrem Família; nomes de automação mantidos; sem botão Atualizar nem código morto dele; cores só por chave do tema.

Validação da revisão: build com 0 erros (2 avisos de nulabilidade já existentes, fora do grupo); filtros `OBS Família` (2 de 2) e `OBS Pacientes` (5 de 5) passam; suíte completa do desktop com **248 PASS e 1 FAIL**, o conhecido da linha de base (P-066, "faixa de retorno … home"); nenhum FAIL novo. Capturas regeneradas em `.build/redesenho/pacientes-familia/` e conferidas (claro e escuro, com dados, vazio, balão, pop-ups, ficha e 980 px).

## Pendências

- Sem pendência nova aberta por esta parte (P-095 a P-099 não foram usados). Mensagens de erro da API de outras telas que ainda mandam "Atualize a lista" (comunicados, computadores, especialidades, metas, exames e vacinas, profissionais) são de outros grupos e ficam para a consolidação.
- Teste manual na tela real (não feito pela IA): conferir Pacientes e Família no OBS aberto, com seleção múltipla (Ctrl e Shift) e o balão em janela de 980 px.
