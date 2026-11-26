# Configurações e preferências

## Aparência

Os três aplicativos oferecem Clara, Escura e Sistema. Sistema é o padrão inicial; acompanha o Windows nos desktops e o tema do dispositivo no Android. A escolha é persistida e reaplicada ao abrir, com mudança da interface sem reinicialização. Os cards indicam a opção escolhida visualmente e pela acessibilidade.

## Desktops

Nos formulários do OBS e do Painel Municipal, Enter avança pelos campos disponíveis e, ao chegar ao último, aciona a ação principal do formulário. Controles desabilitados e campos somente de leitura são ignorados; em caixas de seleção abertas, Enter confirma a escolha primeiro. Em textos de várias linhas, Enter continua inserindo uma quebra de linha. Filtros que atualizam automaticamente não precisam de botão de envio.

Os perfis de inicialização do Visual Studio dos dois desktops definem `NORA_API_URL` como `https://nora-api-kw8x.onrender.com`. Assim, ao iniciar por esses perfis, os aplicativos usam o serviço hospedado e os dados desse ambiente; para desenvolvimento isolado, ajuste a variável do perfil para o endereço da API local.

OBS e Municipal salvam arquivos independentes em `%LOCALAPPDATA%/NORA/obs/preferences.json` e `%LOCALAPPDATA%/NORA/municipal/preferences.json`. O serviço lê/normaliza tema e rota, recupera padrões quando o JSON é inválido e informa o erro na barra inferior. A leitura preserva o arquivo corrompido; uma alteração posterior de preferência substitui o conteúdo pelo estado atual. A gravação usa arquivo temporário e substituição do destino. Se falhar, a interface informa que a mudança vale apenas para a sessão.

As preferências controlam dicas na sidebar, confirmação de fechamento, restauração da navegação, animação ao trocar tela, lembrança da largura da sidebar e tela cheia. Desativar a restauração de navegação faz a próxima abertura iniciar na Home. No OBS são restauradas Home, Configurações, Pacientes, Fila, Vaga Relâmpago, Configurações da UBS, Família e Comunicados; Agenda, Indicadores e Alertas reabrem na Home. Desativar a lembrança da sidebar faz a próxima abertura usar a largura padrão. Restaurar padrões mantém Configurações aberta na sessão atual e salva Home para a próxima abertura.

Navegar entre Home e Configurações abre a tela pelo topo, sem herdar a rolagem da anterior. O comportamento vale nos dois desktops com animações ativadas ou desativadas, verificado por invocação dos botões na janela nativa. O diálogo de fechamento também foi medido: seus botões cabem na área cliente no ambiente testado; DPI ampliado continua em P-007.

Tela cheia cobre os limites do monitor e remove a borda da janela. Ao sair, restaura dimensão, estilo e estado anteriores. A validação de múltiplos monitores e DPI diferentes ainda depende de conferência manual.

| Atalho | Ação |
| --- | --- |
| Ctrl + 1 | Visão geral |
| Ctrl + , | Configurações |
| Ctrl + F | Pesquisar texto e itens carregados na tela |
| Ctrl + B | Alternar entre as larguras compacta e normal da barra lateral |
| Ctrl + Shift + T | Ciclar Clara, Escura e Sistema |
| F11 | Entrar/sair de tela cheia |
| Esc | Fechar pesquisa ou sair da tela cheia |
| Alt + F4 | Fechar, respeitando confirmação |

Os atalhos editáveis ficam ao lado de Aparência e Preferências da interface. Clique na combinação e pressione a nova tecla. O NORA rejeita combinações que conflitam com outras ações. Em janelas estreitas, a seção passa para baixo das preferências. Restaurar padrões pede confirmação e restaura também as combinações alteradas.

Ctrl+F abre a busca no topo da janela, com destaque do resultado atual. Enter avança, Shift+Enter volta e Esc fecha. A pesquisa percorre os dados já carregados na tela atual, respeitando os filtros e as permissões da sessão.

