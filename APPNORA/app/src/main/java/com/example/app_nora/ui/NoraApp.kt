package com.example.app_nora.ui

import androidx.activity.compose.BackHandler
import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.util.Log
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.listSaver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.foundation.background
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import com.example.app_nora.BuildConfig
import com.example.app_nora.preferences.*
import com.example.app_nora.api.*
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.core.content.ContextCompat
import java.io.IOException
import org.json.JSONException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.launch

@Composable
fun NoraApp(store: SettingsStore, retainedController: PatientController? = null,
    openFlashUnit: String? = null, onFlashOpened: () -> Unit = {}, openFlashDependent: String? = null,
    openNotices: Boolean = false, onNoticesOpened: () -> Unit = {}) {
    val context = LocalContext.current.applicationContext
    val controller = remember(retainedController) { retainedController ?: PatientController(context) }
    val scope = rememberCoroutineScope()
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val session = controller.state.session
    val notificationPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (!granted) Log.w("NORA", "Notificações desativadas pelo usuário; a conexão continua indicada pelo gerenciador de tarefas do Android.")
    }
    // Android 17 (API 37): falar com um endereço da rede local (10.0.2.2 do emulador, servidor da UBS em IP privado) exige
    // ACCESS_LOCAL_NETWORK, permissão de execução. Ela é pedida na hora de usar o serviço antes de entrar (login, cadastro e
    // recuperação de senha); depois de concedida vale para o resto da sessão, inclusive para o serviço de conexão.
    var localNetworkAnswer by remember { mutableStateOf<CompletableDeferred<Boolean>?>(null) }
    val localNetworkPermission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        localNetworkAnswer?.complete(granted)
        localNetworkAnswer = null
    }
    val allowLocalNetwork: suspend () -> Boolean = allow@{
        if (Build.VERSION.SDK_INT < LOCAL_NETWORK_SDK || !ApiAddress.usesLocalNetwork(controller.state.address) ||
            ContextCompat.checkSelfPermission(context, ACCESS_LOCAL_NETWORK) == PackageManager.PERMISSION_GRANTED) return@allow true
        val answer = CompletableDeferred<Boolean>()
        localNetworkAnswer?.cancel()
        localNetworkAnswer = answer
        localNetworkPermission.launch(ACCESS_LOCAL_NETWORK)
        answer.await()
    }
    // Para as chamadas que mostram o próprio erro (cadastro e recuperação): a recusa vira uma falha com a explicação.
    suspend fun <T> withLocalNetwork(call: suspend () -> T): T =
        if (allowLocalNetwork()) call() else throw ApiFailure(0, LOCAL_NETWORK_DENIED_MESSAGE)
    LaunchedEffect(controller, lifecycle, session?.token) {
        if (session == null) {
            PatientPresenceService.stop(context)
            return@LaunchedEffect
        }
        var notificationRequested = false
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            if (!notificationRequested && Build.VERSION.SDK_INT >= 33 &&
                ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                notificationRequested = true
                notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
            }
            PatientPresenceService.start(context, controller)
            // The foreground service owns presence after Home/another app; cancellation here
            // only detaches the Activity and must not mark the patient offline.
            awaitCancellation()
        }
    }
    NoraPatientContent(store.settings, store::update, controller.state,
        onAddressChange = controller::changeAddress,
        onLogin = { identifier, password ->
            scope.launch { if (allowLocalNetwork()) controller.login(identifier, password) else controller.localNetworkDenied() }
        },
        onRefresh = { scope.launch { controller.refreshUnit() } },
        openFlashUnit = openFlashUnit, onFlashOpened = onFlashOpened, openFlashDependent = openFlashDependent,
        openNotices = openNotices, onNoticesOpened = onNoticesOpened,
        family = FamilyActions(onLoad = { scope.launch { controller.loadFamily() } },
            onRequest = controller::requestDependent,
            onCancelRequest = { id -> scope.launch { controller.cancelDependentRequest(id) } },
            onEnd = { id -> scope.launch { controller.endDependent(id) } },
            // A troca é imediata (o estado muda na hora); só a leitura dos dados do dependente é assíncrona.
            onActFor = { id -> if (controller.actFor(id)) scope.launch { controller.loadActingData() } }),
        community = CommunityActions(onLoadAnnouncements = { scope.launch { controller.loadAnnouncements() } },
            onMarkAnnouncementRead = { id -> scope.launch { controller.markAnnouncementRead(id) } },
            onLoadRecords = { scope.launch { controller.loadRecords() } }),
        email = EmailActions(onLoad = { scope.launch { controller.loadEmailSuggestion() } },
            onSendCode = { scope.launch { controller.sendEmailCode() } }, onConfirm = controller::confirmEmailCode,
            onDismiss = { scope.launch { controller.dismissEmailSuggestion() } }),
        onLoadUnits = { scope.launch { controller.loadUnits() } },
        onLoadAppointments = { scope.launch { controller.loadAppointments() } },
        onLoadAttendance = { scope.launch { controller.loadAttendance() } },
        onCancelQueueEntry = { id -> scope.launch { controller.cancelQueueEntry(id) } },
        onCheckUnlink = controller::checkUnlinkImpact,
        onUnlinkUnit = { scope.launch { controller.unlinkSelectedUnit() } },
        onShowOwnUnits = controller::showOwnUnits,
        onChangePassword = controller::changePassword,
        onCancelAppointment = { id -> scope.launch { controller.cancelAppointment(id) } },
        onConfirmPresence = { unitId, id -> scope.launch { controller.confirmPresence(unitId, id) } },
        onOpenBooking = { scope.launch { controller.openBooking() } },
        onLoadAvailability = { specialtyId, week -> scope.launch { controller.loadAvailability(specialtyId, week) } },
        onBookSlot = { slot -> scope.launch { controller.bookAppointment(slot) } },
        onSelectUnit = controller::selectUnit,
        onLocateUnit = controller::selectNearestUnit,
        onRespondUnit = { id, accept -> scope.launch { controller.respondUnitOffer(id, accept) } },
        onLogout = { scope.launch { controller.logout() } },
        onRegistrationUnits = { withLocalNetwork(controller::registrationUnits) },
        onLinkUnits = controller::linkUnits,
        onStartRegistration = { name, cpf, password, units -> withLocalNetwork { controller.startRegistration(name, cpf, password, units) } },
        onSendRegistrationCode = { challenge, option -> withLocalNetwork { controller.sendRegistrationCode(challenge, option) } },
        onConfirmRegistration = { challenge, code -> withLocalNetwork { controller.confirmRegistration(challenge, code) } },
        onRequestPasswordReset = { cpf -> withLocalNetwork { controller.requestPasswordReset(cpf) } },
        onConfirmPasswordReset = { challenge, code, password -> withLocalNetwork { controller.confirmPasswordReset(challenge, code, password) } })
}

/** Permissão do Android 17 (API 37) para a rede local; o nome vai em texto para não depender da constante do SDK. */
private const val ACCESS_LOCAL_NETWORK = "android.permission.ACCESS_LOCAL_NETWORK"
private const val LOCAL_NETWORK_SDK = 37

private enum class PatientDestination(val title: String) {
    HOME("Início"),
    JOURNEY("Jornada"),
    NOTICES("Avisos"),
    FAMILY("Família"),
    PROFILE("Perfil"),
    APPOINTMENTS("Consultas"),
    UNITS("UBS"),
    BOOKING("Agendamento"),
    FLASH("Vaga Relâmpago"),
    REGISTER("Cadastro"),
    PASSWORD("Alterar senha"),
}

/** Telas cujo conteúdo vale para a pessoa escolhida em "Agindo por". */
private val actingDestinations = setOf(PatientDestination.HOME, PatientDestination.JOURNEY, PatientDestination.APPOINTMENTS,
    PatientDestination.BOOKING, PatientDestination.FLASH)

private val mainDestinations = listOf(
    PatientDestination.HOME,
    PatientDestination.JOURNEY,
    PatientDestination.NOTICES,
    PatientDestination.FAMILY,
    PatientDestination.PROFILE,
)

@Composable
private fun PatientNavigationIcon(destination: PatientDestination) {
    val color = LocalContentColor.current
    Canvas(Modifier.size(24.dp)) {
        val unit = size.minDimension / 24f
        val line = Stroke(width = 2f * unit, cap = StrokeCap.Round, join = StrokeJoin.Round)
        fun point(x: Float, y: Float) = Offset(x * unit, y * unit)
        when (destination) {
            PatientDestination.HOME -> {
                val roof = Path().apply {
                    moveTo(3f * unit, 11f * unit)
                    lineTo(12f * unit, 3.5f * unit)
                    lineTo(21f * unit, 11f * unit)
                }
                drawPath(roof, color, style = line)
                val house = Path().apply {
                    moveTo(5.5f * unit, 10f * unit)
                    lineTo(5.5f * unit, 20f * unit)
                    lineTo(18.5f * unit, 20f * unit)
                    lineTo(18.5f * unit, 10f * unit)
                }
                drawPath(house, color, style = line)
                drawLine(color, point(10f, 20f), point(10f, 14f), 2f * unit, line.cap)
                drawLine(color, point(14f, 20f), point(14f, 14f), 2f * unit, line.cap)
                drawLine(color, point(10f, 14f), point(14f, 14f), 2f * unit, line.cap)
            }
            PatientDestination.JOURNEY -> {
                drawLine(color, point(7f, 4f), point(7f, 20f), 2f * unit, line.cap)
                listOf(5f, 12f, 19f).forEach { y ->
                    drawCircle(color, radius = 2.3f * unit, center = point(7f, y))
                    drawLine(color, point(12f, y), point(19f, y), 2f * unit, line.cap)
                }
            }
            PatientDestination.NOTICES -> {
                val bell = Path().apply {
                    moveTo(5f * unit, 17f * unit)
                    lineTo(7f * unit, 14.5f * unit)
                    lineTo(7f * unit, 10f * unit)
                    cubicTo(7f * unit, 3.5f * unit, 17f * unit, 3.5f * unit, 17f * unit, 10f * unit)
                    lineTo(17f * unit, 14.5f * unit)
                    lineTo(19f * unit, 17f * unit)
                    close()
                }
                drawPath(bell, color, style = line)
                drawLine(color, point(4f, 17f), point(20f, 17f), 2f * unit, line.cap)
                drawArc(color, 20f, 140f, false, point(9f, 17.5f), Size(6f * unit, 5f * unit), style = line)
            }
            PatientDestination.FAMILY -> {
                drawCircle(color, radius = 3f * unit, center = point(8f, 8f), style = line)
                drawCircle(color, radius = 3f * unit, center = point(16f, 8f), style = line)
                drawArc(color, 180f, 180f, false, point(2.5f, 11f), Size(11f * unit, 10f * unit), style = line)
                drawArc(color, 180f, 180f, false, point(10.5f, 11f), Size(11f * unit, 10f * unit), style = line)
            }
            PatientDestination.PROFILE -> {
                drawCircle(color, radius = 3.5f * unit, center = point(12f, 7.5f), style = line)
                drawArc(color, 180f, 180f, false, point(4f, 12f), Size(16f * unit, 10f * unit), style = line)
            }
            else -> drawCircle(color, radius = 2f * unit, center = point(12f, 12f), style = line)
        }
    }
}

