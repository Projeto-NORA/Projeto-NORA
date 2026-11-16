# App: testes instrumentados antigos (P-045) e E2E Android com API real (P-040)

## Skills e comandos invocados

Nenhum. Pedido do agente coordenador da sessão (subagente), em 2026-11-16.

## O que foi pedido

1. **P-045**: os 4 testes instrumentados antigos que falham — `SettingsInteractionTest` ×2 (procuram "Abrir configurações"),
   `PatientPresenceServiceTest.connectionSurvivesBackgroundAndEndsWhenTaskIsRemoved` (quebra em `ActivityScenario.close`) e
   `PatientInteractionTest.loginRequiresDataAndSendsEnteredIdentifier` (falha com a classe inteira, passa sozinho). Para cada um,
   achar a causa real: teste desatualizado → atualizar para provar o MESMO comportamento pelo caminho atual; defeito do app →
   corrigir com teste; vazamento de estado entre testes → corrigir o isolamento. Sem `@Ignore` e sem retry.
2. Rodar a suíte instrumentada inteira (`connectedDebugAndroidTest`) até verde e anotar os skips por assumption e o motivo.
3. **P-040**: rodar `tests/validar-clientes-descartavel.ps1 -Android` contra API e PostgreSQL 18 descartáveis (porta 55740,
   prévias em `.build\previews-android-e2e`), depois de confirmar que não há outra execução do script. Falha em APPNORA →
   ciclo vermelho/verde e correção; falha na API ou no script → não corrigir, descrever com evidência.
4. Validação final: `assembleDebug testDebugUnitTest lintDebug assembleDebugAndroidTest validateDebugScreenshotTest`, suíte
   instrumentada e E2E `-Android`.
5. Atualizar `docs/app-paciente.md` e `docs/android-emulador.md` se algo mudou; mover P-045 e P-040 para Resolvidas só se
   ficaram verdes.

## Objetivo

Suíte instrumentada do app inteira verde no emulador `Medium_Phone_API_37.0` e o teste real do app contra a API e o banco
descartáveis executado de verdade.

## Restrições

- Não fazer commit, stash, reset nem troca de branch; não desfazer as alterações não commitadas de outras rodadas.
- Só mexer em `APPNORA` (e na documentação); não tocar em `Nora.Api`, `Nora.Desktop` nem `tests/Nora.*Checks`.
- Sem `@Ignore` nem retry para esconder falha. Desligar o emulador no fim.

## Pedidos do coordenador durante a tarefa

- Não usar o script de diagnóstico que travou (o `pg_ctl` com a saída em pipe prendia o PowerShell); rodar cada exe com stdin vazio e prazo.
- Deixar o emulador ligado no fim (substitui o "desligue-o" do pedido inicial).
- Outro agente passou a alterar `Nora.Api/` e `tests/Nora.Api.Checks/` neste worktree: a fixture vem de um worktree temporário do
  HEAD `09fdd87`, com o APPNORA deste worktree, portas 55740-55749; o worktree temporário é removido no fim.

## Checklist

- [x] `SettingsInteractionTest` (2): causa e correção
- [x] `PatientPresenceServiceTest.connectionSurvivesBackgroundAndEndsWhenTaskIsRemoved`: causa e correção
- [x] `PatientInteractionTest.loginRequiresDataAndSendsEnteredIdentifier`: causa e correção
- [x] `connectedDebugAndroidTest` inteiro verde, skips anotados
- [x] E2E `-Android` executado (P-040)
- [x] Validação Gradle final verde
- [x] Documentação e pendências atualizadas
- [x] Emulador ligado no fim (pedido do coordenador); worktree temporário removido

## Estado

Concluído em 2026-11-16, sem commit.

| Teste | Causa real | Correção |
| --- | --- | --- |
| `SettingsInteractionTest` ×2 | Teste desatualizado: o botão "Abrir configurações" e a barra "Início/Configurações" sem conta saíram na reforma visual (`83ebcd4`); o `NoraContent` que o teste usava mostrava um estado que o app não tem. | Reescrito pelo caminho real antes do login (Ajuda → "Configurações"), provando o mesmo: abre na Aparência, volta à entrada e reabre no topo; "Escura" e "Restaurar" mudam o estado inteiro; sem conta não há "Sair da conta". `NoraContent` (sem uso) removido. |
| `PatientPresenceServiceTest.connectionSurvives…` | Do teste: a `MainActivity` adota o Intent da notificação (`setIntent`), que não tem ação/categoria; o `ActivityScenario` ignora eventos de uma Activity cujo Intent não é `filterEquals` ao do lançamento (logcat "ignored because the intent does not match"), e o `close()` encontrava o estágio `RESTARTED`. | O teste lança a Activity com o mesmo Intent da notificação; nada mais mudou. |
| `PatientInteractionTest.loginRequires…` | Do app, e não de isolamento: falhava também sozinho. O `83ebcd4` tirou o `password = ""` depois do envio; o botão "Entrar" seguia ligado (um segundo toque antes de `busy` repetia o login). | `NoraApp.kt`: a senha sai do formulário ao tocar "Entrar". Vermelho antes (sozinho e na classe), verde depois. |
| `patientUsesRealDisposablePostgresApi` (P-040) | (1) Android 17 + `targetSdk` 37: sem `ACCESS_LOCAL_NETWORK` o uid do app não alcança `10.0.2.2` (rede local); provado com `run-as` + `nc` (uid do app: 8.8.8.8 e DNS ok, `10.0.2.2` expira; shell ok; concedida passa, revogada volta a expirar). (2) A API cria a oferta só no ciclo de 15 s do `UnitLightningWorker`; o teste lia uma vez logo após o login. | (1) Manifesto declara a permissão; `ApiAddress.usesLocalNetwork` (teste local vermelho → verde) e `NoraApp.kt` a pedem antes de login, cadastro e recuperação; recusa mostra `LOCAL_NETWORK_DENIED_MESSAGE`; o teste concede com `pm grant`. (2) O teste repete o ciclo automático por até 45 s. |

Validação (números reais):

- `:app:assembleDebug :app:testDebugUnitTest :app:lintDebug :app:assembleDebugAndroidTest :app:validateDebugScreenshotTest`: BUILD
  SUCCESSFUL; 225 testes locais, 0 falhas; lint 0 erros e 6 avisos antigos; 48 prévias, 0 falhas.
- `connectedDebugAndroidTest` (sem API): 30 testes, 28 aprovados, 0 falhas, 2 pulados por assumption
  (`patientUsesRealDisposablePostgresApi` sem `noraApiUrl`; `VisualCaptureTest` sem `noraCaptureVisual=true`).
- `validar-clientes-descartavel.ps1 -Android -Port 55740` (API e desktop do HEAD `09fdd87`, APPNORA deste worktree): sucesso; desktop
  150/150; Android 30 testes, 29 aprovados, 0 falhas, 1 pulado (`VisualCaptureTest`). A primeira tentativa teve 149/150 no desktop
  (P-047, intermitente; a repetição passou).

Pendências abertas por esta tarefa: P-046 (pedido da permissão em tela sem teste instrumentado) e P-047 (verificação intermitente do
desktop). Documentos atualizados: `app-paciente.md`, `execucao.md`, `android-emulador.md`, `integracao-back-front.md`, `PENDENCIAS.md`.
