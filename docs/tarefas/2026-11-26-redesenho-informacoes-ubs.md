# Redesenho de Informações da UBS (OBS) e remoção de Computadores — 2026-11-26

Parte do [redesenho dos desktops](2026-11-26-redesenho-desktops-dados-capturas.md) (itens H1 a H6 e os itens A que valem para todas as telas). Como fazer: [especificação](2026-11-26-redesenho-especificacao.md), seção "Informações da UBS". Grupo de trabalho: `informacoes-ubs`; números de pendência reservados: P-110 a P-114.

## Skills e comandos invocados

- Nenhum comando do usuário nesta parte: o trabalho foi dado por um fluxo de agentes. Foram lidas as skills `list-generator` e `selecao-listagem` como referência dos padrões de listagem.

## O que foi pedido, item por item

- **H1 Atendimento:** sem card e sem o título "Atendimento ao público"; no topo, enquanto o horário não foi informado, um aviso amarelo (o que hoje diz que o aplicativo mostra "Horário não informado"); uma linha por dia com caixa de seleção e o nome do dia; marcada = aberto, com dois campos HH:mm e a palavra "às" entre eles (campos cerca de 1,2 vez mais baixos); desmarcada = fechado, sem campos e sem a palavra "Fechado"; um intervalo só por dia (dia com dois intervalos gravados mostra o primeiro início e o último fim, com uma linha discreta avisando que o do meio será desconsiderado ao salvar); "Serviços oferecidos" com contador continua; some "Especialidades com horário na grade"; **Salvar** embaixo à direita; atendente continua em leitura; validações e mensagens continuam.
- **H2 Profissionais:** listagem aberta na tela inteira (nome; especialidades e registro no conselho quando houver; etiqueta Ativo/Desativado); **+ Novo profissional** no cabeçalho abre pop-up (nome, especialidades em caixas, registro no conselho numa linha só com sigla, número e UF e uma dica do que é; sem o aviso "da lista da rede"); balão com Editar e Desativar (lixeira) ou Reativar; regras e mensagens de 409 preservadas, dentro do pop-up.
- **H3 Consultórios:** igual a Profissionais (nome, etiqueta, **+ Novo consultório**, Editar (renomear), Desativar ou Reativar).
- **H4 Grade semanal:** sete botões de dia (segunda a domingo) num alternador, segunda selecionada ao abrir; abaixo, a listagem do dia (título "08:00 às 12:00 · atendimentos de 20 min"; dados "profissional | especialidade | consultório | vigência"); **+ Novo horário** abre pop-up já com o dia escolhido e **Salvar horário na grade** à direita, sem card; balão com Editar e Remover (lixeira).
- **H5 Bloqueios:** sem o título de baixo; listagem padrão (motivo; "profissional ou UBS inteira | início | fim"); **+ Novo bloqueio** abre pop-up com **Cadastrar bloqueio** à direita; balão com Remover (lixeira).
- **H6 Computadores:** a subtela é excluída (menu, tela e o código de interface que só ela usa); a lógica de login e de computador ativado continua.
- Itens A válidos para estas telas: sem título repetido nem banner; sem card em listagem ou formulário; ações só no balão (remover/desativar sempre lixeira vermelha); ficha em linhas no segundo clique/Enter; botões novos no cabeçalho à direita; pesquisa como campo com lupa; sem botão Atualizar (a tela relê sozinha); estados vazio, carregando e erro; tema escuro; regra de negócio e mensagens preservadas.

## Objetivo

As cinco subtelas de Informações da UBS no padrão novo do OBS (listagens abertas, balão de ações, cadastros em pop-up com a grade de campos, ficha em linhas) e a subtela Computadores fora do programa, sem perder a lógica de login e de computador ativado.

## Restrições

- Mexer só nas telas deste grupo e nas checagens delas; não alterar temas, janela principal, componentes compartilhados, API, app, scripts nem outros documentos; `FormGrid.ColumnsFor` fica como está.
- Nada de push nem de mexer em outras branches.

## Checklist

- [x] Fundação mesclada e solução compilando
- [x] Computadores removido (tela, menu, código de interface, checagens)
- [x] Atendimento (H1)
- [x] Profissionais (H2)
- [x] Consultórios (H3)
- [x] Grade semanal (H4)
- [x] Bloqueios (H5)
- [x] Checagens adaptadas e novas; sem FAIL novo
- [x] Capturas claro e escuro, com dados, vazio, balão, pop-up e ficha
- [x] Loop de verificação sem achados