No canto inferior esquerdo, o menu da conta abre **Dados do perfil**, **Configurações** e **Sair da conta**. Dados do perfil mostra e-mail, função e município ou unidades. O nome completo e a senha podem ser alterados ali.

Em **Exclusão de itens desativados**, o gestor municipal administra contas OBS elegíveis. O administrador do OBS administra consultórios da unidade ativa. **Nunca excluir sozinho** começa marcado. Com confirmação, é possível definir um prazo automático para o escopo, agendar um cadastro elegível ou cancelar seu agendamento. A API protege contas com histórico de uso e consultórios referenciados.

## Android

`SettingsStore` usa SharedPreferences privadas chamadas `nora_preferences`. Tema e cinco escolhas são persistidos: avisos de consultas, avisos de Vaga Relâmpago, comunicados da rede, mostrar orientações e espaçamento confortável. Valores com tipos inválidos são substituídos pelos padrões seguros na leitura. Tema é Sistema e as cinco opções são ativadas inicialmente.

Na entrada, Configurações pode ser aberta pelo diálogo do ícone de ajuda; após login, também fica no topo do início e no Perfil. A tela agrupa aparência, notificações, interface e informações do app. As escolhas de notificações ficam salvas no aparelho. A tela explica que os avisos de Vaga Relâmpago e de comunicados chegam enquanto a conexão ativa do NORA estiver rodando e dependem da permissão de notificações do Android, e que a opção de consultas ainda não tem efeito porque a rede não envia lembrete de consulta. A opção de sair é exibida quando há uma conta conectada.

Mostrar orientações controla o card de personalização da Home; espaçamento confortável alterna intervalos entre cards. Os avisos são notificações locais do ciclo do serviço de conexão; push por servidor está em P-042. Sobre mostra versão/estado da conta; sair é habilitado para sessão conectada. Restaurar padrões pede confirmação e não encerra a conta.

As distribuições do Android e dos desktops chegam com o endereço HTTPS do serviço embutido. No Android de desenvolvimento sem URL embutida, o endereço é editável e salvo em `nora_connection` após login válido. Esse build permite HTTP para o emulador em `10.0.2.2`. O app do paciente mantém a sessão em memória e reabre no login por CPF ou e-mail. Nos desktops, a equipe entra com e-mail e senha, com sessão de 24 horas. Marcar **Manter conectado por 30 dias** protege a sessão pelo DPAPI do Windows e permite restaurá-la ao reabrir até o vencimento.

Home e Configurações têm rolagens independentes. Abrir Configurações volta ao topo, e o retorno preserva a rolagem da Home; a interação foi confirmada pelos testes instrumentados no Pixel_4. O conteúdo fica centralizado e limitado a 640 dp em telas amplas; a conferência visual de tela larga, fonte ampliada e reabertura estável continua em P-003.

Quando a API confirma uma resposta de oferta e a atualização posterior falha, o Android mantém a oferta como aceita/recusada e informa que a resposta foi confirmada, orientando Atualizar. Não oferece os botões de resposta novamente com dados pendentes antigos. Se a atualização retorna 401, limpa os dados da conta e pede nova entrada. O OBS também distingue mutação confirmada de falha de recarga; Atualizar concluído informa sucesso em vez de continuar exibindo conexão em andamento.

## Servidor (`Nora.Api`)

A API não tem tela de preferências: tudo vem da configuração do processo (`appsettings.json` ao lado da DLL, user-secrets ou variáveis de ambiente, com `__` no lugar de `:`, como `Nora__Maintenance__Enabled`). Segredos (conexão com senha, chaves HMAC, senha do SMTP, credencial da conta de serviço do Firebase) ficam fora do Git. Os contratos e os efeitos de cada chave estão em [API](api.md).

