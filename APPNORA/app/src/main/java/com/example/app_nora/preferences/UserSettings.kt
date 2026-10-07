package com.example.app_nora.preferences

/** Local UI choices only; notification delivery and identity belong to future integrations. */
enum class Appearance(val title: String, val description: String) {
    LIGHT("Clara", "Fundo claro e cores suaves"),
    DARK("Escura", "Fundo escuro e conforto visual"),
    SYSTEM("Sistema", "Acompanhar o tema do dispositivo");

    fun isDark(systemDark: Boolean): Boolean = when (this) {
        LIGHT -> false
        DARK -> true
        SYSTEM -> systemDark
    }

    companion object {
        fun fromStored(value: String?) = entries.firstOrNull { it.name == value } ?: SYSTEM
    }
}

data class UserSettings(
    val appearance: Appearance = Appearance.SYSTEM,
    val appointmentNotifications: Boolean = true,
    val flashNotifications: Boolean = true,
    val showGuidance: Boolean = true,
    val comfortableSpacing: Boolean = true,
)
