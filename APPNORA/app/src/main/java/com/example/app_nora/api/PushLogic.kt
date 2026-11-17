package com.example.app_nora.api

// Regras puras (sem Android) do push do servidor: o que o `data` da mensagem pode pedir, a tela que ele abre e o texto genérico.
// O push nunca leva dado pessoal: só o tipo do evento e identificadores opacos (GUID) para abrir a tela certa.

enum class PushType(val wire: String) {
    LIGHTNING_OFFER("lightning-offer"), ANNOUNCEMENT("announcement"), PRESENCE_WINDOW("presence-window"), FAMILY_DECISION("family-decision");

    companion object { fun of(value: String?): PushType? = entries.firstOrNull { it.wire == value } }
}

/** Telas que um push pode abrir. */
enum class PushScreen { FLASH, NOTICES, APPOINTMENTS, FAMILY }

/** [unitId], [dependentId] e [appointmentId] são GUIDs opacos; um valor que não seja GUID é descartado como se não existisse. */
data class PushTarget(val type: PushType, val unitId: String?, val dependentId: String?, val appointmentId: String?)

/** O pedido de abrir uma tela: [unitId] e [dependentId] só existem nas telas de uma UBS (oferta e consultas). */
data class OpenRequest(val screen: PushScreen, val unitId: String? = null, val dependentId: String? = null)

private val OPAQUE_ID = Regex("[a-fA-F0-9-]{36}")

/** Chaves do `data` do push (iguais às que o sistema põe nos extras do Intent quando o app está em segundo plano). */
const val PUSH_TYPE = "type"
const val PUSH_UNIT_ID = "unitId"
const val PUSH_DEPENDENT_ID = "dependentId"
const val PUSH_APPOINTMENT_ID = "appointmentId"

/** `null` quando o tipo é desconhecido ou falta a UBS num tipo que precisa dela (oferta e janela de presença). */
fun parsePush(read: (String) -> String?): PushTarget? {
    val type = PushType.of(read(PUSH_TYPE)) ?: return null
    fun id(key: String) = read(key)?.takeIf { OPAQUE_ID.matches(it) }
    val unit = id(PUSH_UNIT_ID)
    if ((type == PushType.LIGHTNING_OFFER || type == PushType.PRESENCE_WINDOW) && unit == null) return null
    return PushTarget(type, unit, id(PUSH_DEPENDENT_ID), id(PUSH_APPOINTMENT_ID))
}

/** Oferta abre a Vaga Relâmpago (pela mesma decisão do toque na notificação local), comunicado abre Avisos, janela de presença abre Consultas e decisão de família abre Família. */
fun PushTarget.toOpenRequest(): OpenRequest = when (type) {
    PushType.LIGHTNING_OFFER -> OpenRequest(PushScreen.FLASH, unitId, dependentId)
    PushType.PRESENCE_WINDOW -> OpenRequest(PushScreen.APPOINTMENTS, unitId, dependentId)
    PushType.ANNOUNCEMENT -> OpenRequest(PushScreen.NOTICES)
    PushType.FAMILY_DECISION -> OpenRequest(PushScreen.FAMILY)
}

/** Título e texto genéricos de cada evento: sem nome, CPF, especialidade nem dado de saúde. */
fun pushText(type: PushType): Pair<String, String> = when (type) {
    PushType.LIGHTNING_OFFER -> "Vaga Relâmpago" to "Há uma nova oportunidade de consulta para você. Abra o NORA."
    PushType.ANNOUNCEMENT -> "Novo comunicado" to "Há um novo comunicado da rede. Abra o NORA."
    PushType.PRESENCE_WINDOW -> "Confirme sua presença" to "Já dá para confirmar a presença em uma consulta. Abra o NORA."
    PushType.FAMILY_DECISION -> "Pedido de família" to "Há uma resposta para o seu pedido de vínculo. Abra o NORA."
}

/** Oferta e comunicado respeitam as preferências já existentes; os demais avisos não têm preferência própria. */
fun pushAllowed(type: PushType, flashEnabled: Boolean, announcementEnabled: Boolean): Boolean = when (type) {
    PushType.LIGHTNING_OFFER -> flashEnabled
    PushType.ANNOUNCEMENT -> announcementEnabled
    else -> true
}