**Contas iniciais (sem CPF).** `Nora.Api --provision` (só em terminal interativo) pergunta escopo (`obs` ou `municipal`), nome, e-mail e senha, **sem CPF**: a equipe (Secretaria e OBS) não tem CPF desde a migração central 022 (P-053) e entra só por e-mail e senha. Contas de equipe que já existiam perderam o CPF nessa migração. O paciente continua com CPF obrigatório.

| Chave | Padrão | Uso |
| --- | --- | --- |
| `ConnectionStrings:Nora` | nenhum | conexão do banco central (no ambiente, `ConnectionStrings__Nora`) |
| `Nora:Schema` | `nora` | schema do banco central (`^[a-z][a-z0-9_]{0,62}$`) |
| `Nora:UnitDatabases:<id sem hífens>` | nenhum | conexão explícita do banco de uma UBS; tem precedência sobre a derivada |
| `Nora:UnitDatabases:AutoProvision` | `true` | deriva a conexão da UBS da central e cria o banco ao cadastrar a UBS |
| `Nora:UnitDatabases:NamePrefix` | `nora_ubs_` | prefixo do nome dos bancos derivados; inválido impede a inicialização |
| `Nora:UnitDatabases:MaxPoolSize` | `3` | pool de conexões de cada UBS, de 1 a 100 |
| `Nora:ForwardedHeaders:KnownProxies` e `KnownNetworks` | vazio | proxies confiáveis para `X-Forwarded-For`; sem eles o cabeçalho é ignorado |
| `Nora:TimeZone` | `America/Sao_Paulo` | fuso da grade, da capacidade e do dia dos indicadores; fuso desconhecido pelo PostgreSQL impede a inicialização (com o banco inacessível na subida, a conferência é adiada com um aviso no log) |
| `Nora:Smtp:Host`, `Port`, `Username`, `Password`, `FromAddress`, `FromName` | porta `587`, nome do remetente `NORA Saúde` | envio dos códigos por e-mail; sem host, usuário, senha e remetente as rotas que enviam código respondem 503 |
| `Nora:PasswordReset:HashKey` | nenhum | chave HMAC dos códigos de recuperação de senha, com ao menos 32 bytes |
| `Nora:Registration:HashKey` | nenhum | chave HMAC do cadastro do paciente, com ao menos 32 bytes |
| `Nora:EmailChange:HashKey` | `Nora:Registration:HashKey` e depois `Nora:PasswordReset:HashKey` | chave HMAC do código de troca do e-mail da conta; vale a primeira das três com ao menos 32 bytes, e nenhuma faz as rotas que geram código responderem 503 |
| `Nora:Alerts:HighQueue`, `LongWaitDays`, `MissedRate`, `WindowDays`, `Occupancy`, `OccupancyDays` | 20, 30, 0,20, 30, 0,95 e 7 | limites dos alertas; `WindowDays` vai de 1 a 366 dias e continua assim, mesmo com os indicadores aceitando 20 anos; valor fora da faixa não derruba a subida, mas faz as rotas de alertas do OBS e da Secretaria falharem até ser corrigido |
| `Nora:ObsDevices:Enabled` | `false` | liga o recurso dos computadores do OBS. Desde 24/11/2026 o padrão é desligado: o login do OBS vale sozinho (e-mail, senha e UBS com permissão), nenhum computador é registrado nem exigido, o cabeçalho `X-Nora-Device` de um OBS antigo é ignorado e "manter conectado" vale 30 dias como no Painel. O código dos computadores continua guardado para ligar no futuro |
| `Nora:ObsDevices:RequireForAttendants` | `true` | só vale com `Enabled=true`: exige computador ativado em sessões antigas de atendente. O cliente OBS ativa o computador ao entrar com acesso à unidade |
| `Nora:ObsDevices:KeepSignedInDays` | `30` | prazo, em dias (1 a 90), da sessão com "manter conectado" do **paciente** (login por CPF/e-mail e por Google; sem a opção vale 24 h, desde 26/11/2026) e do contrato antigo de sessão do OBS (só com computador). Os clientes WPF atuais têm prazos fixos de 24 horas e 30 dias e não seguem esta opção |
| `Nora:Maintenance:Enabled` | `true` | liga a limpeza periódica (sessões vencidas ou revogadas há mais de 30 dias, desafios de cadastro e de recuperação de senha e códigos de ativação de computadores do OBS vencidos há mais de 1 dia) e a reconciliação das sugestões de e-mail que sobraram na caixa de saída das UBS |
| `Nora:Maintenance:IntervalHours` | `6` | intervalo da limpeza, de 1 a 168 horas; o primeiro passe roda um minuto após a subida, e valor inválido impede a inicialização |
| `Nora:UnitLightningWorker:Enabled` | `true` | verificação de 15 s que expira e redistribui as ofertas de Vaga Relâmpago das UBS |
| `Nora:LegacyGlobalWorkflow:Enabled` | `false` | rotas globais antigas; só valem em `Development`, para fixtures de teste, e não são opção de implantação |
| `Nora:Google:ClientIds` | vazio (desligado) | client IDs OAuth aceitos no `aud` do ID token do Google (lista com índices ou separada por vírgula); vazio desliga o Entrar com Google (503 `google-disabled`) |
| `Nora:Google:JwksUrl` | `https://www.googleapis.com/oauth2/v3/certs` | chaves públicas do Google; só https (http só em loopback); inválida impede a inicialização |
| `Nora:Google:Issuers` | `accounts.google.com` e `https://accounts.google.com` | emissores aceitos; só para testes |
| `Nora:Push:ProjectId` | o `project_id` da credencial | projeto do Firebase; com projeto e sem credencial, a inicialização é impedida |
| `Nora:Push:ServiceAccountJson` | nenhum | JSON da conta de serviço do Firebase (segredo; tem precedência sobre o caminho) |
| `Nora:Push:ServiceAccountPath` | nenhum | caminho do arquivo JSON da conta de serviço (segredo); arquivo inexistente ou JSON/chave inválidos impedem a inicialização |
| `Nora:Push:Enabled` | `true` | liga o worker do push (só trabalha com projeto e credencial) |
| `Nora:Push:IntervalSeconds` | `30` | intervalo do worker, de 5 a 3600; o primeiro passe roda 30 s após a subida |
| `Nora:Push:MaxAttempts` | `5` | tentativas por aviso em falha transitória, de 1 a 20 |
| `Nora:Push:LookbackHours` | `24` | de 1 a 168: quanto para trás o worker procura decisões de família e, na primeira vez, comunicados |
| `Nora:Push:PresenceScanMinutes` | `5` | de 0 a 1440: intervalo da varredura da janela de confirmação de presença (0 = a cada passe) |

