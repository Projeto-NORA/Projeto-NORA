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
    val overview: PatientOverview? = null, val busy: Boolean = false, val message: String? = null)

class PatientController(context: Context, private val apiFactory: (String) -> PatientApi = ::NoraApi) {
    private val preferences = context.getSharedPreferences("nora_connection", Context.MODE_PRIVATE)
    private val presenceMutex = Mutex()
    private var presenceGeneration = 0L
    var state by mutableStateOf(PatientState(address = preferences.all["address"] as? String ?: ""))
        private set

    fun beginPresence(): Long = ++presenceGeneration

    fun closeLocalSession() {
        presenceGeneration++
        state = state.copy(session = null, overview = null)
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
                state = state.copy(session = null, overview = null, message = failure.message)
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
        val overview = api.overview(session.token)
        if (state.session?.token == session.token) state = state.copy(overview = overview)
    }

    suspend fun refresh() = operation {
        val session = state.session ?: return@operation
        val overview = apiFactory(state.address).overview(session.token)
        if (state.session?.token == session.token) state = state.copy(overview = overview)
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
                state = state.copy(session = null, overview = null,
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
        finally { state = state.copy(session = null, overview = null) }
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
            if (failure.status == 401) state = state.copy(session = null, overview = null)
            state = state.copy(message = failure.message)
        }
        catch (invalid: IllegalArgumentException) { state = state.copy(message = invalid.message) }
        catch (_: IOException) { state = state.copy(message = "Não foi possível conectar ao serviço. Confira o endereço e sua conexão e tente novamente.") }
        catch (_: JSONException) { state = state.copy(message = "O serviço retornou uma resposta incompatível. Contate a unidade.") }
        finally { state = state.copy(busy = false) }
    }
}
