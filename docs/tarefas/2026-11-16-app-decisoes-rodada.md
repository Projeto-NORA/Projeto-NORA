# App Android: decisões do usuário de 2026-11-16 (tela UBS, quem marcou, prévia de remoção)

Parte da tarefa [2026-11-11-integracao-back-front.md](2026-11-11-integracao-back-front.md), seção "Decisões do usuário"
(linhas de 2026-11-16). Esta etapa cuida só do app do paciente (`APPNORA/app/src`) e de `docs/app-paciente.md`; a API e o
desktop são feitos em paralelo por outros agentes.

## Skills e comandos

- Etapa de workflow da sessão principal (sem skill invocada diretamente nesta etapa).

## O que foi pedido

1. **Tela UBS no "agindo por":** abrir a tela UBS por qualquer caminho enquanto se age por um dependente sai do "agindo por" e
   mostra "Agora você está vendo as suas UBS"; escolher UBS, localizar, "Atualizar unidades" e "Remover esta UBS" valem para as
   UBS do próprio responsável. Nenhuma mensagem de erro do dependente aparece nessa tela. Teste de controlador e instrumentado.
2. **Quem marcou (contrato C1):** `bookedByYou` (bool) e `bookedByGuardianName` (texto ou nulo) nas consultas do paciente, na rota
   delegada e na resposta de agendar. Rótulo "Marcada por você" quando `bookedByYou`; "Marcada por <nome>" quando há nome e não
   foi você; o texto neutro atual só quando nenhum dos dois. Campos opcionais no parse. Atualizar `bookedByLabel` e `BookingLogicTest`.
3. **Prévia de remoção (contrato C2):** o aviso de remover UBS usa `GET /api/patient/units/{unitId}/unlink-preview`
   (`futureAppointments`, `activeWaitingEntries`); sai a redação "nos próximos 12 meses" e a janela de 366 dias; se a prévia falhar,
   aviso genérico de que todas as consultas futuras e solicitações de fila daquela UBS serão canceladas. Teste.

## Objetivo

O app segue as três decisões, contra fakes e MockWebServer que seguem os contratos C1 e C2 exatamente.

## Restrições

- Sem commit, stash, reset nem troca de branch; alterações anteriores não commitadas ficam como estão.
- Teste vermelho antes, implementação mínima, teste verde; sem enfraquecer asserções.
- Não tocar em `Nora.Desktop`, `tests/Nora.Desktop.Checks` nem em `docs/PENDENCIAS.md` (é do agente da API).
- Referências de screenshot só das prévias mudadas de propósito.

## Checklist

- [x] 1 · teste de controlador e instrumentado vermelhos → implementação → verdes
- [x] 2 · `BookingLogicTest` e parse HTTP vermelhos → implementação → verdes
- [x] 3 · teste do controlador e da rota vermelhos → implementação → verdes
- [x] `docs/app-paciente.md` atualizado
- [x] `assembleDebug testDebugUnitTest lintDebug assembleDebugAndroidTest validateDebugScreenshotTest`
- [x] instrumentados das telas mudadas no emulador `Medium_Phone_API_37.0` (desligado no fim)

## Estado

Concluído em 2026-11-16, sem commit. Para cada item, o teste foi escrito sobre um andaime mínimo que só o fazia compilar (campos
com valor padrão, `unlinkPreview` com o 501 padrão da interface, `showOwnUnits` devolvendo `false`) e falhou pelo motivo certo
antes da implementação:

| Item | Teste (vermelho antes → verde depois) | Arquivos |
| --- | --- | --- |
| 1 | `FamilyControllerTest.openingTheUnitsScreenWhileActingReturnsToTheOwnUnitsWithoutAnyErrorOfTheDependent` (a troca não acontecia); `ActingInteractionTest.openingTheUnitsScreenWhileActingShowsTheOwnUnitsAndNoErrorOfTheDependent` no emulador (aviso ausente, com a troca desligada) | `PatientController.kt` (`showOwnUnits`, `switchView`, `viewSwitches` em `operation`), `NoraApp.kt` (troca ao abrir a tela, aviso, mensagem do dependente oculta) |
| 2 | `BookingLogicTest.bookedByNamesWhoBookedWhenTheServerSays` ("Marcada pelo aplicativo" em vez de "Marcada por você"); `NoraApiHttpTest.whoBookedIsParsedFromTheOwnTheDelegatedAndTheBookingAnswers` (nome nulo) | `NoraApi.kt` (campos e parse opcional), `PatientLogic.kt` (`bookedByLabel`), `AppointmentsUi.kt` |
| 3 | `PatientActionsTest.theUnlinkWarningAsksTheServerForTheExactNumbersEachTimeAndNeverRaisesBusy` e `theUnlinkWarningIsGenericWhenThePreviewFails`; `NoraApiHttpTest.unlinkPreviewReadsTheExactNumbersTheRemovalWillCancel` (501) | `NoraApi.kt` (`unlinkPreview`; saem `upcomingAppointments` e `UPCOMING_APPOINTMENTS_DAYS`), `ApiGates.kt`, `PatientController.checkUnlinkImpact`, `PatientLogic.kt` (sai a contagem local `unlinkImpact`), `UnlinkUi.kt` |

Testes retirados junto com o código morto que verificavam: `NoraApiHttpTest.upcomingAppointmentsAsk…` (rota de 366 dias),
`AttendanceLogicTest.unlinkImpact…` (2, contagem local) e os dois testes antigos do aviso em `PatientActionsTest`, substituídos
pelos novos acima.

Validação: `assembleDebug testDebugUnitTest lintDebug assembleDebugAndroidTest validateDebugScreenshotTest` com sucesso depois de
atualizar só a referência `UnlinkDialogDark` (texto novo, PNG conferido): 224 testes locais sem falha, lint 0 erros e os 6 avisos
antigos, 48 prévias. No emulador `Medium_Phone_API_37.0`: `ActingInteractionTest` 5/5; `PatientInteractionTest` 10/11, com a
falha conhecida de P-045 (`loginRequiresDataAndSendsEnteredIdentifier` na classe inteira). Emulador desligado no fim.
`docs/app-paciente.md` atualizado. `docs/PENDENCIAS.md` fica com o agente da API.
