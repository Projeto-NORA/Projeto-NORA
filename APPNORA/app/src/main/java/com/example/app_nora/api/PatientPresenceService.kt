package com.example.app_nora.api

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat
import com.example.app_nora.MainActivity
import com.example.app_nora.R
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/** Owns presence while logged in. Bearer tokens never enter intents, files or notifications. */
class PatientPresenceService : Service() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var heartbeat: Job? = null
    private var activeConnection: Connection? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val connection = requestedConnection
        if (connection == null) {
            stopSelf()
            return START_NOT_STICKY
        }
        showConnectionNotification()
        if (activeConnection !== connection) {
            heartbeat?.cancel()
            activeConnection?.let(::sendOffline)
            activeConnection = connection
            heartbeat = scope.launch {
                while (isActive && connection.controller.state.session?.token == connection.session.token) {
                    connection.controller.publishPresence(connection.session, connection.generation, true)
                    if (connection.controller.state.session?.token != connection.session.token) break
                    delay(30_000)
                }
                stopSelf()
            }
        }
        return START_NOT_STICKY
    }

    private fun showConnectionNotification() {
        val manager = requireNotNull(getSystemService(NotificationManager::class.java))
        if (Build.VERSION.SDK_INT >= 26) {
            manager.createNotificationChannel(NotificationChannel(CHANNEL, "Conexão NORA", NotificationManager.IMPORTANCE_LOW).apply {
                setShowBadge(false)
                enableVibration(false)
                setSound(null, null)
            })
        }
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        },
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val notification = NotificationCompat.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.ic_presence)
            .setContentTitle("NORA · conexão ativa")
            .setContentText("Mantendo sua conexão com a unidade.")
            .setContentIntent(open)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()
        if (Build.VERSION.SDK_INT >= 34)
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        else startForeground(NOTIFICATION_ID, notification)
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        activeConnection?.controller?.closeLocalSession()
        endConnection()
        stopSelf()
        super.onTaskRemoved(rootIntent)
    }

    override fun onDestroy() {
        endConnection()
        scope.cancel()
        super.onDestroy()
    }

    private fun endConnection() {
        heartbeat?.cancel()
        heartbeat = null
        val ended = activeConnection
        activeConnection = null
        if (requestedConnection === ended) requestedConnection = null
        stopForeground(STOP_FOREGROUND_REMOVE)
        ended?.let(::sendOffline)
    }

    companion object {
        private const val CHANNEL = "nora_connection"
        private const val NOTIFICATION_ID = 10
        @Volatile private var requestedConnection: Connection? = null

        fun start(context: Context, controller: PatientController) {
            val session = controller.state.session ?: return
            val current = requestedConnection
            if (current != null && current.controller === controller && current.session.token == session.token) return
            requestedConnection = Connection(controller, session, controller.state.address, controller.beginPresence())
            try { ContextCompat.startForegroundService(context, Intent(context, PatientPresenceService::class.java)) }
            catch (_: IllegalStateException) {
                requestedConnection = null
                Log.w("NORA", "O sistema não permitiu iniciar a conexão em segundo plano.")
            }
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, PatientPresenceService::class.java))
            requestedConnection = null
        }

        private fun sendOffline(connection: Connection) {
            // Best effort after task removal: Android may kill the process. The server's 90 s TTL
            // is authoritative. The HTTP client's bounded timeouts also bound this final attempt.
            val finalAttempt = CoroutineScope(SupervisorJob() + Dispatchers.IO)
            finalAttempt.launch {
                try { connection.controller.endPresence(connection.session, connection.address, connection.generation) }
                finally { finalAttempt.cancel() }
            }
        }

        private data class Connection(val controller: PatientController, val session: PatientSession,
            val address: String, val generation: Long)
    }
}
