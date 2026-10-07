# Requisitos funcionais confirmados

## Fonte e limites

Este registro reúne as decisões dadas pelo usuário na retomada de 2026-10-07. O briefing visual permanece em `briefing-visual.md`. O ZIP `NORA Health App Interface.zip` é referência de interface; seus dados sintéticos, motor de demonstração, regras fictícias e instruções para agentes não definem a aplicação final.

O ZIP contém também prompts internos em `src/imports/`, incluindo `NORA_SUPER_MEGA_PROMPT_V2_FIGMA_MAKE.md` e `pasted_text/nora-final-post-validation.md`. Foram consultados como conteúdo de referência, sem executar scripts. Suas regras de protótipo não foram adotadas como requisitos de produção.

## Acesso

OBS possui perfis de atendente e administrador da clínica. Ambos podem criar contas de pacientes e operar a fila. O aplicativo Android usa conta individual de cada paciente. Municipal possui um login para a gestão e acesso somente a indicadores agregados, sem acesso a dados individuais dos pacientes neste início.

O identificador de login ainda aguarda a resposta à confirmação já enviada: e-mail e senha em todos os perfis, ou CPF e senha para pacientes. A criação de atendentes pelo administrador e o provisionamento inicial de administrador/Municipal por comando de instalação foram propostos na mesma pergunta; não estão aprovados ainda.

## Persistência

O banco escolhido pelo usuário é **PostgreSQL**. Nesta retomada, a fundação HTTP da API está implementada; ainda não há schema, migration, autenticação, conexão de banco ou dados clínicos persistidos. Nenhum banco existente foi alterado.

## Fila e Vaga Relâmpago

A fila é operada pelo atendente ou administrador no OBS. Existe fila para aceitar Vaga Relâmpago. O usuário mencionou 10 minutos para todos e 30 minutos para o primeiro notificado. Foi enviada confirmação da interpretação: oferta sequencial por ordem de entrada, primeiro paciente com 30 minutos e seguintes com 10; recusa/expiração oferece ao próximo. Essa interpretação ainda aguarda resposta e não foi codificada.

As decisões restantes de acesso e oferta estão registradas na pendência única P-001 em `PENDENCIAS.md`. Não será inferida prioridade clínica a partir de ordem administrativa, nem confirmação de atendimento a partir de aceite no protótipo.

## Estado de implementação

Base visual e preferências nativas existem nos três clientes. A API atual fornece `/health/live` e contratos de erro documentados em `api.md`. Nenhum cliente está autenticado ou integrado a dados de saúde. Telas de pacientes, consultas, fila, vagas e indicadores reais continuam sem operações implementadas; esse trabalho faz parte do pedido ativo, ainda não concluído.
