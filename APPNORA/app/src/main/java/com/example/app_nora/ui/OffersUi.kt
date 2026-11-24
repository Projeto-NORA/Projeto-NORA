package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalInspectionMode
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*
import kotlinx.coroutines.delay

/** Relógio do aparelho em milissegundos; testes e prévias fornecem um valor fixo. */
val LocalClockMillis = staticCompositionLocalOf<() -> Long> { System::currentTimeMillis }

/**
 * Relógio da contagem regressiva: só corre (um tique por segundo) enquanto houver oferta aberta em [offers].
 * É recriado quando a lista muda, já começando na hora atual, para que nenhuma oferta vencida apareça como aberta.
 */
@Composable
internal fun rememberOfferClock(offers: List<UnitOffer>): State<Long> {
    val clock = LocalClockMillis.current
    val preview = LocalInspectionMode.current
    val now = remember(offers) { mutableLongStateOf(clock()) }
    val running by remember(offers) { derivedStateOf { offers.any { it.isOpenAt(now.longValue) } } }
    LaunchedEffect(now, running) {
        if (running && !preview) while (true) {
            now.longValue = clock()
            delay(1000L)
        }
    }
    return now
}

/** Ofertas abertas de todas as UBS vinculadas, com o relógio que as acompanha. */
internal class OpenOffers(val now: State<Long>, val list: State<List<UnitOfferRef>>)

@Composable
internal fun rememberOpenOffers(patient: PatientState): OpenOffers {
    val all = remember(patient.units, patient.unitData) {
        patient.units.orEmpty().flatMap { patient.unitData[it.id]?.offers.orEmpty() }
    }
    val now = rememberOfferClock(all)
    val list = remember(patient.units, patient.unitData, now) {
        derivedStateOf { openOffersAcrossUnits(patient.units, patient.unitData, now.value) }
    }
    return remember(now, list) { OpenOffers(now, list) }
}

/** Ofertas abertas dos dependentes aprovados (lidas pela rota agregada), com o relógio que as acompanha. */
internal class OpenDependentOffers(val now: State<Long>, val list: State<List<DependentOfferRef>>)

@Composable
internal fun rememberOpenDependentOffers(patient: PatientState): OpenDependentOffers {
    val all = remember(patient.dependentOffers) { patient.dependentOffers.flatMap { it.offers } }
    val now = rememberOfferClock(all)
    val list = remember(patient.dependentOffers, now) { derivedStateOf { openDependentOffers(patient.dependentOffers, now.value) } }
    return remember(now, list) { OpenDependentOffers(now, list) }
}

@Composable
internal fun OfferCountdown(expiresAtMillis: Long, now: State<Long>) {
    // Só este texto lê o relógio: o resto do cartão não recompõe a cada segundo.
    val remaining = expiresAtMillis - now.value
    Text("Tempo restante: ${formatRemaining(remaining)}", style = MaterialTheme.typography.titleMedium,
        fontWeight = FontWeight.Bold,
        color = if (remaining < 60_000) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
        // Leitor de tela recebe uma frase que só muda a cada minuto, sem anunciar a cada segundo.
        modifier = Modifier.clearAndSetSemantics { contentDescription = "Tempo restante: ${describeRemaining(remaining)}" })
}

/** Resumo de uma oferta aberta para Início e Avisos: especialidade, UBS, consulta, contagem e atalho. */
@Composable
internal fun OpenOfferSummary(ref: UnitOfferRef, now: State<Long>, onOpen: (String) -> Unit) {
    val offer = ref.offer
    Text(offer.specialty, fontWeight = FontWeight.SemiBold)
    MutedText("${ref.unit.name} · ${formatRange(offer.startsAt, offer.endsAt)}")
    offer.expiresAtMillis?.let { OfferCountdown(it, now) }
    Button(onClick = { onOpen(ref.unit.id) }, modifier = Modifier.fillMaxWidth()) { Text("Ver oportunidade") }
}

/** Resumo de uma oferta aberta de um dependente: com o nome dele, a UBS e o atalho que passa a agir por ele. */
@Composable
internal fun OpenDependentOfferSummary(ref: DependentOfferRef, now: State<Long>, onOpen: (String, String) -> Unit) {
    val offer = ref.offer
    Text("Para ${ref.dependentName}", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.primary,
        fontWeight = FontWeight.Bold)
    Text(offer.specialty, fontWeight = FontWeight.SemiBold)
    MutedText("${ref.unitName} · ${formatRange(offer.startsAt, offer.endsAt)}")
    offer.expiresAtMillis?.let { OfferCountdown(it, now) }
    Button(onClick = { onOpen(ref.dependentId, ref.unitId) }, modifier = Modifier.fillMaxWidth()) { Text("Ver oportunidade de ${ref.dependentName}") }
}

@Composable
internal fun HistoryToggle(count: Int, open: Boolean, onToggle: () -> Unit) {
    TextButton(onClick = onToggle, modifier = Modifier.semantics { stateDescription = if (open) "Expandido" else "Recolhido" }) {
        Text("Histórico ($count) · ${if (open) "Ocultar" else "Mostrar"}", fontWeight = FontWeight.SemiBold)
    }
}

/** Mensagem do resultado da última ação; anunciada de forma educada pelos leitores de tela. */
@Composable
internal fun ActionMessage(message: String?) {
    if (message == null) return
    Surface(shape = androidx.compose.foundation.shape.RoundedCornerShape(16.dp),
        color = MaterialTheme.colorScheme.surfaceVariant, modifier = Modifier.fillMaxWidth().semantics { liveRegion = LiveRegionMode.Polite }) {
        Text(message, Modifier.padding(14.dp), style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurface)
    }
}

