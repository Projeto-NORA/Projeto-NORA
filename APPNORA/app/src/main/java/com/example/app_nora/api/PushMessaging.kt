package com.example.app_nora.api

import android.Manifest
import android.annotation.SuppressLint
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationChannelCompat
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.example.app_nora.MainActivity
import com.example.app_nora.R
import com.example.app_nora.preferences.SettingsStore
import com.google.firebase.FirebaseApp
import com.google.firebase.messaging.FirebaseMessaging
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import java.lang.ref.WeakReference
import kotlin.coroutines.resume

/**
 * Push do servidor (FCM). Só existe com `app/google-services.json` no build: sem o arquivo o Firebase não inicializa, [FirebasePush.tokenProvider]
 * devolve `null`, o serviço nunca é chamado e o app segue só com o ciclo de 30 s de [PatientPresenceService].
 */
internal object FirebasePush {
    /** O Firebase foi inicializado (há `google-services.json`)? */
    fun available(context: Context): Boolean = FirebaseApp.getApps(context).isNotEmpty()

    /** O token FCM do aparelho, ou `null` quando o Firebase está desligado. Falhas de rede viram `null`: o próximo token novo tenta de novo. */
    fun tokenProvider(context: Context): (suspend () -> String?)? = if (available(context)) ({ currentToken() }) else null

    private suspend fun currentToken(): String? = suspendCancellableCoroutine { continuation ->
        FirebaseMessaging.getInstance().token.addOnCompleteListener { task ->
            if (continuation.isActive) continuation.resume(if (task.isSuccessful) task.result else null)
        }
    }
}

/** Liga o serviço do FCM ao controlador da sessão, que é quem sabe se há alguém conectado. */
internal object PushHub {
    private var controller: WeakReference<PatientController>? = null
    // O token novo chega com o serviço prestes a ser encerrado: a requisição roda num escopo do processo.
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    fun attach(controller: PatientController) { this.controller = WeakReference(controller) }

    fun onNewToken(token: String) {
        val target = controller?.get() ?: return
        scope.launch { target.syncPushToken(token) }
    }
}

/** Recebe o push (app aberto) e o token novo do FCM. */
class NoraMessagingService : FirebaseMessagingService() {
    override fun onNewToken(token: String) { PushHub.onNewToken(token) }

    override fun onMessageReceived(message: RemoteMessage) {
        val target = parsePush { message.data[it] } ?: return
        PushNotifier.show(applicationContext, target)
    }
}

/**
 * Notificação local do push com o app aberto (em segundo plano o próprio FCM mostra a do servidor). O texto é sempre o genérico de
 * [pushText]; o Intent leva só o tipo e os ids opacos, com as mesmas chaves que o FCM põe nos extras, então os dois caminhos abrem a tela igual.
 */
internal object PushNotifier {
    const val CHANNEL = "nora_push"

    fun ensureChannel(context: Context) {
        NotificationManagerCompat.from(context).createNotificationChannel(NotificationChannelCompat.Builder(CHANNEL, NotificationManagerCompat.IMPORTANCE_HIGH)
            .setName("Avisos da rede")
            .setDescription("Avisos enviados pela rede: vaga liberada, comunicado, confirmação de presença e resposta de pedidos de família.")
            .setShowBadge(true)
            .build())
    }

    @SuppressLint("MissingPermission") // a permissão é conferida logo antes de postar.
    fun show(context: Context, target: PushTarget) {
        val manager = NotificationManagerCompat.from(context)
        val permitted = Build.VERSION.SDK_INT < 33 || ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
        if (!permitted || !manager.areNotificationsEnabled() ||
            !pushAllowed(target.type, SettingsStore.flashNotificationsEnabled(context), SettingsStore.announcementNotificationsEnabled(context))) return
        ensureChannel(context)
        val id = listOf(target.type.wire, target.unitId, target.dependentId, target.appointmentId).hashCode()
        val open = PendingIntent.getActivity(context, id, Intent(context, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            putExtra(PUSH_TYPE, target.type.wire)
            target.unitId?.let { putExtra(PUSH_UNIT_ID, it) }
            target.dependentId?.let { putExtra(PUSH_DEPENDENT_ID, it) }
            target.appointmentId?.let { putExtra(PUSH_APPOINTMENT_ID, it) }
        }, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val (title, text) = pushText(target.type)
        val publicVersion = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle("NORA")
            .setContentText("Você tem um aviso.")
            .build()
        manager.notify(target.type.wire, id, NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle(title)
            .setContentText(text)
            .setContentIntent(open)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_EVENT)
            .setVisibility(NotificationCompat.VISIBILITY_PRIVATE)
            .setPublicVersion(publicVersion)
            .build())
    }
}
