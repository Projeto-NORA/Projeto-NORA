# Arquitetura da primeira etapa

## Aplicativos e responsabilidades

`APPNORA` é o cliente Android Kotlin/Jetpack Compose. `MainActivity` inicializa `SettingsStore`, mantém `PatientController` em `AndroidViewModel` apenas em memória e apresenta `NoraApp`; `NoraContent` organiza Início e Configurações, com retorno nativo e estados de rolagem independentes. `preferences/UserSettings.kt` define os valores locais, `SettingsStore.kt` encapsula persistência e estado observável, e `ui/NoraTheme.kt` aplica as paletas e a aparência das barras do sistema.

`OBS-NORA` e `Painel-Municipal` são executáveis WPF .NET 10. Cada `App` instancia diretamente `Nora.Desktop.Views.ShellWindow` com seu perfil e a atribui a `Application.MainWindow`. A herança anterior em outra assembly impedia o carregamento do XAML na abertura dos executáveis; foi removida após reprodução nos dois clientes (P-008). As soluções individuais continuam válidas. `NORA.slnx` reúne os dois executáveis, a biblioteca, a API e os harnesses de verificações.

`Nora.Desktop` concentra as responsabilidades comuns:

- `Models`: preferências, perfis e descritores de navegação/cards/linhas.
- `ViewModels`: estado e comandos do shell; navegação, seleção e configuração.
- `Views`: shell, Home e Configurações; integração visual de teclado, animação e confirmação de fechamento.
- `Services`: persistência JSON, temas do Windows e tela cheia.
- `Generics`: notificação de propriedades e comando reutilizável.
- `Themes`: paletas Clara/Escura e templates compartilhados de controles.

A API usa PostgreSQL 18 via Npgsql10.0.3. Models define contratos; Services valida contas/autenticação/fila; DAOs usa SQL parametrizado e transações; Database contém helper e migrações aditivas incorporadas. Sessões revogáveis são persistidas pelo hash do token. WorkflowDao serializa oferta/resposta/expiração com advisory lock PostgreSQL. OfferExpirationWorker verifica prazo a cada15s; operações também verificam antes de responder. Nenhuma interface acessa banco diretamente. Contratos em `api.md`.

`Nora.Desktop/Services/NoraApiClient.cs` encapsula HTTP e guarda token somente em memória. `WorkspaceViewModel` publica dados após recarga completa, controla busy/erros e limpa dados ao sair ou receber401; WorkspaceView contém login, cadastro, pacientes/fila/vagas e Municipal agregado. O endpoint pode vir de NORA_API_URL ou da tela; senhas não são preferências.

Android usa `api/NoraApi.kt` como boundary HTTP, PatientController para estado/busy/erros e ApiAddress para validar HTTPS. A interface recebe estado/callbacks sem SQL. Endpoint validado é preferência local; identidade/token só memória. Release permite HTTPS, debug permite HTTP apenas no host emulador10.0.2.2. Ambos desativam redirecionamentos HTTP automáticos.

## Identidade e temas

Os desktops consomem tokens por `DynamicResource`. `ThemeService` troca o dicionário da paleta, lê `AppsUseLightTheme` no Windows e observa mudanças de preferências do sistema. O callback revalida a opção escolhida ao executar no Dispatcher, preservando uma escolha manual feita enquanto o evento estava enfileirado. A assinatura do evento é encerrada com a janela.

No Android, `NoraTheme` combina a opção persistida com `isSystemInDarkTheme()`, aplicando Material 3 e contraste coerente. A seleção de aparência possui semântica de radio button. A mudança também atualiza a cor dos ícones das barras do dispositivo.

## Limites da fundação

OBS cadastra pacientes e opera fila/ofertas por entrada. Android mostra apenas acompanhamento da própria conta e responde à oferta. Municipal consulta somente totais agregados. Estados vazios e indisponibilidade são explícitos. Consultas, notificações externas e séries/filtros analíticos adicionais continuam fora do escopo aprovado (P-013); não há gráficos fictícios.

Preferências de notificação Android são locais, sem envio externo. Logout funciona para contas conectadas; reabrir exige login. A base definitiva e suas contas aguardam configuração privada PostgreSQL (P-012). Testes usam schemas aleatórios em instância descartável, sem modificar bases de uso.

## Arquivos locais e build

`Directory.Build.props` centraliza os outputs .NET em `.build/<projeto>/`, excluindo artefatos antigos da compilação. `.gitignore` cobre novos outputs, sessões locais do compilador Kotlin em `.kotlin/`, estado de IDE e segredos. Os 37 arquivos `obj/`, `.vs/` e `*.csproj.user` antes rastreados foram retirados somente do índice Git em 2026-10-08. As cópias locais foram preservadas e seus hashes conferidos; `.gitignore` evita reintrodução (P-002 resolvida).

A migração `003_patient_contact_presence.sql` acrescenta telefone opcional em accounts e presença recente por sessão. A API calcula `isOnline` com o relógio do servidor e o estado de autenticação; o OBS nunca infere presença pela existência do cadastro. `WorkspaceViewModel` controla atualização periódica de 20 segundos com exclusão de operação concorrente e encerra o timer ao sair/fechar. Views/Themes concentram a listagem e controles arredondados, mantendo HTTP em NoraApiClient e SQL nos DAOs.

No Android, `NoraApp` observa lifecycle STARTED e sessão para iniciar `PatientPresenceService` quando a tela está visível; deixar o estado visível não encerra o serviço. O serviço foreground não exportado mantém heartbeat de 30 segundos e notificação fixa, retorna START_NOT_STICKY e encerra no logout ou callback de remoção da tarefa. `PatientController` serializa presença com mutex e geração, preserva somente a sessão corrente e ignora respostas antigas que poderiam repor dados após401. O controller retido pela Activity conserva identidade somente em memória em rotação; token não é enviado em Intents nem salvo. O offline final é uma tentativa com timeout, com prazo de presença da API como garantia em perda de conexão/processo. Esta política de segundo plano foi confirmada pelo usuário em 2026-10-11.
