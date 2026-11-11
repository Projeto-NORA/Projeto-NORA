# Gestão / Secretaria

## Visão e navegação

O executável `Painel-Municipal` oferece a área de gestão da Secretaria com Visão geral, Unidades, Especialidades, Mapa da rede, Indicadores, Vaga Relâmpago, Capacidade, Alertas, Relatórios e metas, Comunicação, Usuários e histórico e Configurações. A interface usa os temas compartilhados Claro, Escuro e Sistema, com identidade azul e menu lateral rolável. A última seção pode ser restaurada pelas preferências locais.

O acesso usa o perfil municipal existente. Depois do login, o resumo consulta totais agregados. Na aba Unidades, a gestão pode consultar explicitamente os cadastros identificáveis da UBS selecionada. São exibidos pacientes aguardando, vagas abertas ou ofertadas e ofertas aceitas, além de vagas registradas e ofertas criadas, recusadas, expiradas, pendentes e canceladas. Pacientes aguardando, vagas abertas ou ofertadas e ofertas pendentes representam o estado atual; os demais são totais históricos. Ofertas criadas não comprovam envio de notificações. Aceite de oferta não é confirmação clínica nem comprova atendimento ou reaproveitamento de uma consulta.

## Dados e funcionalidades pendentes

Em Vaga Relâmpago e Relatórios, o filtro de período oferece todo o histórico ou os últimos 7, 30 ou 90 dias corridos, contados a partir do instante da consulta no servidor. A seleção considera a data de criação da vaga; os estados são os atuais das ofertas associadas às vagas selecionadas, não os eventos ocorridos dentro do período. Os totais de pacientes e fila do CSV continuam atuais e sem filtro. UBS, região e especialidade continuam desabilitados porque a API não oferece essas dimensões. Em APIs antigas, o seletor permanece indisponível. Atendimentos, espera média, faltas, comparecimento, cancelamentos, ocupação, vagas reaproveitadas e séries temporais não são calculados nem simulados. Os indicadores comparativos por unidade e especialidade, capacidade, alertas, metas, relatórios por unidade, comunicação, administração de usuários e histórico de ações aguardam contratos e persistência no servidor. Relatórios oferece exportação CSV do resumo agregado carregado, com data e hora da consulta, sem dados individuais; aplica o período somente aos indicadores de vagas e não aplica os demais filtros desabilitados. Uma API antiga que não fornece os novos campos aparece como indisponível, sem apresentar zero. O logout limpa o resumo e desabilita a exportação. A interface informa essas limitações junto a cada seção.

A implementação real depende de dados de unidades, regiões, profissionais, especialidades, agendas, consultas e eventos de atendimento, além de definições para indicadores, limites de alerta, permissões, comunicação, auditoria e confirmação de reaproveitamento. A API deve impor autorização e isolamento por unidade; controles visuais não substituem essas regras. O provisionamento e a conexão definitiva permanecem descritos em `PENDENCIAS.md`.

## Validação desta etapa — 2026-10-28

A solução compilou em Release sem erros ou avisos. A validação direcionada do perfil municipal passou com login e agregados de fixture, renderização nos temas Claro, Escuro e Sistema, filtros indisponíveis desabilitados e logout. A restauração e seleção das novas rotas passou em verificação própria. As capturas dos temas claro e escuro foram inspecionadas.

A suíte desktop completa teve 56 de 63 verificações aprovadas. As sete falhas envolveram dois testes de contraste que esperam pincel de cor sólida, seleção da tabela OBS, restauração de preferências, rolagem, fluxo de cadastro/fila/vaga no OBS e altura da lista de pacientes. A origem de cada falha não foi investigada nesta etapa. Não houve validação integrada com PostgreSQL nesta execução.

## CPF e CEP

O cadastro valida no servidor os dígitos verificadores do CPF e rejeita números repetidos ou caracteres inválidos; o login aceita CPF ou e-mail e confirma a senha da conta. Essa validação matemática não consulta a Receita Federal nem comprova identidade ou situação cadastral. O cadastro do OBS possui endereço opcional e consulta de CEP, conforme descrito em `api.md` e `obs-painel-operacional.md`.

## Validação complementar — 2026-10-28

A solução compilou em Release sem erros ou avisos. Os testes HTTP da API passaram 22/22, incluindo a validação matemática de CPF, após liberação do loopback local; o PostgreSQL descartável não estava disponível e os testes de banco foram pulados. O teste direcionado da Secretaria passou com fixture: novos totais, CSV agregado, renderização dos três temas, botão de exportação e limpeza no logout. A consulta SQL das novas contagens ainda precisa de execução em PostgreSQL descartável.

## Galeria de telas

A [galeria da Secretaria](galeria/secretaria-2026-10-28/index.html) apresenta as 13 telas em modo claro e escuro, com 42 capturas incluindo as partes de rolagem. As telas conectadas usam uma fixture sintética; o login aparece desconectado. Detalhes e reprodução estão no [README da galeria](galeria/secretaria-2026-10-28/README.md).

## Filtro de período da Vaga Relâmpago

O resumo municipal fornece coortes para 7, 30 e 90 dias, calculadas pelo horário do servidor na mesma transação dos totais. O seletor é compartilhado entre Vaga Relâmpago e Relatórios durante a sessão, e o CSV explicita o período e o escopo de cada valor. Ao sair, o filtro volta ao histórico completo. Se uma API antiga não fornecer as coortes, o filtro fica desabilitado. Os valores gerais do painel não são alterados por esse seletor.