### Computadores do OBS — como implantar (P-014)

Antes de distribuir os clientes novos, aplique as migrações centrais até a 024 e das UBS até a 018, conforme o [guia de hospedagem](hospedagem.md). No OBS atual, um login válido com acesso à unidade ativa a instalação. Com várias unidades autorizadas, o primeiro acesso pede uma escolha. O administrador pode consultar e revogar computadores em **Informações da UBS → Computadores**. O cliente protege a identificação da instalação pelo DPAPI. A API recebe a credencial por HTTPS para verificá-la e guarda apenas o hash do segredo.

### Entrar com Google e push — como ligar (P-042)

Os dois ficam desligados e inofensivos sem configuração: o app não mostra os botões do Google, o login pelo Google responde 503 `google-disabled`, os aparelhos são aceitos e guardados, e o worker do push não enfileira nem envia nada. Nenhum valor real vai para o Git; os exemplos abaixo são nomes de chave com valores vazios ou fictícios. Valor inválido impede a subida da API com uma mensagem clara.

1. **Google Cloud (OAuth).** No projeto do Google Cloud ligado ao Firebase, configure a tela de consentimento OAuth e crie dois client IDs: um do tipo **Aplicativo da Web** (o "web client ID", que o app usa para pedir o ID token e que vira o `aud` do token) e um do tipo **Android** (nome do pacote `com.example.app_nora` e a impressão digital SHA-1 do certificado de assinatura, de debug e de release). O client ID web vai para a API em `Nora:Google:ClientIds` e para o app em `NORA_GOOGLE_WEB_CLIENT_ID` (`local.properties` ou propriedade Gradle); o client ID Android não vai para lugar nenhum, só precisa existir.
2. **Firebase (push).** No console do Firebase, adicione o app Android (mesmo pacote) e baixe o `google-services.json` para `APPNORA/app/` (fora do Git). Em "Configurações do projeto → Contas de serviço", gere uma chave privada da conta de serviço do Firebase Admin SDK (arquivo JSON). Ela é segredo: guarde fora do repositório.
3. **API.** Defina no ambiente do servidor ou no user-secrets da API:

