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

Mostrar orientações controla o card de personalização da Home; espaçamento confortável alterna os intervalos entre cards. As opções de avisos são salvas, mas só produzirão notificações após integração do serviço. Sobre o app mostra versão e estado da conta; sair está indisponível enquanto não houver conta. A restauração de padrões pede confirmação.

Home e Configurações têm rolagens independentes. Abrir Configurações volta ao topo, e o retorno preserva a rolagem da Home; a interação foi confirmada pelos testes instrumentados no Pixel_4. O conteúdo fica centralizado e limitado a 640 dp em telas amplas; a conferência visual de tela larga, fonte ampliada e reabertura estável continua em P-003.
