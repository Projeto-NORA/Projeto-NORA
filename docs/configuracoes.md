# Configurações e preferências

## Aparência

Os três aplicativos oferecem Clara, Escura e Sistema. Sistema é o padrão inicial; acompanha o Windows nos desktops e o tema do dispositivo no Android. A escolha é persistida e reaplicada ao abrir, com mudança da interface sem reinicialização. Os cards indicam a opção escolhida visualmente e pela acessibilidade.

## Desktops

OBS e Municipal salvam arquivos independentes em `%LOCALAPPDATA%/NORA/obs/preferences.json` e `%LOCALAPPDATA%/NORA/municipal/preferences.json`. O serviço lê/normaliza tema e rota, recupera padrões quando o JSON é inválido e informa o erro na barra inferior. A leitura preserva o arquivo corrompido; uma alteração posterior de preferência substitui o conteúdo pelo estado atual. A gravação usa arquivo temporário e substituição do destino. Se falhar, a interface informa que a mudança vale apenas para a sessão.

As preferências controlam dicas na sidebar, confirmação de fechamento, restauração da navegação, animação ao trocar tela, lembrança da sidebar e tela cheia. Desativar a restauração de navegação faz a próxima abertura iniciar na Home. Desativar a lembrança da sidebar faz a próxima abertura mostrá-la expandida. Restaurar padrões mantém Configurações aberta na sessão atual e salva Home para a próxima abertura.

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

Os atalhos são exibidos e fixos nesta etapa; não há editor de atalhos. Não foram anunciados comandos de pesquisa, atualização ou ações clínicas sem implementação correspondente.

## Android

`SettingsStore` usa SharedPreferences privadas chamadas `nora_preferences`. Tema e quatro escolhas são persistidos: avisos de consultas, avisos de Vaga Relâmpago, mostrar orientações e espaçamento confortável. Valores com tipos inválidos são substituídos pelos padrões seguros na leitura. Tema é Sistema e as quatro opções são ativadas inicialmente.

Mostrar orientações controla o card de personalização da Home; espaçamento confortável alterna intervalos entre cards. Avisos são preferências locais, entrega externa em P-013. Sobre mostra versão/estado da conta; sair é habilitado para sessão conectada. Restaurar padrões pede confirmação e não encerra a conta.

Endpoint é salvo em SharedPreferences `nora_connection` após login válido. HTTPS é obrigatório em Release; Debug permite HTTP10.0.2.2 para emulador. Trocar endereço exige sair da conta. Token e identidade ficam somente em memória e não entram em backup/preferências. Desktop permite endereço HTTPS ou HTTP loopback, informado na tela ou por NORA_API_URL. Reabrir os três clientes exige login CPF/e-mail novamente.

Home e Configurações têm rolagens independentes. Abrir Configurações volta ao topo, e o retorno preserva a rolagem da Home; a interação foi confirmada pelos testes instrumentados no Pixel_4. O conteúdo fica centralizado e limitado a 640 dp em telas amplas; a conferência visual de tela larga, fonte ampliada e reabertura estável continua em P-003.

Quando a API confirma uma resposta de oferta e a atualização posterior falha, o Android mantém a oferta como aceita/recusada e informa que a resposta foi confirmada, orientando Atualizar. Não oferece os botões de resposta novamente com dados pendentes antigos. Se a atualização retorna401, limpa os dados da conta e pede nova entrada. O OBS também distingue mutação confirmada de falha de recarga; Atualizar concluído informa sucesso em vez de continuar exibindo conexão em andamento.

## Navegação e atualização desktop — 2026-10-11

Configurações e Sair da conta ficam no menu da conta NORA, no canto inferior esquerdo. O botão de três barras recolhe/expande a sidebar e mantém `Ctrl+B`; Configurações mantém `Ctrl+,`. A tela Pacientes não contém os botões Atualizar/Sair ou inclusão na fila. A inclusão passa a ser feita na própria tela Fila de espera, selecionando o paciente.

Depois de entrar, o workspace consulta a API automaticamente a cada 20 segundos, sem requisições concorrentes com operações do usuário. Mantém os campos do cadastro e a seleção ainda válida; erros de consulta continuam visíveis e uma sessão expirada pede nova entrada. As mensagens rotineiras de conta conectada/dados atualizados e preferências salvas ficam ocultas. Falhas de armazenamento de preferências permanecem visíveis, para não aparentar persistência quando a gravação falhou.

## Presença do paciente — Android

Enquanto autenticado, o APPNORA inicia o serviço de presença com a tela visível e publica presença imediatamente e a cada 30 segundos, mantendo o envio em segundo plano ao abrir outro aplicativo ou a tela inicial. A notificação “NORA · conexão ativa” informa a conexão; no Android 13 ou superior o aplicativo solicita permissão para mostrá-la. Se a permissão for negada, o serviço continua indicado pelo gerenciador de tarefas do Android.

Sair da conta encerra o serviço. Fechar o aplicativo nos recentes limpa a sessão local, remove a notificação e tenta informar offline; em queda de rede, suspensão ou encerramento forçado do processo vale o prazo de 90 segundos da API, acrescido do intervalo de atualização do OBS. O serviço não reinicia uma sessão perdida e não grava o token em Intents, arquivos, preferências ou notificações. O controller permanece em memória durante rotação pelo ViewModel da Activity. Sessão vencida limpa identidade/dados e uma resposta antiga não repõe dados após essa limpeza.

Serviço e notificação foram autorizados pelo usuário em 2026-10-11. A rodada Pixel_4 passou 14/14 instrumentados sem skips, incluindo próximo heartbeat enquanto a Activity está em segundo plano, toque na notificação preservando Activity/ViewModel e remoção da tarefa encerrando conta/notificação. O transporte real foi exercitado com API/PostgreSQL descartáveis. A implementação declara o tipo specialUse e seu subtipo para Android 14+, preserva uma tentativa final de offline com timeout e deixa o prazo da API como limite em caso de morte do processo. Conferência em aparelhos físicos/versões Android adicionais continua em P-003. [Serviços foreground Android](https://developer.android.com/develop/background-work/services/fgs).
