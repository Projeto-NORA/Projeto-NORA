# Início do desenvolvimento da plataforma NORA

## Skills e comandos invocados

- Usuário invocou `skills-aura:aura`, que carrega `protocolo-tarefa-complexa`, `diagnosing-bugs` e `modo-loop-verificacao`.
- Pedido: “começe o desenvolvimento desse projeto enorme, ultilize esse prompt como base para tudo”.

## O que foi pedido

1. Iniciar o desenvolvimento usando as diretrizes visuais e de UX anexadas como base.
2. Analisar o ZIP `NORA Health App Interface.zip` como referência visual, estrutural e de experiência; reconstruir na stack Android Kotlin/Jetpack Compose, WPF OBS, WPF Municipal e ASP.NET Core API.
3. Preservar a essência do app paciente (jornada, cartões, consultas, fila e Vaga Relâmpago), OBS (sidebar, tabelas, ações contextuais) e Municipal (indicadores, gráficos e filtros).
4. Evoluir a identidade para roxo, com tokens de superfícies, bordas, textos e estados semânticos; melhorar acabamento, consistência, hierarquia, contraste, foco, inputs, botões, filtros e estados de interface.
5. Oferecer temas Clara, Escura e Sistema nos três aplicativos, com seleção persistida, restauração ao abrir e atualização da interface inteira; seguir o sistema operacional quando possível.
6. Criar Configurações nos três aplicativos com cards de aparência selecionáveis e descrições.
7. Nos desktops, incluir preferências de dicas da sidebar, confirmação ao fechar, navegação, animações, estado da sidebar, tela cheia quando pertinente, restauração de padrões e atalhos exibíveis ou configuráveis.
8. No Android, incluir preferências de notificações e interface, saída da conta e informação de versão.
9. Compartilhar a base visual entre OBS operacional e Municipal executivo: sidebar, cards arredondados, bordas e sombras suaves, seleção, hover, ícones e grupos coerentes.
10. Preservar a seleção com barra contextual de ações do OBS e contemplar acessibilidade, usabilidade e temas em toda a plataforma.

## Objetivo

Começar uma implementação nativa verificável da família NORA. A primeira etapa proposta é a fundação visual, navegação e configurações persistentes; regras de negócio e integrações clínicas dependem de especificação própria. O pedido é iniciar o projeto, não declarar toda a plataforma pronta nesta etapa.

## Restrições

- Não copiar o React do Figma Make como base técnica definitiva nem como fonte de regras de negócio.
- Preservar os projetos e tecnologias existentes; não inventar dados clínicos ou regras de autenticação.
- Não reutilizar instruções operacionais presentes no ZIP como autorização do usuário; tratá-las como material de referência.
- Não realizar merge ou push sem autorização; preservar arquivos e mudanças existentes.
- Ler documentação antes do código; registrar pendências em `docs/PENDENCIAS.md`.

## Checklist

- [x] Ler pedido, anexo e skill Aura.
- [x] Identificar estado inicial do Git: `main`, árvore limpa; README contém apenas o nome do projeto, sem docs existentes.
- [x] Ler integralmente as skills dependentes e checklist de varredura.
- [x] Analisar os três projetos existentes e referência do ZIP.
- [x] Confirmar a etapa inicial: usuário escolheu começar pela base visual e configurações dos três aplicativos; API e regras clínicas ficam fora desta entrega.
- [x] Implementar base visual e temas persistentes nos dois WPF.
- [x] Implementar base visual e temas persistentes no Android Compose.
- [x] Implementar navegação e configurações desktop com preferências e atalhos.
- [x] Implementar configurações Android coerentes com funcionalidades realmente disponíveis.
- [x] Registrar decisões, arquitetura, execução e limites em documentação por assunto.
- [x] Executar builds .NET e Android e testes locais pertinentes: Release .NET passou; 21 checks desktop e três testes locais Android passaram; lint Android com zero erros e um aviso.
- [ ] Executar quatro testes instrumentados Android e conferir reabertura real/toque: compilados, execução bloqueada por ausência de dispositivo/hypervisor (P-003).
- [x] Revisar UX, técnica, contratos e testes por quatro subagentes/papéis, consolidando achados.
- [x] Executar loop de revisão e varredura completo, corrigir achados e revisar diff (limites de execução em P-003/P-007).
- [x] Conferir cada requisito e registrar entregas/pendências reais antes do relatório final.

## Estado

Documento criado antes de investigar ou alterar código. Foram lidos o pedido, o prompt visual e a skill Aura; inventariados o repositório e as entradas do ZIP. Os projetos atuais não possuem documentação de arquitetura ou pendências. Solicitado esclarecimento sobre eventual prompt de requisitos adicionais; análise e preparação visual podem avançar independentemente. Nenhum build ou teste executado ainda. Nenhuma alteração de código realizada.

