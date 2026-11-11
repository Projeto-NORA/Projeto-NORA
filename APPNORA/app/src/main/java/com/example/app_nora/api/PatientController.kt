package com.example.app_nora.api

import android.content.Context
import android.util.Log
import androidx.core.content.edit
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.example.app_nora.BuildConfig
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONException
import java.io.IOException

data class PatientState(val address: String = "", val session: PatientSession? = null,
    val overview: PatientOverview? = null, val units: List<PatientHealthUnit>? = null, val selectedUnitId: String? = null, val appointments: List<PatientAppointment>? = null, val busy: Boolean = false, val message: String? = null,
    val unitQueue: List<UnitQueueEntry>? = null, val unitOffers: List<UnitOffer>? = null)

class PatientController(context: Context, private val apiFactory: (String) -> PatientApi = ::NoraApi) {
    private val preferences = context.getSharedPreferences("nora_connection", Context.MODE_PRIVATE)
    private val presenceMutex = Mutex()
    private var presenceGeneration = 0L
    var state by mutableStateOf(PatientState(address = preferences.all["address"] as? String ?: ""))
        private set

    fun beginPresence(): Long = ++presenceGeneration

    fun closeLocalSession() {
        presenceGeneration++
        state = state.copy(session = null, overview = null, units = null, selectedUnitId = null, appointments = null, unitQueue = null, unitOffers = null)
    }

    suspend fun endPresence(session: PatientSession, address: String, generation: Long) = presenceMutex.withLock {
        // Serialize the final offline request after an in-flight heartbeat for this account.
        if (state.session?.token == session.token && generation != presenceGeneration) return@withLock
        try { apiFactory(address).presence(session.token, false) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: IOException) { Log.w("NORA", "A saída da conexão não foi confirmada; o serviço expirará a presença.") }
        catch (_: JSONException) { Log.w("NORA", "O serviço não confirmou a saída da conexão.") }
    }

