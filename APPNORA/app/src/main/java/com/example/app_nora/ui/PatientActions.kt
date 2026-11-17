package com.example.app_nora.ui

import com.example.app_nora.BuildConfig
import com.example.app_nora.api.GoogleIdResult

// Conjuntos de ações que a tela chama no controlador, agrupados por assunto para a assinatura de NoraPatientContent não crescer
// sem fim. Todos têm padrão que não faz nada (prévias e testes de tela).

/** Aba Família: pedidos de vínculo e "agindo por". */
class FamilyActions(
    val onLoad: () -> Unit = {},
    /** CPF (só dígitos) e id da UBS; `true` quando o pedido foi aceito. */
    val onRequest: suspend (String, String) -> Boolean = { _, _ -> false },
    val onCancelRequest: (String) -> Unit = {},
    val onEnd: (String) -> Unit = {},
    /** Id do vínculo aprovado, ou `null` para agir por si. */
    val onActFor: (String?) -> Unit = {},
)

/** Comunicados da rede e Jornada. */
class CommunityActions(
    /** Leitura completa: ao abrir Avisos. */
    val onLoadAnnouncements: () -> Unit = {},
    val onMarkAnnouncementRead: (String) -> Unit = {},
    val onLoadRecords: () -> Unit = {},
)

/** Sugestão de troca do e-mail da conta. */
class EmailActions(
    val onLoad: () -> Unit = {},
    val onSendCode: () -> Unit = {},
    /** Os 6 dígitos; `true` quando o e-mail foi trocado. */
    val onConfirm: suspend (String) -> Boolean = { false },
    val onDismiss: () -> Unit = {},
)

/**
 * Entrar com Google. [webClientId] vem do build (`NORA_GOOGLE_WEB_CLIENT_ID`, vazio por padrão): vazio esconde os botões.
 * [requestIdToken] abre a lista de contas do Google; as ações que falam com o servidor devolvem `true` quando deram certo.
 */
class GoogleActions(
    val webClientId: String = BuildConfig.NORA_GOOGLE_WEB_CLIENT_ID,
    val requestIdToken: suspend () -> GoogleIdResult = { GoogleIdResult.Cancelled },
    val onSignIn: suspend (String) -> Boolean = { false },
    val onLink: suspend (String) -> Boolean = { false },
    val onUnlink: suspend () -> Boolean = { false },
    val onLoadStatus: () -> Unit = {},
    /** Falha que aconteceu antes de falar com o servidor (o Google não abriu): vira a mensagem da tela. */
    val onFailed: (String) -> Unit = {},
)
