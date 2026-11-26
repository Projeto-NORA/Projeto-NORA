package com.example.app_nora

import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.v2.createEmptyComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.core.content.edit
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.example.app_nora.api.CONNECTION_FAILED_MESSAGE
import com.example.app_nora.api.LOCAL_NETWORK_DENIED_MESSAGE
import com.example.app_nora.api.secureSessionStorage
import java.util.regex.Pattern
import org.junit.After
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeFalse
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.FixMethodOrder
import org.junit.Rule
import org.junit.Test
import org.junit.runners.MethodSorters

/**
 * P-046: o pedido de ACCESS_LOCAL_NETWORK em tela (Android 17). Com um endereço da rede local (o 10.0.2.2 do emulador), tocar em Entrar
 * abre o diálogo de permissão do SISTEMA, que só o UiAutomator alcança. Negar: a tela diz o que fazer e nada é enviado ao serviço.
 * Conceder: o pedido segue (aqui a porta 1 recusa a conexão, e uma falha de conexão é justamente a prova de que o pedido saiu).
 * Em Android anterior ao 17 o diálogo não existe e os testes são pulados.
 */
@OptIn(ExperimentalTestApi::class)
@FixMethodOrder(MethodSorters.NAME_ASCENDING)
class LocalNetworkPermissionTest {
    @get:Rule val compose = createEmptyComposeRule()

    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private lateinit var savedAddress: String

    @Before fun prepare() {
        assumeTrue("ACCESS_LOCAL_NETWORK só existe a partir do Android 17 (API 37)", Build.VERSION.SDK_INT >= 37)
        val connection = context.getSharedPreferences("nora_connection", Context.MODE_PRIVATE)
        savedAddress = connection.getString("address", "").orEmpty()
        connection.edit(commit = true) { putString("address", LOCAL_ADDRESS) }
        // Sem sessão guardada de uma rodada anterior: a MainActivity precisa abrir no formulário de entrada, não restaurar uma conta.
        secureSessionStorage(context).clear()
        // Sem a permissão e com o endereço da rede local, o app precisa pedi-la, e o diálogo só aparece com ela negada. Não dá para
        // revogá-la aqui: `pm revoke` de uma permissão concedida MATA o processo do app, e o processo do teste é o mesmo (provado:
        // "Crash of app ... running instrumentation"). Por isso a ordem dos casos é fixa (negar, depois conceder) e, se a permissão já
        // vier concedida (execução repetida sem desinstalar), o caso é pulado com o motivo; o AGP desinstala o app ao fim de cada
        // `connectedDebugAndroidTest`, o que devolve a permissão ao estado inicial. Para repetir à mão: `adb shell pm revoke` antes.
        assumeFalse("ACCESS_LOCAL_NETWORK já concedida: revogue com `adb shell pm revoke ${context.packageName} $PERMISSION` e rode de novo", granted())
    }

    @After fun restore() {
        if (Build.VERSION.SDK_INT < 37) return
        context.getSharedPreferences("nora_connection", Context.MODE_PRIVATE).edit(commit = true) { putString("address", savedAddress) }
    }

    @Test fun step1DenyingTheSystemDialogShowsTheExplanationAndSendsNothing() {
        ActivityScenario.launch(MainActivity::class.java).use {
            signIn()
            val deny = device.wait(Until.findObject(By.res(Pattern.compile(".*permission_deny_button")).pkg(Pattern.compile(".*permissioncontroller.*"))), DIALOG_WAIT_MS)
                ?: device.wait(Until.findObject(By.text(Pattern.compile("(?i)don.?t allow|deny|não permitir|negar"))), 3_000)
            assertNotNull("O diálogo de ACCESS_LOCAL_NETWORK não apareceu (tela: ${device.currentPackageName})", deny)
            deny.click()
            compose.waitUntilAtLeastOneExists(hasText(LOCAL_NETWORK_DENIED_MESSAGE), 10_000)
            compose.onNodeWithText(LOCAL_NETWORK_DENIED_MESSAGE).assertExists()
            // Nada foi enviado: se o pedido tivesse saído, a porta 1 recusaria a conexão e a tela diria isso.
            compose.waitForIdle()
            Thread.sleep(2_000)
            compose.onNodeWithText(CONNECTION_FAILED_MESSAGE).assertDoesNotExist()
            assertTrue("A permissão não podia ter sido concedida", !granted())
        }
    }

    @Test fun step2AllowingTheSystemDialogLetsTheRequestThrough() {
        ActivityScenario.launch(MainActivity::class.java).use {
            signIn()
            val allow = device.wait(Until.findObject(By.res(Pattern.compile(".*permission_allow_button")).pkg(Pattern.compile(".*permissioncontroller.*"))), DIALOG_WAIT_MS)
                ?: device.wait(Until.findObject(By.text(Pattern.compile("(?i)allow|permitir"))), 3_000)
            assertNotNull("O diálogo de ACCESS_LOCAL_NETWORK não apareceu (tela: ${device.currentPackageName})", allow)
            allow.click()
            // O pedido segue para 10.0.2.2:1, que recusa a conexão: a mensagem de falha de conexão prova que ele saiu.
            compose.waitUntilAtLeastOneExists(hasText(CONNECTION_FAILED_MESSAGE), 30_000)
            compose.onNodeWithText(LOCAL_NETWORK_DENIED_MESSAGE).assertDoesNotExist()
            assertTrue("A permissão devia ter sido concedida", granted())
        }
    }

    private fun signIn() {
        compose.waitUntilAtLeastOneExists(hasText("Entrar"), 15_000)
        compose.onNodeWithText("CPF ou e-mail").performTextInput("paciente@example.test")
        compose.onNodeWithText("Senha").performTextInput("senha-de-teste-123")
        compose.onNodeWithText("Entrar").performClick()
    }

    private fun granted() = context.checkSelfPermission(PERMISSION) == PackageManager.PERMISSION_GRANTED

    private companion object {
        const val PERMISSION = "android.permission.ACCESS_LOCAL_NETWORK"
        const val LOCAL_ADDRESS = "http://10.0.2.2:1"
        const val DIALOG_WAIT_MS = 10_000L
    }
}
