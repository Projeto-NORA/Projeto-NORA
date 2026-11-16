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

/**
 * Notificação local dos comunicados da rede, disparada pelo ciclo de 30 s do serviço de conexão (não há push). Canal próprio
 * ("Comunicados da rede", importância padrão). Só leva o título do comunicado; o toque abre Avisos e o Intent não carrega texto,
 * id, token nem CPF. A tela de bloqueio mostra uma versão genérica. Cada id é avisado uma única vez.
 */
internal class CommunityNotifier(private val context: Context) {
    private val manager = NotificationManagerCompat.from(context)
    private val notified = NotifiedAnnouncements()

    /**
     * Avisa os comunicados novos de [fresh] que ainda não foram avisados. Com [enabled] falso (preferência desligada) ou sem a
     * permissão do Android nada é postado, mas os ids ficam vistos para não despejar avisos antigos ao ligar a preferência.
     */
    fun notify(fresh: List<Announcement>, enabled: Boolean) {
        if (fresh.isEmpty()) return
        val toPost = announcementsToNotify(fresh, enabled && canNotify(), notified)
        if (toPost.isEmpty()) return
        ensureChannel()
        // Uma notificação por comunicado até três; acima disso, uma só resume todos (nenhuma tempestade de avisos).
        if (toPost.size <= 3) for (item in toPost) post(item.id, announcementNoticeTitle(1), item.title)
        else post(SUMMARY_ID, announcementNoticeTitle(toPost.size), announcementNoticeText(toPost))
    }

    /** Retira os avisos de comunicado (fim da conexão ou logout). */
    fun clear() { for (id in notified.clear()) manager.cancel(id, NOTIFICATION_ID) }

    private fun canNotify(): Boolean =
        (Build.VERSION.SDK_INT < 33 || ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) &&
            manager.areNotificationsEnabled()

    private fun ensureChannel() {
        manager.createNotificationChannel(NotificationChannelCompat.Builder(CHANNEL, NotificationManagerCompat.IMPORTANCE_DEFAULT)
            .setName("Comunicados da rede")
            .setDescription("Avisa quando a Secretaria publica um comunicado para as suas UBS.")
            .setShowBadge(true)
            .build())
    }

    @SuppressLint("MissingPermission") // canNotify() confere a permissão antes de qualquer chamada.
    private fun post(tag: String, title: String, text: String) {
        val open = PendingIntent.getActivity(context, tag.hashCode(), Intent(context, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            putExtra(MainActivity.EXTRA_OPEN_NOTICES, true)
        }, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val publicVersion = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle("Comunicado da rede")
            .setContentText("Há um novo comunicado para você.")
            .build()
        val notification = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle(title)
            .setContentText(text)
            .setContentIntent(open)
            .setAutoCancel(true)
            .setOnlyAlertOnce(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setCategory(NotificationCompat.CATEGORY_MESSAGE)
            .setVisibility(NotificationCompat.VISIBILITY_PRIVATE)
            .setPublicVersion(publicVersion)
            .build()
        manager.notify(tag, NOTIFICATION_ID, notification)
        notified.markPosted(tag)
    }

    private companion object {
        const val CHANNEL = "nora_announcements"
        const val NOTIFICATION_ID = 30
        const val SUMMARY_ID = "announcements-summary"
    }
}
