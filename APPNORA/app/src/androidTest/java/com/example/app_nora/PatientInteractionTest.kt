package com.example.app_nora

import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.StateRestorationTester
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.compose.ui.semantics.SemanticsActions
import com.example.app_nora.api.*
import com.example.app_nora.preferences.UserSettings
import com.example.app_nora.ui.LocalClockMillis
import com.example.app_nora.ui.NoraPatientContent
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone

private fun isoAt(millis: Long): String =
    SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.ROOT).apply { timeZone = TimeZone.getTimeZone("UTC") }.format(millis)

class PatientInteractionTest {
    @get:Rule val compose = createComposeRule()

    private val now = System.currentTimeMillis()
    private val user = PatientUser("test-user", "Paciente de teste")
    private val unit = PatientHealthUnit("unit-id", "UBS Teste", "Centro", "Rua de teste", -23.5, -46.6)
    private fun offer(id: String, expiresInSeconds: Long, status: String = "pending") = UnitOffer(id, "Cardiologia", "Profissional",
        isoAt(now + 86_400_000), isoAt(now + 86_400_000 + 1_800_000), isoAt(now + expiresInSeconds * 1000), status)
    private fun connected(offers: List<UnitOffer>? = null, appointments: List<PatientAppointment>? = null,
        queue: List<UnitQueueEntry>? = emptyList()) = PatientState(address = "https://nora.example.test",
        session = PatientSession("test-token", "2099-01-01T00:00:00Z", user), units = listOf(unit), selectedUnitId = unit.id,
        unitData = mapOf(unit.id to UnitSnapshot(queue = queue, offers = offers, appointments = appointments)))

