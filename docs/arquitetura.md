# Arquitetura da primeira etapa

## Aplicativos e responsabilidades

`APPNORA` é o cliente Android Kotlin/Jetpack Compose. `MainActivity` inicializa `SettingsStore` e apresenta `NoraApp`; `NoraContent` organiza Início e Configurações, com retorno nativo e estados de rolagem independentes. `preferences/UserSettings.kt` define os valores locais, `SettingsStore.kt` encapsula persistência e estado observável, e `ui/NoraTheme.kt` aplica as paletas e a aparência das barras do sistema.

`OBS-NORA` e `Painel-Municipal` são executáveis WPF .NET 10. Cada `App` abre sua `MainWindow`, derivada do shell compartilhado com o perfil correspondente. As antigas janelas XAML vazias foram substituídas pela composição compartilhada; as soluções individuais continuam válidas. `NORA.slnx` reúne os dois executáveis, a biblioteca e o harness de verificações.

`Nora.Desktop` concentra as responsabilidades comuns:

- `Models`: preferências, perfis e descritores de navegação/cards/linhas.
- `ViewModels`: estado e comandos do shell; navegação, seleção e configuração.
- `Views`: shell, Home e Configurações; integração visual de teclado, animação e confirmação de fechamento.
- `Services`: persistência JSON, temas do Windows e tela cheia.
- `Generics`: notificação de propriedades e comando reutilizável.
- `Themes`: paletas Clara/Escura e templates compartilhados de controles.

Não há banco nem DAOs nesta etapa, pois o escopo confirmado é a interface com preferências locais. Não foram criados schemas, domínio clínico, contas ou endpoints da API. O acesso futuro a dados de saúde deve ser definido em uma tarefa própria; nenhuma interface acessa banco diretamente.

## Identidade e temas

Os desktops consomem tokens por `DynamicResource`. `ThemeService` troca o dicionário da paleta, lê `AppsUseLightTheme` no Windows e observa mudanças de preferências do sistema. O callback revalida a opção escolhida ao executar no Dispatcher, preservando uma escolha manual feita enquanto o evento estava enfileirado. A assinatura do evento é encerrada com a janela.

No Android, `NoraTheme` combina a opção persistida com `isSystemInDarkTheme()`, aplicando Material 3 e contraste coerente. A seleção de aparência possui semântica de radio button. A mudança também atualiza a cor dos ícones das barras do dispositivo.

## Limites da fundação

Os aplicativos possuem estados vazios explícitos de agenda, fila, oportunidades e indicadores. Áreas que exigem integração ficam indisponíveis; os indicadores não apresentam totais simulados. A tabela do OBS suporta seleção e barra contextual, mas ações clínicas estão desabilitadas. O Municipal reserva uma área para séries históricas sem fabricar gráficos ou métricas.

Preferências de notificação no Android são somente escolhas locais, sem envio de avisos ou solicitação de permissão de notificações. A saída da conta permanece desabilitada porque ainda não existe conta conectada. Estes limites fazem parte do escopo confirmado, não de um backend simulado.

## Arquivos locais e build

`Directory.Build.props` centraliza os outputs .NET em `.build/<projeto>/`, excluindo artefatos antigos da compilação. `.gitignore` cobre novos outputs, estado de IDE e segredos. Os arquivos `obj/` e `.vs/` previamente rastreados foram preservados; a retirada do índice está registrada em P-002.