```powershell
# Exemplos sem valores reais
$env:Nora__Google__ClientIds = "<web-client-id>.apps.googleusercontent.com"
$env:Nora__Push__ProjectId = "<id-do-projeto-firebase>"
$env:Nora__Push__ServiceAccountPath = "C:\segredos\nora-firebase-adminsdk.json"   # ou Nora__Push__ServiceAccountJson com o conteúdo
dotnet .build/Nora.Api/bin/Release/net10.0/Nora.Api.dll --migrate   # aplica a migração central 018
```

Depois de subir, `GET /api/patient/google` e `GET /api/patient/devices/push` respondem `enabled: true`. O worker faz o primeiro passe 30 s após a subida.

## Navegação e atualização desktop — 2026-10-11

Configurações e Sair da conta ficam no menu da conta NORA, no canto inferior esquerdo. O botão de três barras recolhe/expande a sidebar e mantém `Ctrl+B`; Configurações mantém `Ctrl+,`. A tela Pacientes não contém os botões Atualizar/Sair ou inclusão na fila. A inclusão passa a ser feita na própria tela Fila de espera, selecionando o paciente.

Depois de entrar, o workspace consulta a API automaticamente a cada 20 segundos, sem requisições concorrentes com operações do usuário. Mantém os campos do cadastro e a seleção ainda válida; erros de consulta continuam visíveis e uma sessão expirada pede nova entrada. As mensagens rotineiras de conta conectada/dados atualizados e preferências salvas ficam ocultas. Falhas de armazenamento de preferências permanecem visíveis, para não aparentar persistência quando a gravação falhou.

Um clique em Pacientes/Fila/Vagas mostra a barra de ações da seleção; o X limpa a seleção. Pacientes oferece Ver detalhes e Desativar/Reativar paciente aos perfis atendente e administrador. Fila oferece Ver detalhes e Cancelar entrada quando waiting/offered. Vagas oferece Ver detalhes. Duplo clique rápido ou Enter abre os detalhes; X/Escape fecha a ficha. Os quatro dados de identificação/contato são copiáveis por botão na ficha, com feedback; telefone ausente permanece explícito e não tem botão de copiar.

O seletor de paciente da Fila mostra somente o nome, tanto selecionado quanto nas opções. O template compartilhado de ComboBox respeita ItemTemplateSelector/DisplayMemberPath; a representação técnica do registro não é apresentada como rótulo.

## Painel OBS da UBS — 2026-10-15

O OBS apresenta Painel da UBS, Fila, Agenda, Pacientes, Família, Vaga Relâmpago, Indicadores, Alertas, Comunicados e Configurações da UBS pela barra lateral. Cada aba mostra somente o seu conteúdo: Painel concentra o resumo, Agenda concentra o calendário, Pacientes concentra a listagem e cadastro, Fila concentra as entradas e Vaga Relâmpago concentra as ofertas. A troca de aba usa uma única área de conteúdo, portanto não mantém a tela anterior por trás ou abaixo da atual. A lateral não possui botão de recolher; sua largura é ajustada arrastando a divisória entre navegação e conteúdo, entre 208 e 380 px, e essa escolha é preservada quando a preferência estiver ativada. Um botão de engrenagem chamado Configurações no rodapé abre a tela correspondente. A conta no rodapé também mostra o nome da sessão e um avatar de iniciais; o menu dela preserva Configurações e Sair. O cabeçalho informa a seção e que os dados pertencem à unidade conectada, sem alegar que o isolamento de unidade já é aplicado pela API.

