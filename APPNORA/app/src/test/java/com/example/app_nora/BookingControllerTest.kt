package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

/** Agendamento pelo catálogo e confirmação de presença no controlador, com API falsa. */
class BookingControllerTest {
    private class MemoryStore(var address: String = "") : AddressStore {
        override fun load() = address
        override fun save(address: String) { this.address = address }
    }

    private val now = requireNotNull(parseInstantMillis("2026-11-16T12:00:00Z"))
    private val hour = 3_600_000L
    private val day = 24 * hour
    private val unitA = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", -23.5, -46.6)
    private val unitB = PatientHealthUnit("unit-b", "UBS B", "Norte", "Rua B", -20.0, -40.0)
    private val cardiology = Specialty("spec-card", "Cardiologia")

    private fun slot(startsIn: Long, professional: String = "Ana", id: String = "prof-1") = AvailableSlot(
        formatInstantUtc(now + startsIn), formatInstantUtc(now + startsIn + 1_800_000), id, professional, cardiology.id, cardiology.name)

    private fun appointment(id: String, startsIn: Long, status: String = "scheduled", confirmedAt: String? = null, bookedBy: String = "staff") =
        PatientAppointment(id, "Cardiologia", "Ana", formatInstantUtc(now + startsIn), formatInstantUtc(now + startsIn + 1_800_000), status, confirmedAt, bookedBy)

    private class FakeApi : PatientApi {
        var units = listOf<PatientHealthUnit>()
        var specialtyList = listOf<Specialty>()
        var slots = listOf<AvailableSlot>()
        var appointments = listOf<PatientAppointment>()
        var bookFailure: Throwable? = null
        var confirmFailure: Throwable? = null
        var availabilityFailure: Throwable? = null
        var bookResult: PatientAppointment? = null
        var appointmentsFailure: Throwable? = null
        var gateNextAvailability: CompletableDeferred<Unit>? = null
        val availabilityRequests = mutableListOf<Triple<String, Long, Long>>()
        val bookRequests = mutableListOf<List<String>>()
        var specialtyCalls = 0
        var appointmentCalls = 0
        var confirmCalls = 0
        /** Instante da primeira confirmação aceita; repetir devolve o mesmo (idempotência do servidor). */
        var confirmedAt: String? = null

        override suspend fun login(identifier: String, password: String) = PatientSession("token", "2099-01-01T00:00:00Z", PatientUser("id", "Paciente de teste"))
        override suspend fun units(token: String) = units
        override suspend fun unitOffers(token: String, unitId: String) = emptyList<UnitOffer>()
        override suspend fun appointments(token: String, unitId: String): List<PatientAppointment> {
            appointmentCalls++; appointmentsFailure?.let { throw it }; return appointments
        }
        override suspend fun specialties(token: String): List<Specialty> { specialtyCalls++; return specialtyList }
        override suspend fun availability(token: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long): List<AvailableSlot> {
            availabilityRequests += Triple(unitId, fromMillis, toMillis)
            val answer = slots
            gateNextAvailability?.let { gateNextAvailability = null; it.await() }
            availabilityFailure?.let { throw it }
            return answer
        }
        override suspend fun bookAppointment(token: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String): PatientAppointment {
            bookRequests += listOf(unitId, professionalId, specialtyId, startsAt)
            bookFailure?.let { throw it }
            return requireNotNull(bookResult)
        }
        override suspend fun confirmPresence(token: String, unitId: String, id: String): PatientAppointment {
            confirmCalls++
            confirmFailure?.let { throw it }
            val original = confirmedAt ?: "2026-11-17T09:30:00Z".also { confirmedAt = it }
            return appointments.first { it.id == id }.copy(confirmedAt = original)
        }
        override suspend fun logout(token: String) = Unit
        override suspend fun presence(token: String, online: Boolean) = Unit
    }

    private fun failure(status: Int, code: String, context: ApiContext) = ApiFailure(status, failureMessage(status, code, context), code)

