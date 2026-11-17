package com.example.app_nora.api

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.util.Calendar

data class PatientUser(val id: String, val fullName: String)
data class PatientSession(val token: String, val expiresAt: String, val user: PatientUser)
data class RegistrationUnit(val id: String, val name: String, val region: String)
data class RegistrationEmailOption(val id: String, val maskedEmail: String)
data class RegistrationStart(val challengeId: String?, val emails: List<RegistrationEmailOption>)
data class RegistrationResult(val codeValid: Boolean, val linkedUnits: List<String>, val unmatchedUnits: List<String>)
data class UnitLinkResult(val linkedUnits: List<String>, val unmatchedUnits: List<String>)
data class PatientHealthUnit(val id: String, val name: String, val region: String, val address: String, val latitude: Double, val longitude: Double)
/**
 * `confirmedAt` é nulo até o paciente confirmar a presença; `bookedBy` é `staff`, `patient` ou `lightning`
 * (consultas de servidor mais antigo, sem o campo, contam como da equipe). `bookedByYou` diz se a conta que lê marcou a consulta
 * pelo app (para si ou como responsável) e `bookedByGuardianName` é o responsável que a marcou por vínculo de Família; servidor
 * antigo, sem esses campos, deixa os dois vazios.
 */
data class PatientAppointment(val id: String, val specialty: String, val professional: String, val startsAt: String, val endsAt: String, val status: String,
    val confirmedAt: String? = null, val bookedBy: String = "staff", val bookedByYou: Boolean = false, val bookedByGuardianName: String? = null) {
    /** Instante de início já interpretado uma vez; `null` quando o servidor enviou uma data ilegível. */
    val startsAtMillis: Long? = parseInstantMillis(startsAt)
}
/** Especialidade ativa do catálogo único da rede. */
data class Specialty(val id: String, val name: String)
/** Atendimento livre da grade de uma UBS; `startsAt` é devolvido ao servidor exatamente como veio. */
data class AvailableSlot(val startsAt: String, val endsAt: String, val professionalId: String, val professionalName: String,
    val specialtyId: String, val specialtyName: String) {
    val startsAtMillis: Long? = parseInstantMillis(startsAt)
}
data class UnitQueueEntry(val id: Long, val specialty: String, val enteredAt: String, val status: String) {
    val enteredAtMillis: Long? = parseInstantMillis(enteredAt)
}
data class UnitOffer(val id: String, val specialty: String, val professional: String, val startsAt: String, val endsAt: String, val expiresAt: String, val status: String) {
    val expiresAtMillis: Long? = parseInstantMillis(expiresAt)
}
/** Participação do próprio paciente em uma UBS nos últimos [windowMonths] meses; `score` (0 a 1) é nulo sem consulta encerrada. */
data class UnitAttendance(val completed: Int, val missed: Int, val cancelledByPatient: Int, val cancellationWeight: Double,
    val score: Double?, val windowMonths: Int)
/** O que o servidor cancelou ao remover o vínculo com uma UBS. */
data class UnlinkResult(val cancelledAppointments: Int, val cancelledEntries: Int)
/** Resultado do servidor para uma resposta: `accepted` só é verdadeiro quando a consulta foi criada. */
data class OfferResponse(val accepted: Boolean, val appointmentId: String?)
/** Comunicado da rede para o paciente; `sequence` ordena e serve de cursor, `title` e `body` são texto puro. */
data class Announcement(val id: String, val sequence: Long, val title: String, val body: String, val publishedAt: String,
    val expiresAt: String?, val isRead: Boolean, val readAt: String?) {
    val expiresAtMillis: Long? = expiresAt?.let { parseInstantMillis(it) }
}
/** Uma página da rota incremental: `cursor` é o valor para a próxima chamada; `hasMore` pede nova leitura com ele. */
data class AnnouncementPage(val announcements: List<Announcement>, val cursor: Long, val hasMore: Boolean)

/** Exame da Jornada: `status` é `requested`, `done`, `cancelled` ou `voided`; `performedOn` é data de calendário (`yyyy-MM-dd`). */
data class ExamRecord(val id: String, val name: String, val status: String, val requestedAt: String, val performedOn: String?, val note: String?)
/** Vacina aplicada: `dose` é `1`, `2`, `3`, `booster` ou `single`; `appliedOn` é data de calendário (`yyyy-MM-dd`). */
data class VaccineRecord(val id: String, val name: String, val dose: String, val appliedOn: String, val status: String, val note: String?)
/** Exames e vacinas de uma UBS (`unitName` vem do servidor só na rota agregada). */
data class UnitRecords(val unitId: String, val unitName: String, val exams: List<ExamRecord>, val vaccines: List<VaccineRecord>)

/**
 * Pedido ou vínculo de família visto pelo responsável: `status` é `pending`, `approved`, `rejected`, `cancelled` ou `ended`.
 * `dependentName` só vem depois da aprovação; antes disso só existe o CPF mascarado que o próprio responsável digitou.
 */
