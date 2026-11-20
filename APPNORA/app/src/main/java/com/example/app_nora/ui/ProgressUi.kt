package com.example.app_nora.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
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
                StepState.RUNNING -> StepRow(step.label, MaterialTheme.colorScheme.onSurfaceVariant) {
                    CircularProgressIndicator(Modifier.size(STEP_MARK_SIZE), color = MaterialTheme.colorScheme.primary, strokeWidth = 2.dp)
                }
                StepState.DONE -> StepRow(step.label.trimEnd('…'), MaterialTheme.statusColors.success) { StepMark("✓", MaterialTheme.statusColors.success) }
                StepState.FAILED -> StepRow(step.label.trimEnd('…') + " — falhou" + (step.detail?.let { ": $it" } ?: ""), MaterialTheme.colorScheme.error) {
                    StepMark("✗", MaterialTheme.colorScheme.error)
                }
            }
        }
    }
}

// Coluna única da marca (✓, ✗ ou indicador girando): o texto de todos os passos começa no mesmo ponto.
private val STEP_MARK_SIZE = 18.dp

@Composable
private fun StepMark(mark: String, color: Color) {
    Text(mark, style = MaterialTheme.typography.bodyMedium, color = color, textAlign = TextAlign.Center)
}

@Composable
private fun StepRow(text: String, color: Color, mark: @Composable () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.width(STEP_MARK_SIZE), contentAlignment = Alignment.Center) { mark() }
        Spacer(Modifier.width(10.dp))
        Text(text, style = MaterialTheme.typography.bodyMedium, color = color)
    }
}
