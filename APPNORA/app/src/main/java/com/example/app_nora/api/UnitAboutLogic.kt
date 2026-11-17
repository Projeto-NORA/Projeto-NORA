package com.example.app_nora.api

import java.util.Calendar

// Regras puras (sem Android) do cartão "Atendimento" da UBS: horário por dia, serviços e especialidades.

/** Leitura do perfil de uma UBS: [profile] é nulo até a primeira leitura bem-sucedida; [error] é a falha da última. */
data class AboutState(val profile: UnitProfile? = null, val loading: Boolean = false, val error: String? = null)

/** Domingo = 0, como no servidor (`DayOfWeek`). */
val WEEKDAY_NAMES = listOf("Domingo", "Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado")

/** [text] é "Fechado" ou os intervalos (`08:00 – 12:00 · 13:00 – 17:00`), na ordem do dia. */
data class AboutDay(val weekday: Int, val label: String, val text: String)

/** Dia da semana do relógio do aparelho, com domingo = 0. */
fun weekdayOf(millis: Long): Int = Calendar.getInstance().apply { timeInMillis = millis }.get(Calendar.DAY_OF_WEEK) - 1

/** `HH:mm`; o servidor pode mandar segundos (`08:00:00`), que não interessam à pessoa. */
fun clockOnly(value: String): String = if (value.length >= 5 && value[2] == ':') value.substring(0, 5) else value

/**
 * As sete linhas de domingo a sábado. Sem intervalo no dia é "Fechado"; intervalos com dia fora de 0..6 são ignorados.
 * Vazio quando a UBS ainda não informou ([UnitProfile.informed] falso): a tela diz isso em vez de listar dias.
 */
fun aboutDays(profile: UnitProfile): List<AboutDay> {
    if (!profile.informed) return emptyList()
    return WEEKDAY_NAMES.mapIndexed { weekday, name ->
        val intervals = profile.openingHours.filter { it.weekday == weekday }.sortedBy { clockOnly(it.opens) }
        AboutDay(weekday, name, if (intervals.isEmpty()) "Fechado" else intervals.joinToString(" · ") { "${clockOnly(it.opens)} – ${clockOnly(it.closes)}" })
    }
}

/** O texto de serviços como a UBS escreveu (quebras de linha incluídas); em branco é "não informado" (`null`). */
fun aboutServices(profile: UnitProfile): String? = profile.services?.takeIf { it.isNotBlank() }

/** Nomes das especialidades com horário na grade, na ordem que o servidor mandou (já por nome). */
fun aboutSpecialties(profile: UnitProfile): List<String> = profile.specialties.map { it.name }.filter { it.isNotBlank() }