data class FamilyLink(val id: String, val status: String, val unitId: String, val unitName: String, val dependentCpfMasked: String,
    val dependentName: String?, val requestedAt: String, val decidedAt: String?, val decisionReason: String?, val closedAt: String?)
/** Ofertas ainda abertas de um dependente aprovado (rota agregada do ciclo de 30 s). */
data class DependentOffers(val dependentId: String, val unitId: String, val unitName: String, val dependentName: String, val offers: List<UnitOffer>)
/** Sugestão da UBS para trocar o e-mail da conta; o endereço novo só chega mascarado. */
data class EmailSuggestion(val id: String, val maskedEmail: String, val unitId: String, val unitName: String, val createdAt: String)

/** Um intervalo de funcionamento da UBS: `weekday` 0 = domingo; `opens` e `closes` são `HH:mm`. */
data class OpeningInterval(val weekday: Int, val opens: String, val closes: String)
/**
 * Horário de funcionamento, serviços e especialidades da grade de uma UBS. `informed` falso é "não informado" (diferente de fechado:
 * com `informed` e sem intervalos a UBS fica fechada todos os dias); `services` é texto puro como a UBS escreveu.
 */
data class UnitProfile(val unitId: String, val informed: Boolean, val openingHours: List<OpeningInterval>, val services: String?,
    val specialties: List<Specialty>, val updatedAt: String?)
/** Situação do Google na conta: `enabled` é o servidor configurado; `linked` diz se há uma conta Google vinculada (e o e-mail dela). */
data class GoogleStatus(val enabled: Boolean, val linked: Boolean, val email: String?)

/** `code` é o campo fixo do problem+json, já filtrado para os valores conhecidos; nunca o texto do servidor. */
class ApiFailure(val status: Int, message: String, val code: String? = null) : IOException(message)

interface PatientApi {
    suspend fun login(identifier: String, password: String): PatientSession
    suspend fun requestPasswordReset(cpf: String): String = throw ApiFailure(501, "Recuperação de senha indisponível.")
    suspend fun confirmPasswordReset(challengeId: String, code: String, newPassword: String): Boolean = false
    suspend fun startRegistration(name: String, cpf: String, password: String, unitIds: List<String>): RegistrationStart = throw ApiFailure(501, "Cadastro indisponível.")
    suspend fun sendRegistrationCode(challengeId: String, emailOptionId: String): Unit = throw ApiFailure(501, "Cadastro indisponível.")
    suspend fun confirmRegistration(challengeId: String, code: String): RegistrationResult = throw ApiFailure(501, "Cadastro indisponível.")
    suspend fun registrationUnits(): List<RegistrationUnit> = throw ApiFailure(501, "Lista de unidades indisponível.")
    suspend fun units(token: String): List<PatientHealthUnit> = throw ApiFailure(501, "Lista de unidades indisponível.")
    suspend fun linkUnits(token: String, unitIds: List<String>): UnitLinkResult = throw ApiFailure(501, "Vínculo indisponível.")
    suspend fun appointments(token: String, unitId: String): List<PatientAppointment> = throw ApiFailure(501, "Agenda indisponível.")
    suspend fun cancelAppointment(token: String, unitId: String, id: String): Unit = throw ApiFailure(501, "Cancelamento indisponível.")
    suspend fun unitQueue(token: String, unitId: String): List<UnitQueueEntry> = throw ApiFailure(501, "Fila indisponível.")
    suspend fun unitOffers(token: String, unitId: String): List<UnitOffer> = throw ApiFailure(501, "Ofertas indisponíveis.")
    suspend fun respondUnitOffer(token: String, unitId: String, id: String, accept: Boolean): OfferResponse = throw ApiFailure(501, "Oferta indisponível.")
    suspend fun attendance(token: String, unitId: String): UnitAttendance = throw ApiFailure(501, "Participação indisponível.")
    suspend fun cancelQueueEntry(token: String, unitId: String, entryId: Long): Unit = throw ApiFailure(501, "Cancelamento indisponível.")
    suspend fun unlinkUnit(token: String, unitId: String): UnlinkResult = throw ApiFailure(501, "Remoção indisponível.")
    /** Exatamente o que a remoção do vínculo cancelaria agora: todas as consultas futuras e entradas ativas de fila na UBS. */
    suspend fun unlinkPreview(token: String, unitId: String): UnlinkImpact = throw ApiFailure(501, "Remoção indisponível.")
    suspend fun changePassword(token: String, currentPassword: String, newPassword: String): Unit = throw ApiFailure(501, "Troca de senha indisponível.")
    suspend fun specialties(token: String): List<Specialty> = throw ApiFailure(501, "Especialidades indisponíveis.")
    /** Horários livres no intervalo [fromMillis, toMillis); o servidor ainda recorta a janela de 24 h a 90 dias. */
    suspend fun availability(token: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long): List<AvailableSlot> =
        throw ApiFailure(501, "Horários indisponíveis.")
    suspend fun bookAppointment(token: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String): PatientAppointment =
        throw ApiFailure(501, "Agendamento indisponível.")
    /** Idempotente no servidor: repetir devolve a consulta com o `confirmedAt` original. */
    suspend fun confirmPresence(token: String, unitId: String, id: String): PatientAppointment = throw ApiFailure(501, "Confirmação indisponível.")
    // ---- Comunicados, Jornada, família e e-mail da conta ----
    /** Sem [after] devolve tudo o que está em vigor; com ele, só o que tem `sequence` maior. */
    suspend fun announcements(token: String, after: Long?): AnnouncementPage = throw ApiFailure(501, "Comunicados indisponíveis.")
    suspend fun markAnnouncementRead(token: String, id: String): Unit = throw ApiFailure(501, "Comunicados indisponíveis.")
    /** Exames e vacinas do próprio paciente em todas as UBS vinculadas e prontas (uma chamada). */
    suspend fun healthRecords(token: String): List<UnitRecords> = throw ApiFailure(501, "Jornada indisponível.")
    suspend fun dependents(token: String): List<FamilyLink> = throw ApiFailure(501, "Família indisponível.")
    suspend fun requestDependent(token: String, cpf: String, unitId: String): FamilyLink = throw ApiFailure(501, "Família indisponível.")
    suspend fun cancelDependentRequest(token: String, id: String): FamilyLink = throw ApiFailure(501, "Família indisponível.")
    suspend fun endDependentLink(token: String, id: String): FamilyLink = throw ApiFailure(501, "Família indisponível.")
    /** Ofertas abertas de todos os dependentes aprovados; só lê, para o ciclo de 30 s. */
    suspend fun dependentsOpenOffers(token: String): List<DependentOffers> = throw ApiFailure(501, "Família indisponível.")
    suspend fun emailSuggestion(token: String): EmailSuggestion? = throw ApiFailure(501, "Troca de e-mail indisponível.")
    suspend fun sendEmailSuggestionCode(token: String): Unit = throw ApiFailure(501, "Troca de e-mail indisponível.")
    suspend fun confirmEmailSuggestion(token: String, code: String): Boolean = throw ApiFailure(501, "Troca de e-mail indisponível.")
    suspend fun dismissEmailSuggestion(token: String): Unit = throw ApiFailure(501, "Troca de e-mail indisponível.")

