package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.Assert.*
import org.junit.Test

/** Regras puras da rodada: Google, push, horários da UBS, passos de progresso e o botão "Remover UBS". */
class RoundLogicTest {
    private val unit = "11111111-2222-3333-4444-555555555555"
    private val dependent = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    private val appointment = "99999999-8888-7777-6666-555555555555"

    // ---- Google ---------------------------------------------------------------------------------------------

    @Test fun theLoginButtonDependsOnlyOnTheBuildClientId() {
        assertFalse(googleLoginAvailable(""))
        assertFalse(googleLoginAvailable("   "))
        assertTrue(googleLoginAvailable("123-abc.apps.googleusercontent.com"))
    }

    @Test fun theSettingsButtonsNeedTheClientIdAndTheServerEnabled() {
        val id = "123-abc.apps.googleusercontent.com"
        assertFalse("sem leitura do servidor nada aparece", googleSettingsAvailable(id, null))
        assertFalse(googleSettingsAvailable(id, GoogleStatus(enabled = false, linked = false, email = null)))
        assertFalse("sem client ID no build o servidor ligado não basta", googleSettingsAvailable("", GoogleStatus(true, false, null)))
        assertTrue(googleSettingsAvailable(id, GoogleStatus(true, false, null)))
        assertTrue(googleSettingsAvailable(id, GoogleStatus(true, true, "a@gmail.example")))
    }

    // ---- Push -----------------------------------------------------------------------------------------------

    private fun data(vararg pairs: Pair<String, String?>): (String) -> String? = pairs.toMap().let { map -> { key -> map[key] } }

    @Test fun everyEventTypeParsesAndOpensTheRightScreen() {
        val offer = parsePush(data(PUSH_TYPE to "lightning-offer", PUSH_UNIT_ID to unit, PUSH_DEPENDENT_ID to dependent))
        assertEquals(PushTarget(PushType.LIGHTNING_OFFER, unit, dependent, null), offer)
        assertEquals(OpenRequest(PushScreen.FLASH, unit, dependent), offer?.toOpenRequest())
        val presence = parsePush(data(PUSH_TYPE to "presence-window", PUSH_UNIT_ID to unit, PUSH_APPOINTMENT_ID to appointment))
        assertEquals(OpenRequest(PushScreen.APPOINTMENTS, unit, null), presence?.toOpenRequest())
        assertEquals(appointment, presence?.appointmentId)
        assertEquals(OpenRequest(PushScreen.NOTICES), parsePush(data(PUSH_TYPE to "announcement", PUSH_UNIT_ID to unit))?.toOpenRequest())
        assertEquals(OpenRequest(PushScreen.FAMILY), parsePush(data(PUSH_TYPE to "family-decision", PUSH_DEPENDENT_ID to dependent))?.toOpenRequest())
    }

    @Test fun anUnknownTypeOrMissingUnitIsDropped() {
        assertNull(parsePush(data()))
        assertNull(parsePush(data(PUSH_TYPE to "promocao")))
        assertNull(parsePush(data(PUSH_TYPE to "LIGHTNING-OFFER", PUSH_UNIT_ID to unit)))
        assertNull("oferta sem UBS não tem tela para abrir", parsePush(data(PUSH_TYPE to "lightning-offer")))
        assertNull(parsePush(data(PUSH_TYPE to "presence-window", PUSH_UNIT_ID to "nao-e-guid")))
    }

    @Test fun idsThatAreNotGuidsAreDiscardedAndNeverReachAnIntent() {
        val target = parsePush(data(PUSH_TYPE to "announcement", PUSH_UNIT_ID to "<script>", PUSH_DEPENDENT_ID to "1 OR 1=1", PUSH_APPOINTMENT_ID to ""))
        assertEquals(PushTarget(PushType.ANNOUNCEMENT, null, null, null), target)
        val offer = parsePush(data(PUSH_TYPE to "lightning-offer", PUSH_UNIT_ID to unit, PUSH_DEPENDENT_ID to "../../etc"))
        assertNull("o id de dependente inválido vira oferta própria, nunca uma troca de pessoa", offer?.dependentId)
    }

    @Test fun theTextIsGenericAndCarriesNoPersonalData() {
        for (type in PushType.entries) {
            val (title, text) = pushText(type)
            assertTrue(title.isNotBlank() && text.endsWith("Abra o NORA."))
            for (forbidden in listOf("CPF", "Dr.", "Dra.", "Cardiologia", "UBS ")) assertFalse("$type: $text", (title + text).contains(forbidden))
        }
        assertEquals("Há uma nova oportunidade de consulta para você. Abra o NORA.", pushText(PushType.LIGHTNING_OFFER).second)
    }

    @Test fun offerAndAnnouncementFollowTheirExistingPreferencesAndTheOthersAreAlwaysShown() {
        assertFalse(pushAllowed(PushType.LIGHTNING_OFFER, flashEnabled = false, announcementEnabled = true))
        assertTrue(pushAllowed(PushType.LIGHTNING_OFFER, flashEnabled = true, announcementEnabled = false))
        assertFalse(pushAllowed(PushType.ANNOUNCEMENT, flashEnabled = true, announcementEnabled = false))
        assertTrue(pushAllowed(PushType.PRESENCE_WINDOW, flashEnabled = false, announcementEnabled = false))
        assertTrue(pushAllowed(PushType.FAMILY_DECISION, flashEnabled = false, announcementEnabled = false))
    }

