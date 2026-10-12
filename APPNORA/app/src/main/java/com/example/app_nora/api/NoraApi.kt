package com.example.app_nora.api

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

data class PatientUser(val id: String, val fullName: String)
data class PatientSession(val token: String, val expiresAt: String, val user: PatientUser)
data class QueueEntry(val id: Long, val enteredAt: String, val status: String)
data class PatientOffer(val id: String, val label: String, val expiresAt: String, val status: String)
data class PatientOverview(val user: PatientUser, val queue: List<QueueEntry>, val offers: List<PatientOffer>)
class ApiFailure(val status: Int, message: String) : IOException(message)

interface PatientApi {
    suspend fun login(identifier: String, password: String): PatientSession
    suspend fun overview(token: String): PatientOverview
    suspend fun respond(token: String, id: String, accept: Boolean)
    suspend fun logout(token: String)
    suspend fun presence(token: String, online: Boolean)
}

class NoraApi(private val address: String) : PatientApi {
    override suspend fun login(identifier: String, password: String): PatientSession {
        val body = request("/api/auth/login", "POST", body = JSONObject()
            .put("scope", "patient").put("identifier", identifier).put("password", password))
        return PatientSession(body.getString("token"), body.getString("expiresAt"), user(body.getJSONObject("user")))
    }

    override suspend fun overview(token: String): PatientOverview {
        val body = request("/api/patient/overview", token = token)
        val queue = body.getJSONArray("queue")
        val offers = body.getJSONArray("offers")
        return PatientOverview(user(body.getJSONObject("user")),
            List(queue.length()) { index -> queue.getJSONObject(index).let {
                QueueEntry(it.getLong("id"), it.getString("enteredAt"), it.getString("status"))
            } },
            List(offers.length()) { index -> offers.getJSONObject(index).let {
                PatientOffer(it.getString("id"), it.getString("label"), it.getString("expiresAt"), it.getString("status"))
            } })
    }

    override suspend fun respond(token: String, id: String, accept: Boolean) {
        require(Regex("[a-fA-F0-9-]{36}").matches(id)) { "Oferta inválida." }
        request("/api/offers/$id/respond", "POST", token, JSONObject().put("accept", accept))
    }

    override suspend fun logout(token: String) { request("/api/auth/logout", "POST", token) }
    override suspend fun presence(token: String, online: Boolean) {
        request(if (online) "/api/patient/presence" else "/api/patient/presence/offline", "POST", token)
    }

    private fun user(body: JSONObject): PatientUser {
        if (body.getString("scope") != "patient") throw ApiFailure(403, "Esta conta não pertence ao aplicativo do paciente.")
        return PatientUser(body.getString("id"), body.getString("fullName"))
    }

    private suspend fun request(path: String, method: String = "GET", token: String? = null,
        body: JSONObject? = null): JSONObject = withContext(Dispatchers.IO) {
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
            if (status !in 200..299) throw ApiFailure(status, when (status) {
                400 -> "Confira os dados informados."
                401 -> "CPF, e-mail ou senha inválidos, ou sessão expirada. Entre novamente."
                403 -> "Esta conta não tem acesso a esta operação."
                404 -> "O registro não está mais disponível."
                409 -> "A oferta ou fila mudou. Atualize para acompanhar o estado atual."
                503 -> "O serviço está temporariamente indisponível. Tente novamente."
                else -> "Não foi possível concluir a solicitação. Tente novamente."
            })
            if (text.isBlank()) JSONObject() else JSONObject(text)
        } finally { connection.disconnect() }
    }
}
