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
    private var lastUri = ""
    private var lastMethod = ""

    @Before fun start() {
        server = HttpServer.create(InetSocketAddress("127.0.0.1", 0), 0)
        server.start()
    }
    @After fun stop() { server.stop(0) }

    private fun api() = NoraApi("http://127.0.0.1:${server.address.port}")

    private fun route(path: String, status: Int, body: String) {
        try { server.removeContext(path) } catch (_: IllegalArgumentException) { /* primeira vez */ }
        server.createContext(path) { exchange: HttpExchange ->
            lastAuthorization = exchange.requestHeaders.getFirst("Authorization")
            lastBody = exchange.requestBody.readBytes().toString(Charsets.UTF_8)
            lastUri = exchange.requestURI.toString()
            lastMethod = exchange.requestMethod
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

    @Test fun attendanceParsesTheScoreAndANullScore() = runBlocking {
        route("/api/patient/units/$unitId/attendance", 200,
            """{"completed":4,"missed":1,"cancelledByPatient":2,"cancellationWeight":0.32,"score":0.7547,"windowMonths":12}""")
        val value = api().attendance("token-x", unitId)
        assertEquals(4, value.completed); assertEquals(1, value.missed); assertEquals(2, value.cancelledByPatient)
        assertEquals(0.32, value.cancellationWeight, 1e-9); assertEquals(0.7547, value.score!!, 1e-9); assertEquals(12, value.windowMonths)
        assertEquals("Bearer token-x", lastAuthorization)
        route("/api/patient/units/$unitId/attendance", 200,
            """{"completed":0,"missed":0,"cancelledByPatient":0,"cancellationWeight":0,"score":null,"windowMonths":12}""")
        assertNull(api().attendance("token-x", unitId).score)
    }

    @Test fun leavingTheQueueSendsConfirmedAndAcceptsNoContent() = runBlocking {
        route("/api/patient/units/$unitId/waiting-list/42/cancel", 204, "")
        api().cancelQueueEntry("token-x", unitId, 42)
        assertEquals("""{"confirmed":true}""", lastBody)
        route("/api/patient/units/$unitId/waiting-list/43/cancel", 409, """{"code":"conflict","title":"SEGREDO"}""")
        try { api().cancelQueueEntry("t", unitId, 43); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals("Esta solicitação já foi encerrada.", failure.message) }
        route("/api/patient/units/$unitId/waiting-list/44/cancel", 404, """{"code":"not-found"}""")
        try { api().cancelQueueEntry("t", unitId, 44); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals("Esta solicitação não foi encontrada.", failure.message) }
    }

    @Test fun unlinkParsesTheCancelledNumbers() = runBlocking {
        route("/api/patient/units/$unitId/unlink", 200, """{"cancelledAppointments":3,"cancelledEntries":1}""")
        val result = api().unlinkUnit("token-x", unitId)
        assertEquals(UnlinkResult(3, 1), result)
        assertEquals("""{"confirmed":true}""", lastBody)
    }

    @Test fun invalidIdentifiersNeverBecomePathSegments() = runBlocking {
        for (call in listOf<suspend () -> Unit>({ api().attendance("t", "../x") }, { api().unlinkUnit("t", "x") }, { api().cancelQueueEntry("t", unitId, 0) })) {
            try { call(); fail("expected failure") } catch (_: IllegalArgumentException) {}
        }
    }

    @Test fun changePasswordSendsBothPasswordsAndMapsTheSpecificCodes() = runBlocking {
        route("/api/auth/password", 204, "")
        api().changePassword("token-x", "atual-teste", "nova-teste-1")
        org.json.JSONObject(lastBody).let {
            assertEquals("atual-teste", it.getString("currentPassword")); assertEquals("nova-teste-1", it.getString("newPassword")); assertEquals(2, it.length())
        }
        assertEquals("Bearer token-x", lastAuthorization)
        route("/api/auth/password", 400, """{"code":"invalid-current-password","title":"SEGREDO","detail":"SEGREDO"}""")
        try { api().changePassword("t", "x", "y"); fail("expected failure") }
        catch (failure: ApiFailure) {
            assertEquals(400, failure.status); assertEquals("invalid-current-password", failure.code)
            assertEquals("A senha atual está incorreta.", failure.message)
        }
        route("/api/patient/units/$unitId/waiting-list", 403, """{"code":"password-change-required","title":"SEGREDO"}""")
        try { api().unitQueue("t", unitId); fail("expected failure") }
        catch (failure: ApiFailure) {
            assertEquals(PASSWORD_CHANGE_REQUIRED, failure.code)
            assertEquals(PASSWORD_CHANGE_REQUIRED_MESSAGE, failure.message)
        }
    }

    private val specialtyId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    private val professionalId = "12345678-1234-1234-1234-123456789012"
    private val slotJson = """{"slots":[{"startsAt":"2026-11-20T12:00:00+00:00","endsAt":"2026-11-20T12:30:00+00:00","professionalId":"12345678-1234-1234-1234-123456789012","professionalName":"Dra. Ana","specialtyId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","specialtyName":"Cardiologia","roomId":"11111111-2222-3333-4444-555555555555","roomName":"Sala 1"}]}"""

    @Test fun unlinkPreviewReadsTheExactNumbersTheRemovalWillCancel() = runBlocking {
        route("/api/patient/units/$unitId/unlink-preview", 200, """{"futureAppointments":4,"activeWaitingEntries":2}""")
        assertEquals(UnlinkImpact(4, 2), api().unlinkPreview("token-x", unitId))
        assertEquals("GET", lastMethod); assertEquals("/api/patient/units/$unitId/unlink-preview", lastUri)
        assertEquals("Bearer token-x", lastAuthorization)
        route("/api/patient/units/$unitId/unlink-preview", 503, """{"title":"SEGREDO","status":503}""")
        try { api().unlinkPreview("t", unitId); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals(503, failure.status); assertFalse(failure.message.orEmpty().contains("SEGREDO")) }
        try { api().unlinkPreview("t", "../x"); fail("expected failure") } catch (_: IllegalArgumentException) {}
    }

    @Test fun specialtiesAndAvailabilityAreParsedAndTheQueryCarriesTheWindow() = runBlocking {
        route("/api/patient/specialties", 200, """{"specialties":[{"id":"$specialtyId","name":"Cardiologia","isActive":true}]}""")
        assertEquals(listOf(Specialty(specialtyId, "Cardiologia")), api().specialties("token-x"))
        assertEquals("Bearer token-x", lastAuthorization)
        var query = ""
        server.createContext("/api/patient/units/$unitId/availability") { exchange ->
            query = exchange.requestURI.rawQuery
            val bytes = slotJson.toByteArray()
            exchange.responseHeaders.add("Content-Type", "application/json")
            exchange.sendResponseHeaders(200, bytes.size.toLong()); exchange.responseBody.use { it.write(bytes) }
        }
        val from = 1_800_000_000_000L
        val to = from + 7 * 86_400_000L
        val slot = api().availability("token-x", unitId, specialtyId, from, to).single()
        assertEquals("2026-11-20T12:00:00+00:00", slot.startsAt); assertEquals(professionalId, slot.professionalId)
        assertEquals("Dra. Ana", slot.professionalName); assertNotNull(slot.startsAtMillis)
        assertTrue(query, query.contains("specialtyId=$specialtyId"))
        assertTrue(query, query.contains("from=${formatInstantUtc(from).replace(":", "%3A")}"))
        assertTrue(query, query.contains("to=${formatInstantUtc(to).replace(":", "%3A")}"))
    }

    @Test fun bookingSendsTheCatalogChoiceAsIsAndParsesTheNewAppointment() = runBlocking {
        route("/api/patient/units/$unitId/appointments", 201,
            """{"id":"$offerId","cpf":"1","patientName":"n","specialty":"Cardiologia","professional":"Dra. Ana","startsAt":"2026-11-20T12:00:00+00:00","endsAt":"2026-11-20T12:30:00+00:00","status":"scheduled","confirmedAt":null,"bookedBy":"patient","professionalId":"$professionalId","specialtyId":"$specialtyId"}""")
        val created = api().bookAppointment("token-x", unitId, professionalId, specialtyId, "2026-11-20T12:00:00+00:00")
        org.json.JSONObject(lastBody).let {
            assertEquals(professionalId, it.getString("professionalId")); assertEquals(specialtyId, it.getString("specialtyId"))
            assertEquals("2026-11-20T12:00:00+00:00", it.getString("startsAt")); assertEquals(3, it.length())
        }
        assertEquals("patient", created.bookedBy); assertNull(created.confirmedAt); assertEquals("scheduled", created.status)
        assertEquals(offerId, created.id)
    }

    @Test fun everyBookingRefusalCodeSelectsItsMessage() = runBlocking {
        for (code in listOf(BOOKING_WINDOW, SPECIALTY_LIMIT, SLOT_UNAVAILABLE, PATIENT_OVERLAP)) {
            route("/api/patient/units/$unitId/appointments", 409, """{"title":"SEGREDO","detail":"SEGREDO","status":409,"code":"$code"}""")
            try { api().bookAppointment("t", unitId, professionalId, specialtyId, "2026-11-20T12:00:00Z"); fail("expected failure") }
            catch (failure: ApiFailure) {
                assertEquals(409, failure.status); assertEquals(code, failure.code)
                assertEquals(failureMessage(409, code, ApiContext.BOOKING), failure.message)
                assertFalse(failure.message.orEmpty().contains("SEGREDO"))
            }
        }
    }

    @Test fun confirmingPresenceParsesTheConfirmationAndMapsTheWindowRefusal() = runBlocking {
        route("/api/patient/units/$unitId/appointments/$offerId/confirm", 200,
            """{"id":"$offerId","cpf":"1","patientName":"n","specialty":"Cardiologia","professional":"Dra. Ana","startsAt":"2026-11-20T12:00:00Z","endsAt":"2026-11-20T12:30:00Z","status":"scheduled","confirmedAt":"2026-11-19T09:30:00.123+00:00","bookedBy":"staff"}""")
        val confirmed = api().confirmPresence("token-x", unitId, offerId)
        assertEquals("2026-11-19T09:30:00.123+00:00", confirmed.confirmedAt); assertEquals("", lastBody)
        assertEquals("Bearer token-x", lastAuthorization)
        route("/api/patient/units/$unitId/appointments/$offerId/confirm", 409, """{"code":"confirmation-window","title":"SEGREDO"}""")
        try { api().confirmPresence("t", unitId, offerId); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals(failureMessage(409, CONFIRMATION_WINDOW, ApiContext.CONFIRMATION), failure.message) }
    }

    @Test fun appointmentsWithoutTheNewFieldsStillParse() = runBlocking {
        route("/api/patient/units/$unitId/appointments", 200,
            """{"appointments":[{"id":"$offerId","specialty":"Clínica","professional":"P","startsAt":"2026-11-12T12:00:00Z","endsAt":"2026-11-12T12:45:00Z","status":"scheduled"},{"id":"$unitId","specialty":"Clínica","professional":"P","startsAt":"2026-11-13T12:00:00Z","endsAt":"2026-11-13T12:45:00Z","status":"scheduled","confirmedAt":"2026-11-12T12:00:00Z","bookedBy":"lightning"}]}""")
        val items = api().appointments("t", unitId)
        assertEquals("staff", items[0].bookedBy); assertNull(items[0].confirmedAt)
        assertEquals("lightning", items[1].bookedBy); assertEquals("2026-11-12T12:00:00Z", items[1].confirmedAt)
        assertFalse(items[0].bookedByYou); assertNull(items[0].bookedByGuardianName)
    }

    @Test fun whoBookedIsParsedFromTheOwnTheDelegatedAndTheBookingAnswers() = runBlocking {
        fun body(byYou: Boolean, guardian: String?) = """{"id":"$offerId","specialty":"Clínica","professional":"P","startsAt":"2026-11-20T12:00:00Z",""" +
            """"endsAt":"2026-11-20T12:30:00Z","status":"scheduled","confirmedAt":null,"bookedBy":"patient","bookedByYou":$byYou,""" +
            """"bookedByGuardianName":${guardian?.let { "\"$it\"" } ?: "null"}}"""
        route("/api/patient/units/$unitId/appointments", 200, """{"appointments":[${body(false, "Ana Lima")}]}""")
        api().appointments("t", unitId).single().let { assertFalse(it.bookedByYou); assertEquals("Ana Lima", it.bookedByGuardianName) }
        route("$dependentBase/appointments", 200, """{"appointments":[${body(true, null)}]}""")
        api().dependentAppointments("t", dependentId, unitId).single().let { assertTrue(it.bookedByYou); assertNull(it.bookedByGuardianName) }
        route("/api/patient/units/$unitId/appointments", 201, body(true, null))
        assertTrue(api().bookAppointment("t", unitId, professionalId, specialtyId, "2026-11-20T12:00:00Z").bookedByYou)
    }

    @Test fun schedulingIdentifiersNeverBecomePathOrQuery() = runBlocking {
        for (call in listOf<suspend () -> Unit>(
            { api().availability("t", unitId, "x&y=1", 0, 1) }, { api().availability("t", "../x", specialtyId, 0, 1) },
            { api().bookAppointment("t", unitId, "x", specialtyId, "2026-11-20T12:00:00Z") },
            { api().confirmPresence("t", unitId, "../../x") })) {
            try { call(); fail("expected failure") } catch (_: IllegalArgumentException) {}
        }
    }

    // ---- Comunicados, Jornada, família e e-mail da conta -------------------------------------------------------------

    private val dependentId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    private val dependentBase get() = "/api/patient/dependents/$dependentId/units/$unitId"

    @Test fun announcementsParseTheIncrementalPageAndTheCursorTravelsInTheQuery() = runBlocking {
        route("/api/patient/announcements", 200,
            """{"announcements":[{"id":"$offerId","sequence":42,"title":"Vacinação <b>hoje</b>","body":"Linha 1\nLinha 2","publishedAt":"2026-11-16T10:00:00+00:00","expiresAt":null,"isRead":false,"readAt":null}],"cursor":42,"hasMore":true}""")
        val page = api().announcements("token-x", null)
        assertEquals("/api/patient/announcements", lastUri)
        assertEquals(42L, page.cursor); assertTrue(page.hasMore)
        val item = page.announcements.single()
        // Texto puro: nada é interpretado nem removido.
        assertEquals("Vacinação <b>hoje</b>", item.title); assertEquals("Linha 1\nLinha 2", item.body)
        assertNull(item.expiresAt); assertFalse(item.isRead); assertEquals(42L, item.sequence)
        api().announcements("token-x", 42)
        assertEquals("/api/patient/announcements?after=42", lastUri)
        assertEquals("Bearer token-x", lastAuthorization)
    }

    @Test fun markingAnAnnouncementReadIsAPostWithoutBodyAndTheIdNeverBecomesPath() = runBlocking {
        route("/api/patient/announcements/$offerId/read", 204, "")
        api().markAnnouncementRead("t", offerId)
        assertEquals("POST", lastMethod); assertEquals("/api/patient/announcements/$offerId/read", lastUri); assertEquals("", lastBody)
        try { api().markAnnouncementRead("t", "../x"); fail("expected failure") } catch (_: IllegalArgumentException) {}
    }

    @Test fun healthRecordsParseExamsAndVaccinesAndNeverReadTheVoidReason() = runBlocking {
        route("/api/patient/health-records", 200,
            """{"units":[{"unitId":"$unitId","unitName":"UBS Centro","exams":[{"id":"$offerId","name":"Hemograma","status":"done","requestedAt":"2026-11-10T12:00:00+00:00","performedOn":"2026-11-12","note":"Jejum","voidedAt":null,"voidReason":"SEGREDO"}],"vaccines":[{"id":"$dependentId","name":"Gripe","dose":"booster","appliedOn":"2026-05-01","status":"applied","note":null,"voidedAt":null}]}]}""")
        val unit = api().healthRecords("t").single()
        assertEquals("UBS Centro", unit.unitName); assertEquals("/api/patient/health-records", lastUri)
        assertEquals("2026-11-12", unit.exams.single().performedOn); assertEquals("Jejum", unit.exams.single().note)
        assertEquals("booster", unit.vaccines.single().dose); assertNull(unit.vaccines.single().note)
        assertFalse(unit.toString().contains("SEGREDO"))
    }

    @Test fun dependentRecordsUseTheDelegatedRoute() = runBlocking {
        route("$dependentBase/health-records", 200, """{"exams":[],"vaccines":[]}""")
        val records = api().dependentRecords("t", dependentId, unitId)
        assertEquals("$dependentBase/health-records", lastUri); assertEquals(unitId, records.unitId)
        assertTrue(records.exams.isEmpty() && records.vaccines.isEmpty())
    }

    @Test fun dependentsListParsesStatusReasonAndTheMaskedCpf() = runBlocking {
        route("/api/patient/dependents", 200,
            """{"dependents":[{"id":"$offerId","status":"rejected","unitId":"$unitId","unitName":"UBS Centro","dependentCpfMasked":"***.456.789-**","dependentName":null,"requestedAt":"2026-11-10T12:00:00+00:00","decidedAt":"2026-11-11T12:00:00+00:00","decisionReason":"Falta a certidão","closedAt":null}]}""")
        val link = api().dependents("t").single()
        assertEquals("rejected", link.status); assertEquals("Falta a certidão", link.decisionReason)
        assertNull(link.dependentName); assertEquals("***.456.789-**", link.dependentCpfMasked); assertNull(link.closedAt)
    }

    @Test fun requestingADependentSendsCpfAndUnitInTheBodyNeverInTheUrl() = runBlocking {
        route("/api/patient/dependents", 201,
            """{"id":"$offerId","status":"pending","unitId":"$unitId","unitName":"UBS Centro","dependentCpfMasked":"***.456.789-**","dependentName":null,"requestedAt":"2026-11-10T12:00:00+00:00","decidedAt":null,"decisionReason":null,"closedAt":null}""")
        val created = api().requestDependent("t", "52998224725", unitId)
        org.json.JSONObject(lastBody).let { assertEquals("52998224725", it.getString("cpf")); assertEquals(unitId, it.getString("unitId")); assertEquals(2, it.length()) }
        assertEquals("/api/patient/dependents", lastUri); assertFalse(lastUri.contains("52998224725"))
        assertEquals("pending", created.status)
        try { api().requestDependent("t", "52998224725", "x"); fail("expected failure") } catch (_: IllegalArgumentException) {}
    }

    @Test fun cancelAndEndSendConfirmedAndTheirRefusalsHaveMessages() = runBlocking {
        val body = """{"id":"$offerId","status":"cancelled","unitId":"$unitId","unitName":"U","dependentCpfMasked":"***","dependentName":null,"requestedAt":"2026-11-10T12:00:00+00:00","decidedAt":null,"decisionReason":null,"closedAt":"2026-11-12T12:00:00+00:00"}"""
        route("/api/patient/dependents/$offerId/cancel", 200, body)
        assertEquals("cancelled", api().cancelDependentRequest("t", offerId).status)
        assertEquals("""{"confirmed":true}""", lastBody)
        route("/api/patient/dependents/$offerId/end", 409, """{"code":"family-link-closed","title":"SEGREDO"}""")
        try { api().endDependentLink("t", offerId); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals(failureMessage(409, FAMILY_LINK_CLOSED, ApiContext.FAMILY), failure.message); assertFalse(failure.message.orEmpty().contains("SEGREDO")) }
    }

    @Test fun everyDelegatedRouteGoesThroughTheDependentPrefix() = runBlocking {
        val appointment = """{"id":"$offerId","specialty":"Clínica","professional":"P","startsAt":"2026-11-20T12:00:00Z","endsAt":"2026-11-20T12:30:00Z","status":"scheduled","confirmedAt":null,"bookedBy":"patient"}"""
        route("$dependentBase/appointments", 200, """{"appointments":[$appointment]}""")
        assertEquals(1, api().dependentAppointments("t", dependentId, unitId).size)
        assertTrue(lastUri.startsWith("$dependentBase/appointments?from="))
        route("$dependentBase/availability", 200, """{"slots":[]}""")
        api().dependentAvailability("t", dependentId, unitId, specialtyId, 0, 86_400_000)
        assertTrue(lastUri.startsWith("$dependentBase/availability?specialtyId=$specialtyId&from="))
        route("$dependentBase/appointments", 201, appointment)
        api().dependentBook("t", dependentId, unitId, professionalId, specialtyId, "2026-11-20T12:00:00Z")
        assertEquals("POST", lastMethod); assertEquals("$dependentBase/appointments", lastUri)
        route("$dependentBase/appointments/$offerId/confirm", 200, appointment)
        api().dependentConfirmPresence("t", dependentId, unitId, offerId)
        assertEquals("$dependentBase/appointments/$offerId/confirm", lastUri)
        route("$dependentBase/appointments/$offerId/cancel", 200, appointment)
        api().dependentCancelAppointment("t", dependentId, unitId, offerId)
        assertEquals("""{"confirmed":true}""", lastBody)
        route("$dependentBase/waiting-list", 200, """{"entries":[{"id":7,"cpf":"x","patientName":"n","specialty":"Pediatria","enteredAt":"2026-11-10T12:00:00Z","status":"waiting"}]}""")
        assertEquals(7L, api().dependentQueue("t", dependentId, unitId).single().id)
        route("$dependentBase/waiting-list/7/cancel", 204, "")
        api().dependentCancelQueueEntry("t", dependentId, unitId, 7)
        assertEquals("$dependentBase/waiting-list/7/cancel", lastUri)
        route("$dependentBase/lightning-offers", 200, """{"offers":[]}""")
        assertTrue(api().dependentUnitOffers("t", dependentId, unitId).isEmpty())
        route("$dependentBase/lightning-offers/$offerId/respond", 200, """{"accepted":true,"appointmentId":"abc"}""")
        assertTrue(api().dependentRespondOffer("t", dependentId, unitId, offerId, true).accepted)
        assertEquals("""{"accept":true}""", lastBody)
        route("$dependentBase/attendance", 200, """{"completed":1,"missed":0,"cancelledByPatient":0,"cancellationWeight":0.0,"score":1.0,"windowMonths":12}""")
        assertEquals(1.0, api().dependentAttendance("t", dependentId, unitId).score!!, 0.0)
        assertEquals("$dependentBase/attendance", lastUri)
    }

    @Test fun aDependentOrUnitIdentifierNeverBecomesPath() = runBlocking {
        for (call in listOf<suspend () -> Unit>(
            { api().dependentAppointments("t", "../x", unitId) }, { api().dependentQueue("t", dependentId, "x/y") },
            { api().dependentBook("t", dependentId, unitId, "x", specialtyId, "2026-11-20T12:00:00Z") })) {
            try { call(); fail("expected failure") } catch (_: IllegalArgumentException) {}
        }
    }

    @Test fun aggregatedDependentOffersParseTheNameAndTheOffers() = runBlocking {
        route("/api/patient/dependents/lightning-offers", 200,
            """{"dependents":[{"dependentId":"$dependentId","unitId":"$unitId","unitName":"UBS Centro","dependentName":"Maria Souza","offers":[{"id":"$offerId","slotId":"x","specialty":"Cardiologia","professional":"Dra. Ana","startsAt":"2026-11-20T12:00:00+00:00","endsAt":"2026-11-20T12:30:00+00:00","expiresAt":"2026-11-16T12:10:00.123+00:00","status":"pending"}]}]}""")
        val dependent = api().dependentsOpenOffers("t").single()
        assertEquals("Maria Souza", dependent.dependentName); assertEquals(dependentId, dependent.dependentId)
        assertEquals("pending", dependent.offers.single().status); assertNotNull(dependent.offers.single().expiresAtMillis)
    }

    @Test fun emailSuggestionParsesNullAndObjectAndTheCodeGoesInTheBody() = runBlocking {
        route("/api/patient/email-suggestion", 200, """{"suggestion":null}""")
        assertNull(api().emailSuggestion("t"))
        route("/api/patient/email-suggestion", 200,
            """{"suggestion":{"id":"$offerId","maskedEmail":"n***@example.test","unitId":"$unitId","unitName":"UBS Centro","createdAt":"2026-11-16T10:00:00+00:00"}}""")
        val suggestion = requireNotNull(api().emailSuggestion("t"))
        assertEquals("n***@example.test", suggestion.maskedEmail); assertEquals("UBS Centro", suggestion.unitName)
        route("/api/patient/email-suggestion/send-code", 204, "")
        api().sendEmailSuggestionCode("t"); assertEquals("POST", lastMethod); assertEquals("", lastBody)
        route("/api/patient/email-suggestion/confirm", 200, """{"codeValid":false}""")
        assertFalse(api().confirmEmailSuggestion("t", "123456"))
        assertEquals("""{"code":"123456"}""", lastBody)
        route("/api/patient/email-suggestion/dismiss", 204, "")
        api().dismissEmailSuggestion("t"); assertEquals("/api/patient/email-suggestion/dismiss", lastUri)
    }

    @Test fun emailRefusalsMapToSpecificMessages() = runBlocking {
        route("/api/patient/email-suggestion/confirm", 409, """{"code":"conflict","title":"SEGREDO"}""")
        try { api().confirmEmailSuggestion("t", "123456"); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals(failureMessage(409, "conflict", ApiContext.EMAIL_CHANGE), failure.message); assertFalse(failure.message.orEmpty().contains("SEGREDO")) }
        route("/api/patient/email-suggestion/send-code", 503, """{"code":"service-unavailable"}""")
        try { api().sendEmailSuggestionCode("t"); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals(503, failure.status); assertTrue(failure.message.orEmpty().contains("e-mails")) }
    }
}
