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
 * horário na grade. [today] é o dia da semana do aparelho (domingo = 0). Carregando mostra o passo; falha, "Tentar novamente".
 */
@Composable
internal fun UnitAboutCard(unitName: String, about: AboutState?, onRetry: () -> Unit, today: Int = weekdayOf(System.currentTimeMillis())) {
    SectionCard("Atendimento", unitName) {
        val profile = about?.profile
        when {
            about?.error != null -> {
                ErrorNotice(about.error)
                RetryButton(onClick = onRetry)
            }
            profile == null -> BusyLine("Carregando o atendimento…")
            else -> {
                if (about.loading) BusyLine("Atualizando o atendimento…")
                UnitProfileSections(profile, today)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun UnitProfileSections(profile: UnitProfile, today: Int) {
    Text("Horário de funcionamento", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
    if (!profile.informed) MutedText("Horário não informado")
    else aboutDays(profile).forEach { day ->
        val isToday = day.weekday == today
        // O nome do dia ocupa o que precisa, mas quebra em vez de cortar (fonte ampliada); o horário usa o resto, alinhado à direita.
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text(if (isToday) "${day.label} · hoje" else day.label, Modifier.weight(1f, fill = false).padding(end = 12.dp), fontWeight = if (isToday) FontWeight.Bold else FontWeight.Normal,
                color = if (isToday) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurface)
            Text(day.text.replace(" · ", "\n"), textAlign = TextAlign.End, fontWeight = if (isToday) FontWeight.Bold else FontWeight.Normal,
                color = if (isToday) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
    Text("Serviços", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
    // Texto puro, com as quebras de linha como a UBS escreveu.
    MutedText(aboutServices(profile) ?: "Serviços não informados")
    Text("Especialidades", style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.Bold)
    val specialties = aboutSpecialties(profile)
    if (specialties.isEmpty()) MutedText("Nenhuma especialidade com horário aberto")
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
