package com.example.app_nora

import com.example.app_nora.api.ApiAddress
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
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
    // Android 17 (API 37) exige ACCESS_LOCAL_NETWORK para falar com a rede local; o app só pede quando o endereço é dela.
    @Test fun localNetworkAddressesAreThePrivateAndLinkLocalOnes() {
        listOf("http://10.0.2.2:5080", "https://10.1.2.3", "https://172.16.0.1", "https://172.31.255.254", "https://192.168.0.10:8443",
            "https://169.254.1.1", "https://ubs.local", "https://UBS-Centro.LOCAL:8443").forEach {
            assertTrue(it, ApiAddress.usesLocalNetwork(it))
        }
        listOf("https://nora.example.test", "https://172.32.0.1", "https://172.15.255.255", "https://192.169.0.1", "https://8.8.8.8",
            "http://127.0.0.1:5080", "https://10.0.2.2.example.test", "https://localhost", "", "não é endereço").forEach {
            assertFalse(it, ApiAddress.usesLocalNetwork(it))
        }
    }
}
