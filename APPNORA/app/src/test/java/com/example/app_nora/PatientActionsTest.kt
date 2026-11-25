package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.io.IOException

/** Score por UBS, remover vínculo e trocar senha no controlador, com API falsa (sem rede nem Android). */
class PatientActionsTest {
    private class MemoryStore(var address: String = "") : AddressStore {
        override fun load() = address
        override fun save(address: String) { this.address = address }
    }

    private val now = System.currentTimeMillis()
    private fun iso(offsetSeconds: Long): String =
        java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", java.util.Locale.ROOT)
            .apply { timeZone = java.util.TimeZone.getTimeZone("UTC") }.format(now + offsetSeconds * 1000)

    private val unitA = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", -23.5, -46.6)
    private val unitB = PatientHealthUnit("unit-b", "UBS B", "Norte", "Rua B", -20.0, -40.0)

    private class FakeApi : PatientApi {
        var units = emptyList<PatientHealthUnit>()
        var unitsFailure: Throwable? = null
        val appointmentsByUnit = mutableMapOf<String, List<PatientAppointment>>()
        val offers = mutableMapOf<String, List<UnitOffer>>()
        val attendance = mutableMapOf<String, UnitAttendance>()
        val attendanceFailures = mutableMapOf<String, Throwable>()
        var attendanceCalls = 0
        var appointmentCalls = 0; var offersCalls = 0
        var unlinkFailure: Throwable? = null
        var unlinkResult = UnlinkResult(1, 2)
        val unlinked = mutableListOf<String>()
        var passwordFailure: Throwable? = null
        val passwordCalls = mutableListOf<Pair<String, String>>()
        var offersFailure: Throwable? = null

        override suspend fun login(identifier: String, password: String) =
            PatientSession("token-1", "2099-01-01T00:00:00Z", PatientUser("patient-id", "Paciente de teste"))
        override suspend fun units(token: String): List<PatientHealthUnit> { unitsFailure?.let { throw it }; return units }
        override suspend fun appointments(token: String, unitId: String): List<PatientAppointment> { appointmentCalls++; return appointmentsByUnit[unitId].orEmpty() }
        /** Prévia da remoção: o que o servidor diz que cancelará em cada UBS (todas as futuras, sem limite de data). */
        val previews = mutableMapOf<String, UnlinkImpact>()
        val previewed = mutableListOf<String>()
        var previewFailure: Throwable? = null
        override suspend fun unlinkPreview(token: String, unitId: String): UnlinkImpact {
            previewed += unitId; previewFailure?.let { throw it }; return previews[unitId] ?: UnlinkImpact(0, 0)
        }
        override suspend fun unitOffers(token: String, unitId: String): List<UnitOffer> { offersCalls++; offersFailure?.let { throw it }; return offers[unitId].orEmpty() }
        override suspend fun attendance(token: String, unitId: String): UnitAttendance {
            attendanceCalls++
            attendanceFailures[unitId]?.let { throw it }
            return attendance[unitId] ?: UnitAttendance(0, 0, 0, 0.0, null, 12)
        }
        override suspend fun unlinkUnit(token: String, unitId: String): UnlinkResult {
            unlinkFailure?.let { throw it }
            unlinked += unitId
            units = units.filter { it.id != unitId }
            return unlinkResult
        }
        override suspend fun changePassword(token: String, currentPassword: String, newPassword: String) {
            passwordFailure?.let { throw it }
            passwordCalls += currentPassword to newPassword
        }
        override suspend fun logout(token: String) = Unit
        override suspend fun presence(token: String, online: Boolean) = Unit
    }

    private val store = MemoryStore("https://nora.example.test")
    private fun controller(api: FakeApi) = PatientController(store) { api }
    private suspend fun PatientController.signIn() = login("patient@example.test", "test-only-input")
    private fun score(completed: Int, missed: Int, cancelled: Int, weight: Double, score: Double?) =
        UnitAttendance(completed, missed, cancelled, weight, score, 12)

    // ---- Score ----------------------------------------------------------------------------------------