    suspend fun publishPresence(session: PatientSession, generation: Long, online: Boolean) = presenceMutex.withLock {
        // A cancelled Activity/old account must never publish presence for its replacement.
        if (generation != presenceGeneration || state.session?.token != session.token) return@withLock
        try { apiFactory(state.address).presence(session.token, online) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && generation == presenceGeneration && state.session?.token == session.token)
                state = state.copy(session = null, overview = null, units = null, selectedUnitId = null, appointments = null, unitQueue = null, unitOffers = null, message = failure.message)
            else Log.w("NORA", "Presença não atualizada: HTTP ${failure.status}.")
        }
        catch (_: IOException) {
            // A missed heartbeat expires on the server; retry when the next heartbeat is due.
            Log.w("NORA", "Não foi possível atualizar a presença; a próxima conexão tentará novamente.")
        }
        catch (_: JSONException) {
            if (generation == presenceGeneration && state.session?.token == session.token)
                state = state.copy(message = "O serviço retornou uma resposta incompatível. Contate a unidade.")
        }
    }

    fun changeAddress(value: String) {
        if (state.busy || state.session != null) return
        state = state.copy(address = value, message = null)
    }

    suspend fun login(identifier: String, password: String) = operation {
        require(identifier.isNotBlank() && password.isNotBlank()) { "Informe seu CPF ou e-mail e sua senha." }
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        val api = apiFactory(address)
        val session = api.login(identifier.trim(), password)
        // Identity and bearer token remain only in memory; backup contains no account data.
        preferences.edit { putString("address", address) }
        state = state.copy(address = address, session = session)
    }

    suspend fun startRegistration(name: String, cpf: String, password: String, unitIds: List<String>): RegistrationStart {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        val result = apiFactory(address).startRegistration(name, cpf, password, unitIds)
        preferences.edit { putString("address", address) }
        return result
    }
    suspend fun sendRegistrationCode(challengeId: String, optionId: String) =
        apiFactory(ApiAddress.normalize(state.address, BuildConfig.DEBUG)).sendRegistrationCode(challengeId, optionId)
    suspend fun confirmRegistration(challengeId: String, code: String) =
        apiFactory(ApiAddress.normalize(state.address, BuildConfig.DEBUG)).confirmRegistration(challengeId, code)

    suspend fun registrationUnits(): List<RegistrationUnit> {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        return apiFactory(address).registrationUnits()
    }

    suspend fun linkUnits(ids: List<String>): UnitLinkResult {
        val session = state.session ?: throw ApiFailure(401, "Entre na sua conta para vincular unidades.")
        val api = apiFactory(state.address)
        val result = api.linkUnits(session.token, ids)
        val units = api.units(session.token)
        if (state.session?.token == session.token) state = state.copy(units = units)
        return result
    }

    suspend fun requestPasswordReset(cpf: String): String {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        preferences.edit { putString("address", address) }
        return apiFactory(address).requestPasswordReset(cpf)
    }

    suspend fun confirmPasswordReset(challengeId: String, code: String, newPassword: String): Boolean {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        return apiFactory(address).confirmPasswordReset(challengeId, code, newPassword)
    }

    suspend fun refresh() = operation {
        val session = state.session ?: return@operation
        val overview = apiFactory(state.address).overview(session.token)
        if (state.session?.token == session.token) state = state.copy(overview = overview)
    }

    suspend fun loadUnits() = operation {
        val session = state.session ?: return@operation
        val units = apiFactory(state.address).units(session.token)
        if (state.session?.token == session.token) {
            val selected = state.selectedUnitId?.takeIf { id -> units.any { it.id == id } }
            state = state.copy(units = units, selectedUnitId = selected, appointments = if (selected == state.selectedUnitId) state.appointments else null,
                unitQueue = if (selected == state.selectedUnitId) state.unitQueue else null, unitOffers = if (selected == state.selectedUnitId) state.unitOffers else null)
        }
    }

    fun selectUnit(id: String) {
        if (!state.busy && state.session != null && state.units?.any { it.id == id } == true)
            state = state.copy(selectedUnitId = id, appointments = if (state.selectedUnitId == id) state.appointments else null,
                unitQueue = if (state.selectedUnitId == id) state.unitQueue else null, unitOffers = if (state.selectedUnitId == id) state.unitOffers else null)
    }

    suspend fun loadAppointments() = operation {
        val session = state.session ?: return@operation
        val unit = state.selectedUnitId ?: return@operation
        val items = apiFactory(state.address).appointments(session.token, unit)
        if (state.session?.token == session.token && state.selectedUnitId == unit) state = state.copy(appointments = items)
    }

    suspend fun cancelAppointment(id: String) = operation {
        val session = state.session ?: return@operation
        val unit = state.selectedUnitId ?: return@operation
        if (state.appointments?.none { it.id == id && it.status in listOf("scheduled", "called") } != false) return@operation
        apiFactory(state.address).cancelAppointment(session.token, unit, id)
        if (state.session?.token == session.token && state.selectedUnitId == unit)
            state = state.copy(appointments = state.appointments?.map { if (it.id == id) it.copy(status = "cancelled") else it }, message = "Consulta cancelada.")
    }

    suspend fun refreshUnit() = operation {
        val session = state.session ?: return@operation
        val unit = state.selectedUnitId ?: return@operation
        val api = apiFactory(state.address); val offers = api.unitOffers(session.token, unit); val queue = api.unitQueue(session.token, unit)
        if (state.session?.token == session.token && state.selectedUnitId == unit) state = state.copy(unitQueue = queue, unitOffers = offers)
    }
    suspend fun respondUnitOffer(id: String, accept: Boolean) = operation {
        val session = state.session ?: return@operation
        val unit = state.selectedUnitId ?: return@operation
        val offer = state.unitOffers?.singleOrNull { it.id == id && it.status == "pending" } ?: return@operation
        apiFactory(state.address).respondUnitOffer(session.token, unit, id, accept)
        if (state.session?.token == session.token && state.selectedUnitId == unit)
            state = state.copy(unitOffers = state.unitOffers?.map { if (it.id == id) it.copy(status = if (accept) "accepted" else "declined") else it },
                unitQueue = state.unitQueue?.map { if (it.specialty.equals(offer.specialty, true) && it.status == "offered") it.copy(status = "completed") else it },
                appointments = if (accept) null else state.appointments,
                message = if (accept) "Oferta aceita. Sua consulta foi agendada." else "Oferta recusada.")
    }

    fun selectNearestUnit(latitude: Double, longitude: Double): PatientHealthUnit? {
        if (state.busy || state.session == null || !latitude.isFinite() || !longitude.isFinite() ||
            latitude !in -90.0..90.0 || longitude !in -180.0..180.0) return null
        val nearest = state.units?.minByOrNull { unit ->
            val lat1 = Math.toRadians(latitude)
            val lat2 = Math.toRadians(unit.latitude)
            val deltaLat = lat2 - lat1
            val deltaLon = Math.toRadians(unit.longitude - longitude)
            val a = kotlin.math.sin(deltaLat / 2).let { it * it } +
                kotlin.math.cos(lat1) * kotlin.math.cos(lat2) * kotlin.math.sin(deltaLon / 2).let { it * it }
            kotlin.math.asin(kotlin.math.sqrt(a.coerceIn(0.0, 1.0)))
        } ?: return null
        state = state.copy(selectedUnitId = nearest.id, appointments = if (state.selectedUnitId == nearest.id) state.appointments else null,
            unitQueue = if (state.selectedUnitId == nearest.id) state.unitQueue else null, unitOffers = if (state.selectedUnitId == nearest.id) state.unitOffers else null)
        return nearest
    }

    suspend fun respond(id: String, accept: Boolean) = operation {
        val session = state.session ?: return@operation
        val api = apiFactory(state.address)
        api.respond(session.token, id, accept)
        if (state.session?.token != session.token) return@operation
        val decision = if (accept) "accepted" else "declined"
        state = state.copy(overview = state.overview?.let { overview ->
            overview.copy(offers = overview.offers.map { if (it.id == id) it.copy(status = decision) else it })
        })
        try {
            val overview = api.overview(session.token)
            if (state.session?.token == session.token) state = state.copy(overview = overview)
        }
        catch (failure: ApiFailure) {
            if (failure.status == 401) {
                state = state.copy(session = null, overview = null, units = null, selectedUnitId = null, appointments = null, unitQueue = null, unitOffers = null,
                    message = "Resposta confirmada. Sua sessão terminou; entre novamente para atualizar.")
            } else warnConfirmedDecision()
        }
        catch (_: IOException) { warnConfirmedDecision() }
        catch (_: JSONException) { warnConfirmedDecision() }
    }

    private fun warnConfirmedDecision() {
        state = state.copy(message = "Resposta confirmada. Não foi possível atualizar o acompanhamento. Use Atualizar antes de outra operação.")
    }

    suspend fun logout() = operation {
        val session = state.session ?: return@operation
        try { apiFactory(state.address).logout(session.token) }
        catch (failure: ApiFailure) { if (failure.status != 401) warnUnconfirmedLogout() }
        catch (_: IOException) { warnUnconfirmedLogout() }
        catch (_: JSONException) { warnUnconfirmedLogout() }
        finally { state = state.copy(session = null, overview = null, units = null, selectedUnitId = null, appointments = null, unitQueue = null, unitOffers = null) }
    }

    private fun warnUnconfirmedLogout() {
        state = state.copy(message = "Você saiu neste dispositivo. Não foi possível confirmar a saída no serviço.")
    }

    private suspend fun operation(action: suspend () -> Unit) {
        if (state.busy) return
        state = state.copy(busy = true, message = null)
        try { action() }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401) state = state.copy(session = null, overview = null, units = null, selectedUnitId = null, appointments = null, unitQueue = null, unitOffers = null)
            state = state.copy(message = failure.message)
        }
        catch (invalid: IllegalArgumentException) { state = state.copy(message = invalid.message) }
        catch (_: IOException) { state = state.copy(message = "Não foi possível conectar ao serviço. Confira o endereço e sua conexão e tente novamente.") }
        catch (_: JSONException) { state = state.copy(message = "O serviço retornou uma resposta incompatível. Contate a unidade.") }
        finally { state = state.copy(busy = false) }
    }
}
