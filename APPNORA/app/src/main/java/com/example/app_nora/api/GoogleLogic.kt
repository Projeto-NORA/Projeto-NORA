package com.example.app_nora.api

// Regras puras (sem Android) do "Entrar com Google": quando os botões aparecem e o estado lido do servidor.

/** Situação do Google lida do servidor para as Configurações: [status] é nulo até a primeira leitura; [error] é a falha dela. */
data class GoogleState(val status: GoogleStatus? = null, val loading: Boolean = false, val error: String? = null)

/**
 * No login (sem sessão) o botão só depende de o app ter sido compilado com o client ID web (`NORA_GOOGLE_WEB_CLIENT_ID`). Se o servidor
 * estiver com o Google desligado, a tentativa responde 503 `google-disabled` e a tela explica.
 */
fun googleLoginAvailable(webClientId: String): Boolean = webClientId.isNotBlank()

/** Nas Configurações o botão exige também que o servidor diga `enabled` (G1); sem essa leitura nada aparece. */
fun googleSettingsAvailable(webClientId: String, status: GoogleStatus?): Boolean = webClientId.isNotBlank() && status?.enabled == true
