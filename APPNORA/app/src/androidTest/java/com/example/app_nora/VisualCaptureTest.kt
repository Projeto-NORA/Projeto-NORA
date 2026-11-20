package com.example.app_nora

import android.content.Context
import android.content.ContextWrapper
import android.content.SharedPreferences
import android.graphics.Bitmap
import androidx.compose.runtime.*
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import androidx.compose.ui.unit.Density
import androidx.test.platform.app.InstrumentationRegistry
import com.example.app_nora.api.PatientController
import com.example.app_nora.preferences.Appearance
import com.example.app_nora.preferences.UserSettings
import com.example.app_nora.ui.NoraPatientContent
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import java.io.File
import java.util.UUID

/** Opt-in photographs of the actual Compose tree; no fixtures are added to the app. */
class VisualCaptureTest {
    @get:Rule val compose = createComposeRule()

    @Test fun photographCurrentPatientScreens() {
        val args = InstrumentationRegistry.getArguments()
        assumeTrue("Opt in with noraCaptureVisual=true", args.getString("noraCaptureVisual") == "true")
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val suffix = "_visual_${UUID.randomUUID()}"
        val isolated = object : ContextWrapper(context) {
            override fun getSharedPreferences(name: String, mode: Int): SharedPreferences =
                super.getSharedPreferences(name + suffix, mode)
        }
        val controller = PatientController(isolated)
        val output = File(requireNotNull(context.getExternalFilesDir(null)), "nora-visual-2026-10-10").apply { mkdirs() }
        var settings by mutableStateOf(UserSettings(appearance = Appearance.LIGHT))
        var fontScale by mutableFloatStateOf(1f)
        compose.setContent {
            val deviceDensity = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(deviceDensity.density, fontScale)) {
                NoraPatientContent(settings, { settings = it }, controller.state,
                    onAddressChange = controller::changeAddress,
                    onLogin = { _, _ -> }, onRefresh = { }, onRespondUnit = { _, _ -> }, onLogout = { })
            }
        }
        fun capture(name: String) {
            compose.waitForIdle()
            val bitmap = compose.onRoot().captureToImage().asAndroidBitmap()
            check(bitmap.width > 0 && bitmap.height > 0)
            File(output, "$name.png").outputStream().use { check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)) }
        }
        try {
            capture("01-inicio-sem-conta-claro")
            compose.onNodeWithText("Acessar minha conta").performScrollTo()
            capture("02-conexao-login-vazio-claro")
            compose.onNodeWithContentDescription("Ajuda com o aplicativo").performClick()
            capture("03-ajuda-sem-conta-claro")
            compose.onNodeWithText("Configurações").performClick()
            capture("04-configuracoes-aparencia-claro")
            compose.onNodeWithText("Escura").performClick()
            capture("05-configuracoes-aparencia-escuro")
            compose.onNodeWithText("Sistema").performClick()
            capture("06-configuracoes-aparencia-sistema")
            compose.runOnIdle { settings = settings.copy(appearance = Appearance.LIGHT) }
            compose.onNodeWithText("Notificações").performScrollTo()
            capture("07-configuracoes-notificacoes-claro")
            compose.onNodeWithText("Interface").performScrollTo()
            capture("08-configuracoes-interface-claro")
            compose.onNodeWithText("Sobre o app").performScrollTo()
            capture("09-configuracoes-sobre-claro")
            compose.onNodeWithText("Restaurar preferências padrão").performScrollTo().performClick()
            capture("10-restaurar-confirmacao-claro")
            compose.onNodeWithText("Cancelar").performClick()
            compose.onNodeWithContentDescription("Voltar ao início").performClick()
            compose.onNodeWithText("Sua saúde, em um só lugar").performScrollTo()
            compose.runOnIdle { fontScale = 1.3f }
            capture("11-inicio-fonte-130-claro")
            compose.runOnIdle { fontScale = 1f }
        } finally {
            context.deleteSharedPreferences("nora_connection$suffix")
        }
    }
}
