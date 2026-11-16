package com.example.app_nora.api

/** Repassa as chamadas autenticadas e avisa quando o servidor responde 403 `password-change-required`, qualquer que seja a rota. */
internal class PasswordGate(private val inner: PatientApi, private val onRequired: () -> Unit) : PatientApi by inner {
    private suspend inline fun <T> watch(block: () -> T): T =
        try { block() } catch (failure: ApiFailure) { if (failure.code == PASSWORD_CHANGE_REQUIRED) onRequired(); throw failure }
    override suspend fun units(token: String) = watch { inner.units(token) }
    override suspend fun linkUnits(token: String, unitIds: List<String>) = watch { inner.linkUnits(token, unitIds) }
    override suspend fun appointments(token: String, unitId: String) = watch { inner.appointments(token, unitId) }
    override suspend fun dependentUnitOffers(token: String, dependentId: String, unitId: String) = watch { inner.dependentUnitOffers(token, dependentId, unitId) }
    override suspend fun cancelAppointment(token: String, unitId: String, id: String) = watch { inner.cancelAppointment(token, unitId, id) }
    override suspend fun unitQueue(token: String, unitId: String) = watch { inner.unitQueue(token, unitId) }
    override suspend fun unitOffers(token: String, unitId: String) = watch { inner.unitOffers(token, unitId) }
    override suspend fun respondUnitOffer(token: String, unitId: String, id: String, accept: Boolean) = watch { inner.respondUnitOffer(token, unitId, id, accept) }
    override suspend fun attendance(token: String, unitId: String) = watch { inner.attendance(token, unitId) }
    override suspend fun cancelQueueEntry(token: String, unitId: String, entryId: Long) = watch { inner.cancelQueueEntry(token, unitId, entryId) }
    override suspend fun unlinkUnit(token: String, unitId: String) = watch { inner.unlinkUnit(token, unitId) }
    override suspend fun unlinkPreview(token: String, unitId: String) = watch { inner.unlinkPreview(token, unitId) }
    override suspend fun presence(token: String, online: Boolean) = watch { inner.presence(token, online) }
    override suspend fun specialties(token: String) = watch { inner.specialties(token) }
    override suspend fun availability(token: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long) =
        watch { inner.availability(token, unitId, specialtyId, fromMillis, toMillis) }
    override suspend fun bookAppointment(token: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String) =
        watch { inner.bookAppointment(token, unitId, professionalId, specialtyId, startsAt) }
    override suspend fun confirmPresence(token: String, unitId: String, id: String) = watch { inner.confirmPresence(token, unitId, id) }
    override suspend fun announcements(token: String, after: Long?) = watch { inner.announcements(token, after) }
    override suspend fun markAnnouncementRead(token: String, id: String) = watch { inner.markAnnouncementRead(token, id) }
    override suspend fun healthRecords(token: String) = watch { inner.healthRecords(token) }
    override suspend fun dependentRecords(token: String, dependentId: String, unitId: String) = watch { inner.dependentRecords(token, dependentId, unitId) }
    override suspend fun dependents(token: String) = watch { inner.dependents(token) }
    override suspend fun requestDependent(token: String, cpf: String, unitId: String) = watch { inner.requestDependent(token, cpf, unitId) }
    override suspend fun cancelDependentRequest(token: String, id: String) = watch { inner.cancelDependentRequest(token, id) }
    override suspend fun endDependentLink(token: String, id: String) = watch { inner.endDependentLink(token, id) }
    override suspend fun dependentsOpenOffers(token: String) = watch { inner.dependentsOpenOffers(token) }
    override suspend fun emailSuggestion(token: String) = watch { inner.emailSuggestion(token) }
    override suspend fun sendEmailSuggestionCode(token: String) = watch { inner.sendEmailSuggestionCode(token) }
    override suspend fun confirmEmailSuggestion(token: String, code: String) = watch { inner.confirmEmailSuggestion(token, code) }
    override suspend fun dismissEmailSuggestion(token: String) = watch { inner.dismissEmailSuggestion(token) }
}

/**
 * Faz as chamadas por UBS (consultas, agendamento, presença, fila, ofertas, participação) irem pelas rotas do responsável
 * `/api/patient/dependents/{dependentId}/units/{unitId}/...`. O controlador usa uma instância destas enquanto o responsável age por um
 * dependente; o restante (lista de UBS, senha, presença, comunicados) continua sendo do próprio responsável.
 */
internal class DependentScope(private val inner: PatientApi, private val dependentId: String) : PatientApi by inner {
    override suspend fun appointments(token: String, unitId: String) = inner.dependentAppointments(token, dependentId, unitId)
    override suspend fun cancelAppointment(token: String, unitId: String, id: String) = inner.dependentCancelAppointment(token, dependentId, unitId, id)
    override suspend fun confirmPresence(token: String, unitId: String, id: String) = inner.dependentConfirmPresence(token, dependentId, unitId, id)
    override suspend fun bookAppointment(token: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String) =
        inner.dependentBook(token, dependentId, unitId, professionalId, specialtyId, startsAt)
    override suspend fun availability(token: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long) =
        inner.dependentAvailability(token, dependentId, unitId, specialtyId, fromMillis, toMillis)
    override suspend fun unitQueue(token: String, unitId: String) = inner.dependentQueue(token, dependentId, unitId)
    override suspend fun cancelQueueEntry(token: String, unitId: String, entryId: Long) = inner.dependentCancelQueueEntry(token, dependentId, unitId, entryId)
    override suspend fun unitOffers(token: String, unitId: String) = inner.dependentUnitOffers(token, dependentId, unitId)
    override suspend fun respondUnitOffer(token: String, unitId: String, id: String, accept: Boolean) =
        inner.dependentRespondOffer(token, dependentId, unitId, id, accept)
    override suspend fun attendance(token: String, unitId: String) = inner.dependentAttendance(token, dependentId, unitId)
}
