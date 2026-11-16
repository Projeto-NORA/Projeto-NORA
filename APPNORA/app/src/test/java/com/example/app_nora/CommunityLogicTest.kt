package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.Assert.*
import org.junit.Test

/** Regras puras de comunicados, Jornada, Família e avisos de oferta (sem Android). */
class CommunityLogicTest {
    private val now = requireNotNull(parseInstantMillis("2026-11-16T12:00:00Z"))

    // ---- Comunicados: incremental e dedupe ------------------------------------------------------------------

    @Test fun mergeKeepsAscendingSequenceAndFlagsOnlyUnknownIdsAsFresh() {
        val existing = listOf(announcement("a", 1), announcement("c", 3))
        val merged = mergeAnnouncements(existing, listOf(announcement("d", 4), announcement("b", 2)))
        assertEquals(listOf("a", "b", "c", "d"), merged.items.map { it.id })
        assertEquals(listOf("d", "b"), merged.fresh.map { it.id })
    }

    @Test fun mergingTheSameAnnouncementAgainIsNeverFreshTwice() {
        val first = mergeAnnouncements(emptyList(), listOf(announcement("a", 1), announcement("a", 1)))
        assertEquals(1, first.items.size); assertEquals(1, first.fresh.size)
        val second = mergeAnnouncements(first.items, listOf(announcement("a", 1)))
        assertEquals(1, second.items.size); assertTrue(second.fresh.isEmpty())
    }

    @Test fun mergeKeepsTheLocalReadMarkOfAnAnnouncementThatAlreadyExists() {
        val read = announcement("a", 1, isRead = true)
        val merged = mergeAnnouncements(listOf(read), listOf(announcement("a", 1, isRead = false)))
        assertTrue(merged.items.single().isRead); assertTrue(merged.fresh.isEmpty())
    }

    @Test fun visibleAnnouncementsHideTheExpiredOnesAndPutTheNewestFirst() {
        val items = listOf(announcement("a", 1), announcement("b", 2, expiresAt = "2026-11-16T11:59:59Z"),
            announcement("c", 3, expiresAt = "2026-11-20T00:00:00Z"), announcement("d", 4, isRead = true))
        assertEquals(listOf("d", "c", "a"), visibleAnnouncements(items, now).map { it.id })
        assertEquals(2, unreadAnnouncementCount(items, now))
    }

    // ---- Comunicados: notificação ------------------------------------------------------------------------

    @Test fun anAnnouncementIsNotifiedOnlyOnceEvenIfItComesBack() {
        val notified = NotifiedAnnouncements()
        assertEquals(listOf("a"), announcementsToNotify(listOf(announcement("a", 1)), true, notified).map { it.id })
        assertTrue(announcementsToNotify(listOf(announcement("a", 1)), true, notified).isEmpty())
        assertEquals(listOf("b"), announcementsToNotify(listOf(announcement("a", 1), announcement("b", 2)), true, notified).map { it.id })
    }

    @Test fun withThePreferenceOffNothingIsNotifiedAndTurningItOnLaterDoesNotDumpTheOldOnes() {
        val notified = NotifiedAnnouncements()
        assertTrue(announcementsToNotify(listOf(announcement("a", 1)), false, notified).isEmpty())
        // Ligar a preferência depois: o que já passou não é avisado; só o que chegar a partir daí.
        assertTrue(announcementsToNotify(listOf(announcement("a", 1)), true, notified).isEmpty())
        assertEquals(listOf("b"), announcementsToNotify(listOf(announcement("b", 2)), true, notified).map { it.id })
    }

    @Test fun announcementsAlreadyReadAreNeverNotified() {
        assertTrue(announcementsToNotify(listOf(announcement("a", 1, isRead = true)), true, NotifiedAnnouncements()).isEmpty())
    }

    @Test fun notifiedMemoryIsBoundedAndClearForgetsEverything() {
        val notified = NotifiedAnnouncements(capacity = 3)
        notified.markFresh(listOf("1", "2", "3", "4"))
        // O mais antigo ("1") foi esquecido e voltaria a ser considerado novo; os demais não.
        assertEquals(setOf("1"), notified.markFresh(listOf("1", "3", "4")))
        notified.markPosted("x"); notified.markPosted("y")
        assertEquals(listOf("x", "y"), notified.clear())
        assertEquals(setOf("3"), notified.markFresh(listOf("3")))
    }

    @Test fun noticeTextsAreSingularOrSummarizedWithoutLeakingBody() {
        assertEquals("Comunicado da rede", announcementNoticeTitle(1))
        assertEquals("4 novos comunicados da rede", announcementNoticeTitle(4))
        val two = listOf(announcement("a", 1, "Vacinação"), announcement("b", 2, "Reunião"))
        assertEquals("Vacinação e mais 1", announcementNoticeText(two))
        assertFalse(announcementNoticeText(two).contains("Corpo"))
    }

    // ---- Jornada ---------------------------------------------------------------------------------------------

    @Test fun calendarDatesAreFormattedWithoutTimeZoneShifts() {
        assertEquals("10/11/2026", formatCalendarDate("2026-11-10"))
        assertEquals("01/01/1999", formatCalendarDate("1999-01-01"))
        assertEquals("lixo", formatCalendarDate("lixo"))
        assertEquals("2026-13-01", formatCalendarDate("2026-13-01"))
    }

    @Test fun examStatusTextsFollowTheStatusAndNeverShowVoidedExams() {
        fun exam(status: String, performedOn: String? = null) = ExamRecord("e", "Hemograma", status, "2026-11-10T12:00:00Z", performedOn, null)
        assertTrue(examStatusText(exam("requested"))!!.startsWith("Solicitado em "))
        assertEquals("Realizado em 12/11/2026", examStatusText(exam("done", "2026-11-12")))
        assertEquals("Cancelado", examStatusText(exam("cancelled")))
        assertNull(examStatusText(exam("voided")))
        assertFalse(isShownExam(exam("voided"))); assertFalse(isShownExam(exam("estranho")))
    }

