package com.example.app_nora

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
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

    @Test fun patientCanRespondToPendingOfferAndLogout() {
        val user = PatientUser("test-user", "Paciente de teste")
        val offer = PatientOffer("00000000-0000-0000-0000-000000000001", "Oferta de teste", "2026-10-07T12:10:00Z", "pending")
        val state = PatientState(address = "https://nora.example.test",
            session = PatientSession("test-token", "2026-10-07T13:00:00Z", user),
            overview = PatientOverview(user, emptyList(), listOf(offer)))
        var answer: Boolean? = null
        var logout = false
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, state,
                onRespond = { id, accept -> assertEquals(offer.id, id); answer = accept }, onLogout = { logout = true })
        }
        compose.onNodeWithText("Aceitar oferta").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(true, answer) }
        compose.onNodeWithText("Recusar oferta").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(false, answer) }
        compose.onNodeWithText("Configurações").performClick()
        compose.onNodeWithText("Sair da conta").performScrollTo().assertIsEnabled().performClick()
        compose.runOnIdle { assertEquals(true, logout) }
    }
}
