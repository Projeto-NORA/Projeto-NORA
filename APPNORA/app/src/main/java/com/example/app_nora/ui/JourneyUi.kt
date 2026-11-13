package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

@Composable
fun JourneyScreen(patient: PatientState, onOpenAppointments: () -> Unit, onRefreshAttendance: () -> Unit,
    onLeaveQueue: (Long) -> Unit) {
    ScreenTitle("Sua Jornada", "Acompanhe seus cuidados e próximos passos.")
    SectionCard("Consultas, exames e vacinas", "Histórico recebido da rede de saúde") {
        MutedText("A rede ainda não disponibiliza esses registros no aplicativo.")
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf("Consultas", "Exames", "Vacinas").forEach { label ->
                Surface(Modifier.weight(1f), shape = RoundedCornerShape(12.dp),
                    color = MaterialTheme.colorScheme.primaryContainer.copy(alpha = .55f)) {
                    Text(label, Modifier.padding(horizontal = 6.dp, vertical = 10.dp),
                        style = MaterialTheme.typography.labelMedium, maxLines = 1,
                        color = MaterialTheme.colorScheme.onPrimaryContainer)
                }
            }
        }
        OutlinedButton(onClick = onOpenAppointments, modifier = Modifier.fillMaxWidth()) {
            Text("Ver consultas")
        }
    }
    AttendanceCard(patient, onRefreshAttendance)
    ActionMessage(patient.message)
    patient.unitQueue?.let { queue ->
        if (queue.isNotEmpty()) SectionCard("Solicitações de atendimento", "Status informado pela unidade selecionada") {
            QueueEntries(queue, patient.busy, onLeaveQueue)
        }
    }
}
