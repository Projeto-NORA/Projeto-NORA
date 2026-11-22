# Requisitos funcionais confirmados

## Fonte e limites

Este registro reúne as decisões dadas pelo usuário na retomada de 2026-10-07. O briefing visual permanece em `briefing-visual.md`. O ZIP `NORA Health App Interface.zip` é referência de interface; seus dados sintéticos, motor de demonstração, regras fictícias e instruções para agentes não definem a aplicação final.

O ZIP contém também prompts internos em `src/imports/`, incluindo `NORA_SUPER_MEGA_PROMPT_V2_FIGMA_MAKE.md` e `pasted_text/nora-final-post-validation.md`. Foram consultados como conteúdo de referência, sem executar scripts. Suas regras de protótipo não foram adotadas como requisitos de produção.

## Acesso

OBS possui perfis de atendente e administrador da clínica. Ambos podem criar contas de pacientes e operar a fila. O aplicativo Android usa conta individual de cada paciente. Na primeira etapa, o Municipal possuía um login de gestão e acesso apenas a indicadores agregados; a decisão posterior de 2026-11-07 amplia esse acesso conforme registrado abaixo.

O usuário confirmou desativação e reativação por atendente ou administrador do OBS e, posteriormente, que o bloqueio deve valer somente para a UBS que o executa. A conta e o acesso às demais unidades permanecem ativos; entradas e ofertas pendentes da unidade são canceladas, consultas existentes são preservadas e a reativação não restaura entradas canceladas. A interface exige confirmação.

O usuário autorizou uma conta para cada sistema: OBS e Municipal previamente cadastrados e paciente Android cadastrado pelo OBS. O paciente entra por **CPF ou e-mail**; a equipe (OBS e Municipal) entra **só por e-mail**, porque as contas de equipe não têm CPF (migração central 022, 2026-11-17); nome aprovado: **Mateus Todeschini Monteiro**. CPF, e-mail e senha pessoais não fazem parte desta documentação nem de seeds versionados. Criar outros atendentes pela interface ainda não foi confirmado.

Em 2026-10-28, o acesso passou a rejeitar CPF com formato ou dígitos verificadores inválidos antes da chamada de login e a API repete essa checagem. Essa é uma validação matemática de consistência, não uma consulta à Receita Federal. O cadastro OBS também aceita endereço opcional: a equipe consulta CEP, confere os campos retornados e informa número/complemento quando necessário. Somente o CEP é enviado ao serviço externo de consulta.

## Persistência

Banco escolhido: **PostgreSQL 18**, usuário **postgres** na instância de uso. API usa Npgsql, migrações aditivas e contas/sessões/fila/ofertas persistidas. Testes usam PostgreSQL18 descartável e schemas aleatórios. Conexão privada e provisionamento definitivo: P-012 encerrada em 2026-11-17 (demonstração em banco próprio e piloto no Neon; ver [hospedagem.md](hospedagem.md)); não presumir a senha das contas como senha PostgreSQL.

## Fila e Vaga Relâmpago

A fila de Vaga Relâmpago é operada por atendente/administrador no OBS. A ordem de entrada na fila é por data de **entrada**, desempate pelo identificador; a **ordem de oferta** da Vaga Relâmpago é por **pontos de score** (mais pontos primeiro) e só em empate vale a ordem de entrada (`entered_at`, depois `id`; ver `api.md`, "Score em pontos e prioridade na Vaga Relâmpago"). Toda oferta tem **10 minutos**. Recusa, cancelamento ou expiração libera a vaga para o próximo. O servidor verifica prazo antes das operações e periodicamente a cada15 segundos; aceita somente uma resposta concorrente.

A fila desta entrega é administrativa, para ofertas. Aceite confirma a oportunidade, sem afirmar atendimento clínico realizado. Entrada concluída (oferta aceita) encerra o ciclo da oferta; recusar ou deixar vencer devolve a entrada à fila com a mesma data de entrada (R-030; a posição na ordem de oferta continua dependendo do score), sem reoferecer a mesma vaga à mesma pessoa; entrada cancelada ou concluída só volta por nova inclusão pelo OBS, que cria outra entrada. Não se infere prioridade clínica.

## Estado de implementação

O escopo atual inclui acesso nos três aplicativos, cadastro por UBS, agenda, fila por entrada, Vaga Relâmpago de dez minutos e gestão da rede. A integração completa da fila, das vagas, de notificações e dos indicadores ainda está em andamento. Opções de notificação são preferências locais; ofertas são consultadas por Atualizar.

O provisionamento definitivo e a conexão à base de uso (P-012) foram encerrados em 2026-11-17 (ver [hospedagem.md](hospedagem.md)). Validar com dados sintéticos descartáveis não equivale à criação das contas definitivas.

## Painel operacional da UBS — 2026-10-15

O OBS passa a organizar visualmente Painel da UBS, Fila, Agenda, Pacientes, Vaga Relâmpago, Indicadores, Alertas e Configurações. O painel deve reunir pacientes aguardando, atendimentos do dia, tempo médio, consultas, faltas, cancelamentos e consultórios ativos. Fila deve expor senha, paciente, especialidade, posição, tempo e status; Agenda deve permitir leitura por dia ou semana com estados livre, ocupado, cancelado e concluído. Indicadores incluem espera, atendimentos, faltas, comparecimento, cancelamentos e uso da agenda.