@Composable
fun NoraPatientContent(settings: UserSettings, onSettingsChange: (UserSettings) -> Unit,
    patient: PatientState = PatientState(), onAddressChange: (String) -> Unit = {},
    onLogin: ((String, String) -> Unit)? = null, onRefresh: () -> Unit = {},
    onRespondUnit: (String, Boolean) -> Unit = { _, _ -> }, onLogout: () -> Unit = {},
    onRegistrationUnits: suspend () -> List<RegistrationUnit> = { throw ApiFailure(503, "Configure o serviço para consultar as unidades.") },
    onLinkUnits: suspend (List<String>) -> UnitLinkResult = { throw ApiFailure(503, "Vínculo indisponível.") },
    onStartRegistration: suspend (String, String, String, List<String>) -> RegistrationStart = { _, _, _, _ -> throw ApiFailure(503, "Cadastro indisponível.") },
    onSendRegistrationCode: suspend (String, String) -> Unit = { _, _ -> throw ApiFailure(503, "Cadastro indisponível.") },
    onConfirmRegistration: suspend (String, String) -> RegistrationResult = { _, _ -> throw ApiFailure(503, "Cadastro indisponível.") },
    onRequestPasswordReset: suspend (String) -> String = { throw ApiFailure(503, "Configure o serviço para recuperar sua senha.") },
    onConfirmPasswordReset: suspend (String, String, String) -> Boolean = { _, _, _ -> false },
    initiallyShowSettings: Boolean = false, onLoadUnits: () -> Unit = {}, onLoadAppointments: () -> Unit = {}, onCancelAppointment: (String) -> Unit = {}, onSelectUnit: (String) -> Unit = {},
    onLocateUnit: (Double, Double) -> PatientHealthUnit? = { _, _ -> null },
    onLoadAttendance: () -> Unit = {}, onCancelQueueEntry: (Long) -> Unit = {},
    onCheckUnlink: suspend (String) -> UnlinkImpact? = { null }, onUnlinkUnit: () -> Unit = {}, onShowOwnUnits: () -> Boolean = { false },
    onChangePassword: suspend (String, String) -> Unit = { _, _ -> throw ApiFailure(503, "Troca de senha indisponível.") },
    onConfirmPresence: (String, String) -> Unit = { _, _ -> }, onOpenBooking: () -> Unit = {},
    onLoadAvailability: (String, Int) -> Unit = { _, _ -> }, onBookSlot: (AvailableSlot) -> Unit = {},
    family: FamilyActions = FamilyActions(), community: CommunityActions = CommunityActions(), email: EmailActions = EmailActions(),
    openFlashUnit: String? = null, onFlashOpened: () -> Unit = {}, openFlashDependent: String? = null,
    openNotices: Boolean = false, onNoticesOpened: () -> Unit = {}) {
    var settingsOpen by rememberSaveable { mutableStateOf(initiallyShowSettings) }
    // A visão de quem o paciente está agindo: ele mesmo, ou o dependente (UBS do vínculo e dados lidos pelas rotas do dependente).
    val view = patient.effective()
    var destination by rememberSaveable { mutableStateOf(PatientDestination.HOME) }
    // Sessão que termina (401, logout ou fechamento) volta ao Início: é lá que o formulário de entrada e a mensagem aparecem.
    var hadSession by rememberSaveable { mutableStateOf(patient.session != null) }
    LaunchedEffect(patient.session == null) {
        if (patient.session != null) hadSession = true
        else if (hadSession) { hadSession = false; destination = PatientDestination.HOME }
    }
    // Pedido de abrir a Vaga Relâmpago de uma oferta: do toque na notificação ou de um botão "Ver oportunidade" (Início e Avisos). Se a
    // oferta é de um dependente, passa a agir por ele (espera a lista de vínculos chegar); se é do paciente, volta a agir por si e escolhe
    // a UBS (espera as UBS carregarem). Com uma ação em andamento (`busy`) o controlador recusa trocar "agindo por" e UBS: então o pedido
    // espera (`busy` é chave do efeito) em vez de abrir a tela na visão de outra pessoa e perder o pedido.
    var offerRequest by remember { mutableStateOf<Pair<String, String?>?>(null) }
    val flashRequest = openFlashUnit?.let { it to openFlashDependent } ?: offerRequest
    LaunchedEffect(flashRequest, patient.session?.token, patient.units, patient.family.links, patient.busy) {
        val (target, dependentId) = flashRequest ?: return@LaunchedEffect
        fun finish() { offerRequest = null; if (openFlashUnit != null) onFlashOpened() }
        when (flashOpenDecision(target, dependentId, patient)) {
            FlashOpen.WAIT -> Unit
            FlashOpen.DROP -> finish()
            FlashOpen.OPEN_DEPENDENT -> {
                // Já agindo por ele, a troca não relê nada: a oferta nova (a da notificação) só aparece relendo a lista dele.
                val already = patient.actingFor == dependentId && destination == PatientDestination.FLASH
                family.onActFor(dependentId)
                settingsOpen = false
                destination = PatientDestination.FLASH
                if (already) onRefresh()
                finish()
            }
            FlashOpen.OPEN_OWN -> {
                family.onActFor(null)
                onSelectUnit(target)
                settingsOpen = false
                destination = PatientDestination.FLASH
                finish()
            }
        }
    }
    // Toque na notificação de comunicado: abre a aba Avisos.
    LaunchedEffect(openNotices, patient.session?.token) {
        if (!openNotices) return@LaunchedEffect
        if (patient.session != null) { settingsOpen = false; destination = PatientDestination.NOTICES }
        onNoticesOpened()
    }
    val pageScroll = rememberScrollState()
    val settingsScroll = rememberScrollState()
    LaunchedEffect(settingsOpen) {
        if (settingsOpen) settingsScroll.scrollTo(0)
    }
    LaunchedEffect(destination) { pageScroll.scrollTo(0) }
    // A tela UBS é do próprio responsável: aberta por qualquer caminho agindo por um dependente, volta a agir por si (antes de reler as
    // UBS) e avisa enquanto a tela estiver aberta.
    var ownUnitsNotice by rememberSaveable { mutableStateOf(false) }
    LaunchedEffect(destination, patient.session?.token) {
        if (destination != PatientDestination.UNITS) ownUnitsNotice = false
        else if (patient.session != null && onShowOwnUnits()) ownUnitsNotice = true
        if ((destination == PatientDestination.UNITS || (destination in listOf(PatientDestination.APPOINTMENTS, PatientDestination.FLASH, PatientDestination.JOURNEY, PatientDestination.BOOKING, PatientDestination.NOTICES) && patient.units == null)) && patient.session != null) onLoadUnits()
    }
    // Ao abrir o agendamento: limpa o resultado anterior e relê as especialidades. Só uma vez por entrada na tela: recriar a Activity
    // (girar, trocar o tema) refaz o efeito, mas o controlador sobrevive e a tela não pode perder o cartão "Consulta marcada".
    var bookingOpened by rememberSaveable { mutableStateOf(false) }
    LaunchedEffect(destination, patient.session?.token) {
        if (destination != PatientDestination.BOOKING) bookingOpened = false
        else if (patient.session != null && !bookingOpened) { bookingOpened = true; onOpenBooking() }
    }
    // A participação e os registros (exames e vacinas) são lidos ao abrir a Jornada, de novo quando as UBS acabam de chegar e quando
    // muda por quem se age; depois, só ao atualizar.
    LaunchedEffect(destination, patient.session?.token, view.units != null, patient.actingFor) {
        if (destination == PatientDestination.JOURNEY && patient.session != null && view.units != null) {
            onLoadAttendance()
            community.onLoadRecords()
        }
    }
    // Leituras completas ao abrir cada aba: comunicados (Avisos), pedidos de família (Família) e sugestão de e-mail (Perfil).
    LaunchedEffect(destination, patient.session?.token) {
        if (patient.session == null) return@LaunchedEffect
        when (destination) {
            PatientDestination.NOTICES -> community.onLoadAnnouncements()
            PatientDestination.FAMILY -> family.onLoad()
            PatientDestination.PROFILE -> email.onLoad()
            else -> Unit
        }
    }
    // O servidor exigiu nova senha (403 password-change-required): leva à troca em vez de deixar o app preso.
    LaunchedEffect(patient.passwordChangeRequired) {
        if (patient.passwordChangeRequired) { settingsOpen = false; destination = PatientDestination.PASSWORD }
    }
    LaunchedEffect(destination, patient.session?.token, view.selectedUnitId) {
        if (destination == PatientDestination.APPOINTMENTS && patient.session != null && view.selectedUnitId != null) onLoadAppointments()
        if (destination == PatientDestination.FLASH && patient.session != null && view.selectedUnitId != null) onRefresh()
    }
    BackHandler(enabled = settingsOpen || destination != PatientDestination.HOME) {
        if (settingsOpen) settingsOpen = false else destination = PatientDestination.HOME
    }
    NoraTheme(settings.appearance) {
        Scaffold(
            containerColor = MaterialTheme.colorScheme.background,
            bottomBar = {
                if (patient.session != null) {
                    NavigationBar(containerColor = MaterialTheme.colorScheme.surface,
                        tonalElevation = 8.dp) {
                        mainDestinations.forEach { item ->
                            NavigationBarItem(
                                selected = !settingsOpen && destination == item,
                                onClick = { settingsOpen = false; destination = item },
                                icon = { PatientNavigationIcon(item) },
                                label = { Text(item.title, maxLines = 1) },
                                colors = NavigationBarItemDefaults.colors(
                                    indicatorColor = MaterialTheme.colorScheme.primaryContainer,
                                    selectedIconColor = MaterialTheme.colorScheme.primary,
                                    selectedTextColor = MaterialTheme.colorScheme.primary,
                                    unselectedIconColor = MaterialTheme.colorScheme.onSurfaceVariant,
                                    unselectedTextColor = MaterialTheme.colorScheme.onSurfaceVariant,
                                ),
                            )
                        }
                    }
                }
            }
        ) { padding ->
            Box(
                Modifier.fillMaxSize().padding(padding).background(
                    Brush.verticalGradient(listOf(
                        MaterialTheme.colorScheme.background,
                        MaterialTheme.colorScheme.primaryContainer.copy(alpha = .18f),
                        MaterialTheme.colorScheme.tertiaryContainer.copy(alpha = .12f),
                        MaterialTheme.colorScheme.background,
                    )),
                ),
                contentAlignment = Alignment.TopCenter,
            ) {
                Column(
                    Modifier.widthIn(max = 640.dp).fillMaxWidth().fillMaxHeight()
                        .verticalScroll(if (settingsOpen) settingsScroll else pageScroll)
                        .padding(horizontal = 20.dp, vertical = 24.dp),
                    verticalArrangement = Arrangement.spacedBy(if (settings.comfortableSpacing) 20.dp else 12.dp),
                ) {
                    if (settingsOpen) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            IconButton(onClick = { settingsOpen = false }) {
                                Icon(Icons.AutoMirrored.Filled.ArrowBack, "Voltar ao início")
                            }
                            Text("Configurações", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold)
                        }
                        ScreenTitle("Seu app, do seu jeito", "Escolha a aparência e ajuste as preferências deste dispositivo.")
                        SettingsScreen(settings, onSettingsChange, patient, onAddressChange, onChangePassword = {
                            settingsOpen = false
                            destination = PatientDestination.PASSWORD
                        }, onLogout = {
                            settingsOpen = false
                            destination = PatientDestination.HOME
                            onLogout()
                        })
                    } else {
                        if (destination in listOf(PatientDestination.APPOINTMENTS, PatientDestination.UNITS,
                                PatientDestination.BOOKING, PatientDestination.FLASH, PatientDestination.PASSWORD)) {
                            TextButton(onClick = { destination = PatientDestination.HOME }) {
                                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = null)
                                Spacer(Modifier.width(8.dp))
                                Text("Voltar ao início")
                            }
                        }
                        // As telas que agem por alguém dizem sempre por quem (faixa) e usam a visão dessa pessoa.
                        if (patient.session != null && destination in actingDestinations) ActingBanner(patient, family.onActFor)
                        when (destination) {
                        PatientDestination.HOME -> HomeScreen(settings.showGuidance,
                            onOpenSettings = { settingsOpen = true }, onNavigate = { destination = it },
                            patient = view, onAddressChange = onAddressChange, onLogin = onLogin,
                            onRefresh = onRefresh, onLoadUnits = onLoadUnits, onLeaveQueue = onCancelQueueEntry,
                            onConfirmPresence = onConfirmPresence,
                            // O Início mostra as ofertas da visão em tela: agindo por um dependente, as dele.
                            onOpenOffer = { unitId -> offerRequest = unitId to patient.actingFor },
                            onOpenDependentOffer = { dependentId, unitId -> offerRequest = unitId to dependentId },
                            onOpenProfile = { destination = PatientDestination.PROFILE },
                            onRequestPasswordReset = onRequestPasswordReset,
                            onConfirmPasswordReset = onConfirmPasswordReset)
                        PatientDestination.JOURNEY -> JourneyScreen(view, onOpenAppointments = { destination = PatientDestination.APPOINTMENTS },
                            onRefreshAttendance = onLoadAttendance, onLeaveQueue = onCancelQueueEntry, onLoadRecords = community.onLoadRecords)
                        PatientDestination.NOTICES -> NoticesScreen(patient,
                            onOpenOffer = { unitId -> offerRequest = unitId to null },
                            onOpenDependentOffer = { dependentId, unitId -> offerRequest = unitId to dependentId },
                            onMarkRead = community.onMarkAnnouncementRead, onReload = community.onLoadAnnouncements, onLoadUnits = onLoadUnits)
                        PatientDestination.FAMILY -> FamilyScreen(patient, family, onRegistrationUnits)
                        PatientDestination.PROFILE -> ProfileScreen(patient,
                            onOpenSettings = { settingsOpen = true }, onChangePassword = { destination = PatientDestination.PASSWORD },
                            onLogout = {
                                destination = PatientDestination.HOME
                                onLogout()
                            }, email = email)
                        PatientDestination.APPOINTMENTS -> AppointmentsScreen(view, onSelectUnit, onLoadUnits, onLoadAppointments, onCancelAppointment,
                            onConfirmPresence, onBook = { destination = PatientDestination.BOOKING })
                        PatientDestination.UNITS -> UnitsScreen(patient, ownUnitsNotice, onLoadUnits, onSelectUnit, onLocateUnit, onRegistrationUnits, onLinkUnits, onCheckUnlink, onUnlinkUnit)
                        PatientDestination.BOOKING -> BookingScreen(view, onSelectUnit, onLoadUnits, onOpenBooking, onLoadAvailability, onBookSlot,
                            onOpenAppointments = { destination = PatientDestination.APPOINTMENTS },
                            actingName = patient.actingLink()?.let(::dependentDisplayName))
                        PatientDestination.FLASH -> FlashScreen(view, onLoadUnits, onSelectUnit, onRefresh, onRespondUnit)
                        PatientDestination.PASSWORD -> ChangePasswordScreen(patient.passwordChangeRequired, onChangePassword)
                        PatientDestination.REGISTER -> RegisterScreen(onBack = { destination = PatientDestination.HOME }, onLoadUnits = onRegistrationUnits, onStart = onStartRegistration, onSend = onSendRegistrationCode, onConfirm = onConfirmRegistration)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun LoginCard(patient: PatientState, onAddressChange: (String) -> Unit,
    onLogin: (String, String) -> Unit, onRegister: () -> Unit,
    onOpenSettings: () -> Unit,
    onRequestPasswordReset: suspend (String) -> String,
    onConfirmPasswordReset: suspend (String, String, String) -> Boolean) {
    var identifier by rememberSaveable { mutableStateOf("") }
    // Senhas nunca entram em estado salvo: após girar a tela a pessoa digita de novo.
    var password by remember { mutableStateOf("") }
    var connectionSettingsOpen by rememberSaveable { mutableStateOf(false) }
    var showPassword by rememberSaveable { mutableStateOf(false) }
    var recoveryOpen by rememberSaveable { mutableStateOf(false) }
    var googleOpen by rememberSaveable { mutableStateOf(false) }
    var helpOpen by rememberSaveable { mutableStateOf(false) }
    var localMessage by rememberSaveable { mutableStateOf<String?>(null) }
    var recoveryCodeStep by rememberSaveable { mutableStateOf(false) }
    // O desafio da recuperação sobrevive à rotação: refazer o pedido gastaria o limite de envios.
    var recoveryCpf by rememberSaveable { mutableStateOf("") }
    var recoveryChallengeId by rememberSaveable { mutableStateOf("") }
    var recoveryCode by remember { mutableStateOf("") }
    var recoveryPassword by remember { mutableStateOf("") }
    var recoveryPasswordConfirmation by remember { mutableStateOf("") }
    var recoveryPasswordVisible by rememberSaveable { mutableStateOf(false) }
    var recoveryBusy by rememberSaveable { mutableStateOf(false) }
    var recoveryMessage by rememberSaveable { mutableStateOf<String?>(null) }
    var invalidCodeOpen by rememberSaveable { mutableStateOf(false) }
    var recoverySuccessOpen by rememberSaveable { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val canSubmit = !patient.busy && identifier.isNotBlank() && password.isNotBlank()
    val requestRecoveryCode: () -> Unit = {
        if (recoveryCpf.isBlank()) {
            recoveryMessage = "Digite o CPF da conta."
        } else if (!isValidCpf(recoveryCpf)) {
            recoveryMessage = "Confira o CPF informado."
        } else {
            recoveryBusy = true
            recoveryMessage = null
            scope.launch {
                try {
                    recoveryChallengeId = onRequestPasswordReset(recoveryCpf)
                    recoveryCodeStep = true
                    recoveryCode = ""
                    recoveryMessage = "Se houver uma conta vinculada, enviaremos um código ao e-mail cadastrado. Ele vale por 10 minutos."
                } catch (failure: ApiFailure) {
                    recoveryMessage = failure.message
                } catch (_: IOException) {
                    recoveryMessage = "Não foi possível conectar à rede. Confira sua conexão e tente novamente."
                } catch (failure: IllegalArgumentException) {
                    recoveryMessage = failure.message ?: "Confira o CPF e o endereço do serviço."
                } catch (_: JSONException) {
                    recoveryMessage = INCOMPATIBLE_RESPONSE_MESSAGE
                } finally {
                    recoveryBusy = false
                }
            }
        }
    }
    val submitPasswordReset: () -> Unit = {
        when {
            recoveryPassword.length < 8 -> recoveryMessage = "A nova senha precisa ter pelo menos 8 caracteres."
            recoveryPassword != recoveryPasswordConfirmation -> recoveryMessage = "As senhas não correspondem."
            recoveryChallengeId.isBlank() -> recoveryMessage = "Solicite um novo código para continuar."
            else -> {
                recoveryBusy = true
                recoveryMessage = null
                scope.launch {
                    try {
                        val changed = onConfirmPasswordReset(recoveryChallengeId, recoveryCode, recoveryPassword)
                        if (changed) {
                            recoveryOpen = false
                            recoverySuccessOpen = true
                            password = ""
                            recoveryCode = ""
                            recoveryPassword = ""
                            recoveryPasswordConfirmation = ""
                        } else {
                            invalidCodeOpen = true
                        }
                    } catch (failure: ApiFailure) {
                        recoveryMessage = failure.message
                    } catch (_: IOException) {
                        recoveryMessage = "Não foi possível conectar à rede. Tente novamente."
                    } catch (failure: IllegalArgumentException) {
                        recoveryMessage = failure.message ?: "Confira o endereço do serviço."
                    } catch (_: JSONException) {
                        recoveryMessage = INCOMPATIBLE_RESPONSE_MESSAGE
                    } finally {
                        recoveryBusy = false
                    }
                }
            }
        }
    }

    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Surface(shape = RoundedCornerShape(18.dp), color = Color.Transparent, shadowElevation = 8.dp) {
                Text("N", Modifier.background(Brush.linearGradient(listOf(Color(0xFF082C51), Color(0xFF1674B5))))
                    .padding(horizontal = 17.dp, vertical = 8.dp),
                    style = MaterialTheme.typography.headlineSmall, color = Color.White, fontWeight = FontWeight.ExtraBold)
            }
            Column(verticalArrangement = Arrangement.spacedBy(1.dp)) {
                Text("NORA", style = MaterialTheme.typography.titleLarge,
                    color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.ExtraBold)
            }
        }
        IconButton(onClick = { helpOpen = true },
            modifier = Modifier.semantics { contentDescription = "Ajuda com o aplicativo" }) {
            Box(Modifier.size(24.dp).border(2.dp, MaterialTheme.colorScheme.primary, CircleShape),
                contentAlignment = Alignment.Center) {
                Text("?", color = MaterialTheme.colorScheme.primary,
                    style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold)
            }
        }
    }
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text("Sua saúde, em um só lugar", style = MaterialTheme.typography.headlineSmall,
            color = MaterialTheme.colorScheme.onBackground, fontWeight = FontWeight.Bold)
        Text("Entre para acompanhar seus cuidados e avisos da UBS.",
            style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }

    Card(Modifier.fillMaxWidth(), shape = RoundedCornerShape(26.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant),
        elevation = CardDefaults.cardElevation(defaultElevation = 5.dp)) {
        Column(Modifier.fillMaxWidth().padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text("Acessar minha conta", style = MaterialTheme.typography.titleLarge,
                    color = MaterialTheme.colorScheme.onSurface, fontWeight = FontWeight.Bold)
                Text("Use o CPF ou e-mail cadastrado na unidade.",
                    style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            OutlinedTextField(value = identifier, onValueChange = { identifier = it; localMessage = null },
                label = { Text("CPF ou e-mail") }, placeholder = { Text("Digite seu CPF ou e-mail") },
                singleLine = true, enabled = !patient.busy, modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp), keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Email, imeAction = ImeAction.Next))
            OutlinedTextField(value = password, onValueChange = { password = it; localMessage = null },
                label = { Text("Senha") }, placeholder = { Text("Digite sua senha") },
                visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
                singleLine = true, enabled = !patient.busy, modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp), keyboardOptions = KeyboardOptions(
                    keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
                trailingIcon = { PasswordEyeButton(showPassword) { showPassword = !showPassword } })
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                TextButton(onClick = {
                    recoveryOpen = true
                    recoveryCodeStep = false
                    recoveryChallengeId = ""
                    recoveryCode = ""
                    recoveryPassword = ""
                    recoveryPasswordConfirmation = ""
                    recoveryMessage = null
                }) { Text("Esqueci a senha") }
            }
            if (localMessage != null) Text(localMessage!!, color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.bodySmall)
            if (patient.message != null) Text(patient.message, color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.bodySmall)
            Button(onClick = {
                if (!identifier.contains('@') && !isValidCpf(identifier.trim())) {
                    localMessage = "Confira o CPF informado ou digite seu e-mail."
                } else if (patient.address.isBlank()) {
                    connectionSettingsOpen = true
                    localMessage = "Informe o endereço da sua UBS para continuar."
                } else {
                    // A senha sai do formulário no envio: o botão desliga na hora (um segundo toque antes de `busy` chegar não
                    // repete o login) e a senha não fica na tela depois de uma falha.
                    onLogin(identifier, password)
                    password = ""
                }
            }, enabled = canSubmit, modifier = Modifier.fillMaxWidth().height(54.dp),
                shape = RoundedCornerShape(16.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF126BB0), contentColor = Color.White)) {
                if (patient.busy) {
                    CircularProgressIndicator(Modifier.size(20.dp), color = MaterialTheme.colorScheme.onPrimary,
                        strokeWidth = 2.dp)
                    Spacer(Modifier.width(10.dp))
                }
                Text(if (patient.busy) "Entrando…" else "Entrar", fontWeight = FontWeight.Bold)
            }
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                HorizontalDivider(Modifier.weight(1f), color = MaterialTheme.colorScheme.outlineVariant)
                Text("ou", color = MaterialTheme.colorScheme.onSurfaceVariant,
                    style = MaterialTheme.typography.bodySmall)
                HorizontalDivider(Modifier.weight(1f), color = MaterialTheme.colorScheme.outlineVariant)
            }
            OutlinedButton(onClick = { googleOpen = true }, modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(16.dp),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.primary),
                border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant)) {
                Text("G", color = Color(0xFF4285F4), fontWeight = FontWeight.ExtraBold,
                    style = MaterialTheme.typography.titleLarge)
                Spacer(Modifier.width(10.dp))
                Text("Continuar com Google", fontWeight = FontWeight.SemiBold)
            }
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.Center) {
                Text("Primeiro acesso?", color = MaterialTheme.colorScheme.onSurfaceVariant)
                TextButton(onClick = onRegister) { Text("Criar conta", fontWeight = FontWeight.Bold) }
            }
        }
    }

    Surface(shape = RoundedCornerShape(18.dp), color = MaterialTheme.colorScheme.primaryContainer.copy(alpha = .7f)) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
            Text("Precisa conectar à sua unidade?", style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onPrimaryContainer)
            TextButton(onClick = { connectionSettingsOpen = !connectionSettingsOpen }) {
                Text(if (connectionSettingsOpen) "Fechar" else "Configurar", fontWeight = FontWeight.Bold)
            }
        }
    }
    if (connectionSettingsOpen) SectionCard("Conexão da UBS", "Use o endereço fornecido pela unidade") {
        OutlinedTextField(value = patient.address, onValueChange = onAddressChange,
            label = { Text("Endereço do serviço NORA") }, placeholder = { Text("https://...") },
            singleLine = true, enabled = !patient.busy, modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(16.dp),
            supportingText = { Text(if (BuildConfig.DEBUG) "No emulador local também pode usar http://10.0.2.2:5080" else "Informe o endereço HTTPS fornecido pela unidade") })
    }

    if (helpOpen) AlertDialog(
        onDismissRequest = { helpOpen = false },
        containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Ajuda com o aplicativo") },
        text = { Text("Confira sua conexão e se o endereço do serviço informado pela UBS está correto. Se o problema continuar, procure sua unidade de saúde.") },
        confirmButton = { TextButton(onClick = { helpOpen = false }) { Text("Entendi") } },
        dismissButton = { TextButton(onClick = { helpOpen = false; onOpenSettings() }) { Text("Configurações") } },
    )
    if (recoveryOpen) AlertDialog(
        onDismissRequest = { if (!recoveryBusy) recoveryOpen = false },
        containerColor = MaterialTheme.colorScheme.surface,
        title = { Text(if (recoveryCodeStep) "Verificar código" else "Recuperar senha") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                if (!recoveryCodeStep) {
                    Text("Digite o CPF da conta. Se houver um cadastro, enviaremos um código ao e-mail associado.")
                    OutlinedTextField(value = recoveryCpf, onValueChange = {
                        recoveryCpf = it.filter { char -> char.isDigit() || char in ".- " }.take(14)
                        recoveryMessage = null
                    }, label = { Text("CPF") }, placeholder = { Text("000.000.000-00") },
                        singleLine = true, enabled = !recoveryBusy, modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp), keyboardOptions = KeyboardOptions(
                            keyboardType = KeyboardType.Number, imeAction = ImeAction.Done))
                } else {
                    Text("Informe o código recebido por e-mail e escolha uma nova senha.")
                    OutlinedTextField(value = recoveryCode, onValueChange = {
                        recoveryCode = it.filter { digit -> digit in '0'..'9' }.take(6)
                        recoveryMessage = null
                    }, label = { Text("Código de 6 dígitos") }, singleLine = true,
                        enabled = !recoveryBusy, modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp), keyboardOptions = KeyboardOptions(
                            keyboardType = KeyboardType.Number, imeAction = ImeAction.Next))
                    OutlinedTextField(value = recoveryPassword, onValueChange = {
                        recoveryPassword = it; recoveryMessage = null
                    }, label = { Text("Nova senha") }, singleLine = true,
                        visualTransformation = if (recoveryPasswordVisible) VisualTransformation.None else PasswordVisualTransformation(),
                        enabled = !recoveryBusy, modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp), keyboardOptions = KeyboardOptions(
                            keyboardType = KeyboardType.Password, imeAction = ImeAction.Next),
                        trailingIcon = { PasswordEyeButton(recoveryPasswordVisible) {
                            recoveryPasswordVisible = !recoveryPasswordVisible
                        } })
                    OutlinedTextField(value = recoveryPasswordConfirmation, onValueChange = {
                        recoveryPasswordConfirmation = it; recoveryMessage = null
                    }, label = { Text("Confirme a nova senha") }, singleLine = true,
                        visualTransformation = PasswordVisualTransformation(), enabled = !recoveryBusy,
                        modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(14.dp),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password,
                            imeAction = ImeAction.Done))
                }
                if (recoveryMessage != null) Text(recoveryMessage!!,
                    color = if (recoveryCodeStep && recoveryMessage!!.startsWith("Se houver"))
                        MaterialTheme.colorScheme.onSurfaceVariant else MaterialTheme.colorScheme.error,
                    style = MaterialTheme.typography.bodySmall)
                if (recoveryBusy) LinearProgressIndicator(Modifier.fillMaxWidth())
            }
        },
        confirmButton = {
            TextButton(onClick = if (recoveryCodeStep) submitPasswordReset else requestRecoveryCode,
                enabled = !recoveryBusy && (!recoveryCodeStep || recoveryCode.length == 6)) {
                Text(if (recoveryBusy) "Aguarde…" else if (recoveryCodeStep) "Alterar senha" else "Enviar código")
            }
        },
        dismissButton = { TextButton(onClick = { if (!recoveryBusy) recoveryOpen = false }) { Text("Cancelar") } },
    )
    if (invalidCodeOpen) AlertDialog(
        onDismissRequest = { invalidCodeOpen = false },
        containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Código inválido") },
        text = { Text("Confira os 6 dígitos ou solicite outro código. Você também pode informar novamente o CPF.") },
        confirmButton = { TextButton(onClick = {
            invalidCodeOpen = false
            recoveryCodeStep = false
            recoveryChallengeId = ""
            recoveryCode = ""
            recoveryPassword = ""
            recoveryPasswordConfirmation = ""
            recoveryMessage = null
        }) { Text("Digitar CPF novamente") } },
        dismissButton = { TextButton(onClick = {
            invalidCodeOpen = false
            recoveryMessage = null
            recoveryCode = ""
            requestRecoveryCode()
        }) { Text("Reenviar código") } },
    )
    if (recoverySuccessOpen) AlertDialog(
        onDismissRequest = { recoverySuccessOpen = false },
        containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Senha alterada") },
        text = { Text("Sua senha foi atualizada. Entre com a nova senha para continuar.") },
        confirmButton = { TextButton(onClick = { recoverySuccessOpen = false }) { Text("Ir para o login") } },
    )
    if (googleOpen) AlertDialog(
        onDismissRequest = { googleOpen = false },
        containerColor = MaterialTheme.colorScheme.surface,
        title = { Text("Entrar com Google") },
        text = { Text("O acesso com Google ainda não foi habilitado pela rede da sua UBS. Por enquanto, use o CPF ou e-mail e a senha cadastrados na unidade.") },
        confirmButton = { TextButton(onClick = { googleOpen = false }) { Text("Entendi") } },
    )
}

