package com.example.app_nora

import com.example.app_nora.ui.searchPages
import org.junit.Assert.*
import org.junit.Test

class PatientSearchTest {
    @Test fun searchFindsScreensWithoutAccentsOrCase() {
        assertTrue(searchPages("FAMILIA").any { it.page == "FAMILY" })
        assertTrue(searchPages("marcar consulta").any { it.page == "BOOKING" })
        assertTrue(searchPages("cancelar consulta").any { it.page == "APPOINTMENTS" })
        assertTrue(searchPages("trocar senha").any { it.page == "PASSWORD" })
    }

    @Test fun emptyAndUnknownQueriesHaveNoResults() {
        assertTrue(searchPages("  ").isEmpty())
        assertTrue(searchPages("palavrainexistente").isEmpty())
    }
}
