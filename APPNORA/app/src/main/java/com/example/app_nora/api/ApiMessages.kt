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
    /** Lista de especialidades do catálogo da rede. */
    SPECIALTIES,
    /** Horários livres de uma especialidade numa UBS. */
    AVAILABILITY,
    /** Marcar uma consulta pelo catálogo. */
    BOOKING,
    /** Confirmar presença em uma consulta. */
    CONFIRMATION,
    /** Comunicados da rede (leitura e marcação de lido). */
    ANNOUNCEMENTS,
    /** Exames e vacinas da Jornada. */
    RECORDS,
    /** Lista, cancelamento e encerramento de pedidos de vínculo de família. */
    FAMILY,
    /** Pedir o vínculo com um dependente (CPF e UBS). */
    FAMILY_REQUEST,
    /** Sugestão de troca do e-mail da conta, envio e confirmação do código. */
    EMAIL_CHANGE,
}

const val UNIT_FORBIDDEN_MESSAGE = "Seu cadastro nesta UBS está desativado ou sem vínculo. Procure a unidade."
const val OFFER_UNAVAILABLE_MESSAGE = "Esta oportunidade não está mais disponível."
const val LOCAL_NETWORK_DENIED_MESSAGE = "O endereço do serviço fica na rede local. Permita o acesso à rede local para o NORA nas configurações do Android e tente novamente."
const val CONNECTION_FAILED_MESSAGE = "Não foi possível conectar ao serviço. Confira o endereço e sua conexão e tente novamente."
const val INCOMPATIBLE_RESPONSE_MESSAGE = "O serviço retornou uma resposta incompatível. Contate a unidade."

/** Senha atual incorreta na troca de senha (400); nunca 401, para a sessão não ser encerrada. */
const val INVALID_CURRENT_PASSWORD = "invalid-current-password"
/** Conta com senha provisória: 403 em qualquer rota até definir uma nova senha. */
const val PASSWORD_CHANGE_REQUIRED = "password-change-required"
const val PASSWORD_CHANGE_REQUIRED_MESSAGE = "É preciso definir uma nova senha antes de continuar."

/** Códigos 409 da agenda (docs/api.md, "Agendamento pelo paciente" e "Confirmação de presença"). */
const val BOOKING_WINDOW = "booking-window"
const val SPECIALTY_LIMIT = "specialty-limit"
const val SLOT_UNAVAILABLE = "slot-unavailable"
const val PATIENT_OVERLAP = "patient-overlap"
const val CONFIRMATION_WINDOW = "confirmation-window"
/** Códigos 409 da família (docs/api.md, "Família, e-mail da conta e manutenção"). */
const val FAMILY_LINK_EXISTS = "family-link-exists"
const val FAMILY_LINK_LIMIT = "family-link-limit"
const val FAMILY_LINK_CLOSED = "family-link-closed"
const val DEPENDENT_INACTIVE = "dependent-inactive"

/** O responsável agiu pelo dependente e o servidor recusou (403): vínculo encerrado ou cadastro desativado. */
const val DEPENDENT_FORBIDDEN_MESSAGE = "O vínculo com esta pessoa não está ativo nesta UBS ou o cadastro dela foi desativado. Veja a aba Família."

/** Vocabulário fixo do campo `code` do problem+json (docs/api.md). Qualquer outro valor é descartado. */
private val KNOWN_CODES = setOf("invalid-input", "unauthorized", "forbidden", "not-found", "conflict",
    "too-many-requests", "internal-error", "service-unavailable", INVALID_CURRENT_PASSWORD, PASSWORD_CHANGE_REQUIRED,
    BOOKING_WINDOW, SPECIALTY_LIMIT, SLOT_UNAVAILABLE, PATIENT_OVERLAP, CONFIRMATION_WINDOW,
    FAMILY_LINK_EXISTS, FAMILY_LINK_LIMIT, FAMILY_LINK_CLOSED, DEPENDENT_INACTIVE)

fun knownProblemCode(raw: String?): String? = raw?.takeIf { it in KNOWN_CODES }

/**
 * Texto em português escolhido pelo app a partir do status HTTP, do `code` conhecido e da operação.
 * O corpo do servidor (title/detail) nunca é exibido. [acting] é verdadeiro quando o responsável agia por um dependente.
 */
