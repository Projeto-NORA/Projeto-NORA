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
        override suspend fun respond(token: String, id: String, accept: Boolean) = Unit
        override suspend fun logout(token: String) = Unit
        override suspend fun presence(token: String, online: Boolean) {
            presenceFailure?.let { throw it }
            published += online
        }
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
        controller.respond(offer.id, accept)
        assertEquals(expectedStatus, requireNotNull(controller.state.overview).offers.single().status)
        assertEquals("test-token", requireNotNull(controller.state.session).token)
        assertEquals(1, responses)
        assertFalse(controller.state.busy)
        assertTrue(controller.state.message.orEmpty().contains("confirmada"))
        assertTrue(controller.state.message.orEmpty().contains("Atualizar"))
        assertFalse(controller.state.message.orEmpty().contains("tente novamente"))
    }

    @Test fun patientUsesRealDisposablePostgresApi() = runBlocking {
        val address = InstrumentationRegistry.getArguments().getString("noraApiUrl").orEmpty()
        assumeTrue("Pass noraApiUrl for the isolated integration fixture", address.isNotBlank())
        val controller = PatientController(isolatedContext())
        controller.changeAddress(address)
        controller.login("patient1@example.test", "Disposable-test-Account!29")
        assertNull(controller.state.message)
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
