package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.Assert.*
import org.junit.Test

class ApiMessagesTest {
    @Test fun forbiddenInsideAUnitExplainsTheLinkProblem() {
        for (context in listOf(ApiContext.UNIT, ApiContext.APPOINTMENT_CANCEL, ApiContext.OFFER_RESPOND))
            assertEquals(UNIT_FORBIDDEN_MESSAGE, failureMessage(403, "forbidden", context))
        assertEquals("Seu cadastro nesta UBS está desativado ou sem vínculo. Procure a unidade.", UNIT_FORBIDDEN_MESSAGE)
        assertNotEquals(UNIT_FORBIDDEN_MESSAGE, failureMessage(403, null, ApiContext.UNIT_LIST))
    }

    @Test fun conflictDependsOnTheOperation() {
        assertEquals("Esta oportunidade não está mais disponível.", failureMessage(409, "conflict", ApiContext.OFFER_RESPOND))
        assertTrue(failureMessage(409, null, ApiContext.APPOINTMENT_CANCEL).startsWith("Esta consulta já foi alterada"))
        assertNotEquals(failureMessage(409, null, ApiContext.OFFER_RESPOND), failureMessage(409, null, ApiContext.REGISTRATION))
    }

    @Test fun statusAloneIsEnoughWhenThereIsNoCode() {
        assertEquals("CPF, e-mail ou senha inválidos.", failureMessage(401, null, ApiContext.LOGIN))
        assertEquals("Sua sessão terminou. Entre novamente.", failureMessage(401, null, ApiContext.UNIT))
        assertTrue(failureMessage(404, null, ApiContext.APPOINTMENT_CANCEL).contains("consulta"))
        assertTrue(failureMessage(404, null, ApiContext.OFFER_RESPOND).contains("oportunidade"))
        assertTrue(failureMessage(429, null, ApiContext.PASSWORD_RESET).contains("código"))
        assertTrue(failureMessage(429, null, ApiContext.LOGIN).startsWith("Muitas tentativas"))
        assertTrue(failureMessage(503, null, ApiContext.UNIT).contains("UBS"))
        assertTrue(failureMessage(503, null, ApiContext.LOGIN).contains("serviço"))
        assertEquals(failureMessage(500, "internal-error", ApiContext.GENERIC), failureMessage(502, null, ApiContext.GENERIC))
        assertEquals("Não foi possível concluir a solicitação. Tente novamente.", failureMessage(418, null, ApiContext.GENERIC))
    }

    @Test fun onlyTheFixedCodeVocabularyIsKept() {
        for (code in listOf("invalid-input", "unauthorized", "forbidden", "not-found", "conflict", "too-many-requests", "internal-error", "service-unavailable"))
            assertEquals(code, knownProblemCode(code))
        assertNull(knownProblemCode("method-not-allowed"))
        assertNull(knownProblemCode("<script>"))
        assertNull(knownProblemCode(""))
        assertNull(knownProblemCode(null))
        // An unknown code falls back to the status.
        assertEquals(failureMessage(409, null, ApiContext.OFFER_RESPOND), failureMessage(409, "bogus", ApiContext.OFFER_RESPOND))
    }

    @Test fun passwordAndQueueMessagesAreSpecific() {
        assertEquals("A senha atual está incorreta.", failureMessage(400, INVALID_CURRENT_PASSWORD, ApiContext.PASSWORD_CHANGE))
        assertTrue(failureMessage(400, "invalid-input", ApiContext.PASSWORD_CHANGE).contains("8 a 256"))
        for (context in ApiContext.entries)
            assertEquals(PASSWORD_CHANGE_REQUIRED_MESSAGE, failureMessage(403, PASSWORD_CHANGE_REQUIRED, context))
        assertEquals("Esta solicitação não foi encontrada.", failureMessage(404, null, ApiContext.QUEUE_CANCEL))
        assertEquals("Esta solicitação já foi encerrada.", failureMessage(409, null, ApiContext.QUEUE_CANCEL))
        assertEquals(UNIT_FORBIDDEN_MESSAGE, failureMessage(403, "forbidden", ApiContext.QUEUE_CANCEL))
        assertEquals(UNIT_FORBIDDEN_MESSAGE, failureMessage(403, "forbidden", ApiContext.UNLINK))
        assertEquals(INVALID_CURRENT_PASSWORD, knownProblemCode(INVALID_CURRENT_PASSWORD))
        assertEquals(PASSWORD_CHANGE_REQUIRED, knownProblemCode(PASSWORD_CHANGE_REQUIRED))
    }
}
