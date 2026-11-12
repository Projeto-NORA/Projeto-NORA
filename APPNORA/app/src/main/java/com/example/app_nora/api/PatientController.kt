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

/**
 * Estado do paciente. Fila, ofertas e consultas ficam por UBS em [unitData]; os acessos
 * `appointments`, `unitQueue` e `unitOffers` devolvem os da UBS selecionada.
 */
data class PatientState(val address: String = "", val session: PatientSession? = null,
    val units: List<PatientHealthUnit>? = null, val selectedUnitId: String? = null, val busy: Boolean = false,
    val message: String? = null, val loadingUnits: Boolean = false,
    val unitData: Map<String, UnitSnapshot> = emptyMap()) {
    private val selectedSnapshot: UnitSnapshot? get() = selectedUnitId?.let { unitData[it] }
    val appointments: List<PatientAppointment>? get() = selectedSnapshot?.appointments
    val unitQueue: List<UnitQueueEntry>? get() = selectedSnapshot?.queue
    val unitOffers: List<UnitOffer>? get() = selectedSnapshot?.offers
}

/** Guarda só o endereço do serviço; token e identidade nunca vão para disco. */
interface AddressStore {
    fun load(): String
    fun save(address: String)
}

private class PreferencesAddressStore(context: Context) : AddressStore {
    private val preferences = context.getSharedPreferences("nora_connection", Context.MODE_PRIVATE)
    override fun load(): String = preferences.all["address"] as? String ?: ""
    override fun save(address: String) { preferences.edit { putString("address", address) } }
}

class PatientController(private val store: AddressStore, private val apiFactory: (String) -> PatientApi = ::NoraApi) {
    constructor(context: Context, apiFactory: (String) -> PatientApi = ::NoraApi) : this(PreferencesAddressStore(context), apiFactory)

    private val presenceMutex = Mutex()
    private var presenceGeneration = 0L
    // Cada ação do paciente sobre uma UBS invalida leituras em segundo plano que começaram antes dela.
    private val unitVersions = HashMap<String, Long>()
    var state by mutableStateOf(PatientState(address = store.load()))
        private set

    fun beginPresence(): Long = ++presenceGeneration

    fun closeLocalSession() {
        presenceGeneration++
        state = signedOut(state, null)
    }

    private fun signedOut(from: PatientState, message: String?) = from.copy(session = null, units = null, selectedUnitId = null,
        unitData = emptyMap(), loadingUnits = false, message = message)

    private fun isCurrent(session: PatientSession) = state.session?.token == session.token
    private fun isCurrent(session: PatientSession, generation: Long) = generation == presenceGeneration && isCurrent(session)
    private fun touch(unitId: String) { unitVersions[unitId] = (unitVersions[unitId] ?: 0L) + 1 }

    suspend fun endPresence(session: PatientSession, address: String, generation: Long) = presenceMutex.withLock {
        // Serialize the final offline request after an in-flight heartbeat for this account.
        if (isCurrent(session) && generation != presenceGeneration) return@withLock
        try { apiFactory(address).presence(session.token, false) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: IOException) { Log.w("NORA", "A saída da conexão não foi confirmada; o serviço expirará a presença.") }
        catch (_: JSONException) { Log.w("NORA", "O serviço não confirmou a saída da conexão.") }
    }

