# Redesenho do Painel Municipal: cadastros e listagens — 2026-11-26

Parte do [redesenho dos desktops](2026-11-26-redesenho-desktops-dados-capturas.md) (item I1) feita pelo grupo `municipal-cadastros`, em paralelo com os outros grupos, sobre a fundação já pronta ([especificação](2026-11-26-redesenho-especificacao.md)). Branch deste grupo: `worktree-wf_ca153789-8ce-7`, criada de `origin/main` e avançada (fast-forward) até `claude/desktop-redesign-ui`.

## Skills e comandos invocados

- Pedido vindo do fluxo de redesenho (agente do grupo `municipal-cadastros`); sem comando do usuário nesta parte. Skills de referência lidas: `list-generator` e `selecao-listagem`.

## O que foi pedido (item por item)

Telas do grupo (rotas montadas em `MunicipalView.Render`): `units:registry`, `units:reference`, `network`, `specialties:catalog`, `communication`, `users:accounts`, `users:create`, `users:access`, `users:history`. Pedido do usuário aplicável: A1 a A20 e I1.

- **Cadastro de UBS**: `OpenList` na tela inteira (nome; `região | endereço`; etiqueta do estado da base). "+ Nova UBS" no cabeçalho abre pop-up com `FormGrid` (consulta de endereço preservada). Balão: Editar (pop-up) e "Tentar criar a base de novo" quando a base está pendente. Os passos de criar a base aparecem dentro do pop-up.
- **Cadastros de referência** (pacientes por UBS) e **agenda da UBS**: `OpenList`, filtros no cabeçalho e no botão Filtros, ficha em linhas; a auditoria da leitura nominal continua.
- **Mapa da rede**: sem título repetido, sem botão de atualizar, o mapa ocupa a tela.
- **Catálogo de especialidades**: `OpenList` (nome; etiqueta Ativa/Desativada). "+ Nova especialidade" abre pop-up. Balão: Renomear (pop-up), Desativar (lixeira, com a confirmação atual) ou Reativar. Erro de nome repetido dentro do pop-up.
- **Comunicação**: lista de comunicados (título; `público | alcance | publicação | validade`; etiqueta). "+ Novo comunicado" abre pop-up (título, texto, público, alcance com regiões/UBS, validade), primário "Publicar". Balão: encerrar (lixeira). A ficha mostra o texto completo; a lista de leituras segue o mesmo padrão.
- **Usuários**: Contas (nome; `e-mail | perfil | UBS com acesso`; etiqueta Ativa/Desativada/Senha provisória; balão com Redefinir senha provisória em pop-up e Desativar/Reativar); "+ Nova conta" na tela Contas abre pop-up (nome, e-mail, perfil, senha provisória; primário "Criar conta") e o item de menu "Nova conta" leva à tela Contas com o pop-up aberto, sem remover a rota; Acessos às UBS (contas com as UBS; conceder e retirar por pop-up e balão, retirar com lixeira); Histórico (lista só de leitura, filtros no botão Filtros, ficha em linhas).
- Em todas: sem títulos repetidos, sem botão de atualizar, sem card em volta de lista ou formulário.

## Objetivo

As nove rotas do grupo no padrão novo (cabeçalho com caminho, listagem aberta com balão de ações e ficha, cadastros em pop-up com `FormGrid`), sem mudar regra de negócio, chamadas de API, confirmações nem mensagens.

## Restrições

- Mexer só nas telas do grupo e nas checagens delas; não tocar em `Themes/*`, `ShellWindow*`, componentes compartilhados, `Nora.Api`, `APPNORA`, `scripts/`, `docs/` (exceto este arquivo). Pendências novas: P-120 a P-124.
- Sem `Co-Authored-By`; commit na branch do grupo; sem push.

## O que mudou em cada tela

