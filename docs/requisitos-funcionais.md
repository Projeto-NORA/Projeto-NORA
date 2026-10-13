# Requisitos funcionais confirmados

## Fonte e limites

Este registro reúne as decisões dadas pelo usuário na retomada de 2026-10-07. O briefing visual permanece em `briefing-visual.md`. O ZIP `NORA Health App Interface.zip` é referência de interface; seus dados sintéticos, motor de demonstração, regras fictícias e instruções para agentes não definem a aplicação final.

O ZIP contém também prompts internos em `src/imports/`, incluindo `NORA_SUPER_MEGA_PROMPT_V2_FIGMA_MAKE.md` e `pasted_text/nora-final-post-validation.md`. Foram consultados como conteúdo de referência, sem executar scripts. Suas regras de protótipo não foram adotadas como requisitos de produção.

## Acesso

OBS possui perfis de atendente e administrador da clínica. Ambos podem criar contas de pacientes e operar a fila. O aplicativo Android usa conta individual de cada paciente. Municipal possui um login para a gestão e acesso somente a indicadores agregados, sem acesso a dados individuais dos pacientes neste início.

Em 2026-10-12, o usuário confirmou desativação/reativação por ambos os perfis OBS. Desativar bloqueia novos logins, encerra sessões e cancela entradas/ofertas pendentes com avanço FIFO, preservando cadastro e histórico concluído. Reativar libera novo login sem recuperar sessões/fila antigas. A interface exige confirmação dos efeitos e separa estado do cadastro da presença online no APPNORA.

O usuário autorizou uma conta para cada sistema: OBS e Municipal previamente cadastrados e paciente Android cadastrado pelo OBS. Login aceita **CPF ou e-mail**; nome aprovado: **Mateus Todeschini Monteiro**. CPF, e-mail e senha pessoais não fazem parte desta documentação nem de seeds versionados. Criar outros atendentes pela interface ainda não foi confirmado.

## Persistência

Banco escolhido: **PostgreSQL 18**, usuário **postgres** na instância de uso. API usa Npgsql, migrações aditivas e contas/sessões/fila/ofertas persistidas. Testes usam PostgreSQL18 descartável e schemas aleatórios. Conexão privada e provisionamento definitivo permanecem em P-012; não presumir a senha das contas como senha PostgreSQL.

## Fila e Vaga Relâmpago

A fila de Vaga Relâmpago é operada por atendente/administrador no OBS. A ordem confirmada é **entrada**, desempate pelo identificador. Toda oferta tem **10 minutos**. Recusa, cancelamento ou expiração libera a vaga para o próximo. O servidor verifica prazo antes das operações e periodicamente a cada15 segundos; aceita somente uma resposta concorrente.

A fila desta entrega é administrativa, para ofertas. Aceite confirma a oportunidade, sem afirmar atendimento clínico realizado. Entrada concluída encerra o ciclo da oferta, inclusive recusa/expiração; nova inclusão pelo OBS cria outra entrada. Não se infere prioridade clínica.

## Estado de implementação

Escopo aprovado: acesso nos três aplicativos, cadastro de pacientes pelo OBS, fila por entrada, Vaga Relâmpago de10 minutos e totais agregados no Municipal. Consultas/agendamento, regras clínicas adicionais, notificações externas e séries/filtros analíticos dependem de definição posterior (P-013). Opções de notificação são preferências locais; ofertas são consultadas por Atualizar.

Provisionamento definitivo das contas e conexão à base de uso permanecem em P-012. Validar com dados sintéticos descartáveis não equivale à criação das contas definitivas.

## Evoluções solicitadas em 2026-10-08

Depois de concluir a entrega anterior, o OBS deverá usar chave de acesso gerada pelo painel do administrador, sem login de atendente/administrador. O usuário pediu pesquisar o modelo e aprimoramentos, comparando `OliverImportsBusinnes`. Alcance, validade e revogação serão definidos pela pesquisa e confirmação do usuário (P-014).

Em seguida, o Painel Municipal será substituído pelo painel administrativo do dono da marca da clínica. Não poderá consultar dados sensíveis de pacientes. O usuário confirmou totais operacionais e financeiros; origem dos lançamentos financeiros ainda em definição (P-015). Esses itens são solicitações separadas e não alteram silenciosamente a etapa anterior validada.
