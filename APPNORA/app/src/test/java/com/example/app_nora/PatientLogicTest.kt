package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import java.util.TimeZone

class PatientLogicTest {
    private val originalZone = TimeZone.getDefault()
    @Before fun fixZone() { TimeZone.setDefault(TimeZone.getTimeZone("UTC")) }
    @After fun restoreZone() { TimeZone.setDefault(originalZone) }

    private val base = requireNotNull(parseInstantMillis("2026-11-11T12:00:00Z"))
    private fun iso(offsetSeconds: Long): String {
        val millis = base + offsetSeconds * 1000
        return java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", java.util.Locale.ROOT)
            .apply { timeZone = TimeZone.getTimeZone("UTC") }.format(millis)
    }
    private fun offer(id: String, expiresIn: Long, status: String = "pending", startsIn: Long = 3600) =
        UnitOffer(id, "Cardiologia", "Profissional", iso(startsIn), iso(startsIn + 1800), iso(expiresIn), status)
    private fun appointment(id: String, startsIn: Long, status: String = "scheduled") =
        PatientAppointment(id, "Clínica", "Profissional", iso(startsIn), iso(startsIn + 1800), status)
    private fun unit(id: String, name: String = "UBS $id") = PatientHealthUnit(id, name, "Centro", "Rua", -23.0, -46.0)

    @Test fun parsesOffsetsFractionsAndRejectsGarbage() {
        assertEquals(base, parseInstantMillis("2026-11-11T12:00:00Z"))
        assertEquals(base, parseInstantMillis("2026-11-11T09:00:00-03:00"))
        assertEquals(base, parseInstantMillis("2026-11-11T12:00:00.1234567+00:00"))
        assertNull(parseInstantMillis("11/11/2026"))
        assertNull(parseInstantMillis(""))
    }

    @Test fun formatsDateAndRangeInPortuguese() {
        assertEquals("11/11/2026 às 12:00", formatDateTime("2026-11-11T12:00:00Z"))
        assertEquals("11/11/2026, das 12:00 às 12:30", formatRange("2026-11-11T12:00:00Z", "2026-11-11T12:30:00Z"))
        assertEquals("11/11/2026 às 23:30 até 12/11/2026 às 00:10", formatRange("2026-11-11T23:30:00Z", "2026-11-12T00:10:00Z"))
        assertEquals("12:00", formatClock("2026-11-11T12:00:00Z"))
        assertEquals("texto", formatDateTime("texto"))
    }

    @Test fun remainingTimeRoundsUpAndNeverGoesNegative() {
        assertEquals("09:32", formatRemaining(9 * 60_000L + 31_100))
        assertEquals("00:01", formatRemaining(1))
        assertEquals("00:00", formatRemaining(-5_000))
        assertEquals("1:00:00", formatRemaining(3_600_000))
        assertEquals("menos de um minuto", describeRemaining(59_000))
        assertEquals("1 minuto", describeRemaining(60_000))
        assertEquals("9 minutos", describeRemaining(9 * 60_000L + 30_000))
    }

    @Test fun offerOpenAndExpiredAreRelativeToTheDeviceClock() {
        val pending = offer("a", expiresIn = 600)
        assertTrue(pending.isOpenAt(base))
        assertFalse(pending.isLocallyExpiredAt(base))
        assertFalse(pending.isOpenAt(base + 600_000))
        assertTrue(pending.isLocallyExpiredAt(base + 600_000))
        // Only pending offers expire locally; the server decides the rest.
        assertFalse(offer("b", 10, "accepted").isLocallyExpiredAt(base + 1_000_000))
        assertFalse(offer("c", 10, "declined").isOpenAt(base))
        // Unreadable deadline: stays open, the server decides.
        val unreadable = UnitOffer("d", "X", "Y", iso(0), iso(10), "???", "pending")
        assertTrue(unreadable.isOpenAt(base)); assertFalse(unreadable.isLocallyExpiredAt(base))
    }

