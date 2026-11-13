package com.example.app_nora.ui

import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.text.font.FontWeight
import com.example.app_nora.api.*

/**
 * Solicitações de atendimento da UBS: ativas primeiro; o histórico fica recolhido até o paciente abrir.
 * Nas ativas, "Sair da fila" pede confirmação antes de chamar [onLeave].
 */
@Composable
internal fun QueueEntries(queue: List<UnitQueueEntry>, busy: Boolean = false, onLeave: ((Long) -> Unit)? = null) {
    val groups = remember(queue) { groupQueue(queue) }
    var historyOpen by rememberSaveable { mutableStateOf(false) }
    var leaving by remember { mutableStateOf<UnitQueueEntry?>(null) }
    if (groups.active.isEmpty()) MutedText("Nenhuma solicitação ativa nesta UBS.")
    groups.active.forEach { entry ->
        QueueRow(entry)
        if (onLeave != null) TextButton(onClick = { leaving = entry }, enabled = !busy) {
            Text("Sair da fila", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.SemiBold)
        }
    }
    if (groups.history.isNotEmpty()) {
        HistoryToggle(groups.history.size, historyOpen) { historyOpen = !historyOpen }
        if (historyOpen) groups.history.forEach { QueueRow(it) }
    }
    leaving?.let { entry ->
        LeaveQueueDialog(entry, onConfirm = { leaving = null; onLeave?.invoke(entry.id) }, onDismiss = { leaving = null })
    }
}

@Composable
private fun QueueRow(entry: UnitQueueEntry) {
    Text(entry.specialty, fontWeight = FontWeight.SemiBold)
    Text(statusLabel(entry.status), fontWeight = FontWeight.SemiBold)
    MutedText("Registrada em ${formatDateTime(entry.enteredAt)}")
}

/** Confirmação de saída da fila: diz o efeito antes de agir; o botão destrutivo nunca é o padrão. */
@Composable
internal fun LeaveQueueDialog(entry: UnitQueueEntry, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(onDismissRequest = onDismiss, containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Sair da fila?") },
        text = { Text("Você perde a posição na fila de ${entry.specialty} nesta UBS. Se houver uma oferta de vaga pendente " +
            "desta solicitação, ela também será cancelada.") },
        confirmButton = { TextButton(onClick = onConfirm) { Text("Sair da fila", color = MaterialTheme.colorScheme.error) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Continuar na fila") } })
}
