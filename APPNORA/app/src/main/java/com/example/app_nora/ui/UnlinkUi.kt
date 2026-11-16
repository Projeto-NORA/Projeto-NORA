package com.example.app_nora.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

/**
 * Aviso antes de remover o vínculo com uma UBS. [impact] é a prévia do servidor relida agora, com exatamente o que a remoção cancela
 * (nulo enquanto [loading] ou quando a prévia falhou). Confirmar fica bloqueado só enquanto confere, porque a remoção também vale para
 * cadastro desativado.
 */
@Composable
internal fun UnlinkDialog(unitName: String, impact: UnlinkImpact?, loading: Boolean, busy: Boolean,
    onConfirm: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(onDismissRequest = { if (!busy) onDismiss() }, containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Remover esta UBS?") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Você deixa de usar neste aplicativo a unidade $unitName.")
                when {
                    loading -> Text("Conferindo o que será cancelado…")
                    impact == null -> Text("Não foi possível conferir o que será cancelado. A remoção cancela todas as suas consultas " +
                        "futuras e solicitações de fila nesta UBS.", color = MaterialTheme.colorScheme.error)
                    impact.appointments == 0 && impact.queueEntries == 0 ->
                        Text("Você não tem consultas futuras nem solicitações de fila nesta UBS.")
                    else -> Text("Serão canceladas agora: ${countLabel(impact.appointments, "consulta futura", "consultas futuras")} " +
                        "e ${countLabel(impact.queueEntries, "solicitação de fila", "solicitações de fila")}.")
                }
                Text("Esses cancelamentos contam na sua participação conforme a antecedência de cada consulta.")
                Text("Para voltar, será preciso procurar a unidade ou vincular a UBS de novo.")
            }
        },
        confirmButton = { TextButton(onClick = onConfirm, enabled = !loading && !busy) {
            Text("Remover e cancelar", color = MaterialTheme.colorScheme.error) } },
        dismissButton = { TextButton(onClick = onDismiss, enabled = !busy) { Text("Voltar") } })
}
