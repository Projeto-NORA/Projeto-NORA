package com.example.app_nora

import android.content.Context
import android.content.ContextWrapper
import android.content.SharedPreferences
import androidx.test.platform.app.InstrumentationRegistry
import com.example.app_nora.api.*
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.io.IOException

class PatientControllerTest {
    private fun isolatedContext(): Context = object : ContextWrapper(
        InstrumentationRegistry.getInstrumentation().targetContext
    ) {
        override fun getSharedPreferences(name: String, mode: Int): SharedPreferences =
            super.getSharedPreferences("${name}_checks", mode)
    }

    private class PresenceApi : PatientApi {
        private val user = PatientUser("presence-patient", "Paciente de presença")
        val published = mutableListOf<Boolean>()
        var presenceFailure: IOException? = null
        var reloadStarted: CompletableDeferred<Unit>? = null
        var releaseReload: CompletableDeferred<Unit>? = null
        override suspend fun login(identifier: String, password: String) = PatientSession("presence-token", "2026-10-11T23:00:00Z", user)
        override suspend fun overview(token: String): PatientOverview {
            reloadStarted?.complete(Unit)
            releaseReload?.await()
            return PatientOverview(user, emptyList(), emptyList())
        }
        override suspend fun units(token: String) = listOf(
            PatientHealthUnit("unit-id", "UBS Teste", "Centro", "Rua de teste", -23.5, -46.6),
            PatientHealthUnit("other-id", "UBS Outra", "Norte", "Rua dois", -20.0, -40.0))
        override suspend fun appointments(token: String, unitId: String) = listOf(PatientAppointment(unitId, "Clínica", "Profissional", "2026-11-10T12:00:00Z", "scheduled"))
        override suspend fun respond(token: String, id: String, accept: Boolean) = Unit
        override suspend fun logout(token: String) = Unit
        override suspend fun presence(token: String, online: Boolean) {
            presenceFailure?.let { throw it }
            published += online
        }
    }

    @Test fun unitsAreLoadedForSessionAndClearedOnLogout() = runBlocking {
        val controller = PatientController(isolatedContext()) { PresenceApi() }
        controller.changeAddress("https://nora.example.test")
        controller.login("patient@example.test", "test-only-input")
        controller.loadUnits()
        assertEquals("UBS Teste", controller.state.units?.first()?.name)
        assertEquals("other-id", controller.selectNearestUnit(-20.01, -40.01)?.id)
        assertEquals("other-id", controller.state.selectedUnitId)
        controller.selectUnit("unit-id")
        assertEquals("unit-id", controller.state.selectedUnitId)
        controller.loadAppointments()
        assertEquals("unit-id", controller.state.appointments?.single()?.id)
        controller.selectUnit("other-id")
        assertNull(controller.state.appointments)
        controller.selectUnit("unit-id")
        controller.selectUnit("unknown")
        assertEquals("unit-id", controller.state.selectedUnitId)
        controller.logout()
        assertNull(controller.state.units)
        assertNull(controller.state.selectedUnitId)
        assertNull(controller.state.appointments)
    }

    @Test fun registrationUsesSelectedUnitsAndReturnsPartialResult() = runBlocking {
        val user = PatientUser("patient", "Paciente")
        var requestedUnits = emptyList<String>()
        var sentOption = ""
        val api = object : PatientApi {
            override suspend fun login(identifier: String, password: String) = PatientSession("token", "2099-01-01T00:00:00Z", user)
            override suspend fun overview(token: String) = PatientOverview(user, emptyList(), emptyList())
            override suspend fun startRegistration(name: String, cpf: String, password: String, unitIds: List<String>): RegistrationStart {
                requestedUnits = unitIds
                return RegistrationStart("challenge", listOf(RegistrationEmailOption("option", "p***@example.test")))
            }
            override suspend fun sendRegistrationCode(challengeId: String, emailOptionId: String) { sentOption = emailOptionId }
            override suspend fun confirmRegistration(challengeId: String, code: String) = RegistrationResult(code == "123456", listOf("unit-1"), listOf("unit-2"))
            override suspend fun respond(token: String, id: String, accept: Boolean) = Unit
            override suspend fun logout(token: String) = Unit
            override suspend fun presence(token: String, online: Boolean) = Unit
        }
        val controller = PatientController(isolatedContext()) { api }
        controller.changeAddress("https://nora.example.test")
        val started = controller.startRegistration("Paciente", "52998224725", "synthetic-password", listOf("unit-1", "unit-2"))
        controller.sendRegistrationCode(requireNotNull(started.challengeId), started.emails.single().id)
        val result = controller.confirmRegistration("challenge", "123456")
        assertEquals(listOf("unit-1", "unit-2"), requestedUnits)
        assertEquals("option", sentOption)
        assertEquals(listOf("unit-1"), result.linkedUnits)
        assertEquals(listOf("unit-2"), result.unmatchedUnits)
        assertNull(controller.state.session)
    }

