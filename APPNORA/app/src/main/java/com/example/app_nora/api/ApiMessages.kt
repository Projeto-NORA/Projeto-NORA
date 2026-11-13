package com.example.app_nora.api

/** Operação em andamento; define o texto mostrado ao paciente para o mesmo status HTTP. */
enum class ApiContext {
    GENERIC, LOGIN, PASSWORD_RESET, REGISTRATION,
    /** Lista de UBS vinculadas e pedido de novos vínculos. */
    UNIT_LIST,
    /** Leituras dentro de uma UBS: consultas, fila e ofertas. */
    UNIT,
    APPOINTMENT_CANCEL, OFFER_RESPOND,
    /** Sair da fila de uma UBS. */
    QUEUE_CANCEL,
    /** Remover o vínculo com uma UBS. */
    UNLINK,
    /** Troca de senha com a sessão aberta. */
    PASSWORD_CHANGE,
    /** Presença e saída da conta. */
    SESSION,
}

const val UNIT_FORBIDDEN_MESSAGE = "Seu cadastro nesta UBS está desativado ou sem vínculo. Procure a unidade."
const val OFFER_UNAVAILABLE_MESSAGE = "Esta oportunidade não está mais disponível."
const val INCOMPATIBLE_RESPONSE_MESSAGE = "O serviço retornou uma resposta incompatível. Contate a unidade."

/** Senha atual incorreta na troca de senha (400); nunca 401, para a sessão não ser encerrada. */
const val INVALID_CURRENT_PASSWORD = "invalid-current-password"
/** Conta com senha provisória: 403 em qualquer rota até definir uma nova senha. */
const val PASSWORD_CHANGE_REQUIRED = "password-change-required"
const val PASSWORD_CHANGE_REQUIRED_MESSAGE = "É preciso definir uma nova senha antes de continuar."

/** Vocabulário fixo do campo `code` do problem+json (docs/api.md). Qualquer outro valor é descartado. */
private val KNOWN_CODES = setOf("invalid-input", "unauthorized", "forbidden", "not-found", "conflict",
    "too-many-requests", "internal-error", "service-unavailable", INVALID_CURRENT_PASSWORD, PASSWORD_CHANGE_REQUIRED)

fun knownProblemCode(raw: String?): String? = raw?.takeIf { it in KNOWN_CODES }

/**
 * Texto em português escolhido pelo app a partir do status HTTP, do `code` conhecido e da operação.
 * O corpo do servidor (title/detail) nunca é exibido.
 */
fun failureMessage(status: Int, code: String?, context: ApiContext): String {
    val kind = knownProblemCode(code) ?: when (status) {
        400 -> "invalid-input"
        401 -> "unauthorized"
        403 -> "forbidden"
        404 -> "not-found"
        409 -> "conflict"
        429 -> "too-many-requests"
        503 -> "service-unavailable"
        in 500..599 -> "internal-error"
        else -> ""
    }
    val unitScoped = context == ApiContext.UNIT || context == ApiContext.APPOINTMENT_CANCEL || context == ApiContext.OFFER_RESPOND ||
        context == ApiContext.QUEUE_CANCEL || context == ApiContext.UNLINK
    return when (kind) {
        INVALID_CURRENT_PASSWORD -> "A senha atual está incorreta."
        PASSWORD_CHANGE_REQUIRED -> PASSWORD_CHANGE_REQUIRED_MESSAGE
        "invalid-input" ->
            if (context == ApiContext.PASSWORD_CHANGE) "A nova senha precisa ter de 8 a 256 caracteres e ser diferente da atual."
            else "Confira os dados informados."
        "unauthorized" ->
            if (context == ApiContext.LOGIN) "CPF, e-mail ou senha inválidos." else "Sua sessão terminou. Entre novamente."
        "forbidden" -> when {
            unitScoped -> UNIT_FORBIDDEN_MESSAGE
            context == ApiContext.LOGIN -> "Esta conta não pertence ao aplicativo do paciente."
            else -> "Esta conta não tem acesso a esta operação."
        }
        "not-found" -> when (context) {
            ApiContext.APPOINTMENT_CANCEL -> "Esta consulta não foi encontrada. Atualize a lista."
            ApiContext.OFFER_RESPOND -> "Esta oportunidade não existe mais. Atualize a lista."
            ApiContext.QUEUE_CANCEL -> "Esta solicitação não foi encontrada."
            ApiContext.UNIT, ApiContext.UNLINK -> "Esta UBS ou este registro não está mais disponível."
            else -> "O registro não está mais disponível."
        }
        "conflict" -> when (context) {
            ApiContext.APPOINTMENT_CANCEL -> "Esta consulta já foi alterada pela unidade e não pode mais ser cancelada. Atualize a lista."
            ApiContext.OFFER_RESPOND -> OFFER_UNAVAILABLE_MESSAGE
            ApiContext.QUEUE_CANCEL -> "Esta solicitação já foi encerrada."
            ApiContext.REGISTRATION -> "O cadastro mudou. Confira os dados e tente novamente."
            else -> "O registro mudou. Atualize e tente novamente."
        }
        "too-many-requests" ->
            if (context == ApiContext.PASSWORD_RESET) "Muitos pedidos de código. Aguarde alguns minutos antes de tentar novamente."
            else "Muitas tentativas. Aguarde alguns minutos e tente novamente."
        "service-unavailable" ->
            if (unitScoped || context == ApiContext.UNIT_LIST) "A base de uma UBS está temporariamente indisponível. Tente novamente em instantes."
            else "O serviço está temporariamente indisponível. Tente novamente."
        "internal-error" -> "O serviço encontrou um erro. Tente novamente mais tarde."
        else -> "Não foi possível concluir a solicitação. Tente novamente."
    }
}
