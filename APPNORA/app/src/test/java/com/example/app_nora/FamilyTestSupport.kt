package com.example.app_nora

import com.example.app_nora.api.*

/** Endereço em memória para os testes do controlador. */
internal class MemoryAddressStore(var address: String = "https://nora.example.test") : AddressStore {
    override fun load() = address
    override fun save(address: String) { this.address = address }
}

/**
 * API falsa de comunicados, Jornada, família e e-mail: cada chamada fica registrada em [calls] como
 * `own.<operação> <ids>` (rotas do próprio paciente) ou `dep.<operação> <vínculo> <ids>` (rotas do responsável pelo dependente).
 */
internal class FamilyFakeApi : PatientApi {
    val calls = mutableListOf<String>()
    fun callsOf(prefix: String) = calls.filter { it.startsWith(prefix) }

    var units = listOf<PatientHealthUnit>()

    // Próprio paciente
    val ownAppointments = mutableMapOf<String, List<PatientAppointment>>()
    val ownQueue = mutableMapOf<String, List<UnitQueueEntry>>()
    val ownOffers = mutableMapOf<String, List<UnitOffer>>()

    // Dependentes (por id do vínculo)
    val depAppointments = mutableMapOf<String, List<PatientAppointment>>()
    val depQueue = mutableMapOf<String, List<UnitQueueEntry>>()
    val depOffers = mutableMapOf<String, List<UnitOffer>>()
    val depRecords = mutableMapOf<String, UnitRecords>()
    val depAttendance = mutableMapOf<String, UnitAttendance>()
    var depSlots = listOf<AvailableSlot>()
    var depBookResult: PatientAppointment? = null
    var depFailure: Throwable? = null
    var depRespondResult = OfferResponse(true, "novo")
    var specialtyList = listOf<Specialty>()

    // Comunicados
    var announcementsHandler: (Long?) -> AnnouncementPage = { AnnouncementPage(emptyList(), 0, false) }
    val announcementAfter = mutableListOf<Long?>()
    val readIds = mutableListOf<String>()
    var announcementsFailure: Throwable? = null
    var markReadFailure: Throwable? = null

    // Jornada
    var records: List<UnitRecords> = emptyList()
    var recordsFailure: Throwable? = null

    // Família
    var links = mutableListOf<FamilyLink>()
    var linksFailure: Throwable? = null
    var requestResult: FamilyLink? = null
    var requestFailure: Throwable? = null
    val requested = mutableListOf<Pair<String, String>>()
    var changeResult: FamilyLink? = null
    var changeFailure: Throwable? = null
    var dependentOffersResult = listOf<DependentOffers>()

    // Agendamento e oferta do próprio paciente (falha única, para os testes de 409)
    var ownSlots = listOf<AvailableSlot>()
    var ownFailure: Throwable? = null

    // Serviços e horários da UBS
    val aboutByUnit = mutableMapOf<String, UnitProfile>()
    var aboutFailure: Throwable? = null
    var aboutGate: kotlinx.coroutines.CompletableDeferred<Unit>? = null

    // Google
    var googleStatusValue = GoogleStatus(true, false, null)
    var googleFailure: Throwable? = null
    var googleLoginFailure: Throwable? = null
    val googleIdTokens = mutableListOf<String>()

    // Push
    var pushEnabledValue = true
    var pushFailure: Throwable? = null
    var pushRemoveFailure: Throwable? = null
    var pushRemoveGate: kotlinx.coroutines.CompletableDeferred<Unit>? = null

    // E-mail
    var suggestion: EmailSuggestion? = null
    var suggestionFailure: Throwable? = null
    var sendFailure: Throwable? = null
    var confirmFailure: Throwable? = null
    var codeIsValid = true
    val confirmedCodes = mutableListOf<String>()
    var dismissFailure: Throwable? = null

    override suspend fun login(identifier: String, password: String) =
        PatientSession("token-1", "2099-01-01T00:00:00Z", PatientUser("patient-id", "Paciente de teste"))
    override suspend fun units(token: String) = units
    override suspend fun logout(token: String) = Unit
    override suspend fun presence(token: String, online: Boolean) = Unit
    override suspend fun specialties(token: String) = specialtyList
    override suspend fun linkUnits(token: String, unitIds: List<String>) = UnitLinkResult(unitIds, emptyList())

