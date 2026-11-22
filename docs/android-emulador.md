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

## Ambiente Pichau em10/10/2026

O histórico acima descreve outro computador. Aqui o SDK está em `C:/Users/Pichau/AppData/Local/Android/Sdk`, o AVD é `Medium_Phone_API_37.0` e `-accel-check` informa que a aceleração não está instalada. A tentativa de boot somente leitura não disponibilizou dispositivo; nenhum driver, AVD ou configuração Windows foi alterado. Use o JDK21 em `C:/Program Files/Eclipse Adoptium/jdk-21.0.12.101-hotspot`: o JBR do Android Studio desta máquina é Java25.

As15 capturas atuais foram renderizadas em Compose/Layoutlib, incluindo Sistema claro/escuro, fonte130% e tablet. A [galeria](../../../Claude-HTML/Projeto-NORA/galeria/2026-10-10/README.md) identifica essa origem e as fixtures de preview. Os15renders/comparações,7unitários, APKs e lint passaram; não houve execução de teste instrumentado nem HTTP/PostgreSQL nesta rodada. P-003 mantém os testes em dispositivo e recriação de sessão. Para reproduzir as capturas sem emulador, veja [execucao.md](execucao.md#galeria-visual-de-10102026).

## Ambiente Pichau em 16/11/2026

A situação de 10/10 mudou: `emulator -accel-check` agora responde `WHPX(10.0.26300) is installed and usable` e o AVD `Medium_Phone_API_37.0` (Android 17, API 37) faz boot a frio sem janela em cerca de 10 a 35 segundos. Comando usado, com o JDK 21 do Adoptium em `JAVA_HOME` e `GRADLE_USER_HOME` em `.build/gradle-user` do repositório:

```powershell
$sdk = 'C:/Users/Pichau/AppData/Local/Android/Sdk'
& "$sdk/emulator/emulator.exe" -avd Medium_Phone_API_37.0 -no-window -no-audio -no-snapshot-save
& "$sdk/platform-tools/adb.exe" wait-for-device
& "$sdk/platform-tools/adb.exe" shell getprop sys.boot_completed   # 1 = pronto
```

Resultados de 2026-11-16 (P-045 e P-040, tarefa `docs/tarefas/2026-11-16-app-instrumentados-e2e.md`): `connectedDebugAndroidTest` com 30 testes, 28 aprovados e 2 pulados por assumption (`patientUsesRealDisposablePostgresApi` sem `noraApiUrl` e `VisualCaptureTest` sem `noraCaptureVisual=true`); com `tests/validar-clientes-descartavel.ps1 -Android`, 29 aprovados e só o `VisualCaptureTest` pulado.

Três particularidades desta imagem que custaram diagnóstico:

- **Rede local no Android 17.** O app tem `targetSdk` 37, e o `10.0.2.2` (o computador visto do emulador) é rede local: sem a permissão `ACCESS_LOCAL_NETWORK` concedida, o sistema descarta os pacotes do uid do app e a conexão expira em 10 s, enquanto o `adb shell` (outro uid) conecta normalmente. Por isso um teste com `nc` pelo shell não reproduz o problema; para testar como o app, use `adb shell run-as com.example.app_nora ...` com o app de depuração instalado. O app pede a permissão na tela; o teste a concede com `pm grant`.
- **Sondas com `nc`.** O Kestrel não responde quando o cliente fecha a escrita logo depois do pedido (`printf ... | nc`); mantenha a escrita aberta, por exemplo `(printf 'GET / HTTP/1.0\r\n\r\n'; sleep 2) | toybox nc -w 6 10.0.2.2 <porta>`.
- **Rede do emulador.** Nesta versão o tráfego do emulador sai pelo `netsimd` (libslirp), não pelo processo do emulador; é ele que aparece em `Get-NetTCPConnection` quando o emulador conecta a uma porta do computador.

O runner desinstala o app no fim de cada execução; para inspecionar à mão, reinstale com `adb install -r app/build/outputs/apk/debug/app-debug.apk`.

## Fontes sobre emulação

- [Aceleração do Android Emulator e WHPX/AEHD](https://developer.android.com/studio/run/emulator-acceleration).
- [Inicialização do emulador pela linha de comando](https://developer.android.com/studio/run/emulator-commandline).

## Conferência de 2026-11-17 no emulador (P-046, P-048 e parte da P-003)

AVD `Medium_Phone_API_37.0` (Android 17, aceleração WHPX), ligado com `emulator -avd Medium_Phone_API_37.0 -no-snapshot-save`.

**P-046 — diálogo de `ACCESS_LOCAL_NETWORK` com UiAutomator.** O diálogo do Android 17 **é acionável** pelo UiAutomator (`androidx.test.uiautomator:uiautomator` 2.3.0): os botões têm os ids `permission_deny_button` e `permission_allow_button` do `permissioncontroller`. `LocalNetworkPermissionTest` (androidTest) grava o endereço `http://10.0.2.2:1` nas preferências, abre a `MainActivity`, digita e-mail e senha e toca em Entrar: (1) negando, a tela mostra `LOCAL_NETWORK_DENIED_MESSAGE` e **não** mostra a falha de conexão, o que prova que nada saiu; (2) permitindo, o pedido segue e a porta 1 recusa a conexão (`CONNECTION_FAILED_MESSAGE`), e a permissão fica concedida. Os dois passaram. **Limite:** `pm revoke` de uma permissão concedida **mata o processo do app**, que é o mesmo do teste (visto em "Crash of app ... running instrumentation"), então a ordem é fixa (negar, depois permitir) e o teste se apoia em o AGP desinstalar o app ao fim de cada `connectedDebugAndroidTest`; se a permissão já vier concedida, o caso é pulado com a instrução de revogar com `adb shell pm revoke com.example.app_nora android.permission.ACCESS_LOCAL_NETWORK`. `pm clear` também não pode rodar dentro do teste, pelo mesmo motivo. Continua sem cobertura um endereço com nome comum que resolva para IP privado.

**P-048 — release com R8.** Ver [distribuicao.md](distribuicao.md#6-apk-de-release-assinado-com-chave-própria-p-048): o primeiro release caía ao abrir (`IllegalAccessError` em `kotlin.sequences`), corrigido com uma regra de keep; depois disso abre e navega no emulador sem exceções no logcat.

**P-003 — capturas no emulador com o APK de depuração** (em `Claude-HTML\Projeto-NORA\conferencia-2026-11-17\`, 1080x2400; a tela larga é 1600x2560 a 320 dpi). Telas: entrada, Ajuda, Configurações (início e fim) e Criar conta, em fonte 1.3, tela larga, tema escuro (`cmd uimode night yes`) e rotação. Tudo foi restaurado ao fim (`font_scale` 1.0, `wm size`/`wm density` reset, `night no`, rotação 0 com a automática ligada).

- **Defeito achado (fonte 1.3, entrada):** o botão **"Configurar"** do cartão "Precisa conectar à sua unidade?" quebra no meio da palavra ("Configur" / "ar"), porque o texto da esquerda ocupa quase toda a linha. Não corrigido (não mexer no build de depuração da apresentação); ver P-003.
- **Fonte 1.3:** entrada, Ajuda, Configurações e Criar conta sem texto cortado nem sobreposição, fora a quebra acima; em Criar conta há um vão vazio entre CPF e Senha (provavelmente a linha de apoio do CPF reservada).
- **Tela larga:** o conteúdo fica centralizado e limitado em largura (o limite de 640 dp da P-006 funciona); Configurações legível, sem corte.
- **Tema escuro:** contraste bom em entrada, Configurações e Criar conta; sem texto ilegível.
- **Rotação (retrato para paisagem e volta):** na tela Criar conta com "Maria" digitado, o texto **permaneceu** nas duas rotações, sem crash; em paisagem o formulário rola e é limitado em largura.
- **Início com dados de prévia:** o app não tem modo de prévia; a tela Início exige login numa API real, então não foi capturada (fica para a conferência com a API, em aparelho).
