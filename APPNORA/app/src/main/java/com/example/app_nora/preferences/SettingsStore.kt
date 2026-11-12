package com.example.app_nora.preferences

import android.content.Context
import android.content.SharedPreferences
import androidx.core.content.edit
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

class SettingsStore(context: Context) {
    private val preferences: SharedPreferences = context.getSharedPreferences("nora_preferences", Context.MODE_PRIVATE)
    var settings by mutableStateOf(read())
        private set

    fun update(value: UserSettings) {
        preferences.edit {
            putString("appearance", value.appearance.name)
            putBoolean("appointment_notifications", value.appointmentNotifications)
            putBoolean("flash_notifications", value.flashNotifications)
            putBoolean("show_guidance", value.showGuidance)
            putBoolean("comfortable_spacing", value.comfortableSpacing)
        }
        settings = value
    }

    companion object {
        /** Lida a cada ciclo pelo serviço de conexão, que não compartilha a instância da tela. */
        fun flashNotificationsEnabled(context: Context): Boolean =
            context.getSharedPreferences("nora_preferences", Context.MODE_PRIVATE).all["flash_notifications"] as? Boolean ?: true
    }

    // A restored/older preference with a different type must not prevent app startup.
    private fun read(): UserSettings {
        val stored = preferences.all
        return UserSettings(
            appearance = Appearance.fromStored(stored["appearance"] as? String),
            appointmentNotifications = stored["appointment_notifications"] as? Boolean ?: true,
            flashNotifications = stored["flash_notifications"] as? Boolean ?: true,
            showGuidance = stored["show_guidance"] as? Boolean ?: true,
            comfortableSpacing = stored["comfortable_spacing"] as? Boolean ?: true,
        )
    }
}
