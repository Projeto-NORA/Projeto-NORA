# Painel operacional da UBS

## Estado atual — 2026-11-15

Esta seção descreve o OBS como está no código (`7ba3c54`, `49a22d1`). As seções seguintes mantêm o texto de etapas anteriores; onde divergem, vale esta. Todo o OBS opera sobre uma UBS por vez, pelas rotas por UBS; o fluxo global anterior foi removido do cliente.

Uma única faixa de mensagens (informação, sucesso ou erro) e uma barra de progresso ficam no topo do shell e aparecem em todas as abas. Um erro de leitura automática é mostrado uma vez e marca os dados como desatualizados; a atualização de 20 s não desabilita os formulários nem descarta cliques.

Depois do login a API devolve as UBS concedidas à conta. Com uma só, o OBS a confirma sozinho; com várias, a pessoa escolhe a cada login; sem nenhuma, a tela explica que falta a concessão pela Secretaria. O nome e a região da UBS aparecem na barra lateral, no cabeçalho e em Configurações, e uma falha de rede não apaga a UBS ativa. Se a base da UBS estiver pendente ou desatualizada (503 `unit-database-outdated`), o OBS mostra uma mensagem própria, sem laço de erro, e orienta a procurar a Secretaria. Só o administrador da UBS mantém profissionais, consultórios e grade (Fase 3); o atendente opera pacientes, fila, agenda e vagas.

- **Painel:** pacientes aguardando, pacientes cadastrados, vagas ativas, consultas do dia por situação, **tempo médio de espera** (da API de indicadores, com leitura que não se repete a cada ciclo) e série dos últimos 7 dias de concluídas, faltas e canceladas, pela data marcada. Dias sem registro valem zero e não há gráfico decorativo. Consultórios ativos continuam indisponíveis até o cadastro de consultórios (Fase 3).
- **Agenda:** carrega sozinha o mês visível, marca no calendário os dias com consulta e habilita Chamar, Concluir, Falta e Cancelar conforme a situação da consulta, evitando 409.
- **Fila:** posição por especialidade (pela ordem de entrada), tempo de espera, situação e ficha por duplo clique ou Enter. Incluir e cancelar entrada são reais; a tela não tem "Chamar próximo", que a oferta de vaga substitui.
- **Indicadores:** período escolhido (até 366 dias) aplicado a cartões, score de comparecimento com explicação do cálculo, gráfico diário com a série preenchida, tabela por especialidade e Vaga Relâmpago do período, tudo de `GET /api/obs/units/{id}/metrics`. Ocupação e consultórios dependem de dados que a UBS ainda não cadastra.
- **Pacientes:** a ficha e a lista mostram a **participação** (score de comparecimento dos últimos 12 meses, por data marcada) com ordenação "Maior participação" e "Menor participação". Sem consulta encerrada no período o valor é "—". O score é `concluídas ÷ (concluídas + faltas + soma dos pesos dos cancelamentos feitos pelo próprio paciente no app)`, com os pesos por antecedência descritos em `api.md`; cancelamentos registrados pela equipe não pesam.
- **Vaga Relâmpago:** a ficha da vaga mostra a sequência de ofertas (paciente, situação e prazo, sem CPF).
- **Alertas:** lista os critérios que já têm dado (fila por especialidade, espera, faltas, cancelamentos) e o que falta para cada um, mas **nenhum alerta é avaliado**. O usuário decidiu os limites em 2026-11-15 (fila com 20 ou mais pessoas numa especialidade; alguém aguardando há mais de 30 dias; faltas acima de 20% em 30 dias; ocupação acima de 95% nos próximos 7 dias; meta "fora"); a avaliação será ligada na Fase 3, em andamento (P-030, P-037).
- **Configurações:** troca de senha pela própria conta. Conta criada pela Secretaria entra com senha provisória e é levada à tela de troca obrigatória antes de operar (a API responde 403 `password-change-required` às demais rotas).

Agendamento com profissional e especialidade do cadastro, capacidade, metas, comunicados, família e jornada (exames e vacinas) estão em implementação na Fase 3, com as regras já decididas (ver `tarefas/2026-11-11-integracao-back-front.md`); ainda não existem no OBS.

