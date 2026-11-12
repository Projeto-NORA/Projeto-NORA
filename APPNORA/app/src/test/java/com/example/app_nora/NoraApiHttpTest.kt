package com.example.app_nora

import com.example.app_nora.api.*
import com.sun.net.httpserver.HttpExchange
import com.sun.net.httpserver.HttpServer
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import java.net.InetSocketAddress

/** Real HTTP round trips against a loopback server: JSON shapes and problem+json handling of NoraApi. */
class NoraApiHttpTest {
    private lateinit var server: HttpServer
    private val unitId = "11111111-2222-3333-4444-555555555555"
    private val offerId = "99999999-8888-7777-6666-555555555555"
    private var lastAuthorization: String? = null
    private var lastBody = ""

    @Before fun start() {
        server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.start()
    }
    @After fun stop() { server.stop(0) }

    private fun api() = NoraApi("http://127.0.0.1:${server.address.port}")

    private fun route(path: String, status: Int, body: String) {
        server.createContext(path) { exchange: HttpExchange ->
            lastAuthorization = exchange.requestHeaders.getFirst("Authorization")
            lastBody = exchange.requestBody.readBytes().toString(Charsets.UTF_8)
            val bytes = body.toByteArray(Charsets.UTF_8)
            exchange.responseHeaders.add("Content-Type", if (status in 200..299) "application/json" else "application/problem+json")
            exchange.sendResponseHeaders(status, bytes.size.toLong())
            exchange.responseBody.use { it.write(bytes) }
        }
    }

    @Test fun offersCarryTheEndOfTheConsultationAndTheDeadline() = runBlocking {
        route("/api/patient/units/$unitId/lightning-offers", 200,
            """{"offers":[{"id":"$offerId","slotId":"x","specialty":"Cardiologia","professional":"Dra. Teste","startsAt":"2026-11-12T12:00:00+00:00","endsAt":"2026-11-12T12:30:00+00:00","expiresAt":"2026-11-11T12:10:00.1234567+00:00","status":"pending"}]}""")
        val offer = api().unitOffers("token-x", unitId).single()
        assertEquals("2026-11-12T12:30:00+00:00", offer.endsAt)
        assertEquals("pending", offer.status)
        assertNotNull(offer.expiresAtMillis)
        assertEquals("Bearer token-x", lastAuthorization)
    }

    @Test fun appointmentsCarryTheEndOfTheConsultation() = runBlocking {
        route("/api/patient/units/$unitId/appointments", 200,
            """{"appointments":[{"id":"$offerId","cpf":"1","patientName":"n","specialty":"Clínica","professional":"P","startsAt":"2026-11-12T12:00:00Z","endsAt":"2026-11-12T12:45:00Z","status":"scheduled"}]}""")
        val appointment = api().appointments("token-x", unitId).single()
        assertEquals("2026-11-12T12:45:00Z", appointment.endsAt)
        assertNotNull(appointment.startsAtMillis)
    }

    @Test fun respondKeepsAcceptedAndAppointmentIdFromTheServer() = runBlocking {
        route("/api/patient/units/$unitId/lightning-offers/$offerId/respond", 200, """{"accepted":true,"appointmentId":"abc"}""")
        val accepted = api().respondUnitOffer("t", unitId, offerId, true)
        assertTrue(accepted.accepted); assertEquals("abc", accepted.appointmentId)
        assertEquals("""{"accept":true}""", lastBody)
    }

    @Test fun declineResultHasNoAppointment() = runBlocking {
        route("/api/patient/units/$unitId/lightning-offers/$offerId/respond", 200, """{"accepted":false,"appointmentId":null}""")
        val result = api().respondUnitOffer("t", unitId, offerId, false)
        assertFalse(result.accepted); assertNull(result.appointmentId)
    }

    @Test fun problemCodeSelectsTheMessageAndServerTextIsNeverShown() = runBlocking {
        route("/api/patient/units/$unitId/lightning-offers/$offerId/respond", 409,
            """{"type":"x","title":"SEGREDO-DO-SERVIDOR","status":409,"detail":"SEGREDO-DO-SERVIDOR","code":"conflict","traceId":"t"}""")
        try { api().respondUnitOffer("t", unitId, offerId, true); fail("expected failure") }
        catch (failure: ApiFailure) {
            assertEquals(409, failure.status); assertEquals("conflict", failure.code)
            assertEquals(OFFER_UNAVAILABLE_MESSAGE, failure.message)
            assertFalse(failure.message.orEmpty().contains("SEGREDO"))
        }
    }

    @Test fun forbiddenInsideAUnitUsesTheLinkMessage() = runBlocking {
        route("/api/patient/units/$unitId/waiting-list", 403, """{"title":"t","status":403,"code":"forbidden"}""")
        try { api().unitQueue("t", unitId); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals(UNIT_FORBIDDEN_MESSAGE, failure.message) }
    }

    @Test fun unknownCodeOrNonJsonErrorBodyFallsBackToTheStatus() = runBlocking {
        route("/api/patient/units/$unitId/waiting-list", 503, "<html>proxy error</html>")
        try { api().unitQueue("t", unitId); fail("expected failure") }
        catch (failure: ApiFailure) {
            assertNull(failure.code)
            assertEquals(failureMessage(503, null, ApiContext.UNIT), failure.message)
        }
        route("/api/auth/login", 401, """{"code":"<script>alert(1)</script>"}""")
        try { api().login("a", "b"); fail("expected failure") }
        catch (failure: ApiFailure) {
            assertNull(failure.code)
            assertEquals("CPF, e-mail ou senha inválidos.", failure.message)
        }
    }

    @Test fun nonPatientAccountIsRefusedWithoutServerText() = runBlocking {
        route("/api/auth/login", 200, """{"token":"t","expiresAt":"2099-01-01T00:00:00Z","user":{"id":"i","fullName":"n","scope":"obs"}}""")
        try { api().login("a", "b"); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals(403, failure.status) }
    }
}
