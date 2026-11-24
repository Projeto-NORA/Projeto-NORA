package com.example.app_nora.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*
import kotlinx.coroutines.launch

/**
 * Modo Família: pede o vínculo com um dependente (CPF e UBS onde ele tem cadastro), acompanha os pedidos e, para cada dependente
 * aprovado, deixa escolher por quem o paciente age ("Agindo por"). A equipe da UBS confere os documentos e decide; o CPF do dependente
 * só trafega no corpo do pedido e nunca é guardado no aparelho.
 */
@Composable
fun FamilyScreen(patient: PatientState, actions: FamilyActions = FamilyActions(),
    onRegistrationUnits: suspend () -> List<RegistrationUnit> = { emptyList() }, initialCatalog: List<RegistrationUnit>? = null) {
    ScreenTitle("Família", "Gerencie vínculos com dependentes.")
    if (patient.session == null) {
        SectionCard("Entre na sua conta", "Modo Família") { MutedText("Entre na sua conta para pedir o vínculo com um dependente.") }
        return
    }
    ActionMessage(patient.message)
    val links = patient.family.links
    val groups = remember(links) { groupFamilyLinks(links.orEmpty()) }
    var historyOpen by rememberSaveable { mutableStateOf(false) }
    var ending by remember { mutableStateOf<FamilyLink?>(null) }
    var cancelling by remember { mutableStateOf<FamilyLink?>(null) }

    if (approvedLinks(links).isNotEmpty()) SectionCard("Agindo por", "Escolha por quem você usa o aplicativo") {
        ActingPicker(patient, actions.onActFor, enabled = !patient.busy)
    }

    RequestCard(patient, actions, onRegistrationUnits, initialCatalog)

    SectionCard("Seus pedidos e vínculos", "Situação informada pela UBS") {
        when {
            links == null -> {
                MutedText(patient.family.error ?: if (patient.family.loading) "Carregando seus pedidos…" else "Os pedidos ainda não foram consultados.")
            }
            groups.active.isEmpty() && groups.history.isEmpty() -> MutedText("Você ainda não pediu o vínculo com nenhum dependente.")
            groups.active.isEmpty() -> MutedText("Nenhum pedido ativo. O histórico está abaixo.")
        }
        if (links != null && patient.family.error != null) MutedText(patient.family.error)
        if (patient.family.loading) LinearProgressIndicator(Modifier.fillMaxWidth())
        OutlinedButton(onClick = actions.onLoad, enabled = !patient.family.loading, modifier = Modifier.fillMaxWidth()) {
            Text(if (patient.family.loading) "Atualizando…" else "Atualizar pedidos")
        }
    }
    groups.active.forEach { link ->
        key(link.id) { LinkCard(link, patient, onAct = actions.onActFor, onCancel = { cancelling = link }, onEnd = { ending = link }) }
    }
    if (groups.history.isNotEmpty()) {
        HistoryToggle(groups.history.size, historyOpen) { historyOpen = !historyOpen }
        if (historyOpen) groups.history.forEach { link -> key(link.id) { LinkCard(link, patient, {}, {}, {}) } }
    }
    MutedText("A UBS confere os documentos antes de liberar os dados.")

    cancelling?.let { link ->
        AlertDialog(onDismissRequest = { cancelling = null }, containerColor = MaterialTheme.colorScheme.surface,
            title = { Text("Cancelar o pedido?") },
            text = { Text("Cancelar o pedido de ${dependentDisplayName(link)} na ${link.unitName}?") },
            confirmButton = { TextButton(onClick = { cancelling = null; actions.onCancelRequest(link.id) }) { Text("Cancelar pedido", color = MaterialTheme.colorScheme.error) } },
            dismissButton = { TextButton(onClick = { cancelling = null }) { Text("Manter pedido") } })
    }
    ending?.let { link ->
        AlertDialog(onDismissRequest = { ending = null }, containerColor = MaterialTheme.colorScheme.surface,
            title = { Text("Encerrar o vínculo?") },
            text = { Text("Você perderá o acesso aos dados de ${dependentDisplayName(link)} na ${link.unitName}. Para voltar, solicite nova aprovação.") },
            confirmButton = { TextButton(onClick = { ending = null; actions.onEnd(link.id) }) { Text("Encerrar vínculo", color = MaterialTheme.colorScheme.error) } },
            dismissButton = { TextButton(onClick = { ending = null }) { Text("Manter vínculo") } })
    }
}

