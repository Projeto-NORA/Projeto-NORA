package com.example.app_nora.api

import java.text.ParseException
import java.text.SimpleDateFormat
import java.util.Locale

// Regras puras (sem Android) da área do paciente: datas, prazos, ordenação, próxima consulta e
// deduplicação de avisos. Ficam aqui para serem testadas na JVM e reaproveitadas pela tela e pelo serviço.

private val FRACTION = Regex("(?<=\\d)\\.\\d+")
private val PT_BR: Locale = Locale.forLanguageTag("pt-BR")

/** ISO-8601 com deslocamento (`Z` ou `+hh:mm`) para epoch em milissegundos. A fração de segundo é descartada. */
fun parseInstantMillis(value: String): Long? = try {
    SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ssXXX", Locale.ROOT).apply { isLenient = false }
        .parse(FRACTION.replace(value, ""))?.time
} catch (_: ParseException) { null }

private fun format(pattern: String, millis: Long): String = SimpleDateFormat(pattern, PT_BR).format(millis)

/** "12/11/2026 às 14:30"; devolve o texto original se a data for ilegível. */
fun formatDateTime(value: String): String =
    parseInstantMillis(value)?.let { format("dd/MM/yyyy 'às' HH:mm", it) } ?: value

/** "12/11/2026, das 14:30 às 15:00" (mesmo dia) ou início e fim completos quando atravessa o dia. */
fun formatRange(startsAt: String, endsAt: String): String {
    val start = parseInstantMillis(startsAt) ?: return formatDateTime(startsAt)
    val end = parseInstantMillis(endsAt) ?: return formatDateTime(startsAt)
    return if (format("yyyyMMdd", start) == format("yyyyMMdd", end))
        "${format("dd/MM/yyyy", start)}, das ${format("HH:mm", start)} às ${format("HH:mm", end)}"
    else "${formatDateTime(startsAt)} até ${formatDateTime(endsAt)}"
}

/** "14:30"; devolve o texto original se a data for ilegível. */
fun formatClock(value: String): String = parseInstantMillis(value)?.let { format("HH:mm", it) } ?: value

// ---- Prazo de ofertas ----------------------------------------------------------------------------

/** Aguardando resposta e dentro do prazo (ou prazo ilegível, caso em que o servidor decide). */
fun UnitOffer.isOpenAt(now: Long): Boolean =
    status == "pending" && (expiresAtMillis?.let { now < it } ?: true)

/** Aguardando resposta, mas o prazo já passou no relógio do aparelho. */
fun UnitOffer.isLocallyExpiredAt(now: Long): Boolean =
    status == "pending" && expiresAtMillis?.let { now >= it } == true

/** "09:32" (ou "1:09:32"), arredondando para cima para não mostrar 00:00 antes do fim. */
fun formatRemaining(millis: Long): String {
    val total = (millis.coerceAtLeast(0) + 999) / 1000
    val hours = total / 3600
    val minutes = total % 3600 / 60
    val seconds = total % 60
    return if (hours > 0) String.format(Locale.ROOT, "%d:%02d:%02d", hours, minutes, seconds)
    else String.format(Locale.ROOT, "%02d:%02d", minutes, seconds)
}

/** Frase para leitor de tela: muda no máximo a cada minuto, para não anunciar a cada segundo. */
fun describeRemaining(millis: Long): String {
    val total = (millis.coerceAtLeast(0) + 999) / 1000
    if (total < 60) return "menos de um minuto"
    val minutes = total / 60
    return if (minutes == 1L) "1 minuto" else "$minutes minutos"
}

// ---- Ordenação e partição ------------------------------------------------------------------------

data class OfferGroups(val pending: List<UnitOffer>, val history: List<UnitOffer>)

/**
 * Pendentes primeiro: as que ainda estão no prazo, da que vence antes para a que vence depois, e depois as
 * que o aparelho já considera vencidas. O histórico vem do mais recente (maior prazo) para o mais antigo.
 */
fun groupOffers(offers: List<UnitOffer>, now: Long): OfferGroups {
    val pending = offers.filter { it.status == "pending" }
        .sortedWith(compareBy<UnitOffer>({ it.isLocallyExpiredAt(now) }, { it.expiresAtMillis ?: Long.MAX_VALUE }, { it.id }))
    val history = offers.filter { it.status != "pending" }
        .sortedWith(compareBy<UnitOffer>({ it.expiresAtMillis == null }, { -(it.expiresAtMillis ?: 0L) }, { it.id }))
    return OfferGroups(pending, history)
}

data class QueueGroups(val active: List<UnitQueueEntry>, val history: List<UnitQueueEntry>)

