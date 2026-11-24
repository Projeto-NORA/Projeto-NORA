package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalInspectionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

/**
 * Relógio da confirmação de presença: o valor só muda nos instantes em que a janela de alguma consulta de [appointments] abre ou
 * fecha (não a cada segundo, então não recompõe a tela à toa). É recriado quando a lista muda, já na hora atual. A espera é feita em
 * trechos curtos ([trackPresenceClock]) porque o `delay` não conta o sono do aparelho: uma espera única de até 48 h deixaria a tela
 * mostrando "poderá confirmar a partir de..." muito depois de a janela abrir.
 */
@Composable
internal fun rememberPresenceClock(appointments: List<PatientAppointment>): State<Long> {
    val clock = LocalClockMillis.current
    val preview = LocalInspectionMode.current
    val now = remember(appointments) { mutableLongStateOf(clock()) }
    LaunchedEffect(appointments) {
        if (preview) return@LaunchedEffect
        trackPresenceClock(appointments, now.longValue, clock, { now.longValue = it })
    }
    return now
}

/**
 * Confirmação de presença de uma consulta agendada: botão só dentro da janela de 48 h a 2 h antes (pelo relógio do
 * aparelho; o servidor decide), texto da hora depois de confirmar e aviso de quando será possível antes da janela.
 * Em todos os casos sem confirmação diz que nada é cancelado. [onDark] usa as cores do cartão azul do Início.
 */
@Composable
internal fun PresenceConfirmation(appointment: PatientAppointment, now: Long, busy: Boolean, onDark: Boolean = false, onConfirm: () -> Unit) {
    val text = if (onDark) Color.White.copy(alpha = .94f) else MaterialTheme.colorScheme.onSurfaceVariant
    when (presenceState(appointment, now)) {
        PresenceState.NOT_APPLICABLE -> Unit
        PresenceState.CONFIRMED -> Text(presenceConfirmedText(appointment), style = MaterialTheme.typography.labelLarge,
            fontWeight = FontWeight.SemiBold, color = if (onDark) Color.White else MaterialTheme.colorScheme.primary)
        PresenceState.NOT_YET -> Text("Você poderá confirmar sua presença a partir de " +
            "${presenceOpensAt(appointment)?.let { formatDateTime(it) } ?: "48 horas antes"}. Se não confirmar, nada é cancelado.",
            style = MaterialTheme.typography.bodySmall, color = text)
        PresenceState.OPEN -> {
            if (onDark) Button(onClick = onConfirm, enabled = !busy, modifier = Modifier.fillMaxWidth(),
                colors = ButtonDefaults.buttonColors(containerColor = Color.White, contentColor = Color(0xFF0A3D68))) { Text("Confirmar presença") }
            else Button(onClick = onConfirm, enabled = !busy, modifier = Modifier.fillMaxWidth()) { Text("Confirmar presença") }
            Text("Você pode confirmar até 2 horas antes da consulta. Se não confirmar, nada é cancelado.",
                style = MaterialTheme.typography.bodySmall, color = text)
        }
        PresenceState.CLOSED -> Text("O prazo para confirmar a presença terminou. Sua consulta continua agendada: nada é cancelado por isso.",
            style = MaterialTheme.typography.bodySmall, color = text)
    }
}

@Composable
private fun BookingRules(actingName: String?) {
    SectionCard("Como funciona", "Regras do agendamento pelo aplicativo") {
        MutedText("Você pode marcar qualquer horário livre, em qualquer data: não há antecedência mínima nem máxima.")
        MutedText(bookingLimitRuleText(actingName))
        MutedText("A consulta vale na hora: não há aprovação da unidade. Nada é cancelado por falta de confirmação de presença.")
    }
}

@Composable
private fun ChoiceRow(label: String, selected: Boolean, enabled: Boolean, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().selectable(selected = selected, enabled = enabled, onClick = onClick, role = Role.RadioButton),
        verticalAlignment = Alignment.CenterVertically) {
        RadioButton(selected = selected, onClick = null)
        Text(label, Modifier.padding(start = 8.dp))
    }
}

/**
 * Agendamento pelo catálogo: UBS vinculada, especialidade, dia, horário livre e confirmação. As leituras são
 * feitas por [onLoadAvailability] (uma por mudança de especialidade ou de semana) e [onBook] marca o horário.
 */
