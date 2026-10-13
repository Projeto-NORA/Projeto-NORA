package com.example.app_nora.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.BuildConfig
import com.example.app_nora.preferences.*
import com.example.app_nora.api.*
import androidx.compose.ui.text.input.PasswordVisualTransformation
import kotlinx.coroutines.launch

@Composable
fun NoraApp(store: SettingsStore) {
    val context = LocalContext.current.applicationContext
    val controller = remember { PatientController(context) }
    val scope = rememberCoroutineScope()
    NoraPatientContent(store.settings, store::update, controller.state,
        onAddressChange = controller::changeAddress,
        onLogin = { identifier, password -> scope.launch { controller.login(identifier, password) } },
        onRefresh = { scope.launch { controller.refresh() } },
        onRespond = { id, accept -> scope.launch { controller.respond(id, accept) } },
        onLogout = { scope.launch { controller.logout() } })
}

@Composable
fun NoraContent(settings: UserSettings, onSettingsChange: (UserSettings) -> Unit) {
    NoraPatientContent(settings, onSettingsChange)
}

@Composable
fun NoraPatientContent(settings: UserSettings, onSettingsChange: (UserSettings) -> Unit,
    patient: PatientState = PatientState(), onAddressChange: (String) -> Unit = {},
    onLogin: ((String, String) -> Unit)? = null, onRefresh: () -> Unit = {},
    onRespond: (String, Boolean) -> Unit = { _, _ -> }, onLogout: () -> Unit = {},
    initiallyShowSettings: Boolean = false) {
    var settingsOpen by rememberSaveable { mutableStateOf(initiallyShowSettings) }
    val homeScroll = rememberScrollState()
    val settingsScroll = rememberScrollState()
    LaunchedEffect(settingsOpen) {
        if (settingsOpen) settingsScroll.scrollTo(0)
    }
    BackHandler(enabled = settingsOpen) { settingsOpen = false }
    NoraTheme(settings.appearance) {
        Scaffold(
            containerColor = MaterialTheme.colorScheme.background,
            bottomBar = {
                NavigationBar(containerColor = MaterialTheme.colorScheme.surface) {
                    NavigationBarItem(selected = !settingsOpen, onClick = { settingsOpen = false },
                        icon = { Icon(Icons.Default.Home, contentDescription = null) }, label = { Text("Início") })
                    NavigationBarItem(selected = settingsOpen, onClick = { settingsOpen = true },
                        icon = { Icon(Icons.Default.Settings, contentDescription = null) }, label = { Text("Configurações") })
                }
            }
        ) { padding ->
            Box(
                Modifier.fillMaxSize().padding(padding),
                contentAlignment = Alignment.TopCenter,
            ) {
                Column(
                    Modifier.widthIn(max = 640.dp).fillMaxWidth().fillMaxHeight()
                        .verticalScroll(if (settingsOpen) settingsScroll else homeScroll)
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
                        Text("Seu app, do seu jeito. As preferências são salvas neste dispositivo.",
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                        SettingsScreen(settings, onSettingsChange, patient, onAddressChange, onLogout)
                    } else HomeScreen(settings.showGuidance, onOpenSettings = { settingsOpen = true },
                        patient, onAddressChange, onLogin, onRefresh, onRespond)
                }
            }
        }
    }
}