## O que mudou em cada tela

**Atendimento** (`UnitSettingsView.xaml`, `OpeningDayEditor` em `WorkspaceViewModel.UnitProfile.cs`). Sem card, sem título, sem o botão de atualizar e sem a lista "Especialidades com horário na grade". Aviso amarelo (`NoticeItem`) no topo enquanto o horário não foi informado ("Ainda não informado. O aplicativo mostra "Horário não informado"."). Sete linhas (domingo a sábado, a ordem da API): caixa de seleção à esquerda do nome do dia (estilo local `LeadingCheckBox`, quadrado à esquerda); marcada = aberto, com os campos `Abre` **às** `Fecha` (estilo `TimeBox`, 8,6 de preenchimento: cerca de 1,2 vez mais baixos que o campo padrão, conferido pela checagem); desmarcada = fechado, sem campos e sem a palavra "Fechado". Um intervalo por dia: `OpeningDayEditor` perdeu o segundo intervalo; ao carregar um dia com dois intervalos mostra o primeiro início e o último fim e uma linha discreta diz "Havia dois intervalos gravados neste dia; ao salvar, o intervalo do meio será desconsiderado.". Validações e mensagens iguais (HH:mm, fechar depois de abrir, dia marcado sem horário, serviços até 1.000 caracteres). "Serviços oferecidos" com contador continua; **Salvar** embaixo à direita; a leitura automática não apaga uma edição em andamento; o atendente vê tudo desabilitado com o aviso de leitura.

**Profissionais / Consultórios** (`OpenList` na tela inteira com `PageLayout.Fill`). Item de profissional: nome; `especialidades | CRM 12345/SP` só com o que existe; `StatusTag` Ativo (verde) ou Desativado (neutro). Item de consultório: nome e etiqueta. `Novo profissional` e `Novo consultório` (`NewItemButton` em `PageActions`) abrem `DialogService.ShowForm` com `FormGrid`. Pop-up do profissional: nome; "Registro no conselho (opcional)" numa linha com sigla, número e UF (legendas embaixo) e a dica curta do que é o conselho profissional ("o órgão que registra a profissão, como o CRM (medicina) ou o COREN (enfermagem)"); especialidades em caixas de seleção, sem o aviso da lista da rede. Balão: Editar (abre o mesmo pop-up preenchido; no consultório o pop-up se chama "Editar consultório" e só renomeia), Desativar (lixeira vermelha, com a confirmação de antes, uma só para o lote) ou Reativar (só aparece quando todos os selecionados estão desativados; Desativar só quando todos estão ativos). Regras preservadas: nome de 2 a 160, conselho todo ou nada, UF válida, ao menos uma especialidade; o 409 de nome repetido aparece dentro do pop-up, que continua aberto; a especialidade desativada já vinculada continua marcada na edição.

**Grade semanal.** `SegmentedToggle` com Segunda a Domingo (segunda ao abrir; `ScheduleDayOptions`, o número do dia é o da API) e, abaixo, a `OpenList` do dia (`ScheduleDayRows`): título "08:00 às 12:00 · atendimentos de 20 min"; dados `profissional | especialidade | consultório | vigência` (a vigência só quando há limite). `Novo horário` abre o pop-up já com o dia escolhido e **Salvar horário na grade** à direita, sem card; o aviso de sobreposição (409) e o de profissional ou consultório desativado por outra pessoa aparecem dentro do pop-up. Balão: Editar e Remover (lixeira, com confirmação, uma para o lote). A pesquisa do cabeçalho (que já existia) filtra o dia escolhido.

**Bloqueios.** Sem o título de baixo. `OpenList`: motivo; `UBS inteira ou profissional | início | fim` (dia inteiro mostra só as datas, e um dia só vira "01/12/2026 (dia inteiro)"). `Novo bloqueio` abre o pop-up com **Cadastrar bloqueio** à direita. Balão: Remover (lixeira, com confirmação).

**Em todas.** Duplo clique, segundo clique e Enter abrem a ficha em linhas (`ItemDetailsWindow`; `BuildCatalogDetails`). Estados: "Nenhum … cadastrado." diferente de "Nenhum … encontrado para esta busca." (`EmptySearchVisibilityConverter`), carregando e falha com a frase "A tela tenta de novo sozinha." (sem botão Atualizar: `unit-settings` relê o cadastro, a grade, os bloqueios e o atendimento ao entrar e a cada ciclo de 20 s). O aviso `ObsUnitNoticeView` continua no alto; o aviso de somente leitura do atendente vale para as quatro listagens.