    @Test fun loginRequiresDataAndSendsEnteredIdentifier() {
        var submitted = ""
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, PatientState(address = "https://nora.example.test"),
                onLogin = { identifier, _ -> submitted = identifier })
        }
        val enterButton = compose.onNode(hasText("Entrar") and hasClickAction())
        enterButton.assertIsNotEnabled()
        compose.onNodeWithText("CPF ou e-mail").performTextInput("patient@example.test")
        compose.onNodeWithText("Senha").performTextInput("test-only-input")
        enterButton.assertIsEnabled().performClick()
        compose.runOnIdle { assertEquals("patient@example.test", submitted) }
        enterButton.assertIsNotEnabled()
    }

    @Test fun registrationCanSelectMultipleUnitsBeforeSignupIsAvailable() {
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, PatientState(address = "https://nora.example.test"),
                onLogin = { _, _ -> }, onRegistrationUnits = { listOf(
                    RegistrationUnit("unit-1", "UBS Centro", "Centro"),
                    RegistrationUnit("unit-2", "UBS Norte", "Norte")) })
        }
        compose.onNode(hasText("Criar conta") and hasClickAction()).performSemanticsAction(SemanticsActions.OnClick)
        compose.onNodeWithContentDescription("Selecionar UBS Centro").assertIsOff().performSemanticsAction(SemanticsActions.OnClick).assertIsOn()
        compose.onNodeWithContentDescription("Selecionar UBS Norte").assertIsOff().performSemanticsAction(SemanticsActions.OnClick).assertIsOn()
        compose.onNode(hasText("Criar conta") and hasClickAction()).assertIsNotEnabled()
    }

    @Test fun registrationFormSurvivesRotationButThePasswordDoesNot() {
        val restoration = StateRestorationTester(compose)
        restoration.setContent {
            NoraPatientContent(UserSettings(), {}, PatientState(address = "https://nora.example.test"),
                onLogin = { _, _ -> }, onRegistrationUnits = { listOf(RegistrationUnit("unit-1", "UBS Centro", "Centro")) })
        }
        compose.onNode(hasText("Criar conta") and hasClickAction()).performSemanticsAction(SemanticsActions.OnClick)
        compose.onNodeWithText("Nome completo").performTextInput("Paciente Sintético")
        compose.onNodeWithText("Senha").performTextInput("senha-que-nao-deve-sobreviver")
        compose.onNodeWithContentDescription("Selecionar UBS Centro").performSemanticsAction(SemanticsActions.OnClick).assertIsOn()
        restoration.emulateSavedInstanceStateRestore()
        compose.onNodeWithText("Paciente Sintético").assertExists()
        compose.onNodeWithContentDescription("Selecionar UBS Centro").assertIsOn()
        compose.onNodeWithText("senha-que-nao-deve-sobreviver").assertDoesNotExist()
    }

    @Test fun patientCanRequestAdditionalUnitsAndSeePartialResult() {
        val state = PatientState(session = PatientSession("token", "2099-01-01T00:00:00Z", PatientUser("patient", "Paciente de teste")), units = emptyList())
        var submitted = emptyList<String>()
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, state,
                onRegistrationUnits = { listOf(RegistrationUnit("one", "UBS Centro", "Centro"), RegistrationUnit("two", "UBS Norte", "Norte")) },
                onLinkUnits = { submitted = it; UnitLinkResult(listOf("one"), listOf("two")) })
        }
        compose.onNodeWithText("UBS próximas").performScrollTo().performClick()
        compose.onNodeWithText("Vincular outra UBS").performScrollTo().performClick()
        compose.onNodeWithText("UBS Centro · Centro").performClick()
        compose.onNodeWithText("UBS Norte · Norte").performClick()
        compose.onNodeWithText("Conferir e vincular").performClick()
        compose.runOnIdle { assertEquals(setOf("one", "two"), submitted.toSet()) }
        compose.onNodeWithText("Resultado da conferência").assertExists()
        compose.onNodeWithText("UBS vinculadas: UBS Centro.\n\nNão encontramos um cadastro ativo em: UBS Norte. Procure a equipe dessas unidades.").assertExists()
    }

    @Test fun patientCanRespondToPendingOfferAndLogout() {
        val offer = offer("00000000-0000-0000-0000-000000000001", expiresInSeconds = 600)
        val state = connected(offers = listOf(offer))
        var answer: Boolean? = null
        var logout = false
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, state,
                onRespondUnit = { id, accept -> assertEquals(offer.id, id); answer = accept }, onLogout = { logout = true })
        }
        compose.onNodeWithText("Ver oportunidade").performScrollTo().performClick()
        compose.onNodeWithText("Aceitar oportunidade").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(true, answer) }
        compose.onNodeWithText("Recusar oportunidade").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(false, answer) }
        compose.onNodeWithText("Voltar ao início").performClick()
        compose.onNodeWithContentDescription("Configurações").performScrollTo().performClick()
        compose.onNodeWithText("Sair da conta").performScrollTo().assertIsEnabled().performClick()
        compose.runOnIdle { assertEquals(true, logout) }
    }

    @Test fun offerWithPassedDeadlineHidesAcceptAndDeclineAndSaysSo() {
        val state = connected(offers = listOf(offer("expired-offer", expiresInSeconds = -30)))
        compose.setContent { NoraPatientContent(UserSettings(), {}, state) }
        compose.onNode(hasText("Oportunidades") and hasClickAction()).performScrollTo().performClick()
        compose.onNodeWithText("Prazo encerrado").performScrollTo().assertExists()
        compose.onNodeWithText("Aceitar oportunidade").assertDoesNotExist()
        compose.onNodeWithText("Recusar oportunidade").assertDoesNotExist()
    }

    @Test fun openOfferShowsUnitIntervalAndReadableCountdown() {
        val fixed = now
        val state = connected(offers = listOf(offer("open-offer", expiresInSeconds = 570)))
        compose.setContent {
            CompositionLocalProvider(LocalClockMillis provides { fixed }) { NoraPatientContent(UserSettings(), {}, state) }
        }
        compose.onNodeWithContentDescription("Tempo restante: 9 minutos").performScrollTo().assertExists()
        compose.onNodeWithText("Ver oportunidade").performScrollTo().performClick()
        // Nome da UBS na lista de escolha e no cartão da oferta.
        compose.onAllNodesWithText("UBS Teste").assertCountEquals(2)
        compose.onNodeWithText("Aceitar oportunidade").assertIsEnabled()
    }

    @Test fun historyStartsCollapsedAndOpensOnRequest() {
        val state = connected(offers = listOf(offer("open", 600), offer("done", -3600, "accepted")))
        compose.setContent { NoraPatientContent(UserSettings(), {}, state) }
        compose.onNode(hasText("Oportunidades") and hasClickAction()).performScrollTo().performClick()
        compose.onNodeWithText("Histórico (1) · Mostrar").performScrollTo().assertExists()
        compose.onNodeWithText("Oferta aceita. Sua consulta foi agendada.").assertDoesNotExist()
        compose.onNodeWithText("Histórico (1) · Mostrar").performClick()
        compose.onNodeWithText("Oferta aceita. Sua consulta foi agendada.").performScrollTo().assertExists()
    }

    @Test fun homeShowsTheRealNextAppointmentOrAnHonestEmptyState() {
        val appointment = PatientAppointment("ap-1", "Ortopedia", "Dr. Teste", isoAt(now + 2 * 86_400_000), isoAt(now + 2 * 86_400_000 + 1_800_000), "scheduled")
        var state by mutableStateOf(connected(appointments = listOf(appointment)))
        compose.setContent { NoraPatientContent(UserSettings(), {}, state) }
        compose.onNodeWithText("Sua próxima consulta").assertExists()
        compose.onNodeWithText("Ortopedia").assertExists()
        compose.onNodeWithText("Dr. Teste · UBS Teste").assertExists()
        compose.runOnIdle { state = connected(appointments = emptyList()) }
        compose.onNodeWithText("Nenhuma consulta futura").assertExists()
        compose.onNodeWithText("Você não tem consulta agendada nas suas UBS.").assertExists()
    }

    @Test fun endingTheSessionOutsideHomeReturnsToTheLoginForm() {
        var state by mutableStateOf(connected())
        compose.setContent { NoraPatientContent(UserSettings(), {}, state, onLogin = { _, _ -> }) }
        compose.onNodeWithText("Jornada").performClick()
        compose.onNodeWithText("Sua Jornada").assertExists()
        compose.runOnIdle { state = PatientState(address = "https://nora.example.test", message = "Sua sessão terminou. Entre novamente.") }
        compose.onNode(hasText("Entrar") and hasClickAction()).assertExists()
        compose.onNodeWithText("Sua sessão terminou. Entre novamente.").assertExists()
    }

    @Test fun appointmentCancellationRequiresConfirmation() {
        val appointment = PatientAppointment("appointment-1", "Clínica", "Profissional", "2026-11-12T12:00:00Z", "2026-11-12T12:30:00Z", "scheduled")
        val state = connected(appointments = listOf(appointment))
        var cancelled: String? = null
        compose.setContent { NoraPatientContent(UserSettings(), {}, state, onCancelAppointment = { cancelled = it }) }
        compose.onNodeWithText("Ver minhas consultas").performScrollTo().performClick()
        compose.onNodeWithText("Cancelar consulta").performScrollTo().performClick()
        compose.onNodeWithText("Voltar").performClick()
        compose.runOnIdle { assertEquals(null, cancelled) }
        compose.onNodeWithText("Cancelar consulta").performScrollTo().performClick()
        compose.onNodeWithText("Confirmar cancelamento").performClick()
        compose.runOnIdle { assertEquals(appointment.id, cancelled) }
    }
}
