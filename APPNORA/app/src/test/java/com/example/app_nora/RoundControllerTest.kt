package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

/** Entrar com Google, push do servidor, horários da UBS e conflito entre UBS no controlador, com API falsa. */
class RoundControllerTest {
    private val now = requireNotNull(parseInstantMillis("2026-11-16T12:00:00Z"))
    private val day = 24 * 3_600_000L
    private val pending = "pending"

    private suspend fun ready(api: FamilyFakeApi, vararg units: PatientHealthUnit): PatientController {
        api.units = units.toList()
        val controller = PatientController(MemoryAddressStore()) { api }
        controller.clock = { now }
        controller.login("paciente@example.test", "test-only-input")
        return controller
    }

    private fun offer(id: String) = UnitOffer(id, "Cardiologia", "Dra. Ana", formatInstantUtc(now + day), formatInstantUtc(now + day + 1_800_000),
        formatInstantUtc(now + 600_000), pending)

    private suspend fun withDependent(api: FamilyFakeApi): PatientController {
        api.links = mutableListOf(link("dep-1", "approved", "unit-b", "UBS B"))
        val controller = ready(api, unitA, unitB)
        controller.loadFamily()
        return controller
    }

    // ---- Entrar com Google --------------------------------------------------------------------------------------

    @Test fun googleLoginOpensAPatientSessionAndLoadsTheUnits() = runBlocking {
        val api = FamilyFakeApi().also { it.units = listOf(unitA) }
        val controller = PatientController(MemoryAddressStore()) { api }
        assertTrue(controller.loginWithGoogle("id-token-de-teste"))
        assertEquals("token-google", controller.state.session?.token)
        assertEquals(listOf("id-token-de-teste"), api.googleIdTokens)
        assertFalse(controller.state.busy)
        assertEquals(listOf(unitA), controller.state.units)
        assertNull(controller.state.message)
    }

    @Test fun googleLoginRefusedShowsTheGenericMessageAndKeepsNoSession() = runBlocking {
        val api = FamilyFakeApi()
        api.googleLoginFailure = failure(401, "unauthorized", ApiContext.GOOGLE_LOGIN)
        val controller = PatientController(MemoryAddressStore()) { api }
        assertFalse(controller.loginWithGoogle("id-token-de-teste"))
        assertNull(controller.state.session)
        assertEquals(failureMessage(401, "unauthorized", ApiContext.GOOGLE_LOGIN), controller.state.message)
        assertFalse(controller.state.busy)
    }

    @Test fun googleDisabledOnTheServerIsExplainedAtLogin() = runBlocking {
        val api = FamilyFakeApi()
        api.googleLoginFailure = failure(503, GOOGLE_DISABLED, ApiContext.GOOGLE_LOGIN)
        val controller = PatientController(MemoryAddressStore()) { api }
        assertFalse(controller.loginWithGoogle("x"))
        assertEquals(GOOGLE_DISABLED_MESSAGE, controller.state.message)
    }

    @Test fun aBlankGoogleTokenNeverReachesTheServer() = runBlocking {
        val api = FamilyFakeApi()
        val controller = PatientController(MemoryAddressStore()) { api }
        assertFalse(controller.loginWithGoogle(" "))
        assertTrue(api.calls.isEmpty())
        assertNotNull(controller.state.message)
    }