    // ---- próprio
    override suspend fun appointments(token: String, unitId: String): List<PatientAppointment> { calls += "own.appointments $unitId"; return ownAppointments[unitId].orEmpty() }
    override suspend fun unitQueue(token: String, unitId: String): List<UnitQueueEntry> { calls += "own.queue $unitId"; return ownQueue[unitId].orEmpty() }
    override suspend fun unitOffers(token: String, unitId: String): List<UnitOffer> { calls += "own.offers $unitId"; return ownOffers[unitId].orEmpty() }
    override suspend fun cancelAppointment(token: String, unitId: String, id: String) { calls += "own.cancelAppointment $unitId $id" }
    override suspend fun confirmPresence(token: String, unitId: String, id: String): PatientAppointment { calls += "own.confirm $unitId $id"; error("não esperado") }
    override suspend fun cancelQueueEntry(token: String, unitId: String, entryId: Long) { calls += "own.cancelQueue $unitId $entryId" }
    override suspend fun attendance(token: String, unitId: String): UnitAttendance { calls += "own.attendance $unitId"; return UnitAttendance(1, 0, 0, 0.0, 1.0, 12) }
    override suspend fun availability(token: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long): List<AvailableSlot> {
        calls += "own.availability $unitId $specialtyId"; return ownSlots
    }
    override suspend fun bookAppointment(token: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String): PatientAppointment {
        calls += "own.book $unitId"; ownFailure?.let { throw it }; error("não esperado")
    }
    override suspend fun respondUnitOffer(token: String, unitId: String, id: String, accept: Boolean): OfferResponse {
        calls += "own.respond $unitId $id"; ownFailure?.let { throw it }; return OfferResponse(true, "x")
    }

    // ---- serviços e horários, Google e push
    override suspend fun unitAbout(token: String, unitId: String): UnitProfile {
        calls += "own.about $unitId"; aboutGate?.await(); aboutFailure?.let { throw it }
        return aboutByUnit[unitId] ?: UnitProfile(unitId, false, emptyList(), null, emptyList(), null)
    }
    override suspend fun loginWithGoogle(idToken: String, deviceName: String?): PatientSession {
        calls += "own.googleLogin"; googleIdTokens += idToken; googleLoginFailure?.let { throw it }
        return PatientSession("token-google", "2099-01-01T00:00:00Z", PatientUser("patient-id", "Paciente de teste"))
    }
    override suspend fun googleStatus(token: String): GoogleStatus { calls += "own.googleStatus"; googleFailure?.let { throw it }; return googleStatusValue }
    override suspend fun linkGoogle(token: String, idToken: String) {
        calls += "own.googleLink"; googleIdTokens += idToken; googleFailure?.let { throw it }; googleStatusValue = GoogleStatus(true, true, "paciente@gmail.example")
    }
    override suspend fun unlinkGoogle(token: String) { calls += "own.googleUnlink"; googleFailure?.let { throw it }; googleStatusValue = GoogleStatus(true, false, null) }
    override suspend fun pushEnabled(token: String): Boolean { calls += "own.pushEnabled"; return pushEnabledValue }
    override suspend fun registerPushToken(token: String, pushToken: String) { calls += "own.pushRegister $pushToken"; pushFailure?.let { throw it } }
    override suspend fun removePushToken(token: String, pushToken: String) {
        calls += "own.pushRemove $pushToken"; pushRemoveGate?.await(); pushRemoveFailure?.let { throw it }
    }

