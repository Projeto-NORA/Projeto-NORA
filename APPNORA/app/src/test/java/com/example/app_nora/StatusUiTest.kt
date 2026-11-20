package com.example.app_nora

import com.example.app_nora.api.ApiContext
import com.example.app_nora.api.INCOMPATIBLE_RESPONSE_MESSAGE
import com.example.app_nora.api.PatientAppointment
import com.example.app_nora.api.SPECIALTY_LIMIT
import com.example.app_nora.api.failureMessage
import com.example.app_nora.api.formatInstantUtc
import com.example.app_nora.api.unitReference
import com.example.app_nora.ui.MessageKind
import com.example.app_nora.ui.StatusTone
import com.example.app_nora.ui.actionMessageKind
import com.example.app_nora.ui.splitAppointments
import com.example.app_nora.ui.statusStyle
import org.junit.Assert.assertEquals
import org.junit.Test

class StatusUiTest {
    @Test fun resultMessagesAreClassifiedByTheirOutcome() {
        assertEquals(MessageKind.Success, actionMessageKind("Consulta marcada. Ela já vale e aparece em Consultas."))
        assertEquals(MessageKind.Success, actionMessageKind("Senha alterada. Os outros aparelhos foram desconectados; você continua conectado neste."))
        assertEquals(MessageKind.Success, actionMessageKind("O vínculo com Maria foi encerrado."))
        assertEquals(MessageKind.Warning, actionMessageKind("Consulta cancelada. Não foi possível atualizar a tela; use Atualizar."))
        assertEquals(MessageKind.Error, actionMessageKind("Não foi possível conectar ao serviço. Confira o endereço e sua conexão e tente novamente."))
        assertEquals(MessageKind.Error, actionMessageKind("Código incorreto, vencido ou já usado. Confira os dígitos ou peça outro código."))
        assertEquals(MessageKind.Error, actionMessageKind("A presença só pode ser confirmada de 48 horas até 2 horas antes da consulta."))
        assertEquals(MessageKind.Error, actionMessageKind(INCOMPATIBLE_RESPONSE_MESSAGE))
        assertEquals(MessageKind.Error, actionMessageKind("Você chegou ao limite de 20 pedidos e vínculos ativos. Encerre algum antes de pedir outro."))
        assertEquals(MessageKind.Error, actionMessageKind(failureMessage(409, SPECIALTY_LIMIT, ApiContext.BOOKING)))
        assertEquals(MessageKind.Info, actionMessageKind("Agora você está vendo as suas UBS"))
    }

    @Test fun everyServerStatusHasOneToneAndMark() {
        assertEquals(StatusTone.Info to null, statusStyle("scheduled"))
        assertEquals(StatusTone.Warning to null, statusStyle("waiting"))
        assertEquals(StatusTone.Success to null, statusStyle("completed"))
        assertEquals(StatusTone.Neutral to "✕", statusStyle("cancelled"))
        assertEquals(StatusTone.Error to "!", statusStyle("missed"))
    }

    @Test fun unitNamesGetOneArticleAndNeverRepeatUbs() {
        assertEquals("a UBS Centro", unitReference("UBS Centro"))
        assertEquals("a UBS Centro", unitReference("  Centro "))
        assertEquals("a UBS", unitReference(""))
        assertEquals("a UBS", unitReference(null))
    }

    @Test fun appointmentsAreSplitKeepingTheOneInProgressUpcoming() {
        val now = 10_000_000_000L
        fun item(id: String, start: Long, end: Long) = PatientAppointment(id, "x", "y", formatInstantUtc(start), formatInstantUtc(end), "scheduled")
        val past = item("past", now - 7_200_000, now - 5_400_000)
        val running = item("running", now - 300_000, now + 1_500_000)
        val later = item("later", now + 3_600_000, now + 5_400_000)
        val soon = item("soon", now + 1_800_000, now + 3_000_000)
        val older = item("older", now - 86_400_000, now - 84_600_000)
        val unreadable = PatientAppointment("bad", "x", "y", "data-ruim", "data-ruim", "scheduled")
        val (upcoming, previous) = splitAppointments(listOf(later, past, unreadable, soon, older, running), now)
        assertEquals(listOf("running", "soon", "later"), upcoming.map { it.id })
        assertEquals(listOf("past", "older", "bad"), previous.map { it.id })
    }

    @Test fun cancelledOrDeclinedFutureAppointmentsGoToPrevious() {
        val now = 10_000_000_000L
        fun item(id: String, status: String) = PatientAppointment(id, "x", "y", formatInstantUtc(now + 3_600_000), formatInstantUtc(now + 5_400_000), status)
        val (upcoming, previous) = splitAppointments(listOf(item("ok", "scheduled"), item("c", "cancelled"), item("d", "declined")), now)
        assertEquals(listOf("ok"), upcoming.map { it.id })
        assertEquals(setOf("c", "d"), previous.map { it.id }.toSet())
    }

    @Test fun completedOrMissedFutureAppointmentsGoToPrevious() {
        val now = 10_000_000_000L
        fun item(id: String, status: String) = PatientAppointment(id, "x", "y", formatInstantUtc(now + 3_600_000), formatInstantUtc(now + 5_400_000), status)
        val (upcoming, previous) = splitAppointments(listOf(item("ok", "called"), item("c", "completed"), item("m", "missed")), now)
        assertEquals(listOf("ok"), upcoming.map { it.id })
        assertEquals(setOf("c", "m"), previous.map { it.id }.toSet())
    }
}
