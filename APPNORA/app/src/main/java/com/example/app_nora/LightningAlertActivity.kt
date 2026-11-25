package com.example.app_nora

import android.app.KeyguardManager
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.Ringtone
import android.media.RingtoneManager
import android.os.Build
import android.os.Bundle
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.example.app_nora.api.LIGHTNING_VIBRATION
import com.example.app_nora.api.LightningAlerts
import com.example.app_nora.api.OfferNotice
import com.example.app_nora.api.formatRange
import com.example.app_nora.api.formatRemaining
import kotlinx.coroutines.delay

/**
 * Alerta de Vaga Relâmpago em tela cheia, por cima da tela de bloqueio (como um alerta da Defesa Civil). Toca alarme e vibra sem parar
 * até a pessoa tocar na tela ou até o prazo da oferta. "Aceitar" e "Recusar" abrem o app (pedindo o desbloqueio) com a mesma confirmação
 * da notificação; "Ver detalhes" abre a tela da oferta. Os dados vêm da lista em memória de [LightningAlerts]; o Intent só tem o id.
 */
class LightningAlertActivity : ComponentActivity() {
    private var ringtone: Ringtone? = null
    private var vibrator: Vibrator? = null
    private var ringing by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (Build.VERSION.SDK_INT >= 27) { setShowWhenLocked(true); setTurnScreenOn(true) }
        else @Suppress("DEPRECATION") window.addFlags(WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED or WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        val offerId = intent.getStringExtra(EXTRA_OFFER)
        val notice = LightningAlerts.notice(offerId)
        if (notice != null && savedInstanceState == null) { LightningAlerts.quiet(this, notice.offer.id); startAlarm() }
        setContent { AlertScreen(notice, ringing, onExpired = { stopAlarm() }, onAccept = { notice?.let { respond(it, "accept") } },
            onDecline = { notice?.let { respond(it, "decline") } }, onDetails = { notice?.let { respond(it, null) } ?: openApp(null) },
            onClose = { stopAlarm(); finish() }) }
    }

    override fun onUserInteraction() { super.onUserInteraction(); stopAlarm() }

    override fun onDestroy() { stopAlarm(); super.onDestroy() }

    private fun respond(notice: OfferNotice, decision: String?) {
        stopAlarm()
        LightningAlerts.dismiss(this, notice.offer.id)
        openApp(LightningAlerts.openAppIntent(this, notice, decision))
    }

    /** Abre o app; com a tela bloqueada, pede o desbloqueio antes (aceitar uma consulta exige a pessoa dona do aparelho). */
    private fun openApp(target: Intent?) {
        val intent = target ?: Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        val keyguard = getSystemService(Context.KEYGUARD_SERVICE) as KeyguardManager
        if (Build.VERSION.SDK_INT >= 26 && keyguard.isKeyguardLocked) {
            keyguard.requestDismissKeyguard(this, object : KeyguardManager.KeyguardDismissCallback() {
                override fun onDismissSucceeded() { startActivity(intent); finish() }
            })
        } else { startActivity(intent); finish() }
    }

    private fun startAlarm() {
        if (ringing) return
        ringing = true
        val uri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM) ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION)
        ringtone = uri?.let { RingtoneManager.getRingtone(this, it) }?.apply {
            audioAttributes = AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build()
            if (Build.VERSION.SDK_INT >= 28) isLooping = true
            play()
        }
        vibrator = (if (Build.VERSION.SDK_INT >= 31) (getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager).defaultVibrator
            else @Suppress("DEPRECATION") getSystemService(Context.VIBRATOR_SERVICE) as Vibrator).also {
            if (Build.VERSION.SDK_INT >= 26) it.vibrate(VibrationEffect.createWaveform(LIGHTNING_VIBRATION, 1))
            else @Suppress("DEPRECATION") it.vibrate(LIGHTNING_VIBRATION, 1)
        }
    }

    private fun stopAlarm() {
        if (!ringing) return
        ringing = false
        ringtone?.stop(); ringtone = null
        vibrator?.cancel(); vibrator = null
    }

    companion object { const val EXTRA_OFFER = "com.example.app_nora.ALERT_OFFER" }
}

