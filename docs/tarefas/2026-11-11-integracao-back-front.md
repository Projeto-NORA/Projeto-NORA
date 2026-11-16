# Integração back-end x front-end — análise e conclusão

Data: 2026-11-11
Branch de trabalho: `claude/back-front-integration-analysis-3133b1` (worktree)

## Skills e comandos invocados

Nenhum comando ou skill foi invocado pelo usuário neste pedido. Durante a
execução foi usada a skill `merge-preservando-alteracoes` para trazer a
`origin/main` ao branch de trabalho.

## O que foi pedido, item por item

1. Continuar fazendo alterações no projeto deste chat (Projeto-NORA).
2. Considerar que o último commit já trouxe algumas integrações do back com o
   front, mas não todas.
3. Fazer uma análise do projeto e de como ele está atualmente.
4. Ver quais implementações do back faltam ser acrescentadas no front.
5. Caso algo do front esteja em falta para completar um detalhe do back, ou
   vice-versa, indicar em questionamentos para que possa ser realizado.
6. Completar todo o projeto.
7. Propor, ao final, melhorias no back e no front (principalmente no front).
8. Qualquer dúvida sobre regra de negócio, consistência etc. deve ser enviada
   ao usuário para que ele indique a ação.
9. Para acessar o back e ver o que falta, usar a organização
   `https://github.com/Projeto-NORA`.
10. Qualquer dúvida, mesmo pequena, deve virar pergunta durante a requisição.

## Objetivo

Front-end do NORA consumindo tudo o que o back-end oferece, sem tela ou fluxo
dependente de dado simulado onde já existe endpoint, com as lacunas dos dois
lados registradas e decididas pelo usuário, e uma lista final de melhorias
propostas (principalmente de front).

## Restrições

- Não presumir regra de negócio: dúvida vira pergunta ao usuário.
- O back-end é consultado na organização `Projeto-NORA` do GitHub. A
  organização tem um único repositório (`Projeto-NORA`, privado); o back é a
  pasta `Nora.Api` deste mesmo repositório.
- Commit local por fase validada, no branch de trabalho. Push na `main` só no
  final de tudo (autorizado pelo usuário em 2026-11-11).

## Decisões do usuário em 2026-11-11

Procurado antes de perguntar: não há outro chat deste projeto nos últimos dois
dias; documentação, commits e pendências não decidiam estes pontos.