## Objetivo

O OBS passa a apresentar a operação da unidade em abas: Painel da UBS, Fila, Agenda, Pacientes, Vaga Relâmpago, Indicadores, Alertas e Configurações. A navegação organiza a rotina da equipe sem misturar dados da gestão municipal.

O visual usa azul-escuro, branco e tons azuis de apoio. A barra lateral mostra o nome da conta conectada, um avatar formado pelas iniciais e a identificação de que os dados são da unidade conectada. A largura é ajustável pela divisória arrastável, sem botão de recolher. Uma foto real do usuário exige que esse dado exista na conta/API; a interface não inventa imagens pessoais.

O acabamento visual usa gradientes azul-profundo, superfícies claras/escuras em camadas, sombras suaves para profundidade e animação curta de escala nos botões ao passar o cursor. Esses movimentos respeitam o foco por teclado e não alteram a ordem, o conteúdo ou o significado clínico dos controles. A paleta é aplicada pelos recursos compartilhados do desktop, portanto mantém os modos Claro, Escuro e Sistema consistentes.

O cabeçalho explica a finalidade da aba atual, e os módulos Pacientes, Fila e Vaga Relâmpago usam uma categoria curta acima do título para tornar a rotina identificável sem depender apenas do ícone da navegação. Alertas permanecem informativos e categorizados em vez de interromper a operação com avisos invasivos.

As capturas de revisão do OBS incluem Painel, Agenda, Indicadores e Alertas, além dos fluxos de Pacientes, Fila, Vaga Relâmpago, acesso e Configurações. Dados individuais exibidos nessas capturas vêm apenas do fixture isolado de testes.

## Estado atual da interface

As contagens de fila e vagas no painel mostram um traço até que a respectiva lista da UBS tenha sido carregada; zero indica que a consulta retornou uma lista sem itens ativos.

A pesquisa de pacientes aceita CPF e telefone com ou sem formatação.

Depois da confirmação, a escolha de UBS fica recolhida para dar mais espaço à listagem; pode ser aberta novamente para trocar de unidade. A correção isolada de e-mail também fica recolhida, e a edição completa permanece na ficha selecionada.

O painel conta as consultas com início no dia local do computador por situação atual: agendadas ou chamadas, concluídas, faltas e canceladas. Esses números descrevem a agenda daquele dia, não a data em que cada situação foi registrada. São atualizados com os demais dados e ficam indisponíveis até a leitura, depois de uma alteração de agenda ou após a troca de UBS. O tempo médio de espera vem da API de indicadores; consultórios ativos seguem indisponíveis.

O Painel mostra a contagem real de pacientes aguardando na fila, pacientes cadastrados e vagas abertas/ofertadas quando a API atual está conectada. As situações das consultas do dia são contadas a partir da agenda integrada. Consultórios ativos e ocupação continuam indisponíveis; o tempo médio e a série de 7 dias vêm dos indicadores. Não há números ou pacientes de demonstração na tela de produção.

Aba por aba, o conteúdo é isolado por uma única área de conteúdo: Painel mostra o resumo, Fila mostra somente a fila, Agenda mostra somente o calendário, Pacientes mostra somente a lista/cadastro e Vaga Relâmpago mostra somente as ofertas. Ao trocar de aba, a tela anterior é removida em vez de permanecer ocultada na mesma rolagem. A aba Fila apresenta senha derivada da entrada, paciente, especialidade (quando fornecida), posição por ordem de entrada, tempo de espera e situação retornada pela API. As ações de incluir e cancelar a entrada já existentes continuam reais. A tela não oferece "Chamar próximo": a chamada de um paciente acontece pela consulta na Agenda e pela oferta de vaga.

Antes do login, o Painel da UBS oferece o acesso à API. Agenda, Indicadores e Alertas continuam navegáveis como telas sem dados, e Fila, Pacientes e Vaga Relâmpago apresentam uma orientação específica de acesso protegido, em vez de repetirem o formulário de login. Isso deixa clara a função de cada aba sem expor ou inventar dados de uma unidade não autenticada.

