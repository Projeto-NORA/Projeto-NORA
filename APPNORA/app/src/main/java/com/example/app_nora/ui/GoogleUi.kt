package com.example.app_nora.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.GoogleIdResult
import com.example.app_nora.api.PatientState
import com.example.app_nora.api.ProgressSteps
import com.example.app_nora.api.googleSettingsAvailable
import kotlinx.coroutines.launch

/**
 * "Continuar com Google" do login. Só existe com o client ID web no build ([GoogleActions.webClientId]); abre a lista de contas e entra
 * com o ID token. Mostra os passos (abrindo o Google, entrando no NORA) com o indicador na cor do app; o motivo de uma falha fica na
 * mensagem do formulário, não repetido aqui.
 */
@Composable
internal fun LoginGoogleSection(patient: PatientState, google: GoogleActions, onAddressMissing: () -> Unit) {
    var steps by remember { mutableStateOf(ProgressSteps()) }
    var running by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        HorizontalDivider(Modifier.weight(1f), color = MaterialTheme.colorScheme.outlineVariant)
        Text("ou", color = MaterialTheme.colorScheme.onSurfaceVariant, style = MaterialTheme.typography.bodySmall)
        HorizontalDivider(Modifier.weight(1f), color = MaterialTheme.colorScheme.outlineVariant)
    }
    OutlinedButton(onClick = {
        if (patient.address.isBlank()) onAddressMissing()
        else scope.launch {
            running = true
            steps = ProgressSteps().begin("Abrindo o Google…")
            when (val result = google.requestIdToken()) {
                GoogleIdResult.Cancelled -> steps = ProgressSteps()
                is GoogleIdResult.Failed -> { steps = steps.fail("Abrindo o Google…"); google.onFailed(result.message) }
                is GoogleIdResult.Token -> {
                    steps = steps.begin("Entrando no NORA…")
                    steps = if (google.onSignIn(result.idToken)) steps.finish() else steps.fail("Entrando no NORA…")
                }
            }
            running = false
        }
    }, enabled = !patient.busy && !running, modifier = Modifier.fillMaxWidth().height(52.dp),
        shape = RoundedCornerShape(16.dp),
        colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.primary),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant)) {
        Text("G", color = Color(0xFF4285F4), fontWeight = FontWeight.ExtraBold, style = MaterialTheme.typography.titleLarge)
        Spacer(Modifier.width(10.dp))
        Text("Continuar com Google", fontWeight = FontWeight.SemiBold)
    }
    StepList(steps)
}

/** O cartão aparece com sessão e client ID no build, e só se o servidor diz `enabled` ou se a leitura da situação falhou (para tentar de novo). */
internal fun googleCardVisible(patient: PatientState, google: GoogleActions): Boolean =
    patient.session != null && google.webClientId.isNotBlank() &&
        (googleSettingsAvailable(google.webClientId, patient.google.status) || patient.google.error != null)

/**
 * Cartão "Conta Google" das Configurações: aparece só com o client ID web no build **e** o servidor dizendo `enabled`; se a leitura da
 * situação falhou, mostra a falha com "Tentar de novo". Vincular abre a lista de contas; desvincular pede confirmação.
 */
@Composable
internal fun GoogleAccountCard(patient: PatientState, google: GoogleActions) {
    if (!googleCardVisible(patient, google)) return
    val status = patient.google.status
    if (!googleSettingsAvailable(google.webClientId, status)) {
        SectionCard("Conta Google", "Entrar no NORA com o Google") {
            Text(patient.google.error.orEmpty(), color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
            TextButton(onClick = google.onLoadStatus) { Text("Tentar de novo") }
        }
        return
    }
    var steps by remember { mutableStateOf(ProgressSteps()) }
    var running by remember { mutableStateOf(false) }
    var confirmUnlink by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val linked = status?.linked == true
    SectionCard("Conta Google", "Entrar no NORA com o Google") {
        MutedText(if (linked) "Vinculada${status?.email?.let { ": $it" } ?: ""}. Você pode entrar com o Google ou com o CPF e a senha."
            else "Nenhuma conta Google vinculada. Vincule uma para entrar sem digitar a senha.")
        if (linked) OutlinedButton(onClick = { confirmUnlink = true }, enabled = !patient.busy && !running, modifier = Modifier.fillMaxWidth()) {
            Text("Desvincular conta Google")
        } else OutlinedButton(onClick = {
            scope.launch {
                running = true
                steps = ProgressSteps().begin("Abrindo o Google…")
                when (val result = google.requestIdToken()) {
                    GoogleIdResult.Cancelled -> steps = ProgressSteps()
                    is GoogleIdResult.Failed -> { steps = steps.fail("Abrindo o Google…"); google.onFailed(result.message) }
                    is GoogleIdResult.Token -> {
                        steps = steps.begin("Vinculando a conta Google…")
                        steps = if (google.onLink(result.idToken)) steps.finish() else steps.fail("Vinculando a conta Google…")
                    }
                }
                running = false
            }
        }, enabled = !patient.busy && !running, modifier = Modifier.fillMaxWidth()) { Text("Vincular conta Google") }
        StepList(steps)
        patient.message?.let { Text(it, color = if (steps.failed) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
            style = MaterialTheme.typography.bodyMedium) }
    }
    if (confirmUnlink) AlertDialog(
        onDismissRequest = { confirmUnlink = false },
        containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Desvincular o Google?") },
        text = { Text("Você deixa de entrar com o Google. O acesso com CPF ou e-mail e senha continua funcionando e você pode vincular de novo quando quiser.") },
        confirmButton = { TextButton(onClick = {
            confirmUnlink = false
            scope.launch {
                running = true
                steps = ProgressSteps().begin("Desvinculando a conta Google…")
                steps = if (google.onUnlink()) steps.finish() else steps.fail("Desvinculando a conta Google…")
                running = false
            }
        }) { Text("Desvincular", color = MaterialTheme.colorScheme.error) } },
        dismissButton = { TextButton(onClick = { confirmUnlink = false }) { Text("Voltar") } },
    )
}
