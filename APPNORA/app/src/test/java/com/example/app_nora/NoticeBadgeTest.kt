package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.Assert.assertEquals
import org.junit.Test

class NoticeBadgeTest {
    private val now = requireNotNull(parseInstantMillis("2026-11-22T12:00:00Z"))
    private fun announcement(id: String, seq: Long, read: Boolean = false, expires: String? = null) =
        Announcement(id, seq, id, "Texto", "2026-11-22T10:00:00Z", expires, read, null)
    private fun offer(id: String) = OfferNotice(UnitOffer(id, "Clínica", "Profissional", "2026-11-23T10:00:00Z",
        "2026-11-23T10:30:00Z", "2026-11-22T12:10:00Z", "pending"), "u", "UBS", null, null)

    @Test fun onlyNewUnreadAndNotExpiredAnnouncementsAndUnseenOffersCount() {
        val items = listOf(announcement("old", 1), announcement("new", 2), announcement("read", 3, true),
            announcement("expired", 4, expires = "2026-11-22T11:59:00Z"))
        assertEquals(2, noticeBadgeCount(items, 1, listOf(offer("seen"), offer("new")), setOf("seen"), now))
        assertEquals(0, noticeBadgeCount(items, 4, listOf(offer("seen")), setOf("seen"), now))
    }
}