    private suspend fun ready(api: FakeApi, vararg units: PatientHealthUnit, via: PatientApi = api): PatientController {
        api.units = units.toList()
        val controller = PatientController(MemoryStore("https://nora.example.test")) { via }
        controller.clock = { now }
        controller.login("paciente@example.test", "test-only-input")
        return controller
    }

    // ---- Especialidades e horários -------------------------------------------------------------------------

    @Test fun openingTheBookingReadsTheSpecialtiesOnceAndClearsTheLastResult() = runBlocking {
        val api = FakeApi().apply { specialtyList = listOf(cardiology) }
        val controller = ready(api, unitA)
        controller.openBooking()
        assertEquals(listOf(cardiology), controller.state.specialties)
        assertEquals(1, api.specialtyCalls)
        assertFalse(controller.state.specialtiesLoading)
        assertNull(controller.state.bookingError)
    }

    @Test fun specialtiesFailureIsAMessageNotACrashAndRetryWorks() = runBlocking {
        val api = FakeApi().apply { specialtyList = listOf(cardiology) }
        var offline = true
        val flaky = object : PatientApi by api {
            override suspend fun specialties(token: String): List<Specialty> = if (offline) throw IOException("offline") else api.specialties(token)
        }
        val controller = ready(api, unitA, via = flaky)
        controller.openBooking()
        assertNull(controller.state.specialties)
        assertEquals(CONNECTION_FAILED_MESSAGE, controller.state.bookingError)
        assertFalse(controller.state.specialtiesLoading)
        offline = false
        controller.openBooking()
        assertEquals(listOf(cardiology), controller.state.specialties)
        assertNull(controller.state.bookingError)
    }

    @Test fun availabilityMakesOneRequestPerCallForTheWeekWindowOfTheSelectedUnit() = runBlocking {
        val api = FakeApi().apply { slots = listOf(slot(2 * day)) }
        val controller = ready(api, unitA)
        controller.loadAvailability(cardiology.id, 0)
        assertEquals(1, api.availabilityRequests.size)
        val (unit, from, to) = api.availabilityRequests.single()
        val window = requireNotNull(bookingWeek(now, 0))
        assertEquals("unit-a", unit); assertEquals(window.fromMillis, from); assertEquals(window.toMillis, to)
        assertEquals(now, from) // a semana 0 começa agora: não há antecedência mínima
        assertEquals(AvailabilityResult("unit-a", cardiology.id, 0, api.slots), controller.state.availability)
        assertFalse(controller.state.availabilityLoading)
        controller.loadAvailability(cardiology.id, 1)
        assertEquals(2, api.availabilityRequests.size)
        assertEquals(requireNotNull(bookingWeek(now, 1)).fromMillis, api.availabilityRequests[1].second)
        assertEquals(1, controller.state.availability?.week)
        // Não há última semana: uma semana a quase 500 dias gera requisição; índice negativo não.
        controller.loadAvailability(cardiology.id, 70)
        assertEquals(3, api.availabilityRequests.size)
        assertEquals(requireNotNull(bookingWeek(now, 70)).fromMillis, api.availabilityRequests[2].second)
        controller.loadAvailability(cardiology.id, -1)
        assertEquals(3, api.availabilityRequests.size)
    }

    @Test fun onlyTheLatestAvailabilityReadWritesTheResult() = runBlocking {
        val api = FakeApi().apply { slots = listOf(slot(2 * day, "Antigo")) }
        val controller = ready(api, unitA)
        val gate = CompletableDeferred<Unit>()
        api.gateNextAvailability = gate
        val first = launch { controller.loadAvailability(cardiology.id, 0) }
        yield()
        api.slots = listOf(slot(9 * day, "Novo"))
        controller.loadAvailability(cardiology.id, 1)
        gate.complete(Unit)
        first.join()
        assertEquals(1, controller.state.availability?.week)
        assertEquals(listOf("Novo"), controller.state.availability?.slots?.map { it.professionalName })
        assertFalse(controller.state.availabilityLoading)
    }