fun failureMessage(status: Int, code: String?, context: ApiContext, acting: Boolean = false): String {
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
        context == ApiContext.QUEUE_CANCEL || context == ApiContext.UNLINK || context == ApiContext.AVAILABILITY ||
        context == ApiContext.BOOKING || context == ApiContext.CONFIRMATION || context == ApiContext.RECORDS
    return when (kind) {
        INVALID_CURRENT_PASSWORD -> "A senha atual está incorreta."
        PASSWORD_CHANGE_REQUIRED -> PASSWORD_CHANGE_REQUIRED_MESSAGE
        BOOKING_WINDOW -> "Só é possível marcar de 24 horas até 90 dias antes do horário. Escolha outro horário."
        SPECIALTY_LIMIT ->
            if (acting) "Esta pessoa já tem uma consulta futura marcada pelo app nesta especialidade. Cancele essa consulta ou aguarde a data dela para marcar outra."
            else "Você já tem uma consulta futura marcada pelo app nesta especialidade, em alguma das suas UBS. " +
                "Cancele essa consulta ou aguarde a data dela para marcar outra."
        SLOT_UNAVAILABLE -> "Este horário acabou de ser ocupado ou saiu da agenda. Veja a lista atualizada e escolha outro."
        PATIENT_OVERLAP -> if (acting) "Esta pessoa já tem uma consulta neste mesmo horário. Escolha outro horário."
            else "Você já tem uma consulta neste mesmo horário. Escolha outro horário."
        FAMILY_LINK_EXISTS -> "Você já tem um pedido pendente ou um vínculo aprovado com esta pessoa nesta UBS."
        FAMILY_LINK_LIMIT -> "Você chegou ao limite de 20 pedidos e vínculos ativos. Encerre algum antes de pedir outro."
        FAMILY_LINK_CLOSED -> "Este pedido já foi decidido ou encerrado. Atualize a lista."
        DEPENDENT_INACTIVE -> "O cadastro desta pessoa está desativado nesta UBS. Procure a equipe da unidade."
        CONFIRMATION_WINDOW -> "A presença só pode ser confirmada de 48 horas até 2 horas antes da consulta."
        "invalid-input" -> when (context) {
            ApiContext.PASSWORD_CHANGE -> "A nova senha precisa ter de 8 a 256 caracteres e ser diferente da atual."
            ApiContext.BOOKING -> "Este horário não pôde ser conferido. Atualize os horários e escolha outro."
            ApiContext.AVAILABILITY -> "Não foi possível consultar os horários desta especialidade. Escolha outra ou tente mais tarde."
            ApiContext.FAMILY_REQUEST -> "Confira o CPF e a UBS. Você não pode pedir vínculo com o seu próprio CPF."
            ApiContext.EMAIL_CHANGE -> "Confira o código informado."
            else -> "Confira os dados informados."
        }
        "unauthorized" ->
            if (context == ApiContext.LOGIN) "CPF, e-mail ou senha inválidos." else "Sua sessão terminou. Entre novamente."
        "forbidden" -> when {
            unitScoped && acting -> DEPENDENT_FORBIDDEN_MESSAGE
            unitScoped -> UNIT_FORBIDDEN_MESSAGE
            context == ApiContext.LOGIN -> "Esta conta não pertence ao aplicativo do paciente."
            else -> "Esta conta não tem acesso a esta operação."
        }
        "not-found" -> when (context) {
            ApiContext.APPOINTMENT_CANCEL -> "Esta consulta não foi encontrada. Atualize a lista."
            ApiContext.OFFER_RESPOND -> "Esta oportunidade não existe mais. Atualize a lista."
            ApiContext.QUEUE_CANCEL -> "Esta solicitação não foi encontrada."
            ApiContext.CONFIRMATION -> "Esta consulta não foi encontrada. Atualize a lista."
            ApiContext.FAMILY_REQUEST -> "Não encontramos um cadastro ativo com este CPF nesta UBS. Confira o CPF e a UBS ou procure a equipe da unidade."
            ApiContext.FAMILY -> "Este pedido não existe mais. Atualize a lista."
            ApiContext.EMAIL_CHANGE -> "Esta sugestão de e-mail não existe mais."
            ApiContext.ANNOUNCEMENTS -> "Este comunicado não está mais disponível. Atualize a lista."
            ApiContext.UNIT, ApiContext.UNLINK, ApiContext.AVAILABILITY, ApiContext.BOOKING -> "Esta UBS ou este registro não está mais disponível."
            else -> "O registro não está mais disponível."
        }
        "conflict" -> when (context) {
            ApiContext.APPOINTMENT_CANCEL -> "Esta consulta já foi alterada pela unidade e não pode mais ser cancelada. Atualize a lista."
            ApiContext.OFFER_RESPOND -> OFFER_UNAVAILABLE_MESSAGE
            ApiContext.QUEUE_CANCEL -> "Esta solicitação já foi encerrada."
            ApiContext.CONFIRMATION -> "Esta consulta já foi alterada pela unidade e não pode mais ser confirmada. Atualize a lista."
            ApiContext.REGISTRATION -> "O cadastro mudou. Confira os dados e tente novamente."
            ApiContext.EMAIL_CHANGE -> "Este e-mail já é usado por outra conta do NORA, então nada foi trocado. Procure a equipe da UBS."
            ApiContext.FAMILY, ApiContext.FAMILY_REQUEST -> "O pedido mudou. Atualize a lista e tente novamente."
            else -> "O registro mudou. Atualize e tente novamente."
        }
        "too-many-requests" ->
            if (context == ApiContext.PASSWORD_RESET) "Muitos pedidos de código. Aguarde alguns minutos antes de tentar novamente."
            else if (context == ApiContext.EMAIL_CHANGE) "Muitos pedidos de código para este e-mail. Aguarde alguns minutos ou procure a equipe da UBS."
            else if (context == ApiContext.FAMILY_REQUEST) "Muitos pedidos de vínculo em pouco tempo. Aguarde alguns minutos e tente novamente."
            else "Muitas tentativas. Aguarde alguns minutos e tente novamente."
        "service-unavailable" ->
            if (context == ApiContext.BOOKING) "Não foi possível conferir suas consultas em todas as suas UBS agora. Tente marcar novamente em instantes."
            else if (context == ApiContext.EMAIL_CHANGE) "O envio de e-mails não está disponível agora. Tente novamente mais tarde."
            else if (context == ApiContext.FAMILY || context == ApiContext.FAMILY_REQUEST) "A base de uma UBS está temporariamente indisponível. Tente novamente em instantes."
            else if (unitScoped || context == ApiContext.UNIT_LIST) "A base de uma UBS está temporariamente indisponível. Tente novamente em instantes."
            else "O serviço está temporariamente indisponível. Tente novamente."
        "internal-error" -> "O serviço encontrou um erro. Tente novamente mais tarde."
        else -> "Não foi possível concluir a solicitação. Tente novamente."
    }
}
