package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.util.TimeZone

/** Regras puras do agendamento pelo catálogo e da confirmação de presença, sem Android. */
class BookingLogicTest {
    private val utc: TimeZone = TimeZone.getTimeZone("UTC")
    private val hour = 3_600_000L
    private val day = 24 * hour
    // Segunda-feira, 12:00 UTC. Não há janela de agendamento: qualquer horário futuro, em qualquer data, pode ser marcado.
    private val now = requireNotNull(parseInstantMillis("2026-11-16T12:00:00Z"))

    private fun slot(startMillis: Long, professional: String = "Ana", id: String = "p1") =
        AvailableSlot(formatInstantUtc(startMillis), formatInstantUtc(startMillis + 30 * 60_000), id, professional, "s1", "Cardiologia")

    private fun appointment(startMillis: Long, status: String = "scheduled", confirmedAt: String? = null) =
        PatientAppointment("a1", "Cardiologia", "Dra. Ana", formatInstantUtc(startMillis), formatInstantUtc(startMillis + 1_800_000), status, confirmedAt)

    // ---- Janela de agendamento ----------------------------------------------------------------------------

    @Test fun anyFutureTimeIsBookableAndOnlyThePastIsNot() {
        assertFalse(isBookable(now - 1, now))
        assertFalse(isBookable(now, now))
        assertTrue(isBookable(now + 1, now))
        assertTrue(isBookable(now + hour, now))
        assertTrue(isBookable(now + 24 * hour, now))
        assertTrue(isBookable(now + 90 * day + 1, now))
        assertTrue(isBookable(now + 500 * day, now))
    }

    @Test fun firstWeekStartsNowAndEndsAtTheNextWeekBoundary() {
        val first = requireNotNull(bookingWeek(now, 0, utc))
        assertEquals(now, first.fromMillis)
        // A semana 0 começa agora (segunda 16/11 12:00) e termina na meia-noite de 23/11: sete dias do calendário a partir de hoje.
        assertEquals(requireNotNull(parseInstantMillis("2026-11-23T00:00:00Z")), first.toMillis)
        val second = requireNotNull(bookingWeek(now, 1, utc))
        assertEquals(first.toMillis, second.fromMillis)
        assertEquals(second.fromMillis + 7 * day, second.toMillis)
        assertNull(bookingWeek(now, -1, utc))
    }

    @Test fun thereIsNoLastWeekAndWeeksNeverLeaveGapsOrOverlaps() {
        val far = requireNotNull(bookingWeek(now, 70, utc)) // quase 500 dias à frente
        assertEquals(requireNotNull(parseInstantMillis("2026-11-16T00:00:00Z")) + 490 * day, far.fromMillis)
        assertEquals(far.fromMillis + 7 * day, far.toMillis)
        var previousEnd = now
        for (index in 0 until 100) {
            val week = requireNotNull(bookingWeek(now, index, utc))
            assertEquals(previousEnd, week.fromMillis)
            assertTrue(week.toMillis - week.fromMillis <= 7 * day)
            previousEnd = week.toMillis
        }
    }

    @Test fun weekIndexForADateFindsTheWeekThatContainsIt() {
        assertEquals(0, bookingWeekIndexFor(now, now, utc))
        assertEquals(0, bookingWeekIndexFor(now - 5 * day, now, utc)) // dia que já passou: semana de hoje
        assertEquals(0, bookingWeekIndexFor(now + 6 * day, now, utc)) // 22/11
        assertEquals(1, bookingWeekIndexFor(now + 7 * day, now, utc)) // 23/11
        assertEquals(71, bookingWeekIndexFor(now + 500 * day, now, utc))
        // Para qualquer dia até quase dois anos à frente, a semana indicada contém o dia.
        for (offset in 0..700) {
            val date = now + offset * day
            val week = requireNotNull(bookingWeek(now, bookingWeekIndexFor(date, now, utc), utc))
            assertTrue("dia +$offset fora da semana ${week.index}", date >= week.fromMillis && date < week.toMillis)
        }
    }

    @Test fun pickerDatesBecomeTheSameDayOnTheDeviceCalendar() {
        val saoPaulo = TimeZone.getTimeZone("America/Sao_Paulo")
        val picked = requireNotNull(parseInstantMillis("2026-12-25T00:00:00Z")) // o seletor devolve a meia-noite UTC do dia
        assertEquals(picked, localDayFromPicker(picked, utc))
        // Meia-noite local de 25/12 em São Paulo (UTC-3) é 03:00 UTC.
        assertEquals(requireNotNull(parseInstantMillis("2026-12-25T03:00:00Z")), localDayFromPicker(picked, saoPaulo))
        assertTrue(isDayBookable(localDayFromPicker(requireNotNull(parseInstantMillis("2026-11-16T00:00:00Z")), utc), now, utc)) // hoje
        assertFalse(isDayBookable(localDayFromPicker(requireNotNull(parseInstantMillis("2026-11-15T00:00:00Z")), utc), now, utc)) // ontem
    }