    suspend fun publishPresence(session: PatientSession, generation: Long, online: Boolean) = presenceMutex.withLock {
        // A cancelled Activity/old account must never publish presence for its replacement.
        if (!isCurrent(session, generation)) return@withLock
        try { apiFactory(state.address).presence(session.token, online) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session, generation)) state = signedOut(state, failure.message)
            else Log.w("NORA", "Presença não atualizada: HTTP ${failure.status}.")
        }
        catch (_: IOException) {
            // A missed heartbeat expires on the server; retry when the next heartbeat is due.
            Log.w("NORA", "Não foi possível atualizar a presença; a próxima conexão tentará novamente.")
        }
        catch (_: JSONException) {
            if (isCurrent(session, generation)) state = state.copy(message = INCOMPATIBLE_RESPONSE_MESSAGE)
        }
    }

    fun changeAddress(value: String) {
        if (state.busy || state.session != null) return
        state = state.copy(address = value, message = null)
    }

    suspend fun login(identifier: String, password: String) {
        operation(ApiContext.LOGIN) {
            require(identifier.isNotBlank() && password.isNotBlank()) { "Informe seu CPF ou e-mail e sua senha." }
            val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
            val api = apiFactory(address)
            val session = api.login(identifier.trim(), password)
            // Identity and bearer token remain only in memory; backup contains no account data.
            store.save(address)
            state = state.copy(address = address, session = session)
        }
        loadUnitsInBackground()
    }

    // Chamadas feitas fora de `operation`: uma resposta fora do formato não pode derrubar o app.
    private suspend fun <T> guarded(action: suspend () -> T): T {
        try { return action() }
        catch (_: JSONException) { throw ApiFailure(502, INCOMPATIBLE_RESPONSE_MESSAGE) }
    }

    suspend fun startRegistration(name: String, cpf: String, password: String, unitIds: List<String>): RegistrationStart = guarded {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        val result = apiFactory(address).startRegistration(name, cpf, password, unitIds)
        store.save(address)
        result
    }
    suspend fun sendRegistrationCode(challengeId: String, optionId: String) = guarded {
        apiFactory(ApiAddress.normalize(state.address, BuildConfig.DEBUG)).sendRegistrationCode(challengeId, optionId)
    }
    suspend fun confirmRegistration(challengeId: String, code: String) = guarded {
        apiFactory(ApiAddress.normalize(state.address, BuildConfig.DEBUG)).confirmRegistration(challengeId, code)
    }

    suspend fun registrationUnits(): List<RegistrationUnit> = guarded {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        apiFactory(address).registrationUnits()
    }

    suspend fun linkUnits(ids: List<String>): UnitLinkResult = guarded {
        val session = state.session ?: throw ApiFailure(401, "Entre na sua conta para vincular unidades.", "unauthorized")
        val api = apiFactory(state.address)
        val result = api.linkUnits(session.token, ids)
        val units = api.units(session.token)
        if (isCurrent(session)) applyUnits(units)
        result
    }

    suspend fun requestPasswordReset(cpf: String): String = guarded {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        store.save(address)
        apiFactory(address).requestPasswordReset(cpf)
    }

    suspend fun confirmPasswordReset(challengeId: String, code: String, newPassword: String): Boolean = guarded {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        apiFactory(address).confirmPasswordReset(challengeId, code, newPassword)
    }

    // ---- UBS vinculadas e dados por UBS ---------------------------------------------------------------

    /** Guarda as UBS; com exatamente uma e nenhuma selecionada, seleciona-a e devolve seu id para carregar os dados. */
    private fun applyUnits(units: List<PatientHealthUnit>): String? {
        val current = state.selectedUnitId?.takeIf { id -> units.any { it.id == id } }
        val single = if (current == null && units.size == 1) units.single().id else null
        state = state.copy(units = units, selectedUnitId = current ?: single,
            unitData = state.unitData.filterKeys { id -> units.any { it.id == id } })
        return single
    }

    private fun updateUnit(unitId: String, change: (UnitSnapshot) -> UnitSnapshot) {
        if (state.units?.any { it.id == unitId } != true) return
        state = state.copy(unitData = state.unitData + (unitId to change(state.unitData[unitId] ?: UnitSnapshot())))
    }

    /** Fila, ofertas e consultas de uma UBS; cada parte é aplicada assim que chega. Lança em qualquer falha. */
    private suspend fun loadUnitData(api: PatientApi, session: PatientSession, unitId: String) {
        val offers = api.unitOffers(session.token, unitId)
        if (!isCurrent(session)) return
        updateUnit(unitId) { it.copy(offers = offers) }
        val queue = api.unitQueue(session.token, unitId)
        if (!isCurrent(session)) return
        updateUnit(unitId) { it.copy(queue = queue) }
        val appointments = api.appointments(session.token, unitId)
        if (isCurrent(session)) updateUnit(unitId) { it.copy(appointments = appointments) }
    }

    /** Leitura em segundo plano das UBS vinculadas: sem `busy`, silenciosa, e 401 encerra a sessão. */
    private suspend fun loadUnitsInBackground() {
        val session = state.session ?: return
        if (state.loadingUnits || state.units != null) return
        state = state.copy(loadingUnits = true)
        try {
            val api = apiFactory(state.address)
            val units = api.units(session.token)
            if (!isCurrent(session)) return
            val preload = applyUnits(units)
            if (preload != null) loadUnitData(api, session, preload)
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
            else Log.w("NORA", "UBS não carregadas em segundo plano: HTTP ${failure.status}.")
        }
        catch (_: IOException) { Log.w("NORA", "UBS não carregadas em segundo plano; a próxima consulta tentará novamente.") }
        catch (_: JSONException) { Log.w("NORA", "O serviço retornou uma lista de UBS incompatível.") }
        catch (_: IllegalArgumentException) { Log.w("NORA", "O serviço retornou uma UBS com identificador inválido.") }
        finally { if (isCurrent(session)) state = state.copy(loadingUnits = false) }
    }

    suspend fun loadUnits() {
        if (state.loadingUnits) return
        operation(ApiContext.UNIT_LIST) {
            val session = state.session ?: return@operation
            val api = apiFactory(state.address)
            val units = api.units(session.token)
            if (!isCurrent(session)) return@operation
            val preload = applyUnits(units)
            if (preload != null) loadUnitData(api, session, preload)
        }
    }

    fun selectUnit(id: String) {
        if (!state.busy && state.session != null && state.units?.any { it.id == id } == true)
            state = state.copy(selectedUnitId = id)
    }

    suspend fun loadAppointments() = operation(ApiContext.UNIT) {
        val session = state.session ?: return@operation
        val unit = state.selectedUnitId ?: return@operation
        val items = apiFactory(state.address).appointments(session.token, unit)
        if (isCurrent(session)) updateUnit(unit) { it.copy(appointments = items) }
    }

    suspend fun cancelAppointment(id: String) = operation(ApiContext.APPOINTMENT_CANCEL) {
        val session = state.session ?: return@operation
        val unit = state.selectedUnitId ?: return@operation
        if (state.appointments?.none { it.id == id && it.status in listOf("scheduled", "called") } != false) return@operation
        val api = apiFactory(state.address)
        try { api.cancelAppointment(session.token, unit, id) }
        catch (failure: ApiFailure) {
            // 404/409: a consulta mudou na unidade; mostrar o estado real em vez do antigo.
            if (failure.status == 404 || failure.status == 409) reloadAppointmentsQuietly(api, session, unit)
            throw failure
        }
        if (isCurrent(session)) {
            updateUnit(unit) { snapshot -> snapshot.copy(appointments = snapshot.appointments?.map { if (it.id == id) it.copy(status = "cancelled") else it }) }
            state = state.copy(message = "Consulta cancelada.")
        }
    }

    private suspend fun reloadAppointmentsQuietly(api: PatientApi, session: PatientSession, unit: String) {
        try {
            val items = api.appointments(session.token, unit)
            if (isCurrent(session)) updateUnit(unit) { it.copy(appointments = items) }
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: IOException) { Log.w("NORA", "Consultas não atualizadas após a falha.") }
        catch (_: JSONException) { Log.w("NORA", "Consultas incompatíveis após a falha.") }
    }

    suspend fun refreshUnit() = operation(ApiContext.UNIT) {
        val session = state.session ?: return@operation
        val unit = state.selectedUnitId ?: return@operation
        val api = apiFactory(state.address)
        val offers = api.unitOffers(session.token, unit)
        val queue = api.unitQueue(session.token, unit)
        if (isCurrent(session)) { touch(unit); updateUnit(unit) { it.copy(queue = queue, offers = offers) } }
    }

    suspend fun respondUnitOffer(id: String, accept: Boolean) = operation(ApiContext.OFFER_RESPOND) {
        val session = state.session ?: return@operation
        val unit = state.selectedUnitId ?: return@operation
        state.unitOffers?.singleOrNull { it.id == id && it.status == "pending" } ?: return@operation
        val api = apiFactory(state.address)
        touch(unit)
        val result = try { api.respondUnitOffer(session.token, unit, id, accept) }
        catch (failure: ApiFailure) {
            // Qualquer resposta do servidor (inclusive 409) deixa a oferta diferente do que a tela mostra.
            if (failure.status != 401) reloadAfterResponse(api, session, unit, false)
            throw failure
        }
        catch (io: IOException) { reloadAfterResponse(api, session, unit, false); throw io }
        if (!isCurrent(session)) return@operation
        val reloaded = reloadAfterResponse(api, session, unit, result.accepted)
        if (!isCurrent(session)) return@operation
        if (!reloaded) updateUnit(unit) { snapshot ->
            snapshot.copy(offers = snapshot.offers?.map { if (it.id == id) it.copy(status = if (accept) "accepted" else "declined") else it })
        }
        val outcome = when {
            accept && result.accepted -> "Oferta aceita. Sua consulta foi agendada."
            accept -> "A unidade não confirmou o agendamento desta oportunidade. Confira suas consultas."
            else -> "Oferta recusada."
        }
        state = state.copy(message = if (reloaded) outcome else "$outcome Não foi possível atualizar a tela; use Atualizar.")
    }

    /** Recarrega ofertas e fila da UBS (e as consultas quando houve aceite). `false` se não foi possível. */
    private suspend fun reloadAfterResponse(api: PatientApi, session: PatientSession, unit: String, withAppointments: Boolean): Boolean {
        try {
            val offers = api.unitOffers(session.token, unit)
            val queue = api.unitQueue(session.token, unit)
            val appointments = if (withAppointments) api.appointments(session.token, unit) else null
            if (isCurrent(session)) {
                touch(unit)
                updateUnit(unit) { it.copy(offers = offers, queue = queue, appointments = appointments ?: it.appointments) }
            }
            return true
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
        }
        catch (_: IOException) { Log.w("NORA", "Ofertas não recarregadas após a resposta.") }
        catch (_: JSONException) { Log.w("NORA", "Ofertas incompatíveis após a resposta.") }
        return false
    }

    /**
     * Consulta as ofertas de todas as UBS vinculadas, em sequência, para o ciclo de 30 s do serviço de conexão.
     * Não usa `busy`; falhas de rede são silenciosas e 401 encerra a sessão. Usa o mesmo mutex e a mesma geração
     * da presença, de modo que uma resposta atrasada depois de logout/401 é descartada.
     */
    suspend fun pollOffers(session: PatientSession, generation: Long) {
        if (!isCurrent(session, generation)) return
        if (state.units == null) loadUnitsInBackground()
        val units = state.units ?: return
        for (unit in units) {
            val finished = presenceMutex.withLock {
                if (!isCurrent(session, generation)) return@withLock false
                val version = unitVersions[unit.id]
                try {
                    val offers = apiFactory(state.address).unitOffers(session.token, unit.id)
                    if (isCurrent(session, generation) && unitVersions[unit.id] == version) updateUnit(unit.id) { it.copy(offers = offers) }
                }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (failure: ApiFailure) {
                    if (failure.status == 401 && isCurrent(session, generation)) state = signedOut(state, failure.message)
                    else Log.w("NORA", "Ofertas não atualizadas: HTTP ${failure.status}.")
                }
                catch (_: IOException) { Log.w("NORA", "Ofertas não atualizadas; a próxima consulta tentará novamente.") }
                catch (_: JSONException) { Log.w("NORA", "O serviço retornou ofertas incompatíveis.") }
                catch (_: IllegalArgumentException) { Log.w("NORA", "O serviço retornou uma oferta com identificador inválido.") }
                isCurrent(session, generation)
            }
            if (!finished) return
        }
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
        state = state.copy(selectedUnitId = nearest.id)
        return nearest
    }

    suspend fun logout() = operation {
        val session = state.session ?: return@operation
        try { apiFactory(state.address).logout(session.token) }
        catch (failure: ApiFailure) { if (failure.status != 401) warnUnconfirmedLogout() }
        catch (_: IOException) { warnUnconfirmedLogout() }
        catch (_: JSONException) { warnUnconfirmedLogout() }
        finally { state = signedOut(state, state.message) }
    }

    private fun warnUnconfirmedLogout() {
        state = state.copy(message = "Você saiu neste dispositivo. Não foi possível confirmar a saída no serviço.")
    }

    /** Ação do paciente: uma por vez, com `busy` visível. Leituras em segundo plano não passam por aqui. */
    private suspend fun operation(context: ApiContext = ApiContext.GENERIC, action: suspend () -> Unit) {
        if (state.busy) return
        state = state.copy(busy = true, message = null)
        try { action() }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            val text = if (context == ApiContext.GENERIC) failure.message else failureMessage(failure.status, failure.code, context)
            state = if (failure.status == 401) signedOut(state, text) else state.copy(message = text)
        }
        catch (invalid: IllegalArgumentException) { state = state.copy(message = invalid.message) }
        catch (_: IOException) { state = state.copy(message = "Não foi possível conectar ao serviço. Confira o endereço e sua conexão e tente novamente.") }
        catch (_: JSONException) { state = state.copy(message = INCOMPATIBLE_RESPONSE_MESSAGE) }
        finally { state = state.copy(busy = false) }
    }
}
