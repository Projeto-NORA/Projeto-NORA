package com.example.app_nora.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*
import kotlinx.coroutines.launch

@Composable
fun ProfileScreen(patient: PatientState, onOpenSettings: () -> Unit, onChangePassword: () -> Unit, onLogout: () -> Unit,
    email: EmailActions = EmailActions()) {
    ScreenTitle("Perfil", "Seus dados e preferências do aplicativo.")
    ActionMessage(patient.message)
    EmailSuggestionCard(patient, email)
    SectionCard("Minha conta", "Identificação conectada") {
        Text(patient.session?.user?.fullName ?: "Nenhuma conta conectada", fontWeight = FontWeight.SemiBold)
        MutedText("CPF e e-mail não são retornados pelo serviço conectado.")
        OutlinedButton(onClick = onOpenSettings, modifier = Modifier.fillMaxWidth()) {
            Text("Configurações")
        }
        if (patient.session != null) OutlinedButton(onClick = onChangePassword, modifier = Modifier.fillMaxWidth()) {
            Text("Alterar senha")
        }
        OutlinedButton(onClick = onLogout, enabled = patient.session != null && !patient.busy,
            modifier = Modifier.fillMaxWidth().height(52.dp),
            colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.error.copy(alpha = .55f))) {
            Text("Sair da conta", fontWeight = FontWeight.SemiBold)
        }
    }
}

/**
 * A UBS corrigiu o e-mail de referência: oferece trocar o e-mail da conta. O código é enviado ao endereço novo (mostrado mascarado) e
 * só depois de confirmado o e-mail da conta muda; também dá para dispensar. O código digitado fica só nesta tela, em memória.
 */
@Composable
internal fun EmailSuggestionCard(patient: PatientState, actions: EmailActions) {
    val suggestion = patient.email.suggestion ?: return
    val codeSent = patient.email.codeSentFor == suggestion.id
    var code by remember(suggestion.id) { mutableStateOf("") }
    val scope = rememberCoroutineScope()
    SectionCard("E-mail da conta", "Sugestão da UBS", highlight = true) {
        Text("A UBS ${suggestion.unitName} registrou outro e-mail para você.", fontWeight = FontWeight.SemiBold)
        Text("E-mail novo: ${suggestion.maskedEmail}")
        MutedText("Para trocar, enviamos um código de 6 dígitos a esse endereço. O e-mail da conta só muda depois que você digitar o código, e " +
            "o login passa a ser por ele. Se não reconhece essa mudança, dispense.")
        if (!codeSent) {
            Button(onClick = actions.onSendCode, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Trocar e-mail") }
        } else {
            OutlinedTextField(value = code, onValueChange = { code = it.filter { digit -> digit in '0'..'9' }.take(6) },
                label = { Text("Código de 6 dígitos") }, singleLine = true, enabled = !patient.busy, modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Done))
            Button(onClick = { scope.launch { if (actions.onConfirm(code)) code = "" } }, enabled = !patient.busy && code.length == 6,
                modifier = Modifier.fillMaxWidth()) { Text(if (patient.busy) "Confirmando…" else "Confirmar código") }
            OutlinedButton(onClick = actions.onSendCode, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Enviar outro código") }
            MutedText("O código vale por 10 minutos e o envio é limitado a 3 por sugestão.")
        }
        TextButton(onClick = actions.onDismiss, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Agora não, manter meu e-mail") }
    }
}

/** Aviso discreto no Início: leva ao Perfil, onde a troca acontece. */
@Composable
internal fun EmailSuggestionNotice(patient: PatientState, onOpenProfile: () -> Unit) {
    val suggestion = patient.email.suggestion ?: return
    Surface(shape = RoundedCornerShape(18.dp), color = MaterialTheme.colorScheme.secondaryContainer) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 8.dp), verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween) {
            Text("A UBS ${suggestion.unitName} registrou outro e-mail para você.", Modifier.weight(1f), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSecondaryContainer)
            TextButton(onClick = onOpenProfile) { Text("Ver no Perfil", fontWeight = FontWeight.Bold) }
        }
    }
}
