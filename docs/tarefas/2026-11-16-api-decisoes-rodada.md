# API: decisões do usuário de 2026-11-16 (fim do vínculo, quem marcou, prévia de remoção, especialidade inativa)

Data: 2026-11-16
Branch: `claude/back-front-integration-analysis-3133b1` (worktree). Tarefa delegada pelo coordenador da integração back x front
(`docs/tarefas/2026-11-11-integracao-back-front.md`, seção "Decisões do usuário", linhas de 2026-11-16). O coordenador faz o commit.
Área: `Nora.Api`, `tests/Nora.Api.Checks`, `docs/api.md`, `docs/PENDENCIAS.md`. Não tocar em `Nora.Desktop` nem em
`tests/Nora.Desktop.Checks` (outro agente trabalha neles em paralelo).

## Skills e comandos invocados

Nenhum. O pedido veio do coordenador por script de fluxo de trabalho.

## O que foi pedido, item por item

1. **Fim do vínculo de Família.** Quando o vínculo termina (responsável encerra, `CloseAsOwnerAsync` para `ended`, ou a equipe
   encerra, `EndAsync`) e o dependente fica sem ninguém que responda por ele (a mesma regra da oferta pendente:
   `FamilyDao.HasAccessAsync` falso, sob o lock 62419813), cancelar também as consultas futuras (`scheduled`/`called`, início depois
   de agora) do CPF do dependente naquela UBS que foram marcadas por um responsável pela Família (evento de agendamento com
   `guardian_id` não nulo). Não pesa no score, fica auditado (autor = quem encerrou; `guardian_id` quando é o responsável) e o horário
   volta à disponibilidade. Falha da UBS: ou o mesmo tratamento das ofertas (depois do commit central, documentando a janela) ou,
   melhor, a UBS antes do commit central, se a ordem de locks permitir (justificar). Se o dependente continua com acesso, nada muda.
   Atualizar "Limites conhecidos" de `docs/api.md`.
2. **C1, quem marcou.** Nas consultas do paciente (`GET /api/patient/units/{unitId}/appointments`, a rota delegada
   `/api/patient/dependents/{dependentId}/units/{unitId}/appointments` e a resposta de agendar), cada consulta ganha
   `bookedByYou: bool` e `bookedByGuardianName: string|null`. Testes: paciente marca para si (true/null); responsável marca para o
   dependente (visão do responsável true/nome dele; visão do dependente com conta false/nome do responsável); equipe marca (false/null).
3. **C2, prévia de remoção da UBS.** `GET /api/patient/units/{unitId}/unlink-preview` → `{ futureAppointments, activeWaitingEntries }`,
   contando exatamente o que a remoção cancela (sem limite de data). Mesmos erros e limites da remoção (sessão, vínculo, 503, limite de
   leitura). Teste: a contagem bate com a remoção, inclusive consulta a mais de 12 meses.
4. **C3, especialidade inativa já vinculada.** No `PUT /api/obs/units/{u}/professionals/{id}`, `specialtyIds` pode manter uma
   especialidade inativa que já está vinculada ao profissional; vínculo novo com inativa e qualquer inativa no `POST` → 400. Testes:
   PUT mantendo → 200 e continua vinculada; PUT adicionando inativa → 400; POST com inativa → 400.

Regras de execução: teste vermelho antes, implementação mínima, teste verde, sem enfraquecer asserções, sem log de depuração, TODO
ou arquivo temporário; não fazer commit, stash, reset nem troca de branch; P-041 vai para Resolvidas com o item 1 verde; P-044 vai
para Resolvidas com as quatro decisões confirmadas/implementadas.

## Objetivo

A API passa a cumprir as quatro decisões, com testes PostgreSQL que provam cada uma, documentação em `docs/api.md` e pendências
atualizadas.

## Restrições

- Não desfazer as alterações não commitadas de rodadas anteriores.
- Não mexer em `Nora.Desktop` nem em `tests/Nora.Desktop.Checks`.
- Contratos C1, C2 e C3 fixos (iguais para API e app).

