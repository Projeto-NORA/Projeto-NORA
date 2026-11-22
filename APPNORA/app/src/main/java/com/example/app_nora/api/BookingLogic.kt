package com.example.app_nora.api

import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Locale
import java.util.TimeZone

// Regras puras (sem Android) do agendamento pelo catálogo e da confirmação de presença. As janelas são as do
// servidor (docs/api.md); o aparelho as usa só para montar a tela, e quem decide é sempre o servidor.

private const val HOUR_MS = 3_600_000L
private const val DAY_MS = 24 * HOUR_MS
private val PT_BR: Locale = Locale.forLanguageTag("pt-BR")

/** Confirmação de presença: de 48 horas até 2 horas antes do início, limites incluídos. */
const val PRESENCE_OPENS_BEFORE_MS = 48 * HOUR_MS
const val PRESENCE_CLOSES_BEFORE_MS = 2 * HOUR_MS

/**
 * Agendamento pelo app: qualquer horário livre que ainda não começou, sem antecedência mínima (decisão do usuário de
 * 2026-11-17); quem diz se o horário está livre é o servidor. A única distância máxima é o teto técnico do servidor
 * (SchedulingService.MaxScheduleHorizonYears): ele recusa com 400 `invalid-input` qualquer consulta de horários além dele.
 */
fun isBookable(startMillis: Long, now: Long): Boolean = startMillis > now

/** Teto de distância das datas de agendamento, igual a `SchedulingService.MaxScheduleHorizonYears` do servidor. */
const val BOOKING_HORIZON_YEARS = 20

/**
 * O instante a partir do qual o app não pede horários: [BOOKING_HORIZON_YEARS] anos depois de [now], menos um dia de folga para
 * a diferença entre o relógio do aparelho e o do servidor (o servidor compara com o relógio dele no momento da requisição).
 */
fun bookingHorizon(now: Long, zone: TimeZone = TimeZone.getDefault()): Long = Calendar.getInstance(zone).apply {
    timeInMillis = now; add(Calendar.YEAR, BOOKING_HORIZON_YEARS); add(Calendar.DAY_OF_YEAR, -1)
}.timeInMillis

// ---- Semanas de horários ---------------------------------------------------------------------------------

/**
 * Uma "semana" de horários: sete dias do calendário do aparelho a partir de hoje (a semana 0 começa agora, para não pedir
 * horários que já passaram). [fromMillis, toMillis) é o intervalo pedido ao servidor, uma requisição por semana. A última semana
 * termina em [bookingHorizon] (a seguinte não existe); até lá dá para avançar ou saltar para uma data com [bookingWeekIndexFor].
 */
data class BookingWeek(val index: Int, val fromMillis: Long, val toMillis: Long)

private fun startOfDay(millis: Long, zone: TimeZone): Long = Calendar.getInstance(zone).apply {
    timeInMillis = millis
    set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
}.timeInMillis

private fun plusDays(dayStart: Long, days: Int, zone: TimeZone): Long = Calendar.getInstance(zone).apply {
    timeInMillis = dayStart; add(Calendar.DAY_OF_YEAR, days)
}.timeInMillis

/** A semana de número [index] (0 = a que contém hoje, recortada em [now]) ou nulo se [index] for negativo ou já estiver além de [bookingHorizon]. */
fun bookingWeek(now: Long, index: Int, zone: TimeZone = TimeZone.getDefault()): BookingWeek? {
    if (index < 0) return null
    val first = startOfDay(now, zone)
    val from = maxOf(plusDays(first, 7 * index, zone), now)
    val to = minOf(plusDays(first, 7 * (index + 1), zone), bookingHorizon(now, zone))
    return if (from < to) BookingWeek(index, from, to) else null
}

/** O número da semana que contém o dia de [dateMillis] (qualquer instante do dia, no calendário do aparelho); hoje ou antes é a semana 0. */
fun bookingWeekIndexFor(dateMillis: Long, now: Long, zone: TimeZone = TimeZone.getDefault()): Int {
    val first = startOfDay(now, zone)
    val days = Math.round((startOfDay(dateMillis, zone) - first).toDouble() / DAY_MS).toInt()
    return if (days <= 0) 0 else days / 7
}