@Composable
private fun LoginCard(patient: PatientState, onAddressChange: (String) -> Unit,
    onLogin: (String, String) -> Unit) {
    var identifier by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    SectionCard("Entrar na sua conta", "Use o CPF ou e-mail cadastrado pela unidade") {
        OutlinedTextField(patient.address, onAddressChange, label = { Text("Endereço do serviço NORA") },
            singleLine = true, enabled = !patient.busy, modifier = Modifier.fillMaxWidth(),
            supportingText = { Text(if (BuildConfig.DEBUG) "HTTPS da unidade ou http://10.0.2.2:5080 no emulador" else "Informe o endereço HTTPS fornecido pela unidade") })
        OutlinedTextField(identifier, { identifier = it }, label = { Text("CPF ou e-mail") },
            singleLine = true, enabled = !patient.busy, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(password, { password = it }, label = { Text("Senha") },
            visualTransformation = PasswordVisualTransformation(), singleLine = true,
            enabled = !patient.busy, modifier = Modifier.fillMaxWidth())
        Button(onClick = { onLogin(identifier, password); password = "" },
            enabled = !patient.busy && identifier.isNotBlank() && password.isNotBlank() && patient.address.isNotBlank(),
            modifier = Modifier.fillMaxWidth()) { Text("Entrar") }
        MutedText("Para criar ou recuperar sua conta, procure a unidade de saúde.")
    }
}

@Composable
private fun PatientCards(patient: PatientState, onRefresh: () -> Unit, onRespond: (String, Boolean) -> Unit) {
    Text(patient.session?.user?.fullName.orEmpty(), style = MaterialTheme.typography.titleLarge)
    OutlinedButton(onClick = onRefresh, enabled = !patient.busy) { Text("Atualizar acompanhamento") }
    val overview = patient.overview
    SectionCard("Fila de atendimento", "Solicitações registradas pela unidade") {
        if (overview == null) MutedText("Atualize para carregar seu acompanhamento.")
        else if (overview.queue.isEmpty()) MutedText("Você não tem solicitação em fila.")
        else overview.queue.forEach { entry ->
            Text("Solicitação ${entry.id} · ${statusTitle(entry.status)}", fontWeight = FontWeight.SemiBold)
            MutedText("Entrada: ${displayDate(entry.enteredAt)}")
        }
    }
    SectionCard("Vaga Relâmpago", "Cada oferta tem prazo de 10 minutos para resposta") {
        if (overview == null) MutedText("Atualize para carregar suas ofertas.")
        else if (overview.offers.isEmpty()) MutedText("Nenhuma oferta disponível para sua conta.")
        else overview.offers.forEach { offer ->
            HorizontalDivider()
            Text(offer.label, fontWeight = FontWeight.SemiBold)
            MutedText("${statusTitle(offer.status)} · Prazo: ${displayDate(offer.expiresAt)}")
            if (offer.status == "pending") {
                Button(onClick = { onRespond(offer.id, true) }, enabled = !patient.busy,
                    modifier = Modifier.fillMaxWidth()) { Text("Aceitar oferta") }
                OutlinedButton(onClick = { onRespond(offer.id, false) }, enabled = !patient.busy,
                    modifier = Modifier.fillMaxWidth()) { Text("Recusar oferta") }
            }
        }
        MutedText("O serviço confirma a disponibilidade ao receber a resposta. O aceite não confirma um atendimento realizado.")
    }
    SectionCard("Consultas", "Seus próximos cuidados") {
        MutedText("Para consultar agendamentos, procure sua unidade. A agenda ainda não está disponível no aplicativo.")
    }
}

private fun statusTitle(value: String): String = when (value) {
    "waiting" -> "Aguardando"
    "offered" -> "Oferta enviada"
    "called" -> "Chamado pela unidade"
    "completed" -> "Concluído"
    "cancelled" -> "Cancelado"
    "pending" -> "Aguardando resposta"
    "accepted" -> "Oferta aceita"
    "declined" -> "Oferta recusada"
    "expired" -> "Prazo encerrado"
    else -> value
}

private fun displayDate(value: String): String = try {
    val normalized = value.replace(Regex("\\.(\\d+)")) { match ->
        "." + match.groupValues[1].take(3).padEnd(3, '0')
    }
    val pattern = if (normalized.contains('.')) "yyyy-MM-dd'T'HH:mm:ss.SSSXXX" else "yyyy-MM-dd'T'HH:mm:ssXXX"
    val parser = java.text.SimpleDateFormat(pattern, java.util.Locale.ROOT).apply { isLenient = false }
    parser.parse(normalized)?.let { date ->
        java.text.DateFormat.getDateTimeInstance(java.text.DateFormat.SHORT, java.text.DateFormat.SHORT).format(date)
    } ?: value
} catch (_: java.text.ParseException) { value }

@Composable
private fun HomeScreen(showGuidance: Boolean, onOpenSettings: () -> Unit, patient: PatientState,
    onAddressChange: (String) -> Unit, onLogin: ((String, String) -> Unit)?,
    onRefresh: () -> Unit, onRespond: (String, Boolean) -> Unit) {
    Text("NORA", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.primary,
        fontWeight = FontWeight.ExtraBold)
    Text("Cuidar de você começa aqui", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold)
    Text("Consultas, acompanhamento e oportunidades em um só lugar.", color = MaterialTheme.colorScheme.onSurfaceVariant)
    if (patient.message != null) Text(patient.message, color = MaterialTheme.colorScheme.error)
    if (patient.busy) LinearProgressIndicator(Modifier.fillMaxWidth())
    if (patient.session == null && onLogin != null) LoginCard(patient, onAddressChange, onLogin)
    if (patient.session != null) {
        PatientCards(patient, onRefresh, onRespond)
        if (showGuidance) SectionCard("Deixe o app mais confortável", "Aparência e preferências") {
            Button(onClick = onOpenSettings) { Text("Abrir configurações") }
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
    SectionCard("Consultas", "Seus próximos cuidados") {
        Text("Agenda indisponível", style = MaterialTheme.typography.titleMedium)
        MutedText("As consultas aparecerão aqui após a conexão com o serviço de saúde.")
    }
    SectionCard("Fila de atendimento", "Acompanhe sua solicitação") {
        Text("Acompanhamento indisponível", style = MaterialTheme.typography.titleMedium)
        MutedText("Sua posição e as atualizações dependem da integração com a unidade.")
    }
    SectionCard("Vaga Relâmpago", "Oportunidades de atendimento") {
        Text("Oportunidades indisponíveis", style = MaterialTheme.typography.titleMedium)
        MutedText("A disponibilidade de vagas será informada pelo serviço de saúde quando estiver conectado.")
    }
    if (showGuidance) SectionCard("Deixe o app mais confortável", "Aparência e preferências") {
        MutedText("Escolha um tema ou acompanhe a aparência do seu dispositivo.")
        Button(onClick = onOpenSettings) { Text("Abrir configurações") }
    }
}

@Composable
private fun SettingsScreen(settings: UserSettings, onChange: (UserSettings) -> Unit,
    patient: PatientState, onAddressChange: (String) -> Unit, onLogout: () -> Unit) {
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
        MutedText("Preferências salvas. Os avisos serão enviados quando o serviço de notificações estiver disponível.")
        PreferenceSwitch("Consultas", "Lembretes e atualizações de agendamento", settings.appointmentNotifications) {
            onChange(settings.copy(appointmentNotifications = it))
        }
        PreferenceSwitch("Vaga Relâmpago", "Avisos sobre oportunidades de atendimento", settings.flashNotifications) {
            onChange(settings.copy(flashNotifications = it))
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
        OutlinedButton(onClick = onLogout, enabled = patient.session != null && !patient.busy,
            modifier = Modifier.fillMaxWidth()) { Text(if (patient.busy) "Aguarde…" else "Sair da conta") }
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
private fun SectionCard(title: String, subtitle: String, content: @Composable ColumnScope.() -> Unit) {
    OutlinedCard(Modifier.fillMaxWidth(), shape = RoundedCornerShape(20.dp),
        colors = CardDefaults.outlinedCardColors(containerColor = MaterialTheme.colorScheme.surface),
        border = BorderStroke(1.dp, MaterialTheme.colorScheme.outlineVariant)) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
            MutedText(subtitle)
            content()
        }
    }
}

@Composable
private fun MutedText(text: String) {
    Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
}