/** Ativas (aguardando ou com oferta enviada), da entrada mais antiga para a mais nova; histórico do mais novo ao mais antigo. */
fun groupQueue(entries: List<UnitQueueEntry>): QueueGroups {
    val active = entries.filter { it.status == "waiting" || it.status == "offered" }
        .sortedWith(compareBy<UnitQueueEntry>({ it.enteredAtMillis == null }, { it.enteredAtMillis ?: 0L }, { it.id }))
    val history = entries.filter { it.status != "waiting" && it.status != "offered" }
        .sortedWith(compareBy<UnitQueueEntry>({ it.enteredAtMillis == null }, { -(it.enteredAtMillis ?: 0L) }, { -it.id }))
    return QueueGroups(active, history)
}

// ---- Dados por UBS -------------------------------------------------------------------------------

/** O que já foi lido de uma UBS; `null` em cada campo significa "ainda não consultado". */
data class UnitSnapshot(val queue: List<UnitQueueEntry>? = null, val offers: List<UnitOffer>? = null,
    val appointments: List<PatientAppointment>? = null)

data class UnitOfferRef(val unit: PatientHealthUnit, val offer: UnitOffer)

/** Ofertas ainda abertas (pendentes e no prazo) de todas as UBS vinculadas, da que vence primeiro para a última. */
fun openOffersAcrossUnits(units: List<PatientHealthUnit>?, data: Map<String, UnitSnapshot>, now: Long): List<UnitOfferRef> {
    if (units == null) return emptyList()
    val result = ArrayList<UnitOfferRef>()
    for (unit in units) data[unit.id]?.offers?.forEach { if (it.isOpenAt(now)) result += UnitOfferRef(unit, it) }
    result.sortWith(compareBy<UnitOfferRef>({ it.offer.expiresAtMillis ?: Long.MAX_VALUE }, { it.offer.id }))
    return result
}

/** Todas as UBS vinculadas já tiveram as ofertas consultadas (distingue "nenhuma" de "ainda não consultei"). */
fun offersLoadedForAll(units: List<PatientHealthUnit>?, data: Map<String, UnitSnapshot>): Boolean =
    units != null && units.all { data[it.id]?.offers != null }

data class NextAppointment(val unit: PatientHealthUnit, val appointment: PatientAppointment)

/** A consulta agendada ou chamada de início mais próximo no futuro, entre as UBS já carregadas. */
fun nextAppointment(units: List<PatientHealthUnit>?, data: Map<String, UnitSnapshot>, now: Long): NextAppointment? {
    if (units == null) return null
    var best: NextAppointment? = null
    var bestStart = Long.MAX_VALUE
    for (unit in units) data[unit.id]?.appointments?.forEach { item ->
        val start = item.startsAtMillis
        if ((item.status == "scheduled" || item.status == "called") && start != null && start > now && start < bestStart) {
            best = NextAppointment(unit, item); bestStart = start
        }
    }
    return best
}

// ---- Avisos locais de Vaga Relâmpago ---------------------------------------------------------------

/** Memória (apenas em RAM) das ofertas já avisadas, para não repetir a notificação a cada ciclo de 30 s. */
class NotifiedOffers {
    private val ids = LinkedHashSet<String>()

    /** Devolve as ofertas de [candidates] ainda não avisadas e as marca como avisadas. */
    fun markFresh(candidates: Collection<String>): Set<String> {
        val fresh = LinkedHashSet<String>()
        for (id in candidates) if (ids.add(id)) fresh += id
        return fresh
    }

    /** Esquece as ofertas que deixaram de estar abertas e devolve seus ids (as notificações a cancelar). */
    fun retain(open: Set<String>): List<String> {
        val gone = ids.filter { it !in open }
        ids.removeAll(gone.toSet())
        return gone
    }

    fun clear(): List<String> = ids.toList().also { ids.clear() }
}

// ---- Textos de situação ------------------------------------------------------------------------------

/** Tradução única para toda situação que o servidor devolve; nunca mostra o valor cru. */
fun statusLabel(value: String): String = when (value) {
    "scheduled" -> "Agendada"
    "called" -> "Chamada pela unidade"
    "completed" -> "Concluída"
    "missed" -> "Falta registrada"
    "cancelled" -> "Cancelada"
    "waiting" -> "Aguardando"
    "offered" -> "Oferta enviada"
    "pending" -> "Aguardando sua resposta"
    "accepted" -> "Aceita"
    "declined" -> "Recusada"
    "expired" -> "Prazo encerrado"
    "open" -> "Aberta"
    "closed" -> "Encerrada"
    else -> "Situação não reconhecida"
}
