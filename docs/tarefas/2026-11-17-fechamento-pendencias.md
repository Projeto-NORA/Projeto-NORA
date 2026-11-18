# Fechamento das pendências do chat (2026-11-17, depois da apresentação)

## Skills e comandos invocados

- `/aura-retomar-execucao:aura` (protocolo, diagnóstico, loop e graphify), duas vezes: "finalize todas as pendências em aberto desse chat, qualquer decisão pendente me faça perguntas" e "continue, finalizando todas as pendências abertas desse chat para eu poder fechar ele".

## O que foi pedido

1. Finalizar todas as pendências abertas deste chat.
2. Perguntar durante o trabalho qualquer decisão pendente.
3. Deixar o chat em condição de ser fechado.

## Decisões do usuário (2026-11-17, à noite)

| Assunto | Decisão |
| --- | --- |
| P-053, CPF das contas de equipe | Tirar. As contas de equipe que já existem perdem o CPF (apagado); a equipe (OBS e Secretaria) entra só por e-mail e senha. Pacientes continuam com CPF. O cadastro inicial continua pelo terminal (`--provision`), sem perguntar CPF. |
| P-051 e score | Não há limite de ofertas sem resposta (P-051 encerrada; deixar a oferta vencer não baixa o score). O usuário quer o **score estilo Serasa**: pontos de **0 a 1000** a partir do cálculo de comparecimento que já existe (faltas e cancelamentos em cima da hora derrubam), com faixas **Baixo (0–299), Regular (300–599), Bom (600–799) e Excelente (800–1000)**. Aparece no **OBS e no app**; o Painel Municipal não precisa. |
| Efeito do score | **Prioridade na Vaga Relâmpago**: entre quem espera a mesma especialidade, quem tem score maior recebe a oferta antes; empate segue a ordem de entrada na fila. |
| P-052, otimizações | Fazer as três: P3 (lista de cadastro do OBS sem a 2ª conexão central), P4 (paginar o histórico das listas de fila e de vagas do OBS) e P5 (índice por CPF na fila, migração de UBS 017). |
| P-054, dados fictícios | Cancelada: a apresentação já passou e não precisa mais. |
| P-049, hospedagem | Fazer agora, no **Render + Neon**, com a IA guiando no navegador do app; o usuário entra nas contas e cola senhas e chaves (a IA não cria conta nem digita segredo). |
| P-055, "marcar a qualquer hora" | Validar por completo e publicar na `main`. |

## Restrições

- Nenhuma credencial no Git; a IA não digita senha, chave nem token em formulário de serviço externo.
- Merge e push na `main` autorizados para este fechamento (pedido do usuário de deixar tudo finalizado).
- Não mexer na fixture de outra sessão (porta 62489, PostgreSQL 55441).

## Checklist

- [x] Contrato fixo (score em pontos, prioridade, P-053, P4).
- [x] Servidor: P-053, score em pontos e prioridade, P3, P4, P5, com testes.
- [x] Desktop: Secretaria sem CPF na equipe, entrada por e-mail, score em pontos no OBS, histórico paginado na Fila e na Vaga Relâmpago.
- [x] App: score em pontos e faixas, textos da prioridade.
- [x] Scripts e roteiro sem CPF da equipe.
- [x] Revisão em loop (volta de correção dos achados confirmados em 2026-11-17, abaixo).
- [ ] Validação completa (API, desktop, E2E, Android) e merge + push na `main`: API, desktop, E2E desktop com banco (184/184) e Android (testes locais, lint e instrumentados 46/48 com 2 pulados por assumption, conforme `execucao.md`) rodaram; falta só o merge na `main` e a publicação (P-055).
- [ ] Hospedagem no Render + Neon, guiada no navegador (P-049, aberta).
- [x] PENDENCIAS atualizado (P-051, P-052, P-053, P-054 resolvidas ou encerradas; P-055 e P-049 abertas).

## Estado

