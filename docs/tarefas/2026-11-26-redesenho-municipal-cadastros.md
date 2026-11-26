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
- [x] Commit (feito ao fim, na branch do grupo: `56ae82e`)
- [x] Revisão crítica com correções, suíte completa e galeria de novo (ver "Revisão crítica" e "Estado")

## Estado

- 26/11/2026: fundação integrada por fast-forward; build com 0 erros; nove rotas redesenhadas; checagens adaptadas; galeria visual própria (`MunicipalCadastrosGallery.cs`, só com `--visual-gallery`) gera as capturas e foi conferida nos dois temas, no estado vazio e na largura 980.
- Validação: ver o resultado final no retorno do agente (build, contagem de PASS/FAIL da suíte completa).
- Pendências: P-120 (teste do usuário no programa real, ver abaixo).
- 26/11/2026, revisão crítica: dez achados corrigidos (seção "Revisão crítica"). Build da solução com 0 erros (2 avisos que já existiam, fora do grupo). Suíte completa do desktop **250 PASS e 1 FAIL**, o conhecido P-066 ("faixa de retorno … home"); são as 244 da linha de base mais as 6 checagens novas do grupo. Galeria regenerada (80 capturas `cad-*.png` em `.build/redesenho/municipal-cadastros/`, claro e escuro, com dados, vazio, balão, pop-ups, fichas e 980 px) e conferida a olho.
- [x] Commit da revisão na branch `worktree-wf_ca153789-8ce-15`.

## Revisão crítica (26/11/2026, branch `worktree-wf_ca153789-8ce-15`)

Revisão do trabalho acima (commit `56ae82e`) contra o pedido do grupo, os itens A e a especificação, frase por frase, com correção. O que ficou igual foi conferido no código e nas capturas: as nove rotas sem título repetido, sem card e sem Atualizar; listas abertas com etiqueta e balão; "+ Novo …" no cabeçalho; cadastros em pop-up com `FormGrid` e primário à direita; ficha em linhas por duplo clique, segundo clique e Enter; confirmações e mensagens de antes; seleção guardada pela chave no ciclo de 20 s.

Achados e correções:

1. **Ação confirmada e descartada em silêncio.** Os botões do balão e o "+ Novo …" do cabeçalho ficavam habilitados durante uma operação em andamento (antes, os botões da tela seguiam `CanOperate`). A pessoa confirmava, por exemplo, "Desativar" e nada acontecia, porque `RunAsync` ignora a ação com outra em curso. Agora as ações que gravam (Editar, Tentar criar a base de novo, Renomear, Reativar, Desativar, Encerrar, Redefinir senha provisória, Conceder e Retirar acesso, e os "+ Novo …") ficam desabilitadas enquanto `CanOperate` é falso; "Ver ficha" continua livre. Isso também impede o duplo clique de abrir dois pop-ups de conceder ou retirar acesso.
2. **Carregando sem indicador.** As listas mostravam só o texto "Carregando …". `ListEmptyText` passa a mostrar o passo com o indicador girando na cor da marca (`BrandBrush`), que para ao trocar de texto ou sair da tela.
3. **Pop-up aberto depois de a sessão cair.** Os pop-ups de cadastro (inclusive com a senha provisória digitada) continuavam abertos sobre a tela de acesso. `MunicipalDetailsHost.ShowForm` fecha o pop-up sem salvar quando a sessão termina, como a ficha já fazia.
4. **Duas fichas.** "Ver ficha" acionado duas vezes seguidas, com a leitura de quem leu ainda em curso, abria duas fichas. Agora abre uma por vez.
5. **"Nova conta" pelo menu só funcionava uma vez.** Fechado o pop-up, clicar de novo em "Nova conta" não abria outro; agora abre.
6. **"Retirar acesso" para conta sem UBS.** A lixeira aparecia para conta que comprovadamente não tem UBS; agora some nesse caso (enquanto as UBS não foram lidas, continua, e o pop-up avisa).
7. **Ficha com linha vazia.** Região, endereço e e-mail não informados apareciam como valor vazio; agora dizem "Não informado(a)".
8. **Instrução perdida.** O texto curto do cadastro antigo ("Conta só de acesso: nome, e-mail, perfil e senha provisória, sem CPF. Depois, conceda as UBS que ela operará.") voltou ao pop-up de conta nova.
9. **Código morto.** Saíram as propriedades das botoeiras antigas que nenhuma tela usava mais (`CanEditUnit`, `CanProvisionUnitDatabase`, `CanLoadStaffUnits`, `CanGrantStaffUnit`, `CanRevokeStaffUnit`, `CanChangeStaffActive`, `CanResetStaffPassword`, `StaffActiveActionLabel`, `SelectedStaffLabel`, `HasSelectedSpecialty`, `CanChangeSpecialty`, `CanSaveSpecialty`, `CanChangeMyAnnouncement`, `CanCloseMyAnnouncement`, `CanLoadAnnouncementReaders`) e os nomes delas nas listas de notificação de `WorkspaceViewModel.cs`.
10. **Checagem que travava a suíte.** A checagem nova das duas fichas repostava um observador em `ApplicationIdle` sem fim, e o `PumpUntil` nunca chegava ao fim; passou a usar um temporizador. A galeria usava o perfil inexistente `admin`; passou a `administrator`.

Checagens novas desta revisão (em `MunicipalCadastrosChecks.cs`, chamadas no fim de `RunCommunicationChecks`): rotas sem título, Atualizar nem card, com a lista ocupando a tela e o "+ Novo" no cabeçalho; rota "Nova conta" duas vezes; pop-up que fecha com a sessão e apaga a senha; uma ficha só com dois "Ver ficha"; ações desabilitadas durante uma operação e de volta no fim; lista carregando com indicador e vazio/busca só com texto.

Decisões mantidas do autor e conferidas: etiqueta "Base pronta"/"Base não criada" (texto que já existia); Cadastros e Agenda da UBS na mesma rota com alternador; linha de dados de cadastros de referência, agenda e acessos montada por conversor no XAML (os modelos `UnitPatientRecord` e `AppointmentInfo` são compartilhados com os grupos do OBS, e um `MetaLine` neles seria conflito certo na integração); a pré-visualização do comunicado deu lugar à confirmação de antes (o campo já mostra o texto puro como sai, e o pedido proíbe card dentro de pop-up).

Validação da revisão: ver "Estado".

## Testes sugeridos ao usuário

- **P-120** · 2026-11-26 · Abrir o Painel Municipal com a API real e passar por Unidades › Cadastro de UBS, Cadastros de referência, Mapa da rede, Especialidades › Catálogo, Comunicação e Usuários (Contas, Nova conta, Acessos às UBS, Histórico): selecionar um item (o balão aparece embaixo), abrir a ficha por duplo clique, criar e editar pelos pop-ups, desativar pela lixeira vermelha e conferir que nenhuma tela tem título repetido nem botão Atualizar. Defeito: balão que some no ciclo de 20 s, item que não abre a ficha, pop-up cortado ou texto ilegível no tema escuro.
