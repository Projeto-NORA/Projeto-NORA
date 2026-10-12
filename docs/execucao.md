# Execução e validação

## WPF

Requer Windows com .NET SDK 10. Foi usado SDK 10.0.401. As duas soluções individuais continuam disponíveis; a solução integrada inclui os executáveis, `Nora.Desktop` e o harness sem pacotes de testes externos.

```powershell
dotnet build NORA.slnx --configuration Release --nologo -m:1
dotnet run --project OBS-NORA/OBS-NORA.csproj
dotnet run --project Painel-Municipal/Painel-Municipal.csproj
dotnet run --configuration Release --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj
.\tests\desktop-startup.ps1
```

No sandbox desta sessão, a tentativa inicial de build da solução com paralelismo padrão falhou sem diagnóstico de compilação. A execução sequencial com `-m:1 --disable-build-servers` passou com zero erros e avisos. Os builds individuais também passaram. `dotnet --info` encontrou restrição de acesso ao Service Control Manager; isso não impediu os builds.

Os checks verificam preferências, corrupção de arquivo, isolamento dos aplicativos, erros de armazenamento, tema dinâmico, contraste, evento atrasado do Windows, navegação, restauração, seleção acessível de aparência, tabela/contexto, rolagem e binding. Renderizam as telas nativas para PNG, com janelas fora da área visível. O parâmetro `--preview-dir <pasta>` após `--` escolhe a pasta das prévias. Preferências do usuário são preservadas: os testes usam diretórios temporários próprios.

O harness também salva `obs-registration-fixture-light.png` com o cadastro expandido e `obs-settings-light.png` com Configurações. A captura de cadastro usa transporte HTTP sintético, sem dados pessoais nem acesso ao banco de uso. Essas prévias renderizam o conteúdo WPF. O harness também abre/renderiza o menu da conta e tooltips e confere a preferência de cantos pelo DWM; não comprova a moldura física em múltiplos monitores/DPI.

Na retomada de 2026-10-07, passaram **23/23** checks, incluindo acionamento dos botões por `ButtonAutomationPeer` e diálogo nativo de fechamento/cancelamento. O teste separado `tests/desktop-startup.ps1` abre os executáveis reais Release, exige janela principal com título correto e responsiva, e encerra somente os processos que criou. Esse teste reproduziu crash nos dois clientes antes da correção de P-008 e passou nos dois depois. A aplicação OBS também foi mantida aberta e seu processo confirmou `Responding=True`.

O helper de controle Windows retornou timeout ao iniciar o executável e depois indicou solicitação ativa mesmo após reset. A abertura real foi validada pelo processo e pelo teste de regressão; não houve validação completa de cliques/atalhos físicos, DPI ou múltiplos monitores (P-007). Prévias nativas desta retomada estão em `.build/previews-retomada`.

Na conferência complementar, a regressão de navegação falhou antes da correção de P-010 e passou depois: **24/24 checks**, incluindo navegação entre telas no topo nos dois perfis, com e sem animação, além da medição dos botões no diálogo de fechamento. Build integrado Release e teste de abertura dos dois executáveis atualizados passaram; prévias em `.build/previews-conferencia`. Uma tentativa de build encontrou a DLL bloqueada pelo OBS aberto na etapa anterior; encerrada somente essa instância de teste, a nova execução concluiu com zero erros/avisos.

## API

Execução e contratos estão em `api.md`. A solução integrada agora compila também `Nora.Api` e `Nora.Api.Checks`. A revisão integrada confirmou Release com zero erros/avisos e **20/20** verificações HTTP reais, sem banco ou autenticação simulados.

Para repetir os fluxos persistidos sem acessar bases de uso, compile Release e execute os harnesses com PowerShell7:

```powershell
pwsh -NoProfile -File tests/validar-postgres-descartavel.ps1
# Com emulador já iniciado e boot concluído:
pwsh -NoProfile -File tests/validar-clientes-descartavel.ps1 -Android -JavaHome 'C:/Program Files/Android/Android Studio/jbr'
```

O primeiro cria cluster PostgreSQL 18 e valida HTTP, migrações, autorização e fluxo. O segundo cria outro cluster/schema, inicia API com contas sintéticas, testa WPF pelos controles nativos e depois Android conectado com argumento `noraApiUrl`. Os scripts preservam logs em `.build/` e encerram somente instâncias próprias. Não executar concorrentes: metadata de fixture é compartilhada; o harness recusa uma já existente. Sem `-Android`, o segundo valida somente WPF. O JDK21 precisa ser explícito quando `JAVA_HOME` pessoal aponta para outra versão.

Em2026-10-08, baseline .NET Release zero erros/avisos, HTTP 20/20 e PostgreSQL 15/15. E2E WPF 34/34 passou. Bootstrap da DLL iniciada da raiz reproduziu porta 5000 antes da correção de content root; depois carregou appsettings do diretório da DLL, escutou 5080 e respondeu liveness real. Rodada final Android passou10/10 instrumentados sem skips, incluindo integração PostgreSQL, e7/7 testes locais reexecutados. Lint zero erros/um aviso de versão disponível. Limpeza preservou37 arquivos locais por hashes e retirou somente do Git; build final Release passou. Publicação no documento da retomada de2026-10-08.

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

