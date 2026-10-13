package com.example.app_nora

import android.os.Bundle
import android.app.Application
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.ViewModelProvider
import com.example.app_nora.api.PatientController
import com.example.app_nora.preferences.SettingsStore
import com.example.app_nora.ui.NoraApp

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        val settings = SettingsStore(applicationContext)
        val patient = ViewModelProvider(this)[PatientViewModel::class.java]
        setContent { NoraApp(settings, patient.controller) }
    }
}

class PatientViewModel(application: Application) : AndroidViewModel(application) {
    val controller = PatientController(application)
}