    @Test fun attendanceIsReadOncePerUnitAndStoredPerUnitWithoutBusy() = runBlocking {
        val api = FakeApi().apply {
            units = listOf(unitA, unitB)
            attendance[unitA.id] = score(3, 1, 1, 0.5, 0.67); attendance[unitB.id] = score(1, 0, 0, 0.0, 1.0)
        }
        val controller = controller(api)
        controller.signIn()
        controller.loadAttendance()
        assertEquals(2, api.attendanceCalls)
        assertEquals(3, controller.state.unitData[unitA.id]?.attendance?.completed)
        assertEquals(727, summarizeAttendance(controller.state.units, controller.state.unitData).points)
        assertFalse(controller.state.busy); assertFalse(controller.state.attendanceLoading)
        assertNull(controller.state.message)
        controller.loadAttendance()
        assertEquals("Reabrir a aba lê de novo, uma requisição por UBS", 4, api.attendanceCalls)
    }

    @Test fun attendanceWithOneUnitDownStillShowsTheOtherAndFlagsTheFailure() = runBlocking {
        val api = FakeApi().apply {
            units = listOf(unitA, unitB)
            attendance[unitA.id] = score(2, 0, 0, 0.0, 1.0)
            attendanceFailures[unitB.id] = ApiFailure(503, "x", "service-unavailable")
        }
        val controller = controller(api)
        controller.signIn()
        controller.loadAttendance()
        val summary = summarizeAttendance(controller.state.units, controller.state.unitData)
        assertEquals(1000, summary.points)
        assertEquals(1, summary.answeredUnits); assertEquals(1, summary.failedUnits)
        assertTrue(controller.state.unitData[unitB.id]?.attendanceFailed == true)
        assertNull("Falha de leitura é silenciosa; a tela avisa", controller.state.message)
        assertNotNull(controller.state.session)
        // Na próxima leitura a UBS volta e o aviso some.
        api.attendanceFailures.clear(); api.attendance[unitB.id] = score(0, 1, 0, 0.0, 0.0)
        controller.loadAttendance()
        assertFalse(controller.state.unitData[unitB.id]?.attendanceFailed == true)
        assertEquals(667, summarizeAttendance(controller.state.units, controller.state.unitData).points)
    }