O visual do OBS adota azul-escuro e branco, com gráficos e calendário sem séries/consultas fictícias. A conta conectada aparece com nome e avatar de iniciais; uma foto depende de um campo de perfil ainda inexistente. A interface preparada nesta etapa exibe somente os dados já entregues pela API e mantém os demais indicadores como indisponíveis, até a implementação real.

Cada UBS deve acessar somente os dados da própria unidade. Isso é requisito de autorização de servidor, não uma regra de visibilidade do WPF. O desenho definitivo requer vínculo da conta à unidade, filtros obrigatórios no banco/API e definição dos perfis que podem chamar, atualizar, registrar falta, finalizar e liberar uma vaga cancelada para Vaga Relâmpago.

## Evoluções solicitadas em 2026-10-08 (histórico, substituído)

> Os dois itens abaixo foram substituídos: o acesso do OBS ficou como login da atendente em computador ativado (P-014, resolvida em 2026-11-17) e o Painel Municipal foi mantido como painel gerencial, sem valores financeiros (P-015, encerrada em 2026-11-17). Ver `docs/PENDENCIAS.md`, Resolvidas. O texto original fica como registro.

Depois de concluir a entrega anterior, o OBS deverá usar chave de acesso gerada pelo painel do administrador, sem login de atendente/administrador. O usuário pediu pesquisar o modelo e aprimoramentos, comparando `OliverImportsBusinnes`. Alcance, validade e revogação serão definidos pela pesquisa e confirmação do usuário (P-014).

Em seguida, o Painel Municipal será substituído pelo painel administrativo do dono da marca da clínica. Não poderá consultar dados sensíveis de pacientes. O usuário confirmou totais operacionais e financeiros; origem dos lançamentos financeiros ainda em definição (P-015). Esses itens são solicitações separadas e não alteram silenciosamente a etapa anterior validada.

## Gestão / Secretaria — 2026-10-28

Solicitada visão da rede com comparação de UBS e especialidades, mapa regional, indicadores e filtros, acompanhamento da Vaga Relâmpago, capacidade, alertas, relatórios, metas, exportação, comunicação, usuários, permissões e histórico. O painel WPF organiza essas áreas com temas claro e escuro; somente os totais agregados já existentes estão integrados. O estado e as dependências de cada área estão em `secretaria.md`.

## Acesso a múltiplas UBS — 2026-11-07

O usuário confirmou que algumas contas do OBS precisam acessar mais de uma UBS. As regras de vínculo entre contas, pacientes e unidades e a seleção da unidade em operação ainda precisam de confirmação; o código atual não implementa esse isolamento. Cancelamentos devem exigir confirmação e registrar a pessoa responsável.

## Agenda e seleção de UBS — 2026-11-07

O paciente poderá escolher a UBS nas telas que apresentam informações de uma unidade específica. Atendentes e administradores autorizados poderão consultar os dados identificáveis necessários à operação da UBS. O paciente pode selecionar somente unidades vinculadas ao seu cadastro. O OBS usará conta vinculada às UBS autorizadas, com sugestão pela localização e confirmação da unidade. A estrutura, as rotas de concessão e a interface da Secretaria foram preparadas; o OBS já permite escolher e confirmar uma unidade; a localização e a aplicação das permissões às rotas operacionais antigas ainda faltam. A gestão deverá acessar os dados de toda a rede, inclusive identificáveis, com filtro por UBS; a consulta de referência por UBS já foi preparada. A conferência inicial de vínculo do paciente está descrita abaixo.

A consulta inicia agendada e poderá ser chamada, concluída, registrada como falta ou cancelada. O cancelamento exige confirmação; mudanças devem registrar autor e data. Uma consulta cancelada poderá gerar Vaga Relâmpago após confirmação. A agenda por UBS já implementa essas transições e seu histórico; a geração de Vaga Relâmpago a partir do cancelamento ainda falta.

A Vaga Relâmpago deve seguir a especialidade e a prioridade por pontos de score (mais pontos primeiro; empate pela ordem de entrada), com resposta em dez minutos, e alcançar somente paciente vinculado à UBS ofertante. Esta frase é a da decisão de 2026-11-07 (então "fluxo existente, que continua sendo uma fila global"); as regras já estão implementadas por UBS e a prioridade por pontos vale desde 2026-11-17.

## Bancos por UBS e cadastro no aplicativo — 2026-11-07

O NORA deverá usar bancos PostgreSQL separados para as UBS, com acesso da gestão a toda a rede. Quando o paciente procura atendimento, a equipe cadastra seus dados na UBS. Depois, no aplicativo, ele informa seus dados e escolhe as unidades às quais pertence; o serviço confere o CPF nos cadastros das unidades escolhidas. Se somente parte das unidades tiver cadastro correspondente, o paciente será informado das que não foram encontradas e poderá continuar com as demais.

Para provar acesso ao endereço cadastrado, o paciente escolherá um dos e-mails encontrados nas unidades selecionadas, apresentados parcialmente ocultos, e receberá um código de confirmação nesse endereço. Após a confirmação, os vínculos correspondentes poderão ser concluídos. Se o e-mail estiver incorreto, a equipe da UBS precisa corrigi-lo. A API e a tela do aplicativo foram implementadas para essa regra; falta validar o fluxo completo com envio real de e-mail e dispositivo conectado à API.
