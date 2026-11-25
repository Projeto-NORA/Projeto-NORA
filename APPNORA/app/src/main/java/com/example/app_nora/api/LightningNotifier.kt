package com.example.app_nora.api

import android.Manifest
import android.annotation.SuppressLint
import android.app.Notification
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.media.AudioAttributes
import android.media.RingtoneManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.app.NotificationChannelCompat
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.example.app_nora.LightningAlertActivity
import com.example.app_nora.MainActivity
import com.example.app_nora.R

/** Vibração do alerta: pulsos longos, como um alarme (espera, vibra, pausa…). */
internal val LIGHTNING_VIBRATION = longArrayOf(0, 900, 500, 900, 500, 1400, 800)

/**
 * Alerta de Vaga Relâmpago, como um alerta da Defesa Civil: notificação de prioridade máxima num canal próprio com som de alarme e
 * vibração, que insiste (`FLAG_INSISTENT`) até a pessoa interagir ou até o prazo da oferta, e abre [LightningAlertActivity] por cima
 * da tela de bloqueio (full-screen intent). Disparado pelos dois caminhos que trazem oferta nova: o ciclo de 30 s do serviço de
 * conexão e o push do servidor. É um único objeto do processo, então a mesma oferta nunca alerta duas vezes.
 *
 * O Intent leva só o id opaco da oferta, o da UBS e, para dependentes, o do vínculo; nada de nome, CPF ou token. Os detalhes que a
 * tela cheia mostra vêm da lista em memória ([notice]); a tela de bloqueio da bandeja mostra uma versão genérica.
 */
internal object LightningAlerts {
    const val CHANNEL = "nora_lightning_alarm"
    /** Canal antigo (sem som de alarme): o som de um canal não muda depois de criado, então ele é substituído. */
    private const val LEGACY_CHANNEL = "nora_lightning"
    const val NOTIFICATION_ID = 20

    private val notified = NotifiedOffers()
    private val open = LinkedHashMap<String, OfferNotice>()
    /** Ofertas cujo alerta já foi visto na tela cheia: a notificação volta em silêncio. */
    private val quieted = HashSet<String>()

    /** A oferta aberta com este id, para a tela cheia; `null` se ela já não está aberta (ou o processo foi reiniciado). */
    @Synchronized fun notice(offerId: String?): OfferNotice? = offerId?.let { open[it] }

    /**
     * Alerta as ofertas abertas ainda não alertadas e retira as que deixaram de estar abertas. Com [enabled] falso ou sem a permissão
     * do Android, nada é postado e o que estava na bandeja sai. Devolve as ofertas alertadas agora.
     */
    @Synchronized fun sync(context: Context, state: PatientState, enabled: Boolean, now: Long = System.currentTimeMillis()): List<OfferNotice> {
        val manager = NotificationManagerCompat.from(context)
        val current = if (enabled && canNotify(context)) openOfferNotices(state, now) else emptyList()
        open.clear(); current.forEach { open[it.offer.id] = it }
        for (id in notified.retain(current.mapTo(HashSet()) { it.offer.id })) { manager.cancel(id, NOTIFICATION_ID); quieted -= id }
        if (current.isEmpty()) return emptyList()
        ensureChannel(context)
        val fresh = notified.markFresh(current.map { it.offer.id })
        val posted = current.filter { it.offer.id in fresh }
        for (notice in posted) post(context, notice, now, loud = true)
        return posted
    }

    /** Retira todos os alertas (fim da conexão ou logout). */
    @Synchronized fun clear(context: Context) {
        val manager = NotificationManagerCompat.from(context)
        for (id in notified.clear()) manager.cancel(id, NOTIFICATION_ID)
        open.clear(); quieted.clear()
    }

    /** A pessoa respondeu ou abriu a oferta: o alerta some da bandeja (o id continua marcado, para não voltar). */
    @Synchronized fun dismiss(context: Context, offerId: String) {
        NotificationManagerCompat.from(context).cancel(offerId, NOTIFICATION_ID)
        quieted -= offerId
    }

    /** A tela cheia apareceu e toca o próprio alarme: a notificação continua na bandeja, mas sem som nem tela cheia de novo. */
    @Synchronized fun quiet(context: Context, offerId: String, now: Long = System.currentTimeMillis()) {
        val notice = open[offerId] ?: return
        if (!quieted.add(offerId) || !canNotify(context)) return
        val manager = NotificationManagerCompat.from(context)
        manager.cancel(offerId, NOTIFICATION_ID)
        post(context, notice, now, loud = false)
    }

    /** Android 14+: o usuário pode negar o full-screen intent; abaixo disso a permissão é concedida na instalação. */
    fun canUseFullScreen(context: Context): Boolean = NotificationManagerCompat.from(context).canUseFullScreenIntent()

