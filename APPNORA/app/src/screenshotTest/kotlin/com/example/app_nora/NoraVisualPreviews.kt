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
import com.example.app_nora.ui.BookingScreen
import com.example.app_nora.ui.ChangePasswordScreen
import com.example.app_nora.ui.FamilyScreen
import com.example.app_nora.ui.JourneyScreen
import com.example.app_nora.ui.JourneySection
import com.example.app_nora.ui.NoticesScreen
import com.example.app_nora.ui.ProfileScreen
import com.example.app_nora.ui.LeaveQueueDialog
import com.example.app_nora.ui.UnlinkDialog
import com.example.app_nora.ui.FlashScreen
import com.example.app_nora.ui.LocalClockMillis
import com.example.app_nora.ui.NoraPatientContent
import com.example.app_nora.ui.NoraTheme
import com.example.app_nora.ui.GoogleActions
import com.example.app_nora.ui.StepList
import com.example.app_nora.ui.UnitAboutCard

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
@Preview(name = "22 Jornada com score em pontos", widthDp = 400, heightDp = 2000, locale = "pt-rBR")
@Composable
fun JourneyWithScoreLight() = PlainScreen(Appearance.LIGHT) {
    JourneyScreen(withAttendance(centro to UnitAttendance(8, 1, 2, 0.5, 0.84, 12),
        queue = listOf(UnitQueueEntry(1, "Cardiologia · fixture visual", at(-3_600), "waiting"))), {}, {}, {})
}

