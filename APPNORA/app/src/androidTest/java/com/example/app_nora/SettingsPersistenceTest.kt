package com.example.app_nora

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.app_nora.preferences.*
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class SettingsPersistenceTest {
    @Test fun restoredPreferencesWithWrongTypesFallBackWithoutCrashing() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val original = SettingsStore(context).settings
        try {
            context.getSharedPreferences("nora_preferences", 0).edit()
                .putInt("appearance", 8)
                .putString("show_guidance", "invalid")
                .commit()
            val restored = SettingsStore(context)
            assertEquals(Appearance.SYSTEM, restored.settings.appearance)
            assertEquals(true, restored.settings.showGuidance)
        } finally { SettingsStore(context).update(original) }
    }
    @Test fun persistedChoicesSurviveNewStoreAndReset() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val first = SettingsStore(context)
        val original = first.settings
        try {
            val selected = UserSettings(Appearance.DARK, false, false, false, false)
            first.update(selected)
            assertEquals(selected, SettingsStore(context).settings)
            first.update(UserSettings())
            assertEquals(UserSettings(), SettingsStore(context).settings)
        } finally { first.update(original) }
    }
}
