package com.example.app_nora

import com.example.app_nora.api.*
import com.example.app_nora.ui.attendancePriorityText
import org.junit.Assert.*
import org.junit.Test

/** Score em pontos (0 a 1000), faixas e textos de resultado da remoção de UBS: tudo puro, sem Android. */
class AttendanceLogicTest {
    private fun unit(id: String) = PatientHealthUnit(id, "UBS $id", "Centro", "Rua", -23.0, -46.0)
    private fun attendance(completed: Int, missed: Int, cancelled: Int, weight: Double, score: Double?) =
        UnitAttendance(completed, missed, cancelled, weight, score, 12)
    private fun snapshot(a: UnitAttendance?, failed: Boolean = false) = UnitSnapshot(attendance = a, attendanceFailed = failed)

    @Test fun overallScoreSumsEveryUnitBeforeDividing() {
        val units = listOf(unit("a"), unit("b"))
        // A: 3 concluídas, 1 falta, peso 0,5  -> sozinha 3 / 4,5. B: 1 concluída, 0 faltas -> sozinha 1000.
        val data = mapOf("a" to snapshot(attendance(3, 1, 1, 0.5, 0.6667)), "b" to snapshot(attendance(1, 0, 0, 0.0, 1.0)))
        val summary = summarizeAttendance(units, data)
        // Média das médias daria 833; a soma dá 4 / 5,5 = 0,7273 -> 727 pontos, faixa Bom.
        assertEquals(727, summary.points)
        assertEquals(AttendanceBand.GOOD, summary.band)
        assertTrue(summary.hasHistory)
        assertEquals(4, summary.completed); assertEquals(1, summary.missed); assertEquals(1, summary.cancelledByPatient)
        assertEquals(0.5, summary.cancellationWeight, 1e-9)
        assertEquals(2, summary.answeredUnits); assertEquals(0, summary.failedUnits); assertEquals(0, summary.pendingUnits)
    }

    @Test fun fractionalWeightsAreSummedExactlyAndRoundedOnlyAtTheEnd() {
        // Duas UBS: 0,02 + 0,1 + 0,2 de peso e 1 concluída em uma, nada na outra: 1 / 1,32 = 0,7576 -> 758.
        val data = mapOf("a" to snapshot(attendance(1, 0, 3, 0.32, 0.7576)), "b" to snapshot(attendance(0, 0, 0, 0.0, null)))
        assertEquals(758, summarizeAttendance(listOf(unit("a"), unit("b")), data).points)
        assertEquals(0.75, attendanceFraction(3, 1, 0.0)!!, 1e-9)
    }

    @Test fun aSingleUnitKeepsTheServerPointsAndBand() {
        // O servidor manda os pontos: o app os usa como vieram, mesmo que o score arredonde diferente.
        val value = UnitAttendance(8, 1, 2, 0.5, 0.8421, 12, points = 842, band = AttendanceBand.EXCELLENT, hasHistory = true)
        val summary = summarizeAttendance(listOf(unit("a")), mapOf("a" to snapshot(value)))
        assertEquals(842, summary.points); assertEquals(AttendanceBand.EXCELLENT, summary.band); assertTrue(summary.hasHistory)
    }

    @Test fun noClosedAppointmentMeansMaximumPointsWithoutHistory() {
        val data = mapOf("a" to snapshot(attendance(0, 0, 0, 0.0, null)), "b" to snapshot(attendance(0, 0, 0, 0.0, null)))
        val summary = summarizeAttendance(listOf(unit("a"), unit("b")), data)
        assertEquals(1000, summary.points); assertEquals(AttendanceBand.EXCELLENT, summary.band); assertFalse(summary.hasHistory)
        assertEquals(2, summary.answeredUnits)
        assertNull(attendanceFraction(0, 0, 0.0))
        assertEquals(1000, scorePoints(null))
        assertEquals("1000 · sem consultas encerradas ainda", attendancePointsText(1000, false))
        assertEquals("840", attendancePointsText(840, true))
        val single = summarizeAttendance(listOf(unit("a")), mapOf("a" to snapshot(attendance(0, 0, 0, 0.0, null))))
        assertFalse(single.hasHistory); assertEquals(1000, single.points)
    }

    @Test fun onlyCancellationsWithWeightGiveZeroPointsNotTheMaximum() {
        // Cancelou com menos de 1 hora (peso 1) e nada mais: o divisor existe e o resultado é 0, faixa Baixo.
        val summary = summarizeAttendance(listOf(unit("a")), mapOf("a" to snapshot(attendance(0, 0, 1, 1.0, 0.0))))
        assertEquals(0, summary.points); assertEquals(AttendanceBand.LOW, summary.band); assertTrue(summary.hasHistory)
    }

