package com.example.app_nora.api

import java.text.Collator
import java.text.Normalizer
import java.util.Calendar
import java.util.Locale
import java.util.TimeZone

// Regras puras (sem Android) da apresentação: tipo do pop-up de aviso, ordem alfabética, máscara de CPF, iniciais,
// horários agrupados por profissional e o recorte da foto de perfil. Ficam aqui para os testes locais.

private val PT_BR: Locale = Locale.forLanguageTag("pt-BR")

// ---- Pop-up padrão de avisos -------------------------------------------------------------------------------

/** Tipo do pop-up padrão: define ícone, cor e título curto. */
enum class NoticeKind(val title: String) { SUCCESS("Tudo certo"), WARNING("Atenção"), ERROR("Algo deu errado"), INFO("Aviso") }

private fun plain(text: String): String =
    Normalizer.normalize(text.lowercase(PT_BR), Normalizer.Form.NFD).replace(Regex("\\p{M}+"), "")

private val ERROR_HINTS = listOf("erro", "incompativel", "invalid", "incorret", "nao foi possivel", "nao conseguimos", "indisponivel",
    "nao encontr", "confira", "recusou")
private val WARNING_HINTS = listOf("sessao terminou", "nao esta mais disponivel", "nao existe mais", "aguarde", "limite", "ja tem",
    "desativad", "muitas tentativas", "muitos pedidos", "prazo", "nao confirmou", "permita", "rede local", "ainda nao tem")
private val SUCCESS_HINTS = listOf("cancelada", "agendada", "antecipada", "marcada", "confirmada", "aceita", "alterad", "trocado",
    "enviamos", "vinculad", "removid", "atualizada", "sucesso", "salv", "pronto")

/**
 * Classifica o texto de resultado de uma ação para o pop-up: aviso de prazo, limite ou sessão é [NoticeKind.WARNING]; falha é
 * [NoticeKind.ERROR]; ação concluída é [NoticeKind.SUCCESS]; o resto é [NoticeKind.INFO]. Ignora maiúsculas e acentos.
 */
fun noticeKindOf(message: String): NoticeKind {
    val text = plain(message)
    return when {
        WARNING_HINTS.any { it in text } -> NoticeKind.WARNING
        ERROR_HINTS.any { it in text } -> if (SUCCESS_HINTS.any { it in text }) NoticeKind.WARNING else NoticeKind.ERROR
        SUCCESS_HINTS.any { it in text } -> NoticeKind.SUCCESS
        else -> NoticeKind.INFO
    }
}

// ---- Ordem alfabética ----------------------------------------------------------------------------------------

/** Comparação de nomes em português: ignora maiúsculas e acentos ("Árvore" fica junto de "Arvore"). */
val alphabetical: Comparator<String> = Collator.getInstance(PT_BR).apply { strength = Collator.PRIMARY }.let { collator ->
    Comparator { a, b -> collator.compare(a.trim(), b.trim()) }
}

/** A lista em ordem alfabética A–Z pelo nome dado por [name]. */
fun <T> List<T>.sortedAlphabetically(name: (T) -> String): List<T> = sortedWith(compareBy(alphabetical, name))

// ---- Identidade no Perfil ----------------------------------------------------------------------------------

/** CPF mascarado "•••.456.789-••"; texto que já vem mascarado (ou sem 11 dígitos) é devolvido como veio. */
fun maskCpf(value: String): String {
    val digits = value.filter { it in '0'..'9' }
    if (digits.length != 11) return value.trim()
    return "•••.${digits.substring(3, 6)}.${digits.substring(6, 9)}-••"
}

/** Iniciais do nome para o avatar sem foto: primeira letra do primeiro e do último nome ("Ana Maria Souza" vira "AS"). */
fun initialsOf(fullName: String): String {
    val parts = fullName.trim().split(Regex("\\s+")).filter { part -> part.any { it.isLetter() } }
    if (parts.isEmpty()) return "?"
    val first = parts.first().first { it.isLetter() }
    val last = if (parts.size > 1) parts.last().first { it.isLetter() } else null
    return (first.toString() + (last?.toString() ?: "")).uppercase(PT_BR)
}

// ---- Agendamento: horários de um dia por profissional ---------------------------------------------------------

/** Horários livres de um profissional num dia, separados em manhã (antes do meio-dia) e tarde. */
data class ProfessionalDay(val professionalId: String, val professionalName: String, val room: String?,
    val morning: List<AvailableSlot>, val afternoon: List<AvailableSlot>) {
    val total: Int get() = morning.size + afternoon.size
}

/** Agrupa os horários de [slots] (de um mesmo dia) por profissional, em ordem alfabética, cada grupo do mais cedo ao mais tarde. */
fun groupByProfessional(slots: List<AvailableSlot>, zone: TimeZone = TimeZone.getDefault()): List<ProfessionalDay> =
    slots.groupBy { it.professionalId }.map { (id, items) ->
        val sorted = items.sortedBy { it.startsAtMillis ?: Long.MAX_VALUE }
        val (morning, afternoon) = sorted.partition { slot ->
            val start = slot.startsAtMillis ?: return@partition false
            Calendar.getInstance(zone).apply { timeInMillis = start }.get(Calendar.HOUR_OF_DAY) < 12
        }
        ProfessionalDay(id, items.first().professionalName, items.firstNotNullOfOrNull { it.room }, morning, afternoon)
    }.sortedAlphabetically { it.professionalName }

/** Quantos chips de horário aparecem antes do "+N". */
const val SLOT_CHIPS_COLLAPSED = 6

/**
 * Divide os horários de um profissional entre manhã e tarde respeitando o limite de [limit] chips quando recolhido. Devolve o que
 * aparece em cada turno e quantos ficaram escondidos (o número do chip "+N").
 */
fun visibleSlots(day: ProfessionalDay, expanded: Boolean, limit: Int = SLOT_CHIPS_COLLAPSED): Triple<List<AvailableSlot>, List<AvailableSlot>, Int> {
    if (expanded || day.total <= limit) return Triple(day.morning, day.afternoon, 0)
    val morning = day.morning.take(limit)
    val afternoon = day.afternoon.take(limit - morning.size)
    return Triple(morning, afternoon, day.total - morning.size - afternoon.size)
}

// ---- Foto de perfil -----------------------------------------------------------------------------------------

/** Lado máximo da foto enviada ao servidor. */
const val PHOTO_MAX_SIDE = 512

/** Recorte quadrado central de uma imagem [width] × [height]: esquerda, topo e lado. */
fun squareCrop(width: Int, height: Int): Triple<Int, Int, Int> {
    require(width > 0 && height > 0) { "Imagem vazia." }
    val side = minOf(width, height)
    return Triple((width - side) / 2, (height - side) / 2, side)
}

/** Lado final da foto: o do recorte, sem passar de [PHOTO_MAX_SIDE] (nunca aumenta a imagem). */
fun photoSide(cropSide: Int): Int = minOf(cropSide, PHOTO_MAX_SIDE)
