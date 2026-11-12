package com.example.app_nora.ui

import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.text.font.FontWeight
import com.example.app_nora.api.*

/** Solicitações de atendimento da UBS: ativas primeiro; o histórico fica recolhido até o paciente abrir. */
@Composable
internal fun QueueEntries(queue: List<UnitQueueEntry>) {
    val groups = remember(queue) { groupQueue(queue) }
    var historyOpen by rememberSaveable { mutableStateOf(false) }
    if (groups.active.isEmpty()) MutedText("Nenhuma solicitação ativa nesta UBS.")
    groups.active.forEach { QueueRow(it) }
    if (groups.history.isNotEmpty()) {
        HistoryToggle(groups.history.size, historyOpen) { historyOpen = !historyOpen }
        if (historyOpen) groups.history.forEach { QueueRow(it) }
    }
}

@Composable
private fun QueueRow(entry: UnitQueueEntry) {
    Text(entry.specialty, fontWeight = FontWeight.SemiBold)
    Text(statusLabel(entry.status), fontWeight = FontWeight.SemiBold)
    MutedText("Registrada em ${formatDateTime(entry.enteredAt)}")
}
