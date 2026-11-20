package com.example.app_nora.ui

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.ReadOnlyComposable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat
import com.example.app_nora.preferences.Appearance

private val Light = lightColorScheme(
    primary = Color(0xFF0B3B66), onPrimary = Color.White,
    primaryContainer = Color(0xFFDCEEFF), onPrimaryContainer = Color(0xFF092B4A),
    secondary = Color(0xFF087FA8), onSecondary = Color.White,
    secondaryContainer = Color(0xFFDDF4FA), onSecondaryContainer = Color(0xFF073D52),
    tertiary = Color(0xFF08758A), onTertiary = Color.White,
    tertiaryContainer = Color(0xFFD9F1F4), onTertiaryContainer = Color(0xFF073E49),
    background = Color(0xFFF4F8FC), onBackground = Color(0xFF152D43),
    surface = Color(0xFFFFFEFF), onSurface = Color(0xFF152D43),
    surfaceVariant = Color(0xFFEAF2F8), onSurfaceVariant = Color(0xFF536B80),
    outline = Color(0xFF667F95), outlineVariant = Color(0xFFD5E2EC),
    error = Color(0xFFB3261E), onError = Color.White,
)
private val Dark = darkColorScheme(
    primary = Color(0xFF79BDED), onPrimary = Color(0xFF062B47),
    primaryContainer = Color(0xFF16466B), onPrimaryContainer = Color(0xFFE6F4FF),
    secondary = Color(0xFF70CBE9), onSecondary = Color(0xFF05384D),
    secondaryContainer = Color(0xFF14516A), onSecondaryContainer = Color(0xFFDDF5FC),
    tertiary = Color(0xFF72D0D1), onTertiary = Color(0xFF063D46),
    tertiaryContainer = Color(0xFF14535B), onTertiaryContainer = Color(0xFFD9F1F4),
    background = Color(0xFF081B2B), onBackground = Color(0xFFF2F7FB),
    surface = Color(0xFF102A40), onSurface = Color(0xFFF2F7FB),
    surfaceVariant = Color(0xFF19374F), onSurfaceVariant = Color(0xFFBDCFDC),
    outline = Color(0xFF91AFC4), outlineVariant = Color(0xFF304B61),
    error = Color(0xFFFFB4AB), onError = Color(0xFF690005),
)

/**
 * Cores de estado que o Material não traz: sucesso (deu certo, concluído) e atenção (espera alguém, precisa de olhar). Cada uma tem o
 * tom forte (marca, texto sobre o fundo da tela) e o par container/conteúdo das etiquetas. A cor nunca é a única pista: as etiquetas
 * sempre levam marca e texto ([StatusTag]).
 */
@Immutable
class StatusColors(
    val success: Color, val successContainer: Color, val onSuccessContainer: Color,
    val warning: Color, val warningContainer: Color, val onWarningContainer: Color,
)

private val LightStatus = StatusColors(
    success = Color(0xFF1E6B3A), successContainer = Color(0xFFDDF4E3), onSuccessContainer = Color(0xFF0C3A1B),
    warning = Color(0xFF8A5100), warningContainer = Color(0xFFFFEBC2), onWarningContainer = Color(0xFF4A2B00),
)
private val DarkStatus = StatusColors(
    success = Color(0xFF7FD79A), successContainer = Color(0xFF17472B), onSuccessContainer = Color(0xFFD6F5E0),
    warning = Color(0xFFF0B95A), warningContainer = Color(0xFF5A3B00), onWarningContainer = Color(0xFFFFE8BF),
)

private val LocalStatusColors = staticCompositionLocalOf { LightStatus }

/** As cores de sucesso e atenção do tema atual (claro ou escuro). */
val MaterialTheme.statusColors: StatusColors
    @Composable @ReadOnlyComposable get() = LocalStatusColors.current

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
    MaterialTheme(colorScheme = if (dark) Dark else Light) {
        CompositionLocalProvider(LocalStatusColors provides if (dark) DarkStatus else LightStatus, content = content)
    }
}
