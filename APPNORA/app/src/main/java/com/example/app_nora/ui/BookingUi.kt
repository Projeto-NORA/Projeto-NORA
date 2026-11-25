package com.example.app_nora.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowLeft
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.DateRange
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
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
            Text("Confirme até 2 h antes. Sem confirmação, nada é cancelado.",
                style = MaterialTheme.typography.bodySmall, color = text)
        }
        PresenceState.CLOSED -> Text("Prazo de confirmação encerrado. A consulta continua marcada.",
            style = MaterialTheme.typography.bodySmall, color = text)
    }
}

@Composable
private fun BookingRules(actingName: String?) {
    SectionCard("Como funciona", "") {
        MutedText(bookingLimitRuleText(actingName))
        MutedText("Marcada na hora, sem aprovação.")
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
    ScreenTitle("Agendar consulta", "UBS, especialidade e horário.", action = {
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
            else -> units.sortedAlphabetically { it.name }.forEach { unit -> ChoiceRow(unit.name, unit.id == patient.selectedUnitId, !patient.busy) { onSelectUnit(unit.id) } }
        }
    }
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
            else -> specialties.sortedAlphabetically { it.name }.forEach { item -> ChoiceRow(item.name, item.id == specialtyId, !patient.busy) { specialtyId = item.id; weekIndex = 0; chosenDay = null } }
        }
    }

    val chosen = specialtyId ?: return
    val now = remember(unit.id, chosen, weekIndex) { clock() }
    val week = remember(now, weekIndex) { bookingWeek(now, weekIndex) }
    var pickingDate by rememberSaveable(unit.id, subject, specialtyId) { mutableStateOf(false) }
    val result = patient.availability?.takeIf { it.unitId == unit.id && it.specialtyId == chosen && it.week == weekIndex }
    SectionCard("3. Escolha o dia e o horário", week?.let { "Semana de ${formatWeekRange(it)}" } ?: "Escolha uma data") {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Hint("Semana anterior") {
                IconButton(onClick = { weekIndex--; chosenDay = null }, enabled = weekIndex > 0) {
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowLeft, contentDescription = "Semana anterior")
                }
            }
            TextButton(onClick = { pickingDate = true }, enabled = !patient.busy, modifier = Modifier.weight(1f)) {
                Icon(Icons.Default.DateRange, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(6.dp))
                Text("Escolher outra data")
            }
            Hint("Próxima semana") {
                IconButton(onClick = { weekIndex++; chosenDay = null }, enabled = bookingWeek(now, weekIndex + 1) != null) {
                    Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = "Próxima semana")
                }
            }
        }
        if (pickingDate) BookingDatePicker(now, onPick = { dayStart -> weekIndex = bookingWeekIndexFor(dayStart, now); chosenDay = dayStart; pickingDate = false }, onDismiss = { pickingDate = false })
        val days = remember(result, now) { result?.let { groupSlotsByDay(it.slots, now) }.orEmpty() }
        val shown = if (days.isEmpty()) null else shownDay(days, chosenDay)
        // Faixa horizontal de dias: dia da semana e número; o escolhido em destaque e os sem vaga esmaecidos.
        if (week != null) LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            items(weekDays(week)) { day ->
                val count = days.firstOrNull { it.dayStart == day }?.slots?.size ?: 0
                DayPill(day, selected = shown?.dayStart == day, available = count > 0, enabled = count > 0 && !patient.busy) { chosenDay = day }
            }
        }
        if (result == null) {
            if (patient.availabilityLoading) {
                LinearProgressIndicator(Modifier.fillMaxWidth())
                MutedText("Buscando horários livres…")
            } else {
                MutedText(patient.bookingError ?: "Não foi possível consultar os horários.")
                OutlinedButton(onClick = { onLoadAvailability(chosen, weekIndex) }, modifier = Modifier.fillMaxWidth()) { Text("Tentar novamente") }
            }
        } else if (shown == null) {
            MutedText("Sem horários livres nesta semana. Veja a próxima ou escolha outra data.")
        } else {
            if (isWantedDayEmpty(days, chosenDay)) MutedText("Sem horários em ${formatDayLabel(chosenDay!!)}. Veja os outros dias.")
            Text(formatDayLabel(shown.dayStart).replaceFirstChar { it.uppercase() }, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
        }
    }
    if (result != null && days(result, now).isNotEmpty()) {
        val all = days(result, now)
        val shown = shownDay(all, chosenDay)
        val professionals = remember(shown, professionalQuery) {
            val query = professionalQuery.normalizedSearchText()
            groupByProfessional(if (query.isEmpty()) shown.slots else shown.slots.filter { it.professionalName.normalizedSearchText().contains(query) })
        }
        if (professionals.isEmpty()) MutedText("Nenhum horário desse dia corresponde ao filtro de profissional.")
        professionals.forEach { professional ->
            key(shown.dayStart, professional.professionalId) {
                ProfessionalCard(professional, unit.name, !patient.busy) { confirming = it }
            }
        }
    }
    confirming?.let { slot ->
        ConfirmSlotSheet(slot, unit.name, actingName, onConfirm = { confirming = null; onBook(slot) }, onDismiss = { confirming = null })
    }
}

