package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

/**
 * Login salvo (decisão de 26/11/2026): a duração pedida vai ao servidor, a sessão é guardada pelo `expiresAt` recebido e restaurada ao abrir o
 * app, e todo fim de sessão a apaga. O armazenamento aqui é em memória; a cifra é testada em [SessionStorageTest].
 */
class SessionControllerTest {
    private val now = requireNotNull(parseInstantMillis("2026-11-26T12:00:00Z"))
    private val hour = 3_600_000L
    private val address = "https://nora.example.test"

    private class MemorySessions(var saved: StoredSession? = null) : SessionStorage {
        var clears = 0
        var loads = 0
        var failSave = false
        override fun hasSaved() = saved != null
        override fun load(): StoredSession? { loads++; return saved }
        override fun save(session: StoredSession): Boolean { if (failSave) return false; saved = session; return true }
        override fun clear() { clears++; saved = null }
    }

    private class SessionApi : PatientApi {
        var expiresAt = "2026-11-27T12:00:00Z"
        val loginRequests = mutableListOf<Boolean>()
        val googleRequests = mutableListOf<Boolean>()
        var loginFailure: Throwable? = null
        var meResult: PatientUser? = PatientUser("patient-id", "Maria da Silva", "•••.456.789-••", "maria@example.test")
        var meFailure: Throwable? = null
        var meCalls = 0
        var logoutFailure: Throwable? = null
        var logoutCalls = 0
        var presenceFailure: Throwable? = null
        var units = emptyList<PatientHealthUnit>()
        var unitsCalls = 0
        val user = PatientUser("patient-id", "Maria da Silva")

        override suspend fun login(identifier: String, password: String) = login(identifier, password, false)
        override suspend fun login(identifier: String, password: String, keepSignedIn: Boolean): PatientSession {
            loginRequests += keepSignedIn
            loginFailure?.let { throw it }
            return PatientSession("token-novo", expiresAt, user)
        }
        override suspend fun loginWithGoogle(idToken: String, deviceName: String?, keepSignedIn: Boolean): PatientSession {
            googleRequests += keepSignedIn
            return PatientSession("token-google", expiresAt, user)
        }
        override suspend fun me(token: String): PatientUser? { meCalls++; meFailure?.let { throw it }; return meResult }
        override suspend fun units(token: String): List<PatientHealthUnit> { unitsCalls++; return units }
        override suspend fun changePassword(token: String, currentPassword: String, newPassword: String) { logoutFailure?.let { throw it } }
        override suspend fun logout(token: String) { logoutCalls++; logoutFailure?.let { throw it } }
        override suspend fun presence(token: String, online: Boolean) { presenceFailure?.let { throw it } }
    }

    private fun stored(expiresAt: String = "2026-11-27T12:00:00Z", address: String = this.address, userId: String = "patient-id") =
        StoredSession("token-guardado", expiresAt, userId, "Maria da Silva", address)

    private fun controller(api: SessionApi, sessions: SessionStorage): PatientController =
        PatientController(MemoryAddressStore(address), sessions) { api }.also { it.clock = { now } }

    private fun unauthorized() = ApiFailure(401, failureMessage(401, "unauthorized", ApiContext.SESSION), "unauthorized")

    // ---- Entrar e guardar -----------------------------------------------------------------------------------------

    @Test fun loginWithTheBoxUnticked_asksForTheDefaultAndStoresTheServersExpiry() = runBlocking {
        val api = SessionApi().apply { expiresAt = "2026-11-26T20:00:00Z" } // servidor antigo: 8 h
        val sessions = MemorySessions()
        val controller = controller(api, sessions)
        assertFalse("desmarcada por padrão", controller.state.keepSignedIn)
        controller.login("maria@example.test", "senha")
        assertEquals(listOf(false), api.loginRequests)
        val saved = requireNotNull(sessions.saved)
        assertEquals("o app guarda o prazo que o servidor deu, nunca um prazo próprio", "2026-11-26T20:00:00Z", saved.expiresAt)
        assertEquals(StoredSession("token-novo", "2026-11-26T20:00:00Z", "patient-id", "Maria da Silva", address), saved)
        assertEquals("token-novo", controller.state.session?.token)
        assertNull(controller.state.message)
    }

