package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.Assert.assertEquals
import org.junit.Test
import java.util.TimeZone

class UiLogicTest {
    private val utc = TimeZone.getTimeZone("UTC")

    @Test fun noticeKindClassifiesResults() {
        assertEquals(NoticeKind.SUCCESS, noticeKindOf("Consulta cancelada."))
        assertEquals(NoticeKind.SUCCESS, noticeKindOf("Consulta antecipada! Veja a nova data em Consultas."))
        assertEquals(NoticeKind.ERROR, noticeKindOf("CPF, e-mail ou senha inválidos."))
        assertEquals(NoticeKind.WARNING, noticeKindOf("Sua sessão terminou. Entre novamente."))
        assertEquals(NoticeKind.INFO, noticeKindOf("Oferta recusada. Sua consulta continua na data original."))
    }

    @Test fun alphabeticalIgnoresAccentsAndCase() {
        assertEquals(listOf("Ana", "Ávila", "bruno", "Carla"), listOf("Carla", "bruno", "Ávila", "Ana").sortedAlphabetically { it })
    }

    @Test fun cpfMaskAndInitials() {
        assertEquals("•••.456.789-••", maskCpf("123.456.789-09"))
        assertEquals("***.456.789-**", maskCpf("***.456.789-**"))
        assertEquals("AS", initialsOf("Ana Maria Souza"))
        assertEquals("J", initialsOf("joão"))
    }

    private fun slot(hourUtc: Int, minute: Int, pro: String, name: String) = AvailableSlot(
        "2026-12-01T%02d:%02d:00Z".format(hourUtc, minute), "2026-12-01T%02d:%02d:00Z".format(hourUtc, minute + 20), pro, name, "s", "Clínica")

    @Test fun groupsByProfessionalAlphabeticallyAndByShift() {
        val groups = groupByProfessional(listOf(slot(14, 0, "b", "Bruno"), slot(8, 0, "b", "Bruno"), slot(9, 0, "a", "Ana")), utc)
        assertEquals(listOf("Ana", "Bruno"), groups.map { it.professionalName })
        assertEquals(1, groups[1].morning.size); assertEquals(1, groups[1].afternoon.size)
    }

    @Test fun collapsedChipsShowSixAndCountTheRest() {
        val many = (0 until 9).map { slot(8 + it % 9, 0, "a", "Ana") }
        val day = groupByProfessional(many, utc).single()
        val (m, a, hidden) = visibleSlots(day, expanded = false)
        assertEquals(6, m.size + a.size); assertEquals(3, hidden)
        assertEquals(0, visibleSlots(day, expanded = true).third)
    }

    @Test fun photoCropIsCenteredSquareAndCapped() {
        assertEquals(Triple(200, 0, 600), squareCrop(1000, 600))
        assertEquals(512, photoSide(600)); assertEquals(300, photoSide(300))
    }

    @Test fun weekDaysCoverTheWeekWindow() {
        val week = BookingWeek(0, 1_764_590_400_000L /* 2025-12-01T12:00Z */, 1_764_590_400_000L + 6 * 86_400_000L + 1)
        assertEquals(7, weekDays(week, utc).size)
    }
}
