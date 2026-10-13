package com.example.app_nora

import androidx.compose.runtime.Composable
import androidx.compose.ui.tooling.preview.Preview
import com.android.tools.screenshot.PreviewTest
import com.example.app_nora.api.*
import com.example.app_nora.preferences.*
import com.example.app_nora.ui.NoraPatientContent

// Visual fixtures only: never instantiated by the application or sent to an API.
private val previewUser = PatientUser("visual-fixture-only", "Paciente de demonstração visual")
private fun connected(status: String = "pending") = PatientState(
    address = "https://unidade.example.test",
    session = PatientSession("visual-fixture-not-a-token", "2026-10-10T20:00:00Z", previewUser),
    overview = PatientOverview(previewUser,
        listOf(QueueEntry(1, "2026-10-10T10:00:00Z", if (status == "pending") "offered" else "completed")),
        listOf(PatientOffer("00000000-0000-0000-0000-000000000001", "Avaliação na unidade · fixture visual", "2026-10-10T13:10:00Z", status))),
)

@PreviewTest
@Preview(name = "01 Home desconectada completa clara", widthDp = 400, heightDp = 2100, locale = "pt-rBR")
@Composable
fun HomeDisconnectedLight() = NoraPatientContent(UserSettings(Appearance.LIGHT), {}, onLogin = { _, _ -> })

@PreviewTest
@Preview(name = "02 Entrada clara", widthDp = 400, heightDp = 1000, locale = "pt-rBR")
@Composable
fun LoginLight() = NoraPatientContent(UserSettings(Appearance.LIGHT), {},
    PatientState(address = "https://unidade.example.test"), onLogin = { _, _ -> })

@PreviewTest
@Preview(name = "03 Entrada escura", widthDp = 400, heightDp = 1000, locale = "pt-rBR")
@Composable
fun LoginDark() = NoraPatientContent(UserSettings(Appearance.DARK), {},
    PatientState(address = "https://unidade.example.test"), onLogin = { _, _ -> })

@PreviewTest
@Preview(name = "04 Erro de entrada", widthDp = 400, heightDp = 1100, locale = "pt-rBR")
@Composable
fun LoginErrorLight() = NoraPatientContent(UserSettings(Appearance.LIGHT), {},
    PatientState(address = "https://unidade.example.test", message = "CPF, e-mail ou senha inválidos, ou sessão expirada. Entre novamente."), onLogin = { _, _ -> })

@PreviewTest
@Preview(name = "05 Conta fila e oferta pendente", widthDp = 400, heightDp = 1600, locale = "pt-rBR")
@Composable
fun PatientPendingLight() = NoraPatientContent(UserSettings(Appearance.LIGHT), {}, connected())

@PreviewTest
@Preview(name = "06 Oferta confirmada escura", widthDp = 400, heightDp = 1500, locale = "pt-rBR")
@Composable
fun PatientAcceptedDark() = NoraPatientContent(UserSettings(Appearance.DARK), {}, connected("accepted"))

@PreviewTest
@Preview(name = "07 Aparencia clara", widthDp = 400, heightDp = 900, locale = "pt-rBR")
@Composable
fun SettingsLight() = NoraPatientContent(UserSettings(Appearance.LIGHT), {}, initiallyShowSettings = true)

@PreviewTest
@Preview(name = "08 Aparencia escura", widthDp = 400, heightDp = 900, locale = "pt-rBR")
@Composable
fun SettingsDark() = NoraPatientContent(UserSettings(Appearance.DARK), {}, initiallyShowSettings = true)

@PreviewTest
@Preview(name = "09 Sistema claro", widthDp = 400, heightDp = 900, locale = "pt-rBR", uiMode = android.content.res.Configuration.UI_MODE_NIGHT_NO)
@Composable
fun SettingsSystemLight() = NoraPatientContent(UserSettings(Appearance.SYSTEM), {}, initiallyShowSettings = true)

@PreviewTest
@Preview(name = "10 Sistema escuro", widthDp = 400, heightDp = 900, locale = "pt-rBR", uiMode = android.content.res.Configuration.UI_MODE_NIGHT_YES)
@Composable
fun SettingsSystemDark() = NoraPatientContent(UserSettings(Appearance.SYSTEM), {}, initiallyShowSettings = true)

@PreviewTest
@Preview(name = "11 Configuracoes completas claras", widthDp = 400, heightDp = 2300, locale = "pt-rBR")
@Composable
fun SettingsCompleteLight() = NoraPatientContent(UserSettings(Appearance.LIGHT), {}, initiallyShowSettings = true)

@PreviewTest
@Preview(name = "12 Configuracoes completas escuras conectadas", widthDp = 400, heightDp = 2300, locale = "pt-rBR")
@Composable
fun SettingsCompleteDark() = NoraPatientContent(UserSettings(Appearance.DARK), {}, connected(), initiallyShowSettings = true)

@PreviewTest
@Preview(name = "13 Fonte ampliada 130", widthDp = 400, heightDp = 1100, locale = "pt-rBR", fontScale = 1.3f)
@Composable
fun LoginLargeFont() = NoraPatientContent(UserSettings(Appearance.LIGHT), {},
    PatientState(address = "https://unidade.example.test"), onLogin = { _, _ -> })

@PreviewTest
@Preview(name = "14 Tablet configuracoes", widthDp = 1000, heightDp = 1000, locale = "pt-rBR")
@Composable
fun SettingsTablet() = NoraPatientContent(UserSettings(Appearance.LIGHT), {}, initiallyShowSettings = true)

@PreviewTest
@Preview(name = "15 Fila aguardando sem oferta", widthDp = 400, heightDp = 1200, locale = "pt-rBR")
@Composable
fun PatientWaitingLight() = NoraPatientContent(UserSettings(Appearance.LIGHT), {},
    connected().copy(overview = PatientOverview(previewUser,
        listOf(QueueEntry(1, "2026-10-10T10:00:00Z", "waiting")), emptyList())))
