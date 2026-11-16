# Back-end da Família, desativação na UBS, e-mail da conta e manutenção

Data: 2026-11-16
Branch de trabalho: `claude/back-front-integration-analysis-3133b1` (worktree). Tarefa delegada pelo coordenador da integração
back x front (`docs/tarefas/2026-11-11-integracao-back-front.md`); o coordenador faz o commit.

## Skills e comandos invocados

Nenhum comando ou skill foi invocado pelo usuário. O pedido veio do coordenador por script de fluxo de trabalho.

## O que foi pedido, item por item

1. **Família (Fase 3).** O responsável (paciente com conta) pede pelo app o vínculo com um dependente informando o CPF do
   dependente e a UBS onde ele tem cadastro de referência. A equipe dessa UBS (OBS, administrador ou atendente) lista os pedidos
   pendentes e aprova ou recusa, com confirmação, depois de conferir documentos (registrar quem decidiu e quando; motivo opcional na
   recusa). O responsável pode cancelar pedido pendente; responsável ou equipe podem encerrar um vínculo aprovado. O dependente não
   precisa de conta; a UBS decide a idade (não validar idade). Aprovado, o responsável faz pelo dependente tudo o que o próprio
   paciente faz naquela UBS: consultas (listar, cancelar, agendar pelo catálogo com as mesmas regras de janela e limite por
   especialidade na rede, contado por CPF do dependente), confirmar presença, fila (listar, sair), ofertas (listar, responder),
   Jornada (exames e vacinas) e participação (score). Rotas espelhadas sob
   `/api/patient/dependents/{dependentId}/units/{unitId}/...` (dependentId = id do vínculo aprovado; CPF nunca em URL) e um executor
   novo em `UnitAccessService` que valida sessão do responsável ativa, vínculo aprovado e não encerrado e cadastro ativo do dependente
   na UBS. Cada evento do responsável registra o autor (conta do responsável) e `source='patient'`, com coluna de auditoria em
   migração aditiva. Lista `GET /api/patient/dependents`. Rota de ofertas por dependente leve. Migrações reservadas: central 017,
   UBS 014. Rate limit por conta nas escritas.
2. **Desativação na UBS.** `PATCH /api/obs/units/{unitId}/registry/active` com `isActive=false` cancela também as consultas futuras
   `scheduled`/`called` da UBS como cancelamento da unidade (`source='staff'`, não pesa no score), liberando o horário. Prévia da
   quantidade para a confirmação do OBS. A Vaga Relâmpago continua manual, salvo regra automática já existente (conferir e relatar).
3. **E-mail de referência x conta.** Ao corrigir o e-mail de referência (`PATCH .../registry/email` ou `PUT .../registry`), registrar
   uma sugestão pendente para a conta do paciente (se houver conta com o CPF). Rotas do paciente: ler a sugestão (e-mail mascarado e
   UBS), pedir o código ao endereço novo, confirmar com o código (troca o e-mail; HMAC, uso único, 10 min, 5 tentativas; unicidade
   respeitada, 409) e dispensar. Sem SMTP, 503.
4. **P-036, itens técnicos.** (a) Limpeza periódica de sessões revogadas ou vencidas há mais de 30 dias e de desafios de cadastro e de
   recuperação vencidos há mais de 1 dia, em lotes e sem lock longo. (b) `GET /api/patient/units/{unitId}/lightning-offers` não pode
   escrever: tirar a redistribuição da leitura e provar com teste que a leitura não altera estado.
5. **Testes e documentação.** Arquivo novo `FamilyChecks.cs` com PostgreSQL descartável cobrindo tudo o que está acima; seção nova
   "Família, e-mail da conta e manutenção" em `docs/api.md` e correção das frases falsas da seção "Agenda por UBS".

## Objetivo

API com a Família completa (pedido, decisão, ações delegadas auditadas), desativação que cancela as consultas futuras, troca de
e-mail da conta guiada pela correção da UBS, limpeza periódica e leitura de ofertas sem efeito colateral, tudo testado.

## Restrições

- Só edito `Nora.Api/**`, `tests/Nora.Api.Checks/**`, `docs/api.md` (seções minhas ou tornadas falsas) e este documento.
- Sem commit, stash, reset, checkout de arquivos ou troca de branch (outros agentes editam outras pastas do mesmo worktree).
- Compilar só os projetos da API e dos testes da API. Só clusters PostgreSQL descartáveis.
- Sem dependência nova; sem enfraquecer asserções existentes; sem inventar regra fora da tabela de decisões.

## Checklist

