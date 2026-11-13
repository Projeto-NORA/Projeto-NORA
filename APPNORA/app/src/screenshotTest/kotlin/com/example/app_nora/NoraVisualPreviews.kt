package com.example.app_nora

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.Modifier
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import com.android.tools.screenshot.PreviewTest
import com.example.app_nora.api.*
import com.example.app_nora.preferences.*
import com.example.app_nora.ui.AppointmentsScreen
import com.example.app_nora.ui.ChangePasswordScreen
import com.example.app_nora.ui.JourneyScreen
import com.example.app_nora.ui.LeaveQueueDialog
import com.example.app_nora.ui.UnlinkDialog
import com.example.app_nora.ui.FlashScreen
import com.example.app_nora.ui.LocalClockMillis
import com.example.app_nora.ui.NoraPatientContent
import com.example.app_nora.ui.NoraTheme

// Visual fixtures only: never instantiated by the application or sent to an API.
// The clock is fixed so countdowns and "next appointment" never change between runs.
private const val FIXED_NOW = "2026-11-12T12:00:00Z"
private val fixedNowMillis = requireNotNull(parseInstantMillis(FIXED_NOW))
private fun at(offsetSeconds: Long): String =
    java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", java.util.Locale.ROOT)
        .apply { timeZone = java.util.TimeZone.getTimeZone("UTC") }.format(fixedNowMillis + offsetSeconds * 1000)

private val previewUser = PatientUser("visual-fixture-only", "Paciente de demonstração visual")
private val centro = PatientHealthUnit("unit-centro", "UBS Centro · fixture visual", "Centro", "Rua de fixture, 100", -23.5, -46.6)
private val norte = PatientHealthUnit("unit-norte", "UBS Norte · fixture visual", "Norte", "Rua de fixture, 200", -23.4, -46.7)

private fun offer(id: String, specialty: String, expiresIn: Long, status: String = "pending") =
    UnitOffer(id, specialty, "Profissional · fixture visual", at(86_400), at(86_400 + 1_800), at(expiresIn), status)

private fun session() = PatientSession("visual-fixture-not-a-token", "2026-10-10T20:00:00Z", previewUser)

private fun connected(offers: List<UnitOffer>, queue: List<UnitQueueEntry> = emptyList(),
    appointments: List<PatientAppointment>? = emptyList(), message: String? = null) = PatientState(
    address = "https://unidade.example.test", session = session(), units = listOf(centro), selectedUnitId = centro.id, message = message,
    unitData = mapOf(centro.id to UnitSnapshot(queue = queue, offers = offers, appointments = appointments)))

private val nextAppointment = PatientAppointment("ap-1", "Ortopedia · fixture visual", "Profissional · fixture visual", at(172_800), at(172_800 + 1_800), "scheduled")

@Composable
private fun Fixed(content: @Composable () -> Unit) = CompositionLocalProvider(LocalClockMillis provides { fixedNowMillis }, content = content)

@Composable
private fun PlainScreen(appearance: Appearance, content: @Composable ColumnScope.() -> Unit) = Fixed {
    NoraTheme(appearance) {
        Column(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background).padding(horizontal = 20.dp, vertical = 24.dp),
            verticalArrangement = Arrangement.spacedBy(20.dp), content = content)
    }
}

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
    PatientState(address = "https://unidade.example.test", message = "CPF, e-mail ou senha inválidos."), onLogin = { _, _ -> })

@PreviewTest
@Preview(name = "05 Inicio com proxima consulta e oferta pendente", widthDp = 400, heightDp = 2000, locale = "pt-rBR")
@Composable
fun PatientPendingLight() = Fixed {
    NoraPatientContent(UserSettings(Appearance.LIGHT), {}, connected(
        offers = listOf(offer("o1", "Cardiologia · fixture visual", 570)),
        queue = listOf(UnitQueueEntry(1, "Cardiologia · fixture visual", at(-3_600), "offered")),
        appointments = listOf(nextAppointment)))
}

