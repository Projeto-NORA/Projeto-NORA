package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.*

/**
 * Aba Avisos: as ofertas de Vaga Relâmpago abertas (as do paciente e as dos dependentes, com o nome deles) e os comunicados da rede,
 * com os não lidos destacados e a marcação de lido. Os comunicados são texto puro e nunca são interpretados como HTML.
 */
@Composable
fun NoticesScreen(patient: PatientState, onOpenOffer: (String) -> Unit, onOpenDependentOffer: (String, String) -> Unit = { _, _ -> },
    onMarkRead: (String) -> Unit = {}, onReload: () -> Unit = {}, onLoadUnits: () -> Unit = {}) {
    var mode by rememberSaveable { mutableStateOf(NoticeFilter.ALL) }
    ScreenTitle("Avisos", "Comunicados da rede e oportunidades para você.", action = {
        if (patient.session != null) NoraFilters(if (mode == NoticeFilter.ALL) 0 else 1, onClear = { mode = NoticeFilter.ALL }) {
            FilterChoices("Tipo de aviso", NoticeFilter.entries, mode, { it.label }) { mode = it }
        }
    })
    if (patient.session == null) {
        SectionCard("Acesse sua conta", "Avisos da sua rede") { MutedText("Entre na sua conta para consultar avisos.") }
        return
    }
    if (mode == NoticeFilter.ALL || mode == NoticeFilter.OFFERS) {
        NoticeOffers(patient, onOpenOffer, onOpenDependentOffer, onLoadUnits)
    }
    if (mode != NoticeFilter.OFFERS) {
        AnnouncementsSection(patient.announcements, onMarkRead, onReload, mode == NoticeFilter.UNREAD)
    }
}

@Composable
private fun NoticeOffers(
    patient: PatientState,
    onOpenOffer: (String) -> Unit,
    onOpenDependentOffer: (String, String) -> Unit,
    onLoadUnits: () -> Unit,
) {
    val openOffers = rememberOpenOffers(patient)
    val dependentOffers = rememberOpenDependentOffers(patient)
    val open = openOffers.list.value
    val openDependents = dependentOffers.list.value
    val allLoaded = offersLoadedForAll(patient.units, patient.unitData)
    // Sem as UBS (a leitura falhou) os comunicados continuam valendo: eles não dependem delas, só as ofertas das unidades.
    if (patient.units == null) {
        val reading = patient.loadingUnits || patient.busy
        SectionCard("Suas UBS", "Oportunidades da unidade") {
            MutedText(if (reading) "Carregando suas UBS…"
            else "Não foi possível carregar suas UBS; as vagas não aparecem.")
            if (!reading) OutlinedButton(onClick = onLoadUnits, modifier = Modifier.fillMaxWidth()) { Text("Tentar novamente") }
        }
    } else if (open.isEmpty() && openDependents.isEmpty()) {
        SectionCard(if (allLoaded) "Nenhuma oferta pendente" else "Consultando suas UBS", "Oportunidades da unidade") {
            MutedText(if (allLoaded) "Não há Vagas Relâmpago aguardando resposta."
            else "Vagas conferidas a cada 30 s.")
        }
    }
    open.forEach { ref ->
        key(ref.offer.id) {
            SectionCard("Vaga Relâmpago disponível", "Oportunidade enviada pela unidade") { OpenOfferSummary(ref, openOffers.now, onOpenOffer) }
        }
    }
    openDependents.forEach { ref ->
        key(ref.offer.id) {
            SectionCard("Vaga Relâmpago de um dependente", "Oportunidade enviada pela unidade") {
                OpenDependentOfferSummary(ref, dependentOffers.now, onOpenDependentOffer)
            }
        }
    }
}

internal enum class NoticeFilter(val label: String) {
    ALL("Todos os avisos"), OFFERS("Vagas Relâmpago"), ANNOUNCEMENTS("Comunicados"), UNREAD("Comunicados não lidos")
}

@Composable
internal fun AnnouncementsSection(state: AnnouncementsState, onMarkRead: (String) -> Unit, onReload: () -> Unit, unreadOnly: Boolean = false) {
    val clock = LocalClockMillis.current
    val now = remember(state.items) { clock() }
    val visible = remember(state.items, now, unreadOnly) {
        visibleAnnouncements(state.items, now).filter { !unreadOnly || !it.isRead }
    }
    val unread = remember(state.items, now) { unreadAnnouncementCount(state.items, now) }
    SectionCard("Comunicados da rede", when {
        !state.loaded -> "Mensagens da Secretaria para as suas UBS"
        unread == 0 -> "Nenhum comunicado novo"
        else -> countLabel(unread, "comunicado novo", "comunicados novos")
    }) {
        when {
            state.error != null && !state.loaded -> MutedText(state.error)
            !state.loaded -> MutedText(if (state.loading) "Consultando os comunicados…" else "Os comunicados ainda não foram consultados.")
            visible.isEmpty() -> MutedText("Nenhum comunicado no momento.")
        }
        if (state.error != null && state.loaded) MutedText(state.error)
        if (state.loading) LinearProgressIndicator(Modifier.fillMaxWidth())
        OutlinedButton(onClick = onReload, enabled = !state.loading, modifier = Modifier.fillMaxWidth()) {
            Text(if (state.loading) "Atualizando…" else "Atualizar comunicados")
        }
    }
    visible.forEach { item -> key(item.id) { AnnouncementCard(item, onMarkRead) } }
}

@Composable
private fun AnnouncementCard(item: Announcement, onMarkRead: (String) -> Unit) {
    SectionCard(item.title, "Publicado em ${formatDateTime(item.publishedAt)}", highlight = !item.isRead) {
        if (!item.isRead) Surface(shape = androidx.compose.foundation.shape.RoundedCornerShape(50), color = MaterialTheme.colorScheme.primary) {
            Text("NOVO", Modifier.padding(horizontal = 10.dp, vertical = 4.dp), style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onPrimary, fontWeight = FontWeight.Bold)
        }
        // Texto puro, exatamente como a Secretaria o escreveu (quebras de linha incluídas).
        Text(item.body, style = MaterialTheme.typography.bodyLarge, fontWeight = if (item.isRead) FontWeight.Normal else FontWeight.Medium)
        if (item.isRead) MutedText("Lido" + (item.readAt?.let { " em ${formatDate(it)}" } ?: ""))
        else OutlinedButton(onClick = { onMarkRead(item.id) }, modifier = Modifier.fillMaxWidth()) { Text("Marcar como lido") }
    }
}
