# Android: emulador e testes

## Ambiente verificado em 2026-10-07

Esta retomada roda no notebook com Intel i5-1135G7 e Iris Xe. O SDK está em `C:/Users/mateu/AppData/Local/Android/Sdk`, o Android Emulator é 36.4.10 e o AVD existente é `Pixel_4`, Android 12/API 31, imagem Google Play x86_64. O JBR do Android Studio, em `C:/Program Files/Android/Android Studio/jbr`, fornece Java 21.0.10, compatível com o projeto.

O resultado de `emulator -accel-check` depende do ambiente de execução. Dentro do sandbox retornou código 6, sugerindo habilitar Hyper-V/WHPX; fora do sandbox retornou código 0 e `WHPX(10.0.26200) is installed and usable`. A inicialização normal também confirmou `Windows Hypervisor Platform accelerator is operational`. Portanto, o diagnóstico antigo de driver ausente não descreve este computador. Não foi preciso instalar outro driver nem alterar recursos do Windows.

O pacote AEHD existe no SDK, mas o serviço `aehd` não está instalado (SC 1060). A documentação oficial recomenda WHPX; AEHD será desativado em 31/12/2026 e exige desativar recursos que usam Hyper-V. Não instalar AEHD neste ambiente com WHPX utilizável. A tentativa de consultar recursos com DISM retornou 740 porque sair do sandbox não concede privilégios Administrador; a confirmação direta do emulador é o resultado relevante.

## Inicialização pelo terminal

No PowerShell do usuário, fora de ambiente isolado:

```powershell
$sdkNora = 'C:/Users/mateu/AppData/Local/Android/Sdk'
& "$sdkNora/emulator/emulator.exe" -accel-check
& "$sdkNora/emulator/emulator.exe" -list-avds
& "$sdkNora/emulator/emulator.exe" -avd Pixel_4 -no-snapshot
```

Para teste automatizado sem janela, acrescentar `-no-window -no-audio`. Esta sessão usou essa configuração. Aguardar o dispositivo aparecer como `device`, não `offline`, e `sys.boot_completed` retornar `1`:

```powershell
& "$sdkNora/platform-tools/adb.exe" devices
& "$sdkNora/platform-tools/adb.exe" -s emulator-5554 shell getprop sys.boot_completed
```

Não usar `-wipe-data` no AVD pessoal. Nesta retomada foram preservados seus dados e não foi criado nem excluído dispositivo virtual.

## Android Studio

1. Abrir a pasta `APPNORA` e aguardar o Gradle Sync.
2. Em Settings → Build, Execution, Deployment → Build Tools → Gradle, selecionar o JBR 21 do Android Studio como Gradle JDK.
3. Em Tools → SDK Manager, confirmar o SDK local, Android SDK Platform 37, Build Tools exigidas pelo projeto, Platform Tools e Android Emulator.
4. Em Tools → Device Manager, iniciar `Pixel_4`. Para um novo computador, criar telefone com imagem x86_64 API 31 ou superior.
5. Selecionar o dispositivo já iniciado na barra superior e executar `app`.
6. Para testes instrumentados, executar as classes de `app/src/androidTest` ou usar o wrapper descrito abaixo.

Se um computador diferente não tiver aceleração, habilitar a Plataforma de Hypervisor do Windows em “Ativar ou desativar recursos do Windows” e reiniciar manualmente. Conferir novamente `-accel-check`. Não houve reinicialização nem alteração do Windows nesta sessão. Se necessário, confirmar VT-x/SVM no BIOS com a documentação do fabricante.

## Build e testes

```powershell
Set-Location APPNORA
$env:JAVA_HOME = 'C:/Program Files/Android/Android Studio/jbr'
$env:ANDROID_HOME = 'C:/Users/mateu/AppData/Local/Android/Sdk'
.\gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug :app:assembleDebugAndroidTest --console=plain
.\gradlew.bat :app:connectedDebugAndroidTest --console=plain
```

APK de desenvolvimento: `app/build/outputs/apk/debug/app-debug.apk`. Relatórios de testes/lint: `app/build/reports/`. Não versionar caminhos pessoais, cache Gradle, APKs ou relatórios gerados.

## Evidência desta retomada

- ADB iniciou fora do sandbox; nenhum dispositivo físico conectado.
- WHPX confirmado como instalado e utilizável fora do sandbox.
- Duas tentativas de emulação por software (`-accel off`, com `ANDROID_I_WANT_MY_TCG=yes`, e depois SwiftShader sem Vulkan/AVX) encerraram com código 1 antes de disponibilizar ADB; não validaram o aplicativo. Esta modalidade é descrita oficialmente como sem suporte e muito lenta.
- Inicialização normal acelerada concluiu boot em aproximadamente 230 segundos e ADB reconheceu `emulator-5554` como `device`; `sys.boot_completed` retornou `1`.
- Wrapper com JBR21: `assembleDebug`, `testDebugUnitTest`, `lintDebug`, `assembleDebugAndroidTest` e `connectedDebugAndroidTest` concluíram com **BUILD SUCCESSFUL**, 86 tarefas, em 4 minutos e 34 segundos na execução integrada do root.
- Relatório local: **3 testes**, zero falhas/erros. Lint: **zero erros, um aviso** de versão de dependência disponível.
- Relatório instrumentado `app/build/outputs/androidTest-results/connected/debug/TEST-Pixel_4(AVD) - 12.xml`: **4 testes**, zero falhas/erros/skips. Dois casos de interação Compose e dois de persistência, incluindo rolagem independente e restauração.
- O runner remove o app após a suíte. Para a inspeção extra, o APK debug foi reinstalado com `adb install -r` e a Activity iniciou. O AVD apresentou avisos `Process system isn't responding` e `System UI isn't responding`; estes bloquearam a captura da interface e não foram tratados como falha do código NORA.
- Reinicialização do mesmo AVD com `-gpu software -feature -Vulkan -cores 2 -memory 2048`, sem apagar dados, concluiu boot em aproximadamente 128 segundos. `am start -W` confirmou `Status: ok` para `MainActivity`; dumpsys confirmou a superfície da Activity e dispositivo acordado. Porém, logcat registrou crashes de `com.android.systemui` (`Could not find Telephony Service` e `Replacing existing organizer currently unsupported`), `uiautomator dump` não produziu árvore atual e `screencap` não concluiu. Não reutilizar o XML antigo após falha do dump. Não afirmar revisão visual/reabertura completa do app.
- SDK Command-line Tools/`avdmanager` não estão instalados neste notebook. Para uma conferência extra estável, criar **outro** AVD em Tools → Device Manager → Create Device, usando imagem disponível x86_64 API31 ou superior, iniciar com Cold Boot e executar o APK. Preservar `Pixel_4`; não usar Wipe Data como tentativa de correção. Alternativa: dispositivo Android físico. Ver P-003.
- O primeiro processo do emulador foi encerrado por `adb emu kill` para a reinicialização. O segundo já havia encerrado ao conferir o fim da sessão: o processo retornou código 0 e a tentativa posterior de `emu kill` encontrou a porta desconectada. Nenhum dado do AVD foi apagado. O daemon Gradle ocioso desta validação foi encerrado para liberar memória.

## Referências oficiais

- [Aceleração do Android Emulator e WHPX/AEHD](https://developer.android.com/studio/run/emulator-acceleration).
- [Inicialização do emulador pela linha de comando](https://developer.android.com/studio/run/emulator-commandline).
