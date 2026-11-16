# Configurações e preferências

## Aparência

Os três aplicativos oferecem Clara, Escura e Sistema. Sistema é o padrão inicial; acompanha o Windows nos desktops e o tema do dispositivo no Android. A escolha é persistida e reaplicada ao abrir, com mudança da interface sem reinicialização. Os cards indicam a opção escolhida visualmente e pela acessibilidade.

## Desktops

OBS e Municipal salvam arquivos independentes em `%LOCALAPPDATA%/NORA/obs/preferences.json` e `%LOCALAPPDATA%/NORA/municipal/preferences.json`. O serviço lê/normaliza tema e rota, recupera padrões quando o JSON é inválido e informa o erro na barra inferior. A leitura preserva o arquivo corrompido; uma alteração posterior de preferência substitui o conteúdo pelo estado atual. A gravação usa arquivo temporário e substituição do destino. Se falhar, a interface informa que a mudança vale apenas para a sessão.

As preferências controlam dicas na sidebar, confirmação de fechamento, restauração da navegação, animação ao trocar tela, lembrança da largura da sidebar e tela cheia. Desativar a restauração de navegação faz a próxima abertura iniciar na Home. Desativar a lembrança da sidebar faz a próxima abertura usar a largura padrão. Restaurar padrões mantém Configurações aberta na sessão atual e salva Home para a próxima abertura.

Navegar entre Home e Configurações abre a tela pelo topo, sem herdar a rolagem da anterior. O comportamento vale nos dois desktops com animações ativadas ou desativadas, verificado por invocação dos botões na janela nativa. O diálogo de fechamento também foi medido: seus botões cabem na área cliente no ambiente testado; DPI ampliado continua em P-007.

Tela cheia cobre os limites do monitor e remove a borda da janela. Ao sair, restaura dimensão, estilo e estado anteriores. A validação de múltiplos monitores e DPI diferentes ainda depende de conferência manual.

| Atalho | Ação |
| --- | --- |
| Ctrl + 1 | Visão geral |
| Ctrl + , | Configurações |
| Ctrl + B | Recolher/expandir sidebar |
| Ctrl + Shift + T | Ciclar Clara, Escura e Sistema |
| F11 | Entrar/sair de tela cheia |
| Esc | Sair de tela cheia |
| Alt + F4 | Fechar, respeitando confirmação |

Os atalhos são exibidos em cartões com a combinação destacada e a ação explicada; permanecem fixos nesta etapa, sem editor de atalhos. Não foram anunciados comandos de pesquisa, atualização ou ações clínicas sem implementação correspondente.

## Android

`SettingsStore` usa SharedPreferences privadas chamadas `nora_preferences`. Tema e quatro escolhas são persistidos: avisos de consultas, avisos de Vaga Relâmpago, mostrar orientações e espaçamento confortável. Valores com tipos inválidos são substituídos pelos padrões seguros na leitura. Tema é Sistema e as quatro opções são ativadas inicialmente.

Na entrada, Configurações pode ser aberta pelo diálogo do ícone de ajuda; após login, também fica no topo do início e no Perfil. A tela agrupa aparência, notificações, interface e informações do app. As escolhas de notificações são locais e a tela informa que a entrega depende da integração da rede. A opção de sair é exibida quando há uma conta conectada.

Mostrar orientações controla o card de personalização da Home; espaçamento confortável alterna intervalos entre cards. Avisos são preferências locais, entrega externa em P-013. Sobre mostra versão/estado da conta; sair é habilitado para sessão conectada. Restaurar padrões pede confirmação e não encerra a conta.

Endpoint é salvo em SharedPreferences `nora_connection` após login válido. HTTPS é obrigatório em Release; Debug permite HTTP10.0.2.2 para emulador. Trocar endereço exige sair da conta. Token e identidade ficam somente em memória e não entram em backup/preferências. Desktop permite endereço HTTPS ou HTTP loopback, informado na tela ou por NORA_API_URL. Reabrir os três clientes exige login CPF/e-mail novamente.

Home e Configurações têm rolagens independentes. Abrir Configurações volta ao topo, e o retorno preserva a rolagem da Home; a interação foi confirmada pelos testes instrumentados no Pixel_4. O conteúdo fica centralizado e limitado a 640 dp em telas amplas; a conferência visual de tela larga, fonte ampliada e reabertura estável continua em P-003.

Quando a API confirma uma resposta de oferta e a atualização posterior falha, o Android mantém a oferta como aceita/recusada e informa que a resposta foi confirmada, orientando Atualizar. Não oferece os botões de resposta novamente com dados pendentes antigos. Se a atualização retorna401, limpa os dados da conta e pede nova entrada. O OBS também distingue mutação confirmada de falha de recarga; Atualizar concluído informa sucesso em vez de continuar exibindo conexão em andamento.

## Navegação e atualização desktop — 2026-10-11

Configurações e Sair da conta ficam no menu da conta NORA, no canto inferior esquerdo. O botão de três barras recolhe/expande a sidebar e mantém `Ctrl+B`; Configurações mantém `Ctrl+,`. A tela Pacientes não contém os botões Atualizar/Sair ou inclusão na fila. A inclusão passa a ser feita na própria tela Fila de espera, selecionando o paciente.

