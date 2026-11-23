package com.example.app_nora.ui

import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.ColorMatrix

/** Adapta a marca para fundo escuro: azul vira claro, detalhes brancos viram azul e verde/amarelo mantêm contraste. */
private val darkBrandFilter = ColorFilter.colorMatrix(ColorMatrix(floatArrayOf(
    1.4997f, -2.6321f, -0.2938f, 0f, 372.6612f,
    0.4157f, -1.2764f, -0.2746f, 0f, 323.5206f,
    0.2669f, -1.7179f,  0.6536f, 0f, 258.3581f,
    0f,       0f,       0f,      1f, 0f,
)))

internal fun noraBrandColorFilter(dark: Boolean): ColorFilter? = if (dark) darkBrandFilter else null
