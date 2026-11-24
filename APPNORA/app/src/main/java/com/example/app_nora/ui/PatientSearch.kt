package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import java.text.Normalizer
import java.util.Locale

internal data class SearchDestination(val title: String, val page: String, val terms: String)

internal val searchDestinations = listOf(
    SearchDestination("Consultas", "APPOINTMENTS", "consulta cancelar confirmar presença agenda"),
    SearchDestination("Agendar consulta", "BOOKING", "agendamento marcar consulta horário especialidade"),
    SearchDestination("UBS", "UNITS", "unidade saúde vincular remover endereço atendimento"),
    SearchDestination("Vaga Relâmpago", "FLASH", "vaga oportunidade oferta fila"),
    SearchDestination("Jornada", "JOURNEY", "score participação comparecimento exames vacinas fila"),
    SearchDestination("Avisos", "NOTICES", "notificação comunicado mensagem"),
    SearchDestination("Família", "FAMILY", "dependente responsável vínculo"),
    SearchDestination("Perfil", "PROFILE", "conta dados email e-mail"),
    SearchDestination("Configurações", "SETTINGS", "configuração tema aparência notificações"),
    SearchDestination("Alterar senha", "PASSWORD", "trocar mudar senha"),
)

internal fun searchPages(query: String): List<SearchDestination> {
    fun normalize(value: String) = Normalizer.normalize(value, Normalizer.Form.NFD)
        .replace(Regex("\\p{M}+"), "").lowercase(Locale.ROOT)
    val words = normalize(query).trim().split(Regex("\\s+")).filter { it.isNotEmpty() }
    if (words.isEmpty()) return emptyList()
    return searchDestinations.filter { item ->
        val text = normalize("${item.title} ${item.terms}")
        words.all { it in text }
    }
}

@Composable
internal fun PatientSearchDialog(onDismiss: () -> Unit, onOpen: (String) -> Unit) {
    var query by rememberSaveable { mutableStateOf("") }
    val results = remember(query) { searchPages(query) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Pesquisar") },
        text = {
            Column {
                OutlinedTextField(
                    value = query, onValueChange = { query = it },
                    label = { Text("O que você procura?") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = { results.firstOrNull()?.let { onOpen(it.page) } }),
                    modifier = Modifier.fillMaxWidth(),
                )
                if (query.isNotBlank() && results.isEmpty()) Text("Nenhum resultado.", Modifier.padding(top = 12.dp))
                Column(Modifier.heightIn(max = 320.dp).verticalScroll(rememberScrollState())) {
                    results.forEach { result ->
                        TextButton(onClick = { onOpen(result.page) }, modifier = Modifier.fillMaxWidth()) {
                            Text(result.title, modifier = Modifier.fillMaxWidth())
                        }
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = { results.firstOrNull()?.let { onOpen(it.page) } }, enabled = results.isNotEmpty()) { Text("Pesquisar") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Fechar") } },
    )
}