@Composable
internal fun PasswordEyeButton(visible: Boolean, onClick: () -> Unit) {
    val iconColor = MaterialTheme.colorScheme.primary
    IconButton(onClick = onClick, modifier = Modifier.semantics {
        contentDescription = if (visible) "Ocultar senha" else "Mostrar senha"
    }) {
        Canvas(Modifier.size(22.dp)) {
            val unit = size.minDimension / 24f
            val eye = Path().apply {
                moveTo(2f * unit, 12f * unit)
                cubicTo(4.5f * unit, 8f * unit, 8f * unit, 5f * unit, 12f * unit, 5f * unit)
                cubicTo(16f * unit, 5f * unit, 19.5f * unit, 8f * unit, 22f * unit, 12f * unit)
                cubicTo(19.5f * unit, 16f * unit, 16f * unit, 19f * unit, 12f * unit, 19f * unit)
                cubicTo(8f * unit, 19f * unit, 4.5f * unit, 16f * unit, 2f * unit, 12f * unit)
            }
            drawPath(eye, iconColor, style = Stroke(2f * unit, cap = StrokeCap.Round, join = StrokeJoin.Round))
            drawCircle(iconColor, radius = 2.5f * unit, center = Offset(12f * unit, 12f * unit))
            if (!visible) drawLine(iconColor, Offset(4f * unit, 20f * unit),
                Offset(20f * unit, 4f * unit), 2.2f * unit, cap = StrokeCap.Round)
        }
    }
}

