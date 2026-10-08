package com.example.app_nora

import com.example.app_nora.api.ApiAddress
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class ApiAddressTest {
    @Test fun acceptsSecureUnitOriginAndNormalizesTrailingSlash() {
        assertEquals("https://nora.example.test:8443", ApiAddress.normalize(" https://nora.example.test:8443/ ", false))
    }
    @Test fun developmentAllowsOnlyEmulatorLoopback() {
        assertEquals("http://10.0.2.2:5080", ApiAddress.normalize("http://10.0.2.2:5080", true))
        listOf("http://localhost:5080", "http://127.0.0.1", "http://192.168.1.1", "http://10.0.2.2.example.test").forEach {
            assertThrows(IllegalArgumentException::class.java) { ApiAddress.normalize(it, true) }
        }
    }
    @Test fun releaseRejectsAllPlainHttp() {
        assertThrows(IllegalArgumentException::class.java) { ApiAddress.normalize("http://10.0.2.2:5080", false) }
    }
    @Test fun rejectsCredentialsQueryFragmentsAndNonOriginPaths() {
        listOf("https://user:password@nora.example.test", "https://nora.example.test?token=x",
            "https://nora.example.test#x", "https://nora.example.test/api", "https://nora.example.test:70000",
            "https://", "", "file:///tmp/endpoint").forEach {
            assertThrows(IllegalArgumentException::class.java) { ApiAddress.normalize(it, true) }
        }
    }
}
