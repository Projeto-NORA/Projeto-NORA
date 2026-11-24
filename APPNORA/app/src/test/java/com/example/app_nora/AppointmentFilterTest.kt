package com.example.app_nora

import com.example.app_nora.api.PatientAppointment
import com.example.app_nora.ui.AppointmentStatus
import com.example.app_nora.ui.ListOrder
import com.example.app_nora.ui.filterAppointments
import org.junit.Assert.assertEquals
import org.junit.Test

class AppointmentFilterTest {
    private val completed = appointment("concluida", "Clínica geral", "Cláudia", "2026-11-21T14:00:00Z", "completed")
    private val cancelled = appointment("cancelada", "Clínica geral", "Simão", "2026-11-22T14:00:00Z", "cancelled")
    private val scheduled = appointment("agendada", "Cardiologia", "Cláudia", "2026-11-28T14:00:00Z", "scheduled")
    private val received = listOf(cancelled, completed, scheduled)

    @Test fun filtrosCombinamPesquisaSemAcentoESituacao() {
        assertEquals(listOf(completed), filterAppointments(received, "  CLINICA  ", AppointmentStatus.COMPLETED, ListOrder.ORIGINAL))
        assertEquals(listOf(completed, scheduled), filterAppointments(received, "claudia", AppointmentStatus.ALL, ListOrder.EARLIEST))
    }

    @Test fun ordemOriginalEPaginasSemCorrespondencia() {
        assertEquals(received, filterAppointments(received, "", AppointmentStatus.ALL, ListOrder.ORIGINAL))
        assertEquals(listOf(scheduled, cancelled, completed), filterAppointments(received, "", AppointmentStatus.ALL, ListOrder.LATEST))
        assertEquals(emptyList<PatientAppointment>(), filterAppointments(received, "", AppointmentStatus.MISSED, ListOrder.ORIGINAL))
    }

    private fun appointment(id: String, specialty: String, doctor: String, start: String, status: String) =
        PatientAppointment(id, specialty, doctor, start, start, status)
}
