package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*
import kotlinx.coroutines.launch
import org.json.JSONException
import java.io.IOException

private const val MIN_PASSWORD = 8
private const val MAX_PASSWORD = 256

/**
 * Troca de senha com a sessão aberta. As três senhas ficam em `remember` (nunca `rememberSaveable`): girar a tela
 * apaga o que foi digitado em vez de guardá-lo em estado salvo. [required] avisa que o servidor exigiu a troca.
 */
@Composable
fun ChangePasswordScreen(required: Boolean, onSubmit: suspend (String, String) -> Unit) {
    var current by remember { mutableStateOf("") }
    var next by remember { mutableStateOf("") }
    var confirmation by remember { mutableStateOf("") }
    var showCurrent by rememberSaveable { mutableStateOf(false) }
    var showNext by rememberSaveable { mutableStateOf(false) }
    var showConfirmation by rememberSaveable { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    var currentError by remember { mutableStateOf<String?>(null) }
    var formError by remember { mutableStateOf<String?>(null) }
    var success by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    // As regras são conferidas enquanto a pessoa digita; o aviso só aparece depois que o campo tem algo.
    val nextError = when {
        next.isEmpty() -> null
        next.length < MIN_PASSWORD -> "Use pelo menos $MIN_PASSWORD caracteres."
        next == current -> "A nova senha precisa ser diferente da atual."
        else -> null
    }
    val confirmationError = if (confirmation.isNotEmpty() && confirmation != next) "As senhas não correspondem." else null
    fun options(last: Boolean) = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = if (last) ImeAction.Done else ImeAction.Next)
    fun transformation(visible: Boolean) = if (visible) VisualTransformation.None else PasswordVisualTransformation()
    val submit: () -> Unit = {
        success = false
        when {
            current.isEmpty() -> currentError = "Informe a senha atual."
            // Estas regras já aparecem sob cada campo enquanto a pessoa digita; aqui só impedem o envio, sem repetir o aviso.
            next.length < MIN_PASSWORD || next == current || next != confirmation -> Unit
            else -> {
                busy = true; currentError = null; formError = null
                scope.launch {
                    try {
                        onSubmit(current, next)
                        current = ""; next = ""; confirmation = ""
                        success = true
                    }
                    catch (failure: ApiFailure) {
                        if (failure.code == INVALID_CURRENT_PASSWORD) currentError = failure.message else formError = failure.message
                    }
                    catch (_: IOException) { formError = "Não foi possível conectar ao serviço. Confira sua conexão e tente novamente." }
                    catch (_: JSONException) { formError = INCOMPATIBLE_RESPONSE_MESSAGE }
                    catch (invalid: IllegalArgumentException) { formError = invalid.message ?: "Confira o endereço do serviço." }
                    finally { busy = false }
                }
            }
        }
    }
    ScreenTitle("Alterar senha")
    if (required) Surface(shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.primaryContainer,
        modifier = Modifier.fillMaxWidth().semantics { liveRegion = LiveRegionMode.Polite }) {
        Text("É preciso definir uma nova senha para continuar usando o aplicativo.", Modifier.padding(14.dp),
            style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onPrimaryContainer)
    }
    SectionCard("Sua senha", "Mínimo de $MIN_PASSWORD caracteres, diferente da atual") {
        OutlinedTextField(value = current, onValueChange = { current = it.take(MAX_PASSWORD); currentError = null; success = false },
            label = { Text(if (required) "Senha atual (recebida da UBS)" else "Senha atual") }, singleLine = true, enabled = !busy, modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(16.dp), isError = currentError != null,
            supportingText = currentError?.let { message -> { Text(message) } },
            visualTransformation = transformation(showCurrent), keyboardOptions = options(false),
            trailingIcon = { PasswordEyeButton(showCurrent) { showCurrent = !showCurrent } })
        OutlinedTextField(value = next, onValueChange = { next = it.take(MAX_PASSWORD); formError = null; success = false },
            label = { Text("Nova senha") }, singleLine = true, enabled = !busy, modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(16.dp), isError = nextError != null,
            supportingText = nextError?.let { message -> { Text(message) } },
            visualTransformation = transformation(showNext), keyboardOptions = options(false),
            trailingIcon = { PasswordEyeButton(showNext) { showNext = !showNext } })
        OutlinedTextField(value = confirmation, onValueChange = { confirmation = it.take(MAX_PASSWORD); formError = null; success = false },
            label = { Text("Confirmar nova senha") }, singleLine = true, enabled = !busy, modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(16.dp), isError = confirmationError != null,
            supportingText = confirmationError?.let { message -> { Text(message) } },
            visualTransformation = transformation(showConfirmation), keyboardOptions = options(true),
            trailingIcon = { PasswordEyeButton(showConfirmation) { showConfirmation = !showConfirmation } })
        formError?.let { ErrorNotice(it) }
        if (success) ActionMessage("Senha alterada. Os outros aparelhos foram desconectados; você continua conectado neste.")
        Button(onClick = submit, enabled = !busy && current.isNotEmpty() && next.isNotEmpty() && confirmation.isNotEmpty(),
            modifier = Modifier.fillMaxWidth()) {
            Text(if (busy) "Alterando…" else "Alterar senha", fontWeight = FontWeight.Bold)
        }
    }
}
