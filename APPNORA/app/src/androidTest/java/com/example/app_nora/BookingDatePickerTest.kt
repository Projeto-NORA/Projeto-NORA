package com.example.app_nora

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import com.example.app_nora.api.*
import com.example.app_nora.preferences.Appearance
import com.example.app_nora.ui.BookingScreen
import com.example.app_nora.ui.NoraTheme
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Rule
import org.junit.Test
import java.util.Calendar

/**
 * Agendamento sem janela (decisão de 2026-11-17): "Próxima semana" nunca fica desabilitada e "Escolher outra data" abre o seletor,
 * que leva direto à semana do dia escolhido, qualquer que seja a distância.
 */
class BookingDatePickerTest {
    @get:Rule val compose = createComposeRule()

    private val unit = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", -23.5, -46.6)
    private val specialty = Specialty("s1", "Cardiologia")
    private val loads = mutableListOf<Pair<String, Int>>()

    /**
     * Célula de um dia do seletor. Cada célula expõe a data por extenso como texto ("Saturday, November 28, 2026" ou "sábado, 28 de novembro de 2026",
     * conforme o idioma do aparelho); o número do dia é buscado como palavra inteira, para não confundir com o ano.
     */
    private fun dayCell(day: Int) = SemanticsMatcher("célula do dia $day do seletor") { node ->
        node.config.contains(SemanticsActions.OnClick) &&
            node.config.getOrNull(SemanticsProperties.Text)?.any { Regex("\\b$day\\b").containsMatchIn(it.text) } == true
    }

    private fun open() {
        val state = PatientState(
            address = "https://nora.example.test", session = PatientSession("test-token", "2099-01-01T00:00:00Z", PatientUser("test-user", "Paciente de teste")),
            units = listOf(unit), selectedUnitId = unit.id, specialties = listOf(specialty),
            availability = AvailabilityResult(unit.id, specialty.id, 0, emptyList()))
        compose.setContent {
            NoraTheme(Appearance.LIGHT) {
                Column(Modifier.verticalScroll(rememberScrollState())) {
                    BookingScreen(state, {}, {}, {}, { id, week -> loads += id to week }, {}, {}, initialSpecialtyId = specialty.id)
                }
            }
        }
        compose.onNodeWithText("Próxima semana").performScrollTo().assertExists()
    }

    @Test fun nextWeekIsNeverDisabledAndEachClickReadsThatWeek() {
        open()
        repeat(3) { compose.onNodeWithText("Próxima semana").performScrollTo().assertIsEnabled().performClick() }
        compose.waitForIdle()
        compose.runOnIdle { assertEquals(listOf(specialty.id to 0, specialty.id to 1, specialty.id to 2, specialty.id to 3), loads) }
        compose.onNodeWithText("Próxima semana").assertIsEnabled()
        compose.onNodeWithText("Semana anterior").assertIsEnabled()
    }

    @Test fun cancellingThePickerClosesItWithoutChangingTheWeek() {
        open()
        compose.onNodeWithText("Escolher outra data").performScrollTo().performClick()
        compose.onNodeWithText("Ver horários").assertExists().assertIsNotEnabled() // sem dia escolhido, não há o que ver
        compose.onNodeWithText("Cancelar").performClick()
        compose.onNodeWithText("Ver horários").assertDoesNotExist()
        compose.runOnIdle { assertEquals(listOf(specialty.id to 0), loads) }
    }

    @Test fun pickingADayLoadsTheWeekThatContainsIt() {
        // O seletor abre no mês de hoje: o dia 28 existe em todo mês e só é escolhível se ainda não passou.
        val today = Calendar.getInstance()
        assumeTrue("Hoje é dia ${today.get(Calendar.DAY_OF_MONTH)}: o dia 28 deste mês já passou; rode antes do dia 28.", today.get(Calendar.DAY_OF_MONTH) < 28)
        open()
        compose.onNodeWithText("Escolher outra data").performScrollTo().performClick()
        compose.onAllNodes(dayCell(28)).onFirst().performClick()
        compose.onNodeWithText("Ver horários").assertIsEnabled().performClick()
        compose.waitForIdle()
        val day28 = Calendar.getInstance().apply { set(Calendar.DAY_OF_MONTH, 28); set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0) }.timeInMillis
        val expected = bookingWeekIndexFor(day28, System.currentTimeMillis())
        assertTrue("o dia 28 deve cair numa semana depois da de hoje", expected >= 0)
        compose.onNodeWithText("Ver horários").assertDoesNotExist() // o diálogo fecha ao confirmar
        compose.runOnIdle { assertEquals(specialty.id to expected, loads.last()) }
    }
}
