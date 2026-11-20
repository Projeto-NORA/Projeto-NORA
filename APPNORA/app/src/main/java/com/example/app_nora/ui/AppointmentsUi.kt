package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalInspectionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

@Composable
fun AppointmentsScreen(patient: PatientState, onSelectUnit: (String) -> Unit, onLoadUnits: () -> Unit,
    onLoadAppointments: () -> Unit, onCancelAppointment: (String) -> Unit,
    onConfirmPresence: (String, String) -> Unit = { _, _ -> }, onBook: () -> Unit = {}) {
    var cancelPending by remember(patient.selectedUnitId, patient.session?.token) { mutableStateOf<PatientAppointment?>(null) }
    // A janela de confirmação muda de estado sozinha: o relógio acorda só nesses instantes.
    val presenceNow = rememberPresenceClock(patient.appointments.orEmpty())
    val listNow = rememberListClock(patient.appointments.orEmpty())
    ScreenTitle("Consultas", "Atendimentos futuros e anteriores.")
    SectionCard("Sua agenda", "Por UBS") {
        if (patient.session == null) MutedText("Entre na sua conta para consultar sua agenda.")
        else {
            MutedText("Consultas de até 180 dias antes e depois de hoje.")
            patient.units?.forEach { unit ->
                Row(Modifier.fillMaxWidth().selectable(selected = unit.id == patient.selectedUnitId, enabled = !patient.busy, onClick = { onSelectUnit(unit.id) }, role = Role.RadioButton), verticalAlignment = Alignment.CenterVertically) {
                    RadioButton(selected = unit.id == patient.selectedUnitId, onClick = null)
                    Text(unit.name, Modifier.padding(start = 8.dp))
                }
            }
            if (patient.units?.isEmpty() == true) MutedText("Nenhuma UBS vinculada ao seu cadastro.")
            if (patient.units == null) {
                if (patient.loadingUnits || patient.busy) MutedText("Carregando suas UBS…") else ErrorNotice("Não foi possível carregar suas UBS.")
            }
            // Um só "Atualizar": com UBS escolhida recarrega as consultas dela; sem UBS recarrega a lista de unidades. A releitura das unidades com UBS escolhida fica na tela UBS.
            OutlinedButton(onClick = if (patient.selectedUnitId != null) onLoadAppointments else onLoadUnits, enabled = !patient.busy && (patient.selectedUnitId != null || !patient.loadingUnits)) { Text("Atualizar") }
            if (patient.selectedUnitId != null) {
                Button(onClick = onBook, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Agendar nova consulta") }
                if (patient.appointments?.isEmpty() == true) MutedText("Nenhuma consulta encontrada neste período.")
                val (upcoming, past) = remember(patient.appointments, listNow.value) { splitAppointments(patient.appointments.orEmpty(), listNow.value) }
                if (upcoming.isEmpty() && past.isNotEmpty()) MutedText("Nenhuma consulta futura nesta UBS.")
                AppointmentGroup("Próximas", true, upcoming, patient, presenceNow.value, { cancelPending = it }, onConfirmPresence)
                AppointmentGroup("Anteriores", false, past, patient, presenceNow.value, { cancelPending = it }, onConfirmPresence)
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

/** Relógio do agrupamento: só muda quando termina alguma consulta ainda válida de [appointments]; as esperas são curtas porque o `delay` não conta o sono do aparelho. */
@Composable
private fun rememberListClock(appointments: List<PatientAppointment>): State<Long> {
    val clock = LocalClockMillis.current
    val preview = LocalInspectionMode.current
    val now = remember(appointments) { mutableLongStateOf(clock()) }
    LaunchedEffect(appointments) {
        if (preview) return@LaunchedEffect
        while (true) {
            val next = appointments.filter { it.status !in INACTIVE_STATUSES }.mapNotNull { it.endsAtMillis ?: it.startsAtMillis }.filter { it >= now.longValue }.minOrNull() ?: return@LaunchedEffect
            val remaining = next + 1 - clock()
            if (remaining > 0) kotlinx.coroutines.delay(minOf(remaining, PRESENCE_CLOCK_MAX_WAIT_MS)) else now.longValue = clock()
        }
    }
    return now
}

private val OPEN_STATUSES = setOf("scheduled", "called")
private val INACTIVE_STATUSES = setOf("cancelled", "declined", "expired", "completed", "missed")

/** Separa as consultas em próximas (ainda não terminaram, inclusive a que está em andamento; da mais perto para a mais longe) e anteriores (da mais recente para a mais antiga); canceladas, recusadas, expiradas, concluídas e com falta vão sempre para anteriores. */
internal fun splitAppointments(all: List<PatientAppointment>, now: Long): Pair<List<PatientAppointment>, List<PatientAppointment>> {
    val (upcoming, past) = all.partition { it.status !in INACTIVE_STATUSES && (it.endsAtMillis ?: it.startsAtMillis ?: Long.MIN_VALUE) >= now }
    return upcoming.sortedBy { it.startsAtMillis } to past.sortedByDescending { it.startsAtMillis ?: Long.MIN_VALUE }
}

@Composable
private fun AppointmentGroup(title: String, isUpcoming: Boolean, items: List<PatientAppointment>, patient: PatientState, now: Long,
    onCancel: (PatientAppointment) -> Unit, onConfirmPresence: (String, String) -> Unit) {
    if (items.isEmpty()) return
    Text(title, Modifier.semantics { heading() }, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
    items.forEach { appointment ->
        key(appointment.id) {
            HorizontalDivider(Modifier.padding(vertical = 8.dp))
            Text(appointment.specialty, fontWeight = FontWeight.SemiBold)
            Text(formatRange(appointment.startsAt, appointment.endsAt), fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.primary)
            Text("Profissional: ${appointment.professional}")
            // Consulta que passou do fim ainda aberta no servidor: "Agendada" seria falso; a UBS é quem a encerra.
            if (!isUpcoming && appointment.status in OPEN_STATUSES) StatusTag("Aguardando a UBS", StatusTone.Neutral) else StatusBadge(appointment.status)
            bookedByLabel(appointment.bookedBy, appointment.bookedByYou, appointment.bookedByGuardianName)?.let { MutedText(it) }
            // Em "Anteriores" não há o que confirmar nem cancelar, mesmo que o servidor ainda não tenha fechado a consulta.
            if (isUpcoming) PresenceConfirmation(appointment, now, patient.busy) { onConfirmPresence(patient.selectedUnitId.orEmpty(), appointment.id) }
            // Mesma ação, cor e margem do "Sair da fila" da Jornada: o que desfaz algo é vermelho e alinhado ao texto acima.
            if (isUpcoming && appointment.status in OPEN_STATUSES) DestructiveTextButton("Cancelar consulta", onClick = { onCancel(appointment) }, enabled = !patient.busy)
        }
    }
}

/** Texto do cartão "Sua agenda" do Início quando não há próxima consulta, sem inventar dado. */
internal fun noNextAppointmentText(patient: PatientState): String {
    val actingName = patient.actingLink()?.let(::dependentDisplayName)
    return when {
        patient.units == null -> if (patient.loadingUnits || patient.busy) "Carregando suas UBS…" else "Não foi possível carregar suas UBS."
        patient.units.isEmpty() -> if (actingName == null) "Nenhuma UBS vinculada ao seu cadastro." else "Nenhuma UBS vinculada ao cadastro de $actingName."
        patient.units.any { patient.unitData[it.id]?.appointments == null } ->
            "Nenhuma consulta futura nas UBS já consultadas. Escolha outra UBS em Consultas para conferir."
        else -> if (actingName == null) "Você não tem consulta agendada nas suas UBS." else "Não há consulta agendada para $actingName."
    }
}
