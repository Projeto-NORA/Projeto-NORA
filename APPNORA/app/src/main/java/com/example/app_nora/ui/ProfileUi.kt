package com.example.app_nora.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.ImageDecoder
import android.graphics.Matrix
import android.net.Uri
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ExitToApp
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Email
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Face
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.FileProvider
import com.example.app_nora.api.*
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File

/**
 * Perfil no estilo dos apps conhecidos: foto grande com botão de câmera, nome, CPF mascarado e e-mail no topo; engrenagem no canto
 * para Configurações; seções em cartões e "Sair da conta" em vermelho com confirmação.
 */
@Composable
fun ProfileScreen(patient: PatientState, onOpenSettings: () -> Unit, onChangePassword: () -> Unit, onLogout: () -> Unit,
    email: EmailActions = EmailActions(), photo: PhotoActions = PhotoActions(), onOpenFamily: () -> Unit = {}, onOpenUnits: () -> Unit = {}) {
    val user = patient.session?.user
    var logoutOpen by remember { mutableStateOf(false) }
    var photoMenu by remember { mutableStateOf(false) }
    LaunchedEffect(patient.session?.token) { if (patient.session != null && !patient.photoLoaded) photo.onLoad() }
    Box(Modifier.fillMaxWidth()) {
        Column(Modifier.fillMaxWidth().padding(top = 8.dp), horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Box(contentAlignment = Alignment.BottomEnd) {
                ProfileAvatar(user?.fullName ?: "", patient.photo, 112.dp)
                if (user != null) Hint("Alterar foto") {
                    Surface(onClick = { photoMenu = true }, enabled = !patient.photoBusy, shape = CircleShape, color = MaterialTheme.colorScheme.primary,
                        border = BorderStroke(3.dp, MaterialTheme.colorScheme.background), modifier = Modifier.size(38.dp)) {
                        Box(contentAlignment = Alignment.Center) {
                            if (patient.photoBusy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp, color = MaterialTheme.colorScheme.onPrimary)
                            else CameraGlyph(MaterialTheme.colorScheme.onPrimary)
                        }
                    }
                }
            }
            Spacer(Modifier.height(4.dp))
            Text(user?.fullName ?: "Nenhuma conta conectada", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            user?.cpfMasked?.let { MutedText("CPF $it") }
            user?.email?.let { MutedText(it) }
        }
        Box(Modifier.align(Alignment.TopEnd)) {
            Hint("Configurações") { IconButton(onClick = onOpenSettings) { Icon(Icons.Default.Settings, contentDescription = "Configurações") } }
        }
    }
    EmailSuggestionCard(patient, email)
    ProfileSection("Dados pessoais") {
        ProfileRow(Icons.Default.Person, "Nome", user?.fullName ?: "—")
        ProfileRow(Icons.Default.Face, "CPF", user?.cpfMasked ?: "Não informado pelo serviço")
        ProfileRow(Icons.Default.Email, "E-mail", user?.email ?: "Não informado pelo serviço")
    }
    ProfileSection("UBS vinculadas") {
        val units = patient.units
        when {
            units == null -> MutedText("Carregando suas UBS…")
            units.isEmpty() -> MutedText("Nenhuma UBS vinculada.")
            else -> units.sortedAlphabetically { it.name }.forEach { ProfileRow(Icons.Default.Home, it.name, it.region) }
        }
        ProfileLink("Ver UBS", onOpenUnits)
    }
    ProfileSection("Família") {
        val approved = approvedLinks(patient.family.links).sortedAlphabetically { dependentDisplayName(it) }
        if (approved.isEmpty()) MutedText("Nenhum dependente vinculado.")
        approved.forEach { ProfileRow(Icons.Default.Person, dependentDisplayName(it), it.unitName) }
        ProfileLink("Abrir Família", onOpenFamily)
    }
    ProfileSection("Segurança") {
        if (patient.session != null) ProfileLink("Alterar senha", onChangePassword, Icons.Default.Lock)
        ProfileLink("Configurações", onOpenSettings, Icons.Default.Settings)
    }
    if (patient.session != null) Surface(onClick = { logoutOpen = true }, enabled = !patient.busy, shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.error.copy(alpha = .08f), border = BorderStroke(1.dp, MaterialTheme.colorScheme.error.copy(alpha = .4f)),
        modifier = Modifier.fillMaxWidth()) {
        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center) {
            Icon(Icons.AutoMirrored.Filled.ExitToApp, contentDescription = null, tint = MaterialTheme.colorScheme.error)
            Spacer(Modifier.width(10.dp))
            Text("Sair da conta", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold)
        }
    }
    if (logoutOpen) AlertDialog(onDismissRequest = { logoutOpen = false },
        title = { Text("Sair da conta") }, text = { Text("Tem certeza que deseja sair?") },
        confirmButton = { TextButton(onClick = { logoutOpen = false; onLogout() }) { Text("Sair", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.Bold) } },
        dismissButton = { TextButton(onClick = { logoutOpen = false }) { Text("Cancelar") } })
    if (photoMenu) PhotoOptions(hasPhoto = patient.photo != null, onDismiss = { photoMenu = false }, onPhoto = photo.onUpload, onRemove = photo.onRemove)
}

