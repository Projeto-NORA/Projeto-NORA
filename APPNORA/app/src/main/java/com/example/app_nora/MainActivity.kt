package com.example.app_nora

import android.content.Intent
import android.os.Bundle
import android.app.Application
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.ViewModelProvider
import com.example.app_nora.api.PatientController
import com.example.app_nora.preferences.SettingsStore
import com.example.app_nora.ui.NoraApp

class MainActivity : ComponentActivity() {
    // UBS da notificação de Vaga Relâmpago tocada; só o identificador da UBS viaja no Intent.
    private var flashUnit by mutableStateOf<String?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        // Em recriação (rotação) o Intent original ainda existe; o pedido já foi tratado na primeira criação.
        if (savedInstanceState == null) flashUnit = intent.getStringExtra(EXTRA_FLASH_UNIT)
        val settings = SettingsStore(applicationContext)
        val patient = ViewModelProvider(this)[PatientViewModel::class.java]
        setContent { NoraApp(settings, patient.controller, openFlashUnit = flashUnit, onFlashOpened = { flashUnit = null }) }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        intent.getStringExtra(EXTRA_FLASH_UNIT)?.let { flashUnit = it }
    }

    companion object {
        const val EXTRA_FLASH_UNIT = "com.example.app_nora.FLASH_UNIT"
    }
}

class PatientViewModel(application: Application) : AndroidViewModel(application) {
    val controller = PatientController(application)
}