| Assunto | Decisão |
| --- | --- |
| Alterar a `Nora.Api` | Sim, onde o dado já existe; para as telas novas abaixo, tabelas novas estão autorizadas, com as regras perguntadas antes. |
| Fluxo global legado | Remover dos clientes (desktop e Android) e reescrever os testes para o fluxo por UBS. As rotas antigas continuam na API, bloqueadas. |
| Telas sem dado | Definir nesta entrega, depois das integrações e dos indicadores: agendamento pelo paciente; capacidade e profissionais; metas e comunicação; família e jornada. Regras perguntadas tela por tela. |
| Fórmula do score | `concluídas ÷ (concluídas + faltas + soma dos pesos dos cancelamentos)`. Período pela data marcada da consulta. Consultas ainda agendadas ou chamadas não entram. |
| Peso do cancelamento (fração de uma falta, pela antecedência) | menos de 1 h = 100%; 1 h a 2 h = 90%; 2 h a 6 h = 80%; 6 h a 12 h = 60%; 12 h a 24 h = 40%; 1 a 2 dias = 20%; 2 a 3 dias = 10%; 3 a 4 dias = 2%; 4 dias ou mais = 0%. |
| Quais cancelamentos pesam | Só os feitos pelo próprio paciente no app. Cancelamento registrado pela equipe no OBS não pesa. |
| Onde o score aparece | Indicador agregado da UBS e da rede; por paciente na ficha do OBS; por paciente no app (Jornada). |
| Espera média | Tempo entre entrar na fila e sair dela por oferta aceita. |
| Ofertas no app | Consulta automática a cada 30 s pelo serviço de conexão e notificação do Android quando chega oferta nova, também em segundo plano. |
| Ações de conta novas | Trocar senha conectado (três apps); paciente sai da fila; paciente remove vínculo com UBS; Secretaria cria e desativa contas de atendente/administrador. |
| Contagens do resumo | "Pacientes aguardando" conta pessoas (CPF único) e surge "Solicitações na fila"; "Pacientes cadastrados" conta ativos e surge "Desativados". |
| UBS sem banco | A API cria o banco e roda as migrações ao cadastrar a UBS (o usuário PostgreSQL da API precisa poder criar bancos). |
| Testes com banco | Instalar o PostgreSQL 18 nesta máquina e conferir tudo com ele. |
| Commits | Commit local por fase; push na `main` no final de tudo. |
| Função de peso | O usuário pediu que eu escrevesse `CancellationWeight`. Cada faixa inclui o limite inferior (2 h cravadas = 80%); antecedência negativa pesa como falta. |
| Janela do score por paciente | Últimos 12 meses. |
| Senha de conta OBS criada pela Secretaria | Provisória, com troca obrigatória no primeiro login. |
| Remover vínculo com UBS | As consultas futuras são canceladas junto e pesam no score pela tabela normal; o app avisa quantas serão canceladas antes de confirmar. |
| Perfis do OBS | Só o administrador da UBS mantém profissionais, consultórios e grade de horários; o atendente opera pacientes, fila, agenda e vagas. |
| Especialidades (Fase 3) | Lista única da rede, mantida pela Secretaria; cada UBS escolhe dela. |
| Grade de horários (Fase 3) | Grade semanal por profissional (dias, início, fim, duração do atendimento, consultório) mais bloqueios por data (folgas e feriados). |
| Agendamento pela equipe (Fase 3) | Depois que a UBS tiver profissionais cadastrados, profissional e especialidade só do cadastro e só dentro da grade. Consultas antigas mantêm o texto que tinham. |
| Consultórios ativos (Fase 3) | Dois números: consultórios em atendimento agora (consulta chamada) e consultórios com grade prevista para hoje. |
| Agendamento pelo paciente (Fase 3) | Horários livres da grade, de 24 h até 90 dias à frente; uma consulta futura por especialidade marcada pelo app; vale na hora, sem aprovação, e aparece no OBS como marcada pelo paciente (a equipe pode cancelar). Decidido em 2026-11-13. |
| Confirmação de presença (Fase 3) | O paciente confirma pelo app de 48 h até 2 h antes. Sem confirmação nada é cancelado: o OBS só mostra "confirmada" ou "sem confirmação" para a equipe decidir. Decidido em 2026-11-13. |
| Metas (Fase 3) | Secretaria define metas de score de comparecimento (mínimo), taxa de faltas (máximo) e espera média (máximo) para a rede, com sobrescrita opcional por UBS. Estados: atingida; perto (até 10% da meta); fora. Decidido em 2026-11-13 e 2026-11-15. |
| Comunicação (Fase 3) | Secretaria envia comunicados para equipes (OBS das UBS escolhidas: todas, por região ou específicas, com confirmação de leitura por conta) e para pacientes vinculados dessas UBS (aba Avisos do app, com notificação pela conexão ativa, desligável em Configurações). Decidido em 2026-11-13 e 2026-11-15. |
| Família (Fase 3) | Responsável pede pelo app o vínculo com um dependente (CPF); a equipe da UBS aprova no OBS após conferir documentos. Aprovado, o responsável faz pelo dependente tudo que o próprio paciente faz (consultas, agendamento, presença, fila, ofertas, Jornada), com registro do responsável em cada ação. O dependente não precisa de conta própria; a UBS decide a idade (menor ou adulto sob cuidado); se o dependente tiver conta, os dois veem. Decidido em 2026-11-13 e 2026-11-15. |
| Jornada (Fase 3) | A equipe registra no OBS exames (solicitados/realizados) e vacinas aplicadas: nome, data, UBS e observação curta, sem anexo. O paciente só visualiza no app. Decidido em 2026-11-13. |
| Limite do agendamento (Fase 3) | "Uma consulta futura por especialidade marcada pelo app" vale na rede inteira, não por UBS. Decidido em 2026-11-15. |
| Alertas (Fase 3) | Limites sugeridos aceitos: fila alta com 20 ou mais pessoas numa especialidade (por UBS); alguém aguardando há mais de 30 dias; faltas acima de 20% nos últimos 30 dias; ocupação acima de 95% da grade nos próximos 7 dias; qualquer meta "fora". Decidido em 2026-11-15. |
| Profissional (Fase 3) | Guarda registro de conselho opcional (sigla, número, UF), sem validação externa. Decidido em 2026-11-15. |
| Exames e vacinas (Fase 3) | Exame: solicitado → realizado (com data) ou cancelado. Vacina: aplicada, com dose (1ª, 2ª, 3ª, reforço ou única). Decidido em 2026-11-15. |
| Desativação na UBS e consultas | Desativar o paciente numa UBS cancela também as consultas futuras daquela UBS, como cancelamento da unidade (não pesa no score); o horário pode virar Vaga Relâmpago; a confirmação no OBS avisa quantas serão canceladas. Decidido em 2026-11-16. |
| E-mail de referência x conta | Quando a UBS corrige o e-mail de referência, o app avisa o paciente e oferece trocar o e-mail da conta, com código enviado ao endereço novo; só troca depois do código confirmado. Decidido em 2026-11-16. |

