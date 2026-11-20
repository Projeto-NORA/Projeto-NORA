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
    // Recolhido: quem agenda com frequência não relê as regras, e os passos começam logo abaixo do título.
    SectionCard("Como funciona", "Regras do agendamento") {
        ExplanationToggle("Ver regras", "Ocultar regras") {
            MutedText("Qualquer horário livre, em qualquer data: sem antecedência mínima nem máxima.")
            MutedText(bookingLimitRuleText(actingName))
        }
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
    ScreenTitle("Agendar consulta", "Escolha a UBS, a especialidade e o horário.")
    // Depois de marcar, o resultado vem logo abaixo do título: regras e escolha de UBS ficam de fora.
    val bookedHere = patient.lastBooked?.takeIf { patient.session != null && it.unitId == patient.selectedUnitId }
    val bookedUnit = bookedHere?.let { booked -> patient.units?.find { it.id == booked.unitId } }
    if (bookedHere != null && bookedUnit != null) {
        // O cartão já diz que deu certo: a faixa de sucesso repetiria a mesma frase; só avisos e falhas continuam nela.
        ActionMessage(patient.message?.takeIf { actionMessageKind(it) != MessageKind.Success })
        BookedCard(bookedUnit.name, bookedHere.appointment, onOpenAppointments, onAnother = onOpenBooking, actingName = actingName)
        return
    }
    BookingRules(actingName)
    if (patient.session == null) {
        SectionCard("Entre na sua conta", "Agendamento") { MutedText("Entre na sua conta para ver os horários livres.") }
        return
    }
    val units = patient.units
    SectionCard("1. Escolha a UBS", if (actingName == null) "Unidades vinculadas ao seu cadastro" else "A UBS do vínculo com $actingName") {
        when {
            units == null -> {
                if (patient.loadingUnits || patient.busy) MutedText("Carregando suas UBS…")
                else {
                    ErrorNotice("Não foi possível carregar suas UBS.")
                    RetryButton(onClick = onLoadUnits)
                }
            }
            units.isEmpty() -> MutedText("Nenhuma UBS vinculada. Vincule uma em Unidades para agendar.")
            else -> units.forEach { unit -> ChoiceRow(unit.name, unit.id == patient.selectedUnitId, !patient.busy) { onSelectUnit(unit.id) } }
        }
    }
    ActionMessage(patient.message)
    val unit = units?.find { it.id == patient.selectedUnitId } ?: return

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
                if (patient.specialtiesLoading) MutedText("Carregando especialidades…")
                else {
                    ErrorNotice(patient.bookingError ?: "Não foi possível carregar as especialidades.")
                    RetryButton(onClick = onOpenBooking)
                }
            }
            specialties.isEmpty() -> MutedText("Nenhuma especialidade disponível para agendamento.")
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
                ErrorNotice(patient.bookingError ?: "Não foi possível consultar os horários.")
                RetryButton(onClick = { onLoadAvailability(chosen, weekIndex) })
            }
        } else {
            val days = remember(result, now) { groupSlotsByDay(result.slots, now) }
            if (days.isEmpty()) {
                MutedText("Nenhum horário livre nesta semana. Veja a próxima ou escolha outra data.")
            } else {
                val shown = shownDay(days, chosenDay)
                if (isWantedDayEmpty(days, chosenDay)) MutedText("Nenhum horário livre em ${formatDayLabel(chosenDay!!)}; veja os outros dias da semana.")
                Text("Dia", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold)
                days.forEach { day ->
                    ChoiceRow("${formatDayLabel(day.dayStart)} · ${countLabel(day.slots.size, "horário", "horários")}",
                        day.dayStart == shown.dayStart, !patient.busy) { chosenDay = day.dayStart }
                }
                Text("Horário", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold)
                shown.slots.forEach { slot ->
                    key(slot.startsAt, slot.professionalId) {
                        OutlinedButton(onClick = { confirming = slot }, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) {
                            Column(Modifier.fillMaxWidth()) {
                                Text("${formatClock(slot.startsAt)} às ${formatClock(slot.endsAt)}", fontWeight = FontWeight.Bold)
                                Text(slot.professionalName, style = MaterialTheme.typography.bodySmall)
                            }
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
internal fun BookedCard(unitName: String, appointment: PatientAppointment, onOpenAppointments: () -> Unit, onAnother: () -> Unit, actingName: String? = null) {
    val who = if (actingName == null) "sua presença" else "a presença de $actingName"
    val presenceNow = rememberPresenceClock(listOf(appointment))
    // O relógio da presença não acorda no início da consulta, onde a frase de "encerrado" muda: este marca só esse instante.
    val clock = LocalClockMillis.current
    val startedAt = remember(appointment) { mutableLongStateOf(0L) }
    if (!LocalInspectionMode.current) LaunchedEffect(appointment) {
        val start = appointment.startsAtMillis ?: return@LaunchedEffect
        while (clock() < start) kotlinx.coroutines.delay(minOf(start - clock(), PRESENCE_CLOCK_MAX_WAIT_MS))
        startedAt.longValue = clock()
    }
    SectionCard("Consulta marcada", unitName) {
        Text(appointment.specialty, fontWeight = FontWeight.SemiBold)
        Text(formatRange(appointment.startsAt, appointment.endsAt), fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.primary)
        Text("Profissional: ${appointment.professional}")
        StatusBadge(appointment.status)
        // Mesmo critério da tela Consultas: a frase segue a janela de confirmação (ainda não abriu, aberta ou encerrada).
        MutedText(bookedPresenceText(appointment, maxOf(presenceNow.value, startedAt.longValue), who))
        Button(onClick = onOpenAppointments, modifier = Modifier.fillMaxWidth()) { Text("Ver minhas consultas") }
        OutlinedButton(onClick = onAnother, modifier = Modifier.fillMaxWidth()) { Text("Agendar outra especialidade") }
    }
}

/** Frase de presença do cartão "Consulta marcada" por situação da janela; [who] é "sua presença" ou "a presença de X". Nunca afirma o que a situação não garante. */
internal fun bookedPresenceText(appointment: PatientAppointment, now: Long, who: String): String = when (presenceState(appointment, now)) {
    PresenceState.NOT_YET -> presenceOpensAt(appointment)?.let { "Você poderá confirmar $who em Consultas a partir de ${formatDateTime(it)}, até 2 horas antes. Se não confirmar, nada é cancelado." }
        ?: "Confirme $who em Consultas de 48 horas até 2 horas antes. Se não confirmar, nada é cancelado."
    PresenceState.OPEN -> "Você já pode confirmar $who em Consultas, até 2 horas antes. Se não confirmar, nada é cancelado."
    PresenceState.CONFIRMED -> "Presença confirmada. Não é preciso fazer mais nada."
    PresenceState.CLOSED ->
        if ((appointment.startsAtMillis ?: Long.MAX_VALUE) <= now) "O horário da consulta já chegou: não é mais possível confirmar $who."
        else "Faltam menos de 2 horas para a consulta: o prazo de confirmar $who acabou. Nada é cancelado por isso."
    PresenceState.NOT_APPLICABLE -> "Se não confirmar $who, nada é cancelado."
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