    @Test fun obsoleteLifecycleCannotClearNewPresenceAndLogoutStopsPublishing() = runBlocking {
        val api = PresenceApi()
        val controller = PatientController(isolatedContext()) { api }
        controller.changeAddress("https://nora.example.test")
        controller.login("patient@example.test", "test-only-input")
        val session = requireNotNull(controller.state.session)
        val first = controller.beginPresence()
        controller.publishPresence(session, first, true)
        val replacement = controller.beginPresence()
        controller.publishPresence(session, replacement, true)
        controller.publishPresence(session, first, false)
        controller.endPresence(session, controller.state.address, first)
        assertEquals(listOf(true, true), api.published)
        controller.publishPresence(session, replacement, false)
        assertEquals(listOf(true, true, false), api.published)
        controller.logout()
        controller.publishPresence(session, replacement, true)
        assertEquals(listOf(true, true, false), api.published)
        assertNull(controller.state.session)
    }

    @Test fun missedHeartbeatRetriesButUnauthorizedClearsIdentityAndPendingReload() = runBlocking {
        val api = PresenceApi()
        val controller = PatientController(isolatedContext()) { api }
        controller.changeAddress("https://nora.example.test")
        controller.login("patient@example.test", "test-only-input")
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        api.presenceFailure = IOException("Disposable offline")
        controller.publishPresence(session, generation, true)
        assertNotNull(controller.state.session)
        api.presenceFailure = null
        controller.publishPresence(session, generation, true)
        assertEquals(listOf(true), api.published)
        api.reloadStarted = CompletableDeferred()
        api.releaseReload = CompletableDeferred()
        val reload = launch { controller.refresh() }
        requireNotNull(api.reloadStarted).await()
        api.presenceFailure = ApiFailure(401, "Sessão terminou")
        controller.publishPresence(session, generation, true)
        requireNotNull(api.releaseReload).complete(Unit)
        reload.join()
        assertNull(controller.state.session)
        assertNull(controller.state.overview)
        assertEquals("Sessão terminou", controller.state.message)
        assertFalse(controller.state.busy)
    }

    @Test fun closingTaskClearsLocalAccountAndStopsOldHeartbeat() = runBlocking {
        val api = PresenceApi()
        val controller = PatientController(isolatedContext()) { api }
        controller.changeAddress("https://nora.example.test")
        controller.login("patient@example.test", "test-only-input")
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        controller.publishPresence(session, generation, true)
        controller.closeLocalSession()
        controller.publishPresence(session, generation, true)
        assertEquals(listOf(true), api.published)
        controller.endPresence(session, controller.state.address, generation)
        assertEquals(listOf(true, false), api.published)
        assertNull(controller.state.session)
        assertNull(controller.state.overview)
    }

    @Test fun logoutClearsPatientDataWhenServerIsUnreachable() = runBlocking {
        val user = PatientUser("test-user", "Paciente de teste")
        val api = object : PatientApi {
            override suspend fun login(identifier: String, password: String) =
                PatientSession("test-token", "2026-10-07T20:00:00Z", user)
            override suspend fun overview(token: String) = PatientOverview(user, emptyList(), emptyList())
            override suspend fun respond(token: String, id: String, accept: Boolean) = Unit
            override suspend fun logout(token: String) { throw IOException("Test offline") }
            override suspend fun presence(token: String, online: Boolean) = Unit
        }
        val controller = PatientController(isolatedContext()) { api }
        controller.changeAddress("https://nora.example.test")
        controller.login("patient@example.test", "test-only-input")
        assertNotNull(controller.state.session)
        controller.refresh()
        assertNotNull(controller.state.overview)
        controller.logout()
        assertNull(controller.state.session)
        assertNull(controller.state.overview)
        assertFalse(controller.state.busy)
        assertTrue(controller.state.message.orEmpty().contains("saída no serviço"))
    }

    @Test fun acceptedOfferRemainsConfirmedWhenReloadFails() = runBlocking {
        confirmedDecisionSurvivesReloadFailure(true, "accepted")
    }

    @Test fun declinedOfferRemainsConfirmedWhenReloadFails() = runBlocking {
        confirmedDecisionSurvivesReloadFailure(false, "declined")
    }

    private suspend fun confirmedDecisionSurvivesReloadFailure(accept: Boolean, expectedStatus: String) {
        val user = PatientUser("test-user", "Paciente de teste")
        val offer = PatientOffer("00000000-0000-0000-0000-000000000001", "Oferta de teste",
            "2026-10-08T20:10:00Z", "pending")
        var confirmed = false
        var responses = 0
        val api = object : PatientApi {
            override suspend fun login(identifier: String, password: String) =
                PatientSession("test-token", "2026-10-08T21:00:00Z", user)
            override suspend fun overview(token: String): PatientOverview {
                if (confirmed) throw IOException("Reload unavailable after confirmed decision")
                return PatientOverview(user, emptyList(), listOf(offer))
            }
            override suspend fun respond(token: String, id: String, accept: Boolean) {
                assertEquals(offer.id, id)
                responses++
                confirmed = true
            }
            override suspend fun logout(token: String) = Unit
            override suspend fun presence(token: String, online: Boolean) = Unit
        }
        val controller = PatientController(isolatedContext()) { api }
        controller.changeAddress("https://nora.example.test")
        controller.login("patient@example.test", "test-only-input")
        assertNull(controller.state.message)
        controller.refresh()
        controller.respond(offer.id, accept)
        assertEquals(expectedStatus, requireNotNull(controller.state.overview).offers.single().status)
        assertEquals("test-token", requireNotNull(controller.state.session).token)
        assertEquals(1, responses)
        assertFalse(controller.state.busy)
        assertTrue(controller.state.message.orEmpty().contains("confirmada"))
        assertTrue(controller.state.message.orEmpty().contains("Atualizar"))
        assertFalse(controller.state.message.orEmpty().contains("tente novamente"))
    }