    // ---- dependente
    private fun depGate() { depFailure?.let { throw it } }
    override suspend fun dependentAppointments(token: String, dependentId: String, unitId: String): List<PatientAppointment> {
        calls += "dep.appointments $dependentId $unitId"; depGate(); return depAppointments[dependentId].orEmpty()
    }
    override suspend fun dependentQueue(token: String, dependentId: String, unitId: String): List<UnitQueueEntry> {
        calls += "dep.queue $dependentId $unitId"; depGate(); return depQueue[dependentId].orEmpty()
    }
    override suspend fun dependentUnitOffers(token: String, dependentId: String, unitId: String): List<UnitOffer> {
        calls += "dep.offers $dependentId $unitId"; depGate(); return depOffers[dependentId].orEmpty()
    }
    override suspend fun dependentCancelAppointment(token: String, dependentId: String, unitId: String, id: String) {
        calls += "dep.cancelAppointment $dependentId $unitId $id"; depGate()
    }
    override suspend fun dependentConfirmPresence(token: String, dependentId: String, unitId: String, id: String): PatientAppointment {
        calls += "dep.confirm $dependentId $unitId $id"; depGate()
        return depAppointments[dependentId].orEmpty().first { it.id == id }.copy(confirmedAt = "2026-11-17T09:30:00Z")
    }
    override suspend fun dependentCancelQueueEntry(token: String, dependentId: String, unitId: String, entryId: Long) {
        calls += "dep.cancelQueue $dependentId $unitId $entryId"; depGate()
    }
    override suspend fun dependentAttendance(token: String, dependentId: String, unitId: String): UnitAttendance {
        calls += "dep.attendance $dependentId $unitId"; depGate(); return depAttendance[dependentId] ?: UnitAttendance(0, 0, 0, 0.0, null, 12)
    }
    override suspend fun dependentAvailability(token: String, dependentId: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long): List<AvailableSlot> {
        calls += "dep.availability $dependentId $unitId $specialtyId"; depGate(); return depSlots
    }
    override suspend fun dependentBook(token: String, dependentId: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String): PatientAppointment {
        calls += "dep.book $dependentId $unitId $professionalId"; depGate(); return requireNotNull(depBookResult)
    }
    override suspend fun dependentRespondOffer(token: String, dependentId: String, unitId: String, id: String, accept: Boolean): OfferResponse {
        calls += "dep.respond $dependentId $unitId $id $accept"; depGate()
        depOffers[dependentId] = depOffers[dependentId].orEmpty().map { if (it.id == id) it.copy(status = if (accept) "accepted" else "declined") else it }
        return depRespondResult.copy(accepted = accept && depRespondResult.accepted)
    }
    override suspend fun dependentRecords(token: String, dependentId: String, unitId: String): UnitRecords {
        calls += "dep.records $dependentId $unitId"; depGate(); return depRecords[dependentId] ?: UnitRecords(unitId, "", emptyList(), emptyList())
    }

    // ---- comunicados e Jornada
    override suspend fun announcements(token: String, after: Long?): AnnouncementPage {
        calls += "own.announcements ${after ?: "-"}"; announcementAfter += after
        announcementsFailure?.let { throw it }
        return announcementsHandler(after)
    }
    override suspend fun markAnnouncementRead(token: String, id: String) { calls += "own.read $id"; markReadFailure?.let { throw it }; readIds += id }
    override suspend fun healthRecords(token: String): List<UnitRecords> { calls += "own.records"; recordsFailure?.let { throw it }; return records }

    // ---- família
    override suspend fun dependents(token: String): List<FamilyLink> { calls += "own.dependents"; linksFailure?.let { throw it }; return links.toList() }
    override suspend fun requestDependent(token: String, cpf: String, unitId: String): FamilyLink {
        calls += "own.requestDependent"; requested += cpf to unitId
        requestFailure?.let { throw it }
        return requireNotNull(requestResult)
    }
    override suspend fun cancelDependentRequest(token: String, id: String): FamilyLink {
        calls += "own.cancelRequest $id"; changeFailure?.let { throw it }
        return requireNotNull(changeResult)
    }
    override suspend fun endDependentLink(token: String, id: String): FamilyLink {
        calls += "own.endLink $id"; changeFailure?.let { throw it }
        return requireNotNull(changeResult)
    }
    override suspend fun dependentsOpenOffers(token: String): List<DependentOffers> { calls += "own.dependentOffers"; return dependentOffersResult }

    // ---- e-mail
    override suspend fun emailSuggestion(token: String): EmailSuggestion? { calls += "own.emailSuggestion"; suggestionFailure?.let { throw it }; return suggestion }
    override suspend fun sendEmailSuggestionCode(token: String) { calls += "own.sendCode"; sendFailure?.let { throw it } }
    override suspend fun confirmEmailSuggestion(token: String, code: String): Boolean {
        calls += "own.confirmCode"; confirmedCodes += code; confirmFailure?.let { throw it }; return codeIsValid
    }
    override suspend fun dismissEmailSuggestion(token: String) { calls += "own.dismiss"; dismissFailure?.let { throw it } }
}

internal val unitA = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", -23.5, -46.6)
internal val unitB = PatientHealthUnit("unit-b", "UBS B", "Norte", "Rua B", -20.0, -40.0)

internal fun failure(status: Int, code: String?, context: ApiContext) = ApiFailure(status, failureMessage(status, code, context), code)

internal fun link(id: String, status: String, unitId: String = "unit-a", unitName: String = "UBS A", name: String? = "Maria Souza",
    reason: String? = null) = FamilyLink(id, status, unitId, unitName, "***.456.789-**", name, "2026-11-10T12:00:00Z",
    if (status == "pending") null else "2026-11-11T12:00:00Z", reason, null)

internal fun announcement(id: String, sequence: Long, title: String = "Título $sequence", isRead: Boolean = false, expiresAt: String? = null) =
    Announcement(id, sequence, title, "Corpo $sequence", "2026-11-12T12:00:00Z", expiresAt, isRead, null)