    // ---- Rotas do responsável pelo dependente (espelham as do próprio paciente, sob /dependents/{dependentId}/units/{unitId}) ----
    suspend fun dependentAppointments(token: String, dependentId: String, unitId: String): List<PatientAppointment> = throw ApiFailure(501, "Agenda indisponível.")
    suspend fun dependentCancelAppointment(token: String, dependentId: String, unitId: String, id: String): Unit = throw ApiFailure(501, "Cancelamento indisponível.")
    suspend fun dependentConfirmPresence(token: String, dependentId: String, unitId: String, id: String): PatientAppointment = throw ApiFailure(501, "Confirmação indisponível.")
    suspend fun dependentBook(token: String, dependentId: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String): PatientAppointment =
        throw ApiFailure(501, "Agendamento indisponível.")
    suspend fun dependentAvailability(token: String, dependentId: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long): List<AvailableSlot> =
        throw ApiFailure(501, "Horários indisponíveis.")
    suspend fun dependentQueue(token: String, dependentId: String, unitId: String): List<UnitQueueEntry> = throw ApiFailure(501, "Fila indisponível.")
    suspend fun dependentCancelQueueEntry(token: String, dependentId: String, unitId: String, entryId: Long): Unit = throw ApiFailure(501, "Cancelamento indisponível.")
    suspend fun dependentUnitOffers(token: String, dependentId: String, unitId: String): List<UnitOffer> = throw ApiFailure(501, "Ofertas indisponíveis.")
    suspend fun dependentRespondOffer(token: String, dependentId: String, unitId: String, id: String, accept: Boolean): OfferResponse = throw ApiFailure(501, "Oferta indisponível.")
    suspend fun dependentAttendance(token: String, dependentId: String, unitId: String): UnitAttendance = throw ApiFailure(501, "Participação indisponível.")
    suspend fun dependentRecords(token: String, dependentId: String, unitId: String): UnitRecords = throw ApiFailure(501, "Jornada indisponível.")
    // ---- Serviços e horários da UBS ----
    suspend fun unitAbout(token: String, unitId: String): UnitProfile = throw ApiFailure(501, "Informações da UBS indisponíveis.")
    suspend fun dependentUnitAbout(token: String, dependentId: String, unitId: String): UnitProfile = throw ApiFailure(501, "Informações da UBS indisponíveis.")