@PreviewTest
@Preview(name = "22b Jornada com score baixo escura", widthDp = 400, heightDp = 1700, locale = "pt-rBR")
@Composable
fun JourneyLowScoreDark() = PlainScreen(Appearance.DARK) {
    JourneyScreen(withAttendance(centro to UnitAttendance(1, 4, 2, 1.0, 0.1667, 12)), {}, {}, {})
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

// ---- Agendamento e confirmação de presença --------------------------------------------------------------

private val cardiologyFixture = Specialty("spec-card", "Cardiologia · fixture visual")
private val dermatologyFixture = Specialty("spec-derm", "Dermatologia · fixture visual")
private fun freeSlot(offsetSeconds: Long, professional: String, id: String) = AvailableSlot(at(offsetSeconds), at(offsetSeconds + 1_800),
    id, professional, cardiologyFixture.id, cardiologyFixture.name)

private fun booking(slots: List<AvailableSlot>? = null, lastBooked: BookedAppointment? = null, message: String? = null) = PatientState(
    address = "https://unidade.example.test", session = session(), units = listOf(centro, norte), selectedUnitId = centro.id, message = message,
    specialties = listOf(cardiologyFixture, dermatologyFixture),
    availability = slots?.let { AvailabilityResult(centro.id, cardiologyFixture.id, 0, it) }, lastBooked = lastBooked)

@PreviewTest
@Preview(name = "29 Agendamento escolha de especialidade clara", widthDp = 400, heightDp = 1500, locale = "pt-rBR")
@Composable
fun BookingSpecialtyLight() = PlainScreen(Appearance.LIGHT) { BookingScreen(booking(), {}, {}, {}, { _, _ -> }, {}, {}) }

@PreviewTest
@Preview(name = "30 Agendamento horarios do dia clara", widthDp = 400, heightDp = 2300, locale = "pt-rBR")
@Composable
fun BookingTimesLight() = PlainScreen(Appearance.LIGHT) {
    BookingScreen(booking(listOf(freeSlot(90_000, "Profissional A · fixture visual", "p1"), freeSlot(93_600, "Profissional B · fixture visual", "p2"),
        freeSlot(97_200, "Profissional A · fixture visual", "p1"), freeSlot(176_400, "Profissional A · fixture visual", "p1"))),
        {}, {}, {}, { _, _ -> }, {}, {}, initialSpecialtyId = cardiologyFixture.id)
}

@PreviewTest
@Preview(name = "31 Agendamento sem horarios livres escura", widthDp = 400, heightDp = 1900, locale = "pt-rBR")
@Composable
fun BookingNoTimesDark() = PlainScreen(Appearance.DARK) {
    BookingScreen(booking(emptyList()), {}, {}, {}, { _, _ -> }, {}, {}, initialSpecialtyId = cardiologyFixture.id)
}

@PreviewTest
@Preview(name = "32 Consulta marcada pelo app escura", widthDp = 400, heightDp = 1700, locale = "pt-rBR")
@Composable
fun BookingDoneDark() = PlainScreen(Appearance.DARK) {
    BookingScreen(booking(emptyList(), BookedAppointment(centro.id, PatientAppointment("ap-9", "Cardiologia · fixture visual",
        "Profissional A · fixture visual", at(90_000), at(91_800), "scheduled", bookedBy = "patient")),
        message = "Consulta marcada. Ela já vale e aparece em Consultas."), {}, {}, {}, { _, _ -> }, {}, {})
}

private fun appointmentFixture(id: String, startsIn: Long, confirmedAt: String? = null, bookedBy: String = "staff") =
    PatientAppointment(id, "Cardiologia · fixture visual", "Profissional A · fixture visual", at(startsIn), at(startsIn + 1_800), "scheduled", confirmedAt, bookedBy)

@PreviewTest
@Preview(name = "33 Consultas confirmar presenca e antes da janela clara", widthDp = 400, heightDp = 2000, locale = "pt-rBR")
@Composable
fun AppointmentsPresenceLight() = PlainScreen(Appearance.LIGHT) {
    AppointmentsScreen(connected(emptyList(), appointments = listOf(
        appointmentFixture("ap-1", 86_400 + 3_600, bookedBy = "patient"),
        appointmentFixture("ap-2", 864_000),
        appointmentFixture("ap-3", 3_600))), {}, {}, {}, {})
}

@PreviewTest
@Preview(name = "34 Consulta com presenca confirmada escura", widthDp = 400, heightDp = 1500, locale = "pt-rBR")
@Composable
fun AppointmentsConfirmedDark() = PlainScreen(Appearance.DARK) {
    AppointmentsScreen(connected(emptyList(), appointments = listOf(
        appointmentFixture("ap-1", 86_400 + 3_600, confirmedAt = at(-5_400), bookedBy = "patient"),
        appointmentFixture("ap-2", 172_800 + 7_200, confirmedAt = at(-3_600)).copy(bookedBy = "lightning"))), {}, {}, {}, {})
}

// ---- Avisos, Jornada, Família e e-mail da conta -----------------------------------------------------------

private fun fixtureAnnouncement(id: String, sequence: Long, title: String, body: String, isRead: Boolean = false) =
    Announcement(id, sequence, title, body, at(-3_600 * sequence), null, isRead, if (isRead) at(-1_800) else null)

private val mariaLink = FamilyLink("dep-maria", "approved", norte.id, norte.name, "***.456.789-**", "Maria · fixture visual",
    at(-864_000), at(-800_000), null, null)

private fun communityState(announcements: List<Announcement>?, dependentOffers: List<DependentOffers> = emptyList(),
    offers: List<UnitOffer> = emptyList()) = PatientState(
    address = "https://unidade.example.test", session = session(), units = listOf(centro), selectedUnitId = centro.id,
    unitData = mapOf(centro.id to UnitSnapshot(queue = emptyList(), offers = offers, appointments = emptyList())),
    announcements = AnnouncementsState(announcements.orEmpty(), cursor = 3, loaded = announcements != null),
    family = FamilyState(listOf(mariaLink)), dependentOffers = dependentOffers)

@PreviewTest
@Preview(name = "35 Avisos com comunicados e oferta de dependente clara", widthDp = 400, heightDp = 2500, locale = "pt-rBR")
@Composable
fun NoticesWithAnnouncementsLight() = PlainScreen(Appearance.LIGHT) {
    NoticesScreen(communityState(
        listOf(fixtureAnnouncement("c1", 3, "Campanha de vacinação contra a gripe", "A campanha começa na segunda-feira.\nLeve o cartão de vacinação."),
            fixtureAnnouncement("c2", 2, "Horário especial no feriado", "As UBS atendem das 8h às 12h.", isRead = true),
            fixtureAnnouncement("c3", 1, "Obras na UBS Centro", "A entrada principal ficará fechada. Use a entrada lateral.")),
        dependentOffers = listOf(DependentOffers(mariaLink.id, norte.id, norte.name, "Maria · fixture visual",
            listOf(offer("d1", "Pediatria · fixture visual", 420))))), {}, { _, _ -> }, {}, {})
}

@PreviewTest
@Preview(name = "36 Avisos sem comunicados escura", widthDp = 400, heightDp = 1100, locale = "pt-rBR")
@Composable
fun NoticesEmptyDark() = PlainScreen(Appearance.DARK) { NoticesScreen(communityState(emptyList()), {}, { _, _ -> }, {}, {}) }

private fun journeyState(records: UnitRecords?, failed: Boolean = false, second: UnitRecords? = null) = PatientState(
    address = "https://unidade.example.test", session = session(), units = listOfNotNull(centro, if (second != null) norte else null),
    selectedUnitId = centro.id,
    unitData = mapOf(centro.id to UnitSnapshot(records = records, recordsFailed = failed, attendance = UnitAttendance(3, 0, 0, 0.0, 1.0, 12)))
        + (if (second != null) mapOf(norte.id to UnitSnapshot(records = second)) else emptyMap()))

private val centroRecords = UnitRecords(centro.id, centro.name,
    listOf(ExamRecord("e1", "Hemograma completo · fixture visual", "done", at(-1_000_000), "2026-11-10", "Jejum de 8 horas"),
        ExamRecord("e2", "Raio-X de tórax · fixture visual", "requested", at(-86_400), null, null),
        ExamRecord("e3", "Ultrassom · fixture visual", "cancelled", at(-2_000_000), null, null)),
    listOf(VaccineRecord("v1", "Hepatite B · fixture visual", "2", "2026-09-15", "applied", "Braço esquerdo"),
        VaccineRecord("v2", "Gripe · fixture visual", "single", "2026-05-02", "applied", null),
        VaccineRecord("v3", "Febre amarela · fixture visual", "booster", "2025-01-20", "applied", null)))

@PreviewTest
@Preview(name = "37 Jornada exames com situacao clara", widthDp = 400, heightDp = 2300, locale = "pt-rBR")
@Composable
fun JourneyExamsLight() = PlainScreen(Appearance.LIGHT) {
    JourneyScreen(journeyState(centroRecords, second = UnitRecords(norte.id, norte.name,
        listOf(ExamRecord("e9", "Glicemia · fixture visual", "done", at(-500_000), "2026-11-13", null)), emptyList())), {}, {}, {},
        initialSection = JourneySection.EXAMS)
}

@PreviewTest
@Preview(name = "38 Jornada vacinas com dose e data escura", widthDp = 400, heightDp = 2000, locale = "pt-rBR")
@Composable
fun JourneyVaccinesDark() = PlainScreen(Appearance.DARK) {
    JourneyScreen(journeyState(centroRecords), {}, {}, {}, initialSection = JourneySection.VACCINES)
}

@PreviewTest
@Preview(name = "39 Jornada exames vazio e UBS sem resposta escura", widthDp = 400, heightDp = 1900, locale = "pt-rBR")
@Composable
fun JourneyEmptyDark() = PlainScreen(Appearance.DARK) {
    JourneyScreen(journeyState(UnitRecords(centro.id, centro.name, emptyList(), emptyList()), second = null).let {
        it.copy(units = listOf(centro, norte), unitData = it.unitData + (norte.id to UnitSnapshot(recordsFailed = true)))
    }, {}, {}, {}, initialSection = JourneySection.EXAMS)
}

private val pendingLink = FamilyLink("dep-joao", "pending", centro.id, centro.name, "***.111.222-**", null, at(-86_400), null, null, null)
private val rejectedLink = FamilyLink("dep-ana", "rejected", centro.id, centro.name, "***.333.444-**", null, at(-1_000_000), at(-900_000),
    "Faltou a certidão de nascimento. Traga o documento à UBS.", null)
private val catalogFixture = listOf(RegistrationUnit(centro.id, centro.name, "Centro"), RegistrationUnit(norte.id, norte.name, "Norte"))

private fun familyState(vararg links: FamilyLink, actingFor: String? = null, message: String? = null) = PatientState(
    address = "https://unidade.example.test", session = session(), units = listOf(centro, norte), selectedUnitId = centro.id,
    family = FamilyState(links.toList()), actingFor = actingFor, message = message)

@PreviewTest
@Preview(name = "40 Familia pedidos e estados clara", widthDp = 400, heightDp = 3000, locale = "pt-rBR")
@Composable
fun FamilyListLight() = PlainScreen(Appearance.LIGHT) {
    FamilyScreen(familyState(pendingLink, mariaLink, rejectedLink,
        message = "Pedido enviado. A equipe da UBS vai conferir os documentos e a resposta aparece aqui."), initialCatalog = catalogFixture)
}

@PreviewTest
@Preview(name = "41 Familia agindo por dependente escura", widthDp = 400, heightDp = 2600, locale = "pt-rBR")
@Composable
fun FamilyActingDark() = PlainScreen(Appearance.DARK) {
    FamilyScreen(familyState(mariaLink, actingFor = mariaLink.id), initialCatalog = catalogFixture)
}

private fun actingHome() = PatientState(
    address = "https://unidade.example.test", session = session(), units = listOf(centro, norte), selectedUnitId = centro.id,
    unitData = mapOf(centro.id to UnitSnapshot(queue = emptyList(), offers = emptyList(), appointments = emptyList())),
    family = FamilyState(listOf(mariaLink)), actingFor = mariaLink.id,
    dependentData = mapOf(mariaLink.id to UnitSnapshot(offers = listOf(offer("m1", "Pediatria · fixture visual", 500)),
        queue = listOf(UnitQueueEntry(5, "Pediatria · fixture visual", at(-7_200), "waiting")),
        appointments = listOf(PatientAppointment("ap-m", "Pediatria · fixture visual", "Profissional · fixture visual", at(172_800), at(174_600), "scheduled")))),
    )

@PreviewTest
@Preview(name = "42 Inicio agindo por Maria clara", widthDp = 400, heightDp = 2400, locale = "pt-rBR")
@Composable
fun HomeActingLight() = Fixed {
    NoraPatientContent(UserSettings(Appearance.LIGHT), {}, actingHome())
}

@PreviewTest
@Preview(name = "43 Inicio agindo por Maria escura", widthDp = 400, heightDp = 2400, locale = "pt-rBR")
@Composable
fun HomeActingDark() = Fixed {
    NoraPatientContent(UserSettings(Appearance.DARK), {}, actingHome())
}

private val emailFixture = EmailSuggestion("sug-1", "m***@example.test", centro.id, centro.name, at(-3_600))

@PreviewTest
@Preview(name = "44 Perfil sugestao de e-mail clara", widthDp = 400, heightDp = 1500, locale = "pt-rBR")
@Composable
fun ProfileEmailLight() = PlainScreen(Appearance.LIGHT) {
    ProfileScreen(PatientState(address = "https://unidade.example.test", session = session(), email = EmailState(emailFixture)), {}, {}, {})
}

@PreviewTest
@Preview(name = "45 Perfil codigo enviado escura", widthDp = 400, heightDp = 1700, locale = "pt-rBR")
@Composable
fun ProfileEmailCodeDark() = PlainScreen(Appearance.DARK) {
    ProfileScreen(PatientState(address = "https://unidade.example.test", session = session(),
        email = EmailState(emailFixture, codeSentFor = emailFixture.id),
        message = "Enviamos um código de 6 dígitos para m***@example.test."), {}, {}, {})
}

@PreviewTest
@Preview(name = "46 Inicio com aviso de e-mail da UBS clara", widthDp = 400, heightDp = 2000, locale = "pt-rBR")
@Composable
fun HomeEmailNoticeLight() = Fixed {
    NoraPatientContent(UserSettings(Appearance.LIGHT), {}, connected(emptyList(), appointments = listOf(nextAppointment))
        .copy(email = EmailState(emailFixture)))
}

// ---- Correções da revisão: Avisos sem as UBS e Agendamento por um dependente -------------------------------------

@PreviewTest
@Preview(name = "47 Avisos com UBS indisponiveis e comunicados clara", widthDp = 400, heightDp = 1800, locale = "pt-rBR")
@Composable
fun NoticesUnitsFailedLight() = PlainScreen(Appearance.LIGHT) {
    NoticesScreen(communityState(
        listOf(fixtureAnnouncement("c1", 2, "Campanha de vacinação contra a gripe", "A campanha começa na segunda-feira."),
            fixtureAnnouncement("c2", 1, "Horário especial no feriado", "As UBS atendem das 8h às 12h.", isRead = true)))
        .copy(units = null, selectedUnitId = null, unitData = emptyMap()), {}, { _, _ -> }, {}, {})
}

@PreviewTest
@Preview(name = "48 Agendamento agindo por Maria escura", widthDp = 400, heightDp = 1700, locale = "pt-rBR")
@Composable
fun BookingActingDark() = PlainScreen(Appearance.DARK) {
    BookingScreen(booking().copy(units = listOf(norte), selectedUnitId = norte.id, family = FamilyState(listOf(mariaLink)), actingFor = mariaLink.id),
        {}, {}, {}, { _, _ -> }, {}, {}, actingName = "Maria · fixture visual")
}

// ---- Atendimento da UBS, passos de uma operação e conta Google ---------------------------------------------------

private val aboutFixture = UnitProfile(centro.id, true,
    listOf(OpeningInterval(1, "08:00", "12:00"), OpeningInterval(1, "13:00", "17:00"), OpeningInterval(2, "08:00", "17:00"),
        OpeningInterval(3, "08:00", "12:00"), OpeningInterval(4, "08:00", "17:00"), OpeningInterval(5, "08:00", "12:00"), OpeningInterval(6, "08:00", "11:00")),
    "Vacinação\nCurativos\nTeste do pezinho (agendar na recepção)", listOf(Specialty("s1", "Clínica Geral · fixture"), Specialty("s2", "Pediatria · fixture"),
        Specialty("s3", "Ginecologia · fixture")), at(-3_600))

@PreviewTest
@Preview(name = "49 Atendimento da UBS horario por dia clara", widthDp = 400, heightDp = 1500, locale = "pt-rBR")
@Composable
fun UnitAboutLight() = PlainScreen(Appearance.LIGHT) { UnitAboutCard(centro.name, AboutState(aboutFixture), onRetry = {}, today = 1) }

@PreviewTest
@Preview(name = "50 Atendimento nao informado escura", widthDp = 400, heightDp = 1100, locale = "pt-rBR")
@Composable
fun UnitAboutNotInformedDark() = PlainScreen(Appearance.DARK) {
    UnitAboutCard(centro.name, AboutState(UnitProfile(centro.id, false, emptyList(), null, emptyList(), null)), onRetry = {}, today = 1)
}

@PreviewTest
@Preview(name = "51 Atendimento carregando e falha clara", widthDp = 400, heightDp = 1100, locale = "pt-rBR")
@Composable
fun UnitAboutLoadingAndErrorLight() = PlainScreen(Appearance.LIGHT) {
    UnitAboutCard(centro.name, AboutState(loading = true), onRetry = {}, today = 1)
    UnitAboutCard(norte.name, AboutState(error = "A base de uma UBS está temporariamente indisponível. Tente novamente em instantes."), onRetry = {}, today = 1)
}

@PreviewTest
@Preview(name = "52 Entrada com botao do Google escura", widthDp = 400, heightDp = 1500, locale = "pt-rBR")
@Composable
fun LoginGoogleDark() = Fixed {
    NoraPatientContent(UserSettings(Appearance.DARK), {}, PatientState(address = "https://unidade.example.test"), onLogin = { _, _ -> },
        google = GoogleActions(webClientId = "cliente-web-fixture-visual"))
}

@PreviewTest
@Preview(name = "53 Passos concluido atual e falho clara", widthDp = 400, heightDp = 700, locale = "pt-rBR")
@Composable
fun StepListLight() = PlainScreen(Appearance.LIGHT) {
    StepList(ProgressSteps().begin("Abrindo o Google…").begin("Entrando no NORA…"))
    StepList(ProgressSteps().begin("Abrindo o Google…").begin("Vinculando a conta Google…").fail("Vinculando a conta Google…"))
}

@PreviewTest
@Preview(name = "54 Configuracoes com conta Google vinculada escura", widthDp = 400, heightDp = 2900, locale = "pt-rBR")
@Composable
fun SettingsGoogleLinkedDark() = NoraPatientContent(UserSettings(Appearance.DARK), {},
    connected(emptyList()).copy(google = GoogleState(GoogleStatus(true, true, "paciente@gmail.example"))),
    initiallyShowSettings = true, google = GoogleActions(webClientId = "cliente-web-fixture-visual"))
