package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.Assert.*
import org.junit.Test

/** Score geral, contagem do que a remoção de UBS cancela e textos de resultado: tudo puro, sem Android. */
class AttendanceLogicTest {
    private val now = requireNotNull(parseInstantMillis("2026-11-11T12:00:00Z"))
    private fun iso(offsetSeconds: Long): String =
        java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", java.util.Locale.ROOT)
            .apply { timeZone = java.util.TimeZone.getTimeZone("UTC") }.format(now + offsetSeconds * 1000)
    private fun unit(id: String) = PatientHealthUnit(id, "UBS $id", "Centro", "Rua", -23.0, -46.0)
    private fun attendance(completed: Int, missed: Int, cancelled: Int, weight: Double, score: Double?) =
        UnitAttendance(completed, missed, cancelled, weight, score, 12)
    private fun snapshot(a: UnitAttendance?, failed: Boolean = false) = UnitSnapshot(attendance = a, attendanceFailed = failed)

    @Test fun overallScoreSumsEveryUnitBeforeDividing() {
        val units = listOf(unit("a"), unit("b"))
        // A: 3 concluídas, 1 falta, peso 0,5  -> sozinha 3 / 4,5. B: 1 concluída, 0 faltas -> sozinha 100%.
        val data = mapOf("a" to snapshot(attendance(3, 1, 1, 0.5, 0.6667)), "b" to snapshot(attendance(1, 0, 0, 0.0, 1.0)))
        val summary = summarizeAttendance(units, data)
        // Média das médias daria 83%; a soma dá 4 / 5,5 = 72,7%.
        assertEquals(73, summary.percent)
        assertEquals(4, summary.completed); assertEquals(1, summary.missed); assertEquals(1, summary.cancelledByPatient)
        assertEquals(0.5, summary.cancellationWeight, 1e-9)
        assertEquals(2, summary.answeredUnits); assertEquals(0, summary.failedUnits); assertEquals(0, summary.pendingUnits)
    }

    @Test fun fractionalWeightsAreSummedExactlyAndRoundedOnlyAtTheEnd() {
        // 0,02 + 0,1 + 0,2 de peso e 1 concluída: 1 / 1,32 = 75,76% -> 76.
        val data = mapOf("a" to snapshot(attendance(1, 0, 3, 0.32, 0.7576)))
        assertEquals(76, summarizeAttendance(listOf(unit("a")), data).percent)
        assertEquals(0.75, attendanceFraction(3, 1, 0.0)!!, 1e-9)
    }

    @Test fun noClosedAppointmentMeansNoNumber() {
        val data = mapOf("a" to snapshot(attendance(0, 0, 0, 0.0, null)), "b" to snapshot(attendance(0, 0, 0, 0.0, null)))
        val summary = summarizeAttendance(listOf(unit("a"), unit("b")), data)
        assertNull(summary.percent)
        assertEquals(2, summary.answeredUnits)
        assertNull(attendanceFraction(0, 0, 0.0))
        assertNull(attendancePercent(null))
    }

    @Test fun onlyCancellationsWithWeightGiveZeroNotNull() {
        // Cancelou com menos de 1 hora (peso 1) e nada mais: o divisor existe e o resultado é 0%.
        assertEquals(0, summarizeAttendance(listOf(unit("a")), mapOf("a" to snapshot(attendance(0, 0, 1, 1.0, 0.0)))).percent)
    }

    @Test fun aUnitThatFailedIsReportedAndNeverCountedAsZero() {
        val units = listOf(unit("a"), unit("b"), unit("c"))
        val data = mapOf("a" to snapshot(attendance(2, 0, 0, 0.0, 1.0)), "b" to snapshot(null, failed = true))
        val summary = summarizeAttendance(units, data)
        assertEquals(100, summary.percent)
        assertEquals(1, summary.answeredUnits); assertEquals(1, summary.failedUnits); assertEquals(1, summary.pendingUnits)
        val allFailed = summarizeAttendance(listOf(unit("b")), mapOf("b" to snapshot(null, failed = true)))
        assertNull(allFailed.percent); assertEquals(0, allFailed.answeredUnits); assertEquals(1, allFailed.failedUnits)
    }

    @Test fun nullUnitsAndRoundingNeverOverstate() {
        assertNull(summarizeAttendance(null, emptyMap()).percent)
        assertEquals(99, attendancePercent(0.996))
        assertEquals(100, attendancePercent(1.0))
        assertEquals(1, attendancePercent(0.001))
        assertEquals(0, attendancePercent(0.0))
        assertEquals(50, attendancePercent(0.5))
    }

    @Test fun unlinkImpactCountsOnlyFutureOpenAppointmentsAndActiveQueue() {
        val snapshot = UnitSnapshot(
            queue = listOf(UnitQueueEntry(1, "A", iso(-100), "waiting"), UnitQueueEntry(2, "B", iso(-100), "offered"),
                UnitQueueEntry(3, "C", iso(-100), "completed"), UnitQueueEntry(4, "D", iso(-100), "cancelled")),
            appointments = listOf(
                PatientAppointment("f1", "x", "p", iso(3600), iso(5400), "scheduled"),
                PatientAppointment("f2", "x", "p", iso(7200), iso(9000), "called"),
                PatientAppointment("past", "x", "p", iso(-3600), iso(-1800), "scheduled"),
                PatientAppointment("done", "x", "p", iso(-86_400), iso(-85_000), "completed"),
                PatientAppointment("cancelled", "x", "p", iso(3600), iso(5400), "cancelled"),
                PatientAppointment("bad", "x", "p", "lixo", "lixo", "scheduled")))
        assertEquals(UnlinkImpact(2, 2), unlinkImpact(snapshot, now))
        assertEquals(UnlinkImpact(0, 0), unlinkImpact(UnitSnapshot(queue = emptyList(), appointments = emptyList()), now))
    }

    @Test fun unlinkImpactIsUnknownUntilBothListsWereRead() {
        assertNull(unlinkImpact(null, now))
        assertNull(unlinkImpact(UnitSnapshot(queue = emptyList()), now))
        assertNull(unlinkImpact(UnitSnapshot(appointments = emptyList()), now))
    }

    @Test fun resultMessageUsesTheServerNumbersWithPlurals() {
        assertEquals("Você removeu Centro. Foram canceladas 1 consulta e 2 solicitações de fila.",
            unlinkResultMessage("Centro", UnlinkResult(1, 2)))
        assertEquals("Você removeu Centro. Foram canceladas 0 consultas e 1 solicitação de fila.",
            unlinkResultMessage("Centro", UnlinkResult(0, 1)))
    }
}