@Composable
fun BookingScreen(patient: PatientState, onSelectUnit: (String) -> Unit, onLoadUnits: () -> Unit, onOpenBooking: () -> Unit,
    onLoadAvailability: (String, Int) -> Unit, onBook: (AvailableSlot) -> Unit, onOpenAppointments: () -> Unit,
    initialSpecialtyId: String? = null, actingName: String? = null) {
    val clock = LocalClockMillis.current
    var professionalQuery by remember(patient.selectedUnitId, patient.actingFor) { mutableStateOf("") }
    ScreenTitle("Agendar consulta", "Escolha a UBS, a especialidade e um horário livre.", action = {
        if (patient.session != null && patient.lastBooked == null) NoraFilters(
            if (professionalQuery.isBlank()) 0 else 1, onClear = { professionalQuery = "" }
        ) {
            FilterSearch(professionalQuery, { professionalQuery = it }, "Profissional nos horários livres")
        }
    })
    BookingRules(actingName)
    if (patient.session == null) {
        SectionCard("Entre na sua conta", "Agendamento") { MutedText("Entre na sua conta para ver os horários livres das suas UBS.") }
        return
    }
    val units = patient.units
    SectionCard("1. Escolha a UBS", if (actingName == null) "Somente unidades vinculadas ao seu cadastro" else "A UBS do vínculo com $actingName") {
        when {
            units == null -> {
                MutedText(if (patient.loadingUnits || patient.busy) "Carregando suas UBS…" else "Não foi possível carregar suas UBS.")
                if (!patient.loadingUnits && !patient.busy) OutlinedButton(onClick = onLoadUnits, modifier = Modifier.fillMaxWidth()) { Text("Tentar novamente") }
            }
            units.isEmpty() -> MutedText("Nenhuma UBS vinculada ao seu cadastro. Vincule uma UBS em Unidades para agendar.")
            else -> units.forEach { unit -> ChoiceRow(unit.name, unit.id == patient.selectedUnitId, !patient.busy) { onSelectUnit(unit.id) } }
        }
    }
    ActionMessage(patient.message)
    val unit = units?.find { it.id == patient.selectedUnitId } ?: return
    val booked = patient.lastBooked?.takeIf { it.unitId == unit.id }
    if (booked != null) {
        BookedCard(unit.name, booked.appointment, onOpenAppointments, onAnother = onOpenBooking)
        return
    }

    // A escolha é de quem se atende: trocar o "agindo por" (mesmo com a UBS igual) recomeça pela especialidade, já que o controlador
    // descartou os horários da outra pessoa; sem isso a tela ficaria com a escolha antiga e sem horários, mostrando falha que não houve.
    val subject = patient.actingFor
    var specialtyId by rememberSaveable(unit.id, subject) { mutableStateOf(initialSpecialtyId) }
    var weekIndex by rememberSaveable(unit.id, subject, specialtyId) { mutableIntStateOf(0) }
    // O dia pedido não é recomeçado pela troca de semana: "Escolher outra data" muda a semana e o dia juntos (os botões de semana, sim, o limpam).
    var chosenDay by rememberSaveable(unit.id, subject, specialtyId) { mutableStateOf<Long?>(null) }
    var confirming by remember(unit.id, subject) { mutableStateOf<AvailableSlot?>(null) }
    // Uma leitura por mudança de UBS, pessoa, especialidade ou semana; atualizar a tela não repete a requisição.
    LaunchedEffect(unit.id, subject, specialtyId, weekIndex, patient.session.token) {
        specialtyId?.let { onLoadAvailability(it, weekIndex) }
    }

    SectionCard("2. Escolha a especialidade", "Catálogo da rede") {
        val specialties = patient.specialties
        when {
            specialties == null -> {
                MutedText(if (patient.specialtiesLoading) "Carregando especialidades…" else patient.bookingError ?: "Não foi possível carregar as especialidades.")
                if (!patient.specialtiesLoading) OutlinedButton(onClick = onOpenBooking, modifier = Modifier.fillMaxWidth()) { Text("Tentar novamente") }
            }
            specialties.isEmpty() -> MutedText("A rede ainda não tem especialidades disponíveis para agendamento.")
            else -> specialties.forEach { item -> ChoiceRow(item.name, item.id == specialtyId, !patient.busy) { specialtyId = item.id; weekIndex = 0; chosenDay = null } }
        }
    }

    val chosen = specialtyId ?: return
    val now = remember(unit.id, chosen, weekIndex) { clock() }
    val week = remember(now, weekIndex) { bookingWeek(now, weekIndex) }
    var pickingDate by rememberSaveable(unit.id, subject, specialtyId) { mutableStateOf(false) }
    val result = patient.availability?.takeIf { it.unitId == unit.id && it.specialtyId == chosen && it.week == weekIndex }
    SectionCard("3. Escolha o dia e o horário", week?.let { "Semana de ${formatWeekRange(it)}" } ?: "Escolha uma data") {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = { weekIndex--; chosenDay = null }, enabled = weekIndex > 0, modifier = Modifier.weight(1f)) { Text("Semana anterior") }
            OutlinedButton(onClick = { weekIndex++; chosenDay = null }, enabled = bookingWeek(now, weekIndex + 1) != null, modifier = Modifier.weight(1f)) { Text("Próxima semana") }
        }
        OutlinedButton(onClick = { pickingDate = true }, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Escolher outra data") }
        if (pickingDate) BookingDatePicker(now, onPick = { dayStart -> weekIndex = bookingWeekIndexFor(dayStart, now); chosenDay = dayStart; pickingDate = false }, onDismiss = { pickingDate = false })
        if (result == null) {
            if (patient.availabilityLoading) {
                LinearProgressIndicator(Modifier.fillMaxWidth())
                MutedText("Consultando os horários livres…")
            } else {
                MutedText(patient.bookingError ?: "Não foi possível consultar os horários.")
                OutlinedButton(onClick = { onLoadAvailability(chosen, weekIndex) }, modifier = Modifier.fillMaxWidth()) { Text("Tentar novamente") }
            }
        } else {
            val days = remember(result, now) { groupSlotsByDay(result.slots, now) }
            if (days.isEmpty()) {
                MutedText("Nenhum horário livre nesta semana para esta especialidade nesta UBS. Veja a próxima semana ou escolha outra data.")
            } else {
                val shown = shownDay(days, chosenDay)
                if (isWantedDayEmpty(days, chosenDay)) MutedText("Nenhum horário livre em ${formatDayLabel(chosenDay!!)}; veja os outros dias da semana.")
                Text("Dia", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold)
                days.forEach { day ->
                    ChoiceRow("${formatDayLabel(day.dayStart)} · ${countLabel(day.slots.size, "horário", "horários")}",
                        day.dayStart == shown.dayStart, !patient.busy) { chosenDay = day.dayStart }
                }
                Text("Horário", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold)
                val matchingSlots = remember(shown.slots, professionalQuery) {
                    val query = professionalQuery.normalizedSearchText()
                    if (query.isEmpty()) shown.slots else shown.slots.filter { it.professionalName.normalizedSearchText().contains(query) }
                }
                if (matchingSlots.isEmpty()) MutedText("Nenhum horário desse dia corresponde ao filtro de profissional.")
                matchingSlots.forEach { slot ->
                    key(slot.startsAt, slot.professionalId) {
                        OutlinedButton(onClick = { confirming = slot }, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) {
                            Text("${formatClock(slot.startsAt)} às ${formatClock(slot.endsAt)} · ${slot.professionalName}")
                        }
                    }
                }
            }
        }
    }
    confirming?.let { slot ->
        AlertDialog(onDismissRequest = { confirming = null }, containerColor = MaterialTheme.colorScheme.surface,
            title = { Text("Marcar esta consulta?") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("${slot.specialtyName} em ${formatRange(slot.startsAt, slot.endsAt)}", fontWeight = FontWeight.SemiBold)
                    Text("Profissional: ${slot.professionalName}")
                    Text("Unidade: ${unit.name}")
                    Text("A consulta vale na hora, sem aprovação da unidade.")
                    Text(bookingCancelWarningText(actingName))
                }
            },
            confirmButton = { TextButton(onClick = { confirming = null; onBook(slot) }) { Text("Marcar consulta") } },
            dismissButton = { TextButton(onClick = { confirming = null }) { Text("Voltar") } })
    }
}

