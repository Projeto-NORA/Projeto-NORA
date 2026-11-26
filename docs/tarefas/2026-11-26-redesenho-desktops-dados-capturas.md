# Redesenho do OBS e do Painel Municipal, dados fictícios e capturas — 2026-11-26

## Skills e comandos invocados

- `/aura-retomar-execucao:aura` (carrega `protocolo-tarefa-complexa`, `diagnosing-bugs`, `modo-loop-verificacao` e chama a `graphify`).

## Onde o trabalho acontece

Worktree `.claude/worktrees/desktop-redesign-ui`, branch `claude/desktop-redesign-ui`, criada de `origin/main` em `8386b04` (25/11/2026, com os ajustes da Luana). O worktree anterior da sessão (`hospital-simulation-setup-34fbf4`) ficou na branch `codex/cadastro-medicos-http` com quatro arquivos alterados e não commitados por outro chat; não foi tocado.

## O que foi pedido, item por item

Vale para o **OBS e para o Painel Municipal** em tudo o que os dois têm em comum; o que não existir no Painel é ignorado. O software WPF do usuário é a referência para qualquer decisão; dúvida que sobrar vai ao usuário.

### A. Geral (os dois desktops)

- **A1** Título da tela só na barra de cima, como está hoje. Em subtela: `Listagem • Pacientes` (subtela, ponto preto, tela principal). Nenhum outro título repetido dentro da tela (sai o banner "Painel operacional / Sua unidade em um só lugar" e semelhantes).
- **A2** Login salvo no app e nos dois desktops: sem marcar nada vale 1 dia; marcando manter por 30 dias vale 30 dias. A forma fica a critério da IA.
- **A3** Barra de título (a de minimizar, maximizar e fechar): sai o texto "NORA OBS" do canto superior esquerdo. Entram ali os botões abrir/fechar a barra de navegação, voltar, avançar e pesquisar, como no app do Claude (imagem 1). A marca continua só na barra de navegação.
- **A4** O bloco "UBS da operação / Escolher ou trocar UBS" sai de todas as telas. No menu do perfil (canto inferior esquerdo) entra **Trocar perfil**, acima de Configurações: abre um pop-up com os perfis que a pessoa pode acessar; ela escolhe um e clica em **Trocar**; o X no canto superior direito desiste.
- **A5** Listagens abertas: sem card nem divisão atrás; os itens ficam soltos na tela.
- **A6** Sai o chip do canto superior direito ("Jacyra Vieira Baracho", "Gestão conectada") de todas as telas. No lugar entram os filtros quando a tela precisar; senão, nada.
- **A7** Tela que só tem o filtro de pesquisa: em vez do botão "Filtros", o campo de pesquisa direto, com a lupa à direita.
- **A8** Os botões de ação só aparecem com um item selecionado, numa faixa na parte de baixo da tela. Em todas as listagens, idêntico ao software WPF.
- **A9** Duplo clique no item abre um pop-up de detalhes organizado em linhas horizontais (sem cards), como no software WPF.
- **A10** Listagem na tela inteira, com itens menores e só as informações principais.
- **A11** Nenhuma tela tem botão "Atualizar": o programa atualiza sozinho.
- **A12** Cadastros: campos lado a lado em grade (cerca de 300 px por campo; 2 colunas em 1366×768, 3 em telas maiores, 1 em telas pequenas; as colunas esticam por igual; o campo sozinho na última linha fica do mesmo tamanho dos outros, no meio); botão de salvar à direita. Vale para todos os cadastros. O cadastro fica livre na tela, sem card.
- **A13** Botões "novo/nova…" em cima à direita (à direita dos filtros; sem filtros, o mais à direita possível).
- **A14** Hover de todos os botões em azul bebê claro (hoje branco/cinza), nos dois softwares.
- **A15** Remover ou desativar é sempre um botão de lixeira branca em fundo vermelho, como no software WPF.
- **A16** A seta de voltar ao lado do título da tela deixa de existir (vai para a barra de título).
- **A17** O botão "Configurações" do rodapé da navegação some (fica só no menu do perfil).
- **A18** Menu do perfil com ícones: Configurações (engrenagem), Sair da conta (porta), Dados do perfil (pessoa).
- **A19** Cards com cor (a maioria).
- **A20** Preferência geral: sem cards; um pop-up já é o card, então nada de card dentro de pop-up.

### B. Painel da UBS (OBS)