    @Test fun pendingOffersComeFirstNearestDeadlineOnTopThenLocallyExpired() {
        val offers = listOf(
            offer("history-old", -7200, "declined"), offer("late", 500), offer("soon", 120),
            offer("history-new", -600, "accepted"), offer("gone", -30), offer("never", -9000, "expired"))
        val groups = groupOffers(offers, base)
        assertEquals(listOf("soon", "late", "gone"), groups.pending.map { it.id })
        assertEquals(listOf("history-new", "history-old", "never"), groups.history.map { it.id })
    }

    @Test fun queuePartitionKeepsActiveFirstOldestAndHistoryNewestFirst() {
        fun entry(id: Long, secondsAgo: Long, status: String) = UnitQueueEntry(id, "Esp", iso(-secondsAgo), status)
        val groups = groupQueue(listOf(entry(1, 100, "completed"), entry(2, 50, "waiting"), entry(3, 900, "offered"),
            entry(4, 10, "cancelled"), entry(5, 5000, "completed")))
        assertEquals(listOf(3L, 2L), groups.active.map { it.id })
        assertEquals(listOf(4L, 1L, 5L), groups.history.map { it.id })
    }

    @Test fun nextAppointmentIsTheNearestFutureScheduledOrCalledAcrossLoadedUnits() {
        val a = unit("a"); val b = unit("b"); val c = unit("c")
        val data = mapOf(
            "a" to UnitSnapshot(appointments = listOf(appointment("past", -600), appointment("far", 86_400),
                appointment("cancelled", 100, "cancelled"), appointment("done", 50, "completed"))),
            "b" to UnitSnapshot(appointments = listOf(appointment("near", 3_600, "called"), appointment("missed", 10, "missed"))),
            "c" to UnitSnapshot())
        val next = nextAppointment(listOf(a, b, c), data, base)
        assertEquals("near", next?.appointment?.id)
        assertEquals("b", next?.unit?.id)
        assertNull(nextAppointment(listOf(a), mapOf("a" to UnitSnapshot(appointments = listOf(appointment("past", -1)))), base))
        assertNull(nextAppointment(null, data, base))
        assertNull(nextAppointment(listOf(c), data, base))
    }

    @Test fun openOffersAcrossUnitsSkipsClosedAndExpiredAndKeepsUnitNames() {
        val a = unit("a"); val b = unit("b")
        val data = mapOf(
            "a" to UnitSnapshot(offers = listOf(offer("late", 500), offer("done", 100, "accepted"), offer("gone", -5))),
            "b" to UnitSnapshot(offers = listOf(offer("soon", 60))))
        val open = openOffersAcrossUnits(listOf(a, b), data, base)
        assertEquals(listOf("soon", "late"), open.map { it.offer.id })
        assertEquals(listOf("UBS b", "UBS a"), open.map { it.unit.name })
        assertTrue(offersLoadedForAll(listOf(a, b), data))
        assertFalse(offersLoadedForAll(listOf(a, unit("c")), data))
        assertFalse(offersLoadedForAll(null, data))
    }

    @Test fun notificationTrackerNotifiesOnceAndForgetsOffersThatLeave() {
        val tracker = NotifiedOffers()
        assertEquals(setOf("a", "b"), tracker.markFresh(listOf("a", "b")))
        assertEquals(setOf("c"), tracker.markFresh(listOf("a", "b", "c")))
        assertTrue(tracker.markFresh(listOf("a")).isEmpty())
        assertEquals(listOf("a", "b"), tracker.retain(setOf("c")))
        // A forgotten id would be announced again; ids that stayed are not.
        assertEquals(setOf("a"), tracker.markFresh(listOf("a", "c")))
        assertEquals(setOf("c", "a"), tracker.clear().toSet())
        assertTrue(tracker.clear().isEmpty())
    }

    @Test fun everyStatusHasATranslationAndRawValuesNeverLeak() {
        val all = listOf("scheduled", "called", "completed", "missed", "cancelled", "waiting", "offered", "pending",
            "accepted", "declined", "expired", "open", "closed")
        for (status in all) {
            assertNotEquals(status, statusLabel(status))
            assertNotEquals("Situação não reconhecida", statusLabel(status))
        }
        assertEquals("Situação não reconhecida", statusLabel("algo-novo"))
        assertEquals(all.size, all.map(::statusLabel).toSet().size)
    }
}
