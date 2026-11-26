package com.example.app_nora.api

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Log
import androidx.core.content.edit
import org.json.JSONObject
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * A sessão que fica no disco (decisão de 26/11/2026, que substitui a regra de nunca gravar token): só o necessário para entrar de novo sem
 * pedir a senha. [userId] e [fullName] são a identidade mínima da tela; CPF mascarado e e-mail não são guardados, voltam do
 * `GET /api/auth/me` que confere a sessão ao abrir. [address] prende a sessão ao serviço que a criou: se o endereço do build mudar, o token
 * nunca vai a outro servidor.
 */
data class StoredSession(val token: String, val expiresAt: String, val userId: String, val fullName: String, val address: String) {
    // Nunca imprime o token (nem o resto): a classe pode parar num log ou numa mensagem de falha de teste.
    override fun toString() = "StoredSession(…)"
}

/** Onde a sessão fica guardada entre uma abertura e outra do app. Quem implementa nunca lança: falha vira `null`, `false` ou nada. */
interface SessionStorage {
    /** Há algo guardado (ainda não lido nem validado). Barato: serve para o Início já mostrar "Restaurando a sua sessão…". */
    fun hasSaved(): Boolean
    /** A sessão guardada; `null` sem nada guardado ou com o dado/chave ilegível (nesse caso o que havia já foi apagado). */
    fun load(): StoredSession?
    /** `false` quando não foi possível guardar: o app segue com a sessão só em memória e a pessoa entra de novo ao reabrir. */
    fun save(session: StoredSession): Boolean
    fun clear()
}

/** Sem disco: o padrão do controlador (testes e prévias) e o que vale quando a sessão não deve sobreviver ao processo. */
object NoSessionStorage : SessionStorage {
    override fun hasSaved() = false
    override fun load(): StoredSession? = null
    override fun save(session: StoredSession) = true
    override fun clear() = Unit
}

/** Cifra e decifra o conteúdo guardado. [discardKey] joga fora uma chave que não serve mais (a próxima cifra cria outra). */
interface SessionCipher {
    fun encrypt(plain: ByteArray): ByteArray
    fun decrypt(sealed: ByteArray): ByteArray
    fun discardKey()
}

/** Um único valor de texto guardado em disco (o arquivo de preferências próprio da sessão). */
interface BlobStore {
    fun contains(): Boolean
    fun read(): String?
    fun write(value: String): Boolean
    fun delete()
}

/** Guarda a [StoredSession] como JSON cifrado ([cipher]) em hexadecimal num [BlobStore]. Dado ilegível, de outra versão ou adulterado é apagado. */
class CipheredSessionStorage(private val blob: BlobStore, private val cipher: SessionCipher) : SessionStorage {
    override fun hasSaved() = try { blob.contains() } catch (_: Exception) { false }

    override fun load(): StoredSession? {
        val text = (try { blob.read() } catch (_: Exception) { null }) ?: return null
        return try {
            val json = JSONObject(String(cipher.decrypt(fromHex(text)), Charsets.UTF_8))
            require(json.getInt("v") == VERSION)
            StoredSession(json.getString("token"), json.getString("expiresAt"), json.getString("userId"),
                json.getString("fullName"), json.getString("address")).also { require(it.token.isNotBlank() && it.userId.isNotBlank()) }
        } catch (_: Exception) {
            // Chave perdida (backup restaurado, tela de bloqueio removida), dado corrompido ou formato desconhecido: nada serve mais.
            Log.w("NORA", "A sessão guardada estava ilegível e foi apagada; será preciso entrar de novo.")
            clear()
            runCatching { cipher.discardKey() }
            null
        }
    }

    override fun save(session: StoredSession): Boolean {
        val plain = JSONObject().put("v", VERSION).put("token", session.token).put("expiresAt", session.expiresAt)
            .put("userId", session.userId).put("fullName", session.fullName).put("address", session.address)
            .toString().toByteArray(Charsets.UTF_8)
        // Uma chave que o Keystore não deixa mais usar é trocada uma vez; se ainda assim não der, a sessão fica só em memória.
        repeat(2) {
            try { return blob.write(toHex(cipher.encrypt(plain))) }
            catch (_: Exception) { runCatching { cipher.discardKey() } }
        }
        Log.w("NORA", "Não foi possível guardar a sessão neste aparelho.")
        clear()
        return false
    }