- **B1** Sem "UBS da operação" e sem o banner; o topo passa a ser o "Resumo do dia".
- **B2** Em cima à direita: pesquisa e período (combo com dia, semana, mês, ano, 5 anos e máximo).
- **B3** À direita da pesquisa, um botão que alterna cards e gráficos (ícone de card ↔ ícone de gráfico).
- **B4** Gráficos mais bonitos e vívidos; "cancelado" em amarelo. No modo gráficos não há "ver dados em tabela". No modo cards há cards e tabelas.
- **B5** "Atenção da equipe" só no modo cards; o que não cabe em gráfico não aparece no modo gráficos.
- **B6** "Fluxo da unidade" sai da parte de baixo e vai para o resumo do dia.
- **B7** Cada card que comporta tem gráfico de linha e de pizza. Todo gráfico tem o título e, à direita dele, "Linha | Pizza" para alternar; idem nos pop-ups abertos pelo card.
- **B8** No pop-up do gráfico fica só o título: sai "Valor atual: 32" (imagem 2).

### C. Pacientes (OBS)

- **C1** Listagem: sai o botão "Cadastrar paciente" e o "Escolher UBS"; lista na tela inteira.
- **C2** Item: nome; embaixo `CPF | e-mail | telefone`, só os informados; sem ícone de pessoa.
- **C3** Cadastro: sai o título "Pacientes" e o trocar UBS; "Cadastrar referência na UBS" na grade de A12; botão **Cadastrar paciente** embaixo à direita; o endereço continua como seção opcional.

### D. Família (OBS)

- **D1** No menu, abaixo de Família, o item novo **Vínculos aprovados**, com a listagem deles.
- **D2** Família passa a ter uma listagem só (os pedidos).
- **D3** O motivo da recusa só aparece no pop-up aberto por "Recusar pedido": campo "Motivo da recusa (opcional, até 280 caracteres)", botões Enviar e Fechar; depois, a mensagem do que aconteceu (aprovou; recusou; recusou e o motivo foi enviado).

### E. Agenda (OBS)

- **E1** Filtros: busca na listagem e período dia/semana/mês/ano, como o calendário do celular. Dia: o que há no dia. Semana: tabela com os dias em cima e as consultas por hora. Mês: como hoje. Ano: os 12 meses em 4×3.
- **E2** "Agendar nova consulta" mantém o texto e fica em cima à direita.
- **E3** Dia selecionado: fundo azul bem claro e um círculo azul bebê no número; hover em azul bebê (hoje o dia fica todo azul-escuro e o número some, imagem 3).
- **E4** Lista do dia: sem o título do dia; item em duas linhas (nome; "08:20 às 08:40") e, à direita, a situação como etiqueta colorida.
- **E5** Duplo clique: pop-up de detalhes mais estilizado, em linhas horizontais.

### F. Vaga Relâmpago (Em andamento e Encerradas)

- **F1** Sem o título e sem os totais (abertas, em oferta, aceitas, recusadas, vencidas).
- **F2** Filtros em cima à direita.
- **F3** Listagem no padrão novo.

### G. Indicadores, Alertas e Comunicados

- **G1** Indicadores: mesma alternância cards/gráficos do Painel; cards bem resumidos.
- **G2** Alertas e Comunicados: só a listagem, sem textos.

### H. Informações da UBS (OBS)

- **H1** Atendimento: sem "Fechado"; caixa de seleção à esquerda de cada dia (marcada mostra os campos de abre e fecha); dois campos cerca de 1,2× mais baixos com "às" entre eles; sem o segundo intervalo; "Serviços oferecidos" continua; sem "Especialidades com horário na grade"; sem card e sem o título "Atendimento ao público"; o aviso "Ainda não informado" vai para o topo como item destacado em amarelo.
- **H2** Profissionais: listagem na tela inteira; **+ Novo profissional** em cima à direita abre pop-up com o cadastro; sem o aviso "da lista da rede"; explicar ao usuário o que é o conselho profissional.
- **H3** Consultórios: igual a Profissionais.
- **H4** Grade semanal: **Novo horário** em cima à direita abre pop-up (sem card dentro), com "Salvar horário na grade" à direita; botões dos dias da semana (segunda selecionada primeiro) e, abaixo, a listagem do dia.
- **H5** Bloqueios: sem o título de baixo; listagem padrão; remover pela faixa de ações (lixeira); **Novo bloqueio** em cima à direita abre pop-up com **Cadastrar bloqueio** embaixo à direita.
- **H6** A tela Computadores é excluída.