    /** Tela do sistema onde a pessoa permite os alertas em tela cheia (Android 14+); nas versões anteriores, as notificações do app. */
    fun fullScreenSettingsIntent(context: Context): Intent =
        if (Build.VERSION.SDK_INT >= 34) Intent(Settings.ACTION_MANAGE_APP_USE_FULL_SCREEN_INTENT, Uri.parse("package:${context.packageName}"))
        else Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName)

    private fun canNotify(context: Context): Boolean =
        (Build.VERSION.SDK_INT < 33 || ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) &&
            NotificationManagerCompat.from(context).areNotificationsEnabled()

    private fun alarmSound(): Uri? = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM)
        ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION)

    private fun ensureChannel(context: Context) {
        val manager = NotificationManagerCompat.from(context)
        manager.deleteNotificationChannel(LEGACY_CHANNEL)
        manager.createNotificationChannel(NotificationChannelCompat.Builder(CHANNEL, NotificationManagerCompat.IMPORTANCE_HIGH)
            .setName("Vaga Relâmpago (alerta)")
            .setDescription("Alerta com som e vibração quando abre uma vaga para antecipar sua consulta. Aparece mesmo com a tela bloqueada.")
            .setSound(alarmSound(), AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
            .setVibrationEnabled(true)
            .setVibrationPattern(LIGHTNING_VIBRATION)
            .setLightsEnabled(true)
            .setShowBadge(true)
            .build())
    }

    /** Abre o app com a oferta (a mesma decisão do toque na notificação: UBS e, se for o caso, o dependente). */
    fun openAppIntent(context: Context, notice: OfferNotice, decision: String? = null): Intent =
        Intent(context, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            putExtra(MainActivity.EXTRA_FLASH_UNIT, notice.unitId)
            notice.dependentId?.let { putExtra(MainActivity.EXTRA_FLASH_DEPENDENT, it) }
            if (decision != null) {
                putExtra(MainActivity.EXTRA_FLASH_OFFER, notice.offer.id)
                putExtra(MainActivity.EXTRA_FLASH_DECISION, decision)
            }
        }

    @SuppressLint("MissingPermission") // canNotify() confere a permissão antes de qualquer chamada.
    private fun post(context: Context, notice: OfferNotice, now: Long, loud: Boolean) {
        val offer = notice.offer
        val flags = PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        val open = PendingIntent.getActivity(context, offer.id.hashCode(), openAppIntent(context, notice), flags)
        val accept = PendingIntent.getActivity(context, offer.id.hashCode() * 31 + 1, openAppIntent(context, notice, "accept"), flags)
        val decline = PendingIntent.getActivity(context, offer.id.hashCode() * 31 + 2, openAppIntent(context, notice, "decline"), flags)
        val fullScreen = PendingIntent.getActivity(context, offer.id.hashCode() * 31 + 3,
            Intent(context, LightningAlertActivity::class.java).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_NO_USER_ACTION)
                putExtra(LightningAlertActivity.EXTRA_OFFER, offer.id)
            }, flags)
        val publicVersion = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle("Vaga Relâmpago")
            .setContentText("Abriu uma vaga para antecipar sua consulta.")
            .build()
        val builder = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle(offerNoticeTitle(notice))
            .setContentText(offerNoticeText(notice))
            .setStyle(NotificationCompat.BigTextStyle().bigText("Antecipe sua consulta. ${offerNoticeText(notice)}"))
            .setContentIntent(fullScreen)
            .addAction(0, "Aceitar", accept)
            .addAction(0, "Recusar", decline)
            .addAction(0, "Ver detalhes", open)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setVisibility(NotificationCompat.VISIBILITY_PRIVATE)
            .setPublicVersion(publicVersion)
        if (loud) {
            // Abaixo do Android 8 o som e a vibração são da notificação; depois, do canal.
            builder.setSound(alarmSound()).setVibrate(LIGHTNING_VIBRATION).setFullScreenIntent(fullScreen, true)
        } else builder.setSilent(true).setOnlyAlertOnce(true)
        // O alerta some sozinho no fim do prazo; até lá, a contagem regressiva fica visível.
        offer.expiresAtMillis?.let {
            builder.setWhen(it).setShowWhen(true).setUsesChronometer(true).setChronometerCountDown(true)
            builder.setTimeoutAfter((it - now).coerceAtLeast(1_000))
        }
        val notification = builder.build()
        // Som e vibração repetem até a pessoa interagir (abrir a bandeja, tocar ou responder) ou até o prazo.
        if (loud) notification.flags = notification.flags or Notification.FLAG_INSISTENT
        NotificationManagerCompat.from(context).notify(offer.id, NOTIFICATION_ID, notification)
    }
}
