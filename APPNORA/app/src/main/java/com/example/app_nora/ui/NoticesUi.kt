package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
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
    ScreenTitle("Avisos", "Comunicados e oportunidades.")
    val openOffers = rememberOpenOffers(patient)
    val dependentOffers = rememberOpenDependentOffers(patient)
    val open = openOffers.list.value
    val openDependents = dependentOffers.list.value
    val allLoaded = offersLoadedForAll(patient.units, patient.unitData)
    if (patient.session == null) {
        SectionCard("Acesse sua conta", "Avisos") { MutedText("Entre na sua conta para consultar avisos.") }
        return
    }
    // Sem as UBS (a leitura falhou) os comunicados continuam valendo: eles não dependem delas, só as ofertas das unidades.
    if (patient.units == null) {
        val reading = patient.loadingUnits || patient.busy
        SectionCard(if (reading) "Suas UBS" else "Vagas Relâmpago indisponíveis", if (reading) "Vagas Relâmpago" else "") {
            if (reading) MutedText("Carregando suas UBS…")
            else ErrorNotice("Não foi possível carregar suas UBS. Os comunicados abaixo não dependem delas.")
            if (!reading) RetryButton(onClick = onLoadUnits)
        }
    } else if (open.isEmpty() && openDependents.isEmpty()) {
        SectionCard(if (allLoaded) "Nenhuma oferta pendente" else "Consultando suas UBS", "Vagas Relâmpago") {
            MutedText(if (allLoaded) "Não há Vagas Relâmpago aguardando resposta."
            else "As ofertas são atualizadas a cada 30 segundos, enquanto o NORA estiver conectado (aviso fixo na barra do Android).")
        }
    }
    open.forEach { ref ->
        key(ref.offer.id) {
            SectionCard("Vaga Relâmpago disponível", "Enviada pela unidade") { OpenOfferSummary(ref, openOffers.now, onOpenOffer) }
        }
    }
    openDependents.forEach { ref ->
        key(ref.offer.id) {
            SectionCard("Vaga Relâmpago de um dependente", "Enviada pela unidade") {
                OpenDependentOfferSummary(ref, dependentOffers.now, onOpenDependentOffer)
            }
        }
    }
    AnnouncementsSection(patient.announcements, onMarkRead, onReload)
}

@Composable
internal fun AnnouncementsSection(state: AnnouncementsState, onMarkRead: (String) -> Unit, onReload: () -> Unit) {
    val clock = LocalClockMillis.current
    val now = remember(state.items) { clock() }
    val visible = remember(state.items, now) { visibleAnnouncements(state.items, now) }
    val unread = remember(state.items, now) { unreadAnnouncementCount(state.items, now) }
    // Cabeçalho da lista que vem abaixo, sem cartão: a contagem e o botão são da seção, não um aviso a mais.
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        // O botão fica numa linha própria: com fonte ampliada ele não espreme o título e a contagem.
        Column(Modifier.fillMaxWidth()) {
            Text("Comunicados da rede", Modifier.semantics { heading() }, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            MutedText(when {
                !state.loaded -> "Mensagens da Secretaria"
                unread == 0 -> "Nenhum comunicado novo"
                else -> countLabel(unread, "comunicado novo", "comunicados novos")
            })
            TextButton(onClick = onReload, enabled = !state.loading, contentPadding = PaddingValues(0.dp)) {
                Text(if (state.loading) "Atualizando…" else "Atualizar comunicados")
            }
        }
        when {
            state.error != null && !state.loaded -> ErrorNotice(state.error)
            !state.loaded -> MutedText(if (state.loading) "Consultando os comunicados…" else "Os comunicados ainda não foram consultados.")
            visible.isEmpty() -> MutedText("Nenhum comunicado no momento.")
        }
        if (state.error != null && state.loaded) ErrorNotice(state.error)
        if (state.loading) LinearProgressIndicator(Modifier.fillMaxWidth())
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
