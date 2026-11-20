package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

/** As três visões da Jornada; as duas últimas mostram os registros reais da equipe da UBS. */
enum class JourneySection(val label: String) { APPOINTMENTS("Consultas"), EXAMS("Exames"), VACCINES("Vacinas") }

@Composable
fun JourneyScreen(patient: PatientState, onOpenAppointments: () -> Unit, onRefreshAttendance: () -> Unit,
    onLeaveQueue: (Long) -> Unit, onLoadRecords: () -> Unit = {}, initialSection: JourneySection = JourneySection.APPOINTMENTS) {
    val link = patient.actingLink()
    ScreenTitle(if (link == null) "Sua Jornada" else "Jornada de ${dependentDisplayName(link)}")
    var section by rememberSaveable { mutableStateOf(initialSection) }
    SectionCard("Consultas, exames e vacinas") {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            JourneySection.entries.forEach { item ->
                FilterChip(selected = section == item, onClick = { section = item }, label = { Text(item.label, maxLines = 1, fontWeight = if (section == item) FontWeight.SemiBold else FontWeight.Normal) },
                    modifier = Modifier.weight(1f))
            }
        }
        when (section) {
            JourneySection.APPOINTMENTS -> {
                MutedText("Veja as consultas futuras e anteriores na tela Consultas.")
                OutlinedButton(onClick = onOpenAppointments, modifier = Modifier.fillMaxWidth()) { Text("Ver consultas") }
            }
            JourneySection.EXAMS -> RecordsList(patient, onLoadRecords, vaccines = false)
            JourneySection.VACCINES -> RecordsList(patient, onLoadRecords, vaccines = true)
        }
    }
    AttendanceCard(patient, onRefreshAttendance)
    ActionMessage(patient.message)
    patient.unitQueue?.let { queue ->
        if (queue.isNotEmpty()) SectionCard("Solicitações de atendimento") {
            QueueEntries(queue, patient.busy, onLeaveQueue)
        }
    }
}

/** Exames ou vacinas por UBS, com estado vazio honesto: uma UBS que não respondeu nunca vira "nenhum registro". */
@Composable
private fun RecordsList(patient: PatientState, onLoadRecords: () -> Unit, vaccines: Boolean) {
    val overview = remember(patient.units, patient.unitData) { summarizeRecords(patient.units, patient.unitData) }
    val noun = if (vaccines) "vacina" else "exame"
    val total = if (vaccines) overview.vaccineCount else overview.examCount
    when {
        patient.session == null -> MutedText("Entre na sua conta para ver seus registros.")
        patient.units == null -> if (patient.loadingUnits || patient.busy) MutedText("Carregando as UBS…")
            else ErrorNotice("Não foi possível carregar as UBS. Atualize para tentar de novo.")
        patient.units.isEmpty() -> MutedText("Nenhuma UBS vinculada ao cadastro.")
        overview.answered == 0 && overview.failed == 0 ->
            MutedText(if (patient.records.loading) "Consultando os registros das UBS…" else "Os registros ainda não foram consultados.")
        overview.answered == 0 -> ErrorNotice(patient.records.error ?: "Não foi possível consultar os registros. Atualize para tentar de novo.")
        total == 0 -> MutedText(
            if (vaccines) "Nenhuma vacina registrada pelas UBS que responderam." else "Nenhum exame registrado pelas UBS que responderam.")
        else -> overview.units.forEach { view ->
            val shown = if (vaccines) view.vaccines.size else view.exams.size
            if (shown > 0) {
                Text(view.unit.name, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
                if (vaccines) view.vaccines.forEach { VaccineRow(it) } else view.exams.forEach { ExamRow(it) }
            }
        }
    }
    if (overview.answered > 0 && overview.failed > 0) ErrorNotice(
        if (overview.failed == 1) "Uma UBS não respondeu, então pode haver ${noun}s dela que não aparecem aqui. Atualize para tentar de novo."
        else "${overview.failed} UBS não responderam, então pode haver ${noun}s delas que não aparecem aqui. Atualize para tentar de novo.")
    if (patient.records.loading) LinearProgressIndicator(Modifier.fillMaxWidth())
    if (patient.session != null && patient.units?.isNotEmpty() == true) OutlinedButton(onClick = onLoadRecords, enabled = !patient.records.loading,
        modifier = Modifier.fillMaxWidth()) { Text(if (patient.records.loading) "Atualizando…" else "Atualizar exames e vacinas") }
}

@Composable
private fun ExamRow(exam: ExamRecord) {
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Text(exam.name, fontWeight = FontWeight.SemiBold)
        examStatusText(exam)?.let { ExamStatusTag(exam.status, it) }
        exam.note?.takeIf { it.isNotBlank() }?.let { MutedText("Observação: $it") }
        HorizontalDivider(Modifier.padding(top = 6.dp))
    }
}

/** Selo da situação do exame: marca e cor, sempre com o texto; "Solicitado" (ainda por fazer) se distingue de feito e de cancelado. */
@Composable
private fun ExamStatusTag(status: String, text: String) {
    when (status) {
        "done" -> StatusTag(text, StatusTone.Success)
        "requested" -> StatusTag(text, StatusTone.Info)
        else -> StatusTag(text, StatusTone.Neutral, "✕")
    }
}

@Composable
private fun VaccineRow(vaccine: VaccineRecord) {
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Text(vaccine.name, fontWeight = FontWeight.SemiBold)
        Text(vaccineText(vaccine))
        vaccine.note?.takeIf { it.isNotBlank() }?.let { MutedText("Observação: $it") }
        HorizontalDivider(Modifier.padding(top = 6.dp))
    }
}