    @Test fun googleStatusLinkAndUnlinkUpdateTheStateAndExplainTheResult() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.loadGoogleStatus()
        assertEquals(GoogleStatus(true, false, null), controller.state.google.status)
        assertTrue(controller.linkGoogle("id-token-de-teste"))
        assertEquals(GoogleStatus(true, true, "paciente@gmail.example"), controller.state.google.status)
        assertTrue(controller.state.message.orEmpty().startsWith("Conta Google vinculada"))
        assertTrue(controller.unlinkGoogle())
        assertEquals(false, controller.state.google.status?.linked)
        assertTrue(controller.state.message.orEmpty().contains("CPF ou e-mail e a senha"))
        assertFalse(controller.state.busy)
    }

    @Test fun linkingAGoogleAccountOfAnotherUserShowsTheConflict() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.loadGoogleStatus()
        api.googleFailure = failure(409, GOOGLE_ALREADY_LINKED, ApiContext.GOOGLE)
        assertFalse(controller.linkGoogle("id-token-de-teste"))
        assertTrue(controller.state.message.orEmpty().contains("outra conta do NORA"))
        assertEquals("nada muda no estado do Google", false, controller.state.google.status?.linked)
    }

    @Test fun aGoogleStatusFailureIsKeptForTheScreenToRetry() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        api.googleFailure = IOException("sem rede")
        controller.loadGoogleStatus()
        assertEquals(CONNECTION_FAILED_MESSAGE, controller.state.google.error)
        assertNull(controller.state.google.status)
        assertFalse(controller.state.google.loading)
        api.googleFailure = null
        controller.loadGoogleStatus()
        assertNull(controller.state.google.error); assertNotNull(controller.state.google.status)
    }

    @Test fun anExpiredSessionOnTheGoogleStatusSignsOut() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        api.googleFailure = failure(401, "unauthorized", ApiContext.GOOGLE)
        controller.loadGoogleStatus()
        assertNull(controller.state.session)
        assertEquals(GoogleState(), controller.state.google)
    }

    @Test fun showMessageNeverReplacesTheMessageOfAnActionInProgress() = runBlocking {
        val controller = PatientController(MemoryAddressStore()) { FamilyFakeApi() }
        controller.showMessage("O Google não abriu.")
        assertEquals("O Google não abriu.", controller.state.message)
        controller.showMessage(null)
        assertNull(controller.state.message)
    }

    // ---- Push -----------------------------------------------------------------------------------------------

    @Test fun pushIsRegisteredOnceAfterLoginAndAgainOnlyWhenTheTokenChanges() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.pushTokenProvider = { "fcm-1" }
        controller.syncPushToken()
        controller.syncPushToken()
        assertEquals(listOf("own.pushRegister fcm-1"), api.callsOf("own.pushRegister"))
        assertEquals("a pergunta ao servidor sobre o push é feita uma vez por sessão", 1, api.callsOf("own.pushEnabled").size)
        // O FCM entregou um token novo.
        controller.syncPushToken("fcm-2")
        assertEquals(listOf("own.pushRegister fcm-1", "own.pushRegister fcm-2"), api.callsOf("own.pushRegister"))
        controller.syncPushToken("fcm-2")
        assertEquals(2, api.callsOf("own.pushRegister").size)
    }

    @Test fun withoutAProviderOrASessionNothingIsSent() = runBlocking {
        val api = FamilyFakeApi()
        val controller = PatientController(MemoryAddressStore()) { api }
        controller.syncPushToken("fcm-1")
        assertTrue("sem sessão não há para quem registrar", api.calls.isEmpty())
        controller.login("paciente@example.test", "test-only-input")
        api.calls.clear()
        controller.syncPushToken()
        assertTrue("sem Firebase (sem provedor) o push fica desligado", api.calls.isEmpty())
    }

    @Test fun aServerWithPushOffRegistersNothing() = runBlocking {
        val api = FamilyFakeApi().also { it.pushEnabledValue = false }
        val controller = ready(api, unitA)
        controller.pushTokenProvider = { "fcm-1" }
        controller.syncPushToken()
        assertTrue(api.callsOf("own.pushRegister").isEmpty())
        assertEquals(1, api.callsOf("own.pushEnabled").size)
    }

    @Test fun aPushRegistrationFailureIsSilentAndRetriedWithTheNextToken() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.pushTokenProvider = { "fcm-1" }
        api.pushFailure = IOException("sem rede")
        controller.syncPushToken()
        assertNull("falha de rede não vira mensagem", controller.state.message)
        assertFalse(controller.state.busy)
        api.pushFailure = null
        controller.syncPushToken()
        assertEquals(2, api.callsOf("own.pushRegister").size)
    }

    @Test fun anExpiredSessionOnPushRegistrationSignsOut() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.pushTokenProvider = { "fcm-1" }
        api.pushFailure = failure(401, "unauthorized", ApiContext.PUSH)
        controller.syncPushToken()
        assertNull(controller.state.session)
    }

    @Test fun logoutRemovesTheRegisteredTokenBeforeEndingTheSession() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.pushTokenProvider = { "fcm-1" }
        controller.syncPushToken()
        api.calls.clear()
        controller.logout()
        assertEquals(listOf("own.pushRemove fcm-1"), api.callsOf("own.pushRemove"))
        assertNull(controller.state.session)
        // Sair com a sessão ainda aberta no servidor: a remoção vem antes da saída.
        assertEquals(listOf("fcm-1"), api.callsOf("own.pushRemove").map { it.removePrefix("own.pushRemove ") })
    }

    @Test fun logoutStillHappensWhenRemovingThePushTokenFails() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.pushTokenProvider = { "fcm-1" }
        controller.syncPushToken()
        api.pushRemoveFailure = IOException("sem rede")
        controller.logout()
        assertNull(controller.state.session)
        assertFalse(controller.state.busy)
    }

    @Test fun logoutDoesNotHangWhenTheServerNeverAnswersTheRemoval() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.pushTokenProvider = { "fcm-1" }
        controller.syncPushToken()
        api.pushRemoveGate = CompletableDeferred()
        controller.pushRemoveTimeoutMs = 200
        controller.logout()
        assertNull("o logout espera pouco pelo push e termina", controller.state.session)
    }

    @Test fun logoutWithoutPushConfiguredMakesNoPushCall() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        api.calls.clear()
        controller.logout()
        assertTrue(api.callsOf("own.push").isEmpty())
        assertNull(controller.state.session)
    }

    @Test fun logoutRightAfterLoginStillRemovesTheDeviceThroughTheProvider() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        controller.pushTokenProvider = { "fcm-9" }
        controller.logout()
        assertEquals(listOf("own.pushRemove fcm-9"), api.callsOf("own.pushRemove"))
    }

    // ---- Serviços e horários da UBS ---------------------------------------------------------------------------

    private val centro = UnitProfile("unit-a", true, listOf(OpeningInterval(1, "08:00", "12:00")), "Vacinação", listOf(Specialty("s1", "Clínica Geral")), "2026-11-16T10:00:00Z")

    @Test fun theAboutOfEachLinkedUnitIsReadOnceAndKeptPerUnit() = runBlocking {
        val api = FamilyFakeApi().also { it.aboutByUnit["unit-a"] = centro }
        val controller = ready(api, unitA, unitB)
        controller.loadUnitAbout("unit-a")
        controller.loadUnitAbout("unit-b")
        assertEquals(centro, controller.state.about["unit-a"]?.profile)
        assertFalse("UBS sem perfil informado volta como não informada", controller.state.about["unit-b"]?.profile?.informed == true)
        assertEquals(listOf("own.about unit-a", "own.about unit-b"), api.callsOf("own.about"))
        assertFalse(controller.state.about["unit-a"]?.loading == true)
        assertTrue("a leitura não usa o busy das ações", !controller.state.busy)
    }

    @Test fun anAboutReadAlreadyRunningForTheSameUnitDoesNotRepeatTheRequest() = runBlocking {
        val api = FamilyFakeApi().also { it.aboutByUnit["unit-a"] = centro; it.aboutGate = CompletableDeferred() }
        val controller = ready(api, unitA)
        val first = launch { controller.loadUnitAbout("unit-a") }
        yield()
        assertTrue(controller.state.about["unit-a"]?.loading == true)
        controller.loadUnitAbout("unit-a")
        assertEquals(1, api.callsOf("own.about").size)
        api.aboutGate?.complete(Unit)
        first.join()
        assertEquals(centro, controller.state.about["unit-a"]?.profile)
    }

    @Test fun anAboutFailureShowsTheMessageAndNeverLeavesAnOldScheduleOnScreen() = runBlocking {
        val api = FamilyFakeApi().also { it.aboutByUnit["unit-a"] = centro }
        val controller = ready(api, unitA)
        controller.loadUnitAbout("unit-a")
        api.aboutFailure = failure(503, "service-unavailable", ApiContext.ABOUT)
        controller.loadUnitAbout("unit-a")
        val about = requireNotNull(controller.state.about["unit-a"])
        assertNull("sem dado novo, nada de dado velho", about.profile)
        assertTrue(about.error.orEmpty().contains("UBS")); assertFalse(about.loading)
        api.aboutFailure = null
        controller.loadUnitAbout("unit-a")
        assertEquals(centro, controller.state.about["unit-a"]?.profile); assertNull(controller.state.about["unit-a"]?.error)
    }

    @Test fun aboutOfAnUnlinkedUnitIsNeverReadAndLeavesWithTheUnit() = runBlocking {
        val api = FamilyFakeApi().also { it.aboutByUnit["unit-a"] = centro }
        val controller = ready(api, unitA, unitB)
        controller.loadUnitAbout("unit-zz")
        assertTrue(api.callsOf("own.about").isEmpty())
        controller.loadUnitAbout("unit-a")
        api.units = listOf(unitB)
        controller.loadUnits()
        assertNull("a UBS removida leva o perfil dela", controller.state.about["unit-a"])
    }

    @Test fun anExpiredSessionOnTheAboutReadSignsOut() = runBlocking {
        val api = FamilyFakeApi()
        val controller = ready(api, unitA)
        api.aboutFailure = failure(401, "unauthorized", ApiContext.ABOUT)
        controller.loadUnitAbout("unit-a")
        assertNull(controller.state.session)
        assertTrue(controller.state.about.isEmpty())
    }

    // ---- Conflito de horário entre UBS ----------------------------------------------------------------------

    private fun slot() = AvailableSlot(formatInstantUtc(now + 2 * day), formatInstantUtc(now + 2 * day + 1_800_000), "prof-1", "Ana", "spec-1", "Cardiologia")

    @Test fun bookingForYourselfWithAnAppointmentInAnotherUnitSaysSo() = runBlocking {
        val api = FamilyFakeApi().also { it.ownSlots = listOf(slot()) }
        val controller = ready(api, unitA)
        controller.loadAvailability("spec-1", 0)
        api.ownFailure = failure(409, OTHER_UNIT_OVERLAP, ApiContext.BOOKING)
        controller.bookAppointment(slot())
        assertEquals("Você já tem consulta neste horário em outra UBS.", controller.state.message)
        assertNull(controller.state.lastBooked)
    }

    @Test fun bookingForADependentNamesThePerson() = runBlocking {
        val api = FamilyFakeApi().also { it.depSlots = listOf(slot()) }
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        controller.loadAvailability("spec-1", 0)
        api.depFailure = failure(409, OTHER_UNIT_OVERLAP, ApiContext.BOOKING)
        controller.bookAppointment(slot())
        assertEquals("Maria Souza já tem consulta neste horário em outra UBS.", controller.state.message)
    }

    @Test fun acceptingAnOfferWithOverlapKeepsItPendingAndSaysToDeclineIfTheySwapCannot() = runBlocking {
        val api = FamilyFakeApi().also { it.ownOffers["unit-a"] = listOf(offer("off-1")) }
        val controller = ready(api, unitA)
        api.ownFailure = failure(409, OTHER_UNIT_OVERLAP, ApiContext.OFFER_RESPOND)
        controller.respondUnitOffer("off-1", true)
        assertEquals("Você já tem consulta neste horário em outra UBS. Recuse a oferta se não puder trocar.", controller.state.message)
        assertEquals("a oferta continua na lista, pendente", pending, controller.state.unitOffers?.single { it.id == "off-1" }?.status)
        assertFalse(controller.state.busy)
        // Recusar continua possível.
        api.ownFailure = null
        controller.respondUnitOffer("off-1", false)
        assertEquals("Oferta recusada. Você continua na fila, no mesmo lugar.", controller.state.message)
    }

    @Test fun acceptingTheOfferOfADependentWithOverlapNamesThePersonAndKeepsItPending() = runBlocking {
        val api = FamilyFakeApi().also { it.depOffers["dep-1"] = listOf(offer("off-d")) }
        val controller = withDependent(api)
        controller.switchActing("dep-1")
        api.depFailure = failure(409, OTHER_UNIT_OVERLAP, ApiContext.OFFER_RESPOND)
        controller.respondUnitOffer("off-d", true)
        assertEquals("Maria Souza já tem consulta neste horário em outra UBS. Recuse a oferta se não puder trocar.", controller.state.message)
        assertEquals(pending, controller.state.effective().unitOffers?.single { it.id == "off-d" }?.status)
    }
}