- **Infraestrutura do grupo.** `FillStackPanel` (a pilha de `MunicipalView` passa a dar a altura que sobra ao filho com `PageLayout.Fill`, para a lista rolar sozinha e o balão ficar no pé da tela; sem Fill é um `StackPanel`), `ListEmptyText` (vazio "Nenhum … cadastrado." diferente de "Nenhum … encontrado para esta busca."), `MunicipalDetailsHost` (abre a `ItemDetailsWindow` das listagens da Secretaria por segundo clique, duplo clique e Enter; fecha sozinha se a sessão termina), conversores de lista (`ListMetaJoinConverter`, `ListBoolTextConverter`, `ListAppointmentToneConverter`), `ListText.Join` (linha de dados com " | " só entre os campos informados) e `WorkspaceViewModel.MunicipalDetails.cs` (fichas em linhas de UBS, cadastro de referência, consulta, especialidade, conta, histórico e comunicado, que lê quem da equipe leu sob demanda).
- **Cadastro de UBS** (`HealthUnitsView`, `UnitFormView`, `UnitProvisionFormView`): lista aberta; etiqueta "Base pronta"/"Base não criada"; "Nova UBS" no cabeçalho abre o pop-up (os passos de localizar o endereço e salvar aparecem nele); balão com Editar e "Tentar criar a base de novo" (só com a base pendente), cujos passos aparecem num pop-up.
- **Cadastros de referência e agenda** (`UnitRegistryView`): duas listas abertas alternadas por um `SegmentedToggle` Cadastros | Agenda no cabeçalho; a pesquisa vira o campo do cabeçalho; UBS, período, situação e os botões "Consultar …" ficam no botão Filtros (a leitura nominal só roda quando a pessoa consulta, como antes, e continua no histórico de acesso); balão com "Ver ficha".
- **Mapa da rede** (`NetworkMapView`): sem título, sem Atualizar; o seletor da UBS é filtro em linha no cabeçalho e "Mostrar no mapa" é a ação da tela; o mapa ocupa a altura toda e o estado fica numa linha embaixo.
- **Catálogo** (`SpecialtyCatalogView`, `SpecialtyFormView`): lista com etiqueta Ativa/Desativada; "Nova especialidade" e "Renomear" em pop-up (erro de nome repetido dentro dele); balão com Renomear, Reativar e Desativar (lixeira, mesma confirmação), com lote e uma só confirmação.
- **Comunicação** (`CommunicationView`, `AnnouncementFormView`): lista dos comunicados com etiqueta de situação; "Novo comunicado" abre o pop-up (pré-visualização trocada pela confirmação de antes); balão com Ver ficha e Encerrar (lixeira, lote com uma confirmação); "Carregar mais antigos" continua no pé.
- **Usuários**: Contas (`StaffAccountsView`, `StaffCreateFormView`, `StaffResetFormView`) com "Nova conta" no cabeçalho, balão Redefinir senha provisória / Reativar / Desativar; a rota `users:create` mostra a mesma tela e abre o pop-up de conta nova (a subtela `accounts,create`); `StaffCreateView` saiu. Acessos às UBS (`StaffAccessView`, `StaffUnitFormView`): balão Conceder acesso e Retirar acesso (lixeira) abrem pop-up com a UBS a escolher. Histórico (`HistoryView`): lista só de leitura, filtros no botão Filtros, ficha em linhas e "Carregar mais" no pé. A UBS com acesso de cada conta é lida a cada 5 minutos (no máximo 4 leituras ao mesmo tempo) para a linha de dados.
- **F7.** Saíram os botões de atualizar e os métodos de leitura manual (`LoadSpecialtyCatalogAsync`, `LoadMyAnnouncementsAsync`, `LoadStaffAsync`); as seções Especialidades, Comunicação, Usuários e Mapa releem sozinhas ao entrar, no ciclo de 20 s e depois de cada ação.

## Decisões onde a especificação não dizia

- Etiqueta do estado da base: reaproveita o texto que já existia ("Base pronta", "Base não criada"), em verde e amarelo.
- Cadastros de referência e agenda ficam na mesma rota, com o alternador no cabeçalho, em vez de duas rotas novas (o menu e as preferências salvas continuam valendo).
- Retirar acesso e conceder acesso abrem pop-up com a UBS a escolher (a lista de UBS da conta é lida ao abrir); a confirmação de antes continua, uma por ação.
- "Mostrar no mapa" ganhou largura mínima para não quebrar em duas linhas no cabeçalho.
- A explicação "Como ler a contagem" passou do rodapé da tela para a ficha do comunicado.

## Checklist

- [x] Fundação integrada (fast-forward) e build base
- [x] Infraestrutura da tela (painel que preenche, ficha do Painel, estado vazio, conversores de lista)
- [x] Cadastro de UBS · [x] Cadastros de referência e agenda · [x] Mapa da rede
- [x] Catálogo de especialidades · [x] Comunicação
- [x] Contas, Nova conta, Acessos às UBS, Histórico
- [x] Checagens adaptadas e novas (Secretaria*, Communication, EnterNavigation e as demais que tocavam essas telas)
- [x] Build 0 erros
- [x] Capturas nos dois temas, vazio, com balão, pop-up e ficha, conferidas (`.build/redesenho/municipal-cadastros/`, arquivos `cad-*.png`)
- [x] Loop de verificação (ver Estado)
- [ ] Commit (feito ao fim, na branch do grupo)

## Estado

- 26/11/2026: fundação integrada por fast-forward; build com 0 erros; nove rotas redesenhadas; checagens adaptadas; galeria visual própria (`MunicipalCadastrosGallery.cs`, só com `--visual-gallery`) gera as capturas e foi conferida nos dois temas, no estado vazio e na largura 980.
- Validação: ver o resultado final no retorno do agente (build, contagem de PASS/FAIL da suíte completa).
- Pendências: P-120 (teste do usuário no programa real, ver abaixo).

## Testes sugeridos ao usuário

- **P-120** · 2026-11-26 · Abrir o Painel Municipal com a API real e passar por Unidades › Cadastro de UBS, Cadastros de referência, Mapa da rede, Especialidades › Catálogo, Comunicação e Usuários (Contas, Nova conta, Acessos às UBS, Histórico): selecionar um item (o balão aparece embaixo), abrir a ficha por duplo clique, criar e editar pelos pop-ups, desativar pela lixeira vermelha e conferir que nenhuma tela tem título repetido nem botão Atualizar. Defeito: balão que some no ciclo de 20 s, item que não abre a ficha, pop-up cortado ou texto ilegível no tema escuro.
