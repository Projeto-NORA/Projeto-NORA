package com.example.app_nora.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.ProgressSteps
import com.example.app_nora.api.StepState

// Operação demorada mostra o trabalho: indicador girando na cor principal do produto e o passo atual escrito em linguagem da pessoa.

/** Um indicador girando na cor principal do app com o passo atual ao lado. */
@Composable
internal fun BusyLine(text: String, modifier: Modifier = Modifier) {
    Row(modifier.semantics { contentDescription = text }, verticalAlignment = Alignment.CenterVertically) {
        CircularProgressIndicator(Modifier.size(18.dp), color = MaterialTheme.colorScheme.primary, strokeWidth = 2.dp)
        Spacer(Modifier.width(10.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/**
 * Os passos de uma operação de vários passos: os concluídos ficam marcados (✓), o atual gira, e o que falhou fica marcado (✗), com o motivo
 * quando [ProgressSteps] o traz. A marca é texto, nunca só cor.
 */
@Composable
internal fun StepList(steps: ProgressSteps, modifier: Modifier = Modifier) {
    if (steps.steps.isEmpty()) return
    Column(modifier, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        steps.steps.forEach { step ->
            when (step.state) {
                StepState.RUNNING -> BusyLine(step.label)
                StepState.DONE -> Text("✓ ${step.label.trimEnd('…')}", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.primary)
                StepState.FAILED -> Text("✗ ${step.label.trimEnd('…')} — falhou" + (step.detail?.let { ": $it" } ?: ""),
                    style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error)
            }
        }
    }
}