@Composable
private fun OfferCard(offer: UnitOffer, unitName: String?, now: State<Long>, busy: Boolean, onRespond: (String, Boolean) -> Unit) {
    val expired by remember(offer, now) { derivedStateOf { offer.isLocallyExpiredAt(now.value) } }
    SectionCard(offer.specialty, unitName ?: "Oportunidade enviada pela unidade") {
        Text(offer.professional)
        MutedText("Consulta: ${formatRange(offer.startsAt, offer.endsAt)}")
        when {
            offer.status == "pending" && expired -> {
                Text("Prazo encerrado", fontWeight = FontWeight.SemiBold)
                MutedText("Você não pode mais responder a esta oportunidade e continua na fila, no mesmo lugar. Atualize para ver a situação atual.")
            }
            offer.status == "pending" -> {
                offer.expiresAtMillis?.let { OfferCountdown(it, now) }
                MutedText("Responda até ${formatClock(offer.expiresAt)}. Ao aceitar, sua consulta será agendada; ao recusar ou deixar o prazo passar, você continua na fila.")
                Button(onClick = { onRespond(offer.id, true) }, enabled = !busy,
                    modifier = Modifier.fillMaxWidth()) { Text("Aceitar oportunidade") }
                OutlinedButton(onClick = { onRespond(offer.id, false) }, enabled = !busy,
                    modifier = Modifier.fillMaxWidth()) { Text("Recusar oportunidade") }
            }
            offer.status == "accepted" -> MutedText("Oferta aceita. Sua consulta foi agendada.")
            else -> MutedText("${statusLabel(offer.status)} · Prazo: ${formatDateTime(offer.expiresAt)}")
        }
    }
}

@Composable
fun FlashScreen(patient: PatientState, onLoadUnits: () -> Unit, onSelectUnit: (String) -> Unit,
    onRefresh: () -> Unit, onRespond: (String, Boolean) -> Unit) {
    var filter by rememberSaveable(patient.selectedUnitId, patient.actingFor) { mutableStateOf(OfferFilter.ALL) }
    ScreenTitle("Vaga Relâmpago", "Acompanhe as oportunidades da unidade.", action = {
        if (patient.session != null) NoraFilters(if (filter == OfferFilter.ALL) 0 else 1, onClear = { filter = OfferFilter.ALL }) {
            FilterChoices("Oportunidades", OfferFilter.entries, filter, { it.label }) { filter = it }
        }
    })
    SectionCard("Escolha uma UBS", "Somente unidades vinculadas ao seu cadastro") {
        if (patient.session == null) MutedText("Entre na sua conta para consultar oportunidades.")
        patient.units?.forEach { unit ->
            Row(Modifier.fillMaxWidth().selectable(selected = unit.id == patient.selectedUnitId, enabled = !patient.busy, onClick = { onSelectUnit(unit.id) }, role = Role.RadioButton), verticalAlignment = Alignment.CenterVertically) {
                RadioButton(selected = unit.id == patient.selectedUnitId, onClick = null)
                Text(unit.name, Modifier.padding(start = 8.dp))
            }
        }
        if (patient.units?.isEmpty() == true) MutedText("Nenhuma UBS vinculada ao seu cadastro.")
        if (patient.session != null) OutlinedButton(onClick = onLoadUnits, enabled = !patient.busy) { Text("Atualizar unidades") }
    }
    ActionMessage(patient.message)
    val offers = patient.unitOffers.orEmpty()
    val clock = rememberOfferClock(offers)
    val groups by remember(offers, clock) { derivedStateOf { groupOffers(offers, clock.value) } }
    var historyOpen by rememberSaveable { mutableStateOf(false) }
    val unitName = patient.units?.find { it.id == patient.selectedUnitId }?.name
    if (patient.session == null || patient.unitOffers == null) SectionCard("Suas oportunidades", "Oportunidades compatíveis") {
        MutedText("Escolha uma UBS e atualize o acompanhamento para consultar as oportunidades.")
    } else if (filter == OfferFilter.HISTORY && groups.history.isEmpty()) {
        SectionCard("Histórico", "") { MutedText("Nenhuma oportunidade no histórico da unidade.") }
    } else if (filter != OfferFilter.HISTORY && groups.pending.isEmpty()) SectionCard(
        if (offers.isEmpty()) "Nenhuma oportunidade no momento" else "Nenhuma oportunidade aguarda resposta",
        "Você não precisa ficar procurando vagas") {
        MutedText("Quando uma oportunidade estiver disponível, ela aparecerá aqui e você receberá um aviso enquanto a conexão do NORA estiver ativa.")
    }
    if (filter != OfferFilter.HISTORY)
        groups.pending.forEach { offer -> key(offer.id) { OfferCard(offer, unitName, clock, patient.busy, onRespond) } }
    if (filter != OfferFilter.PENDING && groups.history.isNotEmpty()) {
        if (filter == OfferFilter.ALL) HistoryToggle(groups.history.size, historyOpen) { historyOpen = !historyOpen }
        if (historyOpen || filter == OfferFilter.HISTORY)
            groups.history.forEach { offer -> key(offer.id) { OfferCard(offer, unitName, clock, patient.busy, onRespond) } }
    }
    OutlinedButton(onClick = onRefresh, enabled = !patient.busy && patient.selectedUnitId != null, modifier = Modifier.fillMaxWidth()) {
        Text(if (patient.busy) "Atualizando…" else "Atualizar oportunidades")
    }
}

private enum class OfferFilter(val label: String) {
    ALL("Todas"), PENDING("Aguardando resposta"), HISTORY("Histórico")
}
