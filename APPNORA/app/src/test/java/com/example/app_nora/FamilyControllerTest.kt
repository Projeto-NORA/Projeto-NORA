package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

/** Família: pedidos, estados, "agindo por" e as rotas do dependente, com API falsa. */
class FamilyControllerTest {
    private val now = requireNotNull(parseInstantMillis("2026-11-16T12:00:00Z"))
    private val hour = 3_600_000L
    private val day = 24 * hour
    private val validCpf = "529.982.247-25"

    private fun appointment(id: String, startsIn: Long, status: String = "scheduled", confirmedAt: String? = null) = PatientAppointment(
        id, "Cardiologia", "Ana", formatInstantUtc(now + startsIn), formatInstantUtc(now + startsIn + 1_800_000), status, confirmedAt)

    private fun offer(id: String, expiresIn: Long = 600_000) = UnitOffer(id, "Cardiologia", "Dra. Ana", formatInstantUtc(now + day),
        formatInstantUtc(now + day + 1_800_000), formatInstantUtc(now + expiresIn), "pending")

    private suspend fun ready(api: FamilyFakeApi, vararg units: PatientHealthUnit): PatientController {
        api.units = units.toList()
        val controller = PatientController(MemoryAddressStore()) { api }
        controller.clock = { now }
        controller.login("paciente@example.test", "test-only-input")
        return controller
    }

    /** Responsável com um dependente aprovado (vínculo `dep-1`, UBS B) e a lista de família já lida. */
    private suspend fun withDependent(api: FamilyFakeApi, status: String = "approved"): PatientController {
        api.links = mutableListOf(link("dep-1", status, "unit-b", "UBS B"))
        val controller = ready(api, unitA, unitB)
        controller.loadFamily()
        return controller
    }

    // ---- Pedir o vínculo -------------------------------------------------------------------------------------