/**
 * O seletor de data do Material devolve a meia-noite UTC do dia escolhido; isto devolve o início do mesmo dia (ano, mês e dia)
 * no calendário do aparelho, que é o que [bookingWeekIndexFor] espera.
 */
fun localDayFromPicker(utcMidnightMillis: Long, zone: TimeZone = TimeZone.getDefault()): Long {
    val utc = Calendar.getInstance(TimeZone.getTimeZone("UTC")).apply { timeInMillis = utcMidnightMillis }
    return Calendar.getInstance(zone).apply {
        clear(); set(utc.get(Calendar.YEAR), utc.get(Calendar.MONTH), utc.get(Calendar.DAY_OF_MONTH))
    }.timeInMillis
}

/** O dia de [dayStartMillis] pode ter horário livre: é hoje ou depois e ainda antes de [bookingHorizon] (o seletor só deixa escolher esses). */
fun isDayBookable(dayStartMillis: Long, now: Long, zone: TimeZone = TimeZone.getDefault()): Boolean =
    dayStartMillis >= startOfDay(now, zone) && dayStartMillis < bookingHorizon(now, zone)

/** Atendimentos de um mesmo dia do calendário do aparelho, do mais cedo ao mais tarde. */
data class SlotDay(val dayStart: Long, val slots: List<AvailableSlot>)

/**
 * Agrupa os horários livres por dia. Descarta horário ilegível ou que já começou (o servidor já recorta; isto só evita
 * oferecer o que não pode ser marcado). Dentro do dia: início, depois nome do profissional.
 */
fun groupSlotsByDay(slots: List<AvailableSlot>, now: Long, zone: TimeZone = TimeZone.getDefault()): List<SlotDay> {
    val byDay = java.util.TreeMap<Long, MutableList<AvailableSlot>>()
    for (slot in slots) {
        val start = slot.startsAtMillis ?: continue
        if (isBookable(start, now)) byDay.getOrPut(startOfDay(start, zone)) { ArrayList() } += slot
    }
    return byDay.map { (day, items) ->
        SlotDay(day, items.sortedWith(compareBy<AvailableSlot>({ it.startsAtMillis }, { it.professionalName.lowercase(PT_BR) }, { it.professionalId })))
    }
}

/** O dia que a tela mostra: o pedido ([wantedDay], início do dia) se ele tem horário livre; senão o primeiro dia da semana. [days] não pode ser vazio. */
fun shownDay(days: List<SlotDay>, wantedDay: Long?): SlotDay = days.firstOrNull { it.dayStart == wantedDay } ?: days.first()

/** Há um dia pedido, mas ele não tem horário livre nesta semana (a tela avisa e mostra os outros dias). */
fun isWantedDayEmpty(days: List<SlotDay>, wantedDay: Long?): Boolean = wantedDay != null && days.none { it.dayStart == wantedDay }

private fun formatLocal(pattern: String, millis: Long): String = SimpleDateFormat(pattern, PT_BR).format(millis)

/** "quinta-feira, 19/11". */
fun formatDayLabel(dayStart: Long): String = formatLocal("EEEE, dd/MM", dayStart)

/** "16/11 a 22/11": o primeiro e o último dia que a semana pode oferecer. */
fun formatWeekRange(week: BookingWeek): String =
    "${formatLocal("dd/MM", week.fromMillis)} a ${formatLocal("dd/MM", week.toMillis - 1)}"

// ---- Confirmação de presença ------------------------------------------------------------------------------

enum class PresenceState {
    /** Já confirmada (`confirmedAt` preenchido). */
    CONFIRMED,
    /** Agendada, mas a janela de confirmação ainda não abriu. */
    NOT_YET,
    /** Dentro da janela: o botão aparece. */
    OPEN,
    /** A janela fechou sem confirmação; nada é cancelado por isso. */
    CLOSED,
    /** Consulta que não aceita confirmação (cancelada, chamada, encerrada) ou de data ilegível. */
    NOT_APPLICABLE,
}