## Mapa da rede

A aba permite carregar um mapa interativo do OpenStreetMap dentro do aplicativo. O mapa inicia com uma visão do Brasil e exige conexão com a internet e Microsoft Edge WebView2 Runtime. O serviço é carregado ao clicar em “Mostrar no mapa”; não recebe dados de pacientes. Quando uma UBS é selecionada, somente suas coordenadas são enviadas no endereço do mapa. A fonte cartográfica é identificada na tela. Em caso de falha, a interface permite tentar novamente.

As UBS são cadastradas e editadas pela Secretaria na aba Unidades, com nome, região, endereço e latitude/longitude em graus decimais. A lista pode ser pesquisada por nome, região ou endereço. Na aba Mapa, selecione a unidade e clique em “Mostrar no mapa” para abrir sua posição informada; o mapa apresenta uma unidade selecionada por vez. As coordenadas devem ser verificadas pela pessoa que as cadastra, pois o sistema não confirma a posição automaticamente. A API ainda não fornece sobrecarga por região. A disponibilidade do mapa depende do serviço externo. O uso dos tiles do OpenStreetMap segue a [política do serviço](https://operations.osmfoundation.org/policies/tiles/).

## Validação dos filtros e do mapa — 2026-10-28

A solução compilou em Release com zero erros e avisos após restauração dos pacotes. O teste direcionado da Secretaria passou com fixture, incluindo alteração do período, valores e CSV correspondentes, limpeza ao sair e conteúdo inicial da aba de mapa. A restauração das rotas também passou. A consulta das coortes tem teste integrado preparado, mas não foi executada por ausência de PostgreSQL descartável; o carregamento real dos mapas pela internet e marcadores de UBS não foram validados nesta sessão.

## Cadastro de unidades

O perfil municipal pode consultar, cadastrar e corrigir unidades pelo servidor. O registro guarda nome, região, endereço e coordenadas; pesquisa e seleção são feitas no cliente sobre a lista carregada. Não existe exclusão nesta etapa. Ao sair, os dados carregados e o formulário são limpos. O cadastro de UBS ainda não vincula usuários OBS ou atendimentos à unidade, portanto não representa isolamento operacional nem possibilita indicadores por UBS.

## Validação do cadastro e localização — 2026-10-28

A API passou 26/26 verificações HTTP e 25/25 verificações PostgreSQL/fluxos em cluster descartável, incluindo migração reaplicada, autenticação, negação ao perfil OBS, cadastro, alteração, validação de coordenadas e coortes da Vaga Relâmpago. O cluster foi encerrado e a evidência ficou em `.build/postgres-validation-3d23f9814ace40c69800ea33f2d6a98b`. A interface passou no teste com API simulada para cadastro, edição, pesquisa, seleção e limpeza ao sair. O teste do navegador incorporado abriu a página do OpenStreetMap com coordenadas sintéticas e confirmou o parâmetro de posição; não houve conferência visual do marcador ou de endereços reais.

A [galeria das telas atualizadas](galeria/secretaria-atualizada-2026-10-28/index.html) reúne Unidades, Mapa da rede, Vaga Relâmpago e Relatórios nos dois temas, incluindo capturas do mapa carregado com um ponto sintético.

## Vínculos de contas OBS — 2026-11-07

A seção Usuários e histórico permite carregar contas OBS ativas e unidades, consultar vínculos e conceder ou revogar uma UBS para a conta selecionada; a revogação pede confirmação. O servidor registra autor e data das alterações efetivas. A consulta do histórico ainda não foi implementada. As rotas globais antigas ficam bloqueadas por padrão; as rotas atuais do OBS exigem concessão de unidade. O teste WPF com API simulada passou para concessão, revogação e apresentação da tela. As rotas e a persistência também passaram em teste com PostgreSQL 18 descartável; o teste integral da janela com a API real ainda falta.

A decisão de 2026-11-07 permite à gestão consultar dados identificáveis por UBS. Em Unidades, a pessoa seleciona uma UBS e solicita a consulta: nome, CPF e e-mail cadastrados no banco daquela unidade aparecem na lista. A API registra autor, unidade e data do acesso; a lista é limpa ao trocar de UBS ou encerrar a sessão. A consulta foi testada via HTTP com PostgreSQL descartável e a tela foi testada com API simulada. Indicadores filtrados por unidade e outros dados clínicos ainda não estão integrados.

## Agenda por UBS

Em Unidades, após selecionar uma UBS, a gestão pode consultar nominalmente as consultas dos últimos 30 dias e dos próximos 30 dias. A API registra a consulta com o usuário e a unidade; a listagem é limpa ao trocar de unidade ou sair. Filtros por período e visão consolidada de consultas da rede ainda não estão implementados.

O resumo de pacientes, fila e vagas é agregado dos bancos das UBS, com CPF contado uma vez. A Secretaria pode carregar as unidades e aplicar o filtro de uma UBS ao resumo; o CSV identifica o escopo. A consulta falha se uma unidade necessária estiver indisponível, para não apresentar totais parciais como completos. Região e especialidade ainda não filtram o resumo.
