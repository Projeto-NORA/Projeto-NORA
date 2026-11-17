package com.example.app_nora.api

import android.content.Context
import androidx.credentials.CredentialManager
import androidx.credentials.CustomCredential
import androidx.credentials.GetCredentialRequest
import androidx.credentials.exceptions.GetCredentialCancellationException
import androidx.credentials.exceptions.GetCredentialException
import androidx.credentials.exceptions.NoCredentialException
import com.google.android.libraries.identity.googleid.GetGoogleIdOption
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential
import com.google.android.libraries.identity.googleid.GoogleIdTokenParsingException

/** O que o Google devolveu: o ID token (nunca guardado nem registrado em log), o cancelamento da pessoa ou uma falha com texto próprio. */
sealed interface GoogleIdResult {
    class Token(val idToken: String) : GoogleIdResult
    data object Cancelled : GoogleIdResult
    class Failed(val message: String) : GoogleIdResult
}

/**
 * Pede o ID token do Google pelo Credential Manager. Precisa de uma Activity ([activityContext]) para abrir a lista de contas e do
 * client ID web (`NORA_GOOGLE_WEB_CLIENT_ID`); o servidor confere o token (assinatura, emissor, público, validade e e-mail verificado).
 */
internal object GoogleSignIn {
    suspend fun requestIdToken(activityContext: Context, webClientId: String): GoogleIdResult {
        val option = GetGoogleIdOption.Builder()
            // Mostra todas as contas Google do aparelho: a pessoa escolhe, e nada entra sozinho.
            .setFilterByAuthorizedAccounts(false)
            .setServerClientId(webClientId)
            .setAutoSelectEnabled(false)
            .build()
        val request = GetCredentialRequest.Builder().addCredentialOption(option).build()
        return try {
            val credential = CredentialManager.create(activityContext).getCredential(activityContext, request).credential
            if (credential is CustomCredential && credential.type == GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL)
                GoogleIdResult.Token(GoogleIdTokenCredential.createFrom(credential.data).idToken)
            else GoogleIdResult.Failed("O Google devolveu um tipo de acesso que o NORA não usa. Tente de novo.")
        }
        catch (_: GetCredentialCancellationException) { GoogleIdResult.Cancelled }
        catch (_: NoCredentialException) {
            GoogleIdResult.Failed("Não encontramos uma conta Google neste aparelho. Adicione uma conta Google nas configurações do Android e tente de novo.")
        }
        catch (_: GetCredentialException) {
            GoogleIdResult.Failed("Não foi possível abrir o Google agora. Confira a conexão e o Google Play Services e tente de novo.")
        }
        catch (_: GoogleIdTokenParsingException) { GoogleIdResult.Failed("O Google devolveu uma resposta que o NORA não entendeu. Tente de novo.") }
    }
}
