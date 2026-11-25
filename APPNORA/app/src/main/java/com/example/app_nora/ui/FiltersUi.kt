package com.example.app_nora.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsHoveredAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import java.text.Normalizer

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun NoraFilters(activeCount: Int, onClear: () -> Unit, content: @Composable ColumnScope.() -> Unit) {
    var open by remember { mutableStateOf(false) }
    val interaction = remember { MutableInteractionSource() }
    val hovered by interaction.collectIsHoveredAsState()
    val colors = MaterialTheme.colorScheme
    FilledTonalButton(onClick = { open = true }, interactionSource = interaction,
        contentPadding = PaddingValues(horizontal = 12.dp),
        colors = ButtonDefaults.filledTonalButtonColors(
            containerColor = if (hovered || activeCount > 0) colors.primaryContainer else colors.surfaceVariant,
            contentColor = if (hovered || activeCount > 0) colors.onPrimaryContainer else colors.onSurface,
        )) {
        val ink = LocalContentColor.current
        Canvas(Modifier.size(17.dp)) {
            drawPath(Path().apply {
                moveTo(size.width * .06f, size.height * .13f)
                lineTo(size.width * .94f, size.height * .13f)
                lineTo(size.width * .62f, size.height * .52f)
                lineTo(size.width * .62f, size.height * .87f)
                lineTo(size.width * .38f, size.height * .72f)
                lineTo(size.width * .38f, size.height * .52f)
                close()
            }, ink)
        }
        Spacer(Modifier.width(6.dp))
        Text("Filtros", fontWeight = FontWeight.SemiBold)
        if (activeCount > 0) {
            Spacer(Modifier.width(6.dp))
            Badge { Text(activeCount.toString()) }
        }
    }
    if (open) {
        ModalBottomSheet(onDismissRequest = { open = false }) {
            Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text("Filtros", Modifier.weight(1f), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
                    TextButton(onClick = onClear, enabled = activeCount > 0) { Text("Limpar filtros") }
                }
                Column(Modifier.fillMaxWidth().heightIn(max = 380.dp).verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(12.dp), content = content)
                Button(onClick = { open = false }, modifier = Modifier.fillMaxWidth().padding(bottom = 12.dp)) {
                    Text("Ver resultados")
                }
            }
        }
    }
}

@Composable
internal fun <T> FilterChoices(label: String, options: List<T>, selected: T, describe: (T) -> String, onSelect: (T) -> Unit) {
    Column(Modifier.fillMaxWidth().selectableGroup()) {
        Text(label, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
        options.forEach { option ->
            Row(Modifier.fillMaxWidth().heightIn(min = 48.dp)
                .selectable(option == selected, role = Role.RadioButton, onClick = { onSelect(option) }),
                verticalAlignment = Alignment.CenterVertically) {
                RadioButton(option == selected, onClick = null, modifier = Modifier.padding(end = 12.dp))
                Text(describe(option), style = MaterialTheme.typography.bodyLarge)
            }
        }
    }
}

@Composable
internal fun FilterSearch(query: String, onQuery: (String) -> Unit, label: String) {
    val focus = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    OutlinedTextField(query, onQuery, label = { Text(label) }, singleLine = true, modifier = Modifier.fillMaxWidth(),
        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
        keyboardActions = KeyboardActions(onDone = { focus.clearFocus(); keyboard?.hide() }))
}

private val searchDiacritics = Regex("\\p{M}+")

internal fun String.normalizedSearchText(): String =
    Normalizer.normalize(trim(), Normalizer.Form.NFD).replace(searchDiacritics, "").lowercase()

internal enum class ListOrder(val label: String) {
    ORIGINAL("Ordem original"),
    LATEST("Mais recentes"),
    EARLIEST("Mais antigas"),
}