private val stringListSaver = listSaver<List<String>, String>(save = { it }, restore = { it })
private val emailOptionsSaver = listSaver<List<RegistrationEmailOption>, String>(
    save = { options -> options.flatMap { listOf(it.id, it.maskedEmail) } },
    restore = { saved -> saved.chunked(2).map { RegistrationEmailOption(it[0], it[1]) } })

@Composable
private fun RegisterScreen(onBack: () -> Unit, onLoadUnits: suspend () -> List<RegistrationUnit>,
    onStart: suspend (String, String, String, List<String>) -> RegistrationStart,
    onSend: suspend (String, String) -> Unit, onConfirm: suspend (String, String) -> RegistrationResult) {
    // O formulário e o desafio sobrevivem à rotação; a senha não: ela nunca entra em estado salvo.
    var name by rememberSaveable { mutableStateOf("") }
    var cpf by rememberSaveable { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var showPassword by rememberSaveable { mutableStateOf(false) }
    var units by remember { mutableStateOf<List<RegistrationUnit>?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    var loadingUnits by remember { mutableStateOf(false) }
    var selectedUnits by rememberSaveable(stateSaver = stringListSaver) { mutableStateOf(emptyList<String>()) }
    var challenge by rememberSaveable { mutableStateOf<String?>(null) }
    var options by rememberSaveable(stateSaver = emailOptionsSaver) { mutableStateOf(emptyList<RegistrationEmailOption>()) }
    var selectedOption by rememberSaveable { mutableStateOf<String?>(null) }
    var code by rememberSaveable { mutableStateOf("") }
    var codeSent by rememberSaveable { mutableStateOf(false) }
    var noMatches by rememberSaveable { mutableStateOf(false) }
    var completed by rememberSaveable { mutableStateOf(false) }
    var linkedIds by rememberSaveable(stateSaver = stringListSaver) { mutableStateOf(emptyList<String>()) }
    var unmatchedIds by rememberSaveable(stateSaver = stringListSaver) { mutableStateOf(emptyList<String>()) }
    val scope = rememberCoroutineScope()
    suspend fun runRegistration(action: suspend () -> Unit) {
        if (busy) return
        busy = true; error = null
        try { action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (failure: ApiFailure) { error = failure.message }
        catch (invalid: IllegalArgumentException) { error = invalid.message ?: "Confira os dados informados." }
        catch (_: IOException) { error = "Não foi possível conectar ao serviço. Verifique sua conexão." }
        catch (_: org.json.JSONException) { error = "O serviço retornou uma resposta incompatível." }
        finally { busy = false }
    }
    suspend fun loadUnits() {
        loadingUnits = true
        try { runRegistration { units = onLoadUnits() } }
        finally { loadingUnits = false }
    }
    LaunchedEffect(Unit) { loadUnits() }
    Row(verticalAlignment = Alignment.CenterVertically) {
        IconButton(onClick = onBack, enabled = !busy) { Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Voltar") }
        Text("Criar conta", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold)
    }
    SectionCard("Seus dados", "Use os dados do titular cadastrados na UBS") {
        val editing = challenge == null && !busy
        OutlinedTextField(name, { name = it }, enabled = editing, label = { Text("Nome completo") }, singleLine = true,
            modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp), keyboardOptions = KeyboardOptions(imeAction = ImeAction.Next))
        OutlinedTextField(cpf, { cpf = it }, enabled = editing, label = { Text("CPF") }, singleLine = true,
            modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp),
            isError = cpf.isNotBlank() && cpf.count { it in '0'..'9' } >= 11 && !isValidCpf(cpf),
            supportingText = { if (cpf.isNotBlank() && cpf.count { it in '0'..'9' } >= 11 && !isValidCpf(cpf)) Text("Confira o CPF informado.") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Next))
        OutlinedTextField(value = password, onValueChange = { password = it }, enabled = editing, label = { Text("Senha") },
            visualTransformation = if (showPassword) VisualTransformation.None else PasswordVisualTransformation(),
            singleLine = true, modifier = Modifier.fillMaxWidth(), shape = RoundedCornerShape(16.dp),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Done),
            trailingIcon = { PasswordEyeButton(showPassword) { showPassword = !showPassword } })
        Text("Unidades em que você possui cadastro", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
        if (loadingUnits) MutedText("Carregando unidades…")
        if (!loadingUnits && units == null) TextButton(onClick = { scope.launch { loadUnits() } }, enabled = !busy) { Text("Tentar novamente") }
        if (units?.isEmpty() == true) MutedText("Nenhuma UBS disponível no momento.")
        units?.forEach { unit ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                Checkbox(checked = unit.id in selectedUnits, enabled = editing, onCheckedChange = { checked ->
                    if (checked && unit.id !in selectedUnits) selectedUnits = selectedUnits + unit.id
                    if (!checked) selectedUnits = selectedUnits - unit.id
                }, modifier = Modifier.semantics { contentDescription = "Selecionar ${unit.name}" })
                Column { Text(unit.name); MutedText(unit.region) }
            }
        }
        if (challenge == null) {
            Button(onClick = { scope.launch { runRegistration {
                val response = onStart(name.trim(), cpf, password, selectedUnits)
                if (response.challengeId == null || response.emails.isEmpty()) noMatches = true
                else { challenge = response.challengeId; options = response.emails; password = "" }
            } } }, enabled = !busy && name.trim().length >= 2 && isValidCpf(cpf) && password.length >= 8 && selectedUnits.isNotEmpty(),
                modifier = Modifier.fillMaxWidth().height(52.dp), shape = RoundedCornerShape(16.dp)) { Text("Criar conta", fontWeight = FontWeight.Bold) }
        }
        error?.let { MutedText(it) }
    }
    if (challenge != null) SectionCard("Confirme seu e-mail", "Escolha um e-mail cadastrado em uma das UBS para receber o código") {
        options.forEach { option ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                RadioButton(selected = selectedOption == option.id, enabled = !busy, onClick = { selectedOption = option.id; codeSent = false; code = "" })
                Text(option.maskedEmail)
            }
        }
        Button(onClick = { scope.launch { runRegistration { onSend(challenge!!, selectedOption!!); codeSent = true; code = "" } } },
            enabled = !busy && selectedOption != null, modifier = Modifier.fillMaxWidth()) { Text(if (codeSent) "Reenviar código" else "Enviar código") }
        if (codeSent) {
            OutlinedTextField(code, { code = it.filter { digit -> digit in '0'..'9' }.take(6) }, enabled = !busy, label = { Text("Código de 6 dígitos") },
                singleLine = true, modifier = Modifier.fillMaxWidth(), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number))
            Button(onClick = { scope.launch { runRegistration {
                val result = onConfirm(challenge!!, code)
                if (result.codeValid) { linkedIds = result.linkedUnits; unmatchedIds = result.unmatchedUnits; completed = true } else error = "Código incorreto, expirado ou já utilizado."
            } } }, enabled = !busy && code.length == 6, modifier = Modifier.fillMaxWidth()) { Text("Confirmar cadastro") }
        }
        error?.let { MutedText(it) }
        MutedText("O e-mail confirmado será usado para acessar e recuperar sua conta.")
    }
    if (noMatches) AlertDialog(onDismissRequest = { noMatches = false }, title = { Text("Cadastro não encontrado") },
        text = { Text("Não encontramos seu CPF nas UBS selecionadas. Confira as unidades ou procure a equipe da UBS.") },
        confirmButton = { TextButton(onClick = { noMatches = false }) { Text("Entendi") } })
    if (completed) {
        fun names(ids: List<String>) = ids.map { id -> units?.firstOrNull { it.id == id }?.name ?: "UBS" }.joinToString(", ")
        val linked = names(linkedIds)
        val missing = names(unmatchedIds)
        AlertDialog(onDismissRequest = onBack, title = { Text("Conta criada") },
            text = { Text("UBS vinculadas: $linked." + if (unmatchedIds.isNotEmpty())"\n\nNão encontramos seu cadastro em: $missing. Procure a equipe dessas unidades." else "") },
            confirmButton = { TextButton(onClick = onBack) { Text("Ir para o login") } })
    }
}