    @Test fun weeksFollowTheDeviceCalendarDay() {
        val saoPaulo = TimeZone.getTimeZone("America/Sao_Paulo")
        val first = requireNotNull(bookingWeek(now, 0, saoPaulo))
        assertEquals(now, first.fromMillis)
        // 16/11 09:00 em São Paulo; a semana termina na meia-noite local de 23/11 (03:00 UTC).
        assertEquals(requireNotNull(parseInstantMillis("2026-11-23T03:00:00Z")), first.toMillis)
    }

    // ---- Horários por dia -----------------------------------------------------------------------------------

    @Test fun slotsAreGroupedByDayAndOrderedByTimeThenProfessional() {
        val tuesday = now + 24 * hour // 17/11 12:00 UTC
        val slots = listOf(
            slot(tuesday + 3 * hour, "Zé", "p2"), slot(tuesday + hour, "Beto", "p3"), slot(tuesday + hour, "Ana"),
            slot(tuesday + day + hour, "Ana"), slot(tuesday + 3 * day, "Ana"))
        val days = groupSlotsByDay(slots, now, utc)
        assertEquals(listOf(3, 1, 1), days.map { it.slots.size })
        assertTrue(days.zipWithNext().all { (a, b) -> a.dayStart < b.dayStart })
        // Dentro do dia: início primeiro; Ana e Beto empatam no início e saem por nome.
        assertEquals(listOf("Ana", "Beto", "Zé"), days[0].slots.map { it.professionalName })
        assertEquals(listOf(tuesday + hour, tuesday + hour, tuesday + 3 * hour), days[0].slots.map { it.startsAtMillis })
    }

    @Test fun slotsThatAlreadyStartedOrAreUnreadableAreNeverOffered() {
        val soon = slot(now + 60_000) // faltando um minuto: vale, não há antecedência mínima
        val far = slot(now + 500 * day) // quase 500 dias: vale, não há antecedência máxima
        val started = slot(now - 60_000)
        val startsNow = slot(now)
        val broken = AvailableSlot("sem-data", "sem-data", "p1", "Dra. Ana", "s1", "Cardiologia")
        val days = groupSlotsByDay(listOf(far, broken, started, startsNow, soon), now, utc)
        assertEquals(listOf(soon, far), days.flatMap { it.slots })
        assertTrue(groupSlotsByDay(emptyList(), now, utc).isEmpty())
    }

    @Test fun lateEveningSlotBelongsToItsOwnLocalDay() {
        val saoPaulo = TimeZone.getTimeZone("America/Sao_Paulo")
        // 2026-11-18 01:30 UTC ainda é 17/11 22:30 em São Paulo; 03:00 UTC já é 18/11 00:00.
        val late = slot(requireNotNull(parseInstantMillis("2026-11-18T01:30:00Z")))
        val midnight = slot(requireNotNull(parseInstantMillis("2026-11-18T03:00:00Z")))
        assertEquals(2, groupSlotsByDay(listOf(late, midnight), now, saoPaulo).size)
        assertEquals(1, groupSlotsByDay(listOf(late, midnight), now, utc).size)
    }

    // ---- Confirmação de presença --------------------------------------------------------------------------

    @Test fun presenceWindowBordersAreIncluded() {
        val start = now + 10 * day
        val a = appointment(start)
        assertEquals(PresenceState.NOT_YET, presenceState(a, start - 48 * hour - 1))
        assertEquals(PresenceState.OPEN, presenceState(a, start - 48 * hour))
        assertEquals(PresenceState.OPEN, presenceState(a, start - 24 * hour))
        assertEquals(PresenceState.OPEN, presenceState(a, start - 2 * hour))
        assertEquals(PresenceState.CLOSED, presenceState(a, start - 2 * hour + 1))
        assertEquals(PresenceState.CLOSED, presenceState(a, start + hour))
        assertEquals(start - 48 * hour, presenceOpensAt(a))
        assertEquals(start - 2 * hour, presenceClosesAt(a))
    }

