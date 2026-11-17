import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.compose.compiler)
    alias(libs.plugins.screenshot)
}

// Client ID web do Google (entrar com Google). Vem de local.properties ou de -PNORA_GOOGLE_WEB_CLIENT_ID; vazio por padrão, e então o
// botão do Google não aparece. Nenhuma credencial vai para o repositório.
val googleWebClientId: String = run {
    val local = Properties()
    val file = rootProject.file("local.properties")
    if (file.exists()) file.inputStream().use { local.load(it) }
    (providers.gradleProperty("NORA_GOOGLE_WEB_CLIENT_ID").orNull ?: local.getProperty("NORA_GOOGLE_WEB_CLIENT_ID") ?: "").trim()
}

// Push (Firebase): o plugin só entra quando o arquivo de configuração existe; sem ele o app compila e o push fica desligado.
// app/google-services.json fica fora do Git (.gitignore).
if (file("google-services.json").exists()) apply(plugin = libs.plugins.google.services.get().pluginId)

android {
    experimentalProperties["android.experimental.enableScreenshotTest"] = true
    namespace = "com.example.app_nora"
    compileSdk { version = release(37) }
    defaultConfig {
        applicationId = "com.example.app_nora"
        minSdk = 24
        targetSdk = 37
        versionCode = 1
        versionName = "1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        buildConfigField("String", "NORA_GOOGLE_WEB_CLIENT_ID", "\"${googleWebClientId.replace("\\", "\\\\").replace("\"", "\\\"")}\"")
    }
    buildTypes {
        release {
            optimization {
                enable = true
                packageScope = setOf("androidx.**", "kotlin.**", "kotlinx.**")
            }
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    buildFeatures {
        compose = true
        buildConfig = true
    }
    testOptions {
        // Os testes locais do controlador exercitam caminhos que chamam android.util.Log (que vira no-op).
        unitTests.isReturnDefaultValues = true
    }
}

dependencies {
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.icons)
    // Entrar com Google pelo Credential Manager.
    implementation(libs.androidx.credentials)
    implementation(libs.androidx.credentials.play.services)
    implementation(libs.google.googleid)
    // Push (FCM): só funciona com app/google-services.json; sem ele o Firebase não inicializa e o app segue com o ciclo de 30 s.
    implementation(platform(libs.firebase.bom))
    implementation(libs.firebase.messaging)
    debugImplementation(libs.androidx.compose.test.manifest)
    testImplementation(libs.junit)
    // Implementação real de org.json: o android.jar dos testes locais só tem stubs, e o NoraApi lê JSON.
    testImplementation(libs.org.json)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.test)
    androidTestImplementation(libs.androidx.junit)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(libs.androidx.test.runner)
    screenshotTestImplementation(libs.screenshot.validation.api)
    screenshotTestImplementation(libs.androidx.compose.tooling)
}