    @Test fun aUnitThatFailedIsReportedAndNeverCountedAsZero() {
        val units = listOf(unit("a"), unit("b"), unit("c"))
        val data = mapOf("a" to snapshot(attendance(2, 0, 0, 0.0, 1.0)), "b" to snapshot(null, failed = true))
        val summary = summarizeAttendance(units, data)
        assertEquals(1000, summary.points); assertTrue(summary.hasHistory)
        assertEquals(1, summary.answeredUnits); assertEquals(1, summary.failedUnits); assertEquals(1, summary.pendingUnits)
        val allFailed = summarizeAttendance(listOf(unit("b")), mapOf("b" to snapshot(null, failed = true)))
        assertFalse(allFailed.hasHistory); assertEquals(0, allFailed.answeredUnits); assertEquals(1, allFailed.failedUnits)
    }

    @Test fun nullUnitsGiveNoHistory() {
        val summary = summarizeAttendance(null, emptyMap())
        assertFalse(summary.hasHistory); assertEquals(0, summary.answeredUnits)
    }

    @Test fun pointsRoundHalfUpAndStayBetweenZeroAndAThousand() {
        assertEquals(0, scorePoints(0.0))
        assertEquals(1000, scorePoints(1.0))
        assertEquals(500, scorePoints(0.5))
        assertEquals(63, scorePoints(0.0625)) // 62,5 exato: meio para cima
        assertEquals(0, scorePoints(0.00049))
        assertEquals(758, scorePoints(0.7576))
        assertEquals(1000, scorePoints(1.2)); assertEquals(0, scorePoints(-0.3)) // nunca fora da escala
    }

    @Test fun bandsChangeExactlyAtTheContractEdges() {
        assertEquals(AttendanceBand.LOW, attendanceBandOf(0))
        assertEquals(AttendanceBand.LOW, attendanceBandOf(299))
        assertEquals(AttendanceBand.REGULAR, attendanceBandOf(300))
        assertEquals(AttendanceBand.REGULAR, attendanceBandOf(599))
        assertEquals(AttendanceBand.GOOD, attendanceBandOf(600))
        assertEquals(AttendanceBand.GOOD, attendanceBandOf(799))
        assertEquals(AttendanceBand.EXCELLENT, attendanceBandOf(800))
        assertEquals(AttendanceBand.EXCELLENT, attendanceBandOf(1000))
        assertEquals(listOf("Baixo", "Regular", "Bom", "Excelente"), AttendanceBand.values().map { it.label })
        assertEquals(AttendanceBand.GOOD, attendanceBandFromWire("good")); assertNull(attendanceBandFromWire("ouro"))
    }

    @Test fun defaultsDeriveFromTheScoreForAnOlderServer() {
        val value = attendance(3, 1, 0, 0.0, 0.299)
        assertEquals(299, value.points); assertEquals(AttendanceBand.LOW, value.band); assertTrue(value.hasHistory)
        val none = attendance(0, 0, 0, 0.0, null)
        assertEquals(1000, none.points); assertEquals(AttendanceBand.EXCELLENT, none.band); assertFalse(none.hasHistory)
    }

    @Test fun priorityTextFollowsTheContractAndNamesTheDependent() {
        val own = attendancePriorityText(null)
        assertTrue(own, own.contains("Quem tem score maior recebe antes as ofertas de Vaga Relâmpago"))
        assertTrue(own, own.contains("empate segue a ordem de entrada na fila"))
        assertTrue(own, own.contains("O score não muda a prioridade clínica nem o direito ao atendimento."))
        assertTrue(attendancePriorityText("Maria Souza").contains("nem o direito de Maria Souza ao atendimento."))
        // Com mais de uma UBS o número em destaque é a soma; a ordem das ofertas usa o score de cada UBS, e o texto diz isso.
        assertFalse(own, own.contains("score de cada UBS"))
        val several = attendancePriorityText(null, severalUnits = true)
        assertTrue(several, several.contains("A ordem usa o score de cada UBS (veja o detalhe por UBS)"))
        assertTrue(several, several.contains("O score não muda a prioridade clínica nem o direito ao atendimento."))
    }

    @Test fun resultMessageUsesTheServerNumbersWithPlurals() {
        assertEquals("Você removeu Centro. Foram canceladas 1 consulta e 2 solicitações de fila.",
            unlinkResultMessage("Centro", UnlinkResult(1, 2)))
        assertEquals("Você removeu Centro. Foram canceladas 0 consultas e 1 solicitação de fila.",
            unlinkResultMessage("Centro", UnlinkResult(0, 1)))
    }
}