@Composable
private fun RequestCard(patient: PatientState, actions: FamilyActions, onRegistrationUnits: suspend () -> List<RegistrationUnit>,
    initialCatalog: List<RegistrationUnit>?) {
    // O CPF do dependente fica só nesta tela, em memória: não entra em estado salvo nem em disco.
    var cpf by remember { mutableStateOf("") }
    var unitId by remember { mutableStateOf<String?>(null) }
    var catalog by remember { mutableStateOf(initialCatalog) }
    var catalogLoading by remember { mutableStateOf(false) }
    var catalogFailed by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    suspend fun loadCatalog() {
        if (catalogLoading) return
        catalogLoading = true; catalogFailed = false
        try { catalog = onRegistrationUnits() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (_: Exception) { catalogFailed = true }
        finally { catalogLoading = false }
    }
    LaunchedEffect(patient.session?.token) { if (catalog == null) loadCatalog() }
    val digits = cpfDigits(cpf)
    val cpfWrong = digits.length == 11 && !isValidCpf(digits)
    SectionCard("Pedir vínculo com um dependente", "A equipe da UBS confere os documentos") {
        MutedText("Informe o CPF da pessoa e a UBS onde ela tem cadastro. A UBS decide a idade e os documentos necessários.")
        OutlinedTextField(value = cpf, onValueChange = { cpf = it.filter { char -> char in '0'..'9' || char in ".- " }.take(14) },
            label = { Text("CPF do dependente") }, placeholder = { Text("000.000.000-00") }, singleLine = true,
            enabled = !patient.busy, isError = cpfWrong, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp),
            supportingText = { Text(if (cpfWrong) "Confira o CPF informado: os dígitos verificadores não conferem." else "Só números; pontos e traço são opcionais.") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Done))
        Text("UBS onde a pessoa tem cadastro", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold)
        when {
            catalog == null -> {
                MutedText(if (catalogLoading) "Carregando as UBS…" else if (catalogFailed) "Não foi possível carregar a lista de UBS." else "A lista de UBS ainda não foi carregada.")
                if (!catalogLoading) OutlinedButton(onClick = { scope.launch { loadCatalog() } }, modifier = Modifier.fillMaxWidth()) { Text("Tentar novamente") }
            }
            catalog!!.isEmpty() -> MutedText("Nenhuma UBS disponível no momento.")
            else -> catalog!!.forEach { unit ->
                Row(Modifier.fillMaxWidth().selectable(selected = unit.id == unitId, enabled = !patient.busy, onClick = { unitId = unit.id }, role = Role.RadioButton),
                    verticalAlignment = Alignment.CenterVertically) {
                    RadioButton(selected = unit.id == unitId, onClick = null)
                    Text("${unit.name} · ${unit.region}", Modifier.padding(start = 8.dp))
                }
            }
        }
        Button(onClick = {
            val chosen = unitId ?: return@Button
            scope.launch { if (actions.onRequest(digits, chosen)) { cpf = ""; unitId = null } }
        }, enabled = !patient.busy && isValidCpf(digits) && unitId != null, modifier = Modifier.fillMaxWidth()) {
            Text(if (patient.busy) "Enviando…" else "Pedir vínculo")
        }
    }
}