    @Test fun presenceStateDependsOnTheAppointmentSituation() {
        val start = now + day
        assertEquals(PresenceState.CONFIRMED, presenceState(appointment(start, confirmedAt = "2026-11-16T10:00:00Z"), now))
        // Depois de confirmada continua confirmada mesmo fora da janela.
        assertEquals(PresenceState.CONFIRMED, presenceState(appointment(start, confirmedAt = "2026-11-16T10:00:00Z"), start + day))
        for (status in listOf("called", "completed", "missed", "cancelled"))
            assertEquals(PresenceState.NOT_APPLICABLE, presenceState(appointment(start, status), now))
        val unreadable = PatientAppointment("a", "x", "y", "data-ruim", "data-ruim", "scheduled")
        assertEquals(PresenceState.NOT_APPLICABLE, presenceState(unreadable, now))
    }

    @Test fun nextBoundaryIsTheOpeningThenTheClosingOfTheNearestWindow() {
        val start = now + 10 * day
        val a = appointment(start)
        assertEquals(start - 48 * hour, nextPresenceBoundary(listOf(a), now))
        assertEquals(start - 2 * hour + 1, nextPresenceBoundary(listOf(a), start - 48 * hour))
        assertEquals(start - 2 * hour + 1, nextPresenceBoundary(listOf(a), start - 3 * hour))
        assertNull(nextPresenceBoundary(listOf(a), start - 2 * hour + 1))
        val sooner = appointment(now + 5 * day).copy(id = "a2")
        assertEquals(now + 3 * day, nextPresenceBoundary(listOf(a, sooner), now))
        // Confirmada, cancelada ou de data ilegível não acordam o relógio.
        assertNull(nextPresenceBoundary(listOf(appointment(start, confirmedAt = "2026-11-16T10:00:00Z"), appointment(start, "cancelled")), now))
        assertNull(nextPresenceBoundary(emptyList(), now))
    }

    @Test fun confirmedTextShowsTheClockTimeOnly() {
        val confirmed = appointment(now + day, confirmedAt = "2026-11-16T09:41:07Z")
        val text = presenceConfirmedText(confirmed)
        assertTrue(text, text.startsWith("Presença confirmada às "))
        assertEquals("Presença confirmada às ${formatClock("2026-11-16T09:41:07Z")}", text)
        assertEquals("Presença confirmada", presenceConfirmedText(appointment(now + day)))
    }

    @Test fun bookedByIsAlwaysTranslated() {
        // Sem `bookedByYou` nem nome de responsável (servidor antigo), o texto do aplicativo é neutro: não afirma quem foi.
        assertEquals("Marcada pelo aplicativo", bookedByLabel("patient"))
        assertEquals("Marcada pelo aplicativo", bookedByLabel("patient", guardianName = "  "))
        assertEquals("Marcada pela equipe da UBS", bookedByLabel("staff"))
        assertEquals("Marcada ao aceitar uma Vaga Relâmpago", bookedByLabel("lightning"))
        assertNull(bookedByLabel("outro"))
    }

    @Test fun bookedByNamesWhoBookedWhenTheServerSays() {
        assertEquals("Marcada por você", bookedByLabel("patient", byYou = true))
        assertEquals("quem marcou é quem vê: vale mais que o nome", "Marcada por você", bookedByLabel("patient", byYou = true, guardianName = "Ana Lima"))
        assertEquals("Marcada por Ana Lima", bookedByLabel("patient", guardianName = "Ana Lima"))
        assertEquals("Marcada por Ana Lima", bookedByLabel("lightning", guardianName = "Ana Lima"))
    }

    // ---- Textos do agendamento quando se age por um dependente ------------------------------------------------------

    @Test fun bookingTextsSpeakAboutTheDependentWhenActingForOne() {
        assertTrue(bookingLimitRuleText(null).contains("todas as suas UBS"))
        assertTrue(bookingCancelWarningText(null).contains("no seu score de comparecimento"))
        val limit = bookingLimitRuleText("Maria Souza")
        assertTrue(limit, limit.contains("somando as UBS de Maria Souza"))
        assertFalse(limit, limit.contains("suas"))
        val cancel = bookingCancelWarningText("Maria Souza")
        assertTrue(cancel, cancel.contains("no score de comparecimento de Maria Souza"))
        assertFalse(cancel, cancel.contains("seu "))
    }

    // ---- Relógio da confirmação de presença -------------------------------------------------------------------------

    /** Roda o relógio com o aparelho "dormindo": cada espera avança o relógio pelo próximo valor de [jumps] (depois, 100 dias). */
    private fun runClock(appointments: List<PatientAppointment>, jumps: List<Long>): Pair<List<Long>, List<Long>> = runBlocking {
        var clockNow = now
        val waits = mutableListOf<Long>()
        val seen = mutableListOf<Long>()
        trackPresenceClock(appointments, now, { clockNow }, { seen += it }) { millis ->
            clockNow += jumps.getOrNull(waits.size) ?: (100 * day)
            waits += millis
        }
        waits to seen
    }

