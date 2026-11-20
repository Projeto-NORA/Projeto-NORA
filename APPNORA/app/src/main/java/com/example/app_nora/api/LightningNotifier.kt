package com.example.app_nora.api

import android.Manifest
import android.annotation.SuppressLint
import android.app.PendingIntent
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.app.NotificationChannelCompat
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.example.app_nora.MainActivity
import com.example.app_nora.R

/** Se o Android deixa o app postar notificações agora (permissão concedida e notificações do app não bloqueadas). */
internal fun notificationsAllowed(context: Context): Boolean =
    (Build.VERSION.SDK_INT < 33 || ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) &&
        NotificationManagerCompat.from(context).areNotificationsEnabled()

/** Abre a tela de notificações deste app nas configurações do Android; em aparelho sem ela (API 24/25 ou ROM diferente) abre os detalhes do app. */
internal fun openNotificationSettings(context: Context) {
    val packageUri = Uri.fromParts("package", context.packageName, null)
    val detailsIntent = Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, packageUri).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) { context.startActivity(detailsIntent); return }
    try {
        context.startActivity(Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
            .putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    } catch (_: ActivityNotFoundException) {
        context.startActivity(detailsIntent)
    }
}

/**
 * Notificação local de Vaga Relâmpago, disparada pelo ciclo de 30 s do serviço de conexão (não há push).
 * Só leva especialidade, UBS, prazo e, para dependentes, o nome deles; o toque abre a tela da oferta com a UBS selecionada
 * (e o dependente, se for dele). Nada de token, CPF ou profissional no Intent ou no texto, e a tela de bloqueio mostra uma versão genérica.
 */
internal class LightningNotifier(private val context: Context) {
    private val manager = NotificationManagerCompat.from(context)
    private val notified = NotifiedOffers()

    /**
     * Posta as ofertas abertas ainda não avisadas e cancela as que deixaram de estar abertas. Com [enabled] falso
     * ou sem a permissão do Android, nada é postado e o que estava na bandeja é retirado.
     */
    fun sync(state: PatientState, enabled: Boolean, now: Long = System.currentTimeMillis()) {
        val open = if (enabled && canNotify()) openOfferNotices(state, now) else emptyList()
        for (id in notified.retain(open.mapTo(HashSet()) { it.offer.id })) manager.cancel(id, NOTIFICATION_ID)
        if (open.isEmpty()) return
        ensureChannel()
        val fresh = notified.markFresh(open.map { it.offer.id })
        for (notice in open) if (notice.offer.id in fresh) post(notice, now)
    }

    /** Retira todos os avisos (fim da conexão ou logout). */
    fun clear() { for (id in notified.clear()) manager.cancel(id, NOTIFICATION_ID) }

    private fun canNotify(): Boolean = notificationsAllowed(context)

    private fun ensureChannel() {
        manager.createNotificationChannel(NotificationChannelCompat.Builder(CHANNEL, NotificationManagerCompat.IMPORTANCE_HIGH)
            .setName("Vaga Relâmpago")
            .setDescription("Avisa quando a UBS oferece uma consulta liberada, com prazo curto para responder.")
            .setShowBadge(true)
            .build())
    }

    @SuppressLint("MissingPermission") // canNotify() confere a permissão antes de qualquer chamada.
    private fun post(notice: OfferNotice, now: Long) {
        val offer = notice.offer
        val open = PendingIntent.getActivity(context, offer.id.hashCode(), Intent(context, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            putExtra(MainActivity.EXTRA_FLASH_UNIT, notice.unitId)
            // Só o id do vínculo (um GUID) viaja no Intent; nunca nome, CPF ou token.
            notice.dependentId?.let { putExtra(MainActivity.EXTRA_FLASH_DEPENDENT, it) }
        }, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val publicVersion = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle("Vaga Relâmpago")
            .setContentText("Há uma oportunidade aguardando sua resposta.")
            .build()
        val builder = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle(offerNoticeTitle(notice))
            .setContentText(offerNoticeText(notice))
            .setContentIntent(open)
            .setAutoCancel(true)
            .setOnlyAlertOnce(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_EVENT)
            .setVisibility(NotificationCompat.VISIBILITY_PRIVATE)
            .setPublicVersion(publicVersion)
        // A notificação some sozinha quando o prazo termina.
        offer.expiresAtMillis?.let { builder.setTimeoutAfter((it - now).coerceAtLeast(1_000)) }
        manager.notify(offer.id, NOTIFICATION_ID, builder.build())
    }

    private companion object {
        const val CHANNEL = "nora_lightning"
        const val NOTIFICATION_ID = 20
    }
}