@Composable
private fun HomeScreen(showGuidance: Boolean, onOpenSettings: () -> Unit,
    onNavigate: (PatientDestination) -> Unit, patient: PatientState,
    onAddressChange: (String) -> Unit, onLogin: ((String, String) -> Unit)?,
    onRefresh: () -> Unit, onLoadUnits: () -> Unit, onLeaveQueue: (Long) -> Unit, onOpenOffer: (String) -> Unit,
    onOpenDependentOffer: (String, String) -> Unit, onOpenProfile: () -> Unit,
    onConfirmPresence: (String, String) -> Unit,
    onRequestPasswordReset: suspend (String) -> String,
    onConfirmPasswordReset: suspend (String, String, String) -> Boolean) {
    if (patient.session == null && onLogin != null) {
        LoginCard(patient, onAddressChange, onLogin,
            onRegister = { onNavigate(PatientDestination.REGISTER) },
            onOpenSettings = onOpenSettings,
            onRequestPasswordReset = onRequestPasswordReset,
            onConfirmPasswordReset = onConfirmPasswordReset)
        return
    }
    if (patient.session == null) {
        val heroShape = RoundedCornerShape(30.dp)
        Card(
            modifier = Modifier.fillMaxWidth(),
            shape = heroShape,
            colors = CardDefaults.cardColors(containerColor = Color.Transparent),
            elevation = CardDefaults.cardElevation(defaultElevation = 12.dp),
                    border = BorderStroke(1.dp, MaterialTheme.colorScheme.onPrimary.copy(alpha = .12f)),
        ) {
            Box(Modifier.fillMaxWidth().background(
                Brush.linearGradient(listOf(MaterialTheme.colorScheme.primary,
                    MaterialTheme.colorScheme.secondary, MaterialTheme.colorScheme.tertiary)), heroShape)) {
                Canvas(Modifier.matchParentSize()) {
                    val center = Offset(size.width * .94f, size.height * .12f)
                    drawCircle(Brush.radialGradient(listOf(Color.White.copy(alpha = .25f), Color.Transparent),
                        center, size.minDimension * .65f), size.minDimension * .65f, center)
                    drawCircle(Color.White.copy(alpha = .07f), size.minDimension * .32f,
                        Offset(size.width * .91f, size.height * 1.02f))
                    drawCircle(Color.White.copy(alpha = .045f), size.minDimension * .18f,
                        Offset(size.width * .73f, size.height * .84f))
                }
                Column(Modifier.fillMaxWidth().padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically) {
                        Surface(shape = RoundedCornerShape(50), color = Color.White.copy(alpha = .16f),
                            border = BorderStroke(1.dp, Color.White.copy(alpha = .28f))) {
                            Text("NORA  ·  SAÚDE", Modifier.padding(horizontal = 14.dp, vertical = 8.dp),
                                style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onPrimary,
                                fontWeight = FontWeight.Bold)
                        }
                        IconButton(onClick = onOpenSettings) {
                            Icon(Icons.Default.Settings, contentDescription = "Configurações", tint = MaterialTheme.colorScheme.onPrimary)
                        }
                    }
                    Text("Seu cuidado, mais perto.", style = MaterialTheme.typography.headlineMedium,
                        color = MaterialTheme.colorScheme.onPrimary, fontWeight = FontWeight.ExtraBold)
                    Text("Acompanhe sua saúde e encontre os serviços da sua rede em um só lugar.",
                        style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onPrimary.copy(alpha = .92f))
                    Surface(shape = RoundedCornerShape(50), color = Color.White.copy(alpha = .13f)) {
                        Text("SAÚDE PÚBLICA CONECTADA", Modifier.padding(horizontal = 12.dp, vertical = 7.dp),
                            style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onPrimary,
                            fontWeight = FontWeight.Bold)
                    }
                }
            }
        }
    } else {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween) {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text("NORA", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.primary,
                    fontWeight = FontWeight.ExtraBold)
                val firstName = patient.session.user.fullName.substringBefore(' ')
                Text("Olá, $firstName", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            }
            IconButton(onClick = onOpenSettings) {
                Icon(Icons.Default.Settings, contentDescription = "Configurações")
            }
        }
        Text("Consultas, acompanhamento e oportunidades em um só lugar.", color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
    if (patient.message != null) Text(patient.message, color = MaterialTheme.colorScheme.error)
    if (patient.busy || patient.loadingUnits) LinearProgressIndicator(Modifier.fillMaxWidth())
    if (patient.session != null) EmailSuggestionNotice(patient, onOpenProfile)
    if (patient.session != null) {
        // Recalculada quando os dados mudam (o ciclo de 30 s renova as ofertas); nunca inventa consulta.
        val clock = LocalClockMillis.current
        val next = remember(patient.units, patient.unitData) { nextAppointment(patient.units, patient.unitData, clock()) }
        val presenceNow = rememberPresenceClock(listOfNotNull(next?.appointment))
        val openOffers = rememberOpenOffers(patient)
        val dependentOffers = rememberOpenDependentOffers(patient)
        val appointmentShape = RoundedCornerShape(28.dp)
        Card(
            modifier = Modifier.fillMaxWidth(),
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
            shape = appointmentShape,
            elevation = CardDefaults.cardElevation(defaultElevation = 12.dp),
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.onPrimary.copy(alpha = .12f)),
        ) {
            Column(Modifier.fillMaxWidth().background(Brush.linearGradient(listOf(
                Color(0xFF082744), Color(0xFF10527F), Color(0xFF1689AA)),
                start = Offset.Zero, end = Offset(900f, 700f)),
                appointmentShape).padding(22.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically) {
                    Text("SUA AGENDA", style = MaterialTheme.typography.labelLarge,
                        color = Color.White, fontWeight = FontWeight.Bold)
                    Surface(shape = RoundedCornerShape(50), color = MaterialTheme.colorScheme.surface.copy(alpha = .72f)) {
                        Text("Consultas por UBS", Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
                            style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                }
                if (next != null) {
                    Text("Sua próxima consulta", style = MaterialTheme.typography.titleLarge,
                        color = Color.White, fontWeight = FontWeight.Bold)
                    Text(next.appointment.specialty, style = MaterialTheme.typography.titleMedium,
                        color = Color.White, fontWeight = FontWeight.SemiBold)
                    Text(formatRange(next.appointment.startsAt, next.appointment.endsAt),
                        style = MaterialTheme.typography.bodyMedium, color = Color.White.copy(alpha = .94f))
                    Text("${next.appointment.professional} · ${next.unit.name}",
                        style = MaterialTheme.typography.bodyMedium, color = Color.White.copy(alpha = .94f))
                    Text(statusLabel(next.appointment.status), style = MaterialTheme.typography.labelLarge,
                        color = Color.White.copy(alpha = .94f), fontWeight = FontWeight.SemiBold)
                    PresenceConfirmation(next.appointment, presenceNow.value, patient.busy, onDark = true) {
                        onConfirmPresence(next.unit.id, next.appointment.id)
                    }
                } else {
                    Text("Nenhuma consulta futura", style = MaterialTheme.typography.titleLarge,
                        color = Color.White, fontWeight = FontWeight.Bold)
                    Text(noNextAppointmentText(patient),
                        style = MaterialTheme.typography.bodyMedium, color = Color.White.copy(alpha = .94f))
                }
                Button(onClick = { onNavigate(PatientDestination.APPOINTMENTS) }, modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.buttonColors(containerColor = Color.White, contentColor = Color(0xFF0A3D68))) {
                    Text("Ver minhas consultas")
                }
                OutlinedButton(onClick = { onNavigate(PatientDestination.BOOKING) }, modifier = Modifier.fillMaxWidth(),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = Color.White),
                    border = BorderStroke(1.dp, Color.White.copy(alpha = .75f))) {
                    Text("Agendar consulta")
                }
            }
        }
        Text("Acesso rápido", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            QuickActionCard("Consultas", "Futuras e anteriores", "▣", Modifier.weight(1f)) {
                onNavigate(PatientDestination.APPOINTMENTS)
            }
            QuickActionCard("UBS próximas", "Unidades e serviços", "⌖", Modifier.weight(1f)) {
                onNavigate(PatientDestination.UNITS)
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            QuickActionCard("Sua Jornada", "Cuidados e histórico", "↗", Modifier.weight(1f)) {
                onNavigate(PatientDestination.JOURNEY)
            }
            QuickActionCard("Vaga Relâmpago", "Oportunidades", "✦", Modifier.weight(1f)) {
                onNavigate(PatientDestination.FLASH)
            }
        }
        SectionCard("Acompanhamento", "Status informado pela unidade") {
            val queue = patient.unitQueue
            when {
                patient.units == null -> {
                    MutedText(if (patient.loadingUnits) "Carregando suas UBS…" else "Não foi possível carregar suas UBS.")
                    if (!patient.loadingUnits) OutlinedButton(onClick = onLoadUnits, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Carregar minhas UBS") }
                }
                patient.selectedUnitId == null -> MutedText("Escolha uma UBS vinculada para consultar sua situação.")
                queue == null -> MutedText("Toque em atualizar para consultar sua situação.")
                queue.isEmpty() -> MutedText("Não há solicitações de atendimento em acompanhamento nesta UBS.")
                else -> QueueEntries(queue, patient.busy, onLeaveQueue)
            }
            OutlinedButton(onClick = { onNavigate(PatientDestination.FLASH) }, modifier = Modifier.fillMaxWidth()) { Text("Escolher UBS e consultar") }
            OutlinedButton(onClick = onRefresh, enabled = !patient.busy && patient.selectedUnitId != null, modifier = Modifier.fillMaxWidth()) {
                Text(if (patient.busy) "Atualizando…" else "Atualizar acompanhamento")
            }
        }
        SectionCard("Vaga Relâmpago", "Oportunidades enviadas pela unidade") {
            val open = openOffers.list.value
            // As ofertas dos dependentes (com o nome deles) aparecem junto das do paciente; agindo por um dependente, só as dele.
            val dependents = if (patient.actingFor == null) dependentOffers.list.value else emptyList()
            when {
                open.isNotEmpty() || dependents.isNotEmpty() -> {
                    open.forEach { OpenOfferSummary(it, openOffers.now, onOpenOffer) }
                    dependents.forEach { OpenDependentOfferSummary(it, dependentOffers.now, onOpenDependentOffer) }
                }
                patient.units == null -> MutedText("As oportunidades aparecem aqui assim que suas UBS forem carregadas.")
                !offersLoadedForAll(patient.units, patient.unitData) -> MutedText("Consultando as oportunidades das suas UBS…")
                else -> MutedText("Nenhuma oportunidade aguarda sua resposta.")
            }
        }
        if (showGuidance) SectionCard("Sua Jornada", "Consultas, exames e vacinas") {
            MutedText("Consulte o histórico e os próximos passos em um só lugar.")
            OutlinedButton(onClick = { onNavigate(PatientDestination.JOURNEY) }, modifier = Modifier.fillMaxWidth()) {
                Text("Abrir Jornada")
            }
        }
        return
    }
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
        shape = RoundedCornerShape(24.dp)) {
        Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Sua jornada de cuidado", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onPrimaryContainer)
            Text("O acompanhamento estará disponível quando sua conta e a unidade de saúde estiverem conectadas.",
                color = MaterialTheme.colorScheme.onPrimaryContainer)
            Text("Serviços de saúde ainda não conectados", style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onPrimaryContainer)
        }
    }
    SectionCard("Agendamento e unidades", "A rede de saúde em um só lugar") {
        MutedText("Entre na sua conta para acompanhar seus dados. Nenhuma consulta ou UBS é exibida sem informação da rede.")
        OutlinedButton(onClick = { onNavigate(PatientDestination.UNITS) }, modifier = Modifier.fillMaxWidth()) {
            Text("Ver informações sobre UBS")
        }
    }
}

