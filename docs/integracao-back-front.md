# Integração back-end x front-end — estado em 2026-11-11

## Estado em 2026-11-15

O levantamento abaixo é o de 2026-11-11 e fica como histórico. Desde então as lacunas dos grupos 1 e 2 foram resolvidas e commitadas (HEAD `3d46a78`); o que segue em andamento é o grupo 3, pela Fase 3. Detalhes por pendência em [PENDENCIAS.md](PENDENCIAS.md) e as decisões do usuário em `tarefas/2026-11-11-integracao-back-front.md`.

**Resolvido.**

- Grupo 1, OBS: faixa única de mensagens e barra de progresso, carga e escolha da UBS no login, agenda por mês com marcações e botões por estado, fila com posição e tempo de espera, atualização automática sem desabilitar formulários (P-023 a P-026; `7ba3c54`).
- Grupo 1, Secretaria: cadastro de referência completo, agenda com período, totais novos (P-028; `dbe3eb8`).
- Grupo 1, APPNORA: sessão expirada, ofertas com contagem regressiva e prazo, consulta automática a cada 30 s com notificação local, UBS única selecionada, estado preservado na rotação (P-031, P-032; `30f2fcf`).
- Legado global removido dos três clientes, com testes reescritos para o fluxo por UBS (P-027).
- Grupo 2: histórico de ações (P-029), indicadores por UBS e da rede com o score de comparecimento (P-030, só a rota e as telas), contagens novas do resumo (P-033), banco da UBS criado pela API ao cadastrar (P-034), troca de senha, contas OBS pela Secretaria, sair da fila e remover UBS (P-035), ofertas de uma vaga, participação (score de 12 meses) na ficha do paciente no OBS e na Jornada do app.
- Quinze achados da revisão crítica do back, corrigidos: pool por UBS, revalidação de sessão nos executores, versão do schema da UBS (503 `unit-database-outdated`), limites por conta, `ForwardedHeaders` só com proxy declarado, cursor do histórico, `REVOKE CONNECT` nos bancos de UBS, índice da espera média.
- Números: build Release 0/0; API HTTP 30/30; PostgreSQL 18 fluxos 28/28, bancos separados PASS, plataforma 19/19, revisão 17/17; desktop 103/103; E2E com banco 104/104; Android 84/84 locais, 28 previews, lint 0 erros e 6 avisos antigos. Os testes instrumentados do Android só compilam nesta máquina (P-040).

**Em andamento (Fase 3, grupo 3; P-030 e P-037).** As regras já foram decididas: agenda, capacidade, metas e alertas (grade semanal por profissional, bloqueios por data, consultórios, especialidades únicas da rede, agendamento pelo paciente de 24 h a 90 dias, confirmação de 48 h a 2 h, metas com sobrescrita por UBS e os limites de alerta); comunicação e jornada (comunicados para equipes e pacientes, exames e vacinas lançados pelo OBS); e família (vínculo pedido pelo responsável e aprovado pela equipe). O back está em dois worktrees (A: agenda, capacidade, metas e alertas; B: comunicação e jornada), depois vêm família e as telas nos três apps. Até lá as telas correspondentes continuam com aviso de indisponibilidade. "Entrar com Google" e push por servidor seguem sem definição.

**Ainda aberto fora da Fase 3.** P-036 (manutenção e segurança que não foi tratada pela revisão) e P-040 (testes instrumentais do Android com API real).

**Como ler o restante.** As seções abaixo descrevem as lacunas como estavam em 2026-11-11; onde disserem "só front", "falta rota" ou "ainda não corrigido" para os itens da lista de resolvidos, o texto está superado.