Na etapa inicial, build do app/testes, três testes locais e lint passaram; quatro casos instrumentados apenas compilaram e `connectedDebugAndroidTest` falhou com “No connected devices”. Essa evidência histórica não descreve o ambiente atual do notebook.

Na retomada de 2026-10-07, o AVD `Pixel_4` API31 iniciou com WHPX já instalado fora do sandbox. O comando integrado de build, testes locais, lint e `connectedDebugAndroidTest` concluiu com sucesso: **3/3 testes locais e 4/4 instrumentados**, zero falhas/erros/skips. Lint com zero erros e um aviso de versão de dependência disponível. Não foi preciso instalar driver nem alterar/reiniciar Windows. Relatório instrumentado em `APPNORA/app/build/outputs/androidTest-results/connected/debug/`.

Os testes instrumentados confirmam interação, rolagem e persistência em novas instâncias do armazenamento. A conferência extra de reabertura e captura de tela ficou limitada por crashes/ANRs do `System UI` do AVD, inclusive após reinicialização com dois núcleos e GPU software. Não foi registrado crash do processo NORA. Ainda faltam reabertura/render estáveis, tema Sistema, fonte ampliada e layout em telas maiores (P-003). O passo a passo para executar no Android Studio e criar um novo AVD está em `android-emulador.md`; o AVD existente não foi apagado.

## Referências oficiais de compatibilidade

- [AGP 9.4 e requisitos de Gradle/JDK/SDK](https://developer.android.com/build/releases/agp-9-4-0-release-notes).
- [Kotlin integrado ao AGP](https://developer.android.com/build/migrate-to-built-in-kotlin).
- [Dependências e compilador Compose](https://developer.android.com/develop/ui/compose/setup-compose-dependencies-and-compiler).
- [AndroidX Activity](https://developer.android.com/jetpack/androidx/releases/activity).

## Ajustes OBS/Pacientes — 2026-10-11

Baseline atual: solução Release zero erros/avisos, 33/33 checks WPF e abertura 2/2 dos executáveis. Depois dos ajustes, a rodada integrada passou 43/43 checks WPF: telefone, filtros, presença sintética, atualização automática sem apagar seleção/rascunho ou impedir edição, erro/recuperação/401, menu Configurações/Sair, tooltips, zebra, contatos longos, rolagem e preferências DWM de janela restaurada/maximizada/tela cheia.

PostgreSQL18 descartável passou HTTP20/20 e fluxos18/18, incluindo telefone válido/inválido, compatibilidade de cadastros anteriores, migração reaplicável, presença por sessão, timeout/múltiplos dispositivos/logout e ordem recente. Evidência `.build/postgres-validation-59e0c47d0a054bf9bc452e1201d33182`; cluster próprio encerrado. A primeira tentativa foi limitada pelo socket loopback do sandbox e repetida com execução local autorizada.

O ciclo de diagnóstico usou testes vermelhos antes das correções: seleção na Fila, aviso após reconexão, input desabilitado durante polling, padding duplicado dos inputs e contraste do botão. A rodada final em área cliente real acrescentou a ausência de rolagem externa no tamanho mínimo e reproduziu perda do offset interno no polling sem mudanças. A altura da lista passou a reservar o espaço de título/filtros/cadastro; coleções iguais não são limpas/reinseridas e a lista filtrada só é notificada quando os registros mudam. Após essas correções, solução Release zero erros/avisos, 43/43 desktop sem E2E e abertura 2/2 passaram. A rodada com PostgreSQL acrescenta o check E2E (44 no total).

A última inspeção revelou rótulos do cadastro herdando preto do Expander; a regressão nova falhou em 43/44 sem E2E antes da correção. Expander passou a herdar TextBrush e o cadastro foi renderizado/conferido nos três temas. Rodada final WPF com PostgreSQL passou **45/45**, solução Release zero erros/avisos, evidência `.build/clients-validation-0198b1ae92f24cd1b30a367cd9ff0072`; cluster próprio encerrado. Prévias completas em `.build/previews-2026-10-11-final`, inclusive cadastro, zebra, menu, tooltip e configurações.

Android app/APK de testes, 7/7 locais e lint passaram (zero erros/um aviso já conhecido de versão disponível). A execução integrada Pixel_4 API31 passou **14/14 instrumentados, zero falhas/erros/skips**, incluindo HTTP real/PostgreSQL, heartbeat após um intervalo real em segundo plano, toque na notificação preservando Activity/ViewModel, remoção da tarefa e guard de geração/401. Evidência `.build/clients-validation-212d7f97123440a9aa3e84aebe7b7cec` e XML em `APPNORA/app/build/outputs/androidTest-results/connected/debug/`. Essa execução antecedeu somente a correção WPF do Expander; Android não foi alterado depois. P-003/P-007 preservam os limites de aparelhos, recriação de Activity, DPI/monitores e teclado físico.