- [x] Migrações central 017 e UBS 014 (aditivas, reaplicadas duas vezes em teste).
- [x] Família: pedido, lista, cancelamento, decisão da equipe, encerramento.
- [x] Executor do responsável e rotas espelhadas (consultas, agendar, confirmar, fila, ofertas, Jornada, participação, disponibilidade).
- [x] Auditoria do responsável nos eventos (`guardian_id`, `confirmed_by`).
- [x] Distribuidor de Vaga Relâmpago considera dependente com vínculo aprovado (o dependente sem conta recebe oferta).
- [x] Desativação cancela consultas futuras (origem staff, sem peso no score) e prévia `POST .../registry/deactivation-preview`.
- [x] Sugestão de e-mail: registro, leitura, código, confirmação, dispensa.
- [x] Limpeza periódica em lotes (`MaintenanceService` e `MaintenanceWorker`).
- [x] Leitura de ofertas sem escrita, com teste.
- [x] `FamilyChecks.cs` verde (21/21) e as suítes anteriores verdes.
- [x] `docs/api.md`: seção "Família, e-mail da conta e manutenção" e frases falsas corrigidas.
- [x] Builds 0 erros e 0 avisos e validações finais rodadas.

## Estado

- 2026-11-16: documento criado; código lido (executores, DAOs da UBS, serviços de cadastro e recuperação, testes de agenda).
  Build base da API 0 erros e 0 avisos.
- 2026-11-16: implementado. Arquivos novos: `Nora.Api/ApiEndpoints.Family.cs`, `DAOs/FamilyDao.cs`, `DAOs/EmailChangeDao.cs`,
  `Services/FamilyService.cs`, `Services/EmailChangeService.cs`, `Services/MaintenanceService.cs`, `Services/FamilyRateLimiter.cs`,
  `FamilyProblems.cs`, `Models/FamilyModels.cs`, migrações `017_family_email_maintenance.sql` (central) e `014_family_delegation.sql` (UBS),
  `tests/Nora.Api.Checks/FamilyChecks.cs` e `FamilyChecks.Fixture.cs`.
- Resultados reais (PostgreSQL 18.6 descartável, porta 55645, `-IncludeUnitDatabases`): build da API e dos testes 0 erros e 0 avisos;
  HTTP 30/30; fluxos 28/28; bancos separados por UBS PASS; plataforma 19/19; revisão 17/17; comunicação e Jornada 16/16; agenda 20/20;
  Família, e-mail e manutenção 21/21.
- Ajuste em teste existente (decisão de 2026-11-16): `WorkflowChecks.cs` exigia que a desativação local **preservasse** a consulta futura; passou a
  exigir que ela fique `cancelled` com evento de origem `staff` do autor (mesma contagem de pendências canceladas e auditadas).
- Decisões minhas para o coordenador conferir: ver o relatório final desta tarefa e a seção de `docs/api.md`.

## Correções da revisão crítica (2026-11-16, segundo passo)

Pedido do coordenador: corrigir dez achados da revisão confirmados por um segundo revisor, cada um com teste de regressão vermelho antes
da correção quando houver ponto de teste correto. Regressões em `tests/Nora.Api.Checks/FamilyChecks.Review.cs` (`--family-review-only`,
também chamado ao fim da suíte completa), com o mesmo fixture da Família.

| # | Achado | Correção |
| --- | --- | --- |
| 1 e 4 | Agendamento pela própria conta ignora UBS de vínculos de Família | `PatientBookAsync` usa `FamilyService.NetworkUnitsAsync` (mesma lista do responsável) |
| 2 e 5 | Fim do vínculo tira a UBS do limite, mas a consulta continua | `FamilyDao.NetworkUnitIdsAsync` lê vínculos `approved` e `ended` (decisão minha: cancelar as consultas seria regra nova) |
| 3 | `confirmed_by` vazio na confirmação do próprio paciente | a rota do paciente passa a conta como autor |
| 6 | Sugestão de e-mail perdida se o banco central falha | a sugestão é gravada na transação central ANTES de a UBS gravar o e-mail novo |
| 7 | Troca de e-mail não invalida código de recuperação de senha | `ConfirmAsync` consome os códigos abertos da conta na mesma transação |
| 8 | Recusar/encerrar pela equipe responde 503 depois de gravar | nome lido com `TryNamesAsync` (tolerante) depois do commit |
| 9 | Consulta periódica de ofertas: 2 conexões por dependente com a transação central aberta | transação fechada antes das UBS; uma consulta por UBS (`ListPendingForCpfsAsync`) |
| 10 | Oferta pendente prende a vaga quando o vínculo acaba | encerramento (responsável ou equipe) cancela oferta e entrada `offered`, reabre e redistribui a vaga, se o CPF ficou sem quem responda |

Estado: concluído e verificado. Regressões vermelhas antes das correções (2 de 11 passavam; as demais falharam pelo motivo esperado) e 11/11 verdes depois.
Validação completa (PostgreSQL 18.6 descartável, porta 55633, `-IncludeUnitDatabases`): builds 0 erros e 0 avisos; HTTP 30/30; fluxos 28/28; bancos separados PASS;
plataforma 19/19; revisão 17/17; comunicação e Jornada 16/16; agenda 20/20; Família, e-mail e manutenção 21/21; revisão da Família 11/11.