private fun days(result: AvailabilityResult, now: Long) = groupSlotsByDay(result.slots, now)

@Composable
private fun DayPill(day: Long, selected: Boolean, available: Boolean, enabled: Boolean, onClick: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    Surface(onClick = onClick, enabled = enabled, shape = RoundedCornerShape(18.dp),
        color = if (selected) colors.primary else colors.surface,
        border = BorderStroke(1.dp, if (selected) colors.primary else colors.outlineVariant),
        shadowElevation = if (selected) 4.dp else 0.dp,
        modifier = Modifier.width(58.dp).alpha(if (available) 1f else .38f)) {
        Column(Modifier.padding(vertical = 10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
            Text(formatWeekdayShort(day), style = MaterialTheme.typography.labelMedium, color = if (selected) colors.onPrimary else colors.onSurfaceVariant)
            Text(formatDayNumber(day), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, color = if (selected) colors.onPrimary else colors.onSurface)
            Text(formatMonthShort(day), style = MaterialTheme.typography.labelSmall, color = if (selected) colors.onPrimary else colors.onSurfaceVariant)
        }
    }
}

/** Cartão de um profissional: inicial, nome, consultório e os horários em chips de manhã e tarde ("+N" mostra o resto). */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun ProfessionalCard(day: ProfessionalDay, unitName: String, enabled: Boolean, onPick: (AvailableSlot) -> Unit) {
    var expanded by rememberSaveable(day.professionalId) { mutableStateOf(false) }
    val (morning, afternoon, hidden) = visibleSlots(day, expanded)
    Card(Modifier.fillMaxWidth(), shape = RoundedCornerShape(20.dp), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(defaultElevation = 3.dp)) {
        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                ProfileAvatar(day.professionalName, null, 48.dp)
                Column(Modifier.weight(1f)) {
                    Text(day.professionalName, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                    MutedText(day.room ?: unitName)
                }
                Text(countLabel(day.total, "horário", "horários"), style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.primary)
            }
            listOf("Manhã" to morning, "Tarde" to afternoon).forEach { (label, slots) ->
                if (slots.isNotEmpty()) {
                    Text(label, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, fontWeight = FontWeight.SemiBold)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        slots.forEach { slot ->
                            AssistChip(onClick = { onPick(slot) }, enabled = enabled, label = { Text(formatClock(slot.startsAt), fontWeight = FontWeight.SemiBold) },
                                shape = RoundedCornerShape(12.dp),
                                modifier = Modifier.semantics { contentDescription = "${formatClock(slot.startsAt)} às ${formatClock(slot.endsAt)} · ${slot.professionalName}" })
                        }
                    }
                }
            }
            if (hidden > 0 || expanded && day.total > SLOT_CHIPS_COLLAPSED) AssistChip(onClick = { expanded = !expanded },
                label = { Text(if (expanded) "Mostrar menos" else "+$hidden", fontWeight = FontWeight.Bold) }, shape = RoundedCornerShape(12.dp))
        }
    }
}

/** Confirmação do horário escolhido, em bottom sheet. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ConfirmSlotSheet(slot: AvailableSlot, unitName: String, actingName: String?, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp).padding(bottom = 28.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("Marcar esta consulta?", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            Text(slot.specialtyName, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
            Text(formatRange(slot.startsAt, slot.endsAt))
            Text("Profissional: ${slot.professionalName}")
            Text("Unidade: $unitName")
            MutedText(bookingCancelWarningText(actingName))
            Button(onClick = onConfirm, modifier = Modifier.fillMaxWidth().height(54.dp), shape = RoundedCornerShape(16.dp)) {
                Text("Marcar consulta", fontWeight = FontWeight.Bold)
            }
            TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) { Text("Voltar") }
        }
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
        MutedText("Confirme a presença em Consultas de 48 h a 2 h antes.")
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
