package com.example.app_nora.ui

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat
import com.example.app_nora.preferences.Appearance

private val Light = lightColorScheme(
    primary = Color(0xFF6D28D9), onPrimary = Color.White,
    primaryContainer = Color(0xFFEDE6FA), onPrimaryContainer = Color(0xFF442079),
    background = Color(0xFFF7F5FB), onBackground = Color(0xFF241B35),
    surface = Color.White, onSurface = Color(0xFF241B35),
    surfaceVariant = Color(0xFFEDE6FA), onSurfaceVariant = Color(0xFF625970),
    outline = Color(0xFF81748F), outlineVariant = Color(0xFFDDD5E8),
    error = Color(0xFFB3261E), onError = Color.White,
)
private val Dark = darkColorScheme(
    primary = Color(0xFFA78BFA), onPrimary = Color(0xFF1A1028),
    primaryContainer = Color(0xFF33234B), onPrimaryContainer = Color(0xFFE4D6FF),
    background = Color(0xFF15111E), onBackground = Color(0xFFF6F3FC),
    surface = Color(0xFF201B2B), onSurface = Color(0xFFF6F3FC),
    surfaceVariant = Color(0xFF33234B), onSurfaceVariant = Color(0xFFC5BDCF),
    outline = Color(0xFFA99BB9), outlineVariant = Color(0xFF514660),
    error = Color(0xFFFFB4AB), onError = Color(0xFF690005),
)

@Composable
fun NoraTheme(appearance: Appearance, content: @Composable () -> Unit) {
    val dark = appearance.isDark(isSystemInDarkTheme())
    val view = LocalView.current
    SideEffect {
        (view.context as? Activity)?.window?.let { window ->
            WindowCompat.getInsetsController(window, view).apply {
                isAppearanceLightStatusBars = !dark
                isAppearanceLightNavigationBars = !dark
            }
        }
    }
    MaterialTheme(colorScheme = if (dark) Dark else Light, content = content)
}