fun presenceOpensAt(appointment: PatientAppointment): Long? = appointment.startsAtMillis?.minus(PRESENCE_OPENS_BEFORE_MS)
fun presenceClosesAt(appointment: PatientAppointment): Long? = appointment.startsAtMillis?.minus(PRESENCE_CLOSES_BEFORE_MS)

/** Situação da confirmação pelo relógio do aparelho; o servidor continua tendo a palavra final. */
fun presenceState(appointment: PatientAppointment, now: Long): PresenceState {
    if (appointment.status != "scheduled") return PresenceState.NOT_APPLICABLE
    if (appointment.confirmedAt != null) return PresenceState.CONFIRMED
    val opens = presenceOpensAt(appointment) ?: return PresenceState.NOT_APPLICABLE
    val closes = presenceClosesAt(appointment) ?: return PresenceState.NOT_APPLICABLE
    return when {
        now < opens -> PresenceState.NOT_YET
        now <= closes -> PresenceState.OPEN
        else -> PresenceState.CLOSED
    }
}

/** Próximo instante, depois de [now], em que a situação de alguma consulta muda (abertura ou fechamento); nulo se nenhum. */
fun nextPresenceBoundary(appointments: List<PatientAppointment>, now: Long): Long? {
    var best: Long? = null
    for (appointment in appointments) {
        if (appointment.status != "scheduled" || appointment.confirmedAt != null) continue
        val opens = presenceOpensAt(appointment) ?: continue
        val closed = (presenceClosesAt(appointment) ?: continue) + 1 // fecha ao passar do limite (limite incluído)
        for (boundary in longArrayOf(opens, closed)) if (boundary > now && (best == null || boundary < best)) best = boundary
    }
    return best
}

/** "Presença confirmada às 14:30". */
fun presenceConfirmedText(appointment: PatientAppointment): String =
    "Presença confirmada" + (appointment.confirmedAt?.let { " às ${formatClock(it)}" } ?: "")

/** Teto de cada espera do relógio da confirmação: o relógio de corrotina não anda durante o sono profundo do aparelho. */
const val PRESENCE_CLOCK_MAX_WAIT_MS = 60_000L

/**
 * Relógio da confirmação de presença: chama [onChange] com a hora do aparelho cada vez que uma fronteira de [appointments] (abertura ou
 * fechamento da janela) foi cruzada, e termina quando nenhuma consulta pode mudar. Cada espera ([pause], por padrão `delay`) tem no
 * máximo [PRESENCE_CLOCK_MAX_WAIT_MS] e o relógio ([clock]) é relido depois dela: a espera de corrotina usa tempo monotônico, que não
 * conta o sono do aparelho, então uma espera única até a próxima fronteira (até 48 h) deixaria a tela atrasada ao acordar.
 */
suspend fun trackPresenceClock(appointments: List<PatientAppointment>, start: Long, clock: () -> Long, onChange: (Long) -> Unit,
    pause: suspend (Long) -> Unit = { kotlinx.coroutines.delay(it) }) {
    var current = start
    while (true) {
        val boundary = nextPresenceBoundary(appointments, current) ?: return
        val remaining = boundary - clock()
        if (remaining > 0) pause(minOf(remaining, PRESENCE_CLOCK_MAX_WAIT_MS))
        else { current = clock(); onChange(current) }
    }
}

/** Regra do limite por especialidade no cartão "Como funciona"; agindo por um dependente, fala das UBS dele (o servidor as conta por pessoa). */
fun bookingLimitRuleText(actingName: String?): String =
    "Vale uma consulta futura marcada pelo aplicativo por especialidade, somando " +
        (if (actingName == null) "todas as suas UBS" else "as UBS de $actingName") + ". Ao cancelar, " +
        (if (actingName == null) "você libera" else "libera") + " outra."

/** Aviso do diálogo de marcação; o cancelamento pesa no score de comparecimento de quem tem a consulta (o dependente, se for o caso). */
fun bookingCancelWarningText(actingName: String?): String =
    "Se precisar cancelar, o cancelamento conta no " + (if (actingName == null) "seu score" else "score") + " de comparecimento" +
        (if (actingName == null) "" else " de $actingName") + " conforme a antecedência."
