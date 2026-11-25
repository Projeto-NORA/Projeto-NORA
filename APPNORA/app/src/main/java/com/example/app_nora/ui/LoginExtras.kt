package com.example.app_nora.ui

import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Email
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Person
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalInspectionMode
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import com.example.app_nora.api.LightningAlerts

/** Ações da foto de perfil: ler, enviar o JPEG já recortado e remover. */
class PhotoActions(val onLoad: () -> Unit = {}, val onUpload: (ByteArray) -> Unit = {}, val onRemove: () -> Unit = {})

private val confirmSteps = listOf(
    Icons.Default.Person to "Preencha os dados já cadastrados pela UBS.",
    Icons.Default.Lock to "Crie sua senha.",
    Icons.Default.Email to "Receba o código no seu e-mail.",
    Icons.Default.CheckCircle to "Digite o código e confirme.",
)

/** Pop-up de "Confirmar login": os 4 passos com ícones e "Ok, entendi", que segue para a tela de confirmação. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun ConfirmLoginSteps(onOk: () -> Unit, onDismiss: () -> Unit) {
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = MaterialTheme.colorScheme.surface) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp).padding(bottom = 28.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Text("Confirmar login", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            Text("Sua conta já foi criada pela UBS. Em 4 passos você confirma o acesso:",
                style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            confirmSteps.forEachIndexed { index, (icon, text) -> StepRow(index + 1, icon, text) }
            Button(onClick = onOk, modifier = Modifier.fillMaxWidth().height(54.dp), shape = RoundedCornerShape(16.dp)) {
                Text("Ok, entendi", fontWeight = FontWeight.Bold)
            }
        }
    }
}

@Composable
private fun StepRow(number: Int, icon: ImageVector, text: String) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(14.dp)) {
        Box(Modifier.size(48.dp).background(MaterialTheme.colorScheme.primaryContainer, CircleShape), contentAlignment = Alignment.Center) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
        }
        Column {
            Text("Passo $number", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.Bold)
            Text(text, style = MaterialTheme.typography.bodyLarge)
        }
    }
}

/** A permissão de tela cheia, relida a cada volta ao app (a pessoa pode tê-la mudado nas configurações do Android). */
@Composable
private fun rememberFullScreenAllowed(): Boolean {
    val context = LocalContext.current
    if (LocalInspectionMode.current) return true
    var allowed by remember { mutableStateOf(LightningAlerts.canUseFullScreen(context)) }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(lifecycle) { lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) { allowed = LightningAlerts.canUseFullScreen(context) } }
    return allowed
}

/**
 * Android 14+: sem a permissão de tela cheia a Vaga Relâmpago vira só notificação. Depois de entrar, explica uma vez e leva às
 * configurações do Android; "Agora não" fica guardado no aparelho (dá para permitir depois em Configurações).
 */
@Composable
internal fun FullScreenAlertPrompt(flashEnabled: Boolean) {
    val context = LocalContext.current
    val allowed = rememberFullScreenAllowed()
    val preferences = remember { context.getSharedPreferences("nora_fullscreen", Context.MODE_PRIVATE) }
    var dismissed by remember { mutableStateOf(preferences.getBoolean("dismissed", false)) }
    if (!flashEnabled || allowed || dismissed) return
    AlertDialog(onDismissRequest = { dismissed = true },
        title = { Text("Alerta de Vaga Relâmpago") },
        text = { Text("Permita alertas em tela cheia para ver a vaga na hora, mesmo com a tela bloqueada.") },
        confirmButton = { TextButton(onClick = {
            dismissed = true
            runCatching { context.startActivity(LightningAlerts.fullScreenSettingsIntent(context)) }
        }) { Text("Permitir") } },
        dismissButton = { TextButton(onClick = { dismissed = true; preferences.edit().putBoolean("dismissed", true).apply() }) { Text("Agora não") } })
}

/** Linha em Configurações > Notificações com a situação do alerta em tela cheia e o atalho para permitir. */
@Composable
internal fun FullScreenAlertSetting() {
    val context = LocalContext.current
    val allowed = rememberFullScreenAllowed()
    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text("Alerta em tela cheia", style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium)
            MutedText(if (allowed) "Permitido: a vaga aparece mesmo com a tela bloqueada." else "Desligado no Android.")
        }
        if (!allowed) TextButton(onClick = { runCatching { context.startActivity(LightningAlerts.fullScreenSettingsIntent(context)) } }) { Text("Permitir") }
    }
}