@PreviewTest
@Preview(name = "06 Inicio com oferta aceita escuro", widthDp = 400, heightDp = 1900, locale = "pt-rBR")
@Composable
fun PatientAcceptedDark() = Fixed {
    NoraPatientContent(UserSettings(Appearance.DARK), {}, connected(
        offers = listOf(offer("o1", "Cardiologia · fixture visual", -60, "accepted")),
        queue = listOf(UnitQueueEntry(1, "Cardiologia · fixture visual", at(-3_600), "completed")),
        appointments = listOf(nextAppointment)))
}

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
@Preview(name = "11 Configuracoes completas claras", widthDp = 400, heightDp = 2500, locale = "pt-rBR")
@Composable
fun SettingsCompleteLight() = NoraPatientContent(UserSettings(Appearance.LIGHT), {}, initiallyShowSettings = true)

@PreviewTest
@Preview(name = "12 Configuracoes completas escuras conectadas", widthDp = 400, heightDp = 2500, locale = "pt-rBR")
@Composable
fun SettingsCompleteDark() = NoraPatientContent(UserSettings(Appearance.DARK), {}, connected(emptyList()), initiallyShowSettings = true)

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
@Preview(name = "15 Fila aguardando sem oferta", widthDp = 400, heightDp = 1700, locale = "pt-rBR")
@Composable
fun PatientWaitingLight() = Fixed {
    NoraPatientContent(UserSettings(Appearance.LIGHT), {}, connected(emptyList(),
        queue = listOf(UnitQueueEntry(1, "Cardiologia · fixture visual", at(-3_600), "waiting"),
            UnitQueueEntry(2, "Clínica geral · fixture visual", at(-864_000), "completed"))))
}

@PreviewTest
@Preview(name = "16 Vaga Relampago com contagem regressiva", widthDp = 400, heightDp = 1500, locale = "pt-rBR")
@Composable
fun FlashCountdownLight() = PlainScreen(Appearance.LIGHT) {
    FlashScreen(connected(listOf(offer("o1", "Cardiologia · fixture visual", 570), offer("o2", "Dermatologia · fixture visual", 45),
        offer("o3", "Clínica geral · fixture visual", -3_600, "accepted"))), {}, {}, {}, { _, _ -> })
}

@PreviewTest
@Preview(name = "17 Vaga Relampago prazo encerrado escura", widthDp = 400, heightDp = 1300, locale = "pt-rBR")
@Composable
fun FlashExpiredDark() = PlainScreen(Appearance.DARK) {
    FlashScreen(connected(listOf(offer("o1", "Cardiologia · fixture visual", -30),
        offer("o2", "Dermatologia · fixture visual", -7_200, "declined")), message = "Esta oportunidade não está mais disponível."),
        {}, {}, {}, { _, _ -> })
}

@PreviewTest
@Preview(name = "18 Inicio sem consulta futura e sem oferta", widthDp = 400, heightDp = 1800, locale = "pt-rBR")
@Composable
fun HomeNoAppointmentLight() = Fixed {
    NoraPatientContent(UserSettings(Appearance.LIGHT), {}, connected(emptyList(), appointments = emptyList()))
}

@PreviewTest
@Preview(name = "19 Inicio sem UBS vinculada", widthDp = 400, heightDp = 1800, locale = "pt-rBR")
@Composable
fun HomeNoUnitsDark() = Fixed {
    NoraPatientContent(UserSettings(Appearance.DARK), {}, PatientState(address = "https://unidade.example.test",
        session = session(), units = emptyList()))
}

@PreviewTest
@Preview(name = "20 Inicio UBS nao carregadas", widthDp = 400, heightDp = 1800, locale = "pt-rBR")
@Composable
fun HomeUnitsNotLoadedLight() = Fixed {
    NoraPatientContent(UserSettings(Appearance.LIGHT), {}, PatientState(address = "https://unidade.example.test", session = session()))
}

