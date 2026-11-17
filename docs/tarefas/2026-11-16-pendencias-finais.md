# Pendências finais do projeto (rodada de 2026-11-16, noite)

## Skills e comandos invocados

- `/aura-retomar-execucao:aura` (protocolo-tarefa-complexa, diagnosing-bugs, modo-loop-verificacao e graphify juntos), com ultracode ligado.

## O que foi pedido

2026-11-17, segundo `/aura`: finalizar todas as pendências abertas; depois ensinar a mandar os executáveis dos três aplicativos; depois ensinar a hospedar (Hostinger KVM 1 × Render + Neon, preferência por Render + Neon, protótipo com dados de uma escola simulando a rede de saúde, backup e desligamento no fim); por último, montar o roteiro da apresentação de hoje (decisões: demonstração local no notebook; e-mail pelo Gmail com senha de app).

Pedido de 2026-11-16:

1. "continue, finalizando todas as pendências abertas do meu projeto";
2. "faça as perguntas durante a requisição para eu tomar as decisões necessárias".

Vale também o pedido anterior desta sessão: commit local por fase e, no fim de tudo, push na `main`.

## Objetivo

Fechar o que é possível fechar das pendências abertas em `docs/PENDENCIAS.md` e nos achados das revisões. Implementar as decisões abaixo de ponta a ponta (API, OBS/Secretaria e app), com testes. Deixar aberto, com o motivo, só o que depende de algo externo: credenciais do Google, senha do PostgreSQL de uso e conferência física.

## Decisões do usuário (2026-11-16)

| Assunto | Decisão |
| --- | --- |
| P-014, acesso do OBS | Fazer agora. O administrador da UBS ativa e revoga os computadores, sem limite de quantidade. A ativação vale até ser revogada. O login da secretária continua obrigatório, com "manter conectado". |
| P-015, painel do dono da marca | Encerrar: o Painel Municipal/Secretaria cumpre, sem valores financeiros. |
| "Chamar próximo" na Fila do OBS | Oferecer vaga ao 1º: a atendente escolhe um horário livre da agenda e o botão oferece esse horário ao primeiro da fila daquela especialidade, furando a espera automática. |
| Serviços e horários da UBS no app | O administrador da UBS edita no OBS o horário de funcionamento por dia da semana e um texto livre de serviços. As especialidades vêm da grade. |
| Conflito de horário entre UBS | Recusar sempre, no app e no OBS, com a mensagem "já tem consulta neste horário em outra UBS", sem dizer qual. |
| Confirmação clínica da vaga reaproveitada | Opcional por UBS. Ligada, a vaga do cancelamento espera a equipe aprovar no OBS antes de ser ofertada. Se ninguém aprovar até faltar 1 h, ela fecha. |
| P-014, primeira ativação | O administrador da UBS entra sem ativação, em qualquer computador, e de dentro do OBS ativa aquele e os outros. Só a atendente precisa de computador ativado. |
| Chamar próximo: recusa ou vencimento | O horário segue para a distribuição automática da Vaga Relâmpago e é oferecido aos seguintes da fila. |
| "Remover UBS" ao agir por um dependente | Esconder: o botão só aparece quando a pessoa age por si mesma. |

## Restrições

- Nenhuma credencial no repositório.
- Não pedir a senha do PostgreSQL.
- Não apagar pendências: quando resolvidas, elas vão para Resolvidas.
- Não usar `git stash` puro.
- Merge e push na `main` só no fim, conforme a autorização já dada.

## Checklist

- [x] P-045 e P-040: testes instrumentados antigos e E2E com `-Android` (agente do emulador).
- [x] Commit do app e dos docs da segunda volta.
- [x] P-042 no app: botões do Google e `FirebaseMessagingService`, desligados sem credenciais.
- [x] P-014: ativação de computador do OBS (API, OBS e testes).
- [x] Chamar próximo: oferta manual ao 1º da fila (API, OBS e testes).
- [x] Serviços e horários da UBS (API, OBS, app e testes).
- [x] Conflito de horário entre UBS (API, mensagens no app e no OBS, testes).
- [x] Confirmação clínica opcional por UBS (API, OBS e testes).
- [x] Esconder "Remover UBS" ao agir por um dependente (app).
- [x] Achados dos verificadores: textos da Secretaria que mandam usar um botão inexistente; texto da exportação de relatórios; `recordsFailed` sem `loadFamily()`.
- [x] Sugestão de e-mail perdida quando o commit central falha depois do commit da UBS.
- [x] P-015 encerrada em Resolvidas.
- [x] Docs: parágrafo de abertura de PENDENCIAS e linha "Continua fora" de `integracao-back-front.md`.
- [ ] Loop de revisão até uma volta sem achados. Volta 1 com 12 achados confirmados e corrigidos; a volta 2 não rodou (limite semanal dos subagentes, P-050).
- [x] Validação final: `NORA.slnx`, API com PostgreSQL 18, desktop, E2E e Android.
- [ ] Merge na `main` e push.
- [x] Guia de distribuição (`docs/distribuicao.md`, `scripts/empacotar-clientes.ps1`).
- [x] Guia de hospedagem (`docs/hospedagem.md`, `Dockerfile`), sem deploy (P-049).
- [x] Roteiro da demonstração (`Mateus-Projects/Claude-PLANS/Projeto-NORA/roteiro-demonstracao.md`) e `scripts/demo-local.ps1`.

## Estado

- 2026-11-16 19:40: decisões recebidas e registradas. O agente do emulador foi retomado depois de o diagnóstico dele travar por 2 h (processo morto e banco descartável parado).
- 2026-11-17: o workflow foi retomado depois da queda da sessão. API (241 verificações na primeira rodada), telas do OBS e da Secretaria e validação do desktop terminaram verdes. A revisão adversarial (volta 1) confirmou 12 achados, entre eles: o Dockerfile escutava só em 127.0.0.1 e recusava o host público; impasses no login e na revogação; o fecho da vaga sem `closed_reason`; o reconciliador que trocava uma sugestão nova por uma antiga; estados da tela de entrada do OBS; e textos de nova tentativa. O corretor aplicou as correções com testes e parou no limite semanal dos subagentes. Rodei eu mesmo a validação: `NORA.slnx` com 0 erros e 0 avisos, desktop 170/170, API com todas as suítes verdes e E2E 173/173.
- 2026-11-17: escritos `docs/distribuicao.md`, `docs/hospedagem.md`, `Dockerfile`, `.dockerignore`, `scripts/empacotar-clientes.ps1`, `scripts/demo-local.ps1` (testado: sobe banco, migrações e API; aceita o host `10.0.2.2`) e o roteiro. P-014 e P-015 foram para Resolvidas; P-048 (APK de release), P-049 (deploy não feito) e P-050 (volta 2 da revisão) ficaram abertas.
