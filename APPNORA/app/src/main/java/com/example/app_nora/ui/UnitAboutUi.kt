package com.example.app_nora.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.example.app_nora.api.AboutState
import com.example.app_nora.api.UnitProfile
import com.example.app_nora.api.aboutDays
import com.example.app_nora.api.aboutServices
import com.example.app_nora.api.aboutSpecialties
import com.example.app_nora.api.weekdayOf

/**
 * Atendimento de uma UBS vinculada: horário de funcionamento por dia (o de hoje marcado em texto), serviços e especialidades que têm
 * horário na grade. [today] é o dia da semana do aparelho (domingo = 0). Carregando mostra o passo; falha, "Tentar de novo".
 */
@Composable
internal fun UnitAboutCard(unitName: String, about: AboutState?, onRetry: () -> Unit, today: Int = weekdayOf(System.currentTimeMillis())) {
    SectionCard("Atendimento", unitName) {
        val profile = about?.profile
        when {
            about?.error != null -> {
                Text(about.error, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
                TextButton(onClick = onRetry) { Text("Tentar de novo") }
            }
            profile == null -> BusyLine("Carregando o atendimento da UBS…")
            else -> {
                if (about.loading) BusyLine("Atualizando o atendimento da UBS…")
                UnitProfileSections(profile, today)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun UnitProfileSections(profile: UnitProfile, today: Int) {
    Text("Horário de funcionamento", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
    if (!profile.informed) MutedText("Horário não informado pela UBS")
    else aboutDays(profile).forEach { day ->
        val isToday = day.weekday == today
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Text(if (isToday) "${day.label} · hoje" else day.label, Modifier.weight(1f), fontWeight = if (isToday) FontWeight.Bold else FontWeight.Normal,
                color = if (isToday) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface)
            Text(day.text, Modifier.weight(1f), textAlign = TextAlign.End, fontWeight = if (isToday) FontWeight.Bold else FontWeight.Normal,
                color = if (isToday) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
    Text("Serviços", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
    // Texto puro, com as quebras de linha como a UBS escreveu.
    MutedText(aboutServices(profile) ?: "A UBS ainda não informou os serviços")
    Text("Especialidades", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
    val specialties = aboutSpecialties(profile)
    if (specialties.isEmpty()) MutedText("Sem especialidades com horário na grade")
    else FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        specialties.forEach { name ->
            // Rótulo, não botão: a especialidade só informa.
            Surface(shape = RoundedCornerShape(50), color = MaterialTheme.colorScheme.primaryContainer) {
                Text(name, Modifier.padding(horizontal = 12.dp, vertical = 6.dp), style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.onPrimaryContainer)
            }
        }
    }
}
