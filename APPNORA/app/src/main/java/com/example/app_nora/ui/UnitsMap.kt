package com.example.app_nora.ui

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.location.LocationManager
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.SslErrorHandler
import android.net.http.SslError
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import com.example.app_nora.api.PatientHealthUnit
import java.util.Locale

private const val BRAZIL_MAP = "https://www.openstreetmap.org/export/embed.html?bbox=-74%2C-34%2C-34%2C6&layer=mapnik"

/** Coordinates only: never pass identity, patient data or access tokens to the map. */
internal fun mapAround(latitude: Double, longitude: Double): String {
    val lat = latitude.coerceIn(-84.8, 84.8)
    val lon = longitude.coerceIn(-179.8, 179.8)
    fun number(value: Double) = String.format(Locale.ROOT, "%.5f", value)
    return "https://www.openstreetmap.org/export/embed.html?bbox=${number(lon - .04)}%2C${number(lat - .04)}%2C${number(lon + .04)}%2C${number(lat + .04)}&layer=mapnik"
}

private fun mapForUnit(unit: PatientHealthUnit): String {
    val lat = String.format(Locale.ROOT, "%.5f", unit.latitude)
    val lon = String.format(Locale.ROOT, "%.5f", unit.longitude)
    return mapAround(unit.latitude, unit.longitude) + "&marker=$lat%2C$lon"
}

@Composable
internal fun UnitsMap(selectedUnit: PatientHealthUnit? = null, onLocateUnit: (Double, Double) -> PatientHealthUnit? = { _, _ -> null }) {
    val context = LocalContext.current
    var address by remember { mutableStateOf(BRAZIL_MAP) }
    var status by remember { mutableStateOf("Carregando mapa…") }
    var generation by remember { mutableIntStateOf(0) }

    LaunchedEffect(selectedUnit) {
        if (selectedUnit != null) {
            address = mapForUnit(selectedUnit)
            generation++
            status = "Carregando mapa da unidade…"
        }
    }

    fun locate() {
        try {
            val manager = context.getSystemService(Context.LOCATION_SERVICE) as LocationManager
            val location = manager.getProviders(true).mapNotNull { provider ->
                try { manager.getLastKnownLocation(provider) } catch (_: SecurityException) { null }
            }.filter { it.time > System.currentTimeMillis() - 10 * 60 * 1000L }
                .maxByOrNull { it.time }
            if (location == null) {
                status = "Localização recente indisponível. Ative a localização do aparelho e tente novamente."
            } else {
                val nearest = onLocateUnit(location.latitude, location.longitude)
                address = nearest?.let(::mapForUnit) ?: mapAround(location.latitude, location.longitude)
                generation++
                status = if (nearest != null) "Carregando UBS vinculada mais próxima…"
                    else "Nenhuma UBS vinculada disponível; mostrando sua região."
            }
        } catch (_: SecurityException) {
            status = "Permita o acesso à localização para encontrar sua região."
        }
    }

    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) locate() else status = "Localização não autorizada. Você ainda pode explorar o mapa manualmente."
    }
    Card(Modifier.fillMaxWidth(), shape = RoundedCornerShape(24.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(defaultElevation = 4.dp)) {
        Column(Modifier.fillMaxWidth().padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("Mapa da região", style = MaterialTheme.typography.titleMedium)
            Text("Mova o mapa com o dedo e use + e − para aproximar ou afastar.",
                style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text("Usamos uma posição aproximada recente para selecionar a UBS vinculada mais próxima. Sua posição é enviada ao OpenStreetMap ao abrir o mapa da região.",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            OutlinedButton(onClick = {
                if (ContextCompat.checkSelfPermission(context, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED) locate()
                else permission.launch(Manifest.permission.ACCESS_COARSE_LOCATION)
            }, modifier = Modifier.fillMaxWidth()) { Text("Encontrar minha UBS") }
            val activeGeneration = generation
            key(activeGeneration) {
                AndroidView(modifier = Modifier.fillMaxWidth().height(380.dp), factory = { ctx ->
                    WebView(ctx).apply {
                        settings.javaScriptEnabled = true
                        settings.domStorageEnabled = true
                        settings.allowFileAccess = false
                        settings.allowContentAccess = false
                        settings.setGeolocationEnabled(false)
                        settings.userAgentString = settings.userAgentString + " NORA-Paciente/1.0"
                        webViewClient = object : WebViewClient() {
                            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                                val uri = request.url
                                return uri.scheme != "https" || uri.host != "www.openstreetmap.org" || uri.path != "/export/embed.html"
                            }
                            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: WebResourceError) {
                                if (generation == activeGeneration && isMapResource(request))
                                    status = "Não foi possível carregar o mapa completamente. Verifique sua conexão e tente novamente."
                            }
                            override fun onReceivedHttpError(view: WebView, request: WebResourceRequest, errorResponse: WebResourceResponse) {
                                if (generation == activeGeneration && isMapResource(request))
                                    status = "Não foi possível carregar o mapa completamente. Verifique sua conexão e tente novamente."
                            }
                            override fun onReceivedSslError(view: WebView, handler: SslErrorHandler, error: SslError) {
                                handler.cancel()
                                if (generation == activeGeneration)
                                    status = "Não foi possível estabelecer uma conexão segura com o mapa. Tente novamente."
                            }
                            override fun onPageFinished(view: WebView, url: String) {
                                // In a vertically scrolling Compose layout, WebView can report a
                                // zero CSS percentage height even with a nonzero viewport.
                                // Use the actual viewport height and keep it in sync on resize.
                                val uri = android.net.Uri.parse(url)
                                if (uri.scheme == "https" && uri.host == "www.openstreetmap.org" && uri.path == "/export/embed.html") {
                                    view.evaluateJavascript("""
                                        (function () {
                                            function fit() {
                                                var height = window.innerHeight;
                                                var map = document.getElementById('map');
                                                if (!height || !map || !document.body) return;
                                                var size = height + 'px';
                                                var changed = map.style.height !== size;
                                                document.documentElement.style.height = size;
                                                document.body.style.height = size;
                                                map.style.height = size;
                                                if (changed) window.dispatchEvent(new Event('resize'));
                                            }
                                            window.addEventListener('resize', fit);
                                            fit();
                                            window.dispatchEvent(new Event('resize'));
                                            // WebView may finish navigation before its first layout.
                                            // Retry after layout instead of leaving a zero-height map.
                                            var attempts = 0;
                                            var timer = window.setInterval(function () {
                                                fit();
                                                if (++attempts >= 20) window.clearInterval(timer);
                                            }, 250);
                                        })();
                                    """.trimIndent(), null)
                                }
                                if (generation == activeGeneration && status.startsWith("Carregando"))
                                    status = "Se o mapa não aparecer, verifique sua conexão e recarregue."
                            }
                        }
                        loadUrl(address)
                    }
                }, onRelease = { it.stopLoading(); it.destroy() })
            }
            Text(status, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            TextButton(onClick = { generation++; status = "Carregando mapa…" }) { Text("Recarregar mapa") }
            Text("© colaboradores do OpenStreetMap", style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

private fun isMapResource(request: WebResourceRequest): Boolean {
    val uri = request.url
    return request.isForMainFrame || uri.host == "tile.openstreetmap.org" ||
        (uri.host == "www.openstreetmap.org" && uri.path?.startsWith("/assets/embed-") == true)
}