@Composable
private fun AlertScreen(notice: OfferNotice?, ringing: Boolean, onExpired: () -> Unit, onAccept: () -> Unit, onDecline: () -> Unit,
    onDetails: () -> Unit, onClose: () -> Unit) {
    val pulse = rememberInfiniteTransition(label = "pulso")
    val scale by pulse.animateFloat(1f, 1.12f, infiniteRepeatable(tween(520), RepeatMode.Reverse), label = "escala")
    val expiresAt = notice?.offer?.expiresAtMillis
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(expiresAt) { while (true) { now = System.currentTimeMillis(); if (expiresAt != null && now >= expiresAt) { onExpired(); break }; delay(1000) } }
    val expired = expiresAt != null && now >= expiresAt
    val yellow = Color(0xFFF5B800)
    Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Color(0xFF01122B), Color(0xFF0B3B66), Color(0xFF01122B)))).systemBarsPadding().padding(24.dp)) {
        Column(Modifier.fillMaxSize(), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(18.dp, Alignment.CenterVertically)) {
            Text("⚡", fontSize = 64.sp, modifier = Modifier.graphicsLayer { scaleX = scale; scaleY = scale })
            Text("VAGA RELÂMPAGO", color = yellow, fontWeight = FontWeight.Black, fontSize = 32.sp, textAlign = TextAlign.Center,
                modifier = Modifier.graphicsLayer { scaleX = scale; scaleY = scale })
            Text("Antecipe sua consulta", color = Color.White, style = MaterialTheme.typography.titleMedium)
            if (notice == null) {
                Text("Esta oportunidade não está mais disponível.", color = Color.White, textAlign = TextAlign.Center)
                Button(onClick = onDetails, modifier = Modifier.fillMaxWidth().height(56.dp)) { Text("Abrir o NORA") }
                TextButton(onClick = onClose) { Text("Fechar", color = Color.White) }
                return@Column
            }
            Surface(shape = RoundedCornerShape(24.dp), color = Color.White.copy(alpha = .1f), modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    notice.dependentName?.let { Text("Para $it", color = yellow, fontWeight = FontWeight.Bold) }
                    Text(notice.offer.specialty, color = Color.White, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                    Text(notice.offer.professional, color = Color.White.copy(alpha = .9f))
                    Text(formatRange(notice.offer.startsAt, notice.offer.endsAt), color = Color.White, fontWeight = FontWeight.SemiBold)
                    Text(notice.unitName, color = Color.White.copy(alpha = .9f))
                }
            }
            Text(if (expired) "Prazo encerrado" else "Responda em ${formatRemaining((expiresAt ?: now) - now)}",
                color = if (expired) Color(0xFFFFB4AB) else yellow, fontWeight = FontWeight.Bold, fontSize = 24.sp)
            if (!expired) {
                Button(onClick = onAccept, modifier = Modifier.fillMaxWidth().height(58.dp), shape = RoundedCornerShape(18.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = yellow, contentColor = Color(0xFF01122B))) { Text("Aceitar", fontWeight = FontWeight.Bold, fontSize = 18.sp) }
                OutlinedButton(onClick = onDecline, modifier = Modifier.fillMaxWidth().height(54.dp), shape = RoundedCornerShape(18.dp),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = Color.White)) { Text("Recusar", fontWeight = FontWeight.SemiBold) }
            }
            TextButton(onClick = onDetails) { Text("Ver detalhes", color = Color.White, fontWeight = FontWeight.SemiBold) }
            if (ringing) Text("Toque na tela para silenciar", color = Color.White.copy(alpha = .6f), style = MaterialTheme.typography.bodySmall)
        }
    }
}
