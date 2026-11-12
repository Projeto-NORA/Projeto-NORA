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
| Perfis do OBS | Só o administrador da UBS mantém profissionais, especialidades, consultórios e grade de horários; o atendente opera pacientes, fila, agenda e vagas. |

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
- [ ] Fase 1: legado removido dos clientes.
- [ ] Fase 1: OBS.
- [ ] Fase 1: Secretaria.
- [ ] Fase 1: APPNORA.
- [ ] Fase 1: testes reescritos, validação e commit.
- [ ] Fase 2: rotas novas na API, com testes.
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
- Em andamento: Fase 1 desktop (legado + OBS) no agente.
- Falta: validar a Fase 1 desktop, Secretaria, Fases 2 e 3 e o fechamento.
