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

    @Test fun schedulingCodesHaveTheirOwnMessageAndAreKept() {
        val codes = listOf(BOOKING_WINDOW, SPECIALTY_LIMIT, SLOT_UNAVAILABLE, PATIENT_OVERLAP, CONFIRMATION_WINDOW)
        for (code in codes) assertEquals(code, knownProblemCode(code))
        val texts = codes.map { failureMessage(409, it, ApiContext.BOOKING) }
        assertEquals(codes.size, texts.toSet().size)
        assertTrue(texts[0].contains("24 horas") && texts[0].contains("90 dias"))
        assertTrue(texts[1].contains("nesta especialidade") && texts[1].contains("UBS"))
        assertTrue(texts[2].contains("ocupado"))
        assertTrue(texts[3].contains("mesmo horário"))
        assertTrue(texts[4].contains("48 horas") && texts[4].contains("2 horas"))
        // O código manda, não o contexto: o texto é o mesmo onde quer que o servidor o devolva.
        assertEquals(texts[4], failureMessage(409, CONFIRMATION_WINDOW, ApiContext.CONFIRMATION))
    }

    @Test fun bookingAvailabilityAndConfirmationContextsKeepTheUnitMessages() {
        for (context in listOf(ApiContext.AVAILABILITY, ApiContext.BOOKING, ApiContext.CONFIRMATION))
            assertEquals(UNIT_FORBIDDEN_MESSAGE, failureMessage(403, "forbidden", context))
        assertTrue(failureMessage(503, null, ApiContext.AVAILABILITY).contains("UBS"))
        // Marcar confere todas as UBS vinculadas: 503 pede nova tentativa, sem culpar uma unidade só.
        assertTrue(failureMessage(503, null, ApiContext.BOOKING).contains("todas as suas UBS"))
        assertTrue(failureMessage(400, null, ApiContext.BOOKING).contains("horário"))
        assertTrue(failureMessage(404, null, ApiContext.CONFIRMATION).contains("consulta"))
        assertTrue(failureMessage(409, null, ApiContext.CONFIRMATION).contains("confirmada"))
        assertTrue(failureMessage(429, null, ApiContext.BOOKING).startsWith("Muitas tentativas"))
        assertEquals("Sua sessão terminou. Entre novamente.", failureMessage(401, null, ApiContext.BOOKING))
    }

    @Test fun familyCodesHaveTheirOwnMessagesAndAreKept() {
        val codes = listOf(FAMILY_LINK_EXISTS, FAMILY_LINK_LIMIT, FAMILY_LINK_CLOSED, DEPENDENT_INACTIVE)
        for (code in codes) assertEquals(code, knownProblemCode(code))
        val texts = codes.map { failureMessage(409, it, ApiContext.FAMILY_REQUEST) }
        assertEquals(codes.size, texts.toSet().size)
        assertTrue(texts[0].contains("pedido pendente") && texts[1].contains("20"))
        assertEquals("Não encontramos um cadastro ativo com este CPF nesta UBS. Confira o CPF e a UBS ou procure a equipe da unidade.",
            failureMessage(404, "not-found", ApiContext.FAMILY_REQUEST))
        assertTrue(failureMessage(400, "invalid-input", ApiContext.FAMILY_REQUEST).contains("próprio CPF"))
    }

    @Test fun actingForADependentChangesTheForbiddenAndBookingMessages() {
        assertEquals(UNIT_FORBIDDEN_MESSAGE, failureMessage(403, "forbidden", ApiContext.UNIT))
        assertEquals(DEPENDENT_FORBIDDEN_MESSAGE, failureMessage(403, "forbidden", ApiContext.UNIT, acting = true))
        for (context in listOf(ApiContext.APPOINTMENT_CANCEL, ApiContext.OFFER_RESPOND, ApiContext.QUEUE_CANCEL, ApiContext.BOOKING, ApiContext.CONFIRMATION, ApiContext.RECORDS))
            assertEquals(DEPENDENT_FORBIDDEN_MESSAGE, failureMessage(403, "forbidden", context, acting = true))
        assertTrue(failureMessage(409, SPECIALTY_LIMIT, ApiContext.BOOKING, acting = true).startsWith("Esta pessoa"))
        assertTrue(failureMessage(409, SPECIALTY_LIMIT, ApiContext.BOOKING).startsWith("Você"))
        assertTrue(failureMessage(409, PATIENT_OVERLAP, ApiContext.BOOKING, acting = true).startsWith("Esta pessoa"))
        // Fora de contextos de UBS, "agindo por" não muda o texto.
        assertEquals(failureMessage(403, "forbidden", ApiContext.FAMILY), failureMessage(403, "forbidden", ApiContext.FAMILY, acting = true))
    }

    @Test fun emailChangeAndCommunityContextsHaveSpecificTexts() {
        assertTrue(failureMessage(409, "conflict", ApiContext.EMAIL_CHANGE).contains("outra conta"))
        assertTrue(failureMessage(503, "service-unavailable", ApiContext.EMAIL_CHANGE).contains("e-mails"))
        assertTrue(failureMessage(429, "too-many-requests", ApiContext.EMAIL_CHANGE).contains("código"))
        assertTrue(failureMessage(404, "not-found", ApiContext.EMAIL_CHANGE).contains("sugestão"))
        assertEquals("Confira o código informado.", failureMessage(400, "invalid-input", ApiContext.EMAIL_CHANGE))
        assertTrue(failureMessage(503, "service-unavailable", ApiContext.RECORDS).contains("UBS"))
        assertEquals(UNIT_FORBIDDEN_MESSAGE, failureMessage(403, "forbidden", ApiContext.RECORDS))
        assertEquals("Sua sessão terminou. Entre novamente.", failureMessage(401, null, ApiContext.ANNOUNCEMENTS))
    }
}
