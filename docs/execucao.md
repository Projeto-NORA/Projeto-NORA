# Execução e validação

## WPF

Requer Windows com .NET SDK 10. Foi usado SDK 10.0.401. As duas soluções individuais continuam disponíveis; a solução integrada inclui os executáveis, `Nora.Desktop` e o harness sem pacotes de testes externos.

```powershell
dotnet build NORA.slnx --configuration Release --nologo -m:1
dotnet run --project OBS-NORA/OBS-NORA.csproj
dotnet run --project Painel-Municipal/Painel-Municipal.csproj
dotnet run --configuration Release --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj
```

No sandbox desta sessão, a tentativa inicial de build da solução com paralelismo padrão falhou sem diagnóstico de compilação. A execução sequencial com `-m:1 --disable-build-servers` passou com zero erros e avisos. Os builds individuais também passaram. `dotnet --info` encontrou restrição de acesso ao Service Control Manager; isso não impediu os builds.

Os checks verificam preferências, corrupção de arquivo, isolamento dos aplicativos, erros de armazenamento, tema dinâmico, contraste, evento atrasado do Windows, navegação, restauração, seleção acessível de aparência, tabela/contexto, rolagem e binding. Renderizam as telas nativas para PNG, com janelas fora da área visível. O parâmetro `--preview-dir <pasta>` após `--` escolhe a pasta das prévias. Preferências do usuário são preservadas: os testes usam diretórios temporários próprios.

## Android

O projeto mantém AGP 9.4.1, Gradle 9.6, compile/target SDK 37 e min SDK 24. Usa Compose BOM 2026.09.00, plugin Compose Kotlin 2.4.10 e Activity Compose 1.13.0. O Java 21 instalado substituiu o critério Java 25 do template, cujo provisionamento via Foojay retornava HTTP 400. JDK 21 atende ao mínimo 17 documentado para AGP 9.4.

Abra `APPNORA` no Android Studio e configure o SDK local, ou defina `ANDROID_HOME` na sessão. Não versionar `local.properties`. Na sessão de validação, `JAVA_HOME` apontou para o Java 21 instalado e `GRADLE_USER_HOME` para `.build/gradle-user` do repositório.

```powershell
Set-Location APPNORA
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug :app:assembleDebugAndroidTest --console=plain
# Requer dispositivo/emulador conectado:
.\gradlew.bat :app:connectedDebugAndroidTest --console=plain
```

Artefatos: `APPNORA/app/build/outputs/apk/debug/app-debug.apk` e APK de testes em `APPNORA/app/build/outputs/apk/androidTest/debug/`. Relatórios de lint e testes locais ficam em `APPNORA/app/build/reports/`.

Build do app, build dos testes, três testes locais e lint passaram. O lint reportou zero erros e um aviso de versão Kotlin 2.4.20 disponível; a versão documentada e compatível 2.4.10 foi mantida. Quatro casos instrumentados foram compilados, incluindo regressão de rolagem, mas não executados. `connectedDebugAndroidTest` foi tentado e falhou com “No connected devices”. O AVD existente exige o driver de hypervisor ausente; não foram instalados drivers nem alteradas configurações do Windows.

É necessário conectar um Android ou preparar o emulador para confirmar toque, reabertura real, tema Sistema, fonte ampliada e layout em telas maiores. Ver P-003.

## Referências oficiais de compatibilidade

- [AGP 9.4 e requisitos de Gradle/JDK/SDK](https://developer.android.com/build/releases/agp-9-4-0-release-notes).
- [Kotlin integrado ao AGP](https://developer.android.com/build/migrate-to-built-in-kotlin).
- [Dependências e compilador Compose](https://developer.android.com/develop/ui/compose/setup-compose-dependencies-and-compiler).
- [AndroidX Activity](https://developer.android.com/jetpack/androidx/releases/activity).