@Composable
private fun ProfileSection(title: String, content: @Composable ColumnScope.() -> Unit) {
    Card(Modifier.fillMaxWidth(), shape = RoundedCornerShape(20.dp), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(defaultElevation = 3.dp)) {
        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            content()
        }
    }
}

@Composable
private fun ProfileRow(icon: ImageVector, label: String, value: String) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Box(Modifier.size(36.dp).background(MaterialTheme.colorScheme.primaryContainer, CircleShape), contentAlignment = Alignment.Center) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(20.dp))
        }
        Column { Text(label, fontWeight = FontWeight.SemiBold); MutedText(value) }
    }
}

@Composable
private fun ProfileLink(label: String, onClick: () -> Unit, icon: ImageVector? = null) {
    Row(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).clickable(onClick = onClick).padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically) {
        icon?.let { Icon(it, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant); Spacer(Modifier.width(12.dp)) }
        Text(label, Modifier.weight(1f), color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.SemiBold)
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/** Foto circular ou, sem foto, as iniciais do nome sobre a cor da marca. */
@Composable
internal fun ProfileAvatar(name: String, photo: ByteArray?, size: androidx.compose.ui.unit.Dp) {
    val bitmap = remember(photo) { photo?.let { runCatching { BitmapFactory.decodeByteArray(it, 0, it.size)?.asImageBitmap() }.getOrNull() } }
    Box(Modifier.size(size).clip(CircleShape).background(MaterialTheme.colorScheme.primaryContainer)
        .border(2.dp, MaterialTheme.colorScheme.primary.copy(alpha = .3f), CircleShape), contentAlignment = Alignment.Center) {
        if (bitmap != null) Image(bitmap, contentDescription = "Foto de perfil", contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
        else Text(initialsOf(name), fontSize = (size.value / 2.6f).sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.primary)
    }
}

@Composable
private fun CameraGlyph(color: Color) {
    Canvas(Modifier.size(18.dp)) {
        val u = size.minDimension / 24f
        drawRoundRect(color, Offset(2f * u, 6f * u), Size(20f * u, 14f * u), androidx.compose.ui.geometry.CornerRadius(3f * u), style = Stroke(2f * u))
        drawRect(color, Offset(8f * u, 3.5f * u), Size(8f * u, 3f * u))
        drawCircle(color, 4f * u, Offset(12f * u, 13f * u), style = Stroke(2f * u))
    }
}

/** "Alterar foto" (galeria pelo seletor padrão do Android ou câmera) e "Remover foto". A foto sai recortada, até 512 px, em JPEG. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PhotoOptions(hasPhoto: Boolean, onDismiss: () -> Unit, onPhoto: (ByteArray) -> Unit, onRemove: () -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var failed by remember { mutableStateOf(false) }
    fun handle(uri: Uri?) {
        if (uri == null) return
        scope.launch {
            val jpeg = withContext(Dispatchers.Default) { runCatching { preparePhoto(context, uri) }.getOrNull() }
            if (jpeg == null) failed = true else { onPhoto(jpeg); onDismiss() }
        }
    }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { handle(it) }
    var cameraUri by remember { mutableStateOf<Uri?>(null) }
    val camera = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok -> if (ok) handle(cameraUri) }
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 24.dp).padding(bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Foto de perfil", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold)
            ProfileLink("Alterar foto (galeria)", { picker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) }, Icons.Default.Face)
            ProfileLink("Tirar foto", {
                val file = File(File(context.cacheDir, "fotos").apply { mkdirs() }, "perfil.jpg")
                val uri = FileProvider.getUriForFile(context, "${context.packageName}.fotos", file)
                cameraUri = uri
                runCatching { camera.launch(uri) }.onFailure { failed = true }
            }, Icons.Default.Person)
            if (hasPhoto) ProfileLink("Remover foto", { onRemove(); onDismiss() }, Icons.Default.Delete)
            if (failed) Text("Não conseguimos usar esta imagem. Escolha outra.", color = MaterialTheme.colorScheme.error)
        }
    }
}

/** Lê a imagem (com a rotação certa), recorta o quadrado central, reduz para até [PHOTO_MAX_SIDE] px e comprime em JPEG. */
private fun preparePhoto(context: android.content.Context, uri: Uri): ByteArray {
    val source: Bitmap = if (Build.VERSION.SDK_INT >= 28) {
        ImageDecoder.decodeBitmap(ImageDecoder.createSource(context.contentResolver, uri)) { decoder, info, _ ->
            decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
            val longest = maxOf(info.size.width, info.size.height)
            if (longest > 2048) { val f = 2048f / longest; decoder.setTargetSize((info.size.width * f).toInt(), (info.size.height * f).toInt()) }
        }
    } else {
        val raw = context.contentResolver.openInputStream(uri)!!.use { BitmapFactory.decodeStream(it) }!!
        val rotation = context.contentResolver.openInputStream(uri)!!.use {
            when (android.media.ExifInterface(it).getAttributeInt(android.media.ExifInterface.TAG_ORIENTATION, 1)) {
                6 -> 90f; 3 -> 180f; 8 -> 270f; else -> 0f
            }
        }
        if (rotation == 0f) raw else Bitmap.createBitmap(raw, 0, 0, raw.width, raw.height, Matrix().apply { postRotate(rotation) }, true)
    }
    val (left, top, side) = squareCrop(source.width, source.height)
    val target = photoSide(side)
    val square = Bitmap.createBitmap(source, left, top, side, side)
    val scaled = if (target == side) square else Bitmap.createScaledBitmap(square, target, target, true)
    var quality = 88
    while (true) {
        val out = ByteArrayOutputStream()
        scaled.compress(Bitmap.CompressFormat.JPEG, quality, out)
        if (out.size() <= PHOTO_MAX_BYTES || quality <= 40) return out.toByteArray()
        quality -= 12
    }
}

/**
 * A UBS corrigiu o e-mail de referência: oferece trocar o e-mail da conta. O código é enviado ao endereço novo (mostrado mascarado) e
 * só depois de confirmado o e-mail da conta muda; também dá para dispensar. O código digitado fica só nesta tela, em memória.
 */
@Composable
internal fun EmailSuggestionCard(patient: PatientState, actions: EmailActions) {
    val suggestion = patient.email.suggestion ?: return
    val codeSent = patient.email.codeSentFor == suggestion.id
    var code by remember(suggestion.id) { mutableStateOf("") }
    val scope = rememberCoroutineScope()
    SectionCard("E-mail da conta", "Sugestão da UBS", highlight = true) {
        Text("A UBS ${suggestion.unitName} registrou outro e-mail para você.", fontWeight = FontWeight.SemiBold)
        Text("E-mail novo: ${suggestion.maskedEmail}")
        MutedText("Enviamos um código a esse e-mail; a troca vale após digitá-lo. Não reconhece? Dispense.")
        if (!codeSent) {
            Button(onClick = actions.onSendCode, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Trocar e-mail") }
        } else {
            OutlinedTextField(value = code, onValueChange = { code = it.filter { digit -> digit in '0'..'9' }.take(6) },
                label = { Text("Código de 6 dígitos") }, singleLine = true, enabled = !patient.busy, modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number, imeAction = ImeAction.Done))
            Button(onClick = { scope.launch { if (actions.onConfirm(code)) code = "" } }, enabled = !patient.busy && code.length == 6,
                modifier = Modifier.fillMaxWidth()) { Text(if (patient.busy) "Confirmando…" else "Confirmar código") }
            OutlinedButton(onClick = actions.onSendCode, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Enviar outro código") }
            MutedText("O código vale por 10 minutos e o envio é limitado a 3 por sugestão.")
        }
        TextButton(onClick = actions.onDismiss, enabled = !patient.busy, modifier = Modifier.fillMaxWidth()) { Text("Agora não, manter meu e-mail") }
    }
}

/** Aviso discreto no Início: leva ao Perfil, onde a troca acontece. */
@Composable
internal fun EmailSuggestionNotice(patient: PatientState, onOpenProfile: () -> Unit) {
    val suggestion = patient.email.suggestion ?: return
    Surface(shape = RoundedCornerShape(18.dp), color = MaterialTheme.colorScheme.secondaryContainer) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 8.dp), verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween) {
            Text("A UBS ${suggestion.unitName} registrou outro e-mail para você.", Modifier.weight(1f), style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSecondaryContainer)
            TextButton(onClick = onOpenProfile) { Text("Ver no Perfil", fontWeight = FontWeight.Bold) }
        }
    }
}
