# API: login salvo do paciente e período longo dos indicadores — 2026-11-26

Branch de trabalho: `worktree-wf_5640a03a-b07-1` (criada de `origin/main` no commit `8386b04`). Outra equipe redesenha os desktops em outra pasta; esta tarefa não toca `Nora.Desktop`, `OBS-NORA`, `Painel-Municipal` nem `tests/Nora.Desktop.Checks`.

## Skills e comandos invocados

Nenhum comando do usuário. A tarefa chegou por um fluxo de trabalho (workflow) com o pedido do usuário de 26/11/2026 repassado.

## O que foi pedido (palavras do usuário, 26/11/2026)

1. "O login deve ficar salvo no app do paciente como nos desktops: sem marcar nada vale 1 dia; marcando 'manter conectado por 30 dias' vale 30 dias."
2. "Os indicadores do OBS e do Painel Municipal ganham os períodos '5 anos' e 'máximo'."

Parte da API (esta tarefa):

- **A. Sessão do paciente:** 24 h por padrão e 30 dias (o mesmo `KeepSignedInDays`) com `keepSignedIn`, no login por CPF/e-mail e no login com Google (`POST /api/auth/google` passa a aceitar `keepSignedIn`). A resposta continua informando `expiresAt` e o `keepSignedIn` realmente aplicado. Não mudar as regras de desktop, de computador ativado do OBS nem da equipe. Conferir o que depende da duração da sessão do paciente (limpeza de sessões, presença, push, revogação ao desativar, troca de senha) e os testes.
- **B. Indicadores:** janela máxima de 20 anos (7.305 dias) em `GET /api/obs/units/{id}/metrics` e `GET /api/municipal/metrics`. As outras rotas com limite de 366 dias (listas de consultas, bloqueios, disponibilidade) não mudam. Manter o contrato da resposta. Garantir que consulta e resposta continuam leves em 20 anos (ler como a série diária é montada e como os clientes a consomem), medir o tempo com dados de teste e relatar. Mensagens de erro de validação atualizadas.
- **C. Testes** em `tests/Nora.Api.Checks`: paciente sem `keepSignedIn` = 24 h; com = 30 dias; Google idem; desktop e equipe inalterados; janela de 20 anos aceita e 20 anos + 1 dia recusada; limites das outras rotas inalterados.
- **D. Suíte da API** com banco descartável na porta 55461 (`tests/validar-postgres-descartavel.ps1 -IncludeUnitDatabases`).
- **E. Documentação:** `docs/api.md` (sessões e indicadores), `docs/configuracoes.md` se houver opção nova.

## Objetivo

O paciente fica conectado no app por 1 dia (padrão) ou 30 dias (opção), e o OBS e o Painel Municipal podem consultar os indicadores em até 20 anos sem pesar na API nem quebrar nenhum cliente.

## Restrições

- Sem alterar `Nora.Desktop`, `OBS-NORA`, `Painel-Municipal`, `tests/Nora.Desktop.Checks`.
- Desktop (24 h / 30 dias), computador ativado do OBS e equipe (8 h; "manter conectado" só com computador) ficam como estão.
- Limites de 366 dias das outras rotas (consultas, bloqueios, disponibilidade, janela dos alertas) ficam como estão.
- Migrações só aditivas; sem commit com `Co-Authored-By`; sem push; sem mexer em outras branches.

## Checklist

- [x] Ler README, `execucao.md`, `PENDENCIAS.md`, `api.md`, `configuracoes.md`, `app-paciente.md`, `obs-painel-operacional.md`
- [x] A. Sessão do paciente (24 h / `KeepSignedInDays`) no login e no Google (`AuthService`, `SessionDao`, `GoogleAccountService`, `GoogleTokenRequest.KeepSignedIn`)
- [x] A. Dependentes da duração conferidos: tudo é por `expires_at`/`revoked_at`; limpeza (30 dias depois do vencimento), presença, rotas de paciente, troca de senha, sair e desativar conta testados; push é por aparelho; exclusão programada só olha a equipe
- [x] B. Janela de 20 anos (7.305 dias) em `MetricsService`; janela dos alertas separada em 366 (`AlertOptions.MaxWindowDays`); mensagem nova `invalid-period` (código `invalid-input`)
- [x] B. Forma da resposta: série diária já esparsa e os desktops já tratam dia ausente como zero (`ChartSeries.Dense`, só até 400 dias); contrato inalterado. Medido: 20 anos = 823 ms, 398 KiB
- [x] C. Testes: `PatientSessionChecks` (8), `MetricsWindowChecks` (4), uma verificação nova em `GooglePushChecks`; `PlatformChecks` e `ObsDeviceChecks` adaptados
- [x] D. Suíte completa na porta 55461: todas verdes (números em `docs/execucao.md`)
- [x] E. `api.md`, `configuracoes.md`, `execucao.md`, `app-paciente.md`, `obs-painel-operacional.md`, `secretaria.md`, `PENDENCIAS.md` (P-075 desktops, P-076 app)
- [x] Loop de verificação final: pedido conferido item a item; build Release da solução 0 erros (1 aviso antigo, `ApiEndpoints.Photo.cs`, fora desta tarefa)

## Estado

Concluído e verificado, commitado na branch `worktree-wf_5640a03a-b07-1`, sem push. Fora do escopo e registrado: a interface dos desktops (períodos "5 anos" e "máximo", limite de 366 dias na tela e no texto do 400) em P-075 e o app do paciente (enviar `keepSignedIn`, guardar o token) em P-076. Risco conhecido: a Secretaria com 20 anos lê as UBS quatro por vez; numa rede grande o tempo soma o de cada UBS dividido por quatro.
