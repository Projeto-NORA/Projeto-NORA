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
    // Vínculo do dependente da oferta tocada (só o id do vínculo viaja no Intent) e pedido para abrir Avisos (comunicados).
    private var flashDependent by mutableStateOf<String?>(null)
    private var openNotices by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        // Em recriação (rotação) o Intent original ainda existe; o pedido já foi tratado na primeira criação.
        if (savedInstanceState == null) {
            flashUnit = intent.getStringExtra(EXTRA_FLASH_UNIT)
            flashDependent = intent.getStringExtra(EXTRA_FLASH_DEPENDENT)
            openNotices = intent.getBooleanExtra(EXTRA_OPEN_NOTICES, false)
        }
        val settings = SettingsStore(applicationContext)
        val patient = ViewModelProvider(this)[PatientViewModel::class.java]
        setContent {
            NoraApp(settings, patient.controller, openFlashUnit = flashUnit, openFlashDependent = flashDependent,
                onFlashOpened = { flashUnit = null; flashDependent = null },
                openNotices = openNotices, onNoticesOpened = { openNotices = false })
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        intent.getStringExtra(EXTRA_FLASH_UNIT)?.let { flashUnit = it; flashDependent = intent.getStringExtra(EXTRA_FLASH_DEPENDENT) }
        if (intent.getBooleanExtra(EXTRA_OPEN_NOTICES, false)) openNotices = true
    }

    companion object {
        const val EXTRA_FLASH_UNIT = "com.example.app_nora.FLASH_UNIT"
        const val EXTRA_FLASH_DEPENDENT = "com.example.app_nora.FLASH_DEPENDENT"
        const val EXTRA_OPEN_NOTICES = "com.example.app_nora.OPEN_NOTICES"
    }
}

class PatientViewModel(application: Application) : AndroidViewModel(application) {
    val controller = PatientController(application)
}