@PreviewTest
@Preview(name = "21 Consultas com inicio fim e situacoes", widthDp = 400, heightDp = 1700, locale = "pt-rBR")
@Composable
fun AppointmentsLight() = PlainScreen(Appearance.LIGHT) {
    AppointmentsScreen(connected(emptyList(), appointments = listOf(nextAppointment,
        PatientAppointment("ap-2", "Clínica · fixture visual", "Profissional · fixture visual", at(-86_400), at(-84_600), "completed"),
        PatientAppointment("ap-3", "Pediatria · fixture visual", "Profissional · fixture visual", at(-172_800), at(-171_000), "missed"),
        PatientAppointment("ap-4", "Exame · fixture visual", "Profissional · fixture visual", at(-259_200), at(-257_400), "cancelled"))),
        {}, {}, {}, {})
}

private fun withAttendance(vararg items: Pair<PatientHealthUnit, UnitAttendance?>, failed: Set<String> = emptySet(),
    queue: List<UnitQueueEntry> = emptyList()) = PatientState(
    address = "https://unidade.example.test", session = session(), units = items.map { it.first }, selectedUnitId = items.first().first.id,
    unitData = items.associate { (unit, value) ->
        unit.id to UnitSnapshot(queue = queue.takeIf { unit.id == items.first().first.id }, attendance = value, attendanceFailed = unit.id in failed)
    })

@PreviewTest
@Preview(name = "22 Jornada com indice de comparecimento", widthDp = 400, heightDp = 2000, locale = "pt-rBR")
@Composable
fun JourneyWithScoreLight() = PlainScreen(Appearance.LIGHT) {
    JourneyScreen(withAttendance(centro to UnitAttendance(8, 1, 2, 0.5, 0.84, 12),
        queue = listOf(UnitQueueEntry(1, "Cardiologia · fixture visual", at(-3_600), "waiting"))), {}, {}, {})
}

@PreviewTest
@Preview(name = "23 Jornada sem consultas encerradas escura", widthDp = 400, heightDp = 1700, locale = "pt-rBR")
@Composable
fun JourneyNoDataDark() = PlainScreen(Appearance.DARK) {
    JourneyScreen(withAttendance(centro to UnitAttendance(0, 0, 0, 0.0, null, 12)), {}, {}, {})
}

@PreviewTest
@Preview(name = "24 Jornada com duas UBS e uma sem resposta", widthDp = 400, heightDp = 1900, locale = "pt-rBR")
@Composable
fun JourneyOneUnitDownLight() = PlainScreen(Appearance.LIGHT) {
    JourneyScreen(withAttendance(centro to UnitAttendance(5, 0, 0, 0.0, 1.0, 12), norte to null, failed = setOf(norte.id)), {}, {}, {})
}

@PreviewTest
@Preview(name = "25 Dialogo sair da fila", widthDp = 400, heightDp = 700, locale = "pt-rBR")
@Composable
fun LeaveQueueDialogLight() = PlainScreen(Appearance.LIGHT) {
    LeaveQueueDialog(UnitQueueEntry(1, "Cardiologia · fixture visual", at(-3_600), "offered"), {}, {})
}

@PreviewTest
@Preview(name = "26 Dialogo remover UBS com contagens escuro", widthDp = 400, heightDp = 900, locale = "pt-rBR")
@Composable
fun UnlinkDialogDark() = PlainScreen(Appearance.DARK) {
    UnlinkDialog("UBS Centro · fixture visual", UnlinkImpact(2, 1), loading = false, busy = false, onConfirm = {}, onDismiss = {})
}

@PreviewTest
@Preview(name = "27 Alterar senha clara", widthDp = 400, heightDp = 1000, locale = "pt-rBR")
@Composable
fun ChangePasswordLight() = PlainScreen(Appearance.LIGHT) { ChangePasswordScreen(required = false) { _, _ -> } }

@PreviewTest
@Preview(name = "28 Alterar senha exigida escura", widthDp = 400, heightDp = 1100, locale = "pt-rBR")
@Composable
fun ChangePasswordRequiredDark() = PlainScreen(Appearance.DARK) { ChangePasswordScreen(required = true) { _, _ -> } }
