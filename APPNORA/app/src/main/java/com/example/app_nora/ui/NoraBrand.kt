package com.example.app_nora.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.example.app_nora.R
import com.example.app_nora.api.NoticeKind

/** O N da marca: marinho com detalhe amarelo no tema claro e branco no escuro (decidido pelo fundo do tema em uso). */
@Composable
internal fun NoraMark(size: Dp, modifier: Modifier = Modifier) {
    val dark = MaterialTheme.colorScheme.background.luminance() < .5f
    Image(painterResource(if (dark) R.drawable.nora_n_white else R.drawable.nora_n_navy), contentDescription = "Símbolo NORA",
        modifier = modifier.size(size))
}

/** Compatibilidade com as telas antigas: o símbolo é o N da marca. */
@Composable
internal fun NoraSymbol(size: Dp) = NoraMark(size)

/** Rodapé discreto com a autoria, no estilo de rodapé de apps: fonte pequena, opacidade 0,4 e o N da marca. */
@Composable
internal fun NoraFooter() {
    Row(Modifier.fillMaxWidth().padding(top = 24.dp, bottom = 4.dp).alpha(.4f), horizontalArrangement = Arrangement.Center,
        verticalAlignment = Alignment.CenterVertically) {
        NoraMark(12.dp)
        Spacer(Modifier.width(6.dp))
        Text("Elaborado por: Luana Pinheiro (luana.pinheiro.cruz@gmail.com) e Mateus Todeschini (mateustodeschini123@gmail.com)",
            style = MaterialTheme.typography.labelSmall.copy(fontSize = 10.sp, lineHeight = 13.sp),
            color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
    }
}

/**
 * Pop-up padrão do app para avisos curtos (resultado de ações): ícone e cor pelo tipo, título curto, texto e um botão.
 * Substitui os textos vermelhos soltos nas telas.
 */
@Composable
internal fun NoraNoticeDialog(kind: NoticeKind, text: String, onDismiss: () -> Unit, title: String = kind.title, button: String = "Ok") {
    val colors = MaterialTheme.colorScheme
    val (accent, icon) = when (kind) {
        NoticeKind.SUCCESS -> Color(0xFF1B8A4B) to Icons.Default.CheckCircle
        NoticeKind.WARNING -> Color(0xFFD08700) to Icons.Default.Warning
        NoticeKind.ERROR -> colors.error to Icons.Default.Close
        NoticeKind.INFO -> colors.primary to Icons.Default.Info
    }
    Dialog(onDismissRequest = onDismiss) {
        Card(shape = RoundedCornerShape(28.dp), colors = CardDefaults.cardColors(containerColor = colors.surface),
            elevation = CardDefaults.cardElevation(defaultElevation = 12.dp)) {
            Column(Modifier.fillMaxWidth().padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Box(Modifier.size(64.dp).background(accent.copy(alpha = .14f), CircleShape), contentAlignment = Alignment.Center) {
                    Icon(icon, contentDescription = null, tint = accent, modifier = Modifier.size(34.dp))
                }
                Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center)
                Text(text, style = MaterialTheme.typography.bodyMedium, color = colors.onSurfaceVariant, textAlign = TextAlign.Center)
                Button(onClick = onDismiss, modifier = Modifier.fillMaxWidth().height(50.dp), shape = RoundedCornerShape(16.dp)) {
                    Text(button, fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

/** Explicação curta ao segurar ou focar um ícone ou botão (dica do Material 3). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun Hint(text: String, content: @Composable () -> Unit) {
    TooltipBox(positionProvider = TooltipDefaults.rememberTooltipPositionProvider(TooltipAnchorPosition.Above),
        tooltip = { PlainTooltip { Text(text) } }, state = rememberTooltipState()) { content() }
}

/** Cabeçalho único das telas internas: seta de voltar no canto superior esquerdo (volta pela pilha de telas), título e ações. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun NoraTopBar(title: String, onBack: () -> Unit, actions: @Composable RowScope.() -> Unit = {}) {
    TopAppBar(title = { Text(title, fontWeight = FontWeight.Bold, maxLines = 1) },
        navigationIcon = {
            Hint("Voltar à tela anterior") {
                IconButton(onClick = onBack) { Icon(androidx.compose.material.icons.Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Voltar") }
            }
        },
        actions = actions,
        colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.background))
}
