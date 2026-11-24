package com.example.app_nora.ui

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat
import com.example.app_nora.preferences.Appearance

private val Light = lightColorScheme(
    primary = Color(0xFF0B3B66), onPrimary = Color.White,
    primaryContainer = Color(0xFFDCEEFF), onPrimaryContainer = Color(0xFF092B4A),
    secondary = Color(0xFF086887), onSecondary = Color.White,
    secondaryContainer = Color(0xFFDDF4FA), onSecondaryContainer = Color(0xFF073D52),
    tertiary = Color(0xFF08758A), onTertiary = Color.White,
    tertiaryContainer = Color(0xFFD9F1F4), onTertiaryContainer = Color(0xFF073E49),
    background = Color(0xFFF4F8FC), onBackground = Color(0xFF152D43),
    surface = Color(0xFFFFFEFF), onSurface = Color(0xFF152D43),
    surfaceVariant = Color(0xFFEAF2F8), onSurfaceVariant = Color(0xFF3B5369),
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
    surfaceVariant = Color(0xFF19374F), onSurfaceVariant = Color(0xFFD5E2EC),
    outline = Color(0xFF91AFC4), outlineVariant = Color(0xFF304B61),
    error = Color(0xFFFFB4AB), onError = Color(0xFF690005),
)

private val DefaultType = Typography()
private val NoraType = DefaultType.copy(
    bodyLarge = DefaultType.bodyLarge.copy(lineHeight = 24.sp),
    bodyMedium = DefaultType.bodyMedium.copy(fontSize = 15.sp, lineHeight = 22.sp),
    bodySmall = DefaultType.bodySmall.copy(fontSize = 13.sp, lineHeight = 19.sp),
    labelLarge = DefaultType.labelLarge.copy(fontWeight = FontWeight.SemiBold),
    labelMedium = DefaultType.labelMedium.copy(fontSize = 13.sp, lineHeight = 17.sp),
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
    MaterialTheme(colorScheme = if (dark) Dark else Light, typography = NoraType, content = content)
}