@Composable
private fun LinkCard(link: FamilyLink, patient: PatientState, onAct: (String?) -> Unit, onCancel: () -> Unit, onEnd: () -> Unit) {
    SectionCard(dependentDisplayName(link), link.unitName) {
        Text(familyStatusLabel(link.status), fontWeight = FontWeight.SemiBold, style = MaterialTheme.typography.titleSmall,
            color = if (link.isApproved()) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface)
        MutedText("Pedido enviado em ${formatDate(link.requestedAt)}.")
        when (link.status) {
            "pending" -> {
                MutedText("A equipe da UBS ainda vai conferir os documentos. Nenhum dado da pessoa aparece até a aprovação.")
                OutlinedButton(onClick = onCancel, enabled = !patient.busy, modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
                    border = BorderStroke(1.dp, MaterialTheme.colorScheme.error.copy(alpha = .55f))) { Text("Cancelar pedido") }
            }
            "approved" -> {
                link.decidedAt?.let { MutedText("Aprovado em ${formatDate(it)}.") }
                if (patient.actingFor == link.id) Text("Você está agindo por esta pessoa agora.", fontWeight = FontWeight.SemiBold)
                else Button(onClick = { onAct(link.id) }, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Agir por ${dependentDisplayName(link)}") }
                OutlinedButton(onClick = onEnd, enabled = !patient.busy, modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
                    border = BorderStroke(1.dp, MaterialTheme.colorScheme.error.copy(alpha = .55f))) { Text("Encerrar vínculo") }
            }
            "rejected" -> {
                link.decidedAt?.let { MutedText("Recusado em ${formatDate(it)}.") }
                // O motivo é texto da equipe da UBS (até 280 caracteres), mostrado como texto puro.
                Text(link.decisionReason?.takeIf { it.isNotBlank() }?.let { "Motivo: $it" } ?: "A equipe não informou o motivo. Procure a UBS.",
                    style = MaterialTheme.typography.bodyMedium)
            }
            else -> link.closedAt?.let { MutedText("Encerrado em ${formatDate(it)}.") }
        }
    }
}

/** Lista de pessoas pelas quais o paciente pode agir: ele mesmo e cada dependente aprovado; uma só escolha. */
@Composable
internal fun ActingPicker(patient: PatientState, onActFor: (String?) -> Unit, enabled: Boolean) {
    val acting = patient.actingFor
    ActingRow("Você", acting == null, enabled) { onActFor(null) }
    approvedLinks(patient.family.links).forEach { link ->
        key(link.id) { ActingRow(familyLinkLabel(link), acting == link.id, enabled) { onActFor(link.id) } }
    }
}

@Composable
private fun ActingRow(label: String, selected: Boolean, enabled: Boolean, onClick: () -> Unit) {
    Row(Modifier.fillMaxWidth().selectable(selected = selected, enabled = enabled, onClick = onClick, role = Role.RadioButton),
        verticalAlignment = Alignment.CenterVertically) {
        RadioButton(selected = selected, onClick = null)
        Text(label, Modifier.padding(start = 8.dp), fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal)
    }
}

/**
 * Faixa no alto das telas que agem por alguém (Início, Consultas, Agendamento, Vaga Relâmpago, Jornada): sempre diz por quem o
 * paciente está agindo, em texto, e deixa trocar. Só aparece quando há ao menos um dependente aprovado.
 */
@Composable
fun ActingBanner(patient: PatientState, onActFor: (String?) -> Unit) {
    if (approvedLinks(patient.family.links).isEmpty()) return
    val link = patient.actingLink()
    var picking by remember { mutableStateOf(false) }
    val colors = MaterialTheme.colorScheme
    Surface(shape = RoundedCornerShape(18.dp), color = if (link != null) colors.tertiaryContainer else colors.surfaceVariant,
        border = if (link != null) BorderStroke(2.dp, colors.tertiary) else null) {
        Column(Modifier.fillMaxWidth().padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(if (link == null) "Agindo por: você" else "Agindo por: ${dependentDisplayName(link)}", style = MaterialTheme.typography.titleSmall,
                fontWeight = FontWeight.Bold, color = if (link != null) colors.onTertiaryContainer else colors.onSurface)
            if (link != null) Text("Consultas, agendamento, fila, Vaga Relâmpago e Jornada desta tela são de ${dependentDisplayName(link)} na ${link.unitName}, não suas.",
                style = MaterialTheme.typography.bodySmall, color = colors.onTertiaryContainer)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(onClick = { picking = true }, enabled = !patient.busy) { Text("Trocar") }
                if (link != null) TextButton(onClick = { onActFor(null) }, enabled = !patient.busy) { Text("Voltar a agir por mim") }
            }
        }
    }
    if (picking) AlertDialog(onDismissRequest = { picking = false }, containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Agindo por") },
        text = { Column { ActingPicker(patient, { picking = false; onActFor(it) }, enabled = !patient.busy) } },
        confirmButton = { TextButton(onClick = { picking = false }) { Text("Fechar") } })
}