    @Test fun loginWithTheBoxTickedAsksForThirtyDaysAndGoogleUsesTheSameChoice() = runBlocking {
        val api = SessionApi()
        val sessions = MemorySessions()
        val controller = controller(api, sessions)
        controller.setKeepSignedIn(true)
        assertTrue(controller.state.keepSignedIn)
        controller.login("maria@example.test", "senha")
        assertEquals(listOf(true), api.loginRequests)

        val other = controller(api, MemorySessions())
        other.setKeepSignedIn(true)
        assertTrue(other.loginWithGoogle("id-token-de-teste"))
        assertEquals(listOf(true), api.googleRequests)
        val plain = controller(api, MemorySessions())
        assertTrue(plain.loginWithGoogle("id-token-de-teste"))
        assertEquals(listOf(true, false), api.googleRequests)
    }

    @Test fun theChoiceSurvivesAWrongPasswordButNotALogoutAndCannotChangeWhileSignedIn() = runBlocking {
        val api = SessionApi()
        val controller = controller(api, MemorySessions())
        controller.setKeepSignedIn(true)
        api.loginFailure = ApiFailure(401, failureMessage(401, "unauthorized", ApiContext.LOGIN), "unauthorized")
        controller.login("maria@example.test", "errada")
        assertNull(controller.state.session)
        assertTrue("a caixa não desmarca depois de errar a senha", controller.state.keepSignedIn)
        api.loginFailure = null
        controller.login("maria@example.test", "certa")
        controller.setKeepSignedIn(false)
        assertTrue("sem efeito com a sessão aberta", controller.state.keepSignedIn)
        controller.logout()
        assertFalse("volta a desmarcada ao sair", controller.state.keepSignedIn)
    }

    @Test fun aDiskThatRefusesTheSessionDoesNotStopTheLoginButTellsThePerson() = runBlocking {
        val sessions = MemorySessions().apply { failSave = true }
        val controller = controller(SessionApi(), sessions)
        controller.login("maria@example.test", "senha")
        assertNotNull(controller.state.session)
        assertEquals(SESSION_NOT_SAVED_MESSAGE, controller.state.message)
        assertNull(sessions.saved)
    }

    // ---- Restaurar ao abrir ---------------------------------------------------------------------------------------

    @Test fun withNothingSavedThereIsNothingToRestoreAndTheLoginShowsAtOnce() = runBlocking {
        val api = SessionApi()
        val controller = controller(api, MemorySessions())
        assertFalse(controller.state.restoring)
        controller.restoreSession()
        assertNull(controller.state.session)
        assertFalse(controller.state.restoring)
        assertEquals(0, api.meCalls)
    }

    @Test fun aSavedSessionShowsRestoringFromTheStartThenEntersConfirmsAndLoadsTheUnits() = runBlocking {
        val unit = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", -23.5, -46.6)
        val api = SessionApi().apply { units = listOf(unit) }
        val sessions = MemorySessions(stored())
        val controller = controller(api, sessions)
        assertTrue("já abre mostrando 'Restaurando a sua sessão…'", controller.state.restoring)
        assertNull(controller.state.session)
        controller.restoreSession()
        val session = requireNotNull(controller.state.session)
        assertEquals("token-guardado", session.token)
        assertFalse(controller.state.restoring)
        assertEquals(1, api.meCalls)
        assertEquals("a identidade vem de novo do servidor", "•••.456.789-••", session.user.cpfMasked)
        assertEquals("maria@example.test", session.user.email)
        assertEquals(listOf(unit), controller.state.units)
        assertEquals("a sessão continua guardada", 0, sessions.clears)
        assertNull(controller.state.message)
    }

    @Test fun restoringTwiceReadsTheDiskOnceAndConfirmsOnce() = runBlocking {
        val api = SessionApi()
        val sessions = MemorySessions(stored())
        val controller = controller(api, sessions)
        controller.restoreSession()
        controller.restoreSession()
        assertEquals(1, sessions.loads)
        assertEquals(1, api.meCalls)
    }

    @Test fun anExpiredSessionIsErasedWithoutTouchingTheNetwork() = runBlocking {
        val api = SessionApi()
        val sessions = MemorySessions(stored(expiresAt = "2026-11-26T12:00:00Z")) // vence exatamente agora
        val controller = controller(api, sessions)
        controller.restoreSession()
        assertNull(controller.state.session)
        assertFalse(controller.state.restoring)
        assertNull(sessions.saved)
        assertEquals(0, api.meCalls)
        assertEquals(0, api.unitsCalls)
        assertEquals(SESSION_EXPIRED_MESSAGE, controller.state.message)
    }

