# Achados da revisão no app Android (Fase 3)

Parte da retomada `/aura-retomar-execucao:aura` ("continue, finalizando todas as pendências"), dentro da tarefa
[2026-11-11-integracao-back-front.md](2026-11-11-integracao-back-front.md). Esta etapa cuida só do app do paciente
(`APPNORA/app/src`) e de `docs/app-paciente.md`.

## Skills e comandos

- `/aura-retomar-execucao:aura` com o pedido "continue, finalizando todas as pendências, me faça perguntas durante a requisição".

## O que foi pedido

Para cada achado: um teste que reproduz o defeito e fica vermelho, a correção da causa com a menor mudança e o teste verde.
Sem commit, stash, reset nem troca de branch. Não mexer no comportamento da tela UBS nem em "Remover UBS" enquanto se age
por um dependente (aguarda decisão do usuário).

1. Agindo pelo dependente, a oferta nova dele não aparece em Vaga Relâmpago nem no Início, e o toque na notificação não relê a lista.
2. Remover a UBS avisa "nenhuma consulta futura" com dados em cache ou fora da janela de ±180 dias. O aviso deve reler as
   consultas futuras antes de confirmar (todas as futuras, não só a janela) e, sem como saber, avisar de forma genérica.
3. Os botões "Ver oportunidade" do Início e de Avisos abrem a oferta na visão da pessoa errada quando há ação em andamento:
   aplicar a mesma decisão do toque na notificação (`flashOpenDecision`).
4. Agindo pelo dependente, o Índice de Jornada fala do responsável ("Seu comparecimento", "Cancelamentos feitos por você").
5. Exames e vacinas ficam presos em "Atualizando…" quando o vínculo pelo qual se agia termina durante a leitura.
6. `recordsFailed` promete no log reler os vínculos num 403 do dependente, mas não chama `loadFamily()`.

## Objetivo

As telas de quem age por um dependente mostram os dados e os textos certos, e o aviso de remoção da UBS diz o que o servidor fará.

## Restrições

- Tela UBS e "Remover UBS" agindo por dependente: sem mudança de comportamento.
- Não enfraquecer asserções; referências de screenshot só das prévias mudadas de propósito.

## Checklist

- [x] 1 · teste vermelho → correção → verde
- [x] 2 · teste vermelho → correção → verde
- [x] 3 · teste vermelho → correção → verde
- [x] 4 · teste vermelho → correção → verde
- [x] 5 · teste vermelho → correção → verde
- [x] 6 · teste vermelho → correção → verde
- [x] `docs/app-paciente.md` atualizado
- [x] `assembleDebug testDebugUnitTest lintDebug assembleDebugAndroidTest validateDebugScreenshotTest`

## Estado

Concluído em 2026-11-16, sem commit. Cada achado teve o teste vermelho antes da correção e verde depois:

| Achado | Teste (vermelho antes → verde depois) | Correção |
| --- | --- | --- |
| 1 | `FamilyControllerTest.aNewOfferOfTheDependentBeingActedForReachesTheScreensInTheSameCycle` (lista vazia em vez de `off-2`); `ActingInteractionTest.tappingTheDependentsNotificationOnTheOpenFlashScreenRereadsTheOffers` (0 releituras) | `PatientController.refreshActingOffers` no ciclo de 30 s; `NoraApp` relê a lista no toque quando a Vaga Relâmpago do dependente já está aberta |
| 2 | `PatientActionsTest.theUnlinkWarningRereadsInsteadOfTrustingWhatWasReadAtLogin` (0 e 0 em vez de 1 e 1) | `checkUnlinkImpact` relê fila e consultas futuras (`upcomingAppointments`, 366 dias) a cada abertura; texto do aviso em `UnlinkUi` |
| 3 | `ActingInteractionTest.ownOfferButtonInNotices…` e `dependentOfferButtonOnHome…` (a tela abria na pessoa errada) | botões viram pedido decidido por `flashOpenDecision`, como a notificação |
| 4 | `ActingInteractionTest.journeyIndexSpeaksAboutTheDependentWhenActingForHer` (texto do responsável) | `AttendanceCard` usa o nome do dependente |
| 5 | `FamilyControllerTest.aLinkThatEndsDuringTheDependentsRecordsRead…` e `anOwnRecordsReadWaitingForTheUnits…` (preso em loading) | `applyFamily` zera `records`/`attendanceLoading` ao perder o vínculo; `loadRecords` desiste se a pessoa em tela mudou |
| 6 | `FamilyControllerTest.aForbiddenRecordsReadWhileActingRereadsTheLinks` (1 leitura de vínculos em vez de 2) | `loadRecords` chama `loadFamily()` num 403 do dependente; log enganoso removido |

O teste antigo `detailsAreReadOnlyWhenMissingAndNeverRaiseBusy` afirmava o defeito do achado 2 ("Já lidos: não repete") e foi
substituído por `theUnlinkWarningIsReadAgainEachTimeAndNeverRaisesBusy`.

Validação: `assembleDebug testDebugUnitTest lintDebug assembleDebugAndroidTest validateDebugScreenshotTest` com sucesso:
223 testes locais sem falha, lint 0 erros e 6 avisos antigos, 48 prévias (referência de `UnlinkDialogDark` atualizada e conferida).
`ActingInteractionTest` 4/4 no emulador. Testes instrumentados antigos com falha anterior registrados em P-045.
