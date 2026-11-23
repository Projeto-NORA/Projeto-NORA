package com.example.app_nora

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import com.example.app_nora.api.*
import com.example.app_nora.preferences.UserSettings
import com.example.app_nora.ui.GoogleActions
import com.example.app_nora.ui.NoraPatientContent
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test

/** Tela da UBS com o cartão de atendimento, "Remover UBS" só agindo por si e os botões do Google (login e Configurações). */
class RoundInteractionTest {
    @get:Rule val compose = createComposeRule()

    private val unit = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", -23.5, -46.6)
    private val maria = FamilyLink("dep-1", "approved", "unit-a", "UBS A", "***.456.789-**", "Maria Souza", "2026-11-10T12:00:00Z",
        "2026-11-11T12:00:00Z", null, null)
    private val profile = UnitProfile("unit-a", true,
        listOf(OpeningInterval(1, "08:00", "12:00"), OpeningInterval(1, "13:00", "17:00"), OpeningInterval(0, "08:00", "12:00")),
        "Vacinação\nCurativos", listOf(Specialty("s1", "Clínica Geral"), Specialty("s2", "Pediatria")), "2026-11-16T10:00:00Z")

    private fun connected(about: AboutState? = null, google: GoogleState = GoogleState(), actingFor: String? = null) = PatientState(
        address = "https://nora.example.test", session = PatientSession("test-token", "2099-01-01T00:00:00Z", PatientUser("test-user", "Paciente de teste")),
        units = listOf(unit), selectedUnitId = unit.id, unitData = mapOf(unit.id to UnitSnapshot(queue = emptyList(), offers = emptyList(), appointments = emptyList())),
        family = FamilyState(links = listOf(maria)), actingFor = actingFor, google = google,
        about = if (about == null) emptyMap() else mapOf(unit.id to about))

    private var state by mutableStateOf(PatientState())

    private fun openUnits(initial: PatientState, onLoadAbout: (String) -> Unit = {}) {
        state = initial
        compose.setContent { NoraPatientContent(UserSettings(), {}, state, onLoadAbout = onLoadAbout) }
        compose.onNodeWithText("UBS próximas").performScrollTo().performClick()
        compose.onNodeWithText("Unidades de saúde").assertExists()
    }

    @Test fun theAboutCardShowsEachDayTodayInTextServicesAndSpecialties() {
        val loaded = mutableListOf<String>()
        openUnits(connected(AboutState(profile)), onLoadAbout = { loaded += it })
        compose.onNodeWithText("Horário de funcionamento").performScrollTo().assertExists()
        compose.onNodeWithText("08:00 – 12:00 · 13:00 – 17:00").performScrollTo().assertExists()
        // O dia de hoje ganha o sufixo " · hoje" (que dia é hoje depende de quando o teste roda).
        compose.onNodeWithText("Terça-feira", substring = true).performScrollTo().assertExists()
        compose.onAllNodesWithText("Fechado").onFirst().performScrollTo().assertExists()
        compose.onNodeWithText("Vacinação\nCurativos").performScrollTo().assertExists()
        compose.onNodeWithText("Clínica Geral").performScrollTo().assertExists()
        compose.onNodeWithText("Pediatria").performScrollTo().assertExists()
        // O dia de hoje é marcado em texto, não só por cor.
        compose.onNodeWithText("· hoje", substring = true).performScrollTo().assertExists()
        compose.runOnIdle { assertEquals("a tela pede a leitura da UBS selecionada", listOf("unit-a"), loaded.distinct()) }
    }

    @Test fun anUnitWithoutInformedHoursSaysSoAndKeepsTheEmptyServicesAndSpecialties() {
        openUnits(connected(AboutState(UnitProfile("unit-a", false, emptyList(), null, emptyList(), null))))
        compose.onNodeWithText("Horário não informado pela UBS").performScrollTo().assertExists()
        compose.onNodeWithText("A UBS ainda não informou os serviços").performScrollTo().assertExists()
        compose.onNodeWithText("Sem especialidades com horário na grade").performScrollTo().assertExists()
        compose.onNodeWithText("Domingo").assertDoesNotExist()
    }

