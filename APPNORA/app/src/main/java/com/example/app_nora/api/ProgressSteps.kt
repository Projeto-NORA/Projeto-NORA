package com.example.app_nora.api

// Passos de uma operação demorada: o que já terminou (✓), o que está em andamento agora e o que falhou. Puro, para a tela só desenhar.

enum class StepState { RUNNING, DONE, FAILED }

/** [detail] só existe no passo que falhou: o motivo, em linguagem da pessoa. */
data class ProgressStep(val label: String, val state: StepState, val detail: String? = null)

data class ProgressSteps(val steps: List<ProgressStep> = emptyList()) {
    /** O passo em andamento agora, se houver. */
    val current: ProgressStep? get() = steps.lastOrNull { it.state == StepState.RUNNING }
    val failed: Boolean get() = steps.any { it.state == StepState.FAILED }

    /** Conclui o passo em andamento (se houver) e começa [label]. */
    fun begin(label: String): ProgressSteps = ProgressSteps(finishRunning() + ProgressStep(label, StepState.RUNNING))

    /** Conclui o passo em andamento sem começar outro. */
    fun finish(): ProgressSteps = ProgressSteps(finishRunning())

    /** Marca o passo em andamento como falho (com o motivo, quando a tela ainda não o mostra em outro lugar). Sem passo em andamento, acrescenta a falha como passo. */
    fun fail(label: String, detail: String? = null): ProgressSteps {
        val running = steps.indexOfLast { it.state == StepState.RUNNING }
        return if (running < 0) ProgressSteps(steps + ProgressStep(label, StepState.FAILED, detail))
        else ProgressSteps(steps.mapIndexed { index, step -> if (index == running) step.copy(state = StepState.FAILED, detail = detail) else step })
    }

    private fun finishRunning(): List<ProgressStep> = steps.map { if (it.state == StepState.RUNNING) it.copy(state = StepState.DONE) else it }
}