**Computadores removido.** Saíram: o item "Computadores" do menu (`ShellViewModel`, só o menu de Informações da UBS), a subtela e a rota `devices` (a rota antiga cai na página inteira, como uma rota desconhecida), as propriedades e os comandos de tela de `WorkspaceViewModel.Devices.cs` (lista, rascunho, código emitido, ativar este computador, revogar), `UnitScope.Devices` e a leitura periódica dos computadores, e o parâmetro `IClipboardService` do construtor do `WorkspaceViewModel` (só servia para copiar o código). **Continuam intactos:** `ObsDeviceStore`, o cabeçalho `X-Nora-Device`, `session.dat`, `RestoreDesktopAccessAsync`, `HasDeviceSupport`, `DeviceActivated`, a ativação por código na tela de acesso e a revogação detectada ao reabrir, com as checagens deles.

## Decisões onde a especificação não dizia

- **Subtelas com listagem na tela inteira.** `UnitSettingsView` tem várias subtelas e o aviso de UBS/leitura no alto; para a listagem ocupar o resto da altura e o balão ficar no pé da tela, criei o painel `SubPageStack` (uma subtela visível recebe a altura toda; várias visíveis, como numa página aberta sem subtela escolhida, ficam empilhadas e a página rola). Os `PageLayout.Fill` estão nas quatro subtelas de listagem; o Atendimento rola (é formulário).
- **Ordem dos dias.** O Atendimento mantém domingo a sábado (a ordem da API e de antes); a Grade semanal começa na segunda e termina no domingo, como o pedido (P-113 pergunta se o Atendimento deve seguir a mesma ordem).
- **Ficha.** Profissional e consultório mostram "Situação" no mesmo grupo "Identificação" (um grupo "Situação" com uma linha só repetia o rótulo).
- **Mensagem do pop-up.** Quando o motivo da recusa já está escrito junto dos campos (erro do campo ou aviso do 409), a barra do pop-up diz só "Corrija o que está destacado e salve de novo." em vez de repetir o texto; erro de rede ou de permissão aparece na barra.
- **Dia marcado sem horário.** Ao marcar um dia que estava fechado os campos começam vazios e salvar assim é recusado com a mensagem de antes (regra de negócio preservada; P-112 pergunta se deve sugerir um horário).
- **Métodos de computador do cliente da API.** `NoraApiClient.ObsDevicesAsync`, `CreateObsDeviceAsync`, `IssueDeviceCodeAsync`, `RevokeDeviceAsync` e os modelos de computador não são interface e ficam (as checagens de contrato da API e o E2E os usam); ver P-111.

## Mudanças em arquivos fora das telas do grupo

- `Nora.Desktop/Views/SubPageStack.cs` e `EmptySearchVisibilityConverter.cs`: arquivos **novos**, usados por `UnitSettingsView`; qualquer outra tela com subtelas e listagem inteira pode usar o mesmo painel (P-114).
- `WorkspaceViewModel.cs`: construtor sem `IClipboardService`; lista `*Dependents` sem as propriedades de computador e de `CanChange*` das ações antigas.
- `WorkspaceViewModel.ObsUnits.cs`, `UnitData.cs`, `UnitMetrics.cs`: `ClearDevices` e `UnitScope.Devices` saíram; `unit-settings` deixou o limite de 5 minutos (relê a cada ciclo).
- `ShellViewModel.cs`: só o menu de Informações da UBS (sai "Computadores", a descrição perde "e computadores").
- `Models/SchedulingModels.cs`: `MetaLine`, `StatusTone` e `TitleDisplay` dos itens.
- Checagens fora do grupo, só o necessário: `ListingChecks.cs` (o marcador da tela de Informações da UBS, que já não tem título), `DesktopAccessChecks.cs`, `ObsDeviceChecks.cs` (E2E de computadores pelo cliente da API), `RetryTextsChecks.cs`, `ReviewFixesChecks.cs`, `ReviewLoop2Checks.cs` e `SchedulingChecks.cs` (as telas viraram pop-ups e listagens).

## Validação real