    @Test fun loadingShowsTheStepAndAFailureOffersToTryAgainThatRereads() {
        val loads = mutableListOf<String>()
        openUnits(connected(AboutState(loading = true)), onLoadAbout = { loads += it })
        compose.onNodeWithText("Carregando o atendimento da UBS…").performScrollTo().assertExists()
        compose.runOnIdle { state = connected(AboutState(error = "A base de uma UBS está temporariamente indisponível. Tente novamente em instantes.")) }
        compose.onNodeWithText("A base de uma UBS está temporariamente indisponível. Tente novamente em instantes.").performScrollTo().assertExists()
        val before = compose.runOnIdle { loads.size }
        compose.onNodeWithText("Tentar de novo").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(before + 1, loads.size) }
    }

    @Test fun removeUnitShowsActingForYourselfAndHidesWhenActingForADependent() {
        openUnits(connected())
        compose.onNodeWithContentDescription("Remover esta UBS").performScrollTo().assertExists()
        // Agindo por Maria (a tela de teste não volta a agir por si sozinha): o botão some.
        compose.runOnIdle { state = connected(actingFor = maria.id) }
        compose.onNodeWithContentDescription("Remover esta UBS").assertDoesNotExist()
        compose.onNodeWithContentDescription("Atualizar unidades").performScrollTo().assertExists()
    }

    // ---- Google ---------------------------------------------------------------------------------------------

    @Test fun theLoginHasNoGoogleButtonWithoutTheClientIdInTheBuild() {
        compose.setContent { NoraPatientContent(UserSettings(), {}, PatientState(address = "https://nora.example.test"), onLogin = { _, _ -> },
            google = GoogleActions(webClientId = "")) }
        compose.onNodeWithText("Entrar").assertExists()
        compose.onNodeWithText("Continuar com Google").assertDoesNotExist()
        // O aviso antigo de "ainda não habilitado" não existe mais.
        compose.onNodeWithText("Entrar com Google").assertDoesNotExist()
    }

    @Test fun theGoogleButtonSignsInWithTheIdTokenAndShowsTheSteps() {
        var signedIn: String? = null
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, PatientState(address = "https://nora.example.test"), onLogin = { _, _ -> },
                google = GoogleActions(webClientId = "cliente-web-de-teste", requestIdToken = { GoogleIdResult.Token("id-token-de-teste") },
                    onSignIn = { token -> signedIn = token; false }))
        }
        compose.onNodeWithText("Continuar com Google").performScrollTo().performClick()
        compose.waitUntil(5_000) { signedIn != null }
        assertEquals("id-token-de-teste", signedIn)
        // Entrar falhou: o passo fica marcado como falho, e o anterior continua marcado como feito.
        compose.onNodeWithText("✓ Abrindo o Google", substring = true).performScrollTo().assertExists()
        compose.onNodeWithText("✗ Entrando no NORA", substring = true).performScrollTo().assertExists()
    }

    @Test fun whenTheGoogleListFailsTheMessageGoesToTheScreenAndTheStepIsMarked() {
        var message: String? = null
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, PatientState(address = "https://nora.example.test"), onLogin = { _, _ -> },
                google = GoogleActions(webClientId = "cliente-web-de-teste",
                    requestIdToken = { GoogleIdResult.Failed("Não encontramos uma conta Google neste aparelho.") }, onFailed = { message = it }))
        }
        compose.onNodeWithText("Continuar com Google").performScrollTo().performClick()
        compose.waitUntil(5_000) { message != null }
        assertEquals("Não encontramos uma conta Google neste aparelho.", message)
        compose.onNodeWithText("✗ Abrindo o Google", substring = true).performScrollTo().assertExists()
    }

    @Test fun cancellingTheGoogleListLeavesNoStepsAndNoMessage() {
        var asked = false
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, PatientState(address = "https://nora.example.test"), onLogin = { _, _ -> },
                google = GoogleActions(webClientId = "cliente-web-de-teste", requestIdToken = { asked = true; GoogleIdResult.Cancelled }))
        }
        compose.onNodeWithText("Continuar com Google").performScrollTo().performClick()
        compose.waitUntil(5_000) { asked }
        compose.onNodeWithText("Abrindo o Google", substring = true).assertDoesNotExist()
    }

    private fun settings(patient: PatientState, google: GoogleActions) {
        state = patient
        compose.setContent { NoraPatientContent(UserSettings(), {}, state, initiallyShowSettings = true, google = google) }
    }

    @Test fun theSettingsCardAppearsOnlyWithTheClientIdAndTheServerEnabled() {
        settings(connected(google = GoogleState(GoogleStatus(false, false, null))), GoogleActions(webClientId = "cliente-web-de-teste"))
        compose.onNodeWithText("Conta Google").assertDoesNotExist()
        compose.runOnIdle { state = connected(google = GoogleState(GoogleStatus(true, false, null))) }
        compose.onNodeWithText("Conta Google").performScrollTo().assertExists()
        compose.onNodeWithText("Vincular conta Google").performScrollTo().assertExists()
        compose.onNodeWithText("Desvincular conta Google").assertDoesNotExist()
    }

    @Test fun theSettingsCardIsHiddenWithoutTheClientIdEvenIfTheServerIsEnabled() {
        settings(connected(google = GoogleState(GoogleStatus(true, true, "paciente@gmail.example"))), GoogleActions(webClientId = ""))
        compose.onNodeWithText("Conta Google").assertDoesNotExist()
        compose.onNodeWithText("Desvincular conta Google").assertDoesNotExist()
    }

    @Test fun linkedAccountShowsTheEmailAndUnlinkingAsksForConfirmation() {
        var unlinked = 0
        settings(connected(google = GoogleState(GoogleStatus(true, true, "paciente@gmail.example"))),
            GoogleActions(webClientId = "cliente-web-de-teste", onUnlink = { unlinked++; true }))
        compose.onNodeWithText("Vinculada: paciente@gmail.example", substring = true).performScrollTo().assertExists()
        compose.onNodeWithText("Desvincular conta Google").performScrollTo().performClick()
        compose.onNodeWithText("Desvincular o Google?").assertExists()
        compose.onNodeWithText("Voltar").performClick()
        compose.runOnIdle { assertEquals(0, unlinked) }
        compose.onNodeWithText("Desvincular conta Google").performScrollTo().performClick()
        compose.onNodeWithText("Desvincular").performClick()
        compose.waitUntil(5_000) { unlinked == 1 }
        compose.onNodeWithText("✓ Desvinculando a conta Google", substring = true).performScrollTo().assertExists()
    }

    @Test fun linkingOpensTheGoogleListAndSendsTheToken() {
        var linkedWith: String? = null
        settings(connected(google = GoogleState(GoogleStatus(true, false, null))),
            GoogleActions(webClientId = "cliente-web-de-teste", requestIdToken = { GoogleIdResult.Token("id-token-de-teste") },
                onLink = { token -> linkedWith = token; true }))
        compose.onNodeWithText("Vincular conta Google").performScrollTo().performClick()
        compose.waitUntil(5_000) { linkedWith != null }
        assertTrue(linkedWith == "id-token-de-teste")
        compose.onNodeWithText("✓ Vinculando a conta Google", substring = true).performScrollTo().assertExists()
    }

    @Test fun aFailedStatusReadShowsTheErrorWithRetryOnlyWithTheClientId() {
        var reads = 0
        settings(connected(google = GoogleState(error = "Não foi possível conectar ao serviço.")),
            GoogleActions(webClientId = "cliente-web-de-teste", onLoadStatus = { reads++ }))
        compose.onNodeWithText("Não foi possível conectar ao serviço.").performScrollTo().assertExists()
        val before = compose.runOnIdle { reads }
        compose.onNodeWithText("Tentar de novo").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(before + 1, reads) }
    }
}
