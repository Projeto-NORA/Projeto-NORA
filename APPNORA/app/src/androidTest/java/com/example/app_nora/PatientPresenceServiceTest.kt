package com.example.app_nora

import android.app.NotificationManager
import android.content.Context
import android.content.ContextWrapper
import android.content.SharedPreferences
import android.os.Build
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModelProvider
import androidx.test.core.app.ActivityScenario
import androidx.test.platform.app.InstrumentationRegistry
import com.example.app_nora.api.*
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import java.util.concurrent.atomic.AtomicInteger

class PatientPresenceServiceTest {
    @Test fun connectionSurvivesBackgroundAndEndsWhenTaskIsRemoved() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        if (Build.VERSION.SDK_INT >= 33)
            instrumentation.uiAutomation.executeShellCommand("pm grant ${context.packageName} android.permission.POST_NOTIFICATIONS").close()
        val isolated = object : ContextWrapper(context) {
            override fun getSharedPreferences(name: String, mode: Int): SharedPreferences =
                super.getSharedPreferences("${name}_presence_checks", mode)
        }
        val onlineCount = AtomicInteger()
        val offline = AtomicInteger()
        val user = PatientUser("presence-fixture", "Paciente sintético")
        val api = object : PatientApi {
            override suspend fun login(identifier: String, password: String) = PatientSession("fixture-token", "2026-10-11T23:00:00Z", user)
            override suspend fun overview(token: String) = PatientOverview(user, emptyList(), emptyList())
            override suspend fun logout(token: String) = Unit
            override suspend fun respond(token: String, id: String, accept: Boolean) = Unit
            override suspend fun presence(token: String, online: Boolean) {
                if (online) onlineCount.incrementAndGet() else offline.incrementAndGet()
            }
        }
        val controller = PatientController(isolated) { api }
        var originalActivity: MainActivity? = null
        var originalViewModel: PatientViewModel? = null
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            try {
                instrumentation.waitForIdleSync()
                scenario.onActivity { activity ->
                    originalActivity = activity
                    originalViewModel = ViewModelProvider(activity)[PatientViewModel::class.java]
                    controller.changeAddress("https://nora.example.test")
                    runBlocking { controller.login("fixture@example.test", "fixture-only-password") }
                    PatientPresenceService.start(activity, controller)
                }
                await { onlineCount.get() > 0 }
                val notifications = requireNotNull(context.getSystemService(NotificationManager::class.java))
                await { notifications.activeNotifications.any { it.notification.extras.getString("android.title") == "NORA · conexão ativa" } }
                val foregroundBeats = onlineCount.get()
                scenario.moveToState(Lifecycle.State.CREATED)
                await(40_000) { onlineCount.get() > foregroundBeats }
                assertNotNull(controller.state.session)
                assertEquals(0, offline.get())
                assertTrue(notifications.activeNotifications.any { it.notification.extras.getString("android.title") == "NORA · conexão ativa" })
                val connectionNotice = notifications.activeNotifications.single { it.notification.extras.getString("android.title") == "NORA · conexão ativa" }
                connectionNotice.notification.contentIntent.send()
                instrumentation.waitForIdleSync()
                scenario.onActivity { activity ->
                    assertSame("Notification recreated the Activity", originalActivity, activity)
                    assertSame("Notification discarded the retained account owner", originalViewModel, ViewModelProvider(activity)[PatientViewModel::class.java])
                }
                assertNotNull(controller.state.session)
                assertEquals(0, offline.get())
                scenario.onActivity { it.finishAndRemoveTask() }
                await { offline.get() > 0 && controller.state.session == null }
                await { notifications.activeNotifications.none { it.notification.extras.getString("android.title") == "NORA · conexão ativa" } }
            } finally { PatientPresenceService.stop(context) }
        }
    }

    private fun await(timeoutMillis: Long = 10_000, condition: () -> Boolean) {
        val deadline = System.nanoTime() + timeoutMillis * 1_000_000L
        while (!condition() && System.nanoTime() < deadline) Thread.sleep(50)
        assertTrue("Presence lifecycle did not reach the expected state", condition())
    }
}
