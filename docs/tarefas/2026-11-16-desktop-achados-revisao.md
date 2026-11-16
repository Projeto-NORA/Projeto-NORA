# Achados da revisão no desktop (Fase 3)

Parte da retomada `/aura-retomar-execucao:aura` ("continue, finalizando todas as pendências"), dentro da tarefa
[2026-11-11-integracao-back-front.md](2026-11-11-integracao-back-front.md). Esta etapa cuida só do desktop
(`Nora.Desktop`, `tests/Nora.Desktop.Checks`, `docs/obs-painel-operacional.md` e `docs/secretaria.md`).

## Skills e comandos

- `/aura-retomar-execucao:aura` com o pedido "continue, finalizando todas as pendências, me faça perguntas durante a requisição".

## O que foi pedido

Para cada achado: um teste que reproduz o defeito e fica vermelho, a correção da causa com a menor mudança e o teste verde.
Sem commit, stash, reset nem troca de branch. Sem mexer em `Nora.Api` (fica com o coordenador). Sem rodar o E2E com banco.

1. Família no OBS mostra no máximo 200 vínculos sem aviso: seguir as páginas do contrato
   `{ links, cursor, hasMore }` com `after=<cursor>`, com teto de segurança e aviso visível.
2. Editar profissional com especialidade desativada pela Secretaria: a edição continua possível mantendo a especialidade
   já gravada; a mensagem não manda apagar a grade. Contrato aprovado pelo coordenador: o PUT pode manter especialidade
   inativa já vinculada; vínculo novo e POST continuam exigindo especialidade ativa. A API muda na próxima rodada.
3. Confirmação de aprovação da Família não pode prometer "entrar na fila".
4. Alertas da rede filtrados por UBS sem base (ou região só com elas) não podem dizer que nenhum critério passou do limite.
5. Falhas dos alertas da rede, do cadastro da Capacidade e de MyAnnouncementsState mandam usar um botão que não existe:
   dar a essas páginas uma forma real de tentar de novo e apontar o texto para ela.
6. Texto do painel de exportação dos Relatórios diz que pacientes e fila são da rede mesmo com uma UBS escolhida.
7. Busca de horários que falha depois de trocar profissional/especialidade deixa o horário do profissional anterior
   escolhido e "Agendar" marca com ele.
8. Exames e vacinas: leitura que falha depois de alternar "Mostrar anulados" deixa a lista do filtro anterior sem aviso
   e afirma "nem anulado".
9. Família usa um só indicador de falha para as duas leituras.

## Objetivo

Telas do OBS e da Secretaria que dizem a verdade sobre o que foi lido e não agem sobre dados de outra escolha.

## Restrições

- Não enfraquecer asserções existentes; não deixar log, TODO nem arquivo temporário.
- Não mexer em `Nora.Api`.

## Checklist

- [x] 1 · teste vermelho → correção → verde ("Revisão 2.1")
- [x] 2 · teste vermelho → correção → verde ("Revisão 2.2"; a API do `PUT` fica com o coordenador)
- [x] 3 · teste vermelho → correção → verde ("Revisão 2.3")
- [x] 4 · teste vermelho → correção → verde ("Revisão 2.4")
- [x] 5 · teste vermelho → correção → verde ("Revisão 2.5")
- [x] 6 · teste vermelho → correção → verde ("Revisão 2.6")
- [x] 7 · teste vermelho → correção → verde ("Revisão 2.7")
- [x] 8 · teste vermelho → correção → verde ("Revisão 2.8")
- [x] 9 · teste vermelho → correção → verde ("Revisão 2.9")
- [x] `docs/obs-painel-operacional.md` e `docs/secretaria.md` atualizados
- [x] build Release, `Nora.Desktop.Checks` completo, `tests/desktop-startup.ps1`, prévias claro e escuro conferidas

## Estado

Concluído em 2026-11-16, sem commit.

- As verificações ficam em `tests/Nora.Desktop.Checks/ReviewLoop2Checks.cs`, chamadas por `RunReviewLoop2Checks` logo depois de
  `RunReviewFixesChecks` em `ApiClientChecks.cs`. Rodadas antes das correções (com `SummaryExportNote` ainda devolvendo o texto
  antigo, só para compilar): 0/9. Depois: 9/9. As asserções acrescentadas depois (2.2: especialidade desativada desmarcada não volta
  a ser marcada; 2.7: Agendar recusa horário de outro profissional) ficaram vermelhas com a proteção retirada e verdes com ela.
- Fixtures: paginação da Família por cursor base64 com `FamilyPageCap`, `FailFamilyStatuses` e `FamilyLinkQueries`;
  `FailMyAnnouncements`; o `PUT` de profissional aceita especialidade inativa já vinculada; `FailCatalogUnits` também na rota municipal.
- 1: `NoraApiClient.FamilyLinksAsync` segue `after=<cursor>` até `hasMore` falso, teto `FamilyPageMax` = 50 páginas, e devolve
  `FamilyLinkPages(Links, Truncated)`; a aba avisa "mostrando os N mais antigos" e o resumo diz "N ou mais".
- 2: `SpecialtyChoice` mostra a especialidade inativa já vinculada com "(desativada pela Secretaria)", `CanCheck` impede remarcá-la;
  `RebuildProfessionalChoices` oferece ativas + inativas do profissional em edição; o 409 de `/professionals/` não manda remover a grade.
- 3: confirmação e cabeçalho da Família trocam "entrar na fila" por "ver a fila e sair dela" (o app só lê e cancela a fila do dependente).
- 4: `LoadNetworkAlertsCoreAsync` conta UBS avaliadas e sem base no filtro no momento da leitura; só UBS sem base = "não foram avaliados".
- 5: botão "Atualizar alertas" (`RefreshNetworkAlertsAsync`) nos alertas da rede, "Ler o cadastro de novo" (`RetryCapacityCatalog`) na
  Capacidade; textos de falha de alertas, Capacidade e Meus comunicados apontam para esses botões e para "Atualizar lista".
- 6: `SummaryExportNote` depende de `appliedSummaryUnitId`/`AppliedSummaryUnitLabel`, ligado ao painel de exportação.
- 7: `ReloadBookingSlots` zera lista e horário quando o par (especialidade, profissional) muda; `CreateCatalogAppointmentAsync` recusa
  horário de outro profissional ou especialidade.
- 8: `HealthRecordsViewModel` guarda `loadedWithVoided`; falha com filtro diferente diz que a lista é a anterior e se inclui anulados.
- 9: Família com `familyPendingFailed`/`familyApprovedFailed`; a mensagem nomeia a leitura que falhou.
- Validação: build Release 0 erros e 0 avisos; `Nora.Desktop.Checks` 149/149 (eram 140 + 9 novas); `tests/desktop-startup.ps1` 2/2;
  prévias `revisao2-*` em `.build/previews-loop2` conferidas no claro e no escuro, sem corte nem sobreposição. E2E com banco não rodado
  (fica com o coordenador).
- Pendente fora desta área: enquanto a API do `PUT /professionals/{id}` não aceitar especialidade inativa já vinculada, editar esse
  profissional pelo desktop recebe 400 (o desktop agora envia a especialidade). Não registrado em `docs/PENDENCIAS.md` por estar fora
  do escopo desta etapa; o coordenador decide o registro.