    @Test fun anInvalidCpfNeverReachesTheServer() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        assertFalse(controller.requestDependent("111.111.111-11", "unit-a"))
        assertEquals("Confira o CPF informado.", controller.state.message)
        assertFalse(controller.requestDependent("123", "unit-a"))
        assertFalse(controller.requestDependent(validCpf, ""))
        assertTrue(api.requested.isEmpty())
        assertFalse(controller.state.busy)
    }

    @Test fun aValidRequestSendsOnlyDigitsAndAddsThePendingLinkFirst() = runBlocking {
        val api = FamilyFakeApi()
        api.links = mutableListOf(link("old", "rejected", reason = "Documento ilegível"))
        api.requestResult = link("new", "pending", "unit-a", "UBS A", name = null)
        val controller = ready(api, unitA)
        controller.loadFamily()
        assertTrue(controller.requestDependent(validCpf, "unit-a"))
        assertEquals(listOf("52998224725" to "unit-a"), api.requested)
        assertEquals(listOf("new", "old"), controller.state.family.links?.map { it.id })
        assertTrue(controller.state.message.orEmpty().contains("equipe da UBS"))
        assertNull(controller.state.family.links?.first()?.dependentName)
    }

    @Test fun eachRequestRefusalHasItsOwnMessageAndNoServerText() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        suspend fun refuse(status: Int, code: String?, expected: String) {
            api.requestFailure = failure(status, code, ApiContext.FAMILY_REQUEST)
            assertFalse(controller.requestDependent(validCpf, "unit-a"))
            assertEquals(expected, controller.state.message)
        }
        refuse(404, "not-found", "Não encontramos um cadastro ativo com este CPF nesta UBS. Confira o CPF e a UBS ou procure a equipe da unidade.")
        refuse(409, FAMILY_LINK_EXISTS, "Você já tem um pedido pendente ou um vínculo aprovado com esta pessoa nesta UBS.")
        refuse(409, FAMILY_LINK_LIMIT, "Você chegou ao limite de 20 pedidos e vínculos ativos. Encerre algum antes de pedir outro.")
        refuse(400, "invalid-input", "Confira o CPF e a UBS. Você não pode pedir vínculo com o seu próprio CPF.")
        refuse(429, "too-many-requests", "Muitos pedidos de vínculo em pouco tempo. Aguarde alguns minutos e tente novamente.")
    }

    // ---- Estados -----------------------------------------------------------------------------------------------

    @Test fun theListShowsEveryStatusAndTheRejectionReason() = runBlocking {
        val api = FamilyFakeApi()
        api.links = mutableListOf(link("1", "pending", name = null), link("2", "rejected", reason = "Falta a certidão"),
            link("3", "approved"), link("4", "cancelled"), link("5", "ended"))
        val controller = ready(api, unitA)
        controller.loadFamily()
        val links = controller.state.family.links.orEmpty()
        assertEquals(listOf("pending", "rejected", "approved", "cancelled", "ended"), links.map { it.status })
        assertEquals("Falta a certidão", links[1].decisionReason)
        assertNull(controller.state.family.error)
        assertFalse(controller.state.family.loading)
    }

    @Test fun aFailedListReadKeepsAnErrorAndRetryWorks() = runBlocking {
        val api = FamilyFakeApi()
        api.linksFailure = IOException("offline")
        val controller = ready(api, unitA)
        controller.loadFamily()
        assertNull(controller.state.family.links)
        assertEquals(CONNECTION_FAILED_MESSAGE, controller.state.family.error)
        assertFalse(controller.state.family.loading)
        api.linksFailure = null
        controller.loadFamily()
        assertNotNull(controller.state.family.links)
        assertNull(controller.state.family.error)
    }

    @Test fun cancellingAPendingRequestUpdatesItAndNothingElse() = runBlocking {
        val api = FamilyFakeApi()
        api.links = mutableListOf(link("p", "pending", name = null), link("a", "approved"))
        api.changeResult = link("p", "cancelled", name = null)
        val controller = ready(api, unitA)
        controller.loadFamily()
        controller.cancelDependentRequest("a")   // aprovado não se cancela
        assertTrue(api.callsOf("own.cancelRequest").isEmpty())
        controller.cancelDependentRequest("p")
        assertEquals(listOf("own.cancelRequest p"), api.callsOf("own.cancelRequest"))
        assertEquals(listOf("cancelled", "approved"), controller.state.family.links?.map { it.status })
        assertEquals("Pedido cancelado.", controller.state.message)
    }

    @Test fun endingTheLinkWhileActingForItGoesBackToTheOwnPatient() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        api.changeResult = link("dep-1", "ended", "unit-b", "UBS B")
        controller.endDependent("dep-1")
        assertNull(controller.state.actingFor)
        assertTrue(controller.state.dependentData.isEmpty())
        assertEquals("ended", controller.state.family.links?.single()?.status)
        assertTrue(controller.state.message.orEmpty().contains("Maria Souza"))
    }

    @Test fun onlyAnApprovedLinkCanBeEnded() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withDependent(api, status = "pending")
        controller.endDependent("dep-1")
        assertTrue(api.callsOf("own.endLink").isEmpty())
    }

    // ---- Agindo por ------------------------------------------------------------------------------------------

    @Test fun onlyAnApprovedLinkCanBeActedForAndSwitchingBackIsImmediate() = runBlocking {
        val api = FamilyFakeApi()
        val pending = withDependent(api, status = "pending")
        assertFalse(pending.actFor("dep-1")); assertNull(pending.state.actingFor)
        val controller = withDependent(FamilyFakeApi())
        assertFalse(controller.actFor("desconhecido")); assertNull(controller.state.actingFor)
        assertTrue(controller.actFor("dep-1")); assertEquals("dep-1", controller.state.actingFor)
        assertFalse(controller.actFor(null).also { assertNull(controller.state.actingFor) })
    }

    @Test fun actingForADependentReadsOnlyThroughTheDependentRoutesAndKeepsTheOwnDataUntouched() = runBlocking {
        val api = FamilyFakeApi()
        api.ownAppointments["unit-a"] = listOf(appointment("own-1", 2 * day))
        api.depAppointments["dep-1"] = listOf(appointment("dep-ap", 3 * day))
        api.depOffers["dep-1"] = listOf(offer("dep-offer"))
        val controller = withDependent(api)
        val ownBefore = controller.state.unitData
        api.calls.clear()
        controller.switchActing("dep-1")
        assertEquals(listOf("dep.offers dep-1 unit-b", "dep.appointments dep-1 unit-b"), api.calls)
        val view = controller.state.effective()
        assertEquals(listOf("unit-b"), view.units?.map { it.id })
        assertEquals(listOf("dep-ap"), view.appointments?.map { it.id })
        assertEquals(listOf("dep-offer"), view.unitOffers?.map { it.id })
        assertEquals("os dados do responsável não mudam", ownBefore, controller.state.unitData)
        // Voltar a agir por si mostra de novo os dados dele, sem ler nada.
        api.calls.clear()
        assertFalse(controller.actFor(null))
        assertTrue(api.calls.isEmpty())
        assertNull(controller.state.actingFor)
        assertSame(controller.state, controller.state.effective())
    }

    @Test fun everyActionAsADependentUsesTheDependentRoute() = runBlocking {
        val api = FamilyFakeApi()
        api.depAppointments["dep-1"] = listOf(appointment("ap-1", 30 * hour), appointment("ap-2", 6 * day))
        api.depOffers["dep-1"] = listOf(offer("off-1"))
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        api.calls.clear()
        controller.confirmPresence("unit-b", "ap-1")
        assertEquals(listOf("dep.confirm dep-1 unit-b ap-1"), api.calls)
        assertNotNull(controller.state.effective().appointments?.first { it.id == "ap-1" }?.confirmedAt)
        api.calls.clear()
        controller.cancelAppointment("ap-2")
        assertEquals("dep.cancelAppointment dep-1 unit-b ap-2", api.calls.first())
        api.calls.clear()
        controller.respondUnitOffer("off-1", false)
        assertEquals("dep.respond dep-1 unit-b off-1 false", api.calls.first())
        assertTrue("nenhuma rota do próprio paciente foi usada", api.calls.none { it.startsWith("own.") })
        api.calls.clear()
        controller.loadAttendance()
        controller.loadRecords()
        assertEquals(listOf("dep.attendance dep-1 unit-b", "dep.records dep-1 unit-b"), api.calls)
    }

    @Test fun bookingAsADependentUsesTheDependentAvailabilityAndBookRoutes() = runBlocking {
        val api = FamilyFakeApi()
        val slot = AvailableSlot(formatInstantUtc(now + 2 * day), formatInstantUtc(now + 2 * day + 1_800_000), "prof-1", "Ana", "spec-1", "Cardiologia")
        api.depSlots = listOf(slot)
        api.depBookResult = appointment("novo", 2 * day).copy(bookedBy = "patient")
        api.depAppointments["dep-1"] = listOf(appointment("novo", 2 * day))
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        api.calls.clear()
        controller.loadAvailability("spec-1", 0)
        controller.bookAppointment(slot)
        assertEquals("dep.availability dep-1 unit-b spec-1", api.calls.first())
        assertTrue(api.calls.contains("dep.book dep-1 unit-b prof-1"))
        assertTrue(api.calls.none { it.startsWith("own.book") || it.startsWith("own.availability") })
        assertEquals("novo", controller.state.lastBooked?.appointment?.id)
    }

    @Test fun bookingRefusalsForADependentSpeakAboutThePersonNotAboutYou() = runBlocking {
        val api = FamilyFakeApi()
        val slot = AvailableSlot(formatInstantUtc(now + 2 * day), formatInstantUtc(now + 2 * day + 1_800_000), "prof-1", "Ana", "spec-1", "Cardiologia")
        api.depSlots = listOf(slot)
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        controller.loadAvailability("spec-1", 0)
        api.depFailure = failure(409, SPECIALTY_LIMIT, ApiContext.BOOKING)
        controller.bookAppointment(slot)
        assertTrue(controller.state.message.orEmpty().startsWith("Esta pessoa já tem"))
    }

    @Test fun aForbiddenAnswerWhileActingRereadsTheLinksAndReturnsToTheOwnPatientWhenTheLinkEnded() = runBlocking {
        val api = FamilyFakeApi()
        api.depAppointments["dep-1"] = listOf(appointment("ap-1", 30 * hour))
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        // A equipe encerrou o vínculo: as rotas do dependente respondem 403 e a lista de vínculos já não o traz aprovado.
        api.links = mutableListOf(link("dep-1", "ended", "unit-b", "UBS B"))
        api.depFailure = failure(403, "forbidden", ApiContext.UNIT)
        controller.confirmPresence("unit-b", "ap-1")
        assertNull(controller.state.actingFor)
        assertEquals(DEPENDENT_FORBIDDEN_MESSAGE, controller.state.message)
        assertEquals("ended", controller.state.family.links?.single()?.status)
    }

    @Test fun selectingAUnitDoesNothingWhileActingForADependent() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withDependent(api)
        controller.selectUnit("unit-a")
        controller.switchActing("dep-1")
        controller.selectUnit("unit-a")
        assertEquals("unit-a", controller.state.selectedUnitId)
        assertEquals("unit-b", controller.state.effective().selectedUnitId)
        assertNull(controller.selectNearestUnit(-23.5, -46.6))
    }

    @Test fun openingTheUnitsScreenWhileActingReturnsToTheOwnUnitsWithoutAnyErrorOfTheDependent() = runBlocking {
        val api = FamilyFakeApi()
        api.depAppointments["dep-1"] = listOf(appointment("ap-1", 30 * hour))
        val held = CompletableDeferred<Unit>()
        // O cancelamento da consulta do dependente fica em voo até [held]; a tela UBS abre no meio dele.
        val gate = object : PatientApi by api {
            override suspend fun dependentCancelAppointment(token: String, dependentId: String, unitId: String, id: String) {
                held.await(); api.dependentCancelAppointment(token, dependentId, unitId, id)
            }
        }
        api.links = mutableListOf(link("dep-1", "approved", "unit-b", "UBS B"))
        api.units = listOf(unitA, unitB)
        val controller = PatientController(MemoryAddressStore()) { gate }
        controller.clock = { now }
        controller.login("paciente@example.test", "test-only-input")
        controller.loadFamily()
        controller.switchActing("dep-1")
        val cancelling = launch { controller.cancelAppointment("ap-1") }
        yield()
        assertTrue(controller.state.busy)
        assertTrue("abrir a tela UBS sai do agindo por, mesmo com a ação do dependente em andamento", controller.showOwnUnits())
        assertNull(controller.state.actingFor)
        api.depFailure = failure(403, "forbidden", ApiContext.APPOINTMENT_CANCEL)
        held.complete(Unit)
        cancelling.join()
        assertNull("o erro do dependente não chega à tela UBS", controller.state.message)
        assertFalse(controller.state.busy)
        assertFalse("já nas próprias UBS, não há o que avisar", controller.showOwnUnits())
        // Atualizar, escolher e localizar valem para as UBS do próprio responsável.
        api.calls.clear()
        controller.loadUnits()
        assertTrue(api.calls.toString(), api.calls.none { it.startsWith("dep.") })
        controller.selectUnit("unit-b")
        assertEquals("unit-b", controller.state.selectedUnitId)
        assertEquals("unit-a", controller.selectNearestUnit(-23.5, -46.6)?.id)
        assertEquals("unit-a", controller.state.effective().selectedUnitId)
    }

    @Test fun switchingTheActingPersonDropsTheBookingStateOfTheOther() = runBlocking {
        val api = FamilyFakeApi()
        val slot = AvailableSlot(formatInstantUtc(now + 2 * day), formatInstantUtc(now + 2 * day + 1_800_000), "prof-1", "Ana", "spec-1", "Cardiologia")
        api.depSlots = listOf(slot)
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        controller.loadAvailability("spec-1", 0)
        assertNotNull(controller.state.availability)
        controller.actFor(null)
        assertNull(controller.state.availability)
        assertNull(controller.state.lastBooked)
    }

    // ---- Leituras em voo ao trocar o "agindo por" -------------------------------------------------------------------------

    /** API cujas leituras de participação e de Jornada esperam um sinal, para trocar o "agindo por" no meio delas. */
    private class GatedApi(private val inner: FamilyFakeApi) : PatientApi by inner {
        val ownAttendance = CompletableDeferred<Unit>()
        val ownRecords = CompletableDeferred<Unit>()
        val depRecords = CompletableDeferred<Unit>()
        var ownRecordsFailure: Throwable? = null
        /** Lista de UBS: falha enquanto [unitsFailure] existir e, com [unitsGate], espera o sinal. */
        var unitsFailure: Throwable? = null
        var unitsGate: CompletableDeferred<Unit>? = null
        override suspend fun units(token: String): List<PatientHealthUnit> {
            unitsGate?.await(); unitsFailure?.let { throw it }; return inner.units(token)
        }
        override suspend fun attendance(token: String, unitId: String): UnitAttendance { ownAttendance.await(); return inner.attendance(token, unitId) }
        override suspend fun healthRecords(token: String): List<UnitRecords> {
            ownRecords.await(); ownRecordsFailure?.let { throw it }; return inner.healthRecords(token)
        }
        override suspend fun dependentRecords(token: String, dependentId: String, unitId: String): UnitRecords {
            depRecords.await(); return inner.dependentRecords(token, dependentId, unitId)
        }
    }

    private suspend fun gated(api: FamilyFakeApi): Pair<PatientController, GatedApi> {
        val gate = GatedApi(api)
        api.links = mutableListOf(link("dep-1", "approved", "unit-b", "UBS B"))
        api.units = listOf(unitA, unitB)
        val controller = PatientController(MemoryAddressStore()) { gate }
        controller.clock = { now }
        controller.login("paciente@example.test", "test-only-input")
        controller.loadFamily()
        return controller to gate
    }

    @Test fun anOwnAttendanceReadStillRunningDoesNotBlockTheDependentsReadAndDoesNotClearItsLoading() = runBlocking {
        val api = FamilyFakeApi()
        api.depAttendance["dep-1"] = UnitAttendance(3, 2, 1, 0.5, 0.8, 12)
        val (controller, gate) = gated(api)
        val own = launch { controller.loadAttendance() }
        yield()
        assertTrue(controller.state.attendanceLoading)
        // O responsável troca para o dependente com a leitura própria ainda em voo.
        controller.actFor("dep-1")
        assertFalse("a leitura em voo era do responsável, não do dependente", controller.state.attendanceLoading)
        val dependent = launch { controller.loadAttendance() }
        yield()
        assertTrue("a participação do dependente começa mesmo com a leitura própria em voo", api.callsOf("dep.attendance").isNotEmpty())
        gate.ownAttendance.complete(Unit)
        own.join(); dependent.join()
        assertEquals(3, controller.state.effective().unitData["unit-b"]?.attendance?.completed)
        assertFalse(controller.state.attendanceLoading)
        assertFalse(controller.state.effective().unitData["unit-b"]?.attendanceFailed ?: true)
    }

    @Test fun anOwnRecordsReadThatFailsLateNeitherEndsTheDependentsLoadingNorLeaksItsError() = runBlocking {
        val api = FamilyFakeApi()
        val (controller, gate) = gated(api)
        gate.ownRecordsFailure = IOException("sem rede")
        val own = launch { controller.loadRecords() }
        yield()
        controller.actFor("dep-1")
        val dependent = launch { controller.loadRecords() }
        yield()
        assertTrue(controller.state.records.loading)
        gate.ownRecords.complete(Unit)
        own.join()
        assertTrue("a leitura do dependente continua em andamento", controller.state.records.loading)
        assertNull("o erro da leitura própria não aparece na visão do dependente", controller.state.records.error)
        gate.depRecords.complete(Unit)
        dependent.join()
        assertFalse(controller.state.records.loading)
        assertNull(controller.state.records.error)
    }

    @Test fun aLinkThatEndsDuringTheDependentsRecordsReadDoesNotLeaveTheOwnRecordsStuckLoading() = runBlocking {
        val api = FamilyFakeApi()
        val (controller, gate) = gated(api)
        controller.actFor("dep-1")
        val dependent = launch { controller.loadRecords() }
        yield()
        assertTrue(controller.state.records.loading)
        // O responsável encerra o vínculo com a leitura do dependente ainda em voo.
        api.changeResult = link("dep-1", "ended", "unit-b", "UBS B")
        controller.endDependent("dep-1")
        assertNull(controller.state.actingFor)
        gate.depRecords.complete(Unit)
        dependent.join()
        assertFalse("a visão própria não herda o indicador da leitura do dependente", controller.state.records.loading)
        gate.ownRecords.complete(Unit)
        controller.loadRecords()
        assertEquals("a leitura própria acontece", listOf("own.records"), api.callsOf("own.records"))
        assertFalse(controller.state.records.loading)
    }

    @Test fun anOwnRecordsReadWaitingForTheUnitsDoesNotMarkTheDependentsViewAsLoading() = runBlocking {
        val api = FamilyFakeApi()
        val gate = GatedApi(api)
        api.links = mutableListOf(link("dep-1", "approved", "unit-b", "UBS B"))
        api.units = listOf(unitA, unitB)
        val controller = PatientController(MemoryAddressStore()) { gate }
        controller.clock = { now }
        gate.unitsFailure = IOException("sem rede")
        controller.login("paciente@example.test", "test-only-input")
        controller.loadFamily()
        assertNull(controller.state.units)
        gate.unitsFailure = null
        gate.unitsGate = CompletableDeferred()
        gate.ownRecords.complete(Unit)
        val own = launch { controller.loadRecords() }
        yield()
        // As UBS ainda estão chegando quando o responsável passa a agir pelo dependente.
        assertTrue(controller.actFor("dep-1"))
        gate.unitsGate?.complete(Unit)
        own.join()
        assertEquals("dep-1", controller.state.actingFor)
        assertFalse("a leitura própria não deixa a Jornada do dependente presa em 'Atualizando…'", controller.state.records.loading)
    }

    @Test fun aForbiddenRecordsReadWhileActingRereadsTheLinks() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        assertEquals(1, api.callsOf("own.dependents").size)
        // A equipe encerrou o vínculo: a Jornada do dependente responde 403 e a lista de vínculos já não o traz aprovado.
        api.links = mutableListOf(link("dep-1", "ended", "unit-b", "UBS B"))
        api.depFailure = failure(403, "forbidden", ApiContext.RECORDS)
        controller.loadRecords()
        assertEquals("os vínculos são relidos", 2, api.callsOf("own.dependents").size)
        assertNull(controller.state.actingFor)
        assertNull("o erro do dependente não fica na visão própria", controller.state.records.error)
        assertFalse(controller.state.records.loading)
    }

    @Test fun aForbiddenAttendanceReadWhileActingRereadsTheLinks() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        assertEquals(1, api.callsOf("own.dependents").size)
        // A equipe encerrou o vínculo: a participação do dependente (também da Jornada) responde 403 e os vínculos precisam ser relidos.
        api.links = mutableListOf(link("dep-1", "ended", "unit-b", "UBS B"))
        api.depFailure = failure(403, "forbidden", ApiContext.UNIT)
        controller.loadAttendance()
        assertEquals("os vínculos são relidos", 2, api.callsOf("own.dependents").size)
        assertNull("sem vínculo aprovado volta a agir por si", controller.state.actingFor)
        assertFalse(controller.state.attendanceLoading)
    }

    @Test fun aForbiddenAvailabilityReadThatEndsTheLinkDoesNotLeaveTheDependentsErrorOnTheOwnView() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        api.links = mutableListOf(link("dep-1", "ended", "unit-b", "UBS B"))
        api.depFailure = failure(403, "forbidden", ApiContext.AVAILABILITY)
        controller.loadAvailability("spec-1", 0)
        assertNull(controller.state.actingFor)
        assertNull("o erro do dependente não é da visão própria", controller.state.bookingError)
        assertFalse(controller.state.availabilityLoading)
        assertNull(controller.state.availability)
    }

    // ---- Ofertas dos dependentes no ciclo de 30 s ---------------------------------------------------------------

    @Test fun theCycleReadsTheDependentsOpenOffersWithOneRequestAndNotificationsCarryTheName() = runBlocking {
        val api = FamilyFakeApi()
        api.links = mutableListOf(link("dep-1", "approved", "unit-b", "UBS B"))
        api.dependentOffersResult = listOf(DependentOffers("dep-1", "unit-b", "UBS B", "Maria Souza", listOf(offer("off-1"))))
        api.units = listOf(unitA, unitB)
        val controller = PatientController(MemoryAddressStore()) { api }
        controller.login("paciente@example.test", "test-only-input")
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        controller.pollCommunity(session, generation)
        assertEquals(1, api.callsOf("own.dependentOffers").size)
        assertEquals(listOf("off-1"), controller.state.dependentOffers.flatMap { it.offers }.map { it.id })
        val notice = openOfferNotices(controller.state, now).single()
        assertEquals("Vaga Relâmpago para Maria Souza · Cardiologia", offerNoticeTitle(notice))
        assertEquals("dep-1", notice.dependentId)
        // Segundo ciclo: sem pedido pendente a lista de família não é lida de novo, mas as ofertas sim (uma chamada).
        controller.pollCommunity(session, generation)
        assertEquals(1, api.callsOf("own.dependents").size)
        assertEquals(2, api.callsOf("own.dependentOffers").size)
    }

    @Test fun aNewOfferOfTheDependentBeingActedForReachesTheScreensInTheSameCycle() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        assertEquals(emptyList<UnitOffer>(), controller.state.effective().unitOffers)
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        // A UBS cria uma vaga e Maria recebe uma oferta: o ciclo de 30 s a lê pela rota agregada.
        api.depOffers["dep-1"] = listOf(offer("off-2"))
        api.dependentOffersResult = listOf(DependentOffers("dep-1", "unit-b", "UBS B", "Maria Souza", listOf(offer("off-2"))))
        controller.pollCommunity(session, generation)
        val view = controller.state.effective()
        assertEquals("Vaga Relâmpago do dependente", listOf("off-2"), view.unitOffers?.map { it.id })
        assertEquals("Início do dependente", listOf("off-2"), openOffersAcrossUnits(view.units, view.unitData, now).map { it.offer.id })
        // Sem mudança, o ciclo seguinte não relê a lista do dependente.
        val reads = api.callsOf("dep.offers").size
        controller.pollCommunity(session, generation)
        assertEquals(reads, api.callsOf("dep.offers").size)
    }

    @Test fun aPendingRequestIsRereadEachCycleSoTheApprovalAppears() = runBlocking {
        val api = FamilyFakeApi()
        api.links = mutableListOf(link("dep-1", "pending", "unit-b", "UBS B", name = null))
        val controller = ready(api, unitA, unitB)
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        controller.pollCommunity(session, generation)
        controller.pollCommunity(session, generation)
        assertEquals(2, api.callsOf("own.dependents").size)
        api.links = mutableListOf(link("dep-1", "approved", "unit-b", "UBS B"))
        controller.pollCommunity(session, generation)
        assertEquals("approved", controller.state.family.links?.single()?.status)
        controller.pollCommunity(session, generation)
        assertEquals("aprovado: a lista não é lida de novo", 3, api.callsOf("own.dependents").size)
    }

    @Test fun respondingToADependentsOfferRemovesItFromTheCycleNoticeAtOnce() = runBlocking {
        val api = FamilyFakeApi()
        api.depOffers["dep-1"] = listOf(offer("off-1"))
        api.links = mutableListOf(link("dep-1", "approved", "unit-b", "UBS B"))
        api.dependentOffersResult = listOf(DependentOffers("dep-1", "unit-b", "UBS B", "Maria Souza", listOf(offer("off-1"))))
        val controller = ready(api, unitA, unitB)
        val session = requireNotNull(controller.state.session)
        controller.pollCommunity(session, controller.beginPresence())
        assertEquals(1, controller.state.dependentOffers.size)
        controller.switchActing("dep-1")
        controller.respondUnitOffer("off-1", true)
        assertTrue(controller.state.dependentOffers.isEmpty())
    }

    @Test fun aLinkThatStopsBeingApprovedDropsItsDataAndOffers() = runBlocking {
        val api = FamilyFakeApi()
        api.links = mutableListOf(link("dep-1", "approved", "unit-b", "UBS B"))
        api.dependentOffersResult = listOf(DependentOffers("dep-1", "unit-b", "UBS B", "Maria Souza", listOf(offer("off-1"))))
        val controller = ready(api, unitA, unitB)
        val session = requireNotNull(controller.state.session)
        val generation = controller.beginPresence()
        controller.pollCommunity(session, generation)
        controller.switchActing("dep-1")
        api.links = mutableListOf(link("dep-1", "ended", "unit-b", "UBS B"))
        controller.loadFamily()
        assertNull(controller.state.actingFor)
        assertTrue(controller.state.dependentData.isEmpty()); assertTrue(controller.state.dependentOffers.isEmpty())
    }

    @Test fun signingOutClearsEverythingOfTheFamilyAndCommunity() = runBlocking {
        val api = FamilyFakeApi()
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        controller.logout()
        val state = controller.state
        assertNull(state.session); assertNull(state.actingFor)
        assertNull(state.family.links); assertTrue(state.dependentData.isEmpty()); assertTrue(state.dependentOffers.isEmpty())
        assertEquals(AnnouncementsState(), state.announcements); assertEquals(EmailState(), state.email)
    }
}