    @Test fun attendanceOfASingleUnitWithoutClosedAppointmentsHasMaximumPointsAndNoHistory() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); attendance[unitA.id] = score(0, 0, 0, 0.0, null) }
        val controller = controller(api)
        controller.signIn()
        controller.loadAttendance()
        val summary = summarizeAttendance(controller.state.units, controller.state.unitData)
        assertEquals(1000, summary.points); assertFalse(summary.hasHistory)
    }

    @Test fun attendanceUnauthorizedEndsTheSessionAndTransportFailuresDoNot() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB); attendanceFailures[unitA.id] = IOException("offline") ; attendance[unitB.id] = score(1, 0, 0, 0.0, 1.0) }
        val controller = controller(api)
        controller.signIn()
        controller.loadAttendance()
        assertNotNull(controller.state.session)
        api.attendanceFailures[unitA.id] = ApiFailure(401, "Sua sessão terminou. Entre novamente.", "unauthorized")
        controller.loadAttendance()
        assertNull(controller.state.session)
        assertTrue(controller.state.unitData.isEmpty())
        assertFalse(controller.state.attendanceLoading)
    }

    // ---- Remover vínculo ------------------------------------------------------------------------------

    @Test fun theUnlinkWarningAsksTheServerForTheExactNumbersEachTimeAndNeverRaisesBusy() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB) }
        val controller = controller(api)
        controller.signIn()
        controller.selectUnit(unitB.id)
        assertEquals(UnlinkImpact(0, 0), controller.checkUnlinkImpact(unitB.id))
        // Depois da primeira abertura a equipe marca consultas (uma delas daqui a dois anos) e o paciente entra na fila: o aviso usa
        // os números da prévia do servidor, sem contar nada no aparelho nem recortar por data.
        api.previews[unitB.id] = UnlinkImpact(3, 1)
        assertEquals("cada abertura do aviso relê a prévia", UnlinkImpact(3, 1), controller.checkUnlinkImpact(unitB.id))
        assertEquals(listOf(unitB.id, unitB.id), api.previewed)
        assertFalse(controller.state.busy)
    }

    @Test fun theUnlinkWarningIsGenericWhenThePreviewFails() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA) }
        val controller = controller(api)
        controller.signIn()
        for (failure in listOf(ApiFailure(503, "x", null), ApiFailure(429, "x", "too-many-requests"), ApiFailure(403, "x", "forbidden"), IOException("offline"))) {
            api.previewFailure = failure
            assertNull("sem como conferir, o aviso fica genérico", controller.checkUnlinkImpact(unitA.id))
        }
        assertNull(controller.state.message); assertNotNull(controller.state.session)
        api.previewFailure = ApiFailure(401, "Sessão expirada.", "unauthorized")
        assertNull(controller.checkUnlinkImpact(unitA.id))
        assertNull("401 encerra a sessão", controller.state.session)
    }

    @Test fun unlinkingClearsTheUnitAndSelectsTheOnlyRemainingOne() = runBlocking {
        val api = FakeApi().apply {
            units = listOf(unitA, unitB)
            offers[unitA.id] = listOf(UnitOffer("o1", "Cardiologia", "P", iso(7200), iso(9000), iso(600), "pending"))
            offers[unitB.id] = listOf(UnitOffer("o2", "Dermato", "P", iso(7200), iso(9000), iso(600), "pending"))
        }
        val controller = controller(api)
        var offersRemovedCalls = 0
        controller.onOffersRemoved = { offersRemovedCalls++ }
        controller.signIn()
        controller.selectUnit(unitA.id)
        controller.refreshUnit()
        assertNotNull(controller.state.unitData[unitA.id])
        controller.unlinkSelectedUnit()
        assertEquals(listOf(unitA.id), api.unlinked)
        assertEquals(listOf(unitB.id), controller.state.units?.map { it.id })
        assertEquals(unitB.id, controller.state.selectedUnitId)
        assertNull(controller.state.unitData[unitA.id])
        assertEquals("Você removeu UBS A. Foram canceladas 1 consulta e 2 solicitações de fila.", controller.state.message)
        assertEquals(1, offersRemovedCalls)
        assertEquals("A UBS restante é selecionada e seus dados carregados", listOf("o2"), controller.state.unitOffers?.map { it.id })
        assertTrue(openOffersAcrossUnits(controller.state.units, controller.state.unitData, now).none { it.unit.id == unitA.id })
        assertFalse(controller.state.busy)
    }

    @Test fun unlinkingWithSeveralLeftKeepsTheChoiceExplicit() = runBlocking {
        val third = PatientHealthUnit("unit-c", "UBS C", "Sul", "Rua C", -22.0, -45.0)
        val api = FakeApi().apply { units = listOf(unitA, unitB, third) }
        val controller = controller(api)
        controller.signIn()
        controller.selectUnit(unitA.id)
        controller.unlinkSelectedUnit()
        assertEquals(2, controller.state.units?.size)
        assertNull(controller.state.selectedUnitId)
    }

    @Test fun unlinkingTheLastUnitLeavesAnEmptyList() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); unlinkResult = UnlinkResult(0, 0) }
        val controller = controller(api)
        controller.signIn()
        controller.unlinkSelectedUnit()
        assertEquals(emptyList<PatientHealthUnit>(), controller.state.units)
        assertNull(controller.state.selectedUnitId)
        assertTrue(controller.state.unitData.isEmpty())
    }

    @Test fun unlinkSucceedsLocallyEvenWhenTheListCannotBeReread() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB) }
        val controller = controller(api)
        controller.signIn()
        controller.selectUnit(unitA.id)
        api.unitsFailure = IOException("offline")
        controller.unlinkSelectedUnit()
        assertEquals(listOf(unitB.id), controller.state.units?.map { it.id })
        assertTrue(controller.state.message.orEmpty().startsWith("Você removeu UBS A"))
    }

    @Test fun unlinkFailureKeepsTheUnitAndExplains() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB) }
        val controller = controller(api)
        controller.signIn()
        controller.selectUnit(unitA.id)
        api.unlinkFailure = ApiFailure(429, "x", "too-many-requests")
        controller.unlinkSelectedUnit()
        assertEquals(2, controller.state.units?.size)
        assertEquals(unitA.id, controller.state.selectedUnitId)
        assertEquals("Muitas tentativas. Aguarde alguns minutos e tente novamente.", controller.state.message)
    }

    // ---- Trocar senha ---------------------------------------------------------------------------------

    @Test fun passwordChangeKeepsTheSessionAndNeverStoresThePasswords() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA) }
        val controller = controller(api)
        controller.signIn()
        controller.changePassword("SenhaAtual-Teste-1", "SenhaNova-Teste-2")
        assertEquals(listOf("SenhaAtual-Teste-1" to "SenhaNova-Teste-2"), api.passwordCalls)
        assertNotNull(controller.state.session)
        assertFalse(controller.state.passwordChangeRequired)
        val dump = controller.state.toString()
        assertFalse(dump.contains("SenhaAtual-Teste-1")); assertFalse(dump.contains("SenhaNova-Teste-2"))
        assertEquals("https://nora.example.test", store.address)
    }

    @Test fun wrongCurrentPasswordIsReportedWithItsCodeAndTheSessionStays() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); passwordFailure = ApiFailure(400, "A senha atual está incorreta.", INVALID_CURRENT_PASSWORD) }
        val controller = controller(api)
        controller.signIn()
        try { controller.changePassword("errada", "SenhaNova-Teste-2"); fail("expected failure") }
        catch (failure: ApiFailure) { assertEquals(INVALID_CURRENT_PASSWORD, failure.code) }
        assertNotNull(controller.state.session)
        assertTrue(api.passwordCalls.isEmpty())
    }

    @Test fun passwordChangeRequiredFromAnyCallRedirectsAndTheChangeClearsIt() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA) }
        val controller = controller(api)
        controller.signIn()
        assertFalse(controller.state.passwordChangeRequired)
        api.offersFailure = ApiFailure(403, "x", PASSWORD_CHANGE_REQUIRED)
        controller.refreshUnit()
        // refreshUnit lê as ofertas; a leitura respondeu 403 com o código.
        assertTrue(controller.state.passwordChangeRequired)
        assertEquals(PASSWORD_CHANGE_REQUIRED_MESSAGE, controller.state.message)
        assertNotNull("O app não encerra a sessão: leva à troca de senha", controller.state.session)
        controller.changePassword("provisoria-1", "definitiva-2")
        assertFalse(controller.state.passwordChangeRequired)
    }

    @Test fun passwordChangeRequiredFromABackgroundReadAlsoRaisesTheFlag() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA, unitB); attendanceFailures[unitA.id] = ApiFailure(403, "x", PASSWORD_CHANGE_REQUIRED) }
        val controller = controller(api)
        controller.signIn()
        controller.loadAttendance()
        assertTrue(controller.state.passwordChangeRequired)
    }

    @Test fun unauthorizedOnPasswordChangeEndsTheSessionAndClearsTheFlag() = runBlocking {
        val api = FakeApi().apply { units = listOf(unitA); passwordFailure = ApiFailure(401, "Sua sessão terminou. Entre novamente.", "unauthorized") }
        val controller = controller(api)
        controller.signIn()
        try { controller.changePassword("a", "b12345678"); fail("expected failure") } catch (_: ApiFailure) {}
        assertNull(controller.state.session)
        assertFalse(controller.state.passwordChangeRequired)
    }

    @Test fun passwordFieldsAreNeverSavedAcrossRotation() {
        // Guarda estática: só os botões de mostrar/ocultar podem usar rememberSaveable na tela de senha.
        val source = File("src/main/java/com/example/app_nora/ui/PasswordUi.kt").readText()
        val saveable = source.lines().filter { line ->
            "rememberSaveable" in line && line.trimStart().let { !it.startsWith("import") && !it.startsWith("*") && !it.startsWith("/") }
        }
        assertTrue(saveable.isNotEmpty())
        assertTrue("Senha em rememberSaveable: $saveable", saveable.all { it.contains("var show") })
    }
}
