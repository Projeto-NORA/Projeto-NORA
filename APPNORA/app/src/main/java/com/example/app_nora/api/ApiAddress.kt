package com.example.app_nora.api

import java.net.URI

object ApiAddress {
    fun normalize(value: String, debug: Boolean): String {
        val uri = try { URI(value.trim()) } catch (_: java.net.URISyntaxException) {
            throw IllegalArgumentException("Informe um endereço válido para o serviço NORA.")
        }
        val secure = uri.scheme == "https"
        val emulator = debug && uri.scheme == "http" && uri.host == "10.0.2.2"
        require((secure || emulator) && !uri.host.isNullOrBlank() && uri.userInfo == null &&
            uri.query == null && uri.fragment == null && (uri.path.isNullOrEmpty() || uri.path == "/") &&
            (uri.port == -1 || uri.port in 1..65535)) {
            "Use o endereço HTTPS da unidade${if (debug) " ou HTTP em 10.0.2.2 para o emulador" else ""}."
        }
        return uri.toString().trimEnd('/')
    }
}
