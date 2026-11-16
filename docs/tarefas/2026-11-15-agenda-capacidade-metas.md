# Back-end: capacidade, agendamento pelo paciente, confirmação, metas e alertas

Data: 2026-11-15. Branch de trabalho: `worktree-agent-ab7c81d53d43692b6` (worktree do agente), base `3d46a78`.

## Skills e comandos invocados

Nenhum pelo usuário. Etapa delegada por outro agente (Fase 3 do plano em `2026-11-11-integracao-back-front.md`).

## O que foi pedido, item por item

1. Especialidades: lista única da rede, no banco central, mantida pela Secretaria (criar, renomear, ativar/desativar, sem excluir); nome único sem diferenciar maiúsculas/acentos; leitura por OBS e paciente.
2. Profissionais (nome, especialidades da lista central, registro de conselho opcional, ativo) e consultórios (nome, ativo) no banco da UBS; só `administrator` escreve, `attendant` lê, Secretaria lê.
3. Grade semanal por profissional (dia, início, fim, duração 5 a 240 min, consultório, especialidade, vigência opcional), sem sobreposição do profissional nem do consultório; bloqueios por data/intervalo (profissional ou UBS inteira) com motivo; só `administrator` mantém; fuso `Nora:TimeZone`.
4. Agendamento pela equipe: com ao menos um profissional ativo na UBS passa a exigir `professionalId` e `specialtyId` (400 `catalog-required`) e só aceita horário livre da grade; sem profissional, texto livre continua.
5. Disponibilidade: horários livres por especialidade (e profissional opcional) = grade − bloqueios − consultas ativas − vagas relâmpago abertas/ofertadas, para OBS e paciente.
6. Agendamento pelo paciente: horários livres, de 24 h a 90 dias; vale na hora; `source='patient'`; uma consulta futura por especialidade marcada pelo app na rede inteira (serializada pelo lock por CPF); 409 `slot-unavailable` / `specialty-limit` / `booking-window`.
7. Confirmação de presença pelo app de 48 h a 2 h antes (409 `confirmation-window`), idempotente; listagens do OBS com `confirmedAt` e `bookedBy` (`staff`/`patient`/`lightning`); nada é cancelado sem confirmação.
8. Capacidade nos indicadores (OBS, Secretaria, `byUnit`): `capacity` {offeredSlots, bookedSlots, occupancy}; consultórios ativos (em uso agora e previstos hoje).
9. Metas (central): `attendance_score` (mínimo), `missed_rate` (máximo), `average_wait_minutes` (máximo), da rede com sobrescrita por UBS; `goals` nos indicadores com status `met`/`near`/`off`/`no-data` (perto = 10% da meta).
10. Alertas (`Nora:Alerts:*`): fila alta (20+), espera longa (> 30 dias), faltas (> 20% em 30 dias), ocupação (> 95% em 7 dias), meta fora; rotas do OBS e da Secretaria sem dado pessoal.

## Objetivo

Módulo de agenda completo no back-end, com contrato documentado em `docs/api.md` e verificações automatizadas.

## Restrições

- Migração central só `015_*.sql`; de UBS só `012_*.sql`. Rotas novas em `Nora.Api/ApiEndpoints.Scheduling.cs`, ligadas por uma linha em `ApiEndpoints.cs`. DI em bloco `// Fase 3 — agenda e metas`.
- Mexer em arquivos existentes só onde a regra exige. Sem push, merge, rebase nem stash. Não tocar em clientes nem em outros docs (`docs/PENDENCIAS.md` fica para a integração).
- Outro agente trabalha em paralelo (comunicação e jornada; migrações 013 de UBS e 016 central não são minhas).

## Checklist

- [x] Migrações 015 central e 012 de UBS, reaplicáveis.
- [x] Especialidades (central) e rotas da Secretaria.
- [x] Profissionais, consultórios, grade e bloqueios (OBS escreve admin, lê attendant, Secretaria lê).
- [x] Disponibilidade (motor de horários) e agendamento pela equipe com catálogo.
- [x] Agendamento pelo paciente, limite por especialidade na rede, confirmação.
- [x] Capacidade, consultórios ativos, metas e alertas.
- [x] Documentação em `docs/api.md`.
- [x] Build 0/0; HTTP; PostgreSQL descartável (`-Port 55611 -IncludeUnitDatabases`).

## Estado

- Base conferida (`3d46a78`). Implementado: migrações central 015 e de UBS 012; especialidades, profissionais, consultórios, grade, bloqueios, disponibilidade, marcação pela equipe (catálogo) e pelo paciente, confirmação, capacidade, consultórios ativos, metas e alertas. Contrato em `docs/api.md` (seção "Capacidade, agendamento e metas").
- Validação (cluster descartável, porta 55611): build Release 0 avisos/0 erros; HTTP 30/30; fluxos 28/28; plataforma 19/19; revisão 17/17; agenda 20/20 (`tests/Nora.Api.Checks/SchedulingChecks.cs`, também isolável com `--scheduling-only`).
- Pendente para a integração: unir `Program.cs` dos checks, `ApiEndpoints.cs` (linha `MapSchedulingEndpoints`), `NoraApiHost.cs` (bloco Fase 3) e `docs/api.md` com o outro agente; registrar em `docs/PENDENCIAS.md` (não tocado aqui): clientes ainda não consomem as rotas novas; dúvidas de regra resolvidas com o mínimo seguro (ver relatório do agente).
