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

    /**
     * O endereço aponta para a rede local: IPv4 privado (10/8, 172.16/12, 192.168/16, que inclui o 10.0.2.2 do emulador), de enlace
     * (169.254/16) ou nome `.local`. No Android 17 (API 37) só se fala com eles com ACCESS_LOCAL_NETWORK concedida; sem ela o sistema
     * descarta os pacotes e a conexão expira. Loopback não conta. Um nome comum que resolva para IP privado não é detectável aqui.
     */
    fun usesLocalNetwork(address: String): Boolean {
        val host = try { URI(address.trim()).host } catch (_: java.net.URISyntaxException) { null }?.lowercase() ?: return false
        if (host.endsWith(".local")) return true
        val parts = host.split('.')
        if (parts.size != 4) return false
        val octets = parts.map { part -> part.toIntOrNull()?.takeIf { part.length <= 3 && it in 0..255 } ?: return false }
        val (first, second) = octets
        return first == 10 || (first == 172 && second in 16..31) || (first == 192 && second == 168) || (first == 169 && second == 254)
    }
}
