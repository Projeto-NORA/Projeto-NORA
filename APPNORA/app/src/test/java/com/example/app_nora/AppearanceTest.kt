package com.example.app_nora

import com.example.app_nora.preferences.Appearance
import org.junit.Assert.*
import org.junit.Test

class AppearanceTest {
    @Test fun systemPreferenceTracksBothSystemStates() {
        assertFalse(Appearance.SYSTEM.isDark(false))
        assertTrue(Appearance.SYSTEM.isDark(true))
    }
    @Test fun explicitChoiceOverridesSystem() {
        assertFalse(Appearance.LIGHT.isDark(true))
        assertTrue(Appearance.DARK.isDark(false))
    }
    @Test fun missingOrUnknownStoredChoiceUsesSystem() {
        assertEquals(Appearance.SYSTEM, Appearance.fromStored(null))
        assertEquals(Appearance.SYSTEM, Appearance.fromStored("old-value"))
        assertEquals(Appearance.DARK, Appearance.fromStored("DARK"))
    }
}
