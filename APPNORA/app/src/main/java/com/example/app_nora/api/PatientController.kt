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
import kotlinx.coroutines.withTimeoutOrNull
import org.json.JSONException
import java.io.IOException

/**
 * Estado do paciente. Fila, ofertas e consultas ficam por UBS em [unitData]; os acessos
 * `appointments`, `unitQueue` e `unitOffers` devolvem os da UBS selecionada. Tudo isto é do próprio paciente: quando ele age
 * por um dependente ([actingFor]), os dados do dependente ficam em [dependentData] e as telas usam a visão de `effective()`.
 */
data class PatientState(val address: String = "", val session: PatientSession? = null,
    val units: List<PatientHealthUnit>? = null, val selectedUnitId: String? = null, val busy: Boolean = false,
    val message: String? = null, val loadingUnits: Boolean = false,
    val unitData: Map<String, UnitSnapshot> = emptyMap(), val attendanceLoading: Boolean = false,
    /** O servidor exigiu nova senha (403 `password-change-required`); a tela leva o paciente à troca de senha. */
    val passwordChangeRequired: Boolean = false,
    /** Catálogo de especialidades da rede (nulo até a primeira leitura da tela de agendamento). */
    val specialties: List<Specialty>? = null, val specialtiesLoading: Boolean = false,
    /** Horários livres da última consulta feita (UBS, especialidade e semana); a tela só os usa se coincidirem com a escolha. */
    val availability: AvailabilityResult? = null, val availabilityLoading: Boolean = false,
    /** Falha das leituras do agendamento (especialidades e horários); as ações usam [message]. */
    val bookingError: String? = null,
    /** Consulta marcada agora pelo app, para a tela de agendamento mostrar o resultado. */
    val lastBooked: BookedAppointment? = null,
    /** Comunicados da rede (aba Avisos). */
    val announcements: AnnouncementsState = AnnouncementsState(),
    /** Pedidos e vínculos de família do responsável (aba Família). */
    val family: FamilyState = FamilyState(),
    /** Id do vínculo aprovado pelo qual o paciente age agora; nulo quando age por si. */
    val actingFor: String? = null,
    /** Fila, ofertas, consultas e Jornada de cada dependente, por id do vínculo (cada vínculo tem uma só UBS). */
    val dependentData: Map<String, UnitSnapshot> = emptyMap(),
    /** Ofertas abertas dos dependentes, lidas pela rota agregada do ciclo de 30 s. */
    val dependentOffers: List<DependentOffers> = emptyList(),
    /** Leitura de exames e vacinas em andamento ou com falha. */
    val records: RecordsState = RecordsState(),
    /** Sugestão de troca do e-mail da conta feita pela UBS. */
    val email: EmailState = EmailState(),
    /** Situação do Google na conta (Configurações). */
    val google: GoogleState = GoogleState(),
    /** Horário de funcionamento, serviços e especialidades de cada UBS vinculada, por id da UBS (tela UBS). */
    val about: Map<String, AboutState> = emptyMap()) {
    private val selectedSnapshot: UnitSnapshot? get() = selectedUnitId?.let { unitData[it] }
    val appointments: List<PatientAppointment>? get() = selectedSnapshot?.appointments
    val unitQueue: List<UnitQueueEntry>? get() = selectedSnapshot?.queue
    val unitOffers: List<UnitOffer>? get() = selectedSnapshot?.offers
}

/** Comunicados já lidos (ordem crescente de `sequence`), o cursor da próxima leitura incremental e o estado da leitura. */
data class AnnouncementsState(val items: List<Announcement> = emptyList(), val cursor: Long? = null, val loaded: Boolean = false,
    val loading: Boolean = false, val error: String? = null,
    /**
     * A leitura completa parou no teto de páginas com mais por vir: os comunicados que a leitura incremental trouxer em seguida já
     * existiam antes e entram na lista sem aviso, até alcançar o fim.
     */
    val catchingUp: Boolean = false)
/** `links` é nulo até a primeira leitura; [error] é a falha da última leitura. */
data class FamilyState(val links: List<FamilyLink>? = null, val loading: Boolean = false, val error: String? = null)
data class RecordsState(val loading: Boolean = false, val error: String? = null)
/** [codeSentFor] é o id da sugestão para a qual um código já foi enviado (o envio é limitado a três por sugestão). */
data class EmailState(val suggestion: EmailSuggestion? = null, val loaded: Boolean = false, val codeSentFor: String? = null)

/** Horários livres de uma especialidade, numa UBS, em uma semana de [bookingWeek]. */
data class AvailabilityResult(val unitId: String, val specialtyId: String, val week: Int, val slots: List<AvailableSlot>)

/** Consulta recém-marcada pelo paciente e a UBS em que foi marcada. */
data class BookedAppointment(val unitId: String, val appointment: PatientAppointment)

/** Guarda só o endereço do serviço; token e identidade nunca vão para disco. */
interface AddressStore {
    fun load(): String
    fun save(address: String)
}

private class PreferencesAddressStore(context: Context) : AddressStore {
    private val preferences = context.getSharedPreferences("nora_connection", Context.MODE_PRIVATE)
    override fun load(): String = BuildConfig.NORA_API_URL.ifBlank { preferences.all["address"] as? String ?: "" }
    override fun save(address: String) { preferences.edit { putString("address", address) } }
}

/** O que o ciclo de 30 s leu de comunicados e o que é novo para notificar. */
private data class AnnouncementRead(val items: List<Announcement>, val cursor: Long, val hasMore: Boolean)

/** Voltas da leitura completa de comunicados (50 por página): um teto contra servidor que nunca termina, não um limite de uso. */
private const val FULL_ANNOUNCEMENT_PAGES = 100
/** Voltas de cada leitura incremental do ciclo de 30 s; o que sobrar vem no ciclo seguinte. */
private const val CYCLE_ANNOUNCEMENT_PAGES = 5
/** O logout espera no máximo isto pela remoção do aparelho no push (P2). */
private const val PUSH_REMOVE_TIMEOUT_MS = 4_000L

class PatientController(private val store: AddressStore, private val apiFactory: (String) -> PatientApi = ::NoraApi) {
    constructor(context: Context, apiFactory: (String) -> PatientApi = ::NoraApi) : this(PreferencesAddressStore(context), apiFactory)