    @Test fun vaccineDosesAreTranslatedAndAnUnknownDoseIsOmitted() {
        fun vaccine(dose: String) = VaccineRecord("v", "Hepatite B", dose, "2026-11-01", "applied", null)
        assertEquals("1ª dose · aplicada em 01/11/2026", vaccineText(vaccine("1")))
        assertEquals("Reforço · aplicada em 01/11/2026", vaccineText(vaccine("booster")))
        assertEquals("Dose única · aplicada em 01/11/2026", vaccineText(vaccine("single")))
        assertEquals("aplicada em 01/11/2026", vaccineText(vaccine("???")))
        assertFalse(isShownVaccine(vaccine("1").copy(status = "voided")))
    }

    @Test fun recordsOverviewSeparatesAnsweredFailedAndPendingUnitsAndFiltersVoided() {
        val records = UnitRecords("unit-a", "UBS A",
            listOf(ExamRecord("e1", "Raio-X", "done", "2026-11-10T12:00:00Z", "2026-11-12", null), ExamRecord("e2", "Anulado", "voided", "2026-11-10T12:00:00Z", null, null)),
            listOf(VaccineRecord("v1", "Gripe", "single", "2026-05-01", "applied", null)))
        val data = mapOf("unit-a" to UnitSnapshot(records = records), "unit-b" to UnitSnapshot(recordsFailed = true))
        val overview = summarizeRecords(listOf(unitA, unitB, PatientHealthUnit("unit-c", "C", "", "", 0.0, 0.0)), data)
        assertEquals(1, overview.answered); assertEquals(1, overview.failed); assertEquals(1, overview.pending)
        assertEquals(1, overview.examCount); assertEquals(1, overview.vaccineCount)
    }

    // ---- Família e avisos de oferta ----------------------------------------------------------------------------

    @Test fun familyStatusesAreTranslatedAndGroupedWithActiveFirst() {
        assertEquals("Aguardando a equipe da UBS", familyStatusLabel("pending"))
        assertEquals("Recusado", familyStatusLabel("rejected"))
        assertEquals("Situação não reconhecida", familyStatusLabel("x"))
        val groups = groupFamilyLinks(listOf(link("1", "rejected"), link("2", "pending"), link("3", "approved"), link("4", "ended")))
        assertEquals(listOf("2", "3"), groups.active.map { it.id }); assertEquals(listOf("1", "4"), groups.history.map { it.id })
    }

    @Test fun beforeApprovalOnlyTheMaskedCpfIsShown() {
        assertEquals("CPF ***.456.789-**", dependentDisplayName(link("1", "pending", name = null)))
        assertEquals("Maria Souza", dependentDisplayName(link("1", "approved")))
        assertEquals("Maria Souza · UBS A", familyLinkLabel(link("1", "approved")))
        assertEquals("12345678901", cpfDigits("123.456.789-01"))
    }

    private fun offer(id: String, expiresIn: Long, status: String = "pending") = UnitOffer(id, "Cardiologia", "Dra. X",
        formatInstantUtc(now + 86_400_000), formatInstantUtc(now + 88_000_000), formatInstantUtc(now + expiresIn), status)

    @Test fun offerNoticesCarryTheDependentNameAndExcludeClosedOrExpiredOffers() {
        val state = PatientState(units = listOf(unitA), unitData = mapOf("unit-a" to UnitSnapshot(offers = listOf(offer("own", 600_000)))),
            dependentOffers = listOf(DependentOffers("dep-1", "unit-b", "UBS B", "Maria Souza",
                listOf(offer("d1", 300_000), offer("d2", -1_000), offer("d3", 300_000, "declined")))))
        val notices = openOfferNotices(state, now)
        assertEquals(listOf("d1", "own"), notices.map { it.offer.id })
        val dependent = notices.first()
        assertEquals("dep-1", dependent.dependentId)
        assertEquals("Vaga Relâmpago para Maria Souza · Cardiologia", offerNoticeTitle(dependent))
        assertTrue(offerNoticeText(dependent).startsWith("UBS B · responda até "))
        assertEquals("Vaga Relâmpago · Cardiologia", offerNoticeTitle(notices.last()))
        assertNull(notices.last().dependentName)
    }

    @Test fun effectiveStateIsTheDependentsOnlyWhileActingForAnApprovedLink() {
        val own = UnitSnapshot(offers = listOf(offer("own", 600_000)))
        val dep = UnitSnapshot(offers = listOf(offer("dep", 600_000)))
        val base = PatientState(units = listOf(unitA, unitB), selectedUnitId = "unit-a", unitData = mapOf("unit-a" to own),
            family = FamilyState(listOf(link("dep-1", "approved", "unit-b", "UBS B"))), dependentData = mapOf("dep-1" to dep))
        assertSame(base, base.effective())
        val acting = base.copy(actingFor = "dep-1").effective()
        assertEquals(listOf("unit-b"), acting.units?.map { it.id }); assertEquals("unit-b", acting.selectedUnitId)
        assertEquals(listOf("dep"), acting.unitOffers?.map { it.id })
        // Vínculo que deixou de estar aprovado: volta a mostrar os dados do próprio paciente.
        val ended = base.copy(actingFor = "dep-1", family = FamilyState(listOf(link("dep-1", "ended", "unit-b", "UBS B"))))
        assertSame(ended, ended.effective())
    }
}