    @Test fun thePresenceClockNeverWaitsLongerThanTheCapSoAFrozenClockCatchesUpAfterDeepSleep() {
        // Consulta daqui a 49 h: a janela abre em 1 h. Na primeira espera o aparelho dorme 3 h (o relógio de corrotina não conta o sono).
        val (waits, seen) = runClock(listOf(appointment(now + 49 * hour)), listOf(3 * hour))
        assertEquals("nenhuma espera passa do teto de 60 s", PRESENCE_CLOCK_MAX_WAIT_MS, waits.first())
        assertTrue(waits.all { it <= PRESENCE_CLOCK_MAX_WAIT_MS })
        assertEquals("a abertura da janela é percebida logo depois do sono", now + 3 * hour, seen.first())
        assertTrue("a próxima fronteira (fechamento) também é percebida", seen.size == 2)
    }

    @Test fun thePresenceClockOnlyReportsWhenABoundaryWasCrossed() {
        // A janela abre em 150 s: três esperas (60 s, 60 s, 30 s) e uma só mudança de relógio.
        val (waits, seen) = runClock(listOf(appointment(now + 48 * hour + 150_000)), listOf(60_000, 60_000, 30_000))
        assertEquals(listOf(60_000L, 60_000L, 30_000L), waits.take(3))
        assertEquals(now + 150_000, seen.first())
    }

    @Test fun thePresenceClockStopsWhenNoAppointmentCanChange() {
        val (waits, seen) = runClock(listOf(appointment(now + day, confirmedAt = "2026-11-16T10:00:00Z")), emptyList())
        assertTrue(waits.isEmpty()); assertTrue(seen.isEmpty())
    }

    // ---- Toque na notificação de Vaga Relâmpago ----------------------------------------------------------------------

    private val ownUnits = listOf(PatientHealthUnit("unit-a", "UBS A", "", "", 0.0, 0.0), PatientHealthUnit("unit-b", "UBS B", "", "", 0.0, 0.0))
    private val session = PatientSession("t", "2099-01-01T00:00:00Z", PatientUser("p", "Paciente"))
    private fun flashState(busy: Boolean = false, acting: String? = null, selected: String? = "unit-a", withSession: Boolean = true,
        units: List<PatientHealthUnit>? = ownUnits, links: List<FamilyLink>? = listOf(
            FamilyLink("dep-1", "approved", "unit-b", "UBS B", "***", "Maria", "2026-11-10T12:00:00Z", null, null, null))) =
        PatientState(session = session.takeIf { withSession }, units = units, selectedUnitId = selected, busy = busy, actingFor = acting,
            family = FamilyState(links = links))

    @Test fun aFlashNotificationWaitsWhileTheSwitchOfWhoIsActingWouldBeRefused() {
        // Ocupado: a troca do "agindo por" seria recusada, então o pedido não pode ser consumido.
        assertEquals(FlashOpen.WAIT, flashOpenDecision("unit-b", "dep-1", flashState(busy = true)))
        assertEquals(FlashOpen.OPEN_DEPENDENT, flashOpenDecision("unit-b", "dep-1", flashState()))
        assertEquals(FlashOpen.WAIT, flashOpenDecision("unit-a", null, flashState(busy = true, acting = "dep-1")))
        assertEquals(FlashOpen.OPEN_OWN, flashOpenDecision("unit-a", null, flashState(acting = "dep-1")))
        // Ocupado, mas sem nada a trocar: abre.
        assertEquals(FlashOpen.OPEN_DEPENDENT, flashOpenDecision("unit-b", "dep-1", flashState(busy = true, acting = "dep-1")))
        assertEquals(FlashOpen.OPEN_OWN, flashOpenDecision("unit-a", null, flashState(busy = true)))
        // A UBS selecionada é outra: precisa trocar de UBS, o que também é recusado com `busy`.
        assertEquals(FlashOpen.WAIT, flashOpenDecision("unit-b", null, flashState(busy = true)))
        assertEquals(FlashOpen.OPEN_OWN, flashOpenDecision("unit-b", null, flashState()))
    }

    @Test fun aFlashNotificationWaitsForTheDataItNeedsAndIsDroppedWhenItCannotBeServed() {
        assertEquals(FlashOpen.DROP, flashOpenDecision("unit-a", null, flashState(withSession = false)))
        assertEquals(FlashOpen.WAIT, flashOpenDecision("unit-a", null, flashState(units = null)))
        assertEquals(FlashOpen.WAIT, flashOpenDecision("unit-b", "dep-1", flashState(links = null)))
        assertEquals(FlashOpen.DROP, flashOpenDecision("unit-z", null, flashState()))
        assertEquals(FlashOpen.DROP, flashOpenDecision("unit-b", "desconhecido", flashState()))
    }
}