Decisões técnicas minhas, sem regra de negócio envolvida: conta OBS com uma
única UBS concedida entra direto nela; com várias, a pessoa escolhe a cada
login (a unidade só muda por ação explícita, como já documentado).

## Plano por fases

- **Fase 1 — só front.** Remover o legado dos clientes; corrigir e completar
  OBS, Secretaria e APPNORA com o que a API já entrega (P-023 a P-028, P-031,
  P-032); reescrever os testes desktop.
- **Fase 2 — rotas sobre dado existente.** Histórico de ações, indicadores com
  o score, score por paciente, contagens do resumo, criação automática do
  banco da UBS, ações de conta; ligar tudo nos três apps (P-029, P-030, P-033,
  P-034, P-035).
- **Fase 3 — telas novas.** Agendamento pelo paciente; capacidade e
  profissionais; metas e comunicação; família e jornada (P-037). Regras
  perguntadas antes de cada uma.
- **Fechamento.** Documentação, pendências, proposta de melhorias, merge e
  push na `main`.

## Checklist

- [x] Ler a documentação existente (`README.md`, `docs/`, `docs/PENDENCIAS.md`).
- [x] Trazer o commit `83ebcd4` da `origin/main` para o branch de trabalho
      (merge `7c21607`, sem conflitos).
- [x] Mapear o back-end (rotas, contratos, tabelas sem leitura).
- [x] Mapear o que cada front integra e o que é placeholder.
- [x] Cruzar back x front e registrar em `docs/integracao-back-front.md` e em
      `docs/PENDENCIAS.md` (P-023 a P-039).
- [x] Enviar ao usuário as perguntas de regra de negócio e registrar as
      respostas.
- [x] Instalar PostgreSQL 18 nesta máquina (18.6, binários oficiais em
      `%LOCALAPPDATA%\Programs\PostgreSQL\18`, sem serviço; linha de base com
      ele: 28/28 fluxos de banco e bancos separados por UBS).
- [x] Fase 1: legado removido dos clientes.
- [x] Fase 1: OBS.
- [ ] Fase 1: Secretaria.
- [x] Fase 1: APPNORA.
- [ ] Fase 1: testes reescritos, validação e commit.
- [x] Fase 2: rotas novas na API, com testes.
- [ ] Fase 2: rotas novas ligadas nos três apps, validação e commit.
- [ ] Fase 3: regras perguntadas e telas novas implementadas, validação e
      commit.
- [ ] Documentação e pendências atualizadas.
- [ ] Melhorias de back e de front propostas.
- [ ] Merge e push na `main`.

