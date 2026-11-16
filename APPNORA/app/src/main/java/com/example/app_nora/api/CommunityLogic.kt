package com.example.app_nora.api

// Regras puras (sem Android) dos comunicados da rede e dos exames e vacinas da Jornada, testáveis na JVM.

// ---- Comunicados -------------------------------------------------------------------------------------

/** Resultado de juntar uma leitura incremental: a lista inteira (ordem crescente de `sequence`) e só o que chegou agora. */
data class AnnouncementMerge(val items: List<Announcement>, val fresh: List<Announcement>)

/**
 * Junta o que a rota incremental devolveu ao que já estava lido, sem repetir por id: um comunicado que já existe
 * não é substituído (preserva o "lido" marcado aqui) e nunca conta como novo duas vezes.
 */
fun mergeAnnouncements(existing: List<Announcement>, incoming: List<Announcement>): AnnouncementMerge {
    if (incoming.isEmpty()) return AnnouncementMerge(existing, emptyList())
    val byId = LinkedHashMap<String, Announcement>()
    for (item in existing) byId[item.id] = item
    val fresh = ArrayList<Announcement>()
    for (item in incoming) if (item.id !in byId) { byId[item.id] = item; fresh += item }
    return AnnouncementMerge(byId.values.sortedBy { it.sequence }, fresh)
}

/** O comunicado venceu pelo relógio do aparelho; o servidor também deixa de enviá-lo na leitura completa seguinte. */
fun Announcement.isExpiredAt(now: Long): Boolean = expiresAtMillis?.let { now >= it } == true

/** Os que ainda valem, do mais novo para o mais antigo; a ordem não muda ao marcar como lido. */
fun visibleAnnouncements(items: List<Announcement>, now: Long): List<Announcement> =
    items.filter { !it.isExpiredAt(now) }.sortedByDescending { it.sequence }

fun unreadAnnouncementCount(items: List<Announcement>, now: Long): Int = items.count { !it.isRead && !it.isExpiredAt(now) }

/**
 * Memória (apenas em RAM) dos comunicados já tratados pela notificação, para o ciclo de 30 s nunca repetir o aviso do mesmo id.
 * Guarda no máximo [capacity] ids (descarta os mais antigos) e lembra quais avisos estão na bandeja para retirá-los no logout.
 */
class NotifiedAnnouncements(private val capacity: Int = 200) {
    private val seen = LinkedHashSet<String>()
    private val posted = LinkedHashSet<String>()

    /** Devolve os ids de [candidates] que nunca passaram por aqui e os marca como vistos. */
    fun markFresh(candidates: Collection<String>): Set<String> {
        val fresh = LinkedHashSet<String>()
        for (id in candidates) if (seen.add(id)) fresh += id
        while (seen.size > capacity) seen.remove(seen.first())
        return fresh
    }

    fun markPosted(id: String) { posted += id }

    /** Devolve os ids de avisos postados e esquece todos (fim da conexão ou logout). */
    fun clear(): List<String> = posted.toList().also { posted.clear(); seen.clear() }
}

/**
 * O que notificar neste ciclo: só comunicados novos ainda não lidos e nunca avisados antes. Com a preferência desligada ou sem a
 * permissão do Android ([enabled] falso) nada é avisado, mas os ids ficam vistos: ligar a preferência depois não despeja o que passou.
 */
fun announcementsToNotify(fresh: List<Announcement>, enabled: Boolean, notified: NotifiedAnnouncements): List<Announcement> {
    val unseen = notified.markFresh(fresh.filter { !it.isRead }.map { it.id })
    return if (enabled) fresh.filter { it.id in unseen } else emptyList()
}

fun announcementNoticeTitle(count: Int): String = if (count == 1) "Comunicado da rede" else "$count novos comunicados da rede"
fun announcementNoticeText(items: List<Announcement>): String =
    if (items.size == 1) items.single().title else "${items.first().title} e mais ${items.size - 1}"

// ---- Jornada: exames e vacinas ------------------------------------------------------------------------

/** "2026-11-10" vira "10/11/2026" sem passar por fuso (é data de calendário); devolve o texto original se não for uma data. */
fun formatCalendarDate(value: String): String {
    val parts = value.split('-')
    if (parts.size != 3 || parts[0].length != 4 || parts[1].length != 2 || parts[2].length != 2) return value
    val year = parts[0].toIntOrNull() ?: return value
    val month = parts[1].toIntOrNull()?.takeIf { it in 1..12 } ?: return value
    val day = parts[2].toIntOrNull()?.takeIf { it in 1..31 } ?: return value
    return String.format(java.util.Locale.ROOT, "%02d/%02d/%04d", day, month, year)
}

/** Só estes exames aparecem; anulado (ou valor desconhecido) nunca é mostrado, e o motivo da anulação nem chega ao app. */
fun isShownExam(exam: ExamRecord): Boolean = exam.status == "requested" || exam.status == "done" || exam.status == "cancelled"
fun isShownVaccine(vaccine: VaccineRecord): Boolean = vaccine.status == "applied"

/** "Solicitado em 10/11/2026", "Realizado em 12/11/2026" ou "Cancelado"; nulo para o que não se mostra. */
fun examStatusText(exam: ExamRecord): String? = when (exam.status) {
    "requested" -> "Solicitado em ${formatDate(exam.requestedAt)}"
    "done" -> exam.performedOn?.let { "Realizado em ${formatCalendarDate(it)}" } ?: "Realizado"
    "cancelled" -> "Cancelado"
    else -> null
}

fun vaccineDoseLabel(dose: String): String? = when (dose) {
    "1" -> "1ª dose"
    "2" -> "2ª dose"
    "3" -> "3ª dose"
    "booster" -> "Reforço"
    "single" -> "Dose única"
    else -> null
}

/** "2ª dose · aplicada em 10/11/2026"; uma dose desconhecida é omitida (nunca o valor cru). */
fun vaccineText(vaccine: VaccineRecord): String =
    listOfNotNull(vaccineDoseLabel(vaccine.dose), "aplicada em ${formatCalendarDate(vaccine.appliedOn)}").joinToString(" · ")

/** Exames e vacinas visíveis de uma UBS que respondeu. */
data class UnitRecordsView(val unit: PatientHealthUnit, val exams: List<ExamRecord>, val vaccines: List<VaccineRecord>)

/**
 * Resumo da Jornada para a tela: as UBS que responderam (com o que se mostra) e quantas não responderam ou ainda não foram lidas.
 * Uma UBS que falhou nunca vira "sem registros".
 */
data class RecordsOverview(val units: List<UnitRecordsView>, val answered: Int, val failed: Int, val pending: Int) {
    val examCount: Int get() = units.sumOf { it.exams.size }
    val vaccineCount: Int get() = units.sumOf { it.vaccines.size }
}

fun summarizeRecords(units: List<PatientHealthUnit>?, data: Map<String, UnitSnapshot>): RecordsOverview {
    val views = ArrayList<UnitRecordsView>()
    var failed = 0; var pending = 0
    for (unit in units.orEmpty()) {
        val snapshot = data[unit.id]
        val records = snapshot?.records
        when {
            records != null -> views += UnitRecordsView(unit, records.exams.filter(::isShownExam), records.vaccines.filter(::isShownVaccine))
            snapshot?.recordsFailed == true -> failed++
            else -> pending++
        }
    }
    return RecordsOverview(views, views.size, failed, pending)
}