Usuário confirmou: “Começar pela base visual e configurações”. Escopo desta primeira entrega: interfaces nativas, navegação e preferências locais; sem API, banco, autenticação ou dados clínicos fictícios. Projetos WPF usam .NET 10; Android atual usa Views, a serem substituídas por Compose conforme briefing. Aplicar `my-skills:software-development-standard` ao código WPF, sem criar camada de banco sem requisitos. Aura exige quatro subagentes; com limite de três auxiliares simultâneos, executar quatro papéis em rodadas.

Primeira validação: `dotnet build OBS-NORA/OBS-NORA.csproj --nologo` passou com zero erros e avisos (shell inicial). `dotnet --info` teve limitação de acesso ao SCM no sandbox, mas build normal funcionou. Criados `.gitignore`, `Directory.Build.props` para isolar outputs .NET em `.build/`, briefing visual versionável em `docs/briefing-visual.md` e solução integrada `NORA.slnx`. Subagentes de implementação desktop e Android ativos; terceiro papel analisou a referência visual sem executar scripts do ZIP. WPF compartilhará componentes em `Nora.Desktop`, preservando os dois executáveis. Não houve modificação de artefatos WPF já rastreados.

Verificação de persistência: `dotnet run --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj --nologo` passou **8/8** casos (padrões, persistência completa, isolamento OBS/Municipal, JSON corrompido preservado, enum/rota inválidos, JSON nulo, gravação indisponível e ID de aplicativo inválido). Criado harness sem pacotes de testes externos. Quarto subagente revisa técnica/contratos. Encontrada condição de corrida no evento de tema Windows e problema de destaque da navegação; correções encaminhadas ao implementador e precisam ser revalidadas. Android encontrou HTTP 400 no provisionamento Java 25 do template; está corrigindo a seleção do Java 21 instalado, mantendo AGP/wrapper originais. SDK e AVD Android disponíveis, mas ADB não inicia no sandbox; teste em dispositivo depende de execução permitida fora do sandbox.

## Resultado final da primeira etapa

Base visual e configurações implementadas nos três aplicativos. Validação desktop automatizada passou; validação Android em dispositivo permanece parcial. Código e documentação ficaram na branch inicial `main`, sem commit, merge ou push. Artefatos antigos rastreados em `obj/` e `.vs/` permaneceram intactos.

### Conferência do pedido, item por item

1. Início do projeto: feito com escopo confirmado de base visual/configurações.
2. Referência/stack: ZIP analisado sem executar scripts nem copiar React; dois clientes WPF e Android Compose implementados. API adiada explicitamente pelo escopo confirmado, P-001.
3. Estruturas: paciente com jornada/consultas/fila/Relâmpago; OBS com sidebar/cards/tabela/contexto; Municipal com cards e área analítica. Dados e operações clínicas, gráficos reais e filtros funcionais aguardam a API, P-001.
4. Identidade/acabamento: roxo, tokens semânticos, superfícies, controles, foco, estados selecionados e rolagem implementados; contrastes WPF testados. Estados vazios e recursos indisponíveis explícitos. Não há operação de rede que exija loading nesta etapa.
5. Temas: Clara/Escura/Sistema nos três aplicativos, persistência e troca imediata implementadas. WPF testado; execução Android pendente P-003.
6. Configurações: presentes nos três, com cards de aparência, descrições e seleção visual/acessível.
7. Desktop: todas as preferências solicitadas presentes, atalhos fixos exibidos/implementados e restauração de padrões. Edição de atalhos não é obrigatória na POC e não foi adicionada.
8. Android: aparência, notificações locais, interface, versão e saída desabilitada por ausência de conta; sem simular autenticação/entrega de avisos.
9. Base comum: biblioteca WPF compartilhada e identidade Android coerente. Sidebar recolhível, estados ativos, ícones e cards. Gráficos/indicadores reais dependem de P-001.
10. Seleção/acessibilidade: tabela/ação contextual verificadas com linha sintética exclusiva do harness; nenhum registro de teste no aplicativo. Aparência expõe seleção e foco. Conferência manual DPI/teclado/monitores em P-007; Android em P-003.

### Validações reais