    /** Chamado depois de remover uma UBS, para o serviço de conexão retirar na hora os avisos de oferta dela. */
    var onOffersRemoved: (() -> Unit)? = null
    /** Relógio do aparelho usado para as semanas de horários; testes o substituem. */
    var clock: () -> Long = System::currentTimeMillis
    // Só a última leitura de horários pode gravar o resultado; as anteriores, ainda em voo, são descartadas.
    private var availabilityGeneration = 0L
    // Cada troca da pessoa em tela; a falha de uma ação iniciada antes da troca não vira mensagem da visão nova.
    private var viewSwitches = 0L
    private val presenceMutex = Mutex()
    // Leitura completa (abrir Avisos, mudar vínculos) e incremental (ciclo de 30 s) nunca correm juntas.
    private val announcementMutex = Mutex()
    private val markingRead = HashSet<String>()
    // Quem tem a leitura de participação em andamento: "" é o próprio paciente, senão o id do vínculo.
    private val attendanceInFlight = HashSet<String>()
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
        unitData = emptyMap(), loadingUnits = false, attendanceLoading = false, passwordChangeRequired = false, message = message,
        specialties = null, specialtiesLoading = false, availability = null, availabilityLoading = false, bookingError = null, lastBooked = null,
        announcements = AnnouncementsState(), family = FamilyState(), actingFor = null, dependentData = emptyMap(),
        dependentOffers = emptyList(), records = RecordsState(), email = EmailState(), google = GoogleState(), about = emptyMap())

    /**
     * A API do serviço. Com [acting] (por padrão o dependente atual), as chamadas por UBS vão pelas rotas do responsável; as ações
     * que são só do próprio paciente (lista de UBS, vínculo, senha, ofertas do ciclo de 30 s) passam `null`.
     */
    private fun api(address: String, acting: String? = state.actingFor): PatientApi {
        val base = apiFactory(address)
        return PasswordGate(if (acting != null) DependentScope(base, acting) else base) {
            if (state.session != null && !state.passwordChangeRequired) state = state.copy(passwordChangeRequired = true)
        }
    }

    private fun isCurrent(session: PatientSession) = state.session?.token == session.token
    private fun isCurrent(session: PatientSession, generation: Long) = generation == presenceGeneration && isCurrent(session)
    /**
     * A sessão continua a mesma e, numa operação sobre um dependente ([acting]), o paciente ainda age por ele (trocar o "agindo por"
     * descarta leituras em voo). Operação sobre o próprio paciente grava só nos dados dele e vale mesmo que a visão tenha mudado.
     */
    private fun isLive(session: PatientSession, acting: String?) = isCurrent(session) && (acting == null || state.actingFor == acting)
    private fun touch(unitId: String) { unitVersions[unitId] = (unitVersions[unitId] ?: 0L) + 1 }

    suspend fun endPresence(session: PatientSession, address: String, generation: Long) = presenceMutex.withLock {
        // Serialize the final offline request after an in-flight heartbeat for this account.
        if (isCurrent(session) && generation != presenceGeneration) return@withLock
        try { api(address, null).presence(session.token, false) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: IOException) { Log.w("NORA", "A saída da conexão não foi confirmada; o serviço expirará a presença.") }
        catch (_: JSONException) { Log.w("NORA", "O serviço não confirmou a saída da conexão.") }
    }

    suspend fun publishPresence(session: PatientSession, generation: Long, online: Boolean) = presenceMutex.withLock {
        // A cancelled Activity/old account must never publish presence for its replacement.
        if (!isCurrent(session, generation)) return@withLock
        try { api(state.address, null).presence(session.token, online) }
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

    /** O paciente negou ACCESS_LOCAL_NETWORK para um endereço da rede local: nada é enviado e a tela diz o que fazer. */
    fun localNetworkDenied() {
        state = state.copy(message = LOCAL_NETWORK_DENIED_MESSAGE)
    }

    fun changeAddress(value: String) {
        if (state.busy || state.session != null) return
        state = state.copy(address = value, message = null)
    }

    suspend fun login(identifier: String, password: String) {
        operation(ApiContext.LOGIN) {
            require(identifier.isNotBlank() && password.isNotBlank()) { "Informe seu CPF ou e-mail e sua senha." }
            val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
            startSession(address, api(address, null).login(identifier.trim(), password))
        }
        loadUnitsInBackground()
    }

    /** Entrar com o ID token do Google (G4): a mesma sessão de paciente do login por senha. 401 é genérico, como no login inválido. */
    suspend fun loginWithGoogle(idToken: String): Boolean {
        var signedIn = false
        operation(ApiContext.GOOGLE_LOGIN) {
            require(idToken.isNotBlank()) { "O Google não devolveu a identificação da conta. Tente de novo." }
            val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
            startSession(address, api(address, null).loginWithGoogle(idToken))
            signedIn = true
        }
        loadUnitsInBackground()
        return signedIn
    }

    private fun startSession(address: String, session: PatientSession) {
        // Identity and bearer token remain only in memory; backup contains no account data.
        store.save(address)
        state = state.copy(address = address, session = session)
    }

    /** Mostra um texto na tela (falha que aconteceu fora do controlador, como o Google que não abriu); nunca por cima de uma ação em andamento. */
    fun showMessage(text: String?) {
        if (!state.busy) state = state.copy(message = text)
    }

    // Chamadas feitas fora de `operation`: uma resposta fora do formato não pode derrubar o app.
    private suspend fun <T> guarded(action: suspend () -> T): T {
        try { return action() }
        catch (_: JSONException) { throw ApiFailure(502, INCOMPATIBLE_RESPONSE_MESSAGE) }
    }

    suspend fun startRegistration(name: String, cpf: String, password: String, unitIds: List<String>): RegistrationStart = guarded {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        val result = api(address, null).startRegistration(name, cpf, password, unitIds)
        store.save(address)
        result
    }
    suspend fun sendRegistrationCode(challengeId: String, optionId: String) = guarded {
        api(ApiAddress.normalize(state.address, BuildConfig.DEBUG), null).sendRegistrationCode(challengeId, optionId)
    }
    suspend fun confirmRegistration(challengeId: String, code: String) = guarded {
        api(ApiAddress.normalize(state.address, BuildConfig.DEBUG), null).confirmRegistration(challengeId, code)
    }

    suspend fun registrationUnits(): List<RegistrationUnit> = guarded {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        api(address, null).registrationUnits()
    }

    suspend fun linkUnits(ids: List<String>): UnitLinkResult = guarded {
        val session = state.session ?: throw ApiFailure(401, "Entre na sua conta para vincular unidades.", "unauthorized")
        val api = api(state.address, null)
        val result = api.linkUnits(session.token, ids)
        val units = api.units(session.token)
        if (isCurrent(session)) {
            applyUnits(units)
            // Os comunicados dependem dos vínculos: nova leitura completa (as UBS novas podem trazer comunicados que o cursor não pega).
            loadAnnouncements()
        }
        result
    }

    suspend fun requestPasswordReset(cpf: String): String = guarded {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        store.save(address)
        api(address, null).requestPasswordReset(cpf)
    }

    suspend fun confirmPasswordReset(challengeId: String, code: String, newPassword: String): Boolean = guarded {
        val address = ApiAddress.normalize(state.address, BuildConfig.DEBUG)
        api(address, null).confirmPasswordReset(challengeId, code, newPassword)
    }

    // ---- UBS vinculadas e dados por UBS ---------------------------------------------------------------

    /** Guarda as UBS; com exatamente uma e nenhuma selecionada, seleciona-a e devolve seu id para carregar os dados. */
    private fun applyUnits(units: List<PatientHealthUnit>): String? {
        val current = state.selectedUnitId?.takeIf { id -> units.any { it.id == id } }
        val single = if (current == null && units.size == 1) units.single().id else null
        state = state.copy(units = units, selectedUnitId = current ?: single,
            unitData = state.unitData.filterKeys { id -> units.any { it.id == id } },
            about = state.about.filterKeys { id -> units.any { it.id == id } })
        return single
    }

    /**
     * Altera os dados de uma UBS: os do próprio paciente ou, com [acting], os do dependente (só da UBS do vínculo aprovado).
     */
    private fun updateUnit(unitId: String, acting: String? = null, change: (UnitSnapshot) -> UnitSnapshot) {
        if (acting == null) {
            if (state.units?.any { it.id == unitId } != true) return
            state = state.copy(unitData = state.unitData + (unitId to change(state.unitData[unitId] ?: UnitSnapshot())))
        } else {
            val link = state.family.links?.firstOrNull { it.id == acting && it.isApproved() && it.unitId == unitId } ?: return
            state = state.copy(dependentData = state.dependentData + (link.id to change(state.dependentData[link.id] ?: UnitSnapshot())))
        }
    }

    /** Fila, ofertas e consultas de uma UBS; cada parte é aplicada assim que chega. Lança em qualquer falha. */
    private suspend fun loadUnitData(api: PatientApi, session: PatientSession, unitId: String, acting: String? = null) {
        val offers = api.unitOffers(session.token, unitId)
        if (!isLive(session, acting)) return
        updateUnit(unitId, acting) { it.copy(offers = offers) }
        val queue = api.unitQueue(session.token, unitId)
        if (!isLive(session, acting)) return
        updateUnit(unitId, acting) { it.copy(queue = queue) }
        val appointments = api.appointments(session.token, unitId)
        if (isLive(session, acting)) updateUnit(unitId, acting) { it.copy(appointments = appointments) }
    }

    /** Leitura em segundo plano das UBS vinculadas: sem `busy`, silenciosa, e 401 encerra a sessão. */
    private suspend fun loadUnitsInBackground() {
        val session = state.session ?: return
        if (state.loadingUnits || state.units != null) return
        state = state.copy(loadingUnits = true)
        try {
            val api = api(state.address, null)
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

    /** "Atualizar unidades": as UBS do próprio paciente; agindo por um dependente, relê a UBS dele. */
    suspend fun loadUnits() {
        if (state.actingFor != null) { loadSubjectData(); return }
        if (state.loadingUnits) return
        operation(ApiContext.UNIT_LIST) {
            val session = state.session ?: return@operation
            val api = api(state.address, null)
            val units = api.units(session.token)
            if (!isCurrent(session)) return@operation
            val preload = applyUnits(units)
            if (preload != null) loadUnitData(api, session, preload)
        }
    }

    fun selectUnit(id: String) {
        // Agindo por um dependente a UBS é a do vínculo; a escolha de UBS é do próprio paciente.
        if (!state.busy && state.session != null && state.actingFor == null && state.units?.any { it.id == id } == true) {
            if (id == state.selectedUnitId) return
            // Horários e resultado do agendamento pertencem à UBS anterior.
            availabilityGeneration++
            state = state.copy(selectedUnitId = id, availability = null, availabilityLoading = false, bookingError = null, lastBooked = null)
        }
    }

    suspend fun loadAppointments() = operation(ApiContext.UNIT) {
        val session = state.session ?: return@operation
        val acting = state.actingFor
        val unit = state.effective().selectedUnitId ?: return@operation
        val items = api(state.address, acting).appointments(session.token, unit)
        if (isLive(session, acting)) updateUnit(unit, acting) { it.copy(appointments = items) }
    }

    suspend fun cancelAppointment(id: String) = operation(ApiContext.APPOINTMENT_CANCEL) {
        val session = state.session ?: return@operation
        val acting = state.actingFor
        val view = state.effective()
        val unit = view.selectedUnitId ?: return@operation
        if (view.appointments?.none { it.id == id && it.status in listOf("scheduled", "called") } != false) return@operation
        val api = api(state.address, acting)
        try { api.cancelAppointment(session.token, unit, id) }
        catch (failure: ApiFailure) {
            // 404/409: a consulta mudou na unidade; mostrar o estado real em vez do antigo.
            if (failure.status == 404 || failure.status == 409) reloadAppointmentsQuietly(api, session, unit, acting)
            throw failure
        }
        if (isLive(session, acting)) {
            updateUnit(unit, acting) { snapshot -> snapshot.copy(appointments = snapshot.appointments?.map { if (it.id == id) it.copy(status = "cancelled") else it }) }
            state = state.copy(message = "Consulta cancelada.")
        }
    }

    /** Relê as consultas da UBS sem `busy`; `false` se não foi possível (a tela mantém o que já tinha). */
    private suspend fun reloadAppointmentsQuietly(api: PatientApi, session: PatientSession, unit: String, acting: String?): Boolean {
        try {
            val items = api.appointments(session.token, unit)
            if (isLive(session, acting)) updateUnit(unit, acting) { it.copy(appointments = items) }
            return true
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: IOException) { Log.w("NORA", "Consultas não atualizadas após a falha.") }
        catch (_: JSONException) { Log.w("NORA", "Consultas incompatíveis após a falha.") }
        return false
    }

    suspend fun refreshUnit() = operation(ApiContext.UNIT) {
        val session = state.session ?: return@operation
        val acting = state.actingFor
        val unit = state.effective().selectedUnitId ?: return@operation
        val api = api(state.address, acting)
        val offers = api.unitOffers(session.token, unit)
        val queue = api.unitQueue(session.token, unit)
        if (isLive(session, acting)) { if (acting == null) touch(unit); updateUnit(unit, acting) { it.copy(queue = queue, offers = offers) } }
    }

    suspend fun respondUnitOffer(id: String, accept: Boolean) = operation(ApiContext.OFFER_RESPOND) {
        val session = state.session ?: return@operation
        val acting = state.actingFor
        val view = state.effective()
        val unit = view.selectedUnitId ?: return@operation
        view.unitOffers?.singleOrNull { it.id == id && it.status == "pending" } ?: return@operation
        val api = api(state.address, acting)
        if (acting == null) touch(unit)
        val result = try { api.respondUnitOffer(session.token, unit, id, accept) }
        catch (failure: ApiFailure) {
            // Qualquer resposta do servidor (inclusive 409) deixa a oferta diferente do que a tela mostra.
            if (failure.status != 401) reloadAfterResponse(api, session, unit, false, acting)
            throw failure
        }
        catch (io: IOException) { reloadAfterResponse(api, session, unit, false, acting); throw io }
        if (!isLive(session, acting)) return@operation
        // A oferta respondida sai do aviso dos dependentes na hora, sem esperar o próximo ciclo.
        if (acting != null) state = state.copy(dependentOffers = dropOffer(state.dependentOffers, id))
        val reloaded = reloadAfterResponse(api, session, unit, result.accepted, acting)
        if (!isLive(session, acting)) return@operation
        if (!reloaded) updateUnit(unit, acting) { snapshot ->
            snapshot.copy(offers = snapshot.offers?.map { if (it.id == id) it.copy(status = if (accept) "accepted" else "declined") else it })
        }
        val subject = if (acting == null) "Sua consulta foi agendada." else "A consulta foi agendada."
        val outcome = when {
            accept && result.accepted -> "Oferta aceita. $subject"
            accept -> "A unidade não confirmou o agendamento desta oportunidade. Confira ${if (acting == null) "suas consultas" else "as consultas"}."
            else -> if (acting == null) "Oferta recusada. Você continua na fila, no mesmo lugar." else "Oferta recusada. A pessoa continua na fila, no mesmo lugar."
        }
        state = state.copy(message = if (reloaded) outcome else "$outcome Não foi possível atualizar a tela; use Atualizar.")
    }

    private fun dropOffer(list: List<DependentOffers>, offerId: String): List<DependentOffers> =
        list.map { dependent -> dependent.copy(offers = dependent.offers.filter { it.id != offerId }) }.filter { it.offers.isNotEmpty() }

    /** Recarrega ofertas e fila da UBS (e as consultas quando houve aceite). `false` se não foi possível. */
    private suspend fun reloadAfterResponse(api: PatientApi, session: PatientSession, unit: String, withAppointments: Boolean, acting: String?): Boolean {
        try {
            val offers = api.unitOffers(session.token, unit)
            val queue = api.unitQueue(session.token, unit)
            val appointments = if (withAppointments) api.appointments(session.token, unit) else null
            if (isLive(session, acting)) {
                if (acting == null) touch(unit)
                updateUnit(unit, acting) { it.copy(offers = offers, queue = queue, appointments = appointments ?: it.appointments) }
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
     * da presença, de modo que uma resposta atrasada depois de logout/401 é descartada. Sempre são as ofertas do próprio
     * paciente, mesmo que ele esteja agindo por um dependente.
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
                    val offers = api(state.address, null).unitOffers(session.token, unit.id)
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

    // ---- Ciclo de 30 s: comunicados, família e e-mail da conta ------------------------------------------

    /**
     * Uma chamada barata do ciclo de 30 s, sob o mesmo mutex e a mesma geração da presença: uma resposta atrasada depois de logout ou 401 é
     * descartada, falhas de rede são silenciosas e 401 encerra a sessão. `null` quando a chamada falhou ou ficou obsoleta.
     */
    private suspend fun <T : Any> cycleCall(session: PatientSession, generation: Long, what: String, call: suspend (PatientApi) -> T): T? =
        presenceMutex.withLock {
            if (!isCurrent(session, generation)) return@withLock null
            try { call(api(state.address, null)).takeIf { isCurrent(session, generation) } }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (failure: ApiFailure) {
                if (failure.status == 401 && isCurrent(session, generation)) state = signedOut(state, failure.message)
                else Log.w("NORA", "$what não atualizados: HTTP ${failure.status}.")
                null
            }
            catch (_: IOException) { Log.w("NORA", "$what não atualizados; a próxima consulta tentará novamente."); null }
            catch (_: JSONException) { Log.w("NORA", "O serviço retornou $what incompatíveis."); null }
            catch (_: IllegalArgumentException) { Log.w("NORA", "O serviço retornou $what com identificador inválido."); null }
        }

    /**
     * Parte do ciclo de 30 s que vem depois das ofertas: comunicados novos (incremental pelo cursor), pedidos de família enquanto algum
     * estiver pendente, ofertas abertas dos dependentes e a sugestão de e-mail da UBS. Cada uma é uma única requisição leve. Devolve os
     * comunicados que chegaram agora e ainda não estavam na lista (a primeira leitura, sem cursor, nunca gera aviso).
     */
    suspend fun pollCommunity(session: PatientSession, generation: Long): List<Announcement> {
        if (!isCurrent(session, generation)) return emptyList()
        val fresh = pollAnnouncements(session, generation)
        if (!isCurrent(session, generation)) return fresh
        // Pedidos: lidos de novo só enquanto há algum pendente (para a aprovação aparecer) ou enquanto a lista não foi lida.
        val links = state.family.links
        if (links == null || links.any { it.status == "pending" }) {
            cycleCall(session, generation, "Pedidos de família") { it.dependents(session.token) }?.let { if (isCurrent(session, generation)) applyFamily(it) }
        }
        if (!isCurrent(session, generation)) return fresh
        if (approvedLinks(state.family.links).isNotEmpty()) {
            cycleCall(session, generation, "Ofertas dos dependentes") { it.dependentsOpenOffers(session.token) }?.let { offers ->
                val approved = approvedLinks(state.family.links).map { it.id }.toSet()
                if (isCurrent(session, generation)) {
                    state = state.copy(dependentOffers = offers.filter { it.dependentId in approved })
                    refreshActingOffers(session, generation)
                }
            }
        } else if (state.dependentOffers.isNotEmpty()) state = state.copy(dependentOffers = emptyList())
        if (!isCurrent(session, generation)) return fresh
        cycleCall(session, generation, "Sugestão de e-mail") { SuggestionRead(it.emailSuggestion(session.token)) }
            ?.let { if (isCurrent(session, generation)) applyEmailSuggestion(it.value) }
        return fresh
    }

    /**
     * Agindo por um dependente, Início e Vaga Relâmpago mostram as ofertas lidas pela rota dele, não as do aviso agregado. Quando as
     * abertas do aviso não batem com as abertas da tela (oferta nova, ou uma que deixou de estar aberta), relê a lista dele: uma
     * chamada só quando mudou. Com uma ação em andamento (`busy`) espera o próximo ciclo, para não sobrescrever o resultado dela.
     */
    private suspend fun refreshActingOffers(session: PatientSession, generation: Long) {
        val link = state.actingLink() ?: return
        if (state.busy) return
        val now = clock()
        val announced = state.dependentOffers.firstOrNull { it.dependentId == link.id }?.offers.orEmpty()
            .filter { it.isOpenAt(now) }.mapTo(HashSet()) { it.id }
        val shown = state.dependentData[link.id]?.offers.orEmpty().filter { it.isOpenAt(now) }.mapTo(HashSet()) { it.id }
        if (announced == shown) return
        val offers = cycleCall(session, generation, "Ofertas do dependente") { it.dependentUnitOffers(session.token, link.id, link.unitId) } ?: return
        if (isLive(session, link.id) && !state.busy) updateUnit(link.unitId, link.id) { it.copy(offers = offers) }
    }

    private class SuggestionRead(val value: EmailSuggestion?)

    private suspend fun pollAnnouncements(session: PatientSession, generation: Long): List<Announcement> = announcementMutex.withLock {
        if (!isCurrent(session, generation)) return@withLock emptyList()
        val cursor = state.announcements.cursor
        val read = cycleCall(session, generation, "Comunicados") { readAnnouncements(it, session.token, cursor) } ?: return@withLock emptyList()
        if (!isCurrent(session, generation)) return@withLock emptyList()
        if (cursor == null || !state.announcements.loaded) {
            state = state.copy(announcements = AnnouncementsState(read.items.sortedBy { it.sequence }, read.cursor, loaded = true, catchingUp = read.hasMore))
            emptyList()
        } else {
            val merged = mergeAnnouncements(state.announcements.items, read.items)
            val catchingUp = state.announcements.catchingUp
            state = state.copy(announcements = state.announcements.copy(items = merged.items, cursor = maxOf(read.cursor, cursor), error = null,
                catchingUp = catchingUp && read.hasMore))
            // Continuação de uma leitura completa cortada no teto: são comunicados antigos, não novos.
            if (catchingUp) emptyList() else merged.fresh
        }
    }

    /**
     * Lê a rota de comunicados e segue o cursor enquanto houver mais: sem cursor ([after] nulo, leitura completa) até o fim, para a lista
     * não perder os mais novos (o servidor devolve em ordem crescente); no ciclo de 30 s, algumas páginas por vez. Um cursor que não
     * avança encerra a leitura. [AnnouncementRead.hasMore] diz se ainda sobrou algo.
     */
    private suspend fun readAnnouncements(api: PatientApi, token: String, after: Long?): AnnouncementRead {
        val items = ArrayList<Announcement>()
        var cursor = after
        var latest = after ?: 0L
        var more = false
        for (page in 1..(if (after == null) FULL_ANNOUNCEMENT_PAGES else CYCLE_ANNOUNCEMENT_PAGES)) {
            val result = api.announcements(token, cursor)
            items += result.announcements
            val advanced = result.cursor > (cursor ?: 0L)
            latest = result.cursor
            cursor = result.cursor
            more = result.hasMore && advanced
            if (!more) break
        }
        return AnnouncementRead(items.distinctBy { it.id }, latest, more)
    }

    // ---- Comunicados da rede (aba Avisos) --------------------------------------------------------------

    /** Leitura completa: ao abrir a aba Avisos e ao mudar os vínculos. Não avisa nada (é o que o paciente está vendo agora). */
    suspend fun loadAnnouncements() {
        val session = state.session ?: return
        announcementMutex.withLock {
            if (!isCurrent(session) || state.announcements.loading) return@withLock
            state = state.copy(announcements = state.announcements.copy(loading = true, error = null))
            try {
                val read = readAnnouncements(api(state.address, null), session.token, null)
                if (isCurrent(session)) state = state.copy(announcements = AnnouncementsState(read.items.sortedBy { it.sequence }, read.cursor, loaded = true,
                    catchingUp = read.hasMore))
            }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (failure: ApiFailure) {
                if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
                else announcementsFailed(session, failureMessage(failure.status, failure.code, ApiContext.ANNOUNCEMENTS))
            }
            catch (_: IOException) { announcementsFailed(session, CONNECTION_FAILED_MESSAGE) }
            catch (_: JSONException) { announcementsFailed(session, INCOMPATIBLE_RESPONSE_MESSAGE) }
            catch (_: IllegalArgumentException) { announcementsFailed(session, INCOMPATIBLE_RESPONSE_MESSAGE) }
            finally { if (isCurrent(session) && state.announcements.loading) state = state.copy(announcements = state.announcements.copy(loading = false)) }
        }
    }

    private fun announcementsFailed(session: PatientSession, error: String) {
        if (isCurrent(session)) state = state.copy(announcements = state.announcements.copy(loading = false, error = error))
    }

    /** Marca um comunicado como lido (sem `busy`); repetir o toque enquanto a chamada está em voo não gera outra requisição. */
    suspend fun markAnnouncementRead(id: String) {
        val session = state.session ?: return
        val item = state.announcements.items.firstOrNull { it.id == id } ?: return
        if (item.isRead || !markingRead.add(id)) return
        try {
            api(state.address, null).markAnnouncementRead(session.token, id)
            if (isCurrent(session)) {
                val readAt = formatInstantUtc(clock())
                state = state.copy(announcements = state.announcements.copy(error = null,
                    items = state.announcements.items.map { if (it.id == id) it.copy(isRead = true, readAt = readAt) else it }))
            }
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
            else announcementsFailed(session, failureMessage(failure.status, failure.code, ApiContext.ANNOUNCEMENTS))
        }
        catch (_: IOException) { announcementsFailed(session, CONNECTION_FAILED_MESSAGE) }
        catch (_: JSONException) { announcementsFailed(session, INCOMPATIBLE_RESPONSE_MESSAGE) }
        finally { markingRead.remove(id) }
    }

    // ---- Agendamento pelo catálogo e confirmação de presença -------------------------------------------

    /**
     * Leitura sem `busy`: falha vira [PatientState.bookingError] (ou [PatientState.message] com [toMessage]); 401 encerra a sessão;
     * 403 agindo por um dependente relê os vínculos (o vínculo pode ter terminado). `true` se deu certo.
     */
    private suspend fun quiet(session: PatientSession, context: ApiContext, acting: String? = null, toMessage: Boolean = false,
        action: suspend () -> Unit): Boolean {
        val error: String
        try { action(); return true }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401) { if (isCurrent(session)) state = signedOut(state, failure.message); return false }
            if (failure.status == 403 && acting != null) loadFamily()
            error = failureMessage(failure.status, failure.code, context, acting != null, actingName(acting))
        }
        catch (_: IOException) { error = CONNECTION_FAILED_MESSAGE }
        catch (_: JSONException) { error = INCOMPATIBLE_RESPONSE_MESSAGE }
        catch (_: IllegalArgumentException) { error = "O serviço retornou um identificador inválido." }
        // O erro é da pessoa por quem se agia: se a troca (ou o fim do vínculo) já devolveu a visão ao paciente, não vai para ela.
        if (isLive(session, acting)) state = if (toMessage) state.copy(message = error) else state.copy(bookingError = error)
        return false
    }

    /** Ao abrir o agendamento: limpa o resultado anterior e relê o catálogo de especialidades (uma requisição). */
    suspend fun openBooking() {
        val session = state.session ?: return
        if (state.specialtiesLoading) return
        val acting = state.actingFor
        availabilityGeneration++
        state = state.copy(lastBooked = null, availability = null, availabilityLoading = false, bookingError = null, specialtiesLoading = true)
        var loaded: List<Specialty>? = null
        try { quiet(session, ApiContext.SPECIALTIES, acting) { loaded = api(state.address, acting).specialties(session.token) } }
        finally { if (isCurrent(session)) state = state.copy(specialtiesLoading = false) }
        loaded?.let { if (isCurrent(session)) state = state.copy(specialties = it) }
    }

    /**
     * Horários livres da especialidade na UBS selecionada (a do vínculo, agindo por um dependente), para uma semana de
     * [bookingWeek]: uma requisição por mudança de especialidade ou de semana, sem `busy`. Só a leitura mais recente grava o resultado.
     */
    suspend fun loadAvailability(specialtyId: String, week: Int) {
        val session = state.session ?: return
        val acting = state.actingFor
        val unit = state.effective().selectedUnitId ?: return
        val window = bookingWeek(clock(), week) ?: return
        val generation = ++availabilityGeneration
        state = state.copy(availabilityLoading = true, bookingError = null)
        var slots: List<AvailableSlot>? = null
        quiet(session, ApiContext.AVAILABILITY, acting) {
            slots = api(state.address, acting).availability(session.token, unit, specialtyId, window.fromMillis, window.toMillis)
        }
        if (!isLive(session, acting) || generation != availabilityGeneration) return
        // Falha: não deixa à vista horários que podem já ter sido tomados.
        state = state.copy(availabilityLoading = false, availability = slots?.let { AvailabilityResult(unit, specialtyId, week, it) })
    }

    /**
     * Marca o horário (que precisa estar na lista de horários exibida) na UBS selecionada, para o próprio paciente ou para o
     * dependente. Vale na hora. Depois do aceite relê as consultas da UBS e tira o horário da lista; `slot-unavailable` recarrega os horários.
     */
    suspend fun bookAppointment(slot: AvailableSlot) = operation(ApiContext.BOOKING) {
        val session = state.session ?: return@operation
        val acting = state.actingFor
        val view = state.effective()
        val unit = view.selectedUnitId ?: return@operation
        val shown = view.availability?.takeIf { it.unitId == unit && slot in it.slots } ?: return@operation
        val api = api(state.address, acting)
        val booked = try { api.bookAppointment(session.token, unit, slot.professionalId, slot.specialtyId, slot.startsAt) }
        catch (failure: ApiFailure) {
            if (failure.code == SLOT_UNAVAILABLE && isLive(session, acting) && state.effective().selectedUnitId == unit) loadAvailability(shown.specialtyId, shown.week)
            throw failure
        }
        if (!isLive(session, acting)) return@operation
        val reloaded = reloadAppointmentsQuietly(api, session, unit, acting)
        if (!isLive(session, acting)) return@operation
        // Sem a releitura, a consulta nova entra na lista que já estava lida; lista não lida continua não lida.
        if (!reloaded) updateUnit(unit, acting) { snapshot -> snapshot.copy(appointments = snapshot.appointments?.let { it + booked }) }
        val appointment = state.effective().unitData[unit]?.appointments?.firstOrNull { it.id == booked.id } ?: booked
        val remaining = state.availability?.takeIf { it.unitId == shown.unitId && it.specialtyId == shown.specialtyId && it.week == shown.week }
            ?.let { it.copy(slots = it.slots - slot) }
        state = state.copy(availability = remaining ?: state.availability, lastBooked = BookedAppointment(unit, appointment),
            message = if (reloaded) "Consulta marcada. Ela já vale e aparece em Consultas."
            else "Consulta marcada. Não foi possível atualizar ${if (acting == null) "suas" else "as"} consultas; use Atualizar consultas.")
    }

    /**
     * Confirma a presença na consulta [id] da UBS [unitId] (a da próxima consulta pode não ser a UBS selecionada). Consulta já
     * confirmada não gera nova requisição. O servidor decide a janela: 409 `confirmation-window` vira mensagem e a lista é relida.
     */
    suspend fun confirmPresence(unitId: String, id: String) = operation(ApiContext.CONFIRMATION) {
        val session = state.session ?: return@operation
        val acting = state.actingFor
        val view = state.effective()
        if (view.units?.any { it.id == unitId } != true) return@operation
        val current = view.unitData[unitId]?.appointments?.singleOrNull { it.id == id } ?: return@operation
        if (current.status != "scheduled" || current.confirmedAt != null) return@operation
        val api = api(state.address, acting)
        val confirmed = try { api.confirmPresence(session.token, unitId, id) }
        catch (failure: ApiFailure) {
            if (failure.status == 404 || failure.status == 409) reloadAppointmentsQuietly(api, session, unitId, acting)
            throw failure
        }
        if (!isLive(session, acting)) return@operation
        updateUnit(unitId, acting) { snapshot -> snapshot.copy(appointments = snapshot.appointments?.map { if (it.id == id) confirmed else it }) }
        state = state.copy(message = presenceConfirmedText(confirmed) + ".")
    }

    // ---- Participação, fila, vínculo e senha ----------------------------------------------------------

    /**
     * Lê a participação em cada UBS da visão atual (as do paciente, ou a do dependente), uma requisição por UBS, sem `busy`. Quem
     * chama decide quando: ao abrir a Jornada e ao pedir atualização. Uma UBS que não responde fica marcada, nunca vira zero.
     */
    suspend fun loadAttendance() {
        val session = state.session ?: return
        val acting = state.actingFor
        // Cada pessoa tem a sua leitura: uma leitura própria ainda em voo não impede a do dependente (nem o contrário).
        val owner = acting ?: ""
        if (owner in attendanceInFlight) return
        if (acting == null && state.units == null) loadUnitsInBackground()
        val units = state.effective().units ?: return
        if (!isLive(session, acting) || owner in attendanceInFlight) return
        attendanceInFlight += owner
        state = state.copy(attendanceLoading = true)
        try {
            val api = api(state.address, acting)
            for (unit in units) {
                if (!isLive(session, acting)) return
                var value: UnitAttendance? = null
                try { value = api.attendance(session.token, unit.id) }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (failure: ApiFailure) {
                    if (failure.status == 401) { if (isCurrent(session)) state = signedOut(state, failure.message); return }
                    // 403 agindo por um dependente: o vínculo pode ter terminado; relê os pedidos (como as demais leituras da Jornada dele).
                    if (failure.status == 403 && acting != null) loadFamily()
                    Log.w("NORA", "Participação não carregada: HTTP ${failure.status}.")
                }
                catch (_: IOException) { Log.w("NORA", "Participação não carregada; use Atualizar para tentar novamente.") }
                catch (_: JSONException) { Log.w("NORA", "O serviço retornou uma participação incompatível.") }
                catch (_: IllegalArgumentException) { Log.w("NORA", "O serviço retornou uma UBS com identificador inválido.") }
                if (isLive(session, acting)) updateUnit(unit.id, acting) { it.copy(attendance = value, attendanceFailed = value == null) }
            }
        }
        finally {
            attendanceInFlight -= owner
            // O indicador é de quem está em tela: a leitura de outra pessoa que termina agora não o apaga.
            if (isCurrent(session) && state.actingFor == acting) state = state.copy(attendanceLoading = false)
        }
    }

    /** Sai da fila da UBS da visão atual. 404/409: a entrada mudou na unidade; mostra o estado real recarregando. */
    suspend fun cancelQueueEntry(id: Long) = operation(ApiContext.QUEUE_CANCEL) {
        val session = state.session ?: return@operation
        val acting = state.actingFor
        val view = state.effective()
        val unit = view.selectedUnitId ?: return@operation
        val entry = view.unitQueue?.singleOrNull { it.id == id && (it.status == "waiting" || it.status == "offered") } ?: return@operation
        val api = api(state.address, acting)
        if (acting == null) touch(unit)
        try { api.cancelQueueEntry(session.token, unit, id) }
        catch (failure: ApiFailure) {
            if (failure.status == 404 || failure.status == 409) reloadAfterResponse(api, session, unit, false, acting)
            throw failure
        }
        if (!isLive(session, acting)) return@operation
        val reloaded = reloadAfterResponse(api, session, unit, false, acting)
        if (!isLive(session, acting)) return@operation
        if (!reloaded) updateUnit(unit, acting) { snapshot ->
            snapshot.copy(queue = snapshot.queue?.map { if (it.id == id) it.copy(status = "cancelled") else it })
        }
        val outcome = if (acting == null) "Você saiu da fila de ${entry.specialty}." else "A saída da fila de ${entry.specialty} foi registrada."
        state = state.copy(message = if (reloaded) outcome else "$outcome Não foi possível atualizar a tela; use Atualizar.")
    }

    /**
     * Confere, sem `busy`, o que a remoção da UBS vai cancelar, pela prévia do servidor (`unlink-preview`), relida a cada abertura do aviso:
     * os números são exatamente os que a remoção cancelaria, sem limite de data. `null` quando não foi possível conferir (UBS indisponível,
     * limite de leituras, cadastro desativado, sem rede): o aviso então diz, sem número, que todas as consultas futuras e as solicitações
     * de fila da UBS serão canceladas.
     */
    suspend fun checkUnlinkImpact(unitId: String): UnlinkImpact? {
        val session = state.session ?: return null
        if (state.units?.any { it.id == unitId } != true) return null
        try {
            val impact = api(state.address, null).unlinkPreview(session.token, unitId)
            return if (isCurrent(session)) impact else null
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) { if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message) }
        catch (_: IOException) { Log.w("NORA", "Prévia da remoção da UBS não carregada.") }
        catch (_: JSONException) { Log.w("NORA", "Prévia da remoção da UBS incompatível.") }
        catch (_: IllegalArgumentException) { Log.w("NORA", "Prévia da remoção da UBS inválida.") }
        return null
    }

    /** Remove o vínculo com a UBS selecionada; o servidor cancela consultas futuras e solicitações. */
    suspend fun unlinkSelectedUnit() = operation(ApiContext.UNLINK) {
        val session = state.session ?: return@operation
        val unit = state.units?.find { it.id == state.selectedUnitId } ?: return@operation
        val api = api(state.address, null)
        val result = api.unlinkUnit(session.token, unit.id)
        if (!isCurrent(session)) return@operation
        unitVersions.remove(unit.id)
        val remaining = try { api.units(session.token) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) { state = signedOut(state, failure.message); return@operation }
            null
        }
        catch (_: IOException) { null }
        catch (_: JSONException) { null }
        catch (_: IllegalArgumentException) { null }
        if (!isCurrent(session)) return@operation
        // Se a lista não pôde ser relida, a UBS removida sai do estado local mesmo assim.
        val preload = applyUnits((remaining ?: state.units.orEmpty()).filter { it.id != unit.id })
        state = state.copy(message = unlinkResultMessage(unit.name, result))
        onOffersRemoved?.invoke()
        if (preload != null) try { loadUnitData(api, session, preload) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) { if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message) }
        catch (_: IOException) { Log.w("NORA", "Dados da UBS restante não carregados.") }
        catch (_: JSONException) { Log.w("NORA", "Dados incompatíveis da UBS restante.") }
        catch (_: IllegalArgumentException) { Log.w("NORA", "UBS restante com identificador inválido.") }
        // Os comunicados dependem dos vínculos: leitura completa de novo.
        if (isCurrent(session)) loadAnnouncements()
    }

    /**
     * Troca a senha com a sessão aberta (a atual continua; o servidor desconecta os outros aparelhos). As senhas
     * ficam só nos argumentos desta chamada: nunca no estado, no disco nem no log.
     */
    suspend fun changePassword(currentPassword: String, newPassword: String) = guarded {
        val session = state.session ?: throw ApiFailure(401, "Entre na sua conta para trocar a senha.", "unauthorized")
        try { api(state.address, null).changePassword(session.token, currentPassword, newPassword) }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
            throw failure
        }
        if (isCurrent(session)) state = state.copy(passwordChangeRequired = false)
    }

    fun selectNearestUnit(latitude: Double, longitude: Double): PatientHealthUnit? {
        if (state.busy || state.session == null || state.actingFor != null || !latitude.isFinite() || !longitude.isFinite() ||
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

    // ---- Jornada: exames e vacinas ------------------------------------------------------------------------

    /**
     * Lê exames e vacinas, sem `busy`: do próprio paciente, todas as UBS numa só chamada agregada; agindo por um dependente, os dele
     * na UBS do vínculo. A UBS que não respondeu (ou não veio na resposta) fica marcada; nunca vira "sem registros".
     */
    suspend fun loadRecords() {
        val session = state.session ?: return
        if (state.records.loading) return
        val acting = state.actingFor
        if (acting == null && state.units == null) loadUnitsInBackground()
        // Enquanto as UBS chegavam a pessoa em tela pode ter mudado: a leitura é de quem estava em tela e não marca a visão de outro.
        if (!isCurrent(session) || state.actingFor != acting) return
        val units = state.effective().units ?: return
        if (units.isEmpty()) return
        state = state.copy(records = RecordsState(loading = true))
        try {
            val api = api(state.address, acting)
            if (acting == null) {
                val result = api.healthRecords(session.token)
                if (!isLive(session, acting)) return
                val data = state.unitData.toMutableMap()
                for (unit in units) {
                    val found = result.firstOrNull { it.unitId == unit.id }
                    data[unit.id] = (data[unit.id] ?: UnitSnapshot()).copy(records = found, recordsFailed = found == null)
                }
                state = state.copy(unitData = data)
            } else {
                val unit = units.single()
                val result = api.dependentRecords(session.token, acting, unit.id)
                if (isLive(session, acting)) updateUnit(unit.id, acting) { it.copy(records = result, recordsFailed = false) }
            }
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401) { if (isCurrent(session)) state = signedOut(state, failure.message) }
            else {
                // 403 agindo por um dependente: o vínculo pode ter terminado; relê os pedidos (como as demais leituras do dependente).
                if (failure.status == 403 && acting != null) loadFamily()
                recordsFailed(session, acting, units, failureMessage(failure.status, failure.code, ApiContext.RECORDS, acting != null))
            }
        }
        catch (_: IOException) { recordsFailed(session, acting, units, CONNECTION_FAILED_MESSAGE) }
        catch (_: JSONException) { recordsFailed(session, acting, units, INCOMPATIBLE_RESPONSE_MESSAGE) }
        catch (_: IllegalArgumentException) { recordsFailed(session, acting, units, INCOMPATIBLE_RESPONSE_MESSAGE) }
        // O indicador é de quem está em tela: a leitura de outra pessoa que termina agora não o apaga.
        finally { if (isCurrent(session) && state.actingFor == acting && state.records.loading) state = state.copy(records = state.records.copy(loading = false)) }
    }

    private fun recordsFailed(session: PatientSession, acting: String?, units: List<PatientHealthUnit>, error: String) {
        if (!isLive(session, acting)) return
        // Sem dado novo, nada de dado velho: a UBS fica marcada como "não respondeu".
        for (unit in units) updateUnit(unit.id, acting) { it.copy(records = null, recordsFailed = true) }
        // A mensagem só vai para a tela da pessoa a quem a leitura pertence.
        if (state.actingFor == acting) state = state.copy(records = RecordsState(error = error))
    }

    // ---- Família: pedidos, vínculos e "agindo por" ----------------------------------------------------------

    /**
     * Troca a lista de vínculos; se o vínculo pelo qual se age deixou de estar aprovado, volta a agir por si. Como em [actFor], os
     * indicadores de leitura passam a ser os do próprio paciente: a leitura do dependente ainda em voo termina sem mexer neles.
     */
    private fun applyFamily(links: List<FamilyLink>) {
        val approved = approvedLinks(links).map { it.id }.toSet()
        val lostActing = state.actingFor != null && state.actingFor !in approved
        state = state.copy(family = FamilyState(links = links), dependentData = state.dependentData.filterKeys { it in approved },
            dependentOffers = state.dependentOffers.filter { it.dependentId in approved },
            actingFor = if (lostActing) null else state.actingFor,
            availability = if (lostActing) null else state.availability, availabilityLoading = state.availabilityLoading && !lostActing,
            lastBooked = if (lostActing) null else state.lastBooked, bookingError = if (lostActing) null else state.bookingError,
            records = if (lostActing) RecordsState() else state.records,
            attendanceLoading = if (lostActing) "" in attendanceInFlight else state.attendanceLoading,
            message = if (lostActing) "O vínculo com esta pessoa terminou. Você voltou a agir por você." else state.message)
    }

    /** Lê os pedidos e vínculos do responsável (sem `busy`); a falha fica em [FamilyState.error]. */
    suspend fun loadFamily() {
        val session = state.session ?: return
        if (state.family.loading) return
        state = state.copy(family = state.family.copy(loading = true, error = null))
        try {
            val links = api(state.address, null).dependents(session.token)
            if (isCurrent(session)) applyFamily(links)
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
            else familyFailed(session, failureMessage(failure.status, failure.code, ApiContext.FAMILY))
        }
        catch (_: IOException) { familyFailed(session, CONNECTION_FAILED_MESSAGE) }
        catch (_: JSONException) { familyFailed(session, INCOMPATIBLE_RESPONSE_MESSAGE) }
        catch (_: IllegalArgumentException) { familyFailed(session, INCOMPATIBLE_RESPONSE_MESSAGE) }
        finally { if (isCurrent(session) && state.family.loading) state = state.copy(family = state.family.copy(loading = false)) }
    }

    private fun familyFailed(session: PatientSession, error: String) {
        if (isCurrent(session)) state = state.copy(family = state.family.copy(loading = false, error = error))
    }

    /**
     * Pede o vínculo com um dependente (CPF e UBS onde ele tem cadastro). O CPF é conferido aqui antes de qualquer requisição e só
     * trafega no corpo. `true` quando o pedido foi aceito pelo servidor (a equipe da UBS ainda vai conferir os documentos).
     */
    suspend fun requestDependent(cpf: String, unitId: String): Boolean {
        var requested = false
        operation(ApiContext.FAMILY_REQUEST) {
            val session = state.session ?: return@operation
            val digits = cpfDigits(cpf)
            require(isValidCpf(digits)) { "Confira o CPF informado." }
            require(unitId.isNotBlank()) { "Escolha a UBS onde esta pessoa tem cadastro." }
            val link = api(state.address, null).requestDependent(session.token, digits, unitId)
            if (!isCurrent(session)) return@operation
            state = state.copy(family = state.family.copy(links = listOf(link) + state.family.links.orEmpty().filter { it.id != link.id }, error = null),
                message = "Pedido enviado. A equipe da UBS vai conferir os documentos e a resposta aparece aqui.")
            requested = true
        }
        return requested
    }

    /** Cancela um pedido ainda pendente. */
    suspend fun cancelDependentRequest(id: String) = operation(ApiContext.FAMILY) {
        val session = state.session ?: return@operation
        if (state.family.links?.none { it.id == id && it.status == "pending" } != false) return@operation
        val updated = try { api(state.address, null).cancelDependentRequest(session.token, id) }
        catch (failure: ApiFailure) { if (failure.status == 404 || failure.status == 409) loadFamily(); throw failure }
        if (isCurrent(session)) {
            replaceLink(updated)
            state = state.copy(message = "Pedido cancelado.")
        }
    }

    /** Encerra um vínculo aprovado; se era o "agindo por", volta a agir por si. */
    suspend fun endDependent(id: String) = operation(ApiContext.FAMILY) {
        val session = state.session ?: return@operation
        val link = state.family.links?.firstOrNull { it.id == id && it.isApproved() } ?: return@operation
        val updated = try { api(state.address, null).endDependentLink(session.token, id) }
        catch (failure: ApiFailure) { if (failure.status == 404 || failure.status == 409) loadFamily(); throw failure }
        if (isCurrent(session)) {
            replaceLink(updated)
            state = state.copy(message = "O vínculo com ${dependentDisplayName(link)} foi encerrado.")
        }
    }

    private fun replaceLink(updated: FamilyLink) {
        val links = state.family.links.orEmpty()
        applyFamily(if (links.any { it.id == updated.id }) links.map { if (it.id == updated.id) updated else it } else listOf(updated) + links)
    }

    /**
     * Passa a agir pelo dependente [linkId] (um vínculo aprovado) ou, com `null`, por si. A troca é imediata: Início, Consultas,
     * Agendamento, Fila, Vaga Relâmpago e Jornada passam a usar as rotas do dependente e o que estava em tela do outro lado é limpo.
     * Devolve `true` quando agora se age por um dependente e seus dados ainda precisam ser lidos ([loadActingData]).
     */
    fun actFor(linkId: String?): Boolean {
        if (state.session == null || state.busy || state.actingFor == linkId) return false
        if (linkId != null && state.family.links?.none { it.id == linkId && it.isApproved() } != false) return false
        switchView(linkId)
        return linkId != null
    }

    /**
     * A tela UBS é sempre do próprio responsável: abri-la agindo por um dependente volta a agir por si, mesmo com uma ação do dependente
     * em andamento (ela termina sem pôr mensagem na tela). `true` quando houve a troca, para a tela avisar.
     */
    fun showOwnUnits(): Boolean {
        if (state.session == null || state.actingFor == null) return false
        switchView(null)
        return true
    }

    private fun switchView(linkId: String?) {
        availabilityGeneration++
        viewSwitches++
        // Leituras em voo de quem deixou de estar em tela terminam sozinhas e não mexem nos indicadores da nova visão.
        state = state.copy(actingFor = linkId, availability = null, availabilityLoading = false, bookingError = null, lastBooked = null,
            message = null, records = RecordsState(), attendanceLoading = (linkId ?: "") in attendanceInFlight)
    }

    /** [actFor] seguido da leitura dos dados do dependente. */
    suspend fun switchActing(linkId: String?) { if (actFor(linkId)) loadActingData() }

    /** Lê oferta, fila e consultas da UBS do dependente atual (sem `busy`); a falha vira [PatientState.message]. */
    suspend fun loadActingData() = loadSubjectData()

    private suspend fun loadSubjectData() {
        val session = state.session ?: return
        val acting = state.actingFor ?: return
        val unit = state.effective().selectedUnitId ?: return
        quiet(session, ApiContext.UNIT, acting, toMessage = true) { loadUnitData(api(state.address, acting), session, unit, acting) }
    }

    // ---- E-mail da conta guiado pela UBS --------------------------------------------------------------

    private fun applyEmailSuggestion(suggestion: EmailSuggestion?) {
        val sentFor = state.email.codeSentFor?.takeIf { it == suggestion?.id }
        state = state.copy(email = EmailState(suggestion, loaded = true, codeSentFor = sentFor))
    }

    /** Lê a sugestão de e-mail (ao abrir o Perfil; o ciclo de 30 s já a relê sozinho). */
    suspend fun loadEmailSuggestion() {
        val session = state.session ?: return
        try {
            val suggestion = api(state.address, null).emailSuggestion(session.token)
            if (isCurrent(session)) applyEmailSuggestion(suggestion)
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
            else Log.w("NORA", "Sugestão de e-mail não lida: HTTP ${failure.status}.")
        }
        catch (_: IOException) { Log.w("NORA", "Sugestão de e-mail não lida; o próximo ciclo tentará novamente.") }
        catch (_: JSONException) { Log.w("NORA", "O serviço retornou uma sugestão de e-mail incompatível.") }
        catch (_: IllegalArgumentException) { Log.w("NORA", "O serviço retornou uma sugestão de e-mail inválida.") }
    }

    /** Pede o código ao endereço novo (o servidor limita a três envios por sugestão). */
    suspend fun sendEmailCode() = operation(ApiContext.EMAIL_CHANGE) {
        val session = state.session ?: return@operation
        val suggestion = state.email.suggestion ?: return@operation
        try { api(state.address, null).sendEmailSuggestionCode(session.token) }
        catch (failure: ApiFailure) { if (failure.status == 404 && isCurrent(session)) state = state.copy(email = EmailState(loaded = true)); throw failure }
        if (isCurrent(session)) state = state.copy(email = state.email.copy(codeSentFor = suggestion.id),
            message = "Enviamos um código de 6 dígitos para ${suggestion.maskedEmail}.")
    }

    /** Confirma o código: `true` troca o e-mail da conta. Código errado, vencido ou já usado devolve `false` e mantém a sugestão. */
    suspend fun confirmEmailCode(code: String): Boolean {
        var changed = false
        operation(ApiContext.EMAIL_CHANGE) {
            val session = state.session ?: return@operation
            require(state.email.suggestion != null) { "Esta sugestão de e-mail não existe mais." }
            require(code.length == 6 && code.all { it in '0'..'9' }) { "Digite os 6 dígitos do código." }
            val valid = try { api(state.address, null).confirmEmailSuggestion(session.token, code) }
            catch (failure: ApiFailure) { if (failure.status == 404 && isCurrent(session)) state = state.copy(email = EmailState(loaded = true)); throw failure }
            if (!isCurrent(session)) return@operation
            if (valid) {
                changed = true
                state = state.copy(email = EmailState(loaded = true), message = "O e-mail da conta foi trocado. Use o novo e-mail para entrar.")
            } else state = state.copy(message = "Código incorreto, vencido ou já usado. Confira os dígitos ou peça outro código.")
        }
        return changed
    }

    /** Dispensa a sugestão: o e-mail da conta continua o mesmo. */
    suspend fun dismissEmailSuggestion() = operation(ApiContext.EMAIL_CHANGE) {
        val session = state.session ?: return@operation
        if (state.email.suggestion == null) return@operation
        try { api(state.address, null).dismissEmailSuggestion(session.token) }
        catch (failure: ApiFailure) { if (failure.status == 404 && isCurrent(session)) state = state.copy(email = EmailState(loaded = true)); throw failure }
        if (isCurrent(session)) state = state.copy(email = EmailState(loaded = true), message = "Tudo bem. O e-mail da conta continua o mesmo.")
    }

    // ---- Entrar com Google: situação, vínculo e desvínculo -------------------------------------------------

    /** Lê se o servidor tem o Google ligado e se há conta Google vinculada (sem `busy`); a falha fica em [GoogleState.error]. */
    suspend fun loadGoogleStatus() {
        val session = state.session ?: return
        if (state.google.loading) return
        state = state.copy(google = state.google.copy(loading = true, error = null))
        try {
            val status = api(state.address, null).googleStatus(session.token)
            if (isCurrent(session)) state = state.copy(google = GoogleState(status))
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
            else googleFailed(session, failureMessage(failure.status, failure.code, ApiContext.GOOGLE))
        }
        catch (_: IOException) { googleFailed(session, CONNECTION_FAILED_MESSAGE) }
        catch (_: JSONException) { googleFailed(session, INCOMPATIBLE_RESPONSE_MESSAGE) }
        finally { if (isCurrent(session) && state.google.loading) state = state.copy(google = state.google.copy(loading = false)) }
    }

    private fun googleFailed(session: PatientSession, error: String) {
        if (isCurrent(session)) state = state.copy(google = state.google.copy(loading = false, error = error))
    }

    /** Vincula a conta Google (G2) ao paciente conectado. 409 `google-already-linked` e 503 `google-disabled` têm texto próprio. */
    suspend fun linkGoogle(idToken: String): Boolean {
        var linked = false
        operation(ApiContext.GOOGLE) {
            val session = state.session ?: return@operation
            require(idToken.isNotBlank()) { "O Google não devolveu a identificação da conta. Tente de novo." }
            val api = api(state.address, null)
            api.linkGoogle(session.token, idToken)
            if (!isCurrent(session)) return@operation
            val status = readGoogleStatusQuietly(api, session) ?: GoogleStatus(enabled = true, linked = true, email = null)
            if (isCurrent(session)) {
                state = state.copy(google = GoogleState(status), message = "Conta Google vinculada. Agora você também pode entrar com o Google.")
                linked = true
            }
        }
        return linked
    }

    /** Desvincula a conta Google (G3, idempotente). O acesso por CPF ou e-mail e senha continua. */
    suspend fun unlinkGoogle(): Boolean {
        var unlinked = false
        operation(ApiContext.GOOGLE) {
            val session = state.session ?: return@operation
            val api = api(state.address, null)
            api.unlinkGoogle(session.token)
            if (!isCurrent(session)) return@operation
            val status = readGoogleStatusQuietly(api, session) ?: GoogleStatus(enabled = true, linked = false, email = null)
            if (isCurrent(session)) {
                state = state.copy(google = GoogleState(status), message = "Conta Google desvinculada. Você continua entrando com o CPF ou e-mail e a senha.")
                unlinked = true
            }
        }
        return unlinked
    }

    private suspend fun readGoogleStatusQuietly(api: PatientApi, session: PatientSession): GoogleStatus? {
        try { return api.googleStatus(session.token) }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) { if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message) }
        catch (_: IOException) { Log.w("NORA", "Situação do Google não relida após a alteração.") }
        catch (_: JSONException) { Log.w("NORA", "Situação do Google incompatível após a alteração.") }
        return null
    }

    // ---- Serviços e horários da UBS --------------------------------------------------------------------------

    /**
     * Lê horário de funcionamento, serviços e especialidades da UBS [unitId] (a selecionada por padrão), sem `busy`. A leitura é do próprio
     * paciente e fica por UBS; uma leitura já em andamento para a mesma UBS não gera outra requisição.
     */
    suspend fun loadUnitAbout(unitId: String? = state.selectedUnitId) {
        val session = state.session ?: return
        val id = unitId ?: return
        if (state.units?.any { it.id == id } != true || state.about[id]?.loading == true) return
        setAbout(id) { it.copy(loading = true, error = null) }
        try {
            val profile = api(state.address, null).unitAbout(session.token, id)
            if (isCurrent(session)) setAbout(id) { AboutState(profile) }
        }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
            else aboutFailed(session, id, failureMessage(failure.status, failure.code, ApiContext.ABOUT))
        }
        catch (_: IOException) { aboutFailed(session, id, CONNECTION_FAILED_MESSAGE) }
        catch (_: JSONException) { aboutFailed(session, id, INCOMPATIBLE_RESPONSE_MESSAGE) }
        catch (_: IllegalArgumentException) { aboutFailed(session, id, INCOMPATIBLE_RESPONSE_MESSAGE) }
        finally { if (isCurrent(session) && state.about[id]?.loading == true) setAbout(id) { it.copy(loading = false) } }
    }

    private fun setAbout(unitId: String, change: (AboutState) -> AboutState) {
        if (state.units?.any { it.id == unitId } != true) return
        state = state.copy(about = state.about + (unitId to change(state.about[unitId] ?: AboutState())))
    }

    private fun aboutFailed(session: PatientSession, unitId: String, error: String) {
        // Sem dado novo, nada de dado velho: um horário antigo na tela enganaria mais do que a falha.
        if (isCurrent(session)) setAbout(unitId) { AboutState(error = error) }
    }

    // ---- Push do servidor (FCM) -------------------------------------------------------------------------------

    /** Dá o token FCM do aparelho; `null` quando o Firebase não está configurado. Definido pelo app; os testes o substituem. */
    var pushTokenProvider: (suspend () -> String?)? = null
    /** Quanto o logout espera pela remoção do aparelho no push; os testes encurtam. */
    var pushRemoveTimeoutMs = PUSH_REMOVE_TIMEOUT_MS
    private val pushMutex = Mutex()
    // Sessão e token já registrados (P1) e se o servidor diz que o push está ligado (lido uma vez por sessão).
    private var pushRegistered: Pair<String, String>? = null
    private var pushServerEnabled: Pair<String, Boolean>? = null

    /**
     * Registra o token do aparelho no servidor (P1) depois do login e quando o token muda ([fresh] é o token novo que o serviço do FCM
     * entregou). Sem provedor de token (Firebase desligado) e sem [fresh], não faz nada; com o push desligado no servidor, também não.
     * Falhas são silenciosas: o ciclo de 30 s continua avisando, e a próxima entrada tenta de novo. Nunca usa `busy`.
     */
    suspend fun syncPushToken(fresh: String? = null) {
        val session = state.session ?: return
        val provider = pushTokenProvider
        if (fresh == null && provider == null) return
        pushMutex.withLock {
            if (!isCurrent(session)) return@withLock
            try {
                val pushToken = fresh ?: provider?.invoke() ?: return@withLock
                if (pushToken.isBlank() || pushRegistered == (session.token to pushToken)) return@withLock
                val api = api(state.address, null)
                val enabled = pushServerEnabled?.takeIf { it.first == session.token }?.second
                    ?: api.pushEnabled(session.token).also { pushServerEnabled = session.token to it }
                if (!enabled) return@withLock
                api.registerPushToken(session.token, pushToken)
                if (isCurrent(session)) pushRegistered = session.token to pushToken
            }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (failure: ApiFailure) {
                if (failure.status == 401 && isCurrent(session)) state = signedOut(state, failure.message)
                else Log.w("NORA", "Push não registrado: HTTP ${failure.status}.")
            }
            catch (_: IOException) { Log.w("NORA", "Push não registrado; a próxima entrada tentará novamente.") }
            catch (_: JSONException) { Log.w("NORA", "O serviço retornou uma resposta de push incompatível.") }
        }
    }

    /** P2 no logout: tira o aparelho do push desta conta (idempotente). Espera pouco: sair não pode travar por causa do push. */
    private suspend fun removePushToken(session: PatientSession) {
        withTimeoutOrNull(pushRemoveTimeoutMs) {
            pushMutex.withLock {
                try {
                    val known = pushRegistered?.takeIf { it.first == session.token }?.second
                    // Push desligado no servidor e nenhum registro nesta sessão: não há o que remover.
                    if (known == null && pushServerEnabled?.takeIf { it.first == session.token }?.second == false) return@withLock
                    val pushToken = known ?: pushTokenProvider?.invoke() ?: return@withLock
                    api(state.address, null).removePushToken(session.token, pushToken)
                    pushRegistered = null
                }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (failure: ApiFailure) { Log.w("NORA", "Push não removido no logout: HTTP ${failure.status}.") }
                catch (_: IOException) { Log.w("NORA", "Push não removido no logout; o servidor descarta o token quando o FCM recusar.") }
                catch (_: JSONException) { Log.w("NORA", "O serviço retornou uma resposta de push incompatível.") }
            }
        }
    }

    suspend fun logout() = operation {
        val session = state.session ?: return@operation
        // O aparelho deixa de receber push desta conta antes de a sessão acabar (P2); a falha não impede a saída.
        removePushToken(session)
        try { api(state.address, null).logout(session.token) }
        catch (failure: ApiFailure) { if (failure.status != 401) warnUnconfirmedLogout() }
        catch (_: IOException) { warnUnconfirmedLogout() }
        catch (_: JSONException) { warnUnconfirmedLogout() }
        finally { state = signedOut(state, state.message) }
    }

    private fun warnUnconfirmedLogout() {
        state = state.copy(message = "Você saiu neste dispositivo. Não foi possível confirmar a saída no serviço.")
    }

    /** Nome do dependente pelo qual se age (para as mensagens que falam dele); `null` sem nome conhecido. */
    private fun actingName(acting: String?): String? =
        acting?.let { id -> state.family.links?.firstOrNull { it.id == id }?.dependentName?.takeIf { it.isNotBlank() } }

    /** Ação do paciente: uma por vez, com `busy` visível. Leituras em segundo plano não passam por aqui. */
    private suspend fun operation(context: ApiContext = ApiContext.GENERIC, action: suspend () -> Unit) {
        if (state.busy) return
        state = state.copy(busy = true, message = null)
        val acting = state.actingFor
        val view = viewSwitches
        fun fail(text: String?) { if (view == viewSwitches) state = state.copy(message = text) }
        try { action() }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: ApiFailure) {
            val text = if (context == ApiContext.GENERIC) failure.message else failureMessage(failure.status, failure.code, context, acting != null, actingName(acting))
            // 403 agindo por um dependente: o vínculo pode ter terminado; relê os pedidos para a tela mostrar o estado real.
            if (failure.status == 403 && acting != null && state.session != null) loadFamily()
            if (failure.status == 401) state = signedOut(state, text) else fail(text)
        }
        catch (invalid: IllegalArgumentException) { fail(invalid.message) }
        catch (_: IOException) { fail(CONNECTION_FAILED_MESSAGE) }
        catch (_: JSONException) { fail(INCOMPATIBLE_RESPONSE_MESSAGE) }
        finally { state = state.copy(busy = false) }
    }
}