- Decisões implementadas (HEAD `3dab894`) e, em 2026-11-17, a volta de correção dos achados confirmados da revisão:
  - `UnitWaitingListDao.ListForObsAsync` e `UnitLightningDao.ListAsync` leem ativas, histórico e pontos num só retrato (transação `REPEATABLE READ`): uma entrada ou vaga que muda de situação entre as consultas não volta nas duas listas.
  - `SchedulingService`: teto de 20 anos (`MaxScheduleHorizonYears`) em disponibilidade e marcação (equipe e paciente); intervalo em 31/12/9999 responde 400, não 500. Teste na suíte da agenda (`SchedulingChecks`).
  - Desktop: `CanLoadMoreQueueHistory` e `CanLoadMoreSlotsHistory` entraram em `BusyDependents` e `SessionDependents` (o botão não fica desabilitado até o ciclo de 20 s; teste vermelho antes da correção, verde depois); `HistoryPager` mantém como "Encerrada" o item que estava ativo e saiu da primeira página (teste no pager).
  - App: "Escolher outra data" seleciona o dia escolhido e avisa quando ele não tem horário (`shownDay`, `isWantedDayEmpty`; teste local `theDayPickedInThePickerIsTheDayShownAndAnEmptyPickedDayIsReported`).
  - Documentação: versões de migração (central 022, UBS 017), prioridade por pontos, login da equipe só por e-mail, roteiro de recuperação do Render (log do filtro de host, teste de IP por comportamento), Blueprint `render.yaml`, ordem dos itens paginados, `--demo-bootstrap`, P-052 e P-053 em Resolvidas.
- Resultados reais desta volta: `NORA.slnx` Release 0 erros e 0 avisos; suíte `validar-postgres-descartavel.ps1 -IncludeUnitDatabases` (porta 55810): HTTP 34/34, fluxos 28/28, plataforma 19/19, revisão 17/17, comunicação e Jornada 16/16, agenda 21/21, Família 21/21 + 14/14 + 7/7 + 5/5, Google e push 15/15, computadores do OBS 13/13, chamar próximo 8/8, serviços e horários 7/7, conflito entre UBS 7/7, aprovação das vagas 8/8, caixa de saída 5/5, volta à fila 5/5, desempenho 6/6, fechamento 16/16, bancos por UBS PASS (o script avisou no fim que o cluster não confirmou o encerramento; o cluster foi parado à mão); desktop `Nora.Desktop.Checks` 181/181; Android `testDebugUnitTest` 294 testes, 0 falhas, `lintDebug` 0 erros e 7 avisos (nenhum nos arquivos alterados); `gradlew --stop` feito.
- Falta: merge e push na `main` e a hospedagem (P-049). O E2E desktop com banco (184/184) e os instrumentados no emulador (46 aprovados, 2 pulados por assumption) rodaram em 2026-11-18, ver `execucao.md`.

## Correções da revisão (2026-11-18)

Achados confirmados da revisão, corrigidos pela causa raiz:

- **Teto de 20 anos no app.** O seletor de data ia até 2100 e "Próxima semana" não tinha fim, mas o servidor recusa com 400 consultas além de 20 anos. `BookingLogic.kt` ganhou `BOOKING_HORIZON_YEARS` e `bookingHorizon` (20 anos menos um dia de folga para o relógio); `bookingWeek` corta a última semana no teto e não devolve semana além dele, `isDayBookable` recusa dias a partir dele, o seletor usa o ano do teto e "Próxima semana" fica desativada na última semana. Teste `weeksAndDatesStopAtTheServerHorizon` (não compilava antes da correção).
- **Score somado x ordem da Vaga Relâmpago.** Com mais de uma UBS, `attendancePriorityText` acrescenta que a ordem usa o score de cada UBS (detalhe por UBS), não o número somado; teste em `AttendanceLogicTest`.
- **Documentação.** README e `api.md` agora mandam migrar o central até a 022 e as UBS até a 017 (README também com a data de 2026-11-17); teto de 20 anos em `api.md`, `app-paciente.md`, `arquitetura.md` e `integracao-back-front.md` (a frase de 24 h a 90 dias ficou marcada como substituída); `hospedagem.md` diz "quatro valores" no Blueprint, que `ALLOWED_HOSTS` é acrescentado depois e quais seções continuam obrigatórias (P-049 ajustada); `configuracoes.md` não diz mais que os desktops aceitam CPF.
- Resultado: Android `testDebugUnitTest` 295 testes, 0 falhas; `lintDebug` 0 erros e 7 avisos (os mesmos de antes, nenhum nos arquivos alterados); `gradlew --stop` feito. Nada do servidor nem do desktop foi alterado nesta volta, então API e desktop não foram reexecutados.

## Segunda volta da revisão (2026-11-18)

Achados confirmados, corrigidos pela causa raiz:

- **Pontos do score em empate de meio ponto.** `AttendanceScore.Points(completed, missed, weight)` calcula em `decimal` sobre a razão exata (201/400 dá 503 e 203/400 dá 508, que em `double` davam 502 e 507); `PatientTally.Points` (distribuição, prévia, `offerOrder` e score da ficha) usa essa sobrecarga; `Points(double?)` converte para `decimal` antes de multiplicar. Teste na suíte HTTP ("Score em pontos").
- **`--demo-bootstrap` sem `acesso-demo.txt`.** Se o arquivo (ou a senha nele) sumiu e alguma conta de demonstração já existe, o preparo recusa com mensagem clara, antes de criar qualquer coisa e sem tocar no arquivo; o arquivo só é gravado quando a senha é nova (as três contas acabam de ser criadas com ela). Não rodado contra o banco da demo do usuário (porta 55432, fora do escopo): a recusa foi conferida só por leitura e compilação.
- **Fila do desktop: ordenação das colunas.** `UnitWaitingEntry.ScoreSort` e `OfferSort` (numéricas) e `SortMemberPath` nas colunas Score e Ordem de oferta; verificação no check "Fila OBS".
- **App: especialidade já escolhida.** Tocar de novo nela também zera `chosenDay` (`BookingUi.kt`), como os botões de semana. Sem teste de unidade: é estado de composição, sem costura.
- **Entrada do desktop com e-mail sem ponto no domínio.** `BrazilianDataValidator.IsLoginEmail` (formato da API, sem exigir ponto) passa a validar a tela de entrada; `IsValidEmail` (com ponto) continua nos cadastros. Teste: o Painel Municipal entra com `admin@localhost` no check "Entrada por e-mail".
- **Documentação.** `api.md`, `arquitetura.md` e `integracao-back-front.md` dizem "ordem de oferta" (mais pontos primeiro) no Chamar próximo; README, `api.md`, `arquitetura.md` e `requisitos-funcionais.md` não tratam mais P-012, P-014 e P-015 como abertas (os parágrafos de 2026-10-08 ficaram marcados como históricos); `execucao.md` lista `--closing-only` e `--demo-bootstrap` e descreve tudo que a suíte sem argumentos cobre.
- **Resultado:** build Release de `NORA.slnx` 0 erros e 0 avisos; suíte da API (`validar-postgres-descartavel.ps1 -IncludeUnitDatabases`, porta 55812) HTTP 34/34, fluxos 28/28, plataforma 19/19, revisão 17/17, comunicação 16/16, agenda 21/21, Família 21 + 14 + 7 + 5, Google e push 15/15, computadores 13/13, chamar próximo 8/8, serviços 7/7, conflito entre UBS 7/7, aprovação 8/8, e-mail 5/5, volta à fila 5/5, desempenho 6/6, fechamento 16/16; desktop `Nora.Desktop.Checks` 181/181; Android `testDebugUnitTest` 295 testes, 0 falhas, `lintDebug` 0 erros e 7 avisos; `gradlew --stop` feito.

## Volta 4 da revisão (2026-11-18)

Achado confirmado, corrigido pela causa raiz:

- **Variável com ponto no nome descartada pelo `ENTRYPOINT`.** O guia de recuperação do host (`docs/hospedagem.md`) manda criar `Logging__LogLevel__Microsoft.AspNetCore.HostFiltering=Information`, mas o `Dockerfile` iniciava a API por `/bin/sh -c`, e na imagem Debian do aspnet o `sh` é o `dash`, que não importa variáveis de ambiente cujo nome não seja um identificador de shell; a variável nunca chegaria ao `dotnet`. O `ENTRYPOINT` agora usa `/bin/bash -c` (o bash faz parte da imagem Debian, não é imagem chiseled), que repassa o nome com ponto. Comentário no `Dockerfile`, parágrafo "Sobre o endereço e o host" de `hospedagem.md` (com a advertência de não voltar ao `sh` e o caminho alternativo sem a variável: `curl -i` no `/health/live` e `ALLOWED_HOSTS` com o domínio exato) e P-049 em `docs/PENDENCIAS.md` atualizados.
- **Verificação (sem Docker nesta máquina, não há costura de teste no repositório):** vermelho/verde com os dois shells do Git Bash. `env "Logging__LogLevel__Microsoft.AspNetCore.HostFiltering=Information" PLAIN_VAR=1 dash -c 'exec env'` imprime só `PLAIN_VAR`; com `bash -c` imprime as duas. A linha do `ENTRYPOINT` executada com bash e `exec env` entrega `Urls=http://0.0.0.0:10000`, `AllowedHosts=x.onrender.com` (de `RENDER_EXTERNAL_HOSTNAME`) e a variável com ponto. A imagem em si continua sem ser construída (P-049).
- **Suítes:** só `Dockerfile` e documentação mudaram (nenhum código de servidor, desktop ou app), então build, API, desktop e Android não foram reexecutados nesta volta; valem os resultados da volta anterior.


## Volta 5 da revisão (2026-11-18)

