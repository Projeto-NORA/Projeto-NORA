package com.example.app_nora

import com.example.app_nora.api.*
import org.junit.Assert.*
import org.junit.Test
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey

/** Guardar, ler e apagar a sessão cifrada (AES-GCM de verdade, com chave de software no lugar do Keystore) e o prazo pelo relógio. */
class SessionStorageTest {
    private class MemoryBlob(var value: String? = null) : BlobStore {
        var failWrites = false
        var refuseWrites = false
        override fun contains() = value != null
        override fun read() = value
        override fun write(value: String): Boolean {
            if (failWrites) throw IllegalStateException("disco cheio")
            if (refuseWrites) return false // o commit das preferências devolveu false
            this.value = value; return true
        }
        override fun delete() { value = null }
    }

    private fun newKey(): SecretKey = KeyGenerator.getInstance("AES").apply { init(256) }.generateKey()

    private class Keys(var key: SecretKey) { var discarded = 0 }

    private fun cipher(keys: Keys) = AesGcmSessionCipher({ keys.key }, { keys.discarded++ })

    private val session = StoredSession("token-secreto-de-teste-123", "2026-11-27T12:00:00Z", "patient-id", "Maria da Silva", "https://nora.example.test")

    @Test fun savedSessionIsReadBackIdenticallyAndNothingReadableIsOnDisk() {
        val blob = MemoryBlob()
        val storage = CipheredSessionStorage(blob, cipher(Keys(newKey())))
        assertFalse(storage.hasSaved())
        assertTrue(storage.save(session))
        assertTrue(storage.hasSaved())
        assertEquals(session, storage.load())
        val onDisk = requireNotNull(blob.value)
        // O que vai ao disco é só hexadecimal: nem o token, nem o nome, nem o endereço aparecem, em texto ou em bytes.
        assertTrue(onDisk.all { it in "0123456789abcdef" })
        listOf(session.token, session.fullName, session.userId, session.address).forEach { secret ->
            assertFalse(onDisk.contains(secret))
            assertFalse(onDisk.contains(toHex(secret.toByteArray())))
        }
    }

    @Test fun eachSaveUsesAFreshIvSoTheSameSessionNeverLooksTheSame() {
        val blob = MemoryBlob()
        val storage = CipheredSessionStorage(blob, cipher(Keys(newKey())))
        storage.save(session); val first = blob.value
        storage.save(session)
        assertNotEquals(first, blob.value)
        assertEquals(session, storage.load())
    }

    @Test fun clearRemovesTheSession() {
        val blob = MemoryBlob()
        val storage = CipheredSessionStorage(blob, cipher(Keys(newKey())))
        storage.save(session)
        storage.clear()
        assertFalse(storage.hasSaved())
        assertNull(storage.load())
        assertNull(blob.value)
        storage.clear() // idempotente
    }

    @Test fun nothingSavedReadsAsNull() {
        assertNull(CipheredSessionStorage(MemoryBlob(), cipher(Keys(newKey()))).load())
    }

    @Test fun corruptedDataIsErasedAndAsksForLoginWithoutCrashing() {
        listOf("zz-não-é-hexadecimal", "abc", "", "00".repeat(8), "ff".repeat(64)).forEach { garbage ->
            val keys = Keys(newKey())
            val blob = MemoryBlob(garbage)
            val storage = CipheredSessionStorage(blob, cipher(keys))
            assertNull("lixo: $garbage", storage.load())
            assertNull("o dado ilegível é apagado", blob.value)
            assertEquals("a chave que não serve é descartada", 1, keys.discarded)
        }
    }

    @Test fun tamperedCipherTextFailsTheAuthenticationTagAndIsErased() {
        val blob = MemoryBlob()
        val keys = Keys(newKey())
        val storage = CipheredSessionStorage(blob, cipher(keys))
        storage.save(session)
        val sealed = requireNotNull(blob.value)
        val flipped = sealed.substring(0, sealed.length - 2) + if (sealed.endsWith("00")) "01" else "00"
        blob.value = flipped
        assertNull(storage.load())
        assertNull(blob.value)
    }

