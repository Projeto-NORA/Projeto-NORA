package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.Assert.*
import org.junit.Test

class NotificationDecisionTest {
    private val now = requireNotNull(parseInstantMillis("2026-11-22T12:00:00Z"))
    private val unit = PatientHealthUnit("u", "UBS", "", "", 0.0, 0.0)
    private val offer = UnitOffer("o", "Clínica", "Profissional", "2026-11-23T12:00:00Z", "2026-11-23T12:30:00Z", "2026-11-22T12:10:00Z", "pending")
    private val patient = PatientState(session = PatientSession("token", "2027-01-01T00:00:00Z", PatientUser("p", "Paciente")),
        units = listOf(unit), selectedUnitId = unit.id, unitData = mapOf(unit.id to UnitSnapshot(offers = listOf(offer))))

    @Test fun requiresTheCorrectOpenOfferAndUnit() {
        assertEquals(offer, notificationDecisionOffer(patient, "o", "u", null, now))
        assertNull(notificationDecisionOffer(patient, "outro", "u", null, now))
        assertNull(notificationDecisionOffer(patient, "o", "outra", null, now))
        assertNull(notificationDecisionOffer(patient, "o", "u", "dependente", now))
    }

    @Test fun rejectsExpiredOffersAndUnavailableSessions() {
        assertNull(notificationDecisionOffer(patient, "o", "u", null, now + 601_000))
        assertNull(notificationDecisionOffer(patient.copy(session = null), "o", "u", null, now))
        assertNull(notificationDecisionOffer(patient.copy(busy = true), "o", "u", null, now))
        assertNull(notificationDecisionOffer(patient.copy(actingFor = "sem-vinculo"), "o", "u", "sem-vinculo", now))
    }
}
