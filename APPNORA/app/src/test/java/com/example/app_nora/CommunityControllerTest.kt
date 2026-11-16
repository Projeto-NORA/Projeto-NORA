package com.example.app_nora

import com.example.app_nora.api.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

/** Comunicados (cursor incremental, dedupe, lido) e Jornada agregada no controlador, com API falsa. */
class CommunityControllerTest {
    private suspend fun ready(api: FamilyFakeApi, vararg units: PatientHealthUnit): Triple<PatientController, PatientSession, Long> {
        api.units = units.toList()
        val controller = PatientController(MemoryAddressStore()) { api }
        controller.login("paciente@example.test", "test-only-input")
        val session = requireNotNull(controller.state.session)
        return Triple(controller, session, controller.beginPresence())
    }

    // ---- Comunicados -------------------------------------------------------------------------------------

    @Test fun theFirstReadHasNoCursorAndNeverNotifiesThenTheCursorDrivesTheIncrementalRead() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { after ->
            if (after == null) AnnouncementPage(listOf(announcement("a", 1), announcement("b", 2)), 2, false)
            else AnnouncementPage(listOf(announcement("c", 3)), 3, false)
        }
        val (controller, session, generation) = ready(api, unitA)
        val first = controller.pollCommunity(session, generation)
        assertTrue("a primeira leitura é silenciosa", first.isEmpty())
        assertEquals(listOf<Long?>(null), api.announcementAfter)
        assertEquals(2L, controller.state.announcements.cursor)
        assertEquals(listOf("a", "b"), controller.state.announcements.items.map { it.id })
        val second = controller.pollCommunity(session, generation)
        assertEquals(listOf<Long?>(null, 2L), api.announcementAfter)
        assertEquals(listOf("c"), second.map { it.id })
        assertEquals(3L, controller.state.announcements.cursor)
        assertEquals(listOf("a", "b", "c"), controller.state.announcements.items.map { it.id })
    }

    @Test fun anAnnouncementThatComesBackIsNotNewAgain() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { after ->
            if (after == null) AnnouncementPage(listOf(announcement("a", 1)), 1, false)
            else AnnouncementPage(listOf(announcement("b", 2)), 2, false)
        }
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        assertEquals(listOf("b"), controller.pollCommunity(session, generation).map { it.id })
        // O servidor repete o mesmo comunicado (por exemplo, depois de um ressincronismo): já está na lista, não é novo.
        assertTrue(controller.pollCommunity(session, generation).isEmpty())
        assertEquals(listOf("a", "b"), controller.state.announcements.items.map { it.id })
    }

    @Test fun morePagesAreFollowedWithTheCursorOfTheLastPage() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { after ->
            when (after) {
                null -> AnnouncementPage(listOf(announcement("a", 1)), 1, false)
                1L -> AnnouncementPage(listOf(announcement("b", 2)), 2, true)
                2L -> AnnouncementPage(listOf(announcement("c", 3)), 3, false)
                else -> AnnouncementPage(emptyList(), after, false)
            }
        }
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        val fresh = controller.pollCommunity(session, generation)
        assertEquals(listOf("b", "c"), fresh.map { it.id })
        assertEquals(listOf<Long?>(null, 1L, 2L), api.announcementAfter)
        assertEquals(3L, controller.state.announcements.cursor)
    }

    @Test fun theFullReadFollowsEveryPageSoTheNewestAnnouncementsAreNotCutOffAndNothingOldComesBackAsNew() = runBlocking {
        val api = FamilyFakeApi()
        // 8 páginas de um comunicado (o servidor devolve em ordem crescente de sequência): mais que as 5 voltas de antes.
        api.announcementsHandler = { after ->
            val next = (after ?: 0L) + 1
            if (next > 8) AnnouncementPage(emptyList(), after ?: 0L, false)
            else AnnouncementPage(listOf(announcement("n$next", next)), next, next < 8)
        }
        val (controller, session, generation) = ready(api, unitA)
        controller.loadAnnouncements()
        assertEquals((1L..8L).map { "n$it" }, controller.state.announcements.items.map { it.id })
        assertEquals(8L, controller.state.announcements.cursor)
        // O ciclo seguinte lê só a partir do cursor e não há nada de novo para avisar.
        assertTrue(controller.pollCommunity(session, generation).isEmpty())
        assertEquals(8, controller.state.announcements.items.size)
    }

    @Test fun aFullReadThatHitsTheCeilingStaysSilentWhileTheOlderAnnouncementsAreCaughtUp() = runBlocking {
        val api = FamilyFakeApi()
        // O servidor sempre diz que há mais (um comunicado por página): a leitura completa para no teto, mas não pode avisar o resto depois.
        api.announcementsHandler = { after ->
            val next = (after ?: 0L) + 1
            AnnouncementPage(listOf(announcement("n$next", next)), next, true)
        }
        val (controller, session, generation) = ready(api, unitA)
        controller.loadAnnouncements()
        val cut = controller.state.announcements.cursor ?: error("sem cursor")
        assertTrue("a leitura completa tem um teto", cut < 1_000)
        assertTrue("os comunicados seguintes chegam como continuação silenciosa", controller.pollCommunity(session, generation).isEmpty())
        assertTrue(controller.state.announcements.cursor!! > cut)
    }

    @Test fun aCursorThatDoesNotAdvanceStopsTheReadInsteadOfLooping() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { _ -> AnnouncementPage(listOf(announcement("a", 1)), 1, true) }
        val (controller, _, _) = ready(api, unitA)
        controller.loadAnnouncements()
        assertTrue("no máximo duas chamadas: a segunda não adianta o cursor", api.announcementAfter.size <= 2)
        assertEquals(listOf("a"), controller.state.announcements.items.map { it.id })
    }

    @Test fun anEmptyIncrementalReadKeepsTheCursorAndTheList() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { after -> if (after == null) AnnouncementPage(listOf(announcement("a", 1)), 7, false) else AnnouncementPage(emptyList(), 7, false) }
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        assertTrue(controller.pollCommunity(session, generation).isEmpty())
        assertEquals(7L, controller.state.announcements.cursor)
        assertEquals(1, controller.state.announcements.items.size)
    }

    @Test fun openingTheTabDoesAFullReadReplacingTheListWithoutNotifying() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { after ->
            if (after == null) AnnouncementPage(listOf(announcement("a", 1), announcement("z", 9, isRead = true)), 9, false)
            else AnnouncementPage(emptyList(), after, false)
        }
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        controller.loadAnnouncements()
        assertEquals("duas leituras completas, ambas sem cursor", listOf<Long?>(null, null), api.announcementAfter)
        assertEquals(listOf("a", "z"), controller.state.announcements.items.map { it.id })
        assertFalse(controller.state.announcements.loading)
    }

    @Test fun linkingNewUnitsForcesAFullReadOfTheAnnouncements() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { AnnouncementPage(listOf(announcement("a", 1)), 1, false) }
        val (controller, _, _) = ready(api, unitA)
        controller.linkUnits(listOf("unit-b"))
        assertEquals(listOf<Long?>(null), api.announcementAfter)
        assertEquals(listOf("a"), controller.state.announcements.items.map { it.id })
    }

    @Test fun markingAsReadCallsTheServerOnceAndUpdatesTheItem() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { AnnouncementPage(listOf(announcement("a", 1)), 1, false) }
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        controller.markAnnouncementRead("a")
        controller.markAnnouncementRead("a")
        assertEquals(listOf("a"), api.readIds)
        assertTrue(controller.state.announcements.items.single().isRead)
        assertNotNull(controller.state.announcements.items.single().readAt)
        // Desconhecido nunca gera requisição.
        controller.markAnnouncementRead("inexistente")
        assertEquals(1, api.readIds.size)
    }

    @Test fun aFailedMarkKeepsTheItemUnreadAndShowsAMessage() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { AnnouncementPage(listOf(announcement("a", 1)), 1, false) }
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        api.markReadFailure = IOException("offline")
        controller.markAnnouncementRead("a")
        assertFalse(controller.state.announcements.items.single().isRead)
        assertEquals(CONNECTION_FAILED_MESSAGE, controller.state.announcements.error)
        api.markReadFailure = null
        controller.markAnnouncementRead("a")
        assertTrue(controller.state.announcements.items.single().isRead)
        assertNull(controller.state.announcements.error)
    }

    @Test fun aFailedFullReadKeepsTheListAndSetsTheError() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { AnnouncementPage(listOf(announcement("a", 1)), 1, false) }
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        api.announcementsFailure = failure(503, "service-unavailable", ApiContext.ANNOUNCEMENTS)
        controller.loadAnnouncements()
        assertEquals(listOf("a"), controller.state.announcements.items.map { it.id })
        assertNotNull(controller.state.announcements.error)
        assertFalse(controller.state.announcements.loading)
    }

    @Test fun an401DuringTheCycleEndsTheSession() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsFailure = failure(401, "unauthorized", ApiContext.ANNOUNCEMENTS)
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        assertNull(controller.state.session)
    }

    @Test fun aStaleGenerationNeverWritesTheCycleResult() = runBlocking {
        val api = FamilyFakeApi()
        api.announcementsHandler = { AnnouncementPage(listOf(announcement("a", 1)), 1, false) }
        val (controller, session, generation) = ready(api, unitA)
        controller.beginPresence()
        assertTrue(controller.pollCommunity(session, generation).isEmpty())
        assertTrue(api.calls.none { it.startsWith("own.announcements") })
    }

    @Test fun theCycleDoesOneLightRequestForEachKindAndSkipsDependentsWhenThereAreNone() = runBlocking {
        val api = FamilyFakeApi()
        val (controller, session, generation) = ready(api, unitA)
        controller.pollCommunity(session, generation)
        assertEquals(1, api.callsOf("own.announcements").size)
        assertEquals(1, api.callsOf("own.emailSuggestion").size)
        assertEquals(1, api.callsOf("own.dependents").size)
        assertEquals("sem dependentes aprovados não há leitura de ofertas deles", 0, api.callsOf("own.dependentOffers").size)
        controller.pollCommunity(session, generation)
        assertEquals(2, api.callsOf("own.announcements").size)
        assertEquals(2, api.callsOf("own.emailSuggestion").size)
        // A lista de família só é lida de novo enquanto houver pedido pendente.
        assertEquals(1, api.callsOf("own.dependents").size)
    }

    // ---- Jornada -------------------------------------------------------------------------------------------

    @Test fun recordsAreReadWithOneAggregatedRequestAndSplitByUnit() = runBlocking {
        val api = FamilyFakeApi()
        api.records = listOf(
            UnitRecords("unit-a", "UBS A", listOf(ExamRecord("e1", "Hemograma", "done", "2026-11-10T12:00:00Z", "2026-11-12", "Jejum")), emptyList()),
            UnitRecords("unit-b", "UBS B", emptyList(), listOf(VaccineRecord("v1", "Gripe", "single", "2026-05-01", "applied", null))))
        val (controller, _, _) = ready(api, unitA, unitB)
        controller.loadRecords()
        assertEquals(listOf("own.records"), api.callsOf("own.records"))
        val overview = summarizeRecords(controller.state.units, controller.state.unitData)
        assertEquals(2, overview.answered); assertEquals(0, overview.failed)
        assertEquals(1, overview.examCount); assertEquals(1, overview.vaccineCount)
        assertFalse(controller.state.records.loading)
    }

    @Test fun anEmptyRecordsAnswerIsAnHonestEmptyStateNotAFailure() = runBlocking {
        val api = FamilyFakeApi()
        api.records = listOf(UnitRecords("unit-a", "UBS A", emptyList(), emptyList()))
        val (controller, _, _) = ready(api, unitA)
        controller.loadRecords()
        val overview = summarizeRecords(controller.state.units, controller.state.unitData)
        assertEquals(1, overview.answered); assertEquals(0, overview.failed); assertEquals(0, overview.examCount)
    }

    @Test fun aUnitMissingFromTheAggregatedAnswerIsMarkedAsNotAnsweredNeverAsEmpty() = runBlocking {
        val api = FamilyFakeApi()
        api.records = listOf(UnitRecords("unit-a", "UBS A", emptyList(), emptyList()))
        val (controller, _, _) = ready(api, unitA, unitB)
        controller.loadRecords()
        val overview = summarizeRecords(controller.state.units, controller.state.unitData)
        assertEquals(1, overview.answered); assertEquals(1, overview.failed)
    }

    @Test fun aRecordsFailureMarksEveryUnitAndKeepsAMessageWithoutStaleData() = runBlocking {
        val api = FamilyFakeApi()
        api.records = listOf(UnitRecords("unit-a", "UBS A", listOf(ExamRecord("e1", "Hemograma", "done", "2026-11-10T12:00:00Z", "2026-11-12", null)), emptyList()))
        val (controller, _, _) = ready(api, unitA)
        controller.loadRecords()
        api.recordsFailure = failure(503, "service-unavailable", ApiContext.RECORDS)
        controller.loadRecords()
        val overview = summarizeRecords(controller.state.units, controller.state.unitData)
        assertEquals(0, overview.answered); assertEquals(1, overview.failed)
        assertNotNull(controller.state.records.error)
        assertFalse(controller.state.records.loading)
        api.recordsFailure = null
        controller.loadRecords()
        assertEquals(1, summarizeRecords(controller.state.units, controller.state.unitData).answered)
        assertNull(controller.state.records.error)
    }

    @Test fun recordsWithNoLinkedUnitsMakeNoRequest() = runBlocking {
        val api = FamilyFakeApi()
        val (controller, _, _) = ready(api)
        controller.loadRecords()
        assertTrue(api.callsOf("own.records").isEmpty())
    }
}
