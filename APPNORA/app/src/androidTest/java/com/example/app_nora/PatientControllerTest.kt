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
        override suspend fun units(token: String) = listOf(
            PatientHealthUnit("unit-id", "UBS Teste", "Centro", "Rua de teste", -23.5, -46.6),
            PatientHealthUnit("other-id", "UBS Outra", "Norte", "Rua dois", -20.0, -40.0))
        override suspend fun appointments(token: String, unitId: String) = listOf(PatientAppointment(unitId, "Clínica", "Profissional", "2026-11-10T12:00:00Z", "2026-11-10T12:30:00Z", "scheduled"))
        override suspend fun unitOffers(token: String, unitId: String): List<UnitOffer> {
            reloadStarted?.complete(Unit)
            releaseReload?.await()
            return emptyList()
        }
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
        assertNull("Duas UBS vinculadas: a escolha é explícita", controller.state.selectedUnitId)
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
            override suspend fun startRegistration(name: String, cpf: String, password: String, unitIds: List<String>): RegistrationStart {
                requestedUnits = unitIds
                return RegistrationStart("challenge", listOf(RegistrationEmailOption("option", "p***@example.test")))
            }
            override suspend fun sendRegistrationCode(challengeId: String, emailOptionId: String) { sentOption = emailOptionId }
            override suspend fun confirmRegistration(challengeId: String, code: String) = RegistrationResult(code == "123456", listOf("unit-1"), listOf("unit-2"))
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
        controller.loadUnits()
        controller.selectUnit("unit-id")
        api.reloadStarted = CompletableDeferred()
        api.releaseReload = CompletableDeferred()
        val reload = launch { controller.refreshUnit() }
        requireNotNull(api.reloadStarted).await()
        api.presenceFailure = ApiFailure(401, "Sessão terminou")
        controller.publishPresence(session, generation, true)
        requireNotNull(api.releaseReload).complete(Unit)
        reload.join()
        assertNull(controller.state.session)
        assertTrue(controller.state.unitData.isEmpty())
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
        assertNull(controller.state.units)
    }

    @Test fun logoutClearsPatientDataWhenServerIsUnreachable() = runBlocking {
        val user = PatientUser("test-user", "Paciente de teste")
        val api = object : PatientApi {
            override suspend fun login(identifier: String, password: String) =
                PatientSession("test-token", "2026-10-07T20:00:00Z", user)
            override suspend fun units(token: String) = listOf(PatientHealthUnit("unit-id", "UBS Teste", "Centro", "Rua", -23.5, -46.6))
            override suspend fun logout(token: String) { throw IOException("Test offline") }
            override suspend fun presence(token: String, online: Boolean) = Unit
        }
        val controller = PatientController(isolatedContext()) { api }
        controller.changeAddress("https://nora.example.test")
        controller.login("patient@example.test", "test-only-input")
        assertNotNull(controller.state.session)
        assertEquals("unit-id", controller.state.selectedUnitId)
        controller.logout()
        assertNull(controller.state.session)
        assertNull(controller.state.units)
        assertFalse(controller.state.busy)
        assertTrue(controller.state.message.orEmpty().contains("saída no serviço"))
    }

    /**
     * Fixture externa: API + bancos PostgreSQL descartáveis iniciados com NORA_TEST_REGISTRATION=1
     * (três "UBS Cadastro E2E", código do cadastro em /__checks/registration-code). Passe `noraApiUrl`.
     * Todo o fluxo do paciente usa somente rotas por UBS, pelo controlador real.
     */
    @Test fun patientUsesRealDisposablePostgresApi() = runBlocking {
        val address = InstrumentationRegistry.getArguments().getString("noraApiUrl").orEmpty()
        assumeTrue("Pass noraApiUrl for the isolated integration fixture", address.isNotBlank())
        // O teste fala com o controlador sem a tela, então faz o papel do paciente que concede a rede local: no Android 17 (API 37)
        // o 10.0.2.2 do emulador é rede local e, sem ACCESS_LOCAL_NETWORK, a conexão expira (ver ApiAddress.usesLocalNetwork).
        if (android.os.Build.VERSION.SDK_INT >= 37 && ApiAddress.usesLocalNetwork(address)) {
            val instrumentation = InstrumentationRegistry.getInstrumentation()
            // Lê a saída até o fim para só seguir depois que o `pm grant` terminou.
            android.os.ParcelFileDescriptor.AutoCloseInputStream(instrumentation.uiAutomation.executeShellCommand(
                "pm grant ${instrumentation.targetContext.packageName} android.permission.ACCESS_LOCAL_NETWORK")).use { it.readBytes() }
        }
        val password = "Disposable-patient-registration!29"
        val controller = PatientController(isolatedContext())
        controller.changeAddress(address)
        val units = controller.registrationUnits().filter { it.name.startsWith("UBS Cadastro E2E ") }.sortedBy { it.name }
        assertEquals(3, units.size)
        val started = controller.startRegistration("Paciente Sintético", "52998224725", password, units.map { it.id })
        val challenge = requireNotNull(started.challengeId)
        assertEquals(2, started.emails.size)
        controller.sendRegistrationCode(challenge, started.emails.single { it.maskedEmail.startsWith("f***") }.id)
        val code = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
            val connection = java.net.URL("$address/__checks/registration-code").openConnection().apply { connectTimeout = 10000; readTimeout = 10000 }
            connection.getInputStream().bufferedReader().use { org.json.JSONObject(it.readText()).getString("code") }
        }
        val result = controller.confirmRegistration(challenge, code)
        assertTrue(result.codeValid)
        assertEquals(units.take(2).map { it.id }.toSet(), result.linkedUnits.toSet())
        assertEquals(listOf(units[2].id), result.unmatchedUnits)

        controller.login("52998224725", password)
        assertNull(controller.state.message)
        val session = requireNotNull(controller.state.session)
        assertEquals(units.take(2).map { it.id }.toSet(), controller.state.units.orEmpty().map { it.id }.toSet())
        assertNull("Duas UBS vinculadas: a escolha é explícita", controller.state.selectedUnitId)
        val link = controller.linkUnits(listOf(units[0].id, units[2].id))
        assertEquals(listOf(units[0].id), link.linkedUnits)
        assertEquals(listOf(units[2].id), link.unmatchedUnits)

        controller.selectUnit(units[0].id)
        controller.loadAppointments()
        val clinic = requireNotNull(controller.state.appointments).single { it.specialty == "Clínica de teste" }
        assertTrue(clinic.endsAtMillisOrNull() != null)
        try { NoraApi(address).appointments(session.token, units[2].id); fail("UBS sem vínculo não deve retornar consultas") }
        catch (failure: ApiFailure) { assertEquals(403, failure.status); assertEquals(UNIT_FORBIDDEN_MESSAGE, failure.message) }
        controller.cancelAppointment(clinic.id)
        assertEquals("Consulta cancelada.", controller.state.message)
        controller.loadAppointments()
        assertEquals("cancelled", requireNotNull(controller.state.appointments).single { it.id == clinic.id }.status)

        // O ciclo de consulta automática lê as ofertas de todas as UBS vinculadas sem ação do usuário.
        val generation = controller.beginPresence()
        controller.publishPresence(session, generation, true)
        // A API só cria a oferta no seu ciclo de 15 s (UnitLightningWorker), e só depois que a conta existe e está vinculada à UBS;
        // o app a encontra no próprio ciclo automático. O teste repete esse ciclo por até 45 s, como o laço de 30 s do app faria.
        val offerDeadline = System.currentTimeMillis() + 45_000
        var found: UnitOffer? = null
        while (found == null) {
            controller.pollOffers(session, generation)
            assertFalse(controller.state.busy)
            found = controller.state.unitData[units[0].id]?.offers?.singleOrNull { it.status == "pending" }
            if (found == null) {
                assertTrue("Nenhuma oferta pendente na UBS 1 em 45 s: ${controller.state.unitData.mapValues { it.value.offers }}",
                    System.currentTimeMillis() < offerDeadline)
                kotlinx.coroutines.delay(1_000)
            }
        }
        val available = requireNotNull(found)
        assertEquals("Cardiologia", available.specialty)
        assertTrue(openOffersAcrossUnits(controller.state.units, controller.state.unitData, System.currentTimeMillis()).any { it.offer.id == available.id })
        controller.publishPresence(session, generation, false)

        controller.refreshUnit()
        controller.respondUnitOffer(available.id, true)
        assertEquals("Oferta aceita. Sua consulta foi agendada.", controller.state.message)
        assertEquals("accepted", requireNotNull(controller.state.unitOffers).single { it.id == available.id }.status)
        assertTrue(requireNotNull(controller.state.appointments).any { it.specialty == "Cardiologia" && it.status == "scheduled" })
        controller.logout()
        assertNull(controller.state.session)
        assertNull(controller.state.units)
        try { NoraApi(address).units(session.token); fail("Revoked session must not retain access") }
        catch (failure: ApiFailure) { assertEquals(401, failure.status) }
    }

    private fun PatientAppointment.endsAtMillisOrNull(): Long? = parseInstantMillis(endsAt)
}