    // ---- Horários da UBS --------------------------------------------------------------------------------------

    private fun profile(informed: Boolean = true, hours: List<OpeningInterval> = emptyList(), services: String? = null,
        specialties: List<Specialty> = emptyList()) = UnitProfile(unit, informed, hours, services, specialties, null)

    @Test fun sundayIsZeroAndEachDayHasUpToTwoIntervalsOrIsClosed() {
        val days = aboutDays(profile(hours = listOf(
            OpeningInterval(1, "13:00", "17:00"), OpeningInterval(1, "08:00", "12:00"), OpeningInterval(0, "08:00", "12:00"), OpeningInterval(6, "09:00", "11:30"))))
        assertEquals(7, days.size)
        assertEquals(listOf("Domingo", "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado"), days.map { it.label })
        assertEquals("08:00 – 12:00", days[0].text)
        assertEquals("dois intervalos na ordem do dia", "08:00 – 12:00 · 13:00 – 17:00", days[1].text)
        assertEquals("Fechado", days[2].text)
        assertEquals("Fechado", days[5].text)
        assertEquals("09:00 – 11:30", days[6].text)
    }

    @Test fun notInformedListsNoDaysAndAClosedUnitIsDifferentFromIt() {
        assertTrue("não informado não lista dias", aboutDays(profile(informed = false)).isEmpty())
        val closed = aboutDays(profile(informed = true, hours = emptyList()))
        assertEquals(7, closed.size)
        assertTrue("informado e sem intervalos é fechado todos os dias", closed.all { it.text == "Fechado" })
    }

    @Test fun secondsAreDroppedAndAnOutOfRangeWeekdayIsIgnored() {
        val days = aboutDays(profile(hours = listOf(OpeningInterval(3, "08:00:00", "12:00:00"), OpeningInterval(9, "08:00", "12:00"), OpeningInterval(-1, "08:00", "12:00"))))
        assertEquals("08:00 – 12:00", days[3].text)
        assertEquals(1, days.count { it.text != "Fechado" })
        assertEquals("08:00", clockOnly("08:00:00")); assertEquals("8h", clockOnly("8h"))
    }

    @Test fun servicesKeepTheTextAndBlankIsNotInformed() {
        assertEquals("Vacinação\nCurativos", aboutServices(profile(services = "Vacinação\nCurativos")))
        assertNull(aboutServices(profile(services = null)))
        assertNull(aboutServices(profile(services = "  \n ")))
        assertEquals(listOf("Clínica Geral", "Pediatria"), aboutSpecialties(profile(specialties = listOf(Specialty("a", "Clínica Geral"), Specialty("b", "Pediatria"), Specialty("c", " ")))))
    }

    @Test fun weekdayOfUsesSundayAsZero() {
        val sunday = java.util.Calendar.getInstance().apply { clear(); set(2026, java.util.Calendar.NOVEMBER, 15, 12, 0) }.timeInMillis
        assertEquals(0, weekdayOf(sunday))
        assertEquals(1, weekdayOf(sunday + 24 * 3_600_000L))
        assertEquals(6, weekdayOf(sunday - 24 * 3_600_000L))
    }

    // ---- Passos de progresso ----------------------------------------------------------------------------------

    @Test fun finishedStepsAccumulateAndOnlyTheLastOneRuns() {
        val steps = ProgressSteps().begin("Abrindo o Google…").begin("Entrando no NORA…")
        assertEquals(listOf(StepState.DONE, StepState.RUNNING), steps.steps.map { it.state })
        assertEquals("Entrando no NORA…", steps.current?.label)
        val done = steps.finish()
        assertTrue(done.steps.all { it.state == StepState.DONE }); assertNull(done.current)
    }

    @Test fun aFailedStepIsMarkedAndKeepsWhatAlreadyFinished() {
        val failed = ProgressSteps().begin("Abrindo o Google…").begin("Entrando no NORA…").fail("Entrando no NORA…", "Sem conexão.")
        assertEquals(listOf(StepState.DONE, StepState.FAILED), failed.steps.map { it.state })
        assertEquals("Sem conexão.", failed.steps.last().detail); assertTrue(failed.failed); assertNull(failed.current)
        // Sem passo em andamento a falha entra como passo, para a tela nunca ficar sem dizer que falhou.
        assertEquals(StepState.FAILED, ProgressSteps().fail("Abrindo o Google…").steps.single().state)
    }

    // ---- Remover UBS --------------------------------------------------------------------------------------------

    private val unitA = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", 0.0, 0.0)

    @Test fun removeUnitOnlyAppearsActingForYourself() {
        val session = PatientSession("t", "2099-01-01T00:00:00Z", PatientUser("i", "Maria"))
        val own = PatientState(session = session, units = listOf(unitA), selectedUnitId = "unit-a")
        assertTrue(canRemoveUnit(own, unitA))
        assertFalse("agindo por um dependente o botão some", canRemoveUnit(own.copy(actingFor = "dep-1"), unitA))
        assertFalse("sem UBS selecionada", canRemoveUnit(own, null))
        assertFalse("sem sessão", canRemoveUnit(own.copy(session = null), unitA))
    }
}