    @Test fun availabilityFailureClearsTheOldSlotsAndSaysWhy() = runBlocking {
        val api = FakeApi().apply { slots = listOf(slot(2 * day)) }
        val controller = ready(api, unitA)
        controller.loadAvailability(cardiology.id, 0)
        api.availabilityFailure = failure(503, "service-unavailable", ApiContext.AVAILABILITY)
        controller.loadAvailability(cardiology.id, 0)
        assertNull(controller.state.availability)
        assertEquals(failureMessage(503, null, ApiContext.UNIT), controller.state.bookingError)
        assertFalse(controller.state.availabilityLoading)
        assertFalse(controller.state.busy)
    }

    @Test fun changingTheUnitDropsTheSlotsOfTheOldOne() = runBlocking {
        val api = FakeApi().apply { slots = listOf(slot(2 * day)) }
        val controller = ready(api, unitA, unitB)
        controller.selectUnit(unitA.id)
        controller.loadAvailability(cardiology.id, 0)
        assertNotNull(controller.state.availability)
        controller.selectUnit(unitB.id)
        assertNull(controller.state.availability)
    }

    // ---- Marcar ------------------------------------------------------------------------------------------------

    @Test fun bookingReloadsTheAppointmentsShowsTheNewOneAndRemovesTheSlot() = runBlocking {
        val chosen = slot(2 * day)
        val other = slot(2 * day + hour, "Beto", "prof-2")
        val created = appointment("novo", 2 * day, bookedBy = "patient")
        val api = FakeApi().apply { slots = listOf(chosen, other); bookResult = created; appointments = listOf(appointment("velha", -10 * day, "completed")) }
        val controller = ready(api, unitA)
        controller.loadAvailability(cardiology.id, 0)
        val callsBefore = api.appointmentCalls
        api.appointments = api.appointments + created
        controller.bookAppointment(chosen)
        assertEquals(listOf(listOf("unit-a", "prof-1", cardiology.id, chosen.startsAt)), api.bookRequests)
        assertEquals(callsBefore + 1, api.appointmentCalls)
        assertEquals(listOf("velha", "novo"), controller.state.appointments?.map { it.id })
        val booked = requireNotNull(controller.state.lastBooked)
        assertEquals("unit-a", booked.unitId); assertEquals("novo", booked.appointment.id); assertEquals("patient", booked.appointment.bookedBy)
        assertEquals(listOf(other), controller.state.availability?.slots)
        assertEquals("Consulta marcada. Ela já vale e aparece em Consultas.", controller.state.message)
        assertFalse(controller.state.busy)
    }

    @Test fun bookingStillShowsTheNewAppointmentWhenTheReloadFails() = runBlocking {
        val chosen = slot(2 * day)
        val created = appointment("novo", 2 * day, bookedBy = "patient")
        val api = FakeApi().apply { slots = listOf(chosen); bookResult = created; appointments = listOf(appointment("velha", -10 * day, "completed")) }
        val controller = ready(api, unitA)
        controller.loadAvailability(cardiology.id, 0)
        api.appointmentsFailure = IOException("offline")
        controller.bookAppointment(chosen)
        assertEquals(listOf("velha", "novo"), controller.state.appointments?.map { it.id })
        assertEquals("novo", controller.state.lastBooked?.appointment?.id)
        assertTrue(controller.state.message.orEmpty().contains("Não foi possível atualizar suas consultas"))
    }

    @Test fun onlyAShownSlotCanBeBooked() = runBlocking {
        val api = FakeApi().apply { slots = listOf(slot(2 * day)); bookResult = appointment("x", 2 * day) }
        val controller = ready(api, unitA)
        controller.bookAppointment(slot(2 * day)) // sem horários carregados
        controller.loadAvailability(cardiology.id, 0)
        controller.bookAppointment(slot(3 * day)) // horário que não está na lista
        assertTrue(api.bookRequests.isEmpty())
        assertNull(controller.state.lastBooked)
    }