A seleção de tema preserva Clara, Escura e Sistema, com a nova paleta azul-escuro e branca aplicada aos desktops. Configurações também reúne o cartão Perfil e unidade, com a identidade da sessão e as opções de nome, e-mail, senha e localidade. Nome, e-mail e localidade continuam indisponíveis porque a API não tem rota para salvá-los; os controles explicam a dependência em vez de aparentarem uma mudança local que não seria real. A senha já pode ser trocada (ver Senha da conta). Agenda, Indicadores e Alertas mostram o que a API entrega (consultas, métricas e alertas avaliados) e mantêm estados vazios claros quando não há dado. O calendário não cria consultas de exemplo e os gráficos não mostram séries inventadas.

Desativar/reativar pede confirmação dos efeitos no acesso e na fila. O botão de confirmação acompanha a disponibilidade da operação durante a atualização automática, evitando perder a ação quando a consulta estiver ocupada. Mudança de tela/filtro limpa ações que deixaram de corresponder a uma seleção válida; recarga conserva o item ainda existente pelo identificador. Logout/401 fecha fichas com dados do paciente e limpa a seleção.

## Presença do paciente — Android

Enquanto autenticado, o APPNORA inicia o serviço de presença com a tela visível e publica presença imediatamente e a cada 30 segundos, mantendo o envio em segundo plano ao abrir outro aplicativo ou a tela inicial. A notificação “NORA · conexão ativa” informa a conexão; no Android 13 ou superior o aplicativo solicita permissão para mostrá-la. Se a permissão for negada, o serviço continua indicado pelo gerenciador de tarefas do Android.

Sair da conta encerra o serviço. Fechar o aplicativo nos recentes limpa a sessão local, remove a notificação e tenta informar offline; em queda de rede, suspensão ou encerramento forçado do processo vale o prazo de 90 segundos da API, acrescido do intervalo de atualização do OBS. O serviço não reinicia uma sessão perdida e não grava o token em Intents, arquivos, preferências ou notificações. O controller permanece em memória durante rotação pelo ViewModel da Activity. Sessão vencida limpa identidade/dados e uma resposta antiga não repõe dados após essa limpeza.

Serviço e notificação foram autorizados pelo usuário em 2026-10-11. A rodada Pixel_4 passou 14/14 instrumentados sem skips, incluindo próximo heartbeat enquanto a Activity está em segundo plano, toque na notificação preservando Activity/ViewModel e remoção da tarefa encerrando conta/notificação. O transporte real foi exercitado com API/PostgreSQL descartáveis. A implementação declara o tipo specialUse e seu subtipo para Android 14+, preserva uma tentativa final de offline com timeout e deixa o prazo da API como limite em caso de morte do processo. Conferência em aparelhos físicos/versões Android adicionais continua em P-003. [Serviços foreground Android](https://developer.android.com/develop/background-work/services/fgs).

## Senha da conta — 2026-11-15

Nos três aplicativos a Configurações (no Android, o Perfil) tem "Alterar senha": a pessoa informa a senha atual e a nova (8 a 256 caracteres, diferente da atual). A senha atual incorreta volta como erro do formulário, sem encerrar a sessão; em caso de sucesso as outras sessões da conta são revogadas e a atual continua. Conta criada pela Secretaria nasce com senha provisória e, no primeiro login, a API responde 403 `password-change-required` às demais rotas até a troca; os apps levam a pessoa à tela de nova senha. Senhas não são salvas nas preferências.
