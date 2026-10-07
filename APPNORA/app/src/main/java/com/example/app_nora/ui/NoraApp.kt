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
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.BuildConfig
import com.example.app_nora.preferences.*

@Composable
fun NoraApp(store: SettingsStore) {
    NoraContent(store.settings, store::update)
}

@Composable
fun NoraContent(settings: UserSettings, onSettingsChange: (UserSettings) -> Unit) {
    var settingsOpen by rememberSaveable { mutableStateOf(false) }
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
                        SettingsScreen(settings, onSettingsChange)
                    } else HomeScreen(settings.showGuidance, onOpenSettings = { settingsOpen = true })
                }
            }
        }
    }
}

@Composable
private fun HomeScreen(showGuidance: Boolean, onOpenSettings: () -> Unit) {
    Text("NORA", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.primary,
        fontWeight = FontWeight.ExtraBold)
    Text("Cuidar de você começa aqui", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold)
    Text("Consultas, acompanhamento e oportunidades em um só lugar.", color = MaterialTheme.colorScheme.onSurfaceVariant)
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
private fun SettingsScreen(settings: UserSettings, onChange: (UserSettings) -> Unit) {
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
        MutedText("Configurações locais disponíveis. Conta e serviços de saúde ainda não conectados.")
        OutlinedButton(onClick = {}, enabled = false, modifier = Modifier.fillMaxWidth()) { Text("Sair da conta") }
        MutedText("Nenhuma conta conectada neste dispositivo.")
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
