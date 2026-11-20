package com.example.app_nora.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.statusLabel

// Peças de estado reaproveitadas por todas as telas do paciente: etiqueta de situação, aviso de falha, mensagem de resultado,
// ação que desfaz algo, bloco recolhível e "Tentar novamente". Cada uma sempre diz o estado em texto e em marca, nunca só pela cor.

/** Tom de uma situação; as cores vêm do tema (sucesso e atenção em [StatusColors], o resto do esquema Material). */
enum class StatusTone { Success, Warning, Info, Error, Neutral }

/**
 * Etiqueta única de situação: marca (✓, !, ●, ✕), texto e cor. [mark] troca a marca padrão do [tone]; [onDark] é para cartões de
 * fundo azul-marinho (Início), onde a etiqueta vira um vidro claro com texto branco.
 */
@Composable
internal fun StatusTag(label: String, tone: StatusTone, mark: String? = null, onDark: Boolean = false) {
    val colors = MaterialTheme.colorScheme
    val status = MaterialTheme.statusColors
    val (defaultMark, tonedBackground, tonedContent) = when (tone) {
        StatusTone.Success -> Triple("✓", status.successContainer, status.onSuccessContainer)
        StatusTone.Warning -> Triple("!", status.warningContainer, status.onWarningContainer)
        StatusTone.Info -> Triple("●", colors.primaryContainer, colors.onPrimaryContainer)
        StatusTone.Error -> Triple("✕", colors.errorContainer, colors.onErrorContainer)
        StatusTone.Neutral -> Triple("●", colors.surfaceVariant, colors.onSurfaceVariant)
    }
    val background = if (onDark) Color.White.copy(alpha = .16f) else tonedBackground
    val content = if (onDark) Color.White else tonedContent
    Surface(shape = RoundedCornerShape(50), color = background) {
        Row(Modifier.padding(horizontal = 12.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(6.dp),
            verticalAlignment = Alignment.CenterVertically) {
            // A marca é enfeite visual: o leitor de tela lê só o texto da situação.
            Text(mark ?: defaultMark, Modifier.clearAndSetSemantics {}, style = MaterialTheme.typography.labelLarge,
                fontWeight = FontWeight.Bold, color = content)
            Text(label, style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold, color = content)
        }
    }
}

/** Tom e marca de uma situação do servidor (consulta, fila, oferta); o texto vem de [statusLabel]. */
internal fun statusStyle(status: String): Pair<StatusTone, String?> = when (status) {
    "completed", "accepted" -> StatusTone.Success to null
    "missed" -> StatusTone.Error to "!"
    "cancelled", "declined", "expired" -> StatusTone.Neutral to "✕"
    "waiting", "pending" -> StatusTone.Warning to null
    "scheduled", "called", "offered", "open" -> StatusTone.Info to null
    else -> StatusTone.Neutral to null
}

/** A situação de uma consulta, de uma solicitação na fila ou de uma oferta, no mesmo selo em todas as telas. */
@Composable
internal fun StatusBadge(status: String, onDark: Boolean = false) {
    val (tone, mark) = statusStyle(status)
    StatusTag(statusLabel(status), tone, mark, onDark)
}

/** Falha parcial ou total: ícone, cor de erro e texto, igual em todas as telas. */
@Composable
internal fun ErrorNotice(text: String, modifier: Modifier = Modifier) {
    Row(modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.Top) {
        Icon(Icons.Default.Warning, contentDescription = "Erro", tint = MaterialTheme.colorScheme.error, modifier = Modifier.size(20.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error)
    }
}

/** Como uma mensagem de resultado é mostrada: deu certo, falhou, deu certo mas pede atenção, ou só informa. */
internal enum class MessageKind { Success, Error, Warning, Info }

private val SUCCESS_MESSAGES = listOf("Consulta marcada", "Consulta cancelada", "Presença confirmada", "Oferta aceita", "Oferta recusada",
    "Você saiu da fila", "A saída da fila", "Pedido enviado", "Pedido cancelado", "Senha alterada", "Conta Google vinculada",
    "Conta Google desvinculada", "O e-mail da conta foi trocado", "Tudo bem", "Enviamos um código", "Você removeu")
private val FAILURE_MESSAGE = Regex("(?i)n[ãa]o |incorret|inv[áa]lid|erro|falh|temporariamente|muit[ao]s |confira|procure|atualize|tente|escolha|sess[ãa]o terminou|s[óo] pode|incompat|limite|cancele |j[áa] (tem|foi|est)")

/**
 * Classifica o texto de resultado de uma ação. As mensagens do paciente chegam como texto, sem tipo: as de sucesso são as frases
 * conhecidas do controlador; "use Atualizar" e "não confirmou" marcam um sucesso com ressalva; falha é o que fala em erro, em "não" ou
 * em tentar de novo. O resto só informa, o que mantém a faixa neutra em vez de chamar de erro o que não se sabe.
 */
internal fun actionMessageKind(message: String): MessageKind = when {
    message.contains("use Atualizar") || message.startsWith("A unidade não confirmou") || message.contains("Não foi possível confirmar a saída") -> MessageKind.Warning
    SUCCESS_MESSAGES.any { message.startsWith(it) } || (message.startsWith("O vínculo com") && message.endsWith("foi encerrado.")) -> MessageKind.Success
    FAILURE_MESSAGE.containsMatchIn(message) -> MessageKind.Error
    else -> MessageKind.Info
}

private class MessageStyle(val background: Color, val content: Color, val icon: ImageVector, val name: String)

/** Mensagem do resultado da última ação, com ícone e cor do tipo; anunciada de forma educada pelos leitores de tela. */
@Composable
internal fun ActionMessage(message: String?) {
    if (message == null) return
    val kind = remember(message) { actionMessageKind(message) }
    val colors = MaterialTheme.colorScheme
    val status = MaterialTheme.statusColors
    val style = when (kind) {
        MessageKind.Success -> MessageStyle(status.successContainer, status.onSuccessContainer, Icons.Default.CheckCircle, "Sucesso")
        MessageKind.Error -> MessageStyle(colors.errorContainer, colors.onErrorContainer, Icons.Default.Warning, "Erro")
        MessageKind.Warning -> MessageStyle(status.warningContainer, status.onWarningContainer, Icons.Default.Warning, "Atenção")
        MessageKind.Info -> MessageStyle(colors.surfaceVariant, colors.onSurface, Icons.Default.Info, "Aviso")
    }
    Surface(shape = RoundedCornerShape(16.dp), color = style.background, modifier = Modifier.fillMaxWidth().semantics { liveRegion = LiveRegionMode.Polite }) {
        Row(Modifier.padding(14.dp), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.Top) {
            Icon(style.icon, contentDescription = style.name, tint = style.content, modifier = Modifier.size(20.dp))
            Text(message, style = MaterialTheme.typography.bodyMedium, color = style.content)
        }
    }
}

/** Ação de texto que desfaz algo (sair da fila, cancelar consulta): sempre vermelha e alinhada ao texto do cartão. */
@Composable
internal fun DestructiveTextButton(label: String, onClick: () -> Unit, enabled: Boolean = true) {
    TextButton(onClick = onClick, enabled = enabled, contentPadding = PaddingValues(0.dp),
        colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error)) {
        Text(label, fontWeight = FontWeight.SemiBold)
    }
}

/** Explicação longa recolhida por padrão: um botão de texto abre e fecha o [content]; o estado sobrevive a girar a tela. */
@Composable
internal fun ExplanationToggle(showLabel: String, hideLabel: String, content: @Composable ColumnScope.() -> Unit) {
    var open by rememberSaveable { mutableStateOf(false) }
    TextButton(onClick = { open = !open }, contentPadding = PaddingValues(0.dp),
        modifier = Modifier.semantics { stateDescription = if (open) "Expandido" else "Recolhido" }) {
        Text(if (open) hideLabel else showLabel, fontWeight = FontWeight.SemiBold)
    }
    if (open) Column(verticalArrangement = Arrangement.spacedBy(10.dp)) { content() }
}

/** O mesmo botão, com o mesmo nome e formato, para repetir uma leitura que falhou. */
@Composable
internal fun RetryButton(onClick: () -> Unit, enabled: Boolean = true) {
    OutlinedButton(onClick = onClick, enabled = enabled, modifier = Modifier.fillMaxWidth()) { Text("Tentar novamente") }
}
