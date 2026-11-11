package com.example.app_nora

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.compose.ui.semantics.SemanticsActions
import com.example.app_nora.api.*
import com.example.app_nora.preferences.UserSettings
import com.example.app_nora.ui.NoraPatientContent
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class PatientInteractionTest {
    @get:Rule val compose = createComposeRule()

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

    @Test fun patientCanRequestAdditionalUnitsAndSeePartialResult() {
        val user = PatientUser("patient", "Paciente de teste")
        val state = PatientState(session = PatientSession("token", "2099-01-01T00:00:00Z", user), units = emptyList())
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
        val user = PatientUser("test-user", "Paciente de teste")
        val offer = UnitOffer("00000000-0000-0000-0000-000000000001", "Cardiologia", "Profissional", "2026-10-08T12:00:00Z", "2026-10-07T12:10:00Z", "pending")
        val unit = PatientHealthUnit("unit-id", "UBS Teste", "Centro", "Rua de teste", -23.5, -46.6)
        val state = PatientState(address = "https://nora.example.test",
            session = PatientSession("test-token", "2026-10-07T13:00:00Z", user),
            units = listOf(unit), selectedUnitId = unit.id, unitQueue = emptyList(), unitOffers = listOf(offer))
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

    @Test fun appointmentCancellationRequiresConfirmation() {
        val user = PatientUser("patient", "Paciente de teste")
        val appointment = PatientAppointment("appointment-1", "Clínica", "Profissional", "2026-11-12T12:00:00Z", "scheduled")
        val unit = PatientHealthUnit("unit-1", "UBS Centro", "Centro", "Rua de teste", -23.5, -46.6)
        val state = PatientState(session = PatientSession("token", "2099-01-01T00:00:00Z", user),
            units = listOf(unit), selectedUnitId = unit.id, appointments = listOf(appointment))
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
