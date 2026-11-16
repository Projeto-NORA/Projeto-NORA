package com.example.app_nora.ui

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
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
    ScreenTitle("Avisos", "Comunicados da rede e oportunidades para você.")
    val openOffers = rememberOpenOffers(patient)
    val dependentOffers = rememberOpenDependentOffers(patient)
    val open = openOffers.list.value
    val openDependents = dependentOffers.list.value
    val allLoaded = offersLoadedForAll(patient.units, patient.unitData)
    if (patient.session == null) {
        SectionCard("Acesse sua conta", "Avisos da sua rede") { MutedText("Entre na sua conta para consultar avisos.") }
        return
    }
    // Sem as UBS (a leitura falhou) os comunicados continuam valendo: eles não dependem delas, só as ofertas das unidades.
    if (patient.units == null) {
        val reading = patient.loadingUnits || patient.busy
        SectionCard("Suas UBS", "Oportunidades da unidade") {
            MutedText(if (reading) "Carregando suas UBS…"
            else "Não foi possível carregar suas UBS. As Vagas Relâmpago dependem delas; os comunicados abaixo não.")
            if (!reading) OutlinedButton(onClick = onLoadUnits, modifier = Modifier.fillMaxWidth()) { Text("Tentar novamente") }
        }
    } else if (open.isEmpty() && openDependents.isEmpty()) {
        SectionCard(if (allLoaded) "Nenhuma oferta pendente" else "Consultando suas UBS", "Oportunidades da unidade") {
            MutedText(if (allLoaded) "Não há Vagas Relâmpago aguardando resposta."
            else "As ofertas das suas UBS são consultadas a cada 30 segundos enquanto a conexão do NORA estiver ativa.")
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
    AnnouncementsSection(patient.announcements, onMarkRead, onReload)
}

@Composable
internal fun AnnouncementsSection(state: AnnouncementsState, onMarkRead: (String) -> Unit, onReload: () -> Unit) {
    val clock = LocalClockMillis.current
    val now = remember(state.items) { clock() }
    val visible = remember(state.items, now) { visibleAnnouncements(state.items, now) }
    val unread = remember(state.items, now) { unreadAnnouncementCount(state.items, now) }
    SectionCard("Comunicados da rede", when {
        !state.loaded -> "Mensagens da Secretaria para as suas UBS"
        unread == 0 -> "Nenhum comunicado novo"
        else -> countLabel(unread, "comunicado novo", "comunicados novos")
    }) {
        when {
            state.error != null && !state.loaded -> MutedText(state.error)
            !state.loaded -> MutedText(if (state.loading) "Consultando os comunicados…" else "Os comunicados ainda não foram consultados.")
            visible.isEmpty() -> MutedText("Nenhum comunicado da rede no momento. Quando a Secretaria publicar um, ele aparece aqui.")
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
