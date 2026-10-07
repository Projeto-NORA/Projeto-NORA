package com.example.app_nora

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import com.example.app_nora.preferences.*
import com.example.app_nora.ui.NoraContent
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class SettingsInteractionTest {
    @get:Rule val compose = createComposeRule()

    @Test fun settingsOpenedFromBottomOfHomeStartAtTheAppearanceSection() {
        var settings by mutableStateOf(UserSettings())
        compose.setContent { NoraContent(settings) { settings = it } }
        compose.onNodeWithText("Abrir configurações").performScrollTo().performClick()
        compose.onNodeWithText("Aparência").assertIsDisplayed()
        compose.onNodeWithContentDescription("Voltar ao início").assertIsDisplayed()
        compose.onNodeWithText("Restaurar preferências padrão").performScrollTo()
        compose.onNodeWithText("Início").performClick()
        compose.onNodeWithText("Configurações").performClick()
        compose.onNodeWithText("Aparência").assertIsDisplayed()
    }

    @Test fun appearanceSelectionAndResetUpdateTheWholeState() {
        var settings by mutableStateOf(UserSettings())
        compose.setContent { NoraContent(settings) { settings = it } }
        compose.onNodeWithText("Configurações").performClick()
        compose.onNodeWithText("Escura").performClick()
        compose.runOnIdle { assertEquals(Appearance.DARK, settings.appearance) }
        compose.onNodeWithText("Escura").assertIsDisplayed()
        compose.onNodeWithText("Restaurar preferências padrão").performScrollTo().performClick()
        compose.onNodeWithText("Restaurar").performClick()
        compose.runOnIdle { assertEquals(UserSettings(), settings) }
        compose.onNodeWithText("Sair da conta").performScrollTo().assertIsNotEnabled()
        compose.onNodeWithContentDescription("Voltar ao início").performScrollTo().performClick()
        compose.onNodeWithText("Cuidar de você começa aqui").assertIsDisplayed()
    }
}