@Composable
private fun UnitsScreen(patient: PatientState, ownUnitsNotice: Boolean, onLoadUnits: () -> Unit, onSelectUnit: (String) -> Unit,
    onLocateUnit: (Double, Double) -> PatientHealthUnit?, onCatalog: suspend () -> List<RegistrationUnit>,
    onLink: suspend (List<String>) -> UnitLinkResult, onCheckUnlink: suspend (String) -> UnlinkImpact?, onUnlink: () -> Unit) {
    ScreenTitle("Unidades de saúde", "Consulte as UBS cadastradas pela rede.")
    if (ownUnitsNotice) ActionMessage("Agora você está vendo as suas UBS")
    val selected = patient.units?.find { it.id == patient.selectedUnitId }
    var expanded by remember { mutableStateOf(false) }
    // A seleção do diálogo de vínculo sobrevive à rotação; o catálogo é consultado de novo se faltar.
    var linkOpen by rememberSaveable { mutableStateOf(false) }
    var linkBusy by remember { mutableStateOf(false) }
    var catalog by remember { mutableStateOf<List<RegistrationUnit>?>(null) }
    var chosenIds by rememberSaveable(stateSaver = stringListSaver) { mutableStateOf(emptyList<String>()) }
    val chosen = remember(chosenIds) { chosenIds.toSet() }
    var linkError by remember { mutableStateOf<String?>(null) }
    var linkResult by remember { mutableStateOf<UnitLinkResult?>(null) }
    val scope = rememberCoroutineScope()
    // Remoção de vínculo: ação destrutiva, então o diálogo não sobrevive à rotação e nunca abre sozinho.
    var unlinkOpen by remember(selected?.id) { mutableStateOf(false) }
    // O que a remoção vai cancelar, relido a cada abertura do aviso; nulo enquanto confere ou quando não foi possível conferir.
    var checking by remember(selected?.id) { mutableStateOf(false) }
    var impact by remember(selected?.id) { mutableStateOf<UnlinkImpact?>(null) }
    suspend fun linkAction(action: suspend () -> Unit) {
        linkBusy = true; linkError = null
        try { action() }
        catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (failure: ApiFailure) { linkError = failure.message }
        catch (_: IOException) { linkError = "Não foi possível conectar ao serviço. Tente novamente." }
        catch (_: org.json.JSONException) { linkError = "O serviço retornou uma resposta incompatível." }
        finally { linkBusy = false }
    }
    SectionCard("Escolha uma UBS", "Nome, região e endereço") {
        when {
            patient.session == null -> MutedText("Entre na sua conta para consultar as unidades.")
            patient.units == null -> MutedText(if (patient.busy) "Carregando unidades…" else "Não foi possível carregar as unidades. Tente novamente.")
            patient.units.isEmpty() -> MutedText("Nenhuma UBS vinculada ao seu cadastro.")
            else -> {
                Box {
                    OutlinedButton(onClick = { expanded = true }, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) {
                        Text(selected?.name ?: "Selecionar UBS")
                    }
                    DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                        patient.units.forEach { unit ->
                            DropdownMenuItem(text = { Text(unit.name) }, onClick = {
                                expanded = false
                                onSelectUnit(unit.id)
                            })
                        }
                    }
                }
                selected?.let { MutedText("${it.region} · ${it.address}") }
            }
        }
        // Até a troca para as próprias UBS (no primeiro quadro), a mensagem em tela ainda é a do dependente: não aparece aqui.
        patient.message?.takeIf { patient.actingFor == null }?.let { MutedText(it) }
        if (patient.session != null) OutlinedButton(onClick = onLoadUnits, enabled = !patient.busy) { Text("Atualizar unidades") }
        if (patient.session != null) OutlinedButton(onClick = {
            linkOpen = true; chosenIds = emptyList(); catalog = null
        }, enabled = !patient.busy) { Text("Vincular outra UBS") }
        if (patient.session != null && selected != null) OutlinedButton(onClick = { unlinkOpen = true; checking = true; impact = null }, enabled = !patient.busy,
            colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.error.copy(alpha = .55f))) { Text("Remover esta UBS") }
        MutedText("Distâncias, horários e serviços ainda não são fornecidos pela rede.")
    }
    UnitsMap(selected, onLocateUnit)
    // Confere o que será cancelado: relê a fila e as consultas futuras da UBS a cada abertura, sem confiar no que já estava lido.
    LaunchedEffect(unlinkOpen, selected?.id) {
        val id = selected?.id
        if (unlinkOpen && id != null) { impact = onCheckUnlink(id); checking = false }
    }
    if (unlinkOpen && selected != null) {
        UnlinkDialog(selected.name, impact, loading = checking, busy = patient.busy,
            onConfirm = { unlinkOpen = false; onUnlink() }, onDismiss = { unlinkOpen = false })
    }
    LaunchedEffect(linkOpen) {
        if (linkOpen && catalog == null) linkAction { catalog = onCatalog() }
    }
    if (linkOpen) AlertDialog(onDismissRequest = { if (!linkBusy) linkOpen = false },
        title = { Text("Vincular UBS") },
        text = {
            Column(Modifier.heightIn(max = 360.dp).verticalScroll(rememberScrollState())) {
                MutedText("Selecione as unidades onde você já foi cadastrado. Seu CPF será conferido em cada uma.")
                catalog?.forEach { unit ->
                    Row(Modifier.fillMaxWidth().selectable(selected = unit.id in chosen, enabled = !linkBusy,
                        onClick = { chosenIds = if (unit.id in chosen) chosenIds - unit.id else if (chosenIds.size < 20) chosenIds + unit.id else chosenIds },
                        role = Role.Checkbox), verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(checked = unit.id in chosen, onCheckedChange = null)
                        Text("${unit.name} · ${unit.region}", Modifier.padding(start = 8.dp))
                    }
                }
                if (linkBusy) MutedText("Aguarde…")
                linkError?.let { MutedText(it) }
                if (!linkBusy && catalog == null) TextButton(onClick = { scope.launch { linkAction { catalog = onCatalog() } } }) { Text("Tentar novamente") }
            }
        },
        confirmButton = { TextButton(enabled = !linkBusy && chosen.isNotEmpty(), onClick = {
            scope.launch { linkAction { linkResult = onLink(chosenIds); linkOpen = false } }
        }) { Text("Conferir e vincular") } },
        dismissButton = { TextButton(enabled = !linkBusy, onClick = { linkOpen = false }) { Text("Cancelar") } })
    linkResult?.let { result ->
        fun names(ids: List<String>) = ids.joinToString(", ") { id -> catalog?.firstOrNull { it.id == id }?.name ?: "UBS" }
        AlertDialog(onDismissRequest = { linkResult = null }, title = { Text("Resultado da conferência") },
            text = { Text((if (result.linkedUnits.isNotEmpty()) "UBS vinculadas: ${names(result.linkedUnits)}." else "Nenhuma UBS foi vinculada.") +
                if (result.unmatchedUnits.isNotEmpty()) "\n\nNão encontramos um cadastro ativo em: ${names(result.unmatchedUnits)}. Procure a equipe dessas unidades." else "") },
            confirmButton = { TextButton(onClick = { linkResult = null }) { Text("Entendi") } })
    }
}