Validações previstas a cada fase: build Release da solução (0 erros, 0 avisos),
testes HTTP da API, testes PostgreSQL 18 descartável
(`tests/validar-postgres-descartavel.ps1 -IncludeUnitDatabases`), suíte desktop
(`Nora.Desktop.Checks`), E2E desktop com banco
(`tests/validar-clientes-descartavel.ps1`), abertura dos executáveis
(`tests/desktop-startup.ps1`), e no Android `assembleDebug`,
`testDebugUnitTest`, `lintDebug` e `assembleDebugAndroidTest`. Testes
instrumentados em emulador não rodam nesta máquina (sem aceleração).

## Estado

### Retomada de 2026-11-13 e 2026-11-15 (sessões caíram por falta de bateria)

- Commits desde a última atualização: `2171794` (Android Fase 2: score na Jornada, sair da fila, remover UBS,
  trocar senha; 84/84 testes locais, 28 previews), `dbe3eb8` (Secretaria: indicadores da rede, histórico,
  contas OBS, base da UBS, troca de senha comum; desktop 94/94), `49a22d1` (OBS: indicadores da UBS, espera
  média e série no Painel, participação na ficha, ofertas da vaga, critérios de alerta, E2E por UBS; desktop
  103/103 e E2E com PostgreSQL 18 104/104), merge `3d46a78` (15 achados da revisão crítica do back corrigidos
  em 14 commits `fix`: pool por UBS, revalidação de sessão nos executores, versão do schema da UBS e 503
  `unit-database-outdated`, limites por conta, ForwardedHeaders só com proxy confiável, cursor do histórico,
  REVOKE CONNECT, índice da espera média; revisão 17/17).
- Grafo do graphify reconstruído só com código (commit `dbe3eb8d`: 3518 nós, 8885 arestas); `graph.html` em
  `Mateus-Projects/Claude-HTML/Projeto-NORA/graphify/graph.html`.
- Em andamento: dois agentes da Fase 3 do back em worktrees isolados, com números de migração reservados
  (A: central 015 e UBS 012, agenda/capacidade/metas/alertas; B: central 016 e UBS 013, comunicação e
  jornada). Depois deles: família no back e as telas da Fase 3 nos três apps.
- Pendências desta etapa a documentar no fechamento: `obs-painel-operacional.md`, `secretaria.md`,
  `app-paciente.md`, `arquitetura.md` (ainda diz que não há criação automática de bancos), `PENDENCIAS.md`
  (fechar P-023 a P-036 conforme entregue) e README.

- 2026-11-11: documento criado; documentação lida; merge da `origin/main`
  feito; quatro levantamentos concluídos e conferidos por amostragem no código;
  análise e pendências registradas; decisões do usuário registradas acima.
- Linha de base antes de qualquer alteração: build Release 0 erros/0 avisos;
  API HTTP 26/26 (banco pulado por falta do PostgreSQL 18); suíte desktop
  43/66, com as 23 falhas ligadas ao fluxo legado.
- PostgreSQL 18.6 instalado e linha de base de banco registrada (28/28).
- Em andamento: Fase 1 em dois agentes paralelos, um no desktop (legado + OBS,
  `Nora.Desktop/**` e `tests/Nora.Desktop.Checks/**`) e um no Android
  (`APPNORA/**`). A Secretaria (Fase 1) vem depois do agente desktop, porque
  divide arquivos com ele.
- Preparado para a Fase 2: `Nora.Api/Services/AttendanceScore.cs` com a fórmula
  do score; a função `CancellationWeight` está com `TODO(human)` aguardando a
  implementação do usuário (tabela de pesos da seção de decisões).
- Desenho da Fase 2 (rotas): indicadores por UBS e da rede com o score;
  score por paciente no cadastro de referência e no app; histórico de ações;
  contagens novas no resumo; criação automática do banco da UBS; troca de
  senha; paciente sai da fila e remove vínculo; Secretaria cria e desativa
  conta OBS; ofertas de uma vaga para o OBS. Para saber quem cancelou, a
  tabela `appointment_events` da UBS ganha a coluna `source` (hoje só guarda o
  autor), em migração aditiva.