    override fun clear() { try { blob.delete() } catch (_: Exception) { Log.w("NORA", "A sessão guardada não pôde ser apagada.") } }

    private companion object { const val VERSION = 1 }
}

private const val HEX = "0123456789abcdef"
internal fun toHex(bytes: ByteArray): String = buildString(bytes.size * 2) {
    for (byte in bytes) { append(HEX[(byte.toInt() shr 4) and 0xF]); append(HEX[byte.toInt() and 0xF]) }
}
internal fun fromHex(text: String): ByteArray {
    require(text.length % 2 == 0)
    return ByteArray(text.length / 2) { index ->
        val high = HEX.indexOf(text[index * 2]); val low = HEX.indexOf(text[index * 2 + 1])
        require(high >= 0 && low >= 0)
        ((high shl 4) or low).toByte()
    }
}

/**
 * AES-256-GCM com a chave de [keyProvider]. O resultado é `iv (12 bytes) + texto cifrado com a etiqueta de autenticação`; o dado adicional
 * autenticado amarra o conteúdo a este uso, então um valor copiado de outro lugar não é aceito.
 */
class AesGcmSessionCipher(private val keyProvider: () -> SecretKey, private val onDiscard: () -> Unit = {}) : SessionCipher {
    override fun encrypt(plain: ByteArray): ByteArray {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, keyProvider())
        cipher.updateAAD(AAD)
        val iv = cipher.iv
        check(iv.size == IV_BYTES)
        return iv + cipher.doFinal(plain)
    }

    override fun decrypt(sealed: ByteArray): ByteArray {
        require(sealed.size > IV_BYTES)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, keyProvider(), GCMParameterSpec(TAG_BITS, sealed, 0, IV_BYTES))
        cipher.updateAAD(AAD)
        return cipher.doFinal(sealed, IV_BYTES, sealed.size - IV_BYTES)
    }

    override fun discardKey() = onDiscard()

    private companion object {
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV_BYTES = 12
        const val TAG_BITS = 128
        val AAD = "nora-patient-session-v1".toByteArray(Charsets.UTF_8)
    }
}

/** A chave AES do Android Keystore: gerada no aparelho, nunca exportável, e fora do backup do Android. */
internal object KeystoreSessionKey {
    private const val PROVIDER = "AndroidKeyStore"
    private const val ALIAS = "nora.patient.session.v1"

    fun get(): SecretKey {
        val store = KeyStore.getInstance(PROVIDER).apply { load(null) }
        (store.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, PROVIDER)
        generator.init(KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .build())
        return generator.generateKey()
    }

    fun discard() {
        try { KeyStore.getInstance(PROVIDER).apply { load(null) }.deleteEntry(ALIAS) }
        catch (_: Exception) { Log.w("NORA", "A chave da sessão não pôde ser removida.") }
    }
}

/**
 * O arquivo de preferências próprio da sessão (`nora_secure_session.xml`), excluído do backup automático em `backup_rules.xml` e
 * `data_extraction_rules.xml`. Escreve e apaga de forma síncrona: sair da conta não pode deixar o token no disco por mais um instante.
 */
private class PreferencesBlobStore(context: Context) : BlobStore {
    private val preferences = context.getSharedPreferences(FILE, Context.MODE_PRIVATE)
    override fun contains() = preferences.contains(KEY)
    override fun read(): String? = preferences.getString(KEY, null)
    override fun write(value: String): Boolean { preferences.edit(commit = true) { putString(KEY, value) }; return true }
    override fun delete() { preferences.edit(commit = true) { clear() } }

    companion object {
        const val FILE = "nora_secure_session"
        private const val KEY = "session"
    }
}

/** A sessão cifrada com a chave do Android Keystore, no arquivo de preferências próprio. */
fun secureSessionStorage(context: Context): SessionStorage =
    CipheredSessionStorage(PreferencesBlobStore(context), AesGcmSessionCipher(KeystoreSessionKey::get, KeystoreSessionKey::discard))

/** A sessão venceu pelo relógio? Data ilegível conta como vencida: sem prazo conhecido a sessão não é aceita. */
fun sessionExpired(expiresAt: String, nowMillis: Long): Boolean = parseInstantMillis(expiresAt)?.let { it <= nowMillis } ?: true
