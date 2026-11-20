package com.example.app_nora.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.size
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
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.withTransform
import androidx.compose.ui.graphics.vector.PathParser
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
        GoogleLogo(Modifier.size(20.dp))
        Spacer(Modifier.width(10.dp))
        Text("Continuar com Google", fontWeight = FontWeight.SemiBold)
    }
    StepList(steps)
}

/** O cartão aparece com sessão e client ID no build, e só se o servidor diz `enabled` ou se a leitura da situação falhou (para tentar de novo). */
internal fun googleCardVisible(patient: PatientState, google: GoogleActions): Boolean =
    patient.session != null && google.webClientId.isNotBlank() &&
        (googleSettingsAvailable(google.webClientId, patient.google.status) || patient.google.error != null)

/** O "G" colorido oficial do Google (traçado de 48 x 48 das diretrizes de marca), desenhado em vetor. */
@Composable
private fun GoogleLogo(modifier: Modifier) {
    val parts = remember {
        listOf(
            Color(0xFF4285F4) to "M45.12 24.5c0-1.56-.14-3.06-.4-4.5H24v8.51h11.84c-.51 2.75-2.06 5.08-4.39 6.64v5.52h7.11c4.16-3.83 6.56-9.47 6.56-16.17z",
            Color(0xFF34A853) to "M24 46c5.94 0 10.92-1.97 14.56-5.33l-7.11-5.52c-1.97 1.32-4.49 2.1-7.45 2.1-5.73 0-10.58-3.87-12.31-9.07H4.34v5.7C7.96 41.07 15.4 46 24 46z",
            Color(0xFFFBBC05) to "M11.69 28.18C11.25 26.86 11 25.45 11 24s.25-2.86.69-4.18v-5.7H4.34A21.99 21.99 0 0 0 2 24c0 3.55.85 6.91 2.34 9.88l7.35-5.7z",
            Color(0xFFEA4335) to "M24 10.75c3.23 0 6.13 1.11 8.41 3.29l6.31-6.31C34.91 4.18 29.93 2 24 2 15.4 2 7.96 6.93 4.34 14.12l7.35 5.7c1.73-5.2 6.58-9.07 12.31-9.07z",
        ).map { (color, data) -> color to PathParser().parsePathString(data).toPath() }
    }
    Canvas(modifier) {
        val scale = size.minDimension / 48f
        withTransform({ scale(scale, scale, pivot = Offset.Zero) }) {
            parts.forEach { (color, path) -> drawPath(path, color) }
        }
    }
}

/**
 * Cartão "Conta" das Configurações: reúne as formas de entrar (a conta Google, quando disponível) e as ações da conta que o
 * [footer] traz (alterar senha, sair).
 */
@Composable
internal fun AccountCard(patient: PatientState, google: GoogleActions, footer: @Composable ColumnScope.() -> Unit) {
    SectionCard("Conta") {
        if (googleCardVisible(patient, google)) {
            Text("Conta Google", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
            GoogleAccountBody(patient, google)
        }
        footer()
    }
}

@Composable
private fun ColumnScope.GoogleAccountBody(patient: PatientState, google: GoogleActions) {
    val status = patient.google.status
    if (!googleSettingsAvailable(google.webClientId, status)) {
        ErrorNotice(patient.google.error.orEmpty())
        RetryButton(onClick = google.onLoadStatus)
        return
    }
    var steps by remember { mutableStateOf(ProgressSteps()) }
    var running by remember { mutableStateOf(false) }
    var confirmUnlink by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val linked = status?.linked == true
    MutedText(if (linked) "Vinculada${status?.email?.let { ": $it" } ?: ""}."
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
    ActionMessage(patient.message)

    if (confirmUnlink) AlertDialog(
        onDismissRequest = { confirmUnlink = false },
        containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Desvincular o Google?") },
        text = { Text("Você deixa de entrar com o Google. O acesso com CPF ou e-mail e senha continua funcionando.") },
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
