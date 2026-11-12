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
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Locale
import java.util.TimeZone

data class PatientUser(val id: String, val fullName: String)
data class PatientSession(val token: String, val expiresAt: String, val user: PatientUser)
data class RegistrationUnit(val id: String, val name: String, val region: String)
data class RegistrationEmailOption(val id: String, val maskedEmail: String)
data class RegistrationStart(val challengeId: String?, val emails: List<RegistrationEmailOption>)
data class RegistrationResult(val codeValid: Boolean, val linkedUnits: List<String>, val unmatchedUnits: List<String>)
data class UnitLinkResult(val linkedUnits: List<String>, val unmatchedUnits: List<String>)
data class PatientHealthUnit(val id: String, val name: String, val region: String, val address: String, val latitude: Double, val longitude: Double)
data class PatientAppointment(val id: String, val specialty: String, val professional: String, val startsAt: String, val endsAt: String, val status: String) {
    /** Instante de início já interpretado uma vez; `null` quando o servidor enviou uma data ilegível. */
    val startsAtMillis: Long? = parseInstantMillis(startsAt)
}
data class UnitQueueEntry(val id: Long, val specialty: String, val enteredAt: String, val status: String) {
    val enteredAtMillis: Long? = parseInstantMillis(enteredAt)
}
data class UnitOffer(val id: String, val specialty: String, val professional: String, val startsAt: String, val endsAt: String, val expiresAt: String, val status: String) {
    val expiresAtMillis: Long? = parseInstantMillis(expiresAt)
}
/** Resultado do servidor para uma resposta: `accepted` só é verdadeiro quando a consulta foi criada. */
data class OfferResponse(val accepted: Boolean, val appointmentId: String?)
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

    override suspend fun appointments(token: String, unitId: String): List<PatientAppointment> {
        require(GUID.matches(unitId)) { "UBS inválida." }
        val format = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply { timeZone = TimeZone.getTimeZone("UTC") }
        val from = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, -180) }.time
        val to = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, 180) }.time
        val path = "/api/patient/units/$unitId/appointments?from=${URLEncoder.encode(format.format(from), "UTF-8")}&to=${URLEncoder.encode(format.format(to), "UTF-8")}"
        val items = request(path, token = token, context = ApiContext.UNIT).getJSONArray("appointments")
        return List(items.length()) { index -> items.getJSONObject(index).let {
            PatientAppointment(it.getString("id"), it.getString("specialty"), it.getString("professional"),
                it.getString("startsAt"), it.getString("endsAt"), it.getString("status"))
        } }
    }

    override suspend fun cancelAppointment(token: String, unitId: String, id: String) {
        require(GUID.matches(unitId) && GUID.matches(id)) { "Consulta inválida." }
        request("/api/patient/units/$unitId/appointments/$id/cancel", "POST", token, JSONObject().put("confirmed", true), ApiContext.APPOINTMENT_CANCEL)
    }
    override suspend fun unitQueue(token: String, unitId: String): List<UnitQueueEntry> {
        require(GUID.matches(unitId)) { "UBS inválida." }
        val entries = request("/api/patient/units/$unitId/waiting-list", token = token, context = ApiContext.UNIT).getJSONArray("entries")
        return List(entries.length()) { index -> entries.getJSONObject(index).let { UnitQueueEntry(it.getLong("id"), it.getString("specialty"), it.getString("enteredAt"), it.getString("status")) } }
    }
    override suspend fun unitOffers(token: String, unitId: String): List<UnitOffer> {
        require(GUID.matches(unitId)) { "UBS inválida." }
        val offers = request("/api/patient/units/$unitId/lightning-offers", token = token, context = ApiContext.UNIT).getJSONArray("offers")
        return List(offers.length()) { index -> offers.getJSONObject(index).let {
            UnitOffer(it.getString("id"), it.getString("specialty"), it.getString("professional"),
                it.getString("startsAt"), it.getString("endsAt"), it.getString("expiresAt"), it.getString("status"))
        } }
    }
    override suspend fun respondUnitOffer(token: String, unitId: String, id: String, accept: Boolean): OfferResponse {
        require(GUID.matches(unitId) && GUID.matches(id)) { "Oferta inválida." }
        val response = request("/api/patient/units/$unitId/lightning-offers/$id/respond", "POST", token, JSONObject().put("accept", accept), ApiContext.OFFER_RESPOND)
        return OfferResponse(response.getBoolean("accepted"), if (response.isNull("appointmentId")) null else response.getString("appointmentId"))
    }

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