    @Test fun eachRefusalHasItsOwnMessageAndOnlySlotUnavailableReloadsTheTimes() = runBlocking {
        val chosen = slot(2 * day)
        val api = FakeApi().apply { slots = listOf(chosen) }
        val controller = ready(api, unitA)
        controller.loadAvailability(cardiology.id, 0)
        val expected = mapOf(
            BOOKING_WINDOW to "Esse horário já começou ou passou. Escolha outro horário.",
            SPECIALTY_LIMIT to "Você já tem uma consulta futura marcada pelo app nesta especialidade, em alguma das suas UBS. " +
                "Cancele essa consulta ou aguarde a data dela para marcar outra.",
            PATIENT_OVERLAP to "Você já tem uma consulta neste mesmo horário. Escolha outro horário.")
        for ((code, text) in expected) {
            api.bookFailure = failure(409, code, ApiContext.BOOKING)
            val reads = api.availabilityRequests.size
            controller.bookAppointment(chosen)
            assertEquals(code, text, controller.state.message)
            assertEquals("$code não recarrega os horários", reads, api.availabilityRequests.size)
            assertEquals(listOf(chosen), controller.state.availability?.slots)
            assertNull(controller.state.lastBooked)
            assertFalse(controller.state.busy)
        }
        assertEquals(3, api.bookRequests.size)
        // Os quatro textos são diferentes entre si.
        val all = expected.values + failureMessage(409, SLOT_UNAVAILABLE, ApiContext.BOOKING)
        assertEquals(4, all.toSet().size)
    }

    @Test fun slotUnavailableReloadsTheTimesAndTheTakenSlotDisappears() = runBlocking {
        val taken = slot(2 * day)
        val free = slot(2 * day + hour, "Beto", "prof-2")
        val api = FakeApi().apply { slots = listOf(taken, free) }
        val controller = ready(api, unitA)
        controller.loadAvailability(cardiology.id, 0)
        api.slots = listOf(free) // outro paciente levou o primeiro horário
        val appointmentReads = api.appointmentCalls
        api.bookFailure = failure(409, SLOT_UNAVAILABLE, ApiContext.BOOKING)
        controller.bookAppointment(taken)
        assertEquals(2, api.availabilityRequests.size)
        assertEquals(api.availabilityRequests[0], api.availabilityRequests[1])
        assertEquals(listOf(free), controller.state.availability?.slots)
        assertEquals("a recusa não relê as consultas", appointmentReads, api.appointmentCalls)
        assertEquals(failureMessage(409, SLOT_UNAVAILABLE, ApiContext.BOOKING), controller.state.message)
        assertNull(controller.state.lastBooked)
        assertFalse(controller.state.busy)
    }

    @Test fun anExpiredSessionDuringBookingSignsOut() = runBlocking {
        val chosen = slot(2 * day)
        val api = FakeApi().apply { slots = listOf(chosen); bookFailure = failure(401, "unauthorized", ApiContext.BOOKING) }
        val controller = ready(api, unitA)
        controller.loadAvailability(cardiology.id, 0)
        controller.bookAppointment(chosen)
        assertNull(controller.state.session)
        assertNull(controller.state.availability)
        assertNull(controller.state.lastBooked)
    }

    @Test fun unavailableLinkedUnitMakesBookingAskToTryAgain() = runBlocking {
        val chosen = slot(2 * day)
        val api = FakeApi().apply { slots = listOf(chosen); bookFailure = failure(503, "service-unavailable", ApiContext.BOOKING) }
        val controller = ready(api, unitA)
        controller.loadAvailability(cardiology.id, 0)
        controller.bookAppointment(chosen)
        assertEquals("Não foi possível conferir suas consultas em todas as suas UBS agora. Tente marcar novamente em instantes.", controller.state.message)
        assertEquals(listOf(chosen), controller.state.availability?.slots)
    }

    // ---- Confirmação de presença ---------------------------------------------------------------------------

