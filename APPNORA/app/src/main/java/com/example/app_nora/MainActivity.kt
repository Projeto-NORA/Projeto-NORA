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
import com.example.app_nora.api.FirebasePush
import com.example.app_nora.api.PatientController
import com.example.app_nora.api.PushHub
import com.example.app_nora.api.PushNotifier
import com.example.app_nora.api.PushScreen
import com.example.app_nora.api.parsePush
import com.example.app_nora.api.toOpenRequest
import com.example.app_nora.preferences.SettingsStore
import com.example.app_nora.ui.NoraApp

class MainActivity : ComponentActivity() {
    // UBS da notificação de Vaga Relâmpago tocada; só o identificador da UBS viaja no Intent.
    private var flashUnit by mutableStateOf<String?>(null)
    // Vínculo do dependente da oferta tocada (só o id do vínculo viaja no Intent) e pedido para abrir Avisos (comunicados).
    private var flashDependent by mutableStateOf<String?>(null)
    private var openNotices by mutableStateOf(false)
    // Push do servidor: abrir Consultas (janela de presença) em vez da Vaga Relâmpago, e abrir Família.
    private var openAppointments by mutableStateOf(false)
    private var openFamily by mutableStateOf(false)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        // Em recriação (rotação) o Intent original ainda existe; o pedido já foi tratado na primeira criação.
        if (savedInstanceState == null) applyIntent(intent)
        // O canal do push existe antes de qualquer mensagem: sem ele o FCM mostraria a notificação num canal genérico.
        PushNotifier.ensureChannel(applicationContext)
        val settings = SettingsStore(applicationContext)
        val patient = ViewModelProvider(this)[PatientViewModel::class.java]
        setContent {
            NoraApp(settings, patient.controller, openFlashUnit = flashUnit, openFlashDependent = flashDependent,
                onFlashOpened = { flashUnit = null; flashDependent = null; openAppointments = false }, openAppointments = openAppointments,
                openNotices = openNotices, onNoticesOpened = { openNotices = false },
                openFamily = openFamily, onFamilyOpened = { openFamily = false })
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        applyIntent(intent)
    }

    /**
     * O que o Intent pede: a notificação local de oferta ou de comunicado (extras próprios) ou um push do servidor, cujo `data` chega nos
     * extras com as chaves `type`, `unitId`, `dependentId` e `appointmentId` (também quando o toque foi na notificação que o FCM mostrou).
     * O push só abre tela: ids que não são GUID são descartados e a decisão de trocar de pessoa ou de UBS é a de `flashOpenDecision`.
     */
    private fun applyIntent(intent: Intent) {
        intent.getStringExtra(EXTRA_FLASH_UNIT)?.let { flashUnit = it; flashDependent = intent.getStringExtra(EXTRA_FLASH_DEPENDENT); openAppointments = false }
        if (intent.getBooleanExtra(EXTRA_OPEN_NOTICES, false)) openNotices = true
        val request = parsePush { intent.getStringExtra(it) }?.toOpenRequest() ?: return
        when (request.screen) {
            PushScreen.FLASH, PushScreen.APPOINTMENTS -> {
                flashUnit = request.unitId
                flashDependent = request.dependentId
                openAppointments = request.screen == PushScreen.APPOINTMENTS
            }
            PushScreen.NOTICES -> openNotices = true
            PushScreen.FAMILY -> openFamily = true
        }
    }

    companion object {
        const val EXTRA_FLASH_UNIT = "com.example.app_nora.FLASH_UNIT"
        const val EXTRA_FLASH_DEPENDENT = "com.example.app_nora.FLASH_DEPENDENT"
        const val EXTRA_OPEN_NOTICES = "com.example.app_nora.OPEN_NOTICES"
    }
}

class PatientViewModel(application: Application) : AndroidViewModel(application) {
    val controller = PatientController(application).also {
        // Sem app/google-services.json o provedor é nulo: o push fica desligado e nada mais muda.
        it.pushTokenProvider = FirebasePush.tokenProvider(application)
        PushHub.attach(it)
    }
}
