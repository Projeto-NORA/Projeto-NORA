package com.example.app_nora.api

// Regras puras (sem Android) da Família: situação dos pedidos, "agindo por" e avisos de oferta com o nome do dependente.

fun FamilyLink.isApproved(): Boolean = status == "approved"

/** Só dígitos: o CPF do dependente trafega apenas no corpo do pedido, nunca em URL. */
fun cpfDigits(value: String): String = value.filter { it in '0'..'9' }

/** O nome só existe depois da aprovação; antes (ou se a UBS não respondeu) mostra o CPF mascarado que o próprio responsável digitou. */
fun dependentDisplayName(link: FamilyLink): String =
    link.dependentName?.takeIf { it.isNotBlank() } ?: "CPF ${link.dependentCpfMasked}"

/** Nome e UBS: o vínculo é por UBS, então o mesmo dependente em duas UBS aparece duas vezes. */
fun familyLinkLabel(link: FamilyLink): String = "${dependentDisplayName(link)} · ${link.unitName}"

/** Tradução única das situações do pedido; nunca mostra o valor cru. */
fun familyStatusLabel(status: String): String = when (status) {
    "pending" -> "Aguardando a equipe da UBS"
    "approved" -> "Aprovado"
    "rejected" -> "Recusado"
    "cancelled" -> "Cancelado por você"
    "ended" -> "Vínculo encerrado"
    else -> "Situação não reconhecida"
}

data class FamilyGroups(val active: List<FamilyLink>, val history: List<FamilyLink>)

/** Pendentes e aprovados primeiro (como o servidor os ordena, do mais recente ao mais antigo); recusados, cancelados e encerrados no histórico. */
fun groupFamilyLinks(links: List<FamilyLink>): FamilyGroups =
    FamilyGroups(links.filter { it.status == "pending" || it.status == "approved" }, links.filter { it.status != "pending" && it.status != "approved" })

fun approvedLinks(links: List<FamilyLink>?): List<FamilyLink> = links.orEmpty().filter { it.isApproved() }

/** O vínculo aprovado pelo qual o responsável está agindo agora, ou nulo quando age por si. */
fun PatientState.actingLink(): FamilyLink? = actingFor?.let { id -> family.links?.firstOrNull { it.id == id && it.isApproved() } }

/**
 * A visão que as telas de Início, Consultas, Agendamento, Vaga Relâmpago e Jornada usam: sem "agindo por" é o próprio
 * estado; agindo por um dependente, a UBS do vínculo e os dados lidos pelas rotas do dependente ocupam o lugar dos do responsável.
 */
fun PatientState.effective(): PatientState {
    val link = actingLink() ?: return this
    val unit = units?.firstOrNull { it.id == link.unitId } ?: PatientHealthUnit(link.unitId, link.unitName, "", "", 0.0, 0.0)
    return copy(units = listOf(unit), selectedUnitId = link.unitId, unitData = mapOf(link.unitId to (dependentData[link.id] ?: UnitSnapshot())),
        loadingUnits = false)
}

// ---- Ofertas dos dependentes e avisos --------------------------------------------------------------------

data class DependentOfferRef(val dependentId: String, val dependentName: String, val unitId: String, val unitName: String, val offer: UnitOffer)

/** Ofertas ainda abertas (pendentes e no prazo, pelo relógio do aparelho) de todos os dependentes, da que vence primeiro para a última. */
fun openDependentOffers(dependents: List<DependentOffers>, now: Long): List<DependentOfferRef> =
    dependents.flatMap { dependent -> dependent.offers.filter { it.isOpenAt(now) }
        .map { DependentOfferRef(dependent.dependentId, dependent.dependentName, dependent.unitId, dependent.unitName, it) } }
        .sortedWith(compareBy<DependentOfferRef>({ it.offer.expiresAtMillis ?: Long.MAX_VALUE }, { it.offer.id }))

/** Uma oferta aberta a avisar: do próprio paciente (`dependentId` nulo) ou de um dependente (com o nome dele). */
data class OfferNotice(val offer: UnitOffer, val unitId: String, val unitName: String, val dependentId: String?, val dependentName: String?)

fun openOfferNotices(state: PatientState, now: Long): List<OfferNotice> {
    val own = openOffersAcrossUnits(state.units, state.unitData, now).map { OfferNotice(it.offer, it.unit.id, it.unit.name, null, null) }
    val dependents = openDependentOffers(state.dependentOffers, now).map { OfferNotice(it.offer, it.unitId, it.unitName, it.dependentId, it.dependentName) }
    return (own + dependents).sortedWith(compareBy<OfferNotice>({ it.offer.expiresAtMillis ?: Long.MAX_VALUE }, { it.offer.id }))
}

fun offerNoticeTitle(notice: OfferNotice): String =
    if (notice.dependentName == null) "Vaga Relâmpago · ${notice.offer.specialty}"
    else "Vaga Relâmpago para ${notice.dependentName} · ${notice.offer.specialty}"

fun offerNoticeText(notice: OfferNotice): String = "${notice.unitName} · responda até ${formatClock(notice.offer.expiresAt)}"

// ---- Toque na notificação de Vaga Relâmpago -------------------------------------------------------------------

/** O que fazer com o pedido de abrir a Vaga Relâmpago de uma notificação. */
enum class FlashOpen { WAIT, DROP, OPEN_OWN, OPEN_DEPENDENT }

/**
 * Decide o pedido de abrir a tela de Vaga Relâmpago da UBS [unitId] (de um dependente quando [dependentId] não é nulo). Troca de "agindo
 * por" e de UBS são recusadas enquanto uma ação está em andamento (`busy`): então o pedido espera, em vez de abrir a tela na visão
 * errada e perder o pedido. Sem sessão, ou sem a UBS/vínculo, o pedido é descartado.
 */
fun flashOpenDecision(unitId: String, dependentId: String?, state: PatientState): FlashOpen {
    if (state.session == null) return FlashOpen.DROP
    if (dependentId != null) {
        val links = state.family.links ?: return FlashOpen.WAIT
        if (links.none { it.id == dependentId && it.isApproved() }) return FlashOpen.DROP
        return if (state.busy && state.actingFor != dependentId) FlashOpen.WAIT else FlashOpen.OPEN_DEPENDENT
    }
    val units = state.units ?: return FlashOpen.WAIT
    if (units.none { it.id == unitId }) return FlashOpen.DROP
    val needsChange = state.actingFor != null || state.selectedUnitId != unitId
    return if (state.busy && needsChange) FlashOpen.WAIT else FlashOpen.OPEN_OWN
}

/** "Remover UBS" é uma ação do próprio responsável sobre o vínculo dele: só aparece com sessão, uma UBS selecionada e agindo por si. */
fun canRemoveUnit(state: PatientState, selected: PatientHealthUnit?): Boolean = state.session != null && selected != null && state.actingFor == null

/** Confere a pessoa, a UBS e o prazo antes de oferecer confirmação de uma ação da notificação. */
fun notificationDecisionOffer(state: PatientState, offerId: String?, unitId: String?, dependentId: String?, now: Long): UnitOffer? {
    if (state.session == null || state.busy || offerId == null || unitId == null || state.actingFor != dependentId) return null
    if (dependentId != null && state.actingLink() == null) return null
    val view = state.effective()
    if (view.selectedUnitId != unitId) return null
    return view.unitOffers?.firstOrNull { it.id == offerId && it.isOpenAt(now) }
}
