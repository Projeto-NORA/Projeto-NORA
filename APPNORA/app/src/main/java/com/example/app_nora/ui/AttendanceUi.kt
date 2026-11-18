package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

/** Explicação única da conta, em linguagem simples, igual à regra do servidor (docs/api.md). */
internal const val ATTENDANCE_EXPLANATION =
    "Como é calculado: cada consulta concluída aumenta o score e cada falta o reduz. Cancelar pelo aplicativo reduz " +
        "menos quanto mais cedo for feito: com 4 dias ou mais de antecedência não reduz, e com menos de 1 hora vale " +
        "como falta. Cancelamentos feitos pela unidade não contam."
/** Escala do score: pontos de 0 a 1000 e as quatro faixas (iguais às do servidor e do OBS). */
internal const val ATTENDANCE_BANDS =
    "O score vai de 0 a 1000: Baixo (0 a 299), Regular (300 a 599), Bom (600 a 799) e Excelente (800 a 1000). " +
        "Quem ainda não tem consulta encerrada começa em 1000."
/** Efeito do score, igual ao texto do contrato: ordem das ofertas de Vaga Relâmpago, sem mexer na prioridade clínica. */
internal fun attendancePriorityText(name: String?): String =
    "Quem tem score maior recebe antes as ofertas de Vaga Relâmpago; empate segue a ordem de entrada na fila. " +
        "O score não muda a prioridade clínica nem o direito ${if (name == null) "" else "de $name "}ao atendimento."

/**
 * Score de comparecimento (0 a 1000, com faixa), geral e por UBS, com os números que o compõem: do próprio paciente ou, agindo por um
 * dependente, dele (o servidor conta nos cancelamentos os feitos pelo aplicativo na conta do dependente, por ele ou pelo responsável).
 */
@Composable
internal fun AttendanceCard(patient: PatientState, onRefresh: () -> Unit) {
    val summary = remember(patient.units, patient.unitData) { summarizeAttendance(patient.units, patient.unitData) }
    var detailOpen by rememberSaveable { mutableStateOf(false) }
    val units = patient.units
    val name = patient.actingLink()?.let(::dependentDisplayName)
    val participation = if (name == null) "sua participação" else "a participação de $name"
    SectionCard("Score de Jornada", (if (name == null) "Seu comparecimento" else "Comparecimento de $name") +
        " nos últimos ${summary.windowMonths} meses") {
        when {
            patient.session == null -> MutedText("Entre na sua conta para ver seu score.")
            units == null -> MutedText(if (patient.loadingUnits) "Carregando suas UBS…"
                else "Não foi possível carregar suas UBS. Atualize para tentar novamente.")
            units.isEmpty() -> MutedText("Nenhuma UBS vinculada ao seu cadastro.")
            summary.answeredUnits == 0 && summary.failedUnits == 0 ->
                MutedText(if (patient.attendanceLoading) "Consultando $participation…"
                    else "${participation.replaceFirstChar { it.uppercase() }} ainda não foi consultada.")
            summary.answeredUnits == 0 -> MutedText("Não foi possível consultar $participation. Atualize para tentar novamente.")
            else -> {
                ScoreHeadline(summary.points, summary.band, summary.hasHistory, name)
                if (summary.hasHistory) {
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
        MutedText(ATTENDANCE_BANDS)
        MutedText(ATTENDANCE_EXPLANATION)
        MutedText(attendancePriorityText(name))
        if (patient.session != null && units != null) OutlinedButton(onClick = onRefresh, enabled = !patient.attendanceLoading,
            modifier = Modifier.fillMaxWidth()) {
            Text(if (patient.attendanceLoading) "Atualizando…" else "Atualizar participação")
        }
    }
}

/** Cor da faixa; o rótulo escrito sempre acompanha, então a cor nunca é a única informação. */
@Composable
private fun bandColor(band: AttendanceBand) =
    if (band == AttendanceBand.LOW) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary

/**
 * Número do score em destaque, com "de 1000" e a faixa por extenso. Sem histórico mostra "1000 · sem consultas encerradas
 * ainda"; agindo por um dependente a descrição falada traz o nome dele.
 */
@Composable
private fun ScoreHeadline(points: Int, band: AttendanceBand, hasHistory: Boolean, name: String?) {
    val owner = if (name == null) "Score" else "Score de $name"
    val spoken = "$owner: $points de $SCORE_MAX_POINTS pontos, faixa ${band.label}" +
        (if (hasHistory) "" else ", sem consultas encerradas ainda")
    Column(Modifier.clearAndSetSemantics { contentDescription = spoken }, verticalArrangement = Arrangement.spacedBy(2.dp)) {
        if (hasHistory) {
            Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("$points", style = MaterialTheme.typography.displaySmall, fontWeight = FontWeight.ExtraBold, color = bandColor(band))
                Text("de $SCORE_MAX_POINTS", style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(bottom = 6.dp))
            }
        } else {
            Text(attendancePointsText(points, false), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.ExtraBold,
                color = bandColor(band))
        }
        Text("Faixa ${band.label}", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, color = bandColor(band))
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
            Text("${attendancePointsText(value.points, value.hasHistory)} · ${value.band.label}", fontWeight = FontWeight.Bold,
                color = bandColor(value.band))
            MutedText("Concluídas: ${value.completed} · Faltas: ${value.missed} · Cancelamentos seus: ${value.cancelledByPatient}")
        }
    }
}