Este documento cruza o que a `Nora.Api` oferece com o que os três clientes
consomem (OBS, Secretaria e APPNORA), depois do commit `83ebcd4` ("faltam
integrações com o back"). Os contratos estão em [api.md](api.md); aqui fica só
o que **falta ligar**, de que lado falta e por quê. As pendências correspondentes
estão em [PENDENCIAS.md](PENDENCIAS.md), de P-023 em diante.

## Como ler

Cada lacuna cai em um de três grupos:

- **Grupo 1 — só front.** A API já entrega; falta a tela usar, mostrar ou
  tratar. Não depende de regra de negócio nova.
- **Grupo 2 — falta rota no back, o dado já existe.** O banco já grava a
  informação; não há rota que a leia ou agregue. Depende de definição (fórmula,
  quem pode ver).
- **Grupo 3 — falta dado e definição.** Nem o banco guarda. Depende de decisão
  de produto antes de qualquer código.

## Cobertura de rotas

No nível de cliente HTTP, toda rota atual da API tem um método correspondente:
`NoraApiClient.cs` (desktop) cobre os grupos `obs` e `municipal`; `NoraApi.kt`
(Android) cobre `patient`, cadastro público e recuperação de senha. Envelopes,
nomes de campo e enums de status conferem nos três clientes. As exceções:

- `GET /api/auth/me` não é chamado por nenhum cliente.
- Os clientes ainda carregam os métodos do **fluxo global legado**
  (`/api/patients`, `/api/queue`, `/api/slots`, `/api/patient/overview`,
  `/api/offers/{id}/respond`). O servidor só libera essas rotas em
  `Development` com `Nora:LegacyGlobalWorkflow:Enabled=true`; fora disso
  responde 403. Nenhum caminho de tela chega neles hoje: no desktop os blocos
  estão com `Visibility="Collapsed"`, no Android só os testes os chamam.
- Nenhum cliente lê o campo `code` do `problem+json`; os erros são traduzidos só
  pelo status HTTP, o que deixa as mensagens de 403, 409 e 503 genéricas.

As lacunas reais estão, portanto, na camada de tela e em rotas que ainda não
existem.

## OBS (operação da UBS)

### Grupo 1 — só front

- **Retorno das ações invisível.** A faixa de mensagem só existe em
  `WorkspaceView.xaml` (Pacientes, Fila, Vaga Relâmpago). Em Painel, Agenda,
  Indicadores e Alertas, sucesso e erro de qualquer ação não aparecem. A barra
  de progresso nunca aparece, porque `HasMessage` esconde justamente o texto
  "Conectando à API…".
- **Escolha da UBS manual e escondida.** Só existe em Pacientes, depois de
  clicar "Buscar UBS autorizadas". Não há carga no login nem seleção automática
  quando a conta tem uma única UBS. As outras abas só dizem "Confirme uma UBS em
  Pacientes". `UnitLabel` é texto fixo ("Dados da unidade conectada") na barra
  lateral, no cabeçalho e em Configurações, em vez do nome da UBS. Conta sem
  concessão vê lista vazia, sem explicação.
- **Depois de confirmar a UBS**, só o cadastro é carregado; fila, vagas e
  resumo do dia esperam o ciclo de 20 s. Se a chamada falha, a UBS ativa
  anterior é perdida.
- **Agenda.** Não carrega ao abrir a aba nem ao trocar de data; exige "Atualizar
  agenda". Os botões Chamar, Concluir, Falta e Cancelar ficam sempre
  habilitados, gerando 409 evitáveis. O calendário não marca os dias com
  consulta e a legenda "Livre" não corresponde a nenhum dado.
- **Fila.** A grade não mostra posição nem tempo de espera (calculáveis de
  `enteredAt`), embora a documentação os descreva. Sem ficha por duplo clique.
- **Atualização automática de 20 s** desabilita os formulários (`CanOperate`) e
  pode tirar o foco de quem digita; cliques durante o ciclo são ignorados em
  silêncio.
- **Campos devolvidos e não mostrados:** região e endereço da UBS no seletor,
  CPF na agenda, data de criação da vaga, perfil (`role`) da conta.
- **Validações locais ausentes:** formato de e-mail, telefone, agendamento no
  passado.

### Grupo 2 — falta rota

- **Indicadores e Alertas da UBS.** Tempo médio de espera, comparecimento,
  faltas e séries podem ser calculados das tabelas `appointments`,
  `waiting_list` e dos `*_events` da própria UBS; não há rota de resumo para o
  OBS.
- **Ofertas de uma vaga.** O OBS lista vagas, mas não quem recebeu, aceitou ou
  recusou cada oferta (`lightning_offers`).
- **Perfil da conta.** Alterar nome, e-mail e senha em Configurações está
  desabilitado: a API não tem rota para isso em nenhum escopo.

### Grupo 3 — falta dado

Consultórios ativos, estado "Livre" da agenda, cadastro estruturado de
profissionais e de especialidades (hoje texto livre), "chamar próximo" da fila.

## Secretaria (gestão)

### Grupo 1 — só front

- **Cadastro de referência por UBS** mostra só nome, CPF cru e e-mail; a rota
  devolve também telefone, endereço, situação, presença e data de cadastro, e o
  modelo do cliente já tem os campos formatados.
- **Agenda por UBS** com janela fixa de −30 a +30 dias; a API aceita qualquer
  intervalo até 366 dias. Sem filtro por situação e sem CPF na grade.
- **Total de pacientes** (`patients`) do resumo não aparece em nenhuma tela da
  Secretaria, só no CSV. O CSV identifica a UBS por GUID, não pelo nome.
- **Filtro de UBS** exige dois cliques ("Carregar" e "Aplicar") e aparece em
  seções onde não muda nada.
- **Usuários e histórico** não carrega as contas ao abrir; conceder acesso não
  pede confirmação; sem estados de vazio e de carregamento.
- **Sem indicador de carregamento**; 503 de uma UBS indisponível vira mensagem
  genérica repetida a cada 20 s, com o resumo antigo ainda na tela.
- A mesma atualização de 20 s desabilita o formulário de UBS e o de vínculos.

### Grupo 2 — falta rota

- **Histórico de ações.** `staff_unit_events` (concessões e revogações) e
  `unit_access_events` (consultas nominais da gestão) só são gravados.
- **Indicadores da rede.** Atendimentos, faltas, comparecimento, cancelamentos,
  espera média, quebra por UBS, região e especialidade e séries no tempo podem
  ser calculados dos bancos das UBS. Isso preenche Visão geral, Unidades,
  Especialidades, Indicadores e parte de Alertas (que ainda precisa de limites).
- **Contas OBS.** Criar atendente só pelo CLI `--provision`, que sempre cria
  `administrator`; não há criação nem desativação pela API.

### Grupo 3 — falta dado

Capacidade e ocupação (não há agenda ofertada nem profissionais), Metas,
Comunicação, confirmação clínica do reaproveitamento (a oferta não guarda a
consulta que gerou).

## APPNORA (paciente)

### Grupo 1 — só front

- **Sessão expirada fora do Início** deixa a tela sem formulário de login e sem
  navegação; a única saída é o botão voltar do sistema.
- **Vaga Relâmpago.** Sem contagem regressiva; a oferta vencida continua com
  Aceitar/Recusar até o usuário atualizar. Responder oferta vencida dá 409
  genérico e o cartão continua "pendente". Ofertas antigas aparecem antes das
  novas e o histórico inteiro é listado sem separação.
- **Sem atualização automática.** Ofertas só chegam ao abrir a tela ou tocar
  "Atualizar", embora o prazo seja de dez minutos.
- **UBS única não é selecionada sozinha**; Início e Avisos ficam vazios até o
  paciente escolher a UBS em outra tela.
- **Início** promete "seu próximo cuidado", mas não calcula a próxima consulta.
- **Rotação da tela** apaga o formulário de cadastro e o desafio de recuperação
  de senha, podendo queimar o limite de envios. Resposta inesperada do servidor
  na recuperação derruba o app (`JSONException` não tratada).
- **Mensagens de erro** iguais para qualquer 409, 403 e 404.
- Resultado do aceite (`accepted`, `appointmentId`) e fim da consulta (`endsAt`)
  são descartados.

### Grupo 2 — falta rota

Cancelar a própria entrada na fila, remover vínculo com uma UBS, trocar a senha
estando conectado.

### Grupo 3 — falta dado

Agendamento pelo paciente (não há catálogo de horários), confirmar presença,
Jornada (exames, vacinas, índice), Família (dependentes), entrar com Google,
notificações push, serviços e horários da UBS.

## Problemas do back vistos na leitura

- `waitingPatients` conta entradas da fila, não pacientes: quem aguarda duas
  especialidades conta duas vezes. `patients` inclui cadastros desativados.
- Cadastrar uma UBS na Secretaria não cria nem configura o banco dela. A partir
  daí o resumo da rede sem filtro, a lista de UBS do paciente vinculado e o
  cadastro público que a inclua respondem 503.
- Desativar um paciente na UBS cancela fila e ofertas, mas mantém as consultas
  agendadas, que ele não consegue mais cancelar pelo app (recebe 403).
- Alterar o e-mail de referência na UBS não altera o e-mail da conta; a
  recuperação de senha continua indo ao endereço antigo.
- Sessões revogadas ou vencidas e desafios usados nunca são apagados.
- O limite por IP não considera `X-Forwarded-For`; atrás de um proxy todos os
  usuários dividiriam o mesmo limite.
- `GET .../lightning-offers` executa redistribuição (escrita) a cada leitura.
- Atendente e administrador têm exatamente as mesmas permissões.

## Documentação desatualizada encontrada

- `api.md` diz que o cadastro de referência devolve só CPF, nome e e-mail;
  devolve também telefone, endereço, situação, presença e data.
- `api.md` lista os estados de vaga como open/offered/accepted; existe também
  `closed`.
- `obs-painel-operacional.md` descreve senha, posição e tempo de espera na Fila
  e botões "Chamar próximo" desabilitados; a tela atual não tem nenhum deles.

## Linha de base de validação (antes de qualquer alteração)

- Build Release da solução: 0 erros, 0 avisos.
- Testes HTTP da API: 26/26. Testes com PostgreSQL pulados (ver P-038).
- Suíte desktop: 43/66. As 23 falhas procuram controles e contratos do fluxo
  global legado.
