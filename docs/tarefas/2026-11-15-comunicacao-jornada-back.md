# Back-end: comunicados (Comunicação) e Jornada (exames e vacinas) — Fase 3

Data: 2026-11-15
Branch: worktree `worktree-agent-a246e4a3e82e3a6e9`, base `3d46a78`.

## Skills e comandos invocados

Nenhum. Tarefa delegada por outro agente (executor da Fase 3, módulo de comunicação e jornada).

## O que foi pedido, item por item

1. Comunicados: a Secretaria (`manager`) cria (título até 120, texto puro até 2000, público
   `staff`/`patients`/`both`, alcance `all`/`region`/`units`, validade opcional), encerra antes da validade
   (não apaga) e lista os seus com contagem de leituras por público.
2. Equipes: aparece no OBS das contas com concessão a UBS alcançada; leitura por conta com instante; a
   Secretaria vê quantas contas leram e quais (nome, sem e-mail).
3. Pacientes: aparece para vinculados (`patient_units`) a UBS alcançada; marcação de lido por conta; rota
   leve e incremental (`GET /api/patient/announcements?after=<cursor>`), índices adequados.
4. Alcance avaliado na leitura; sem dado pessoal no comunicado; sem HTML (texto devolvido como veio).
5. Jornada: equipe da UBS (OBS) registra no banco da UBS exames (`requested` → `done` com data, ou
   `cancelled`) e vacinas (dose `1`/`2`/`3`/`booster`/`single`, data de aplicação); observação até 280; sem
   anexo; anulação (`voided`) com motivo, nunca apaga; escrita grava autor e instante (tabela de eventos).
6. Paciente só visualiza (`GET /api/patient/units/{unitId}/health-records`, sem anulados por padrão) e rota
   agregada leve para a Jornada.
7. A Secretaria não lê registros de saúde nominais.
8. Limites de requisição no padrão do projeto; rate limit por conta nas escritas do paciente.
9. Testes novos (arquivo novo), reaplicação de migrações, documentação (seção "Comunicação e Jornada" em
   `docs/api.md`).

## Restrições

- Migração central só `016_*.sql`; de UBS só `013_*.sql` (sem depender de 012).
- Rotas novas em `Nora.Api/ApiEndpoints.Community.cs`, chamadas por uma linha em `ApiEndpoints.cs`; DI em
  `NoraApiHost.cs` sob `// Fase 3 — comunicação e jornada`.
- Não alterar `AppointmentDao`, `UnitMetricsDao`, `MetricsService` nem executores existentes de
  `UnitAccessService`.
- Sem push, merge, rebase ou stash. Não tocar em clientes nem em outros docs.

## Checklist

- [x] Migração central 016 e de UBS 013
- [x] Modelos, DAOs, serviços, limitador por conta
- [x] Rotas (municipal, OBS, paciente) e DI
- [x] Checks novos (`CommunityChecks.cs`)
- [x] Documentação em `docs/api.md`
- [x] Build 0/0, checks HTTP, validação PostgreSQL descartável

## Estado

Concluído em 2026-11-15, branch `worktree-agent-a246e4a3e82e3a6e9`.

- Migrações: central `016_announcements.sql`; UBS `013_health_records.sql` (sem depender de 012).
- Rotas em `Nora.Api/ApiEndpoints.Community.cs`; DI em `NoraApiHost.cs` (bloco "Fase 3 — comunicação e jornada").
- Validação real: build Release 0 erros/0 avisos; checks HTTP 30/30; com PostgreSQL 18.6 descartável
  (porta 55622, `-IncludeUnitDatabases`): fluxos 28/28, plataforma 19/19, revisão 17/17 e comunicação e
  Jornada 16/16.
- Decisões de mínimo seguro: ver o relatório do agente e a seção "Comunicação e Jornada" de `docs/api.md`.
- Fora do escopo desta tarefa (outros agentes): clientes, `docs/PENDENCIAS.md`, demais docs.
