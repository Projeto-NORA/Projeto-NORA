package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

/** Explicação única da conta, em linguagem simples, igual à regra do servidor (docs/api.md). */
internal const val ATTENDANCE_EXPLANATION =
    "Como é calculado: cada consulta concluída aumenta o índice e cada falta o reduz. Cancelar pelo aplicativo reduz " +
        "menos quanto mais cedo for feito: com 4 dias ou mais de antecedência não reduz, e com menos de 1 hora vale " +
        "como falta. Cancelamentos feitos pela unidade não contam."
internal const val ATTENDANCE_NOT_PRIORITY =
    "O índice não altera fila, prioridade clínica nem seu direito ao atendimento."

/**
 * Índice de comparecimento, geral e por UBS, com os números que o compõem: do próprio paciente ou, agindo por um dependente, dele
 * (o servidor conta nos cancelamentos os feitos pelo aplicativo na conta do dependente, por ele ou pelo responsável).
 */
@Composable
internal fun AttendanceCard(patient: PatientState, onRefresh: () -> Unit) {
    val summary = remember(patient.units, patient.unitData) { summarizeAttendance(patient.units, patient.unitData) }
    var detailOpen by rememberSaveable { mutableStateOf(false) }
    val units = patient.units
    val name = patient.actingLink()?.let(::dependentDisplayName)
    val participation = if (name == null) "sua participação" else "a participação de $name"
    SectionCard("Índice de Jornada", (if (name == null) "Seu comparecimento" else "Comparecimento de $name") +
        " nos últimos ${summary.windowMonths} meses") {
        when {
            patient.session == null -> MutedText("Entre na sua conta para ver seu índice.")
            units == null -> MutedText(if (patient.loadingUnits) "Carregando suas UBS…"
                else "Não foi possível carregar suas UBS. Atualize para tentar novamente.")
            units.isEmpty() -> MutedText("Nenhuma UBS vinculada ao seu cadastro.")
            summary.answeredUnits == 0 && summary.failedUnits == 0 ->
                MutedText(if (patient.attendanceLoading) "Consultando $participation…"
                    else "${participation.replaceFirstChar { it.uppercase() }} ainda não foi consultada.")
            summary.answeredUnits == 0 -> MutedText("Não foi possível consultar $participation. Atualize para tentar novamente.")
            else -> {
                val percent = summary.percent
                if (percent == null) {
                    Text("Ainda não há consultas encerradas nos últimos ${summary.windowMonths} meses",
                        style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                } else {
                    Text("$percent%", style = MaterialTheme.typography.displaySmall, fontWeight = FontWeight.ExtraBold,
                        color = MaterialTheme.colorScheme.primary,
                        modifier = Modifier.clearAndSetSemantics { contentDescription = "Comparecimento de $percent por cento" })
                    Text("Consultas concluídas: ${summary.completed}")
                    Text("Faltas: ${summary.missed}")
                    Text("${if (name == null) "Cancelamentos feitos por você" else "Cancelamentos pelo aplicativo"}: ${summary.cancelledByPatient}")
                }
                if (summary.failedUnits > 0) Text(
                    if (summary.failedUnits == 1) "Uma UBS não respondeu e não entrou nesta conta. Atualize para tentar de novo."
                    else "${summary.failedUnits} UBS não responderam e não entraram nesta conta. Atualize para tentar de novo.",
                    style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error)
                if (units.size > 1) {
                    TextButton(onClick = { detailOpen = !detailOpen }, modifier = Modifier.fillMaxWidth()) {
                        Text(if (detailOpen) "Ocultar detalhe por UBS" else "Ver detalhe por UBS", fontWeight = FontWeight.SemiBold)
                    }
                    if (detailOpen) units.forEach { unit -> key(unit.id) { UnitAttendanceRow(unit, patient.unitData[unit.id]) } }
                }
            }
        }
        MutedText(ATTENDANCE_EXPLANATION)
        MutedText(if (name == null) ATTENDANCE_NOT_PRIORITY
            else "O índice não altera fila, prioridade clínica nem o direito de $name ao atendimento.")
        if (patient.session != null && units != null) OutlinedButton(onClick = onRefresh, enabled = !patient.attendanceLoading,
            modifier = Modifier.fillMaxWidth()) {
            Text(if (patient.attendanceLoading) "Atualizando…" else "Atualizar participação")
        }
    }
}

@Composable
private fun UnitAttendanceRow(unit: PatientHealthUnit, snapshot: UnitSnapshot?) {
    val value = snapshot?.attendance
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Text(unit.name, fontWeight = FontWeight.SemiBold)
        if (value == null) {
            MutedText(if (snapshot?.attendanceFailed == true) "Esta UBS não respondeu." else "Ainda não consultada.")
        } else {
            val percent = attendancePercent(attendanceFraction(value.completed, value.missed, value.cancellationWeight))
            Text(if (percent == null) "Sem consultas encerradas" else "$percent%", fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary)
            MutedText("Concluídas: ${value.completed} · Faltas: ${value.missed} · Cancelamentos seus: ${value.cancelledByPatient}")
        }
    }
}