Depois de entrar, o workspace consulta a API automaticamente a cada 20 segundos, sem requisições concorrentes com operações do usuário. Mantém os campos do cadastro e a seleção ainda válida; erros de consulta continuam visíveis e uma sessão expirada pede nova entrada. As mensagens rotineiras de conta conectada/dados atualizados e preferências salvas ficam ocultas. Falhas de armazenamento de preferências permanecem visíveis, para não aparentar persistência quando a gravação falhou.

Um clique em Pacientes/Fila/Vagas mostra a barra de ações da seleção; o X limpa a seleção. Pacientes oferece Ver detalhes e Desativar/Reativar paciente aos perfis atendente e administrador. Fila oferece Ver detalhes e Cancelar entrada quando waiting/offered. Vagas oferece Ver detalhes. Duplo clique rápido ou Enter abre os detalhes; X/Escape fecha a ficha. Os quatro dados de identificação/contato são copiáveis por botão na ficha, com feedback; telefone ausente permanece explícito e não tem botão de copiar.

O seletor de paciente da Fila mostra somente o nome, tanto selecionado quanto nas opções. O template compartilhado de ComboBox respeita ItemTemplateSelector/DisplayMemberPath; a representação técnica do registro não é apresentada como rótulo.

## Painel OBS da UBS — 2026-10-15

O OBS apresenta Painel da UBS, Fila, Agenda, Pacientes, Vaga Relâmpago, Indicadores e Alertas pela barra lateral. Cada aba mostra somente o seu conteúdo: Painel concentra o resumo, Agenda concentra o calendário, Pacientes concentra a listagem e cadastro, Fila concentra as entradas e Vaga Relâmpago concentra as ofertas. A troca de aba usa uma única área de conteúdo, portanto não mantém a tela anterior por trás ou abaixo da atual. A lateral não possui botão de recolher; sua largura é ajustada arrastando a divisória entre navegação e conteúdo, entre 208 e 380 px, e essa escolha é preservada quando a preferência estiver ativada. Um botão de engrenagem chamado Configurações no rodapé abre a tela correspondente. A conta no rodapé também mostra o nome da sessão e um avatar de iniciais; o menu dela preserva Configurações e Sair. O cabeçalho informa a seção e que os dados pertencem à unidade conectada, sem alegar que o isolamento de unidade já é aplicado pela API.

A seleção de tema preserva Clara, Escura e Sistema, com a nova paleta azul-escuro e branca aplicada aos desktops. Configurações também reúne o cartão Perfil e unidade, com a identidade da sessão e as opções de nome, e-mail, senha e localidade. Essas alterações continuam indisponíveis até a API oferecer uma operação autenticada para salvá-las; os controles explicam a dependência em vez de aparentarem uma mudança local que não seria real. A Agenda, Indicadores e Alertas mantêm estados vazios claros enquanto a API não entrega consultas, métricas e regras de alerta. O calendário não cria consultas de exemplo e os gráficos não mostram séries inventadas.

Desativar/reativar pede confirmação dos efeitos no acesso e na fila. O botão de confirmação acompanha a disponibilidade da operação durante a atualização automática, evitando perder a ação quando a consulta estiver ocupada. Mudança de tela/filtro limpa ações que deixaram de corresponder a uma seleção válida; recarga conserva o item ainda existente pelo identificador. Logout/401 fecha fichas com dados do paciente e limpa a seleção.

## Presença do paciente — Android

Enquanto autenticado, o APPNORA inicia o serviço de presença com a tela visível e publica presença imediatamente e a cada 30 segundos, mantendo o envio em segundo plano ao abrir outro aplicativo ou a tela inicial. A notificação “NORA · conexão ativa” informa a conexão; no Android 13 ou superior o aplicativo solicita permissão para mostrá-la. Se a permissão for negada, o serviço continua indicado pelo gerenciador de tarefas do Android.

Sair da conta encerra o serviço. Fechar o aplicativo nos recentes limpa a sessão local, remove a notificação e tenta informar offline; em queda de rede, suspensão ou encerramento forçado do processo vale o prazo de 90 segundos da API, acrescido do intervalo de atualização do OBS. O serviço não reinicia uma sessão perdida e não grava o token em Intents, arquivos, preferências ou notificações. O controller permanece em memória durante rotação pelo ViewModel da Activity. Sessão vencida limpa identidade/dados e uma resposta antiga não repõe dados após essa limpeza.

Serviço e notificação foram autorizados pelo usuário em 2026-10-11. A rodada Pixel_4 passou 14/14 instrumentados sem skips, incluindo próximo heartbeat enquanto a Activity está em segundo plano, toque na notificação preservando Activity/ViewModel e remoção da tarefa encerrando conta/notificação. O transporte real foi exercitado com API/PostgreSQL descartáveis. A implementação declara o tipo specialUse e seu subtipo para Android 14+, preserva uma tentativa final de offline com timeout e deixa o prazo da API como limite em caso de morte do processo. Conferência em aparelhos físicos/versões Android adicionais continua em P-003. [Serviços foreground Android](https://developer.android.com/develop/background-work/services/fgs).

## Senha da conta — 2026-11-15

Nos três aplicativos a Configurações (no Android, o Perfil) tem "Alterar senha": a pessoa informa a senha atual e a nova (8 a 256 caracteres, diferente da atual). A senha atual incorreta volta como erro do formulário, sem encerrar a sessão; em caso de sucesso as outras sessões da conta são revogadas e a atual continua. Conta criada pela Secretaria nasce com senha provisória e, no primeiro login, a API responde 403 `password-change-required` às demais rotas até a troca; os apps levam a pessoa à tela de nova senha. Senhas não são salvas nas preferências.