    @Test fun confirmingStoresTheServerAnswerAndRepeatingSendsNothing() = runBlocking {
        val api = FakeApi().apply { appointments = listOf(appointment("a1", day), appointment("a2", 5 * day)) }
        val controller = ready(api, unitA)
        controller.confirmPresence(unitA.id, "a1")
        assertEquals(1, api.confirmCalls)
        val confirmed = controller.state.appointments?.first { it.id == "a1" }
        assertEquals("2026-11-17T09:30:00Z", confirmed?.confirmedAt)
        assertNull(controller.state.appointments?.first { it.id == "a2" }?.confirmedAt)
        assertEquals("Presença confirmada às ${formatClock("2026-11-17T09:30:00Z")}.", controller.state.message)
        // Idempotente: a consulta já confirmada não gera nova requisição nem muda o instante.
        controller.confirmPresence(unitA.id, "a1")
        assertEquals(1, api.confirmCalls)
        assertEquals("2026-11-17T09:30:00Z", controller.state.appointments?.first { it.id == "a1" }?.confirmedAt)
        assertFalse(controller.state.busy)
    }

    @Test fun confirmationOfAnotherUnitUsesThatUnitNotTheSelectedOne() = runBlocking {
        val api = FakeApi().apply { appointments = listOf(appointment("a1", day)) }
        val seen = mutableListOf<String>()
        val spy = object : PatientApi by api {
            override suspend fun confirmPresence(token: String, unitId: String, id: String): PatientAppointment {
                seen += unitId; return api.confirmPresence(token, unitId, id)
            }
        }
        val controller = ready(api, unitA, unitB, via = spy)
        // As consultas da UBS A já estão lidas, mas a UBS selecionada é a B.
        controller.selectUnit(unitA.id)
        controller.loadAppointments()
        controller.selectUnit(unitB.id)
        controller.confirmPresence(unitA.id, "a1")
        assertEquals(listOf("unit-a"), seen)
        assertEquals("unit-b", controller.state.selectedUnitId)
        assertNotNull(controller.state.unitData[unitA.id]?.appointments?.single()?.confirmedAt)
    }

    @Test fun confirmationOutsideTheWindowShowsTheMessageAndReloadsTheList() = runBlocking {
        val api = FakeApi().apply { appointments = listOf(appointment("a1", 10 * day)) }
        val controller = ready(api, unitA)
        api.confirmFailure = failure(409, CONFIRMATION_WINDOW, ApiContext.CONFIRMATION)
        val before = api.appointmentCalls
        controller.confirmPresence(unitA.id, "a1")
        assertEquals("A presença só pode ser confirmada de 48 horas até 2 horas antes da consulta.", controller.state.message)
        assertEquals(before + 1, api.appointmentCalls)
        assertNull(controller.state.appointments?.single()?.confirmedAt)
        assertFalse(controller.state.busy)
    }

    @Test fun confirmationOfAChangedAppointmentExplainsAndReloads() = runBlocking {
        val api = FakeApi().apply { appointments = listOf(appointment("a1", day)) }
        val controller = ready(api, unitA)
        api.appointments = listOf(appointment("a1", day, "cancelled"))
        api.confirmFailure = failure(409, "conflict", ApiContext.CONFIRMATION)
        controller.confirmPresence(unitA.id, "a1")
        assertTrue(controller.state.message.orEmpty().startsWith("Esta consulta já foi alterada pela unidade"))
        assertEquals("cancelled", controller.state.appointments?.single()?.status)
        // Já cancelada na tela: nem tenta de novo.
        val calls = api.confirmCalls
        controller.confirmPresence(unitA.id, "a1")
        assertEquals(calls, api.confirmCalls)
    }

    @Test fun onlyScheduledAppointmentsOfTheLoadedListCanBeConfirmed() = runBlocking {
        val api = FakeApi().apply { appointments = listOf(appointment("called", day, "called"), appointment("done", -day, "completed")) }
        val controller = ready(api, unitA)
        controller.confirmPresence(unitA.id, "called")
        controller.confirmPresence(unitA.id, "done")
        controller.confirmPresence(unitA.id, "desconhecida")
        controller.confirmPresence("outra-ubs", "called")
        assertEquals(0, api.confirmCalls)
    }
}