    @Test fun aLostKeyMakesTheSavedSessionUnreadableAndItIsErased() {
        val blob = MemoryBlob()
        val keys = Keys(newKey())
        val storage = CipheredSessionStorage(blob, cipher(keys))
        storage.save(session)
        keys.key = newKey() // o Keystore perdeu a chave (backup restaurado, tela de bloqueio removida)
        assertNull(storage.load())
        assertNull(blob.value)
        assertEquals(1, keys.discarded)
        // Depois disso uma sessão nova volta a ser guardada normalmente.
        assertTrue(storage.save(session))
        assertEquals(session, storage.load())
    }

    @Test fun anotherVersionOrMissingFieldIsRefused() {
        val keys = Keys(newKey())
        val cipher = cipher(keys)
        listOf("""{"v":2,"token":"t","expiresAt":"x","userId":"u","fullName":"n","address":"a"}""",
            """{"v":1,"token":"t","expiresAt":"x","userId":"u","fullName":"n"}""",
            """{"v":1,"token":" ","expiresAt":"x","userId":"u","fullName":"n","address":"a"}""",
            "não é json").forEach { payload ->
            val blob = MemoryBlob(toHex(cipher.encrypt(payload.toByteArray())))
            assertNull(payload, CipheredSessionStorage(blob, cipher).load())
            assertNull(blob.value)
        }
    }

    @Test fun aKeyThatCannotBeUsedIsReplacedOnceAndAFailedSaveLeavesNothingBehind() {
        val blob = MemoryBlob("resto-de-uma-sessao-antiga")
        var attempts = 0
        val broken = object : SessionCipher {
            override fun encrypt(plain: ByteArray): ByteArray { attempts++; throw IllegalStateException("Keystore indisponível") }
            override fun decrypt(sealed: ByteArray): ByteArray = throw IllegalStateException()
            override fun discardKey() = Unit
        }
        val storage = CipheredSessionStorage(blob, broken)
        assertFalse(storage.save(session))
        assertEquals("tenta de novo uma vez, com a chave nova", 2, attempts)
        assertNull("nem a sessão antiga sobra no disco", blob.value)
    }

    @Test fun aDiskWriteFailureIsReportedNotThrown() {
        val blob = MemoryBlob().apply { failWrites = true }
        val keys = Keys(newKey())
        assertFalse(CipheredSessionStorage(blob, cipher(keys)).save(session))
        assertEquals("o disco falhou, não a chave: ela continua", 0, keys.discarded)
    }

    @Test fun aRefusedCommitIsReportedAsNotSavedAndLeavesNothingBehind() {
        val blob = MemoryBlob("resto-de-uma-sessao-antiga").apply { refuseWrites = true }
        val keys = Keys(newKey())
        assertFalse("o app precisa saber que a sessão não foi guardada", CipheredSessionStorage(blob, cipher(keys)).save(session))
        assertNull(blob.value)
        assertEquals(0, keys.discarded)
    }

    @Test fun theStoredSessionNeverPrintsItsToken() {
        assertFalse(session.toString().contains(session.token))
        assertFalse(session.toString().contains(session.fullName))
    }

    @Test fun hexRoundTripAndRejectsBadInput() {
        val bytes = ByteArray(256) { it.toByte() }
        assertArrayEquals(bytes, fromHex(toHex(bytes)))
        assertThrows(IllegalArgumentException::class.java) { fromHex("abc") }
        assertThrows(IllegalArgumentException::class.java) { fromHex("zz") }
    }

    @Test fun noSessionStorageNeverKeepsAnything() {
        assertTrue(NoSessionStorage.save(session))
        assertFalse(NoSessionStorage.hasSaved())
        assertNull(NoSessionStorage.load())
    }

    @Test fun expiryFollowsTheClockAndAnUnreadableDateCountsAsExpired() {
        val expires = requireNotNull(parseInstantMillis("2026-11-27T12:00:00Z"))
        assertFalse(sessionExpired("2026-11-27T12:00:00Z", expires - 1))
        assertTrue("no instante exato já venceu", sessionExpired("2026-11-27T12:00:00Z", expires))
        assertTrue(sessionExpired("2026-11-27T12:00:00Z", expires + 1))
        assertFalse("o fuso é respeitado", sessionExpired("2026-11-27T09:00:00-03:00", expires - 1))
        assertTrue(sessionExpired("amanhã", 0))
        assertTrue(sessionExpired("", 0))
    }
}