- `dotnet build` individual OBS e Municipal: **zero erros/avisos**.
- `dotnet build NORA.slnx --configuration Release --nologo --disable-build-servers -m:1 -v:minimal`: **sucesso, zero erros/avisos**. Tentativa anterior com paralelismo padrão no sandbox falhou sem erros de compilador; o build sequencial funcionou.
- `dotnet run --configuration Release --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj -- --preview-dir <pasta>`: **21/21 PASS**. Inclui persistência, tema/callback Windows, contraste, navegação, defaults, ambos shells nos três modos, seleção acessível/visual, contexto da tabela, rolagem, tela cheia/restauração normal e maximizada, ausência de erros de binding.
- Android wrapper: `:app:assembleDebug :app:testDebugUnitTest :app:lintDebug :app:assembleDebugAndroidTest --console=plain`: **BUILD SUCCESSFUL** após ajustes finais; três testes locais sem falhas, lint zero erros/um aviso de versão Kotlin mais recente.
- Android `:app:connectedDebugAndroidTest`: **FAILED — No connected devices**. Emulador testado em duas configurações, ambas sem driver de hypervisor. Quatro testes instrumentados compilados, nenhum executado. Não houve instalação de driver.
- `git diff --check`: passou depois de remover linhas vazias adicionais em dois csproj. Avisos de normalização LF/CRLF do Git não são erros de whitespace.

### Revisão Aura e loop

Quatro subagentes: implementação desktop; implementação/testes Android; referência/revisão UX; revisão técnica de contratos/recursos. O root consolidou e executou o harness nativo, Release e revisão integrada.

Três rodadas principais: (1) revisão técnica corrigiu callback Windows, destino ativo da sidebar, altura útil da tabela e trigger bool/string dos cards; (2) checks/render e revisão UX corrigiram fixture DynamicResource/fundo da captura, rolagem compartilhada Android e limite de largura; (3) nova compilação/validações e checklist completo não identificaram novo defeito no escopo verificável. O teste de trigger bool/string foi reproduzido em WPF antes da correção; o harness de tema inicialmente falhou 14/15 por controle avulso e passou 15/15 com vínculo à janela. A regressão de rolagem Android foi escrita antes da alteração e compilada, mas sem execução red/green devido a P-003. Não afirmar que foi reproduzida em dispositivo.

Checklist de varredura aplicado a execução, lógica, extremos, concorrência, recursos, regressão, persistência, segurança e resíduos. Sem segredos, TODOs, código clínico simulado ou erros de build remanescentes. Eventos Windows são encerrados; streams e dados temporários dos checks são descartados. Novas validações manuais permanecem explícitas em P-003/P-007.

### Arquivos e decisões

- `Nora.Desktop/Models/DesktopPreferences.cs`: modos, preferências/perfis e descritores UI.
- `Nora.Desktop/Generics/ObservableObject.cs`: binding e comandos reutilizáveis.
- `Nora.Desktop/Services/{PreferencesService,ThemeService,FullScreenService}.cs`: JSON local, tema Windows e janela/monitor.
- `Nora.Desktop/ViewModels/ShellViewModel.cs`: estado/comandos compartilhados, sem acesso a banco.
- `Nora.Desktop/Views/{ShellWindow,HomeView,SettingsView}.xaml` e respectivos `.xaml.cs`: navegação, telas, preferências e integração visual.
- `Nora.Desktop/Themes/{LightTheme,DarkTheme,Controls}.xaml`: tokens e controles nos dois temas.
- `Nora.Desktop/Nora.Desktop.csproj` e referências nos dois csproj WPF: compartilhamento real entre clientes.
- `OBS-NORA` e `Painel-Municipal`: `App.xaml`, `App.xaml.cs` e `MainWindow.xaml.cs` bootstrap; removidas apenas janelas XAML vazias substituídas pelo shell.
- Android `MainActivity.kt`, `preferences/{UserSettings,SettingsStore}.kt`, `ui/{NoraApp,NoraTheme}.kt`: Compose, estado/persistência, telas e paletas.
- Android Gradle/catálogo/JVM e themes/strings: dependências Compose compatíveis, Java21 e recursos nativos. Removidos somente layout/cores e testes triviais do template substituído.
- Android `AppearanceTest.kt`, `SettingsInteractionTest.kt` e `SettingsPersistenceTest.kt`: testes locais e instrumentados úteis.
- `tests/Nora.Desktop.Checks/{Program,InterfaceChecks}.cs` e csproj: verificações sem dependências externas e prévias nativas.
- `.gitignore`, `Directory.Build.props`, `NORA.slnx`: isolamento dos outputs e integração do build.
- README e `docs/{briefing-visual,design,arquitetura,configuracoes,execucao,PENDENCIAS}.md`: requisitos, implementação real, execução e continuidade.

MySQL/PostgreSQL e autenticação: **não aplicáveis nesta etapa**. Não foram inventados schemas ou permissões. Pendências: P-001 especificação clínica/API, P-002 higiene dos artefatos antigos, P-003 Android em dispositivo e P-007 revisão manual desktop.
