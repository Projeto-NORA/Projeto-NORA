package com.example.app_nora

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import com.example.app_nora.api.PatientState
import com.example.app_nora.preferences.*
import com.example.app_nora.ui.NoraPatientContent
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

/**
 * Configurações antes do login, pelo caminho que o app oferece hoje: a tela de entrada não tem engrenagem e chega às Configurações
 * pela Ajuda (o antigo botão "Abrir configurações" saiu na reforma visual).
 */
class SettingsInteractionTest {
    @get:Rule val compose = createComposeRule()

    private fun showLoginScreen(settings: () -> UserSettings, onChange: (UserSettings) -> Unit) = compose.setContent {
        NoraPatientContent(settings(), onChange, PatientState(address = "https://nora.example.test"), onLogin = { _, _ -> })
    }

    private fun openSettingsFromHelp() {
        compose.onNodeWithContentDescription("Ajuda com o aplicativo").performScrollTo().performClick()
        compose.onNodeWithText("Configurações").performClick()
    }

    @Test fun settingsOpenedBeforeLoginAlwaysStartAtTheAppearanceSection() {
        var settings by mutableStateOf(UserSettings())
        showLoginScreen({ settings }) { settings = it }
        openSettingsFromHelp()
        compose.onNodeWithText("Aparência").assertIsDisplayed()
        compose.onNodeWithContentDescription("Voltar ao início").assertIsDisplayed()
        // Desce até o fim das Configurações, volta à entrada e reabre: a tela começa de novo no topo, na Aparência.
        compose.onNodeWithText("Restaurar preferências padrão").performScrollTo()
        compose.onNodeWithContentDescription("Voltar ao início").performScrollTo().performClick()
        compose.onNodeWithText("Acessar minha conta").assertIsDisplayed()
        openSettingsFromHelp()
        compose.onNodeWithText("Aparência").assertIsDisplayed()
    }

    @Test fun appearanceSelectionAndResetUpdateTheWholeState() {
        var settings by mutableStateOf(UserSettings())
        showLoginScreen({ settings }) { settings = it }
        openSettingsFromHelp()
        compose.onNodeWithText("Escura").performClick()
        compose.runOnIdle { assertEquals(Appearance.DARK, settings.appearance) }
        compose.onNodeWithText("Escura").assertIsDisplayed()
        compose.onNodeWithText("Restaurar preferências padrão").performScrollTo().performClick()
        compose.onNodeWithText("Restaurar").performClick()
        compose.runOnIdle { assertEquals(UserSettings(), settings) }
        // Sem conta conectada não há como sair: a tela diz isso e não oferece "Sair da conta".
        compose.onNodeWithText("Nenhuma conta conectada neste dispositivo.").performScrollTo().assertIsDisplayed()
        compose.onNodeWithText("Sair da conta").assertDoesNotExist()
        compose.onNodeWithContentDescription("Voltar ao início").performScrollTo().performClick()
        compose.onNodeWithText("Acessar minha conta").assertIsDisplayed()
    }
}
