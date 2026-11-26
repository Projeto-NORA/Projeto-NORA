# Revisão crítica: login salvo do paciente e período longo dos indicadores — 2026-11-26

Branch de trabalho: `worktree-wf_5640a03a-b07-5` (criada de `origin/main` em `8386b04` e avançada por fast-forward até `49b24b7`, o trabalho da branch `worktree-wf_5640a03a-b07-1`). Esta tarefa não toca `Nora.Desktop`, `OBS-NORA`, `Painel-Municipal` nem `tests/Nora.Desktop.Checks`.

## Skills e comandos invocados

Nenhum comando do usuário. A tarefa chegou por um fluxo de trabalho (workflow); a mensagem do usuário repassada foi "pode escrever você mesmo a função, continue."

## O que foi pedido

1. Trazer o trabalho de `worktree-wf_5640a03a-b07-1` com `git merge --ff-only` (parar se não der fast-forward).
2. Ler o diff inteiro (`git diff 8386b04..HEAD`) e os documentos do assunto (`docs/api.md`, `docs/configuracoes.md`, `docs/app-paciente.md`, `docs/obs-painel-operacional.md`).
3. Conferir cada exigência do pedido original (A sessão do paciente, B indicadores de 20 anos, C testes, D suíte na porta 55461, E documentação): feita, pela metade ou faltando.
4. Procurar defeitos reais: segurança, perda de dado, concorrência, casos extremos, regressão em quem consome, teste afrouxado.
5. Rodar as validações nas portas do pedido somadas de 5 (55466) e comparar com o relato.
6. Corrigir a causa de tudo o que for achado e repetir até uma volta sem achados (no máximo 5 voltas no mesmo problema).
7. Commit na própria branch, sem push e sem `Co-Authored-By`; informar a branch e o hash final.

## Objetivo

O trabalho da sessão do paciente (24 h ou 30 dias) e da janela de 20 anos dos indicadores conferido contra o pedido, com os defeitos achados corrigidos e a suíte da API verde de novo.

## Restrições

- Não alterar `Nora.Desktop`, `OBS-NORA`, `Painel-Municipal` nem `tests/Nora.Desktop.Checks` (outra equipe redesenha os desktops).
- Desktop, computador ativado do OBS e equipe com as regras de sempre; as outras rotas por período com os limites de sempre.
- Migrações só aditivas; sem segredo em arquivo; sem push; sem mexer em outras branches.

## Checklist

- [x] Fast-forward até `49b24b7`
- [x] Diff e documentos lidos
- [x] Exigências A a E conferidas contra o código
- [x] Defeitos corrigidos
- [x] Build Release e suíte da API na porta 55466
- [x] Documentação e pendências atualizadas
- [x] Commit na branch

## Conferência do pedido

- **A. Sessão do paciente:** feita. `AuthService.CreateSessionAsync` dá 24 h ao paciente e `SessionDao.CreateAsync` aplica `keepSignedIn` ao escopo `patient` (`KeepSignedInDays`, 30 por padrão); `POST /api/auth/google` aceita `keepSignedIn`. Desktop, computador do OBS e equipe com as regras de antes. Limpeza, presença, troca de senha, sair, redefinir e desativar a conta só olham `expires_at`/`revoked_at`; o push é por aparelho.
- **B. Indicadores:** feita. `MetricsService.MaxWindowDays = 7305`; alertas com teto próprio de 366; consultas, bloqueios e disponibilidade inalterados; série diária esparsa, que os desktops já completam com zero até 400 dias (`ChartSeries.Dense`) e o app não lê.
- **C. Testes:** feitos; nenhum teste foi afrouxado (`PlatformChecks` e `ObsDeviceChecks` passaram a esperar o comportamento novo).
- **D. Suíte:** reproduzida na porta 55466, toda verde, números iguais aos do relato.
- **E. Documentação:** feita; dois textos corrigidos nesta revisão.

## Achados e correções

1. **500 com datas no fim do calendário** (`GET /api/obs/units/{id}/metrics` e `GET /api/municipal/metrics` com `to` em 31/12/9999): `SchedulingDao.LoadAsync` soma 240 min a `to` e estoura o limite de `DateTimeOffset`. Já existia com 366 dias. Correção: `MetricsService.ValidateWindow` recusa com o 400 "Período inválido" a janela que começa antes de 02/01/0001 ou termina depois de 30/12/9999. Teste novo em `MetricsWindowChecks`, vermelho com o código anterior (500 nas duas rotas).
2. **Texto errado em `docs/api.md`:** dizia que, sem `from` e `to`, a resposta trazia o título "Período inválido". Na verdade a leitura dos parâmetros recusa antes, com o 400 genérico "Dados inválidos". O texto foi corrigido e o teste agora confere o título.
3. **Cobertura:** a disponibilidade (92 dias, OBS e paciente) entrou em `MetricsWindowChecks` entre as rotas que não mudaram. Antes só `SchedulingChecks` a cobria.
4. **Registrado, sem mudar código:** P-077 (a capacidade projeta a grade de hoje sobre 5 ou 20 anos, e a ocupação fica subestimada; mudar isso altera o número de qualquer período, então a decisão é do usuário). P-075 ganhou o risco do tempo limite de 20 s do `NoraApiClient` numa rede grande. P-076 ganhou a nota de que o app está sendo feito nas branches `-b07-2`/`-b07-4`.

## Estado

Concluído e verificado. Build Release com 0 erros. Suíte completa na porta 55466 com saída 0, e `--metrics-window-only` 5/5 na porta 55477 depois do último ajuste do teste; os números estão em `docs/execucao.md`. Sem push. Bancos descartáveis parados, e nenhum processo desta pasta ficou rodando.