Agenda fornece um seletor segmentado de Dia e Semana, formato de calendário clicável e os estados visuais Livre (amarelo), Ocupado (roxo), Cancelado (vermelho) e Concluído (verde), sem criar consultas fictícias. A seleção de uma data é local; a consulta explícita carrega os agendamentos reais do período na UBS confirmada. Indicadores e Alertas são telas distintas: a primeira mostra as métricas reais da UBS e o gráfico diário; a segunda lista os critérios de alerta e indica explicitamente que a avaliação ainda não foi ligada.

O acesso valida localmente CPF formatado pelos dígitos verificadores ou o formato do e-mail antes de enviá-lo à API; a API repete a validação. Isso reduz erros de digitação, mas não equivale a validar identidade em uma base governamental. No cadastro de pacientes, endereço é opcional: Buscar CEP consulta a API NORA, que encaminha apenas os oito dígitos ao ViaCEP e preenche logradouro, bairro, cidade e UF. A equipe confere o número e pode ajustar os campos antes de salvar. O endereço salvo aparece somente na ficha do paciente para o OBS autenticado.

Abrir o cadastro limpa a seleção de uma ficha já aberta, para a barra de ações contextual não cobrir os campos do formulário.

## Dependências para a próxima etapa

Em Pacientes, o cadastro de referência por UBS permite carregar as unidades autorizadas para a conta, confirmar uma delas e listar ou cadastrar pessoas com CPF, nome, e-mail, telefone e endereço opcionais. O CEP pode ser consultado pela API e os campos continuam editáveis. Esse cadastro alimenta a verificação de vínculo no aplicativo. Após selecionar um cadastro, a equipe pode corrigir seu e-mail mediante confirmação; a alteração é auditada na UBS e não modifica automaticamente contas já criadas no aplicativo. A ficha também permite editar nome, e-mail, telefone e endereço; o CPF permanece fixo e cada edição é auditada.

A listagem permite pesquisar por nome, CPF, e-mail ou telefone, filtrar a situação na UBS e a presença no aplicativo, ordenar por data de cadastro ou nome e abrir ficha com cópia explícita. A presença mostra atividade recente no aplicativo, sem indicar em qual UBS o paciente está navegando. Atendente ou administrador pode desativar ou reativar o paciente **somente na UBS selecionada**, após confirmação. Na desativação, entradas e ofertas pendentes dessa unidade são canceladas; consultas e acesso às outras UBS são preservados. A reativação não restaura entradas canceladas.

Após carregar as UBS autorizadas, a equipe pode pedir sugestão pela localização atual. A janela solicita a posição pelo WebView2 e pelo serviço de localização do Windows; a aplicação calcula a unidade autorizada mais próxima localmente e exige confirmação antes da troca. O conteúdo da janela é local e as coordenadas não são enviadas à API NORA. As permissões e o provedor de localização do Windows/Edge podem exigir configuração ou acesso à rede; se não houver posição recente, a escolha manual permanece disponível. O cálculo foi preparado para teste com coordenadas sintéticas; a localização física e as permissões do computador ainda exigem validação.

O OBS carrega cadastro, fila e vagas somente da UBS confirmada. As telas antigas globais foram retiradas da navegação e suas rotas ficam bloqueadas por padrão. A migração de cadastros globais antigos para as UBS continua fora do escopo.

A agenda por UBS permite consultar dia ou semana, cadastrar uma consulta para paciente da unidade e chamar, concluir, registrar falta ou cancelar após confirmação. Os dados são lidos do banco da UBS confirmada. A equipe informa horários locais e profissional; a API impede sobreposição para o mesmo paciente ou nome de profissional e registra as mudanças com autor e data. Uma consulta cancelada pode gerar uma Vaga Relâmpago mediante confirmação; ela é distribuída para a fila da mesma especialidade e UBS.

Consultas e estados de atendimento já existem por UBS. Para completar o OBS faltam os cadastros estruturados de profissionais, especialidades e consultórios (Fase 3, em andamento) e a avaliação dos alertas.

O servidor confere a concessão da conta OBS a cada operação e usa o banco da UBS escolhida. A unidade só muda após confirmação na interface; a gestão tem acesso à rede inteira.