@Composable
internal fun ScreenTitle(title: String, subtitle: String) {
    val shape = RoundedCornerShape(26.dp)
    Card(Modifier.fillMaxWidth(), shape = shape,
        colors = CardDefaults.cardColors(containerColor = Color.Transparent),
        elevation = CardDefaults.cardElevation(defaultElevation = 9.dp)) {
        Box(Modifier.fillMaxWidth().background(Brush.linearGradient(listOf(
            Color(0xFF082744), Color(0xFF104C7A), Color(0xFF167BA6))), shape)) {
            Canvas(Modifier.matchParentSize()) {
                val orbCenter = Offset(size.width * .92f, size.height * .22f)
                val radius = size.minDimension * .48f
                drawCircle(Brush.radialGradient(listOf(Color(0xFFB9F4FF).copy(alpha = .60f),
                    Color(0xFF44A9D9).copy(alpha = .34f), Color.Transparent),
                    orbCenter - Offset(radius * .3f, radius * .3f), radius * 1.5f), radius, orbCenter)
                drawCircle(Color.White.copy(alpha = .18f), radius * 1.2f, orbCenter,
                    style = Stroke(width = 1.dp.toPx()))
                drawCircle(Color.White.copy(alpha = .10f), radius * 1.43f, orbCenter,
                    style = Stroke(width = 1.dp.toPx()))
            }
            Column(Modifier.fillMaxWidth().padding(horizontal = 22.dp, vertical = 20.dp),
                verticalArrangement = Arrangement.spacedBy(7.dp)) {
                Text("NORA  ·  SUA SAÚDE", style = MaterialTheme.typography.labelMedium,
                    color = Color.White.copy(alpha = .80f), fontWeight = FontWeight.Bold)
                Text(title, style = MaterialTheme.typography.headlineMedium,
                    color = Color.White, fontWeight = FontWeight.ExtraBold)
                Text(subtitle, style = MaterialTheme.typography.bodyMedium,
                    color = Color.White.copy(alpha = .94f))
            }
        }
    }
}