    // ---- Entrar com Google e push (desligados no servidor sem credenciais) ----
    /** G4: sessão de paciente a partir do ID token do Google. 401 genérico quando a conta Google não está vinculada. */
    suspend fun loginWithGoogle(idToken: String, deviceName: String? = null): PatientSession = throw ApiFailure(501, "Entrar com Google indisponível.")
    suspend fun googleStatus(token: String): GoogleStatus = throw ApiFailure(501, "Google indisponível.")
    suspend fun linkGoogle(token: String, idToken: String): Unit = throw ApiFailure(501, "Google indisponível.")
    suspend fun unlinkGoogle(token: String): Unit = throw ApiFailure(501, "Google indisponível.")
    /** P1: registra o token do aparelho para o push. */
    suspend fun registerPushToken(token: String, pushToken: String): Unit = throw ApiFailure(501, "Push indisponível.")
    /** P2: remove o token do aparelho (logout); idempotente. */
    suspend fun removePushToken(token: String, pushToken: String): Unit = throw ApiFailure(501, "Push indisponível.")
    suspend fun pushEnabled(token: String): Boolean = throw ApiFailure(501, "Push indisponível.")
    suspend fun logout(token: String)
    suspend fun presence(token: String, online: Boolean)
}

/** Identificadores de UBS e de registros chegam como GUID; qualquer outra coisa nunca vira caminho de URL. */
private val GUID = Regex("[a-fA-F0-9-]{36}")

class NoraApi(private val address: String) : PatientApi {
    override suspend fun requestPasswordReset(cpf: String): String = request(
        "/api/auth/password-reset/request", "POST", body = JSONObject().put("cpf", cpf), context = ApiContext.PASSWORD_RESET)
        .getString("challengeId")

    override suspend fun confirmPasswordReset(challengeId: String, code: String, newPassword: String): Boolean =
        request("/api/auth/password-reset/confirm", "POST", body = JSONObject()
            .put("challengeId", challengeId).put("code", code).put("newPassword", newPassword), context = ApiContext.PASSWORD_RESET)
            .getBoolean("codeValid")

    override suspend fun login(identifier: String, password: String): PatientSession {
        val body = request("/api/auth/login", "POST", body = JSONObject()
            .put("scope", "patient").put("identifier", identifier).put("password", password), context = ApiContext.LOGIN)
        return PatientSession(body.getString("token"), body.getString("expiresAt"), user(body.getJSONObject("user")))
    }

    override suspend fun startRegistration(name: String, cpf: String, password: String, unitIds: List<String>): RegistrationStart {
        val response = request("/api/registration/start", "POST", body = JSONObject()
            .put("fullName", name).put("cpf", cpf).put("password", password).put("unitIds", JSONArray(unitIds)), context = ApiContext.REGISTRATION)
        val emails = response.getJSONArray("emails")
        return RegistrationStart(if (response.isNull("challengeId")) null else response.getString("challengeId"),
            List(emails.length()) { index -> emails.getJSONObject(index).let { RegistrationEmailOption(it.getString("id"), it.getString("maskedEmail")) } })
    }
    override suspend fun sendRegistrationCode(challengeId: String, emailOptionId: String) {
        request("/api/registration/send-code", "POST", body = JSONObject().put("challengeId", challengeId).put("emailOptionId", emailOptionId), context = ApiContext.REGISTRATION)
    }
    override suspend fun confirmRegistration(challengeId: String, code: String): RegistrationResult {
        val response = request("/api/registration/confirm", "POST", body = JSONObject().put("challengeId", challengeId).put("code", code), context = ApiContext.REGISTRATION)
        fun ids(name: String): List<String> = response.getJSONArray(name).let { values -> List(values.length()) { values.getString(it) } }
        return RegistrationResult(response.getBoolean("codeValid"), ids("linkedUnits"), ids("unmatchedUnits"))
    }

    override suspend fun registrationUnits(): List<RegistrationUnit> {
        val units = request("/api/registration/units", context = ApiContext.REGISTRATION).getJSONArray("units")
        return List(units.length()) { index -> units.getJSONObject(index).let {
            RegistrationUnit(it.getString("id"), it.getString("name"), it.getString("region"))
        } }
    }

    override suspend fun units(token: String): List<PatientHealthUnit> {
        val units = request("/api/patient/units", token = token, context = ApiContext.UNIT_LIST).getJSONArray("units")
        return List(units.length()) { index -> units.getJSONObject(index).let {
            PatientHealthUnit(it.getString("id"), it.getString("name"), it.getString("region"),
                it.getString("address"), it.getDouble("latitude"), it.getDouble("longitude"))
        } }
    }

    override suspend fun linkUnits(token: String, unitIds: List<String>): UnitLinkResult {
        val response = request("/api/patient/units/link", "POST", token, JSONObject().put("unitIds", JSONArray(unitIds)), ApiContext.UNIT_LIST)
        fun ids(key: String): List<String> = response.getJSONArray(key).let { array -> List(array.length()) { array.getString(it) } }
        return UnitLinkResult(ids("linkedUnits"), ids("unmatchedUnits"))
    }

    // As rotas do próprio paciente e as do responsável pelo dependente só diferem no prefixo do caminho.
    private fun own(unitId: String): String {
        require(GUID.matches(unitId)) { "UBS inválida." }
        return "/api/patient/units/$unitId"
    }
    private fun delegated(dependentId: String, unitId: String): String {
        require(GUID.matches(dependentId) && GUID.matches(unitId)) { "Vínculo inválido." }
        return "/api/patient/dependents/$dependentId/units/$unitId"
    }

