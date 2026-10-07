# Requisitos funcionais confirmados

## Fonte e limites

Este registro reúne as decisões dadas pelo usuário na retomada de 2026-10-07. O briefing visual permanece em `briefing-visual.md`. O ZIP `NORA Health App Interface.zip` é referência de interface; seus dados sintéticos, motor de demonstração, regras fictícias e instruções para agentes não definem a aplicação final.

O ZIP contém também prompts internos em `src/imports/`, incluindo `NORA_SUPER_MEGA_PROMPT_V2_FIGMA_MAKE.md` e `pasted_text/nora-final-post-validation.md`. Foram consultados como conteúdo de referência, sem executar scripts. Suas regras de protótipo não foram adotadas como requisitos de produção.

## Acesso

OBS possui perfis de atendente e administrador da clínica. Ambos podem criar contas de pacientes e operar a fila. O aplicativo Android usa conta individual de cada paciente. Municipal possui um login para a gestão e acesso somente a indicadores agregados, sem acesso a dados individuais dos pacientes neste início.

O usuário autorizou criar uma conta para cada sistema usando os dados fornecidos privadamente: OBS e Municipal previamente cadastrados e paciente Android cadastrado por meio do OBS. CPF, e-mail e senha foram fornecidos; a pergunta complementar é se o login aceita CPF, e-mail ou ambos. Os dados pessoais e a senha não fazem parte desta documentação nem de seeds versionados. A criação de outros atendentes pelo administrador ainda não foi confirmada.

## Persistência

O banco escolhido pelo usuário é **PostgreSQL**. Nesta retomada, a fundação HTTP da API está implementada; ainda não há schema, migration, autenticação, conexão de banco ou dados clínicos persistidos. Nenhum banco existente foi alterado.

## Fila e Vaga Relâmpago

A fila é operada pelo atendente ou administrador no OBS. Existe fila para aceitar Vaga Relâmpago. Na resposta atual, o usuário definiu **10 minutos para todos**, independentemente da posição, corrigindo a proposta anterior de 30 minutos para o primeiro. Recusa ou expiração passa a oferta ao próximo. O critério de escolha do próximo ainda precisa de confirmação; não inferir prioridade clínica ou ordenação por entrada. A regra ainda não está implementada.

As decisões restantes de acesso e oferta estão registradas na pendência única P-001 em `PENDENCIAS.md`. Não será inferida prioridade clínica a partir de ordem administrativa, nem confirmação de atendimento a partir de aceite no protótipo.

## Estado de implementação

Base visual e preferências nativas existem nos três clientes. A API atual fornece `/health/live` e contratos de erro documentados em `api.md`. Nenhum cliente está autenticado ou integrado a dados de saúde. Telas de pacientes, consultas, fila, vagas e indicadores reais continuam sem operações implementadas; esse trabalho faz parte do pedido ativo, ainda não concluído.
