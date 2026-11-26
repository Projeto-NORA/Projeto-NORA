package com.example.app_nora

import android.content.Context
import android.content.ContextWrapper
import android.content.SharedPreferences
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.example.app_nora.api.StoredSession
import com.example.app_nora.api.secureSessionStorage
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.xmlpull.v1.XmlPullParser
import java.io.File

/**
 * A sessão guardada com a chave real do Android Keystore (AES-GCM), no arquivo de preferências próprio: o que vai ao disco é cifrado, um dado
 * corrompido é apagado sem derrubar o app e o arquivo está nas regras de exclusão do backup. Roda no aparelho ou emulador (a cifra em si, com
 * chave de software, é coberta pelos testes locais `SessionStorageTest`).
 */
@RunWith(AndroidJUnit4::class)
class SecureSessionStorageTest {
    private val real: Context = InstrumentationRegistry.getInstrumentation().targetContext
    // Arquivo de preferências de teste: nunca toca a sessão guardada do app de verdade.
    private val context = object : ContextWrapper(real) {
        override fun getSharedPreferences(name: String, mode: Int): SharedPreferences = super.getSharedPreferences("${name}_keystore_checks", mode)
    }
    private val storage = secureSessionStorage(context)
    private val session = StoredSession("token-de-teste-secreto-987", "2099-01-01T00:00:00Z", "patient-id", "Maria da Silva", "https://nora.example.test")
    private val prefsFile get() = File(real.applicationInfo.dataDir, "shared_prefs/nora_secure_session_keystore_checks.xml")

    @Before fun clean() = storage.clear()
    @After fun cleanAfter() = storage.clear()

    @Test fun savedSessionComesBackAndTheFileOnDiskHoldsNothingReadable() {
        assertFalse(storage.hasSaved())
        assertTrue(storage.save(session))
        assertTrue(storage.hasSaved())
        assertEquals(session, secureSessionStorage(context).load())
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
        context.getSharedPreferences("nora_secure_session", Context.MODE_PRIVATE).edit().putString("session", "00ff").commit()
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
        assertEquals("backup automático até a API 30", listOf("nora_secure_session.xml"), excludedPaths("backup_rules"))
        assertEquals("backup na nuvem e transferência de aparelho (API 31+)",
            listOf("nora_secure_session.xml", "nora_secure_session.xml"), excludedPaths("data_extraction_rules"))
    }
}