    @Test fun anUnreadableExpiryOrAnotherServiceAddressIsErasedWithoutTouchingTheNetwork() = runBlocking {
        for (saved in listOf(stored(expiresAt = "amanhã"), stored(address = "https://outro-servidor.example.test"))) {
            val api = SessionApi()
            val sessions = MemorySessions(saved)
            val controller = controller(api, sessions)
            controller.restoreSession()
            assertNull(controller.state.session)
            assertNull("o token nunca vai a outro servidor", sessions.saved)
            assertEquals(0, api.meCalls)
        }
    }

    @Test fun anUnreadableDiskGoesStraightToTheLogin() = runBlocking {
        // `hasSaved` diz que há algo, mas a leitura (chave perdida ou dado corrompido, já apagado pelo armazenamento) devolve nulo.
        val sessions = object : SessionStorage {
            override fun hasSaved() = true
            override fun load(): StoredSession? = null
            override fun save(session: StoredSession) = true
            override fun clear() = Unit
        }
        val api = SessionApi()
        val controller = controller(api, sessions)
        assertTrue(controller.state.restoring)
        controller.restoreSession()
        assertNull(controller.state.session)
        assertFalse(controller.state.restoring)
        assertNull("sem pop-up: a pessoa só vê a entrada", controller.state.message)
        assertEquals(0, api.meCalls)
    }

    @Test fun aRejectedSessionErasesEverythingAndBackToTheLoginWithAClearMessage() = runBlocking {
        val api = SessionApi().apply { meFailure = unauthorized() }
        val sessions = MemorySessions(stored())
        val controller = controller(api, sessions)
        controller.restoreSession()
        assertNull(controller.state.session)
        assertFalse(controller.state.restoring)
        assertNull(sessions.saved)
        assertEquals("Sua sessão terminou. Entre novamente.", controller.state.message)
        assertEquals(0, api.unitsCalls)
    }

    @Test fun aNetworkFailureKeepsTheSessionShowsTheConnectionStateAndTheNextCycleConfirmsIt() = runBlocking {
        val api = SessionApi().apply { meFailure = IOException("sem rede") }
        val sessions = MemorySessions(stored())
        val controller = controller(api, sessions)
        controller.restoreSession()
        val session = requireNotNull(controller.state.session)
        assertEquals("token-guardado", session.token)
        assertNotNull("não apagou", sessions.saved)
        assertEquals(0, sessions.clears)
        assertFalse(controller.state.restoring)
        assertEquals(CONNECTION_FAILED_MESSAGE, controller.state.message)

        // Ciclo de 30 s do serviço de conexão: ainda sem rede, continua em silêncio.
        controller.dismissMessage()
        val generation = controller.beginPresence()
        controller.confirmRestoredSession(session, generation)
        assertEquals(2, api.meCalls)
        assertNull(controller.state.message)
        assertNotNull(sessions.saved)

        // A rede volta: confirma, atualiza a identidade e para de perguntar.
        api.meFailure = null
        controller.confirmRestoredSession(session, generation)
        assertEquals(3, api.meCalls)
        assertEquals("maria@example.test", controller.state.session?.user?.email)
        controller.confirmRestoredSession(session, generation)
        assertEquals("já confirmada: não pergunta mais", 3, api.meCalls)
    }

    @Test fun aServerThatIsWakingUpOrBrokenDoesNotEraseTheSession() = runBlocking {
        for (status in listOf(500, 502, 503)) {
            val api = SessionApi().apply { meFailure = ApiFailure(status, failureMessage(status, null, ApiContext.SESSION)) }
            val sessions = MemorySessions(stored())
            val controller = controller(api, sessions)
            controller.restoreSession()
            assertNotNull("HTTP $status", controller.state.session)
            assertNotNull(sessions.saved)
        }
    }

    @Test fun anIncompatibleAnswerDoesNotEraseTheSessionEither() = runBlocking {
        val api = SessionApi().apply { meFailure = org.json.JSONException("resposta estranha") }
        val sessions = MemorySessions(stored())
        val controller = controller(api, sessions)
        controller.restoreSession()
        assertNotNull(controller.state.session)
        assertNotNull(sessions.saved)
    }

    @Test fun aRejectionInTheCycleAfterANetworkFailureErasesTheSession() = runBlocking {
        val api = SessionApi().apply { meFailure = IOException("sem rede") }
        val sessions = MemorySessions(stored())
        val controller = controller(api, sessions)
        controller.restoreSession()
        val session = requireNotNull(controller.state.session)
        api.meFailure = unauthorized()
        controller.confirmRestoredSession(session, controller.beginPresence())
        assertNull(controller.state.session)
        assertNull(sessions.saved)
        assertEquals("Sua sessão terminou. Entre novamente.", controller.state.message)
    }