    @Test fun registrationUsesRealSeparateUnitDatabases() = runBlocking {
        val address = InstrumentationRegistry.getArguments().getString("noraApiUrl").orEmpty()
        assumeTrue("Pass noraApiUrl for the isolated registration fixture", address.isNotBlank())
        val api = NoraApi(address)
        val units = api.registrationUnits().filter { it.name.startsWith("UBS Cadastro E2E ") }.sortedBy { it.name }
        assertEquals(3, units.size)
        val started = api.startRegistration("Paciente Sintético", "52998224725", "Disposable-patient-registration!29", units.map { it.id })
        val challenge = requireNotNull(started.challengeId)
        assertEquals(2, started.emails.size)
        val option = started.emails.single { it.maskedEmail.startsWith("f***") }
        api.sendRegistrationCode(challenge, option.id)
        val code = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            val connection = java.net.URL("$address/__checks/registration-code").openConnection().apply { connectTimeout = 10000; readTimeout = 10000 }
            connection.getInputStream().bufferedReader().use { org.json.JSONObject(it.readText()).getString("code") }
        }
        val result = api.confirmRegistration(challenge, code)
        assertTrue(result.codeValid)
        assertEquals(units.take(2).map { it.id }.toSet(), result.linkedUnits.toSet())
        assertEquals(listOf(units[2].id), result.unmatchedUnits)
        val session = api.login("52998224725", "Disposable-patient-registration!29")
        assertEquals(units.take(2).map { it.id }.toSet(), api.units(session.token).map { it.id }.toSet())
        val link = api.linkUnits(session.token, listOf(units[0].id, units[2].id))
        assertEquals(listOf(units[0].id), link.linkedUnits)
        assertEquals(listOf(units[2].id), link.unmatchedUnits)
        val appointments = api.appointments(session.token, units[0].id)
        val clinic = appointments.single { it.specialty == "Clínica de teste" }
        assertTrue(api.appointments(session.token, units[1].id).isEmpty())
        try { api.appointments(session.token, units[2].id); fail("UBS sem vínculo não deve retornar consultas") }
        catch (failure: ApiFailure) { assertEquals(403, failure.status) }
        api.cancelAppointment(session.token, units[0].id, clinic.id)
        assertEquals("cancelled", api.appointments(session.token, units[0].id).single { it.id == clinic.id }.status)
        val available = api.unitOffers(session.token, units[0].id).single { it.status == "pending" }
        assertEquals("Cardiologia", available.specialty)
        assertEquals("offered", api.unitQueue(session.token, units[0].id).single { it.specialty == "Cardiologia" }.status)
        api.respondUnitOffer(session.token, units[0].id, available.id, true)
        assertEquals("accepted", api.unitOffers(session.token, units[0].id).single { it.id == available.id }.status)
        assertEquals("scheduled", api.appointments(session.token, units[0].id).single { it.specialty == "Cardiologia" && it.status == "scheduled" }.status)
        api.logout(session.token)
    }

    @Test fun patientUsesRealDisposablePostgresApi() = runBlocking {
        val address = InstrumentationRegistry.getArguments().getString("noraApiUrl").orEmpty()
        assumeTrue("Pass noraApiUrl for the isolated integration fixture", address.isNotBlank())
        val controller = PatientController(isolatedContext())
        controller.changeAddress(address)
        controller.login("patient1@example.test", "Disposable-test-Account!29")
        assertNull(controller.state.message)
        controller.refresh()
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        controller.publishPresence(session, generation, true)
        assertNotNull(controller.state.session)
        controller.publishPresence(session, generation, false)
        val offer = requireNotNull(controller.state.overview).offers.single { it.status == "pending" }
        controller.respond(offer.id, true)
        assertNull(controller.state.message)
        assertEquals("accepted", requireNotNull(controller.state.overview).offers.single { it.id == offer.id }.status)
        controller.refresh()
        assertNull(controller.state.message)
        controller.logout()
        assertNull(controller.state.session)
        assertNull(controller.state.overview)
        assertNull(controller.state.message)
        try {
            NoraApi(address).overview(session.token)
            fail("Revoked session must not retain access")
        } catch (failure: ApiFailure) { assertEquals(401, failure.status) }
        controller.login("123.456.781-43", "Disposable-test-Account!29")
        assertNull(controller.state.message)
        assertEquals(session.user.id, requireNotNull(controller.state.session).user.id)
        controller.logout()
        assertNull(controller.state.session)
        assertNull(controller.state.message)
    }
}
