package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import org.json.JSONException
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone

/** Controller rules that need no Android: unit selection, background polling and operation rules. */
class PatientControllerTest {
    private class MemoryStore(var address: String = "") : AddressStore {
        override fun load() = address
        override fun save(address: String) { this.address = address }
    }

    private fun iso(offsetSeconds: Long): String =
        SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.ROOT).apply { timeZone = TimeZone.getTimeZone("UTC") }
            .format(System.currentTimeMillis() + offsetSeconds * 1000)

    private val unitA = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", -23.5, -46.6)
    private val unitB = PatientHealthUnit("unit-b", "UBS B", "Norte", "Rua B", -20.0, -40.0)
    private fun pending(id: String, expiresIn: Long = 600) =
        UnitOffer(id, "Cardiologia", "Profissional", iso(7200), iso(9000), iso(expiresIn), "pending")

    private class FakeApi(val user: PatientUser = PatientUser("patient-id", "Paciente de teste")) : PatientApi {
        var units = emptyList<PatientHealthUnit>()
        val offers = mutableMapOf<String, List<UnitOffer>>()
        var appointments = listOf<PatientAppointment>()
        var offersCalls = 0
        var appointmentCalls = 0
        var gateNextOffersCall: CompletableDeferred<Unit>? = null
        var offersFailure: Throwable? = null
        var respondResult: OfferResponse = OfferResponse(true, "new-appointment")
        var respondFailure: Throwable? = null
        var cancelFailure: Throwable? = null
        val busyDuringReads = mutableListOf<Boolean>()
        var observeBusy: (() -> Boolean)? = null
        var resetFailure: Throwable? = null

        override suspend fun login(identifier: String, password: String) = PatientSession("token-1", "2099-01-01T00:00:00Z", user)
        override suspend fun units(token: String) = units
        override suspend fun unitQueue(token: String, unitId: String) = emptyList<UnitQueueEntry>()
        override suspend fun appointments(token: String, unitId: String): List<PatientAppointment> { appointmentCalls++; return appointments }
        override suspend fun cancelAppointment(token: String, unitId: String, id: String) { cancelFailure?.let { throw it } }
        override suspend fun unitOffers(token: String, unitId: String): List<UnitOffer> {
            offersCalls++
            observeBusy?.let { busyDuringReads += it() }
            // The server answers with what it knows now; a gated call delivers that (possibly stale) answer later.
            val snapshot = offers[unitId].orEmpty()
            val gate = gateNextOffersCall
            if (gate != null) { gateNextOffersCall = null; gate.await() }
            offersFailure?.let { throw it }
            return snapshot
        }
        override suspend fun respondUnitOffer(token: String, unitId: String, id: String, accept: Boolean): OfferResponse {
            respondFailure?.let { throw it }
            offers[unitId] = offers[unitId].orEmpty().map { if (it.id == id) it.copy(status = if (accept) "accepted" else "declined") else it }
            return respondResult.copy(accepted = accept && respondResult.accepted)
        }
        override suspend fun requestPasswordReset(cpf: String): String { resetFailure?.let { throw it }; return "challenge" }
        override suspend fun confirmPasswordReset(challengeId: String, code: String, newPassword: String): Boolean { resetFailure?.let { throw it }; return true }
        override suspend fun logout(token: String) = Unit
        override suspend fun presence(token: String, online: Boolean) = Unit
    }

    private fun controller(api: FakeApi): PatientController =
        PatientController(MemoryStore("https://nora.example.test")) { api }

    private suspend fun PatientController.signIn() = login("patient@example.test", "test-only-input")

    @Test fun singleLinkedUnitIsSelectedAndItsDataLoadedAfterLogin() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); offers[unitA.id] = listOf(pending("o1")); appointments = listOf(PatientAppointment("ap", "Clínica", "Prof", iso(3600), iso(5400), "scheduled")) }
        val controller = controller(api)
        controller.signIn()
        assertNotNull(controller.state.session)
        assertEquals(unitA.id, controller.state.selectedUnitId)
        assertEquals(listOf("o1"), controller.state.unitOffers?.map { it.id })
        assertEquals(emptyList<UnitQueueEntry>(), controller.state.unitQueue)
        assertEquals("ap", controller.state.appointments?.single()?.id)
        assertFalse(controller.state.busy); assertFalse(controller.state.loadingUnits)
    }

    @Test fun severalUnitsKeepTheChoiceExplicit() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB) }
        val controller = controller(api)
        controller.signIn()
        assertEquals(2, controller.state.units?.size)
        assertNull(controller.state.selectedUnitId)
        assertEquals(0, api.offersCalls)
        controller.selectUnit(unitB.id)
        assertEquals(unitB.id, controller.state.selectedUnitId)
        controller.selectUnit("unknown")
        assertEquals(unitB.id, controller.state.selectedUnitId)
    }

    @Test fun failedUnitLoadAfterLoginLeavesTheSessionAndRetriesInThePollCycle() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA) }
        val failing = object : PatientApi by api { override suspend fun units(token: String): List<PatientHealthUnit> = throw IOException("offline") }
        var useFailing = true
        val controller = PatientController(MemoryStore("https://nora.example.test")) { if (useFailing) failing else api }
        controller.signIn()
        assertNotNull(controller.state.session)
        assertNull(controller.state.units)
        assertNull(controller.state.message)
        useFailing = false
        controller.pollOffers(requireNotNull(controller.state.session), controller.beginPresence())
        assertEquals(unitA.id, controller.state.selectedUnitId)
    }

    @Test fun pollingReadsEveryUnitWithoutTheGlobalBusyFlag() = runBlocking {
        lateinit var controller: PatientController
        val api = FakeApi().apply {
            units = listOf(unitA, unitB)
            offers[unitA.id] = listOf(pending("a1")); offers[unitB.id] = listOf(pending("b1"), pending("b2"))
            observeBusy = { controller.state.busy }
        }
        controller = controller(api)
        controller.signIn()
        val generation = controller.beginPresence()
        controller.pollOffers(requireNotNull(controller.state.session), generation)
        assertEquals(listOf("a1"), controller.state.unitData[unitA.id]?.offers?.map { it.id })
        assertEquals(listOf("b1", "b2"), controller.state.unitData[unitB.id]?.offers?.map { it.id })
        assertTrue(api.busyDuringReads.isNotEmpty())
        assertTrue("Polling must not raise busy", api.busyDuringReads.none { it })
        assertFalse(controller.state.busy)
        assertNull(controller.state.message)
    }

    @Test fun pollingFailuresAreSilentAndRetriedInTheNextCycle() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB); offers[unitA.id] = listOf(pending("a1")) }
        val controller = controller(api)
        controller.signIn()
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        api.offersFailure = IOException("offline")
        controller.pollOffers(session, generation)
        assertNull(controller.state.message)
        assertNotNull(controller.state.session)
        api.offersFailure = JSONException("bad body")
        controller.pollOffers(session, generation)
        assertNull(controller.state.message)
        api.offersFailure = ApiFailure(403, "x", "forbidden")
        controller.pollOffers(session, generation)
        assertNull(controller.state.message)
        assertNotNull(controller.state.session)
        api.offersFailure = null
        controller.pollOffers(session, generation)
        assertEquals(listOf("a1"), controller.state.unitData[unitA.id]?.offers?.map { it.id })
    }

    @Test fun unauthorizedWhilePollingEndsTheSession() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB) }
        val controller = controller(api)
        controller.signIn()
        val session = requireNotNull(controller.state.session)
        api.offersFailure = ApiFailure(401, "Sua sessão terminou. Entre novamente.", "unauthorized")
        controller.pollOffers(session, controller.beginPresence())
        assertNull(controller.state.session)
        assertNull(controller.state.units)
        assertTrue(controller.state.unitData.isEmpty())
        assertEquals("Sua sessão terminou. Entre novamente.", controller.state.message)
    }

    @Test fun lateAnswerAfterLogoutIsDiscarded() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB); offers[unitA.id] = listOf(pending("a1")) }
        val controller = controller(api)
        controller.signIn()
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        val gate = CompletableDeferred<Unit>()
        api.gateNextOffersCall = gate
        val poll = launch { controller.pollOffers(session, generation) }
        yield()
        controller.logout()
        assertNull(controller.state.session)
        gate.complete(Unit)
        poll.join()
        assertNull(controller.state.session)
        assertTrue(controller.state.unitData.isEmpty())
        assertNull(controller.state.units)
    }

    @Test fun lateAnswerAfterANewGenerationIsDiscarded() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); offers[unitA.id] = listOf(pending("a1")) }
        val controller = controller(api)
        controller.signIn()
        val session = requireNotNull(controller.state.session)
        val gate = CompletableDeferred<Unit>()
        api.gateNextOffersCall = gate
        val poll = launch { controller.pollOffers(session, controller.beginPresence()) }
        yield()
        controller.closeLocalSession()
        gate.complete(Unit)
        poll.join()
        assertTrue(controller.state.unitData.isEmpty())
    }

    @Test fun userActionIsNotDroppedAndNotOverwrittenByAPollInFlight() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); offers[unitA.id] = listOf(pending("o1")) }
        val controller = controller(api)
        controller.signIn()
        val session = requireNotNull(controller.state.session)
        val gate = CompletableDeferred<Unit>()
        api.gateNextOffersCall = gate
        // The poll asked the server while the offer was still pending and is now waiting for the answer.
        val poll = launch { controller.pollOffers(session, controller.beginPresence()) }
        yield()
        assertFalse("A background poll must not block the user", controller.state.busy)
        controller.respondUnitOffer("o1", true)
        assertEquals("Oferta aceita. Sua consulta foi agendada.", controller.state.message)
        assertEquals("accepted", controller.state.unitOffers?.single()?.status)
        gate.complete(Unit)
        poll.join()
        // The stale "pending" answer that left before the user's action must not undo it.
        assertEquals("accepted", controller.state.unitOffers?.single()?.status)
    }

    @Test fun respondReloadsOffersAndAppointmentsOnlyWhenAccepted() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); offers[unitA.id] = listOf(pending("o1"), pending("o2")) }
        val controller = controller(api)
        controller.signIn()
        val offerCallsBefore = api.offersCalls; val appointmentCallsBefore = api.appointmentCalls
        controller.respondUnitOffer("o1", true)
        assertEquals(offerCallsBefore + 1, api.offersCalls)
        assertEquals(appointmentCallsBefore + 1, api.appointmentCalls)
        controller.respondUnitOffer("o2", false)
        assertEquals("Oferta recusada. Você continua na fila, no mesmo lugar.", controller.state.message)
        assertEquals(offerCallsBefore + 2, api.offersCalls)
        assertEquals("No appointment reload after a decline", appointmentCallsBefore + 1, api.appointmentCalls)
        assertEquals(listOf("accepted", "declined"), controller.state.unitOffers?.map { it.status })
    }

    @Test fun acceptWithoutServerConfirmationDoesNotClaimAScheduledAppointment() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); offers[unitA.id] = listOf(pending("o1")); respondResult = OfferResponse(false, null) }
        val controller = controller(api)
        controller.signIn()
        controller.respondUnitOffer("o1", true)
        assertFalse(controller.state.message.orEmpty().contains("agendada"))
    }

    @Test fun conflictOnRespondShowsTheUnavailableMessageAndReloadsOffers() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); offers[unitA.id] = listOf(pending("o1")) }
        val controller = controller(api)
        controller.signIn()
        // Meanwhile the server expired the offer.
        api.offers[unitA.id] = listOf(pending("o1").copy(status = "expired"))
        api.respondFailure = ApiFailure(409, "texto qualquer do servidor", "conflict")
        val callsBefore = api.offersCalls
        controller.respondUnitOffer("o1", true)
        assertEquals("Esta oportunidade não está mais disponível.", controller.state.message)
        assertEquals(callsBefore + 1, api.offersCalls)
        assertEquals("expired", controller.state.unitOffers?.single()?.status)
        assertFalse(controller.state.busy)
        assertNotNull(controller.state.session)
    }

    @Test fun forbiddenInsideAUnitExplainsTheLinkProblem() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB) }
        val controller = controller(api)
        controller.signIn()
        controller.selectUnit(unitB.id)
        api.offersFailure = ApiFailure(403, "x", "forbidden")
        controller.refreshUnit()
        assertEquals(UNIT_FORBIDDEN_MESSAGE, controller.state.message)
        assertNotNull(controller.state.session)
    }

    @Test fun cancelConflictReloadsAppointmentsAndExplains() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); appointments = listOf(PatientAppointment("ap", "Clínica", "Prof", iso(3600), iso(5400), "scheduled")) }
        val controller = controller(api)
        controller.signIn()
        api.appointments = listOf(PatientAppointment("ap", "Clínica", "Prof", iso(3600), iso(5400), "completed"))
        api.cancelFailure = ApiFailure(409, "x", "conflict")
        controller.cancelAppointment("ap")
        assertTrue(controller.state.message.orEmpty().startsWith("Esta consulta já foi alterada"))
        assertEquals("completed", controller.state.appointments?.single()?.status)
    }

    @Test fun unauthorizedOnUserActionClearsEverythingAndKeepsTheMessage() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); offers[unitA.id] = listOf(pending("o1")) }
        val controller = controller(api)
        controller.signIn()
        api.offersFailure = ApiFailure(401, "x", "unauthorized")
        controller.refreshUnit()
        assertNull(controller.state.session)
        assertTrue(controller.state.unitData.isEmpty())
        assertEquals("Sua sessão terminou. Entre novamente.", controller.state.message)
        assertFalse(controller.state.busy)
    }

    @Test fun unexpectedResponsesInRecoveryBecomeAnErrorInsteadOfACrash() = runBlocking {
        val api = FakeApi().apply { resetFailure = JSONException("missing challengeId") }
        val controller = controller(api)
        for (call in listOf<suspend () -> Unit>({ controller.requestPasswordReset("52998224725") }, { controller.confirmPasswordReset("c", "123456", "new-password-1") })) {
            try { call(); fail("expected ApiFailure") }
            catch (failure: ApiFailure) { assertEquals(INCOMPATIBLE_RESPONSE_MESSAGE, failure.message) }
        }
    }

    @Test fun savedStateKeepsOnlyTheServiceAddress() = runBlocking {
        val store = MemoryStore("https://old.example.test")
        val controller = PatientController(store) { FakeApi().apply { units = listOf(unitA) } }
        assertEquals("https://old.example.test", controller.state.address)
        controller.changeAddress("https://nora.example.test")
        controller.signIn()
        assertEquals("https://nora.example.test", store.address)
    }
}
