package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

/** Sugestão de troca do e-mail da conta: leitura no ciclo, código, confirmação e dispensa. */
class EmailControllerTest {
    private val suggestion = EmailSuggestion("sug-1", "n***@example.test", "unit-a", "UBS A", "2026-11-16T10:00:00Z")

    private suspend fun ready(api: FamilyFakeApi): PatientController {
        api.units = listOf(unitA)
        val controller = PatientController(MemoryAddressStore()) { api }
        controller.login("paciente@example.test", "test-only-input")
        return controller
    }

    private suspend fun withSuggestion(api: FamilyFakeApi): PatientController {
        api.suggestion = suggestion
        val controller = ready(api)
        controller.loadEmailSuggestion()
        return controller
    }

    @Test fun theCycleReadsTheSuggestionWithOneLightRequestAndTheProfileCanReadItToo() = runBlocking {
        val api = FamilyFakeApi().apply { suggestion = this@EmailControllerTest.suggestion }
        val controller = ready(api)
        val session = requireNotNull(controller.state.session)
        controller.pollCommunity(session, controller.beginPresence())
        assertEquals(1, api.callsOf("own.emailSuggestion").size)
        assertEquals(suggestion, controller.state.email.suggestion)
        assertTrue(controller.state.email.loaded)
        // Sem sugestão: nada aparece.
        api.suggestion = null
        controller.loadEmailSuggestion()
        assertNull(controller.state.email.suggestion)
    }

    @Test fun aReadFailureIsSilentAndKeepsWhatWasKnown() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withSuggestion(api)
        api.suggestionFailure = IOException("offline")
        controller.loadEmailSuggestion()
        assertEquals(suggestion, controller.state.email.suggestion)
        assertNull(controller.state.message)
    }

    @Test fun sendingTheCodeRemembersItWasSentAndTellsWhereItWent() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withSuggestion(api)
        assertNull(controller.state.email.codeSentFor)
        controller.sendEmailCode()
        assertEquals(listOf("own.sendCode"), api.callsOf("own.sendCode"))
        assertEquals("sug-1", controller.state.email.codeSentFor)
        assertTrue(controller.state.message.orEmpty().contains("n***@example.test"))
        // Uma leitura do ciclo da mesma sugestão não apaga o "código enviado".
        api.suggestion = suggestion
        controller.pollCommunity(requireNotNull(controller.state.session), controller.beginPresence())
        assertEquals("sug-1", controller.state.email.codeSentFor)
    }

    @Test fun aNewSuggestionForgetsThatACodeWasSent() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withSuggestion(api)
        controller.sendEmailCode()
        api.suggestion = suggestion.copy(id = "sug-2", maskedEmail = "o***@example.test")
        controller.loadEmailSuggestion()
        assertEquals("sug-2", controller.state.email.suggestion?.id)
        assertNull(controller.state.email.codeSentFor)
    }

    @Test fun aWrongCodeKeepsTheSuggestionAndTheCurrentEmail() = runBlocking {
        val api = FamilyFakeApi().apply { codeIsValid = false }
        val controller = withSuggestion(api)
        controller.sendEmailCode()
        assertFalse(controller.confirmEmailCode("123456"))
        assertEquals(listOf("123456"), api.confirmedCodes)
        assertEquals(suggestion, controller.state.email.suggestion)
        assertTrue(controller.state.message.orEmpty().startsWith("Código incorreto"))
    }

    @Test fun aMalformedCodeNeverReachesTheServer() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withSuggestion(api)
        assertFalse(controller.confirmEmailCode("12ab56"))
        assertFalse(controller.confirmEmailCode("123"))
        assertTrue(api.confirmedCodes.isEmpty())
        assertEquals("Digite os 6 dígitos do código.", controller.state.message)
    }

    @Test fun aCorrectCodeChangesTheEmailAndClearsTheSuggestion() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withSuggestion(api)
        controller.sendEmailCode()
        assertTrue(controller.confirmEmailCode("654321"))
        assertNull(controller.state.email.suggestion)
        assertNull(controller.state.email.codeSentFor)
        assertTrue(controller.state.message.orEmpty().contains("novo e-mail"))
        assertFalse(controller.state.busy)
    }

    @Test fun dismissingKeepsTheCurrentEmailAndClearsTheNotice() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withSuggestion(api)
        controller.dismissEmailSuggestion()
        assertEquals(listOf("own.dismiss"), api.callsOf("own.dismiss"))
        assertNull(controller.state.email.suggestion)
        assertTrue(api.confirmedCodes.isEmpty())
    }

    @Test fun anEmailAlreadyUsedByAnotherAccountKeepsTheSuggestionAndSaysSo() = runBlocking {
        val api = FamilyFakeApi().apply { confirmFailure = failure(409, "conflict", ApiContext.EMAIL_CHANGE) }
        val controller = withSuggestion(api)
        controller.sendEmailCode()
        assertFalse(controller.confirmEmailCode("123456"))
        assertEquals(suggestion, controller.state.email.suggestion)
        assertEquals("Este e-mail já é usado por outra conta do NORA, então nada foi trocado. Procure a equipe da UBS.", controller.state.message)
    }

    @Test fun withoutMailConfiguredTheServerAnswers503AndTheMessageIsSpecific() = runBlocking {
        val api = FamilyFakeApi().apply { sendFailure = failure(503, "service-unavailable", ApiContext.EMAIL_CHANGE) }
        val controller = withSuggestion(api)
        controller.sendEmailCode()
        assertNull(controller.state.email.codeSentFor)
        assertEquals("O envio de e-mails não está disponível agora. Tente novamente mais tarde.", controller.state.message)
    }

    @Test fun tooManySendsHaveTheirOwnMessage() = runBlocking {
        val api = FamilyFakeApi().apply { sendFailure = failure(429, "too-many-requests", ApiContext.EMAIL_CHANGE) }
        val controller = withSuggestion(api)
        controller.sendEmailCode()
        assertTrue(controller.state.message.orEmpty().startsWith("Muitos pedidos de código para este e-mail"))
    }

    @Test fun aSuggestionThatNoLongerExistsDisappears() = runBlocking {
        val api = FamilyFakeApi().apply { sendFailure = failure(404, "not-found", ApiContext.EMAIL_CHANGE) }
        val controller = withSuggestion(api)
        controller.sendEmailCode()
        assertNull(controller.state.email.suggestion)
        assertEquals("Esta sugestão de e-mail não existe mais.", controller.state.message)
    }

    @Test fun anInvalidCodeAnswerFromTheServerIsAMessageNotACrash() = runBlocking {
        val api = FamilyFakeApi().apply { confirmFailure = failure(400, "invalid-input", ApiContext.EMAIL_CHANGE) }
        val controller = withSuggestion(api)
        controller.sendEmailCode()
        assertFalse(controller.confirmEmailCode("123456"))
        assertEquals("Confira o código informado.", controller.state.message)
        assertFalse(controller.state.busy)
    }
}
