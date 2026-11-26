package com.example.app_nora

import android.content.Context
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.app_nora.api.SESSION_PREFERENCES_FILE
import com.example.app_nora.api.StoredSession
import com.example.app_nora.api.secureSessionStorage
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.xmlpull.v1.XmlPullParser
import java.io.File
import java.security.KeyStore

private const val TEST_FILE = "nora_secure_session_keystore_checks"
private const val TEST_ALIAS = "nora.patient.session.keystore-checks"

/**
 * A sessão guardada com a chave real do Android Keystore (AES-GCM), no arquivo de preferências próprio: o que vai ao disco é cifrado, um dado
 * corrompido é apagado sem derrubar o app e o arquivo está nas regras de exclusão do backup. Roda no aparelho ou emulador (a cifra em si, com
 * chave de software, é coberta pelos testes locais `SessionStorageTest`).
 */
@RunWith(AndroidJUnit4::class)
class SecureSessionStorageTest {
    private val real: Context = InstrumentationRegistry.getInstrumentation().targetContext
    // Arquivo de preferências e chave do Keystore só de teste: o dado corrompido descarta a chave, e a do app de verdade não pode ir junto.
    private val storage = secureSessionStorage(real, TEST_FILE, TEST_ALIAS)
    private val session = StoredSession("token-de-teste-secreto-987", "2099-01-01T00:00:00Z", "patient-id", "Maria da Silva", "https://nora.example.test")
    private val prefsFile get() = File(real.applicationInfo.dataDir, "shared_prefs/$TEST_FILE.xml")

    @Before fun clean() = storage.clear()
    @After fun cleanAfter() {
        storage.clear()
        KeyStore.getInstance("AndroidKeyStore").apply { load(null) }.deleteEntry(TEST_ALIAS)
    }

    @Test fun savedSessionComesBackAndTheFileOnDiskHoldsNothingReadable() {
        assertFalse(storage.hasSaved())
        assertTrue(storage.save(session))
        assertTrue(storage.hasSaved())
        assertEquals(session, secureSessionStorage(real, TEST_FILE, TEST_ALIAS).load())
        val xml = prefsFile.readText()
        listOf(session.token, session.fullName, session.userId, session.address).forEach { assertFalse("$it em texto no disco", xml.contains(it)) }
    }

    @Test fun clearRemovesTheSession() {
        storage.save(session)
        storage.clear()
        assertFalse(storage.hasSaved())
        assertNull(storage.load())
    }

    @Test fun corruptedValueIsErasedAndAsksForLoginWithoutCrashing() {
        storage.save(session)
        real.getSharedPreferences(TEST_FILE, Context.MODE_PRIVATE).edit().putString("session", "00ff").commit()
        assertNull(storage.load())
        assertFalse("o dado ilegível foi apagado", storage.hasSaved())
        assertTrue("uma sessão nova volta a ser guardada", storage.save(session))
        assertEquals(session, storage.load())
    }

    /** Os caminhos `<exclude>` de um XML de regras de backup do app (compilado em binário, então lido pelo parser). */
    private fun excludedPaths(name: String): List<String> {
        val parser = real.resources.getXml(real.resources.getIdentifier(name, "xml", real.packageName))
        val paths = mutableListOf<String>()
        while (parser.next() != XmlPullParser.END_DOCUMENT) {
            if (parser.eventType == XmlPullParser.START_TAG && parser.name == "exclude") paths += parser.getAttributeValue(null, "path").orEmpty()
        }
        return paths
    }

    @Test fun sessionFileIsExcludedFromEveryBackupPath() {
        // O nome excluído é o mesmo arquivo que o app grava: renomear um sem o outro deixaria a sessão ir para o backup.
        val file = "$SESSION_PREFERENCES_FILE.xml"
        assertEquals("backup automático até a API 30", listOf(file), excludedPaths("backup_rules"))
        assertEquals("backup na nuvem e transferência de aparelho (API 31+)", listOf(file, file), excludedPaths("data_extraction_rules"))
    }
}