    override suspend fun appointments(token: String, unitId: String) = listAppointments(token, own(unitId))
    override suspend fun dependentAppointments(token: String, dependentId: String, unitId: String) =
        listAppointments(token, delegated(dependentId, unitId))

    private suspend fun listAppointments(token: String, prefix: String): List<PatientAppointment> {
        val from = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, -180) }.timeInMillis
        val to = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, 180) }.timeInMillis
        val path = "$prefix/appointments?from=${URLEncoder.encode(formatInstantUtc(from), "UTF-8")}&to=${URLEncoder.encode(formatInstantUtc(to), "UTF-8")}"
        val items = request(path, token = token, context = ApiContext.UNIT).getJSONArray("appointments")
        return List(items.length()) { index -> appointment(items.getJSONObject(index)) }
    }

    override suspend fun specialties(token: String): List<Specialty> {
        val items = request("/api/patient/specialties", token = token, context = ApiContext.SPECIALTIES).getJSONArray("specialties")
        return List(items.length()) { index -> items.getJSONObject(index).let { Specialty(it.getString("id"), it.getString("name")) } }
    }

    override suspend fun availability(token: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long) =
        slots(token, own(unitId), specialtyId, fromMillis, toMillis)
    override suspend fun dependentAvailability(token: String, dependentId: String, unitId: String, specialtyId: String, fromMillis: Long, toMillis: Long) =
        slots(token, delegated(dependentId, unitId), specialtyId, fromMillis, toMillis)

    private suspend fun slots(token: String, prefix: String, specialtyId: String, fromMillis: Long, toMillis: Long): List<AvailableSlot> {
        require(GUID.matches(specialtyId)) { "Especialidade inválida." }
        val path = "$prefix/availability?specialtyId=$specialtyId" +
            "&from=${URLEncoder.encode(formatInstantUtc(fromMillis), "UTF-8")}&to=${URLEncoder.encode(formatInstantUtc(toMillis), "UTF-8")}"
        val items = request(path, token = token, context = ApiContext.AVAILABILITY).getJSONArray("slots")
        return List(items.length()) { index -> items.getJSONObject(index).let {
            AvailableSlot(it.getString("startsAt"), it.getString("endsAt"), it.getString("professionalId"), it.getString("professionalName"),
                it.getString("specialtyId"), it.getString("specialtyName"))
        } }
    }

    override suspend fun bookAppointment(token: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String) =
        book(token, own(unitId), professionalId, specialtyId, startsAt)
    override suspend fun dependentBook(token: String, dependentId: String, unitId: String, professionalId: String, specialtyId: String, startsAt: String) =
        book(token, delegated(dependentId, unitId), professionalId, specialtyId, startsAt)

    private suspend fun book(token: String, prefix: String, professionalId: String, specialtyId: String, startsAt: String): PatientAppointment {
        require(GUID.matches(professionalId) && GUID.matches(specialtyId)) { "Horário inválido." }
        return appointment(request("$prefix/appointments", "POST", token, JSONObject()
            .put("professionalId", professionalId).put("specialtyId", specialtyId).put("startsAt", startsAt), ApiContext.BOOKING))
    }

    override suspend fun confirmPresence(token: String, unitId: String, id: String) = confirm(token, own(unitId), id)
    override suspend fun dependentConfirmPresence(token: String, dependentId: String, unitId: String, id: String) = confirm(token, delegated(dependentId, unitId), id)

    private suspend fun confirm(token: String, prefix: String, id: String): PatientAppointment {
        require(GUID.matches(id)) { "Consulta inválida." }
        return appointment(request("$prefix/appointments/$id/confirm", "POST", token, context = ApiContext.CONFIRMATION))
    }

    private fun appointment(body: JSONObject) = PatientAppointment(body.getString("id"), body.getString("specialty"), body.getString("professional"),
        body.getString("startsAt"), body.getString("endsAt"), body.getString("status"),
        body.stringOrNull("confirmedAt"), body.optString("bookedBy", "staff").ifBlank { "staff" },
        body.optBoolean("bookedByYou", false), body.stringOrNull("bookedByGuardianName")?.takeIf { it.isNotBlank() })

    override suspend fun cancelAppointment(token: String, unitId: String, id: String) = cancelAppt(token, own(unitId), id)
    override suspend fun dependentCancelAppointment(token: String, dependentId: String, unitId: String, id: String) = cancelAppt(token, delegated(dependentId, unitId), id)

    private suspend fun cancelAppt(token: String, prefix: String, id: String) {
        require(GUID.matches(id)) { "Consulta inválida." }
        request("$prefix/appointments/$id/cancel", "POST", token, JSONObject().put("confirmed", true), ApiContext.APPOINTMENT_CANCEL)
    }

    override suspend fun unitQueue(token: String, unitId: String) = queue(token, own(unitId))
    override suspend fun dependentQueue(token: String, dependentId: String, unitId: String) = queue(token, delegated(dependentId, unitId))

    private suspend fun queue(token: String, prefix: String): List<UnitQueueEntry> {
        val entries = request("$prefix/waiting-list", token = token, context = ApiContext.UNIT).getJSONArray("entries")
        return List(entries.length()) { index -> entries.getJSONObject(index).let { UnitQueueEntry(it.getLong("id"), it.getString("specialty"), it.getString("enteredAt"), it.getString("status")) } }
    }

    override suspend fun unitOffers(token: String, unitId: String) = offers(token, own(unitId))
    override suspend fun dependentUnitOffers(token: String, dependentId: String, unitId: String) = offers(token, delegated(dependentId, unitId))

    private suspend fun offers(token: String, prefix: String): List<UnitOffer> =
        offerList(request("$prefix/lightning-offers", token = token, context = ApiContext.UNIT).getJSONArray("offers"))

    private fun offerList(offers: JSONArray): List<UnitOffer> = List(offers.length()) { index -> offers.getJSONObject(index).let {
        UnitOffer(it.getString("id"), it.getString("specialty"), it.getString("professional"),
            it.getString("startsAt"), it.getString("endsAt"), it.getString("expiresAt"), it.getString("status"))
    } }

    override suspend fun respondUnitOffer(token: String, unitId: String, id: String, accept: Boolean) = respond(token, own(unitId), id, accept)
    override suspend fun dependentRespondOffer(token: String, dependentId: String, unitId: String, id: String, accept: Boolean) =
        respond(token, delegated(dependentId, unitId), id, accept)

    private suspend fun respond(token: String, prefix: String, id: String, accept: Boolean): OfferResponse {
        require(GUID.matches(id)) { "Oferta inválida." }
        val response = request("$prefix/lightning-offers/$id/respond", "POST", token, JSONObject().put("accept", accept), ApiContext.OFFER_RESPOND)
        return OfferResponse(response.getBoolean("accepted"), response.stringOrNull("appointmentId"))
    }

    override suspend fun attendance(token: String, unitId: String) = attendanceOf(token, own(unitId))
    override suspend fun dependentAttendance(token: String, dependentId: String, unitId: String) = attendanceOf(token, delegated(dependentId, unitId))

    private suspend fun attendanceOf(token: String, prefix: String): UnitAttendance {
        val body = request("$prefix/attendance", token = token, context = ApiContext.UNIT)
        return UnitAttendance(body.getInt("completed"), body.getInt("missed"), body.getInt("cancelledByPatient"),
            body.getDouble("cancellationWeight"), if (body.isNull("score")) null else body.getDouble("score"), body.getInt("windowMonths"))
    }

    override suspend fun cancelQueueEntry(token: String, unitId: String, entryId: Long) = cancelEntry(token, own(unitId), entryId)
    override suspend fun dependentCancelQueueEntry(token: String, dependentId: String, unitId: String, entryId: Long) =
        cancelEntry(token, delegated(dependentId, unitId), entryId)

    private suspend fun cancelEntry(token: String, prefix: String, entryId: Long) {
        require(entryId > 0) { "Solicitação inválida." }
        request("$prefix/waiting-list/$entryId/cancel", "POST", token, JSONObject().put("confirmed", true), ApiContext.QUEUE_CANCEL)
    }

    override suspend fun unlinkUnit(token: String, unitId: String): UnlinkResult {
        val body = request("${own(unitId)}/unlink", "POST", token, JSONObject().put("confirmed", true), ApiContext.UNLINK)
        return UnlinkResult(body.getInt("cancelledAppointments"), body.getInt("cancelledEntries"))
    }

    override suspend fun unlinkPreview(token: String, unitId: String): UnlinkImpact {
        val body = request("${own(unitId)}/unlink-preview", token = token, context = ApiContext.UNLINK)
        val impact = UnlinkImpact(body.getInt("futureAppointments"), body.getInt("activeWaitingEntries"))
        require(impact.appointments >= 0 && impact.queueEntries >= 0) { "Prévia de remoção inválida." }
        return impact
    }

    // ---- Comunicados ---------------------------------------------------------------------------------------

    override suspend fun announcements(token: String, after: Long?): AnnouncementPage {
        val query = if (after == null) "" else "?after=$after"
        val body = request("/api/patient/announcements$query", token = token, context = ApiContext.ANNOUNCEMENTS)
        val items = body.getJSONArray("announcements")
        return AnnouncementPage(List(items.length()) { index -> items.getJSONObject(index).let {
            Announcement(it.getString("id"), it.getLong("sequence"), it.getString("title"), it.getString("body"), it.getString("publishedAt"),
                it.stringOrNull("expiresAt"), it.getBoolean("isRead"), it.stringOrNull("readAt"))
        } }, body.getLong("cursor"), body.getBoolean("hasMore"))
    }

    override suspend fun markAnnouncementRead(token: String, id: String) {
        require(GUID.matches(id)) { "Comunicado inválido." }
        request("/api/patient/announcements/$id/read", "POST", token, context = ApiContext.ANNOUNCEMENTS)
    }

    // ---- Jornada: exames e vacinas -------------------------------------------------------------------------

    override suspend fun healthRecords(token: String): List<UnitRecords> {
        val units = request("/api/patient/health-records", token = token, context = ApiContext.RECORDS).getJSONArray("units")
        return List(units.length()) { index -> units.getJSONObject(index).let { records(it, it.getString("unitId"), it.getString("unitName")) } }
    }

    override suspend fun dependentRecords(token: String, dependentId: String, unitId: String): UnitRecords =
        records(request("${delegated(dependentId, unitId)}/health-records", token = token, context = ApiContext.RECORDS), unitId, "")

    // O motivo de anulação nunca é lido: o servidor não o envia ao paciente e o app não o guarda.
    private fun records(body: JSONObject, unitId: String, unitName: String): UnitRecords {
        val exams = body.getJSONArray("exams")
        val vaccines = body.getJSONArray("vaccines")
        return UnitRecords(unitId, unitName,
            List(exams.length()) { index -> exams.getJSONObject(index).let {
                ExamRecord(it.getString("id"), it.getString("name"), it.getString("status"), it.getString("requestedAt"), it.stringOrNull("performedOn"), it.stringOrNull("note"))
            } },
            List(vaccines.length()) { index -> vaccines.getJSONObject(index).let {
                VaccineRecord(it.getString("id"), it.getString("name"), it.getString("dose"), it.getString("appliedOn"), it.getString("status"), it.stringOrNull("note"))
            } })
    }

    // ---- Família -------------------------------------------------------------------------------------------

    override suspend fun dependents(token: String): List<FamilyLink> {
        val items = request("/api/patient/dependents", token = token, context = ApiContext.FAMILY).getJSONArray("dependents")
        return List(items.length()) { index -> link(items.getJSONObject(index)) }
    }

    override suspend fun requestDependent(token: String, cpf: String, unitId: String): FamilyLink {
        require(GUID.matches(unitId)) { "UBS inválida." }
        return link(request("/api/patient/dependents", "POST", token, JSONObject().put("cpf", cpf).put("unitId", unitId), ApiContext.FAMILY_REQUEST))
    }

    override suspend fun cancelDependentRequest(token: String, id: String): FamilyLink {
        require(GUID.matches(id)) { "Pedido inválido." }
        return link(request("/api/patient/dependents/$id/cancel", "POST", token, JSONObject().put("confirmed", true), ApiContext.FAMILY))
    }

    override suspend fun endDependentLink(token: String, id: String): FamilyLink {
        require(GUID.matches(id)) { "Vínculo inválido." }
        return link(request("/api/patient/dependents/$id/end", "POST", token, JSONObject().put("confirmed", true), ApiContext.FAMILY))
    }

    override suspend fun dependentsOpenOffers(token: String): List<DependentOffers> {
        val items = request("/api/patient/dependents/lightning-offers", token = token, context = ApiContext.FAMILY).getJSONArray("dependents")
        return List(items.length()) { index -> items.getJSONObject(index).let {
            DependentOffers(it.getString("dependentId"), it.getString("unitId"), it.getString("unitName"), it.getString("dependentName"), offerList(it.getJSONArray("offers")))
        } }
    }

    private fun link(body: JSONObject) = FamilyLink(body.getString("id"), body.getString("status"), body.getString("unitId"), body.getString("unitName"),
        body.getString("dependentCpfMasked"), body.stringOrNull("dependentName"), body.getString("requestedAt"), body.stringOrNull("decidedAt"),
        body.stringOrNull("decisionReason"), body.stringOrNull("closedAt"))

    // ---- E-mail da conta guiado pela UBS --------------------------------------------------------------------

    override suspend fun emailSuggestion(token: String): EmailSuggestion? {
        val body = request("/api/patient/email-suggestion", token = token, context = ApiContext.EMAIL_CHANGE)
        if (body.isNull("suggestion")) return null
        return body.getJSONObject("suggestion").let {
            EmailSuggestion(it.getString("id"), it.getString("maskedEmail"), it.getString("unitId"), it.getString("unitName"), it.getString("createdAt"))
        }
    }

    override suspend fun sendEmailSuggestionCode(token: String) {
        request("/api/patient/email-suggestion/send-code", "POST", token, context = ApiContext.EMAIL_CHANGE)
    }

    override suspend fun confirmEmailSuggestion(token: String, code: String): Boolean =
        request("/api/patient/email-suggestion/confirm", "POST", token, JSONObject().put("code", code), ApiContext.EMAIL_CHANGE).getBoolean("codeValid")

    override suspend fun dismissEmailSuggestion(token: String) {
        request("/api/patient/email-suggestion/dismiss", "POST", token, context = ApiContext.EMAIL_CHANGE)
    }
    override suspend fun changePassword(token: String, currentPassword: String, newPassword: String) {
        request("/api/auth/password", "POST", token, JSONObject().put("currentPassword", currentPassword).put("newPassword", newPassword), ApiContext.PASSWORD_CHANGE)
    }

    // ---- Serviços e horários da UBS ------------------------------------------------------------------------

    override suspend fun unitAbout(token: String, unitId: String) = about(token, own(unitId), unitId)
    override suspend fun dependentUnitAbout(token: String, dependentId: String, unitId: String) = about(token, delegated(dependentId, unitId), unitId)

    private suspend fun about(token: String, prefix: String, unitId: String): UnitProfile {
        val body = request("$prefix/about", token = token, context = ApiContext.ABOUT)
        val hours = body.getJSONArray("openingHours")
        val specialties = body.getJSONArray("specialties")
        return UnitProfile(body.optString("unitId", unitId).ifBlank { unitId }, body.getBoolean("informed"),
            List(hours.length()) { index -> hours.getJSONObject(index).let { OpeningInterval(it.getInt("weekday"), it.getString("opens"), it.getString("closes")) } },
            body.stringOrNull("services"),
            List(specialties.length()) { index -> specialties.getJSONObject(index).let { Specialty(it.getString("id"), it.getString("name")) } },
            body.stringOrNull("updatedAt"))
    }

    // ---- Entrar com Google e push --------------------------------------------------------------------------

    override suspend fun loginWithGoogle(idToken: String, deviceName: String?): PatientSession {
        val body = request("/api/auth/google", "POST", body = JSONObject().put("idToken", idToken).apply { deviceName?.let { put("deviceName", it) } },
            context = ApiContext.GOOGLE_LOGIN)
        return PatientSession(body.getString("token"), body.getString("expiresAt"), user(body.getJSONObject("user")))
    }

    override suspend fun googleStatus(token: String): GoogleStatus {
        val body = request("/api/patient/google", token = token, context = ApiContext.GOOGLE)
        return GoogleStatus(body.getBoolean("enabled"), body.getBoolean("linked"), body.stringOrNull("email"))
    }

    override suspend fun linkGoogle(token: String, idToken: String) {
        request("/api/patient/google/link", "POST", token, JSONObject().put("idToken", idToken), ApiContext.GOOGLE)
    }

    override suspend fun unlinkGoogle(token: String) { request("/api/patient/google/unlink", "POST", token, context = ApiContext.GOOGLE) }

    override suspend fun registerPushToken(token: String, pushToken: String) {
        request("/api/patient/devices", "POST", token, JSONObject().put("token", pushToken).put("platform", "android"), ApiContext.PUSH)
    }

    // O token do aparelho vai sempre no corpo, nunca na URL.
    override suspend fun removePushToken(token: String, pushToken: String) {
        request("/api/patient/devices/remove", "POST", token, JSONObject().put("token", pushToken), ApiContext.PUSH)
    }

    override suspend fun pushEnabled(token: String): Boolean =
        request("/api/patient/devices/push", token = token, context = ApiContext.PUSH).getBoolean("enabled")

    override suspend fun logout(token: String) { request("/api/auth/logout", "POST", token, context = ApiContext.SESSION) }
    override suspend fun presence(token: String, online: Boolean) {
        request(if (online) "/api/patient/presence" else "/api/patient/presence/offline", "POST", token, context = ApiContext.SESSION)
    }

    private fun user(body: JSONObject): PatientUser {
        if (body.getString("scope") != "patient") throw ApiFailure(403, failureMessage(403, "forbidden", ApiContext.LOGIN), "forbidden")
        return PatientUser(body.getString("id"), body.getString("fullName"))
    }

    private suspend fun request(path: String, method: String = "GET", token: String? = null,
        body: JSONObject? = null, context: ApiContext = ApiContext.GENERIC): JSONObject = withContext(Dispatchers.IO) {
        val connection = URL(address + path).openConnection() as HttpURLConnection
        try {
            connection.requestMethod = method
            connection.connectTimeout = 10000
            connection.readTimeout = 15000
            connection.instanceFollowRedirects = false
            connection.setRequestProperty("Accept", "application/json")
            token?.let { connection.setRequestProperty("Authorization", "Bearer $it") }
            body?.let {
                connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { output -> output.write(it.toString().toByteArray(Charsets.UTF_8)) }
            }
            val status = connection.responseCode
            val stream = if (status in 200..299) connection.inputStream else connection.errorStream
            val text = stream?.bufferedReader(Charsets.UTF_8)?.use { reader ->
                val result = StringBuilder()
                val buffer = CharArray(4096)
                var length = reader.read(buffer)
                while (length != -1) {
                    if (result.length + length > 1048576) throw IOException("Resposta do serviço excede o limite esperado.")
                    result.append(buffer, 0, length)
                    length = reader.read(buffer)
                }
                result.toString()
            }.orEmpty()
            if (status !in 200..299) {
                // Only the fixed `code` vocabulary is read; the server's title/detail text is never shown.
                val code = problemCode(text)
                throw ApiFailure(status, failureMessage(status, code, context), code)
            }
            if (text.isBlank()) JSONObject() else JSONObject(text)
        } finally { connection.disconnect() }
    }

    private fun problemCode(text: String): String? =
        if (text.isBlank()) null else try { knownProblemCode(JSONObject(text).optString("code", "")) } catch (_: JSONException) { null }
}

/** String ou `null` (campo ausente, nulo ou JSON null). */
private fun JSONObject.stringOrNull(key: String): String? = if (isNull(key)) null else getString(key)