    @Test fun aSessionThatTheServerAnswersForAnotherAccountIsDropped() = runBlocking {
        val api = SessionApi().apply { meResult = PatientUser("outra-pessoa", "Outra Pessoa") }
        val sessions = MemorySessions(stored())
        val controller = controller(api, sessions)
        controller.restoreSession()
        assertNull(controller.state.session)
        assertNull(sessions.saved)
        assertEquals(SESSION_ENDED_MESSAGE, controller.state.message)
    }

    @Test fun aNotificationTapWaitsWhileTheSessionIsBeingRestoredInsteadOfBeingDropped() {
        val restoring = PatientState(restoring = true)
        assertEquals(FlashOpen.WAIT, flashOpenDecision("unit-a", null, restoring))
        assertEquals(FlashOpen.WAIT, flashOpenDecision("unit-a", "dep-1", restoring))
        assertEquals(FlashOpen.DROP, flashOpenDecision("unit-a", null, PatientState()))
    }

    // ---- Fim da sessão apaga o disco ------------------------------------------------------------------------------

    @Test fun loggingOutErasesTheSavedSessionEvenWhenTheServerCannotBeReached() = runBlocking {
        val api = SessionApi()
        val sessions = MemorySessions()
        val controller = controller(api, sessions)
        controller.login("maria@example.test", "senha")
        assertNotNull(sessions.saved)
        api.logoutFailure = IOException("sem rede")
        controller.logout()
        assertNull(controller.state.session)
        assertNull(sessions.saved)
    }

    @Test fun loggingOutErasesTheDiskBeforeAnyNetworkCall() = runBlocking {
        val sessions = MemorySessions()
        var savedWhenTheServerWasCalled: StoredSession? = StoredSession("x", "x", "x", "x", "x")
        val api = object : PatientApi by SessionApi() {
            override suspend fun logout(token: String) { savedWhenTheServerWasCalled = sessions.saved }
        }
        val controller = PatientController(MemoryAddressStore(address), sessions) { api }
        controller.login("maria@example.test", "senha")
        assertNotNull(sessions.saved)
        controller.logout()
        assertNull("o token já tinha saído do disco quando o servidor foi chamado", savedWhenTheServerWasCalled)
    }

    @Test fun anyDefinitive401ErasesTheSavedSession() = runBlocking {
        val api = SessionApi()
        val sessions = MemorySessions()
        val controller = controller(api, sessions)
        controller.login("maria@example.test", "senha")
        assertNotNull(sessions.saved)
        // Conta desativada ou sessão revogada: o servidor responde 401 na presença, que é o ciclo de 30 s.
        api.presenceFailure = unauthorized()
        val session = requireNotNull(controller.state.session)
        controller.publishPresence(session, controller.beginPresence(), true)
        assertNull(controller.state.session)
        assertNull(sessions.saved)
        assertEquals("Sua sessão terminou. Entre novamente.", controller.state.message)
    }

    @Test fun a401WhenChangingThePasswordErasesTheSavedSession() = runBlocking {
        val api = SessionApi()
        val sessions = MemorySessions()
        val controller = controller(api, sessions)
        controller.login("maria@example.test", "senha")
        api.logoutFailure = unauthorized() // a mesma falha serve a changePassword do dublê
        try { controller.changePassword("senha", "outra-senha"); fail("deveria recusar") } catch (_: ApiFailure) { }
        assertNull(controller.state.session)
        assertNull(sessions.saved)
    }

    @Test fun removingTheAppFromRecentsKeepsTheSavedSessionForTheNextOpening() = runBlocking {
        val api = SessionApi()
        val sessions = MemorySessions()
        val controller = controller(api, sessions)
        controller.login("maria@example.test", "senha")
        controller.closeLocalSession()
        assertNull("a sessão em memória acaba", controller.state.session)
        assertNotNull("a guardada continua", sessions.saved)

        // Próxima abertura: outro controlador, o mesmo disco.
        val reopened = controller(api, sessions)
        reopened.restoreSession()
        assertEquals("token-novo", reopened.state.session?.token)
    }

    @Test fun aFailedLoginDoesNotTouchTheDiskWhenNothingWasSaved() = runBlocking {
        val api = SessionApi().apply { loginFailure = IOException("sem rede") }
        val sessions = MemorySessions()
        val controller = controller(api, sessions)
        controller.login("maria@example.test", "senha")
        assertNull(controller.state.session)
        assertNull(sessions.saved)
    }
}