- Fase 1 Android concluída pelo agente e conferida por mim: legado removido;
  sessão expirada volta ao Início; ofertas com contagem regressiva, "Prazo
  encerrado", histórico recolhido e recarga após resposta; consulta automática
  de ofertas no laço de 30 s do serviço de conexão com notificação local
  (canal "Vaga Relâmpago", Intent só com o id da UBS, imutável); UBS única
  selecionada sozinha; próxima consulta real no Início; estado de cadastro e
  recuperação preservado na rotação (senhas nunca salvas); mensagens por
  contexto. Arquivos novos: `api/ApiMessages.kt`, `api/PatientLogic.kt`,
  `api/LightningNotifier.kt`, `ui/OffersUi.kt`, `ui/AppointmentsUi.kt`,
  `ui/QueueUi.kt`. Resultado: `assembleDebug`, `assembleDebugAndroidTest` e
  `validateDebugScreenshotTest` (21 previews) verdes; testes locais 48/48;
  lint 0 erros e 6 avisos, todos anteriores (versões disponíveis e
  `UnitsMap.kt`). Testes instrumentados só compilam: não foram executados
  (sem emulador acelerado). Dependência nova só de teste: `org.json:json`.
- Pendente do Android fora de `APPNORA/`: `tests/validar-clientes-descartavel.ps1
  -Android` ainda sobe a fixture legada; o teste real do app agora exige
  `NORA_TEST_REGISTRATION=1` (P-040).
- Fase 1 desktop (legado + OBS) concluída pelo agente e conferida por mim
  (commit `7ba3c54`): legado removido; faixa única de mensagens e barra de
  progresso no shell; UBS carregada no login (única entra direto, várias exigem
  escolha, nenhuma explica a concessão); agenda por mês com marcações e botões
  por estado; fila com posição por especialidade e tempo de espera; atualização
  automática sem desabilitar formulários; cliente HTTP robusto. Suíte desktop
  71/71 (antes 43/66), build 0/0, abertura dos dois executáveis 2/2, capturas
  conferidas.
- Fase 2 do back concluída pelo agente em worktree isolado e integrada (merge
  `6896ba3`): `source` nos eventos de consulta, indicadores por UBS e da rede,
  score por paciente, histórico, criação automática do banco da UBS, contagens
  novas do resumo, troca de senha e troca obrigatória, contas OBS pela
  Secretaria, paciente sai da fila e remove vínculo, ofertas de uma vaga.
  Contratos em `docs/api.md`. Depois da integração, rodado por mim: build 0/0;
  API HTTP 30/30; PostgreSQL 18.6 descartável com 28/28 fluxos, bancos
  separados por UBS e 19/19 de plataforma (rotas novas); desktop 71/71.
- Commits locais até aqui: `30f2fcf` (Android Fase 1 + análise), `7ba3c54`
  (desktop Fase 1), `425de83` e `250bdfa` (back Fase 2), merge `6896ba3`.
- Em andamento, três agentes: revisão crítica do back (somente leitura);
  Secretaria (Fase 1 + rotas novas + troca de senha comum aos dois perfis) em
  `Nora.Desktop/**`; Android Fase 2 (score na Jornada, sair da fila, remover
  UBS, trocar senha) em `APPNORA/**`.
- Depois deles: OBS Fase 2 (Indicadores, tempo médio no Painel, score na ficha,
  ofertas de uma vaga), E2E desktop com banco (o script ainda não define
  `NORA_E2E_UNIT_REGISTRY=1`), correções que a revisão apontar, Fase 3.
- Pontos que o agente do back deixou para confirmar: `daily.scheduled` conta as
  consultas ainda agendadas, não o total marcado no dia; UBS com base pendente
  continua aparecendo para o OBS e a Secretaria (operar nela responde 503);
  `scripts/iniciar-local.ps1` ainda não roda `--migrate-all-units`.
- Falta: concluir a Fase 2 nos clientes, Fase 3 e o fechamento.