## Checklist

- [x] Item 1: teste vermelho → implementação → verde
- [x] Item 2 (C1): teste vermelho → implementação → verde
- [x] Item 3 (C2): teste vermelho → implementação → verde
- [x] Item 4 (C3): teste vermelho → implementação → verde
- [x] `dotnet build` Release `-m:1` de `Nora.Api` e `tests/Nora.Api.Checks` com 0 avisos e 0 erros
- [x] `tests/validar-postgres-descartavel.ps1 -IncludeUnitDatabases` na porta 55720 e de novo na 55721
- [x] `docs/api.md` e `docs/PENDENCIAS.md` (P-041 e P-044) atualizados

## Estado

Concluído em 2026-11-16, sem commit (o coordenador faz). Cada item teve o teste vermelho antes da implementação e verde depois.

| Item | Teste | Vermelho antes | Arquivos |
| --- | --- | --- | --- |
| 1. Fim do vínculo | `FamilyChecks.Decisions.cs`: responsável encerra (cancela, origem `system`, autor e `guardian_id`, sem score, horário volta, consulta da equipe e passada intactas); equipe encerra (`called` também); dependente com outro vínculo ou conta própria (nada muda); UBS fora do ar (503, nada muda, repetir conclui) | sim: consulta não era cancelada; UBS fora do ar respondia 200 | `Services/FamilyService.cs`, `DAOs/UnitPatientUnlinkDao.cs`, `DAOs/FamilyDao.cs` (comentário) |
| 2. C1 quem marcou | `FamilyChecks.Decisions.cs`: próprio paciente, responsável, dependente com conta, outro responsável, equipe; resposta de agendar | sim: campo inexistente | `Models/AppointmentModels.cs`, `DAOs/AppointmentDao.cs`, `Services/SchedulingService.cs`, `ApiEndpoints.cs`, `ApiEndpoints.Family.cs` |
| 3. C2 prévia | `FamilyChecks.Decisions.cs`: 3 consultas (uma a mais de 12 meses, uma `called`) e 2 entradas, sem cancelada nem passada; igual ao que a remoção cancela; 401, 403, 503, cadastro desativado, 429 | sim: rota 404 | `ApiEndpoints.cs`, `DAOs/UnitPatientUnlinkDao.cs`, `Models/ApiModels.cs`, `Services/UnitAccessService.cs`, `Services/AccountRateLimiter.cs` |
| 4. C3 especialidade inativa | `SchedulingChecks.cs`: PUT mantendo inativa vinculada → 200 e continua; PUT vinculando inativa nova → 400; POST com inativa → 400; recusas não gravam | sim: PUT mantendo → 400 | `Services/SchedulingService.cs`, `DAOs/SchedulingDao.cs`, `ApiEndpoints.Scheduling.cs` |

Testes ajustados pela decisão (P-041): em `FamilyChecks.Review.cs`, o limite por especialidade depois do fim do vínculo agora mantém outro
responsável vinculado ao dependente (sem ele a consulta é cancelada), e o encerramento com a UBS fora do ar que responde 200 passou a ser
o de um dependente que continua com acesso (o que fica sem ninguém responde 503, testado em `FamilyChecks.Decisions.cs`). O ajudante
"UBS fora do ar" foi para o fixture (`WithUnitDownAsync`). Suíte nova registrada em `Program.cs` (`--family-decisions-only`).

Validações: `dotnet build` Release `-m:1` de `Nora.Api` e de `tests/Nora.Api.Checks` com 0 avisos e 0 erros;
`validar-postgres-descartavel.ps1 -IncludeUnitDatabases` com saída 0 nas portas 55720 e 55721, as duas com HTTP 30/30, PostgreSQL e
fluxos 28/28, plataforma 19/19, revisão 17/17, comunicação e Jornada 16/16, agenda 21/21, Família 21/21, revisão da Família 14/14 e
decisões da Família 7/7.