- `dotnet build NORA.slnx --configuration Release --nologo -m:1 -v q`: **0 erros, 0 avisos** de código do grupo (o aviso do `Nora.Api` em `ApiEndpoints.Photo.cs` é de antes).
- Checagens do desktop (suíte completa, sem galeria, rodada duas vezes, a última depois da limpeza de código morto): **251 PASS e 1 FAIL**; a única falha é a conhecida da linha de base (P-066, "faixa de retorno … home", só falha na suíte completa). Linha de base da fundação: 244 PASS e 1 FAIL; esta parte soma 7 checagens novas de Informações da UBS e 1 de Atendimento e retira 1 da subtela Computadores. Subconjunto do grupo (Atendimento, Profissionais, Consultórios, Grade semanal, Bloqueios, Informações da UBS, textos de nova tentativa, Computador do OBS, desativar): 37 PASS e 0 FAIL.
- Checagens novas em `UnitInformationChecks.cs` (7 da autoria e 1 da revisão, o pop-up que fecha com a sessão encerrada): menu sem Computadores e sem título repetido nem Atualizar; Profissionais (listagem, balão, lote, ficha); pop-up do profissional (grade, conselho, 409 dentro); Consultórios; Grade semanal (dias, novo horário com o dia, lote); Bloqueios; estados (vazio, sem resultado, falha, carregando, seleção que sobrevive à releitura, atendente só consulta). Em `UnitProfileChecks.cs` o Atendimento ganhou 1 e teve 2 adaptadas (sem card, aviso amarelo, caixa e "às", altura, um intervalo, dois intervalos gravados, atendente).
- Checagens removidas: a "Computador do OBS: seção Computadores do administrador (lista, vazio, falha …, ativar este, código que some, revogar este)" (`ObsDeviceChecks.cs`), por testar a subtela retirada; o E2E de computadores foi **adaptado** (o administrador gera o código e revoga pelo cliente da API, sem a tela), não removido. As asserções `ShowDevicesSection` de `DesktopAccessChecks`/`ObsDeviceChecks` viraram "não leu `/devices`" e "a atendente entra".
- Capturas em `.build/redesenho/informacoes-ubs/`, claro e escuro, conferidas uma a uma: atendimento (não informado, com horários, erros, dois intervalos, escuro), profissionais (listagem com balão claro e escuro, vazio, sem resultado, falha, janela de 980 px, ficha em linhas, pop-up claro e escuro, pop-up com erro, especialidade desativada), consultórios (lista, balão, pop-up), grade (dia, dia vazio, pop-up, sobreposição, vazio escuro), bloqueios (lista com balão, vazio, pop-up). Nada cortado nem sobreposto, sem título repetido, sem card atrás da lista, lixeira branca em fundo vermelho, botões novos à direita no cabeçalho.

## Estado

- 26/11/2026: fundação mesclada (fast-forward de `claude/desktop-redesign-ui`), build com 0 erros. Leituras feitas: pedido, especificação, anexos, design, componentes da fundação, skills de listagem.
- 26/11/2026: as cinco subtelas redesenhadas, Computadores removido, checagens adaptadas e novas, capturas conferidas.
- 26/11/2026: loop de verificação concluído: conferência item a item contra H1 a H6 e os itens A, varredura de código morto (saíram `SelectedBlock`, `HasActiveProfessional` e os métodos de um item só `ChangeProfessionalActiveAsync` e `ChangeRoomActiveAsync`) e ajustes finos (campos de horário do Atendimento com 8,6 de preenchimento, "Situação" da ficha no grupo Identificação). Última volta sem achados; build com 0 erros e 0 avisos; suíte completa 251 PASS e 1 FAIL conhecido (P-066).
- 26/11/2026: revisão crítica concluída (seção acima): 7 achados corrigidos, build 0 erros, suíte 252 PASS e 1 FAIL conhecido (P-066).
- Falta, dessa parte, só o teste manual na tela real (P-110) e as decisões do usuário P-112 e P-113.

## Revisão crítica (26/11/2026)

Revisão do trabalho acima contra H1 a H6, os itens A e a especialidade da especificação, frase por frase, com capturas abertas uma a uma. Achados e correções:

