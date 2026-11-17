# 2026-11-17 — R-030 (volta à fila), otimização P1/P2 e achados da volta 2

Prazo: apresentação às 19h. Só `Nora.Api`, `Nora.Desktop`, `tests/` e `docs/` (não mexer em `APPNORA/`, `scripts/empacotar-clientes.ps1`, `scripts/demo-local.ps1` nem em `docs/PENDENCIAS.md`). Sem commit nem stash.

## Pedido (decisões do usuário de 2026-11-17), em ordem

1. **R-030** — Vaga Relâmpago: recusar a oferta ou deixá-la vencer (10 min) devolve a entrada à fila na mesma posição (`waiting`, `entered_at` intacto) em vez de `completed`. Só sai se aceitar, cancelar a entrada ou a UBS tirar. A mesma pessoa não recebe de novo a oferta da MESMA vaga que recusou/deixou vencer. Vale para a oferta automática e para o Chamar próximo. Atualizar testes que esperavam `completed`, criar regressão, `docs/api.md`; textos do app/OBS que digam que a pessoa sai da fila (APPNORA só listar arquivo:linha).
2. **Otimização** — P1: pré-checagem só de leitura que pula o passe do `UnitLightningDispatcher` quando não há nada a fazer. P2: `FOR SHARE` em vez de `FOR UPDATE` na concessão/vínculo das rotas de leitura (`UnitAccessService`), só se mantiver a garantia da revogação. Medir antes e depois.
3. **Achados da revisão volta 2** — #3 `ObsDeviceStore` só descarta `device.dat` em dado inválido (não em IOException/UnauthorizedAccessException); #4 confirmação do Chamar próximo com a verdade (prévia com horário usando o mesmo NoOverlap); #5 repetir uma vez em 40P01 (revogação, geração de código, ativação).
4. Fim: build de `NORA.slnx` (0 erros, 0 avisos), suíte da API completa, `Nora.Desktop.Checks`, E2E desktop. Atualizar `docs/api.md`, `docs/arquitetura.md`, `docs/obs-painel-operacional.md` e criar `docs/desempenho.md`.

## Checklist

- [x] R-030 código (UnitLightningDao, UnitLightningDispatcher, LightningEligibility)
- [x] R-030 testes ajustados + regressão (`QueueReturnChecks` 5/5)
- [x] P1 + medição (12 -> 2 comandos por passe ocioso; 300 -> 0 XIDs na central)
- [x] P2 + medição e testes de bloqueio (FOR SHARE; revogação, escrita e sair da UBS continuam esperando a leitura)
- [x] #3 ObsDeviceStore (2 testes novos)
- [x] #4 prévia do Chamar próximo com o horário (API + Desktop, testes nos dois)
- [x] #5 repetição em 40P01 (teste da costura `RetryOnDeadlockAsync`)
- [x] Build 0/0, suíte da API completa (script oficial), Desktop.Checks 173/173, E2E desktop 176/176
- [x] Docs: api, arquitetura, obs-painel-operacional, requisitos-funcionais, app-paciente, execucao e `docs/desempenho.md` (novo)

## Estado

Tudo feito e verificado (resultados em `docs/execucao.md`, seção "Volta 2"). Nada desfeito. Não foi editado `docs/PENDENCIAS.md`, `APPNORA/`, `scripts/empacotar-clientes.ps1` nem `scripts/demo-local.ps1`. Textos do APPNORA que precisam mudar (R-030): `APPNORA/app/src/main/java/com/example/app_nora/api/PatientController.kt:437` ("Oferta recusada.") e `APPNORA/app/src/main/java/com/example/app_nora/ui/OffersUi.kt:128-131` e `:135` (prazo encerrado / "Ao aceitar..."), para dizer que a pessoa continua na fila.