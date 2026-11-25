package com.example.app_nora

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.v2.createComposeRule
import com.example.app_nora.api.*
import com.example.app_nora.preferences.UserSettings
import com.example.app_nora.ui.FamilyActions
import com.example.app_nora.ui.NoraPatientContent
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Rule
import org.junit.Test
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone

/** Telas de quem age por um dependente: Vaga Relâmpago aberta pelos botões e pela notificação, Score de Jornada e tela UBS. */
class ActingInteractionTest {
    @get:Rule val compose = createComposeRule()

    private val now = System.currentTimeMillis()
    private val day = 86_400_000L
    private val own = PatientHealthUnit("unit-a", "UBS A", "Centro", "Rua A", -23.5, -46.6)
    private val maria = FamilyLink("dep-1", "approved", "unit-b", "UBS B", "***.456.789-**", "Maria Souza", "2026-11-10T12:00:00Z",
        "2026-11-11T12:00:00Z", null, null)
    private fun iso(millis: Long) = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.ROOT).apply { timeZone = TimeZone.getTimeZone("UTC") }.format(millis)
    private fun offer(id: String, specialty: String) = UnitOffer(id, specialty, "Dra. Ana", iso(now + day), iso(now + day + 1_800_000),
        iso(now + 600_000), "pending")
    private val ownOffer = offer("own-offer", "Cardiologia")
    private val mariaOffer = offer("maria-offer", "Pediatria")

    private fun base(dependent: UnitSnapshot = UnitSnapshot(offers = listOf(mariaOffer), appointments = emptyList())) =
        PatientState(address = "https://nora.example.test", session = PatientSession("test-token", "2099-01-01T00:00:00Z",
            PatientUser("test-user", "Paciente de teste")), units = listOf(own), selectedUnitId = own.id,
            unitData = mapOf(own.id to UnitSnapshot(offers = listOf(ownOffer), appointments = emptyList())),
            family = FamilyState(links = listOf(maria)), dependentData = mapOf(maria.id to dependent))

    /** Estado da tela; o controlador recusa trocar "agindo por" e UBS com uma ação em andamento, e a tela de teste faz o mesmo. */
    private var state by mutableStateOf(PatientState())

    private fun content(initial: PatientState, onRefresh: () -> Unit = {}, flash: () -> Pair<String?, String?> = { null to null },
        onFlashOpened: () -> Unit = {}) {
        state = initial
        compose.setContent {
            val (unit, dependent) = flash()
            NoraPatientContent(UserSettings(), {}, state, onRefresh = onRefresh,
                onSelectUnit = { id -> if (!state.busy && state.actingFor == null) state = state.copy(selectedUnitId = id) },
                family = FamilyActions(onActFor = { id -> if (!state.busy && state.actingFor != id) state = state.copy(actingFor = id) }),
                openFlashUnit = unit, openFlashDependent = dependent, onFlashOpened = onFlashOpened)
        }
    }

    @Test fun ownOfferButtonInNoticesWaitsForTheActionInProgressInsteadOfOpeningTheDependentsView() {
        content(base().copy(actingFor = maria.id, busy = true))
        compose.onNode(hasText("Avisos") and hasClickAction()).performClick()
        compose.onNodeWithText("Ver oportunidade").performScrollTo().performClick()
        // Com a ação em andamento a troca é recusada: a tela não abre a Vaga Relâmpago de Maria no lugar da oferta tocada.
        compose.onNodeWithText("Comunicados da rede e oportunidades para você.").assertExists()
        compose.onNodeWithText("Pediatria").assertDoesNotExist()
        compose.runOnIdle { state = state.copy(busy = false) }
        compose.onNodeWithText("Aceitar e antecipar").performScrollTo().assertExists()
        compose.onNodeWithText("Cardiologia").assertExists()
        compose.runOnIdle { assertNull(state.actingFor) }
    }

    @Test fun dependentOfferButtonOnHomeWaitsForTheActionInProgressInsteadOfOpeningTheOwnView() {
        content(base().copy(busy = true, dependentOffers = listOf(DependentOffers(maria.id, maria.unitId, maria.unitName, "Maria Souza",
            listOf(mariaOffer)))))
        compose.onNodeWithText("Ver oportunidade de Maria Souza").performScrollTo().performClick()
        compose.onNodeWithText("Aceitar e antecipar").assertDoesNotExist()
        compose.runOnIdle { state = state.copy(busy = false) }
        compose.onNodeWithText("Aceitar e antecipar").performScrollTo().assertExists()
        compose.onNodeWithText("Pediatria").assertExists()
        compose.runOnIdle { assertEquals(maria.id, state.actingFor) }
    }

    @Test fun tappingTheDependentsNotificationOnTheOpenFlashScreenRereadsTheOffers() {
        var refreshes = 0
        var opened = 0
        var flash by mutableStateOf<Pair<String?, String?>>(null to null)
        content(base().copy(actingFor = maria.id), onRefresh = { refreshes++ }, flash = { flash }, onFlashOpened = { opened++; flash = null to null })
        compose.onNode(hasText("Oportunidades") and hasClickAction()).performScrollTo().performClick()
        compose.onNodeWithText("Antecipe sua consulta quando abrir uma vaga.").assertExists()
        val before = compose.runOnIdle { refreshes }
        // A notificação de uma oferta nova de Maria chega com a tela de Maria já aberta.
        compose.runOnIdle { flash = maria.unitId to maria.id }
        compose.runOnIdle {
            assertEquals(1, opened)
            assertEquals("a lista de ofertas de Maria é relida", before + 1, refreshes)
        }
    }

    @Test fun openingTheUnitsScreenWhileActingShowsTheOwnUnitsAndNoErrorOfTheDependent() {
        var loads = 0
        var loadsWhileActing = 0
        // Como o controlador: sair do agindo por limpa a mensagem da visão anterior e diz se houve troca.
        val showOwn = { state.actingFor != null && run { state = state.copy(actingFor = null, message = null); true } }
        state = base().copy(actingFor = maria.id, message = DEPENDENT_FORBIDDEN_MESSAGE)
        compose.setContent {
            NoraPatientContent(UserSettings(), {}, state, onShowOwnUnits = showOwn,
                onLoadUnits = { loads++; if (state.actingFor != null) loadsWhileActing++ },
                family = FamilyActions(onActFor = { id -> if (!state.busy && state.actingFor != id) state = state.copy(actingFor = id) }))
        }
        compose.onNodeWithText("UBS próximas").performScrollTo().performClick()
        compose.onNodeWithText("Agora você está vendo as suas UBS").assertExists()
        compose.onAllNodesWithText("UBS A").onFirst().assertExists()
        compose.onNodeWithContentDescription("Remover esta UBS").performScrollTo().assertExists()
        compose.onNodeWithText(DEPENDENT_FORBIDDEN_MESSAGE).assertDoesNotExist()
        compose.runOnIdle {
            assertNull(state.actingFor)
            assertEquals("a tela relê as UBS do próprio responsável", 1, loads)
            assertEquals(0, loadsWhileActing)
        }
        // Já agindo por si, abrir a tela de novo não avisa nada.
        compose.onNodeWithContentDescription("Voltar").performScrollTo().performClick()
        compose.onNodeWithText("UBS próximas").performScrollTo().performClick()
        compose.onNodeWithText("Unidades de saúde").assertExists()
        compose.onNodeWithText("Agora você está vendo as suas UBS").assertDoesNotExist()
    }

    @Test fun journeyIndexSpeaksAboutTheDependentWhenActingForHer() {
        content(base(UnitSnapshot(offers = emptyList(), appointments = emptyList(),
            attendance = UnitAttendance(3, 1, 2, 0.5, 0.67, 12))).copy(actingFor = maria.id))
        compose.onNode(hasText("Jornada") and hasClickAction()).performClick()
        compose.onNodeWithText("Comparecimento de Maria Souza nos últimos 12 meses").performScrollTo().assertExists()
        compose.onNodeWithText("Seu comparecimento", substring = true).assertDoesNotExist()
        compose.onNodeWithText("feitos por você", substring = true).assertDoesNotExist()
        compose.onNodeWithText("Cancelamentos pelo aplicativo: 2").performScrollTo().assertExists()
        // Pontos e prioridade falam da Maria: a descrição do score leva o nome dela e a regra cita o direito dela.
        compose.onNodeWithContentDescription("Score de Maria Souza: 670 de 1000 pontos, faixa Bom").performScrollTo().assertExists()
        compose.onNodeWithText("Como funciona o score?").performScrollTo().performClick()
        compose.onNodeWithText("O score não muda a prioridade clínica nem o direito de Maria Souza ao atendimento.", substring = true)
            .performScrollTo().assertExists()
    }
}
