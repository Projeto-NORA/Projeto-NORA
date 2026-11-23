package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun AppointmentsScreen(patient: PatientState, onSelectUnit: (String) -> Unit, onLoadUnits: () -> Unit,
    onLoadAppointments: () -> Unit, onCancelAppointment: (String) -> Unit,
    onConfirmPresence: (String, String) -> Unit = { _, _ -> }, onBook: () -> Unit = {}) {
    var cancelPending by remember(patient.selectedUnitId, patient.session?.token) { mutableStateOf<PatientAppointment?>(null) }
    // A janela de confirmação muda de estado sozinha: o relógio acorda só nesses instantes.
    val presenceNow = rememberPresenceClock(patient.appointments.orEmpty())
    ScreenTitle("Consultas", "Veja seus atendimentos futuros e anteriores.")
    SectionCard("Sua agenda", "Informações da unidade de saúde") {
        if (patient.session == null) MutedText("Entre na sua conta para consultar sua agenda.")
        else {
            MutedText("Selecione uma UBS vinculada. São exibidas consultas de até 180 dias antes e depois de hoje.")
            patient.units?.forEach { unit ->
                Row(Modifier.fillMaxWidth().selectable(selected = unit.id == patient.selectedUnitId, enabled = !patient.busy, onClick = { onSelectUnit(unit.id) }, role = Role.RadioButton), verticalAlignment = Alignment.CenterVertically) {
                    RadioButton(selected = unit.id == patient.selectedUnitId, onClick = null)
                    Text(unit.name, Modifier.padding(start = 8.dp))
                }
            }
            if (patient.units?.isEmpty() == true) MutedText("Nenhuma UBS vinculada ao seu cadastro.")
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                OutlinedButton(onClick = onLoadUnits, enabled = !patient.busy, contentPadding = PaddingValues(horizontal = 12.dp)) { Text("Atualizar unidades") }
                if (patient.selectedUnitId != null)
                    OutlinedButton(onClick = onLoadAppointments, enabled = !patient.busy, contentPadding = PaddingValues(horizontal = 12.dp)) { Text("Atualizar consultas") }
            }
            if (patient.selectedUnitId != null) {
                Button(onClick = onBook, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Agendar nova consulta") }
                if (patient.appointments?.isEmpty() == true) MutedText("Nenhuma consulta encontrada neste período.")
                patient.appointments?.forEach { appointment ->
                    key(appointment.id) {
                        HorizontalDivider(Modifier.padding(vertical = 8.dp))
                        Text(appointment.specialty, fontWeight = FontWeight.SemiBold)
                        Text("Profissional: ${appointment.professional}")
                        Text(formatRange(appointment.startsAt, appointment.endsAt))
                        Text(statusLabel(appointment.status))
                        bookedByLabel(appointment.bookedBy, appointment.bookedByYou, appointment.bookedByGuardianName)?.let { MutedText(it) }
                        PresenceConfirmation(appointment, presenceNow.value, patient.busy) {
                            onConfirmPresence(patient.selectedUnitId, appointment.id)
                        }
                        if (appointment.status in listOf("scheduled", "called"))
                            TextButton(onClick = { cancelPending = appointment }, enabled = !patient.busy) { Text("Cancelar consulta") }
                    }
                }
            }
        }
    }
    ActionMessage(patient.message)
    MutedText("Para remarcar, procure a equipe da UBS.")
    cancelPending?.let { appointment ->
        AlertDialog(onDismissRequest = { cancelPending = null }, title = { Text("Cancelar consulta?") },
            text = { Text("Confirma o cancelamento de ${appointment.specialty} em ${formatDateTime(appointment.startsAt)}?") },
            confirmButton = { TextButton(onClick = { cancelPending = null; onCancelAppointment(appointment.id) }) { Text("Confirmar cancelamento") } },
            dismissButton = { TextButton(onClick = { cancelPending = null }) { Text("Voltar") } })
    }
}

/** Texto do cartão "Sua agenda" do Início quando não há próxima consulta, sem inventar dado. */
internal fun noNextAppointmentText(patient: PatientState): String = when {
    patient.units == null -> if (patient.loadingUnits) "Carregando suas UBS…" else "Não foi possível carregar suas UBS. Toque em Ver minhas consultas para tentar novamente."
    patient.units.isEmpty() -> "Nenhuma UBS vinculada ao seu cadastro."
    patient.units.any { patient.unitData[it.id]?.appointments == null } ->
        "Nenhuma consulta futura nas UBS já consultadas. Abra suas consultas e escolha uma UBS para conferir as demais."
    else -> "Você não tem consulta agendada nas suas UBS."
}