@Composable
private fun QuickActionCard(title: String, subtitle: String, glyph: String, modifier: Modifier = Modifier,
    onClick: () -> Unit) {
    val shape = RoundedCornerShape(22.dp)
    val colors = MaterialTheme.colorScheme
    Card(onClick = onClick, modifier = modifier.heightIn(min = 128.dp),
        shape = RoundedCornerShape(22.dp),
        colors = CardDefaults.cardColors(containerColor = colors.surface),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant.copy(alpha = .55f)),
        elevation = CardDefaults.cardElevation(defaultElevation = 7.dp, pressedElevation = 12.dp)) {
        Column(Modifier.fillMaxWidth().background(Brush.linearGradient(listOf(
            colors.surface,
            lerp(colors.surface, colors.primaryContainer, .22f),
            lerp(colors.surface, colors.tertiaryContainer, .18f)),
            start = Offset.Zero, end = Offset(520f, 600f)), shape).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(9.dp)) {
            Surface(shape = RoundedCornerShape(14.dp), color = MaterialTheme.colorScheme.primaryContainer) {
                Text(glyph, Modifier.padding(horizontal = 9.dp, vertical = 6.dp),
                    style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.primary,
                    fontWeight = FontWeight.Bold)
            }
            Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.onSurface)
            Text(subtitle, style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun SettingsScreen(settings: UserSettings, onChange: (UserSettings) -> Unit,
    patient: PatientState, onAddressChange: (String) -> Unit, onChangePassword: () -> Unit, onLogout: () -> Unit) {
    SectionCard("Aparência", "Escolha como a NORA aparece para você") {
        Appearance.entries.forEach { appearance ->
            val selected = settings.appearance == appearance
            Surface(
                modifier = Modifier.fillMaxWidth().selectable(selected, role = Role.RadioButton,
                    onClick = { onChange(settings.copy(appearance = appearance)) }),
                shape = RoundedCornerShape(16.dp),
                color = if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface,
                border = BorderStroke(if (selected) 2.dp else 1.dp,
                    if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outlineVariant)
            ) {
                Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                    RadioButton(selected = selected, onClick = null)
                    Column(Modifier.padding(start = 12.dp)) {
                        Text(appearance.title, fontWeight = FontWeight.SemiBold)
                        MutedText(appearance.description)
                    }
                }
            }
        }
    }
    SectionCard("Notificações", "Suas preferências de avisos") {
        MutedText("As escolhas ficam salvas neste aparelho. Os avisos de Vaga Relâmpago e de comunicados chegam enquanto a conexão ativa do NORA estiver rodando (a notificação fixa \"NORA · conexão ativa\"); para recebê-los, permita notificações do NORA nas configurações do Android.")
        PreferenceSwitch("Consultas", "Lembretes de consulta ainda não estão disponíveis: a rede não envia esse aviso, e esta escolha não tem efeito por enquanto.", settings.appointmentNotifications) {
            onChange(settings.copy(appointmentNotifications = it))
        }
        PreferenceSwitch("Vaga Relâmpago", "Notificação quando a UBS oferece uma consulta liberada. A lista também se atualiza sozinha.", settings.flashNotifications) {
            onChange(settings.copy(flashNotifications = it))
        }
        PreferenceSwitch("Comunicados da rede", "Notificação quando a Secretaria publica um comunicado para as suas UBS. Eles continuam na aba Avisos mesmo com a notificação desligada.", settings.announcementNotifications) {
            onChange(settings.copy(announcementNotifications = it))
        }
    }
    SectionCard("Interface", "Mais conforto na navegação") {
        PreferenceSwitch("Mostrar orientações", "Dicas de configuração na tela inicial", settings.showGuidance) {
            onChange(settings.copy(showGuidance = it))
        }
        PreferenceSwitch("Espaçamento confortável", "Mais espaço entre os cartões", settings.comfortableSpacing) {
            onChange(settings.copy(comfortableSpacing = it))
        }
    }
    SectionCard("Sobre o app", "NORA · Saúde digital") {
        Text("Versão ${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})")
        MutedText(patient.session?.user?.fullName ?: "Nenhuma conta conectada neste dispositivo.")
        if (patient.session == null) OutlinedTextField(patient.address, onAddressChange,
            label = { Text("Endereço do serviço NORA") }, singleLine = true,
            enabled = !patient.busy, modifier = Modifier.fillMaxWidth())
        if (patient.address.isNotBlank()) MutedText("Serviço: ${patient.address}. Para trocar o endereço, saia e configure na tela de entrada.")
        if (patient.message != null) Text(patient.message, color = MaterialTheme.colorScheme.error)
        if (patient.session != null) {
            OutlinedButton(onClick = onChangePassword, modifier = Modifier.fillMaxWidth()) { Text("Alterar senha") }
            OutlinedButton(onClick = onLogout, enabled = !patient.busy,
                modifier = Modifier.fillMaxWidth().height(52.dp),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
                border = BorderStroke(1.dp, MaterialTheme.colorScheme.error.copy(alpha = .55f))) {
                Text(if (patient.busy) "Aguarde…" else "Sair da conta", fontWeight = FontWeight.SemiBold)
            }
        }
    }
    var resetOpen by rememberSaveable { mutableStateOf(false) }
    OutlinedButton(onClick = { resetOpen = true }, modifier = Modifier.fillMaxWidth()) { Text("Restaurar preferências padrão") }
    if (resetOpen) AlertDialog(
        onDismissRequest = { resetOpen = false },
        title = { Text("Restaurar preferências?") },
        text = { Text("A aparência voltará a seguir o sistema e as preferências locais serão restauradas.") },
        confirmButton = { TextButton(onClick = { onChange(UserSettings()); resetOpen = false }) { Text("Restaurar") } },
        dismissButton = { TextButton(onClick = { resetOpen = false }) { Text("Cancelar") } },
    )
}

@Composable
private fun PreferenceSwitch(title: String, description: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f).padding(end = 12.dp)) {
            Text(title, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium)
            MutedText(description)
        }
        Switch(checked = checked, onCheckedChange = onChange,
            modifier = Modifier.semantics { contentDescription = title })
    }
}

@Composable
internal fun SectionCard(title: String, subtitle: String, highlight: Boolean = false, content: @Composable ColumnScope.() -> Unit) {
    val shape = RoundedCornerShape(24.dp)
    val colors = MaterialTheme.colorScheme
    Card(Modifier.fillMaxWidth(), shape = shape,
        colors = CardDefaults.cardColors(containerColor = colors.surface),
        // O destaque (não lido, sugestão) é uma borda mais grossa e na cor primária; o texto do cartão diz o estado, nunca só a cor.
        border = if (highlight) BorderStroke(2.dp, MaterialTheme.colorScheme.primary) else BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant.copy(alpha = .5f)),
        elevation = CardDefaults.cardElevation(defaultElevation = 6.dp)) {
        Column(Modifier.fillMaxWidth().background(Brush.linearGradient(listOf(
            colors.surface,
            lerp(colors.surface, colors.primaryContainer, .13f),
            lerp(colors.surface, colors.tertiaryContainer, .10f)),
            start = Offset.Zero, end = Offset(800f, 900f)), shape).padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Box(Modifier.width(38.dp).height(4.dp).background(
                Brush.horizontalGradient(listOf(Color(0xFF1686C4), Color(0xFF71D5DF))), RoundedCornerShape(50)))
            Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Text(subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            content()
        }
    }
}

@Composable
internal fun MutedText(text: String) {
    Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
}