/** Resultado de uma marcação: a consulta nova, o que fazer depois e os atalhos. */
@Composable
internal fun BookedCard(unitName: String, appointment: PatientAppointment, onOpenAppointments: () -> Unit, onAnother: () -> Unit) {
    SectionCard("Consulta marcada", unitName) {
        Text(appointment.specialty, fontWeight = FontWeight.SemiBold)
        Text(formatRange(appointment.startsAt, appointment.endsAt))
        Text("Profissional: ${appointment.professional}")
        Text(statusLabel(appointment.status))
        MutedText("De 48 horas até 2 horas antes, confirme sua presença em Consultas. Se não confirmar, nada é cancelado.")
        Button(onClick = onOpenAppointments, modifier = Modifier.fillMaxWidth()) { Text("Ver minhas consultas") }
        OutlinedButton(onClick = onAnother, modifier = Modifier.fillMaxWidth()) { Text("Agendar outra especialidade") }
    }
}

/** Seletor de data do agendamento: só de hoje até o teto de [BOOKING_HORIZON_YEARS] anos do servidor; devolve o início do dia escolhido no calendário do aparelho. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun BookingDatePicker(now: Long, onPick: (Long) -> Unit, onDismiss: () -> Unit) {
    val today = java.util.Calendar.getInstance().apply { timeInMillis = now }.get(java.util.Calendar.YEAR)
    val lastYear = java.util.Calendar.getInstance().apply { timeInMillis = bookingHorizon(now) }.get(java.util.Calendar.YEAR)
    val state = rememberDatePickerState(selectableDates = object : SelectableDates {
        override fun isSelectableDate(utcTimeMillis: Long): Boolean = isDayBookable(localDayFromPicker(utcTimeMillis), now)
        override fun isSelectableYear(year: Int): Boolean = year in today..lastYear
    })
    DatePickerDialog(
        onDismissRequest = onDismiss,
        confirmButton = { TextButton(onClick = { state.selectedDateMillis?.let { onPick(localDayFromPicker(it)) } }, enabled = state.selectedDateMillis != null) { Text("Ver horários") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancelar") } },
    ) { DatePicker(state = state) }
}