### I. Painel Municipal

- **I1** Aplicar tudo o que for comum; dizer ao usuário o que faltou ele analisar no Painel.

### J. Dados, capturas e slides

- **J1** Script de inserts que mantém os dados atuais e acrescenta dados fictícios bonitos para as capturas.
- **J2** Capturas de cada tela do OBS, do Painel e do app, com foco nas mais importantes; entregar uma pasta normal (não zip) e um arquivo Excel com as mesmas imagens.
- **J3** Indicar as 10 melhores imagens, que parte de cada uma mostrar, 4 slides com 3 imagens cada e as 20 melhores.

## Objetivo

OBS e Painel Municipal com o visual e o comportamento do software WPF de referência (barra de título com navegação, listagens abertas com faixa de ações e ficha de detalhe, cadastros em grade, sem títulos repetidos), login persistente nos três clientes, dados fictícios para capturas e o material de imagens para os slides.

## Restrições

- Fazer com calma e revisar cada parte contra o pedido; perguntar em caso de dúvida.
- Analisar o software WPF de referência antes de decidir ou de perguntar.
- Manter os dados que já existem (o script só acrescenta).
- O que não existir no Painel Municipal não é inventado lá.

## Decisões do usuário (26/11/2026)

- **Dados fictícios:** entram no **Neon de produção**. Como a IA não digita senha em serviço de produção, o script é escrito para qualquer banco NORA: a IA roda num banco local para tirar as capturas e o usuário roda no Neon.
- **Prazo:** sem pressa; seguir a ordem do pedido, tela por tela, validando cada uma.
- **Luana:** não está mexendo nos desktops; a branch segue a partir da `main` de 25/11.
- **"Sugerir UBS pela localização":** vai para o pop-up "Trocar perfil", como botão à esquerda do botão **Trocar**.

## O que a análise mostrou (e muda o tamanho do trabalho)

- A barra de título é a nativa do Windows (`ShellWindow.xaml:1`, sem `WindowChrome`). Para pôr botões nela é preciso uma barra própria, e isso toca `FullScreenService`, `ApplyWindowCorners` e `tests/desktop-startup.ps1` (que lê o título da janela).
- Não existe "avançar" na navegação (`ShellViewModel.cs:145-197` só tem a pilha de voltar). Ctrl+B não recolhe a barra lateral de verdade.
- Quase toda a interface dos dois programas está em `Nora.Desktop`; o Painel Municipal é montado em C# (`MunicipalView.xaml.cs`).
- Não há componente de listagem com seleção, faixa de ações nem painel de formulário em grade; o estilo `FloatingActions` existe sem uso. Não há estilo de botão de perigo.
- **API:** o período dos indicadores é limitado a 366 dias (`MetricsService.cs:25`), então "5 anos" e "máximo" exigem mudar a API. A sessão do paciente dura 8 h e o app guarda o token só na memória (`PatientController.kt:81`), então o login salvo no app exige mudar a API e o app. Nos desktops, sem marcar a caixa a sessão não é gravada em disco (`DesktopAccess.cs:86`).
- As checagens de `tests/Nora.Desktop.Checks` (185 casos) dependem de nomes e textos dos controles: mudam junto com as telas.

## Checklist

- [ ] Análise do NORA (shell, telas, listas, filtros, gráficos, sessão) e do software WPF de referência
- [ ] Dúvidas levadas ao usuário e respondidas
- [ ] A (geral) · [ ] B (Painel da UBS) · [ ] C (Pacientes) · [ ] D (Família) · [ ] E (Agenda) · [ ] F (Vaga Relâmpago) · [ ] G (Indicadores, Alertas, Comunicados) · [ ] H (Informações da UBS) · [ ] I (Painel Municipal)
- [ ] A2 login salvo no app Android
- [ ] J1 script de dados · [ ] J2 capturas, pasta e Excel · [ ] J3 indicação das imagens
- [ ] Validação: build da solução, checagens de desktop e de API, suíte do app, abertura real dos dois programas
- [ ] Loop de verificação até uma volta sem achados
- [ ] Documentação (`obs-painel-operacional.md`, `secretaria.md`, `design.md`, `app-paciente.md`) e `PENDENCIAS.md`

## Estado

- 26/11/2026: skills carregadas, branch e worktree criados, pedido registrado. Análise em andamento. Nenhum código alterado ainda.