1. **Erro de XAML deixado pela edição anterior:** o botão Salvar do Atendimento tinha `AutomationProperties.Name="…"views:EnterNavigation…` sem espaço (a tela não carregaria). Corrigido; o nome de automação ficou "Salvar atendimento ao público" (o texto antigo do botão) e o rótulo visível é "Salvar", como o pedido.
2. **Consultório com "Renomear":** a especificação pede o balão "Editar e Desativar (lixeira) ou Reativar" igual a Profissionais. O botão e o título do pop-up passaram a "Editar" / "Editar consultório"; a checagem acompanhou.
3. **Pop-up de cadastro preso depois de a sessão cair:** a ficha fechava sozinha quando a sessão terminava ou a UBS mudava, mas o pop-up de cadastro ficava aberto por cima da tela de entrada. Agora fecha sozinho (sem gravar), inclusive quando a recusa vem no meio do próprio salvamento (fecha logo que o pop-up fica livre). Checagem nova.
4. **Dica do conselho profissional:** estava com quatro linhas na versão anterior; ficou curta (duas a três linhas no pop-up): "O conselho profissional é o órgão que registra a profissão, como o CRM (medicina) ou o COREN (enfermagem). Informe sigla, número e UF juntos."
5. **Aviso do intervalo do meio:** quebrava em duas linhas; o pedido diz "uma linha discreta". Texto encurtado.
6. **Ficha do horário da grade:** o grupo "Vigência" tinha uma linha só, também chamada "Vigência" (rótulo repetido, o mesmo caso já corrigido em profissional e consultório). A vigência entrou no grupo "Horário".
7. **Capturas de pop-up esmaecidas:** as imagens do pop-up recém-aberto saíam no meio da animação de entrada. As checagens do grupo esperam a animação antes de capturar (`SaveDialog`).
8. Capturas novas para a revisão: grade no tema escuro e na janela de 980 px (com a checagem de que os sete dias e o botão novo cabem), balão de lote da grade, ficha do horário e do bloqueio, pop-up da grade no escuro, consultórios e bloqueios com balão no escuro.

Validação depois das correções: build com **0 erros e 0 avisos**; suíte completa do desktop **252 PASS e 1 FAIL** (o P-066 da linha de base; a checagem nova soma uma às 251 da autoria); checagens do grupo (Informações da UBS, Profissionais, Consultórios, Grade semanal, Bloqueios, Atendimento ao público, Configurações da UBS, nova tentativa, Computador do OBS, login compacto, Revisão) todas PASS. Capturas regeradas em `.build/redesenho/informacoes-ubs/` e abertas nos dois temas.

Conferido e sem achado: sem título repetido, sem banner, sem card atrás das listas, lixeira branca em fundo vermelho, botões novos no cabeçalho à direita da pesquisa, balão por cima da lista sem faixa vazia, seleção e balão que não piscam no ciclo de 20 s (checagem), uma confirmação para o lote, Editar some com vários selecionados, Reativar/Desativar só quando valem para todos, estados vazio e "sem resultado na busca" diferentes, rota antiga `unit-settings:devices` abrindo a página, nenhum botão ou método de atualizar sobrando, lógica de computador ativado intacta (checagens de `ObsDeviceChecks` e `DesktopAccessChecks` passam), nenhum hex nas telas.

## Pendências novas (números reservados P-110 a P-114)

- **P-110 (teste sugerido ao usuário).** Abrir o OBS com um administrador e passar por Atendimento, Profissionais, Consultórios, Grade semanal e Bloqueios no tema claro e no escuro: marcar e desmarcar dias; cadastrar, editar, desativar e reativar um profissional e um consultório; selecionar vários com Ctrl e Shift e conferir o balão; criar e remover um horário e um bloqueio; apertar Enter e dar duplo clique para ver a ficha; reduzir a janela até 980 px. Defeito: balão que pisca ou some no ciclo de 20 s, pop-up que fecha com erro, ou campo cortado.
- **P-111.** `NoraApiClient.ObsDevicesAsync`, `CreateObsDeviceAsync`, `IssueDeviceCodeAsync`, `RevokeDeviceAsync` e os modelos `ObsDevice`, `ObsDeviceList`, `CreatedObsDevice`, `RevokedObsDevice` ficaram sem uso no programa (só as checagens de contrato os usam). Decidir se saem junto com a API de computadores (que continua desligada por padrão).
- **P-112.** Ao marcar um dia de atendimento, os campos de horário começam vazios. Decidir se sugerimos um horário padrão (por exemplo, o do dia útil anterior).
- **P-113.** O Atendimento lista domingo a sábado e a Grade semanal, segunda a domingo (como o pedido). Decidir se o Atendimento deve seguir a mesma ordem da Grade.
- **P-114.** `SubPageStack` e `EmptySearchVisibilityConverter` nasceram nesta parte; na integração, conferir se os outros grupos de telas com subtelas e listagem inteira (Pacientes, Vaga Relâmpago) criaram algo equivalente e unificar.