Achado confirmado, corrigido pela causa raiz:

- **Chamar próximo sem o teto de 20 anos (500 em 31/12/9999).** O teto valia só em disponibilidade e marcação; `GET /obs/units/{id}/waiting-list/next?startsAt=9999-12-31...` e o `POST call-next` chegavam a `FreeSlotAsync` e o laço do `SlotEngine` estourava `DateOnly.AddDays(1)`. `SchedulingService.BeyondHorizon` passou a `internal` e `CallNextService` (`PreviewAsync` e `CallAsync`) recusa com `ApiInputException` (400) um `startsAt` além do teto; o laço do `SlotEngine.Generate` passou a andar pelo `DayNumber` (inteiro), sem `AddDays` no último dia possível. Teste de regressão em `CallNextChecks` (check de validação: prévia e chamada com `startsAt` em 31/12/9999 → 400): vermelho antes (a prévia respondia 500 e o check falhava), verde depois. `api.md` registra o 400.
- **Resultado:** build Release de `NORA.slnx` 0 erros e 0 avisos; `validar-postgres-descartavel.ps1 -IncludeUnitDatabases` (porta 55831) exit 0, 273 verificações PASS e nenhuma FAIL (inclui `--call-next` 8/8 e fechamento 16/16); desktop `Nora.Desktop.Checks` 181/181. Android não reexecutado (nenhum arquivo do app mudou).

## Volta 6 da revisão (2026-11-18)

Achado confirmado, corrigido pela causa raiz:

- **Carregar mais histórico levava a tabela ao topo (fila e vagas do OBS).** `ShowQueueList` e `ShowSlotList` chamavam `Replace`, que só preserva a rolagem com contagem igual; com a lista maior ele fazia `Clear()` e `Add` item a item (Reset no DataGrid). Correção em `Nora.Desktop/ViewModels/WorkspaceViewModel.UnitData.cs`: novo `Grow`, que troca só as linhas que mudaram e acrescenta o resto com `Add`, sem `Clear`; `ShowQueueList` e `ShowSlotList` ganharam o parâmetro `appended`, passado como `true` apenas por `LoadMoreQueueHistoryAsync` e `LoadMoreSlotsHistoryAsync` (`WorkspaceViewModel.Lightning.cs`), onde `HistoryPager.ApplyOlder` garante que os itens existentes continuam nos mesmos índices. O ciclo de 20 s continua usando `Replace`.
- **Regressão:** em `tests/Nora.Desktop.Checks/ScoreHistoryChecks.cs` as verificações da fila e das vagas gravam as ações da `CollectionChanged` durante o clique em Carregar mais histórico e exigem `Add` e nenhum `Reset`. Vermelho antes da correção (`Reset,Add,Add,...` nas duas, 179/181) e verde depois (181/181).
- **Resultado:** build Release de `NORA.slnx` 0 erros e 0 avisos; desktop `Nora.Desktop.Checks` 181/181. Só o desktop mudou (nenhum código de servidor ou app), então a suíte do PostgreSQL e a do Android não foram reexecutadas; valem os resultados da volta 5.

## Volta 7 da revisão (2026-11-18)

Quatro achados de documentação, corrigidos pela causa raiz (só texto; nenhum código mudou, então não há teste de regressão e as suítes de código não foram reexecutadas, valem as da volta 6):

- **README e P-055 desatualizados.** Diziam que o E2E com banco e os instrumentados ainda faltavam, mas `execucao.md` registra 184/184 e 46 de 48 (2 pulados por assumption). README, P-055, Estado e checklist desta tarefa agora dizem que só falta o merge e a publicação.
- **Ressalva velha em `execucao.md`.** A linha de versões de banco dizia que a volta 2 da revisão não rodou; P-050 está resolvida. Frase trocada.
- **Plano B com Nginx do host (`hospedagem.md`).** Faltavam `Host`, `X-Forwarded-For` e `X-Forwarded-Proto` (sem o `Host` o `ALLOWED_HOSTS` devolve 400; sem o `X-Forwarded-For` todos dividem o limite de login) e a porta da API em `127.0.0.1`. Acrescentados, com o requisito de o IP do proxy cair em `KnownNetworks`.
- **Túnel SSH do plano B.** O PostgreSQL do compose não publicava porta, então `ssh -L 5433:localhost:5432` não tinha onde chegar. O passo 2 manda publicar `127.0.0.1:5432:5432` (só loopback) e o passo 4 ganhou o comando dentro do contêiner (`docker compose exec api dotnet Nora.Api.dll --migrate`/`--provision`) como alternativa.
