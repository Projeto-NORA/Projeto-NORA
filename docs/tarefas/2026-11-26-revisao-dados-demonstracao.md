# Tarefa: revisão crítica do script de dados de demonstração (2026-11-26)

## Skills e comandos invocados

Nenhum. Tarefa recebida de um fluxo automatizado; a mensagem do usuário repassada foi "pode escrever você mesmo a função, continue."

## O que foi pedido

Revisão crítica, com correção, do trabalho da branch `worktree-wf_5640a03a-b07-3` (commit `22a1cc7`): `scripts/popular-dados-demonstracao.ps1`,
`scripts/dados-demonstracao/*` e `docs/dados-demonstracao.md`, contra o pedido original do usuário (resumido em
`docs/tarefas/2026-11-26-dados-demonstracao.md`).

1. Trazer o trabalho com `git merge --ff-only worktree-wf_5640a03a-b07-3`.
2. Ler o diff inteiro e os documentos do assunto.
3. Conferir cada exigência do pedido contra o código (feita, pela metade, faltando).
4. Procurar defeitos reais: segurança (segredo em log, SQL concatenado, dado real), perda ou alteração de dado existente, concorrência,
   casos extremos, regressão, teste afrouxado.
5. Rodar as validações nas portas 55476 (banco), 55477 (suíte da API) e 55466 (API), comparar com o relato.
6. Corrigir a causa de cada achado e repetir até uma volta sem achados; commit na branch própria, sem push.

## Objetivo

O script cumprir todas as regras inegociáveis (só acrescenta, repetível, contagem antes/depois, identificável, `-Remover` preciso,
`-Simular`, nenhuma conta utilizável, nenhum dado real, 60% de folga na agenda futura) com evidência real.

## Restrições

- Não tocar em `Nora.Desktop`, `OBS-NORA`, `Painel-Municipal` nem `tests/Nora.Desktop.Checks`.
- Sem push; commit sem linha de co-autor de IA; pendências novas a partir de P-081 (P-081 e P-082 já usadas pelo autor; usei P-083 a P-085).
- Não rodar `tests/desktop-startup.ps1`; parar o banco e os processos que eu iniciar.

## Conferência do pedido (item a item)

| Exigência | Situação no trabalho do autor | Depois da revisão |
| --- | --- | --- |
| Script PS7 no estilo do `implantar-neon` (passos, URI oculta, `verify-full`, `-Local`) | feito; a URI mal colada aparecia com a senha no erro | corrigido |
| Escolha API x SQL, regras dos DAOs replicadas | feito (tudo SQL, justificado) | conferido contra `UnitLightningDispatcher`, `LightningEligibility`, `UnitDatabaseProvisioner`, `UnitMigrationDao` |
| Só acrescenta | feito (única exceção documentada: atendimento ao público vazio) | mantido |
| Repetível sem duplicar | feito com a agenda parada; com a agenda real mudando entre execuções, podia passar do teto, sobrepor paciente e criar vaga para consulta ainda marcada | corrigido |
| Contagem antes/depois com recusa | feito | mantido; somada a conferência da folga de 60% dentro da transação |
| Identificável e `-Remover` só do que criou | feito; falhava com UBS fictícia que alguém abriu no Painel, apagava vaga fictícia com decisão real, desfazia horário editado pela equipe | corrigido |
| `-Simular` | feito | conferido (0 diferenças nas contagens) |
| Sem conta utilizável, e-mail reservado, CPF válido sem colisão, sem dado real | feito | conferido (login do responsável fictício: 401) |
| Conteúdo (48 pacientes, 12 meses, hoje, 14 dias, Vaga Relâmpago, Família, exames, vacinas, comunicados, metas, extras) | feito | conferido pelas rotas da API |
| Pelo menos 60% livres por especialidade e dia futuro | feito só com a grade do catálogo | agora conta todos os profissionais da especialidade e é conferido no fim |
| Validação real e números | feito pelo autor (porta 55471) | refeita (portas 55476, 55477, 55466) |
| `docs/dados-demonstracao.md` | feito | atualizado com as correções, avisos e a validação da revisão |

## Achados e correções

1. **Senha na tela.** `[Uri]$texto` repete a URI inteira na mensagem de erro quando ela é inválida. Troca por `[Uri]::TryCreate`.
2. **Ambiente sujo.** `PGHOST`, `PGPORT`, `PGUSER`, `PGSSLMODE` e `PGCHANNELBINDING` ficavam definidos depois do script. Agora todas as variáveis `PG*` voltam ao valor anterior.
3. **Teto de 35% e sobreposição numa repetição com a agenda real mudada.** A escolha ignorava as consultas fictícias já gravadas. Agora o teto conta todas as fictícias do dia e a grade de todos os profissionais da especialidade (`_capf`), só consulta nova entra no espaço que sobra, e consulta nova não pode cair em cima de outra fictícia do mesmo paciente.
4. **Vaga Relâmpago fantasma.** Na mesma situação, o par da vaga "em andamento" mudava e o script gravava uma vaga ofertada para uma consulta que continuava marcada (reproduzido: 1 vaga e 1 oferta indevidas). Agora as vagas (histórico e em andamento) só nascem entre consultas que a própria execução grava.
5. **Folga de 60% conferida.** No fim da transação da UBS, se a execução acrescentou consulta num dia e especialidade acima de 40% ocupados, tudo é desfeito; o resumo mostra a maior ocupação.
6. **`-Remover` travado por UBS fictícia "tocada".** Leitura do cadastro no Painel (`unit_access_events`), acesso dado, paciente vinculado etc. faziam o `DELETE` da UBS falhar por chave estrangeira. Agora os registros de leitura saem com ela e a UBS fictícia com ligação real fica inteira, com aviso da tabela que a segura (`pg_temp.ligacoes_reais`).
7. **Vaga fictícia com decisão real.** O `-Remover` apagava a vaga e a oferta real aceita ou recusada. Agora ela fica (com as consultas a que aponta); ofertas reais sem resposta saem.
8. **Atendimento ao público e contas.** O `-Remover` desfazia o horário só comparando o texto de serviços; agora compara também o horário inteiro (`pg_temp.horario_demo`). A conta de responsável só sai se nenhuma tabela com chave estrangeira para `nora.accounts` aponta para ela e se continua inativa.
9. Menor: o resumo final diz de qual UBS é cada número.

## Checklist

- [x] Fast-forward da branch do autor (22a1cc7)
- [x] Leitura do diff e dos documentos
- [x] Achados e correções (itens 1 a 9)
- [x] Validação real (aplicar, repetir, remover, 60% livre, API, suíte da API)
- [x] Documentação e pendências atualizadas (`dados-demonstracao.md`, P-083, P-084, P-085)
- [x] Commit

## Estado

Concluído. Resultados reais (PostgreSQL 18 descartável na porta 55476, com o `--demo-bootstrap` depois de `--migrate`):

- `-Simular -UbsExtras 3 -CpfApp`: 6 s, contagens das 55 tabelas idênticas.
- Aplicar com 3 extras: 18 s, 18.108 linhas; maior ocupação futura 29,2% (UBS real), 16,7% e 8,3% nas extras.
- Repetir: 37 s, 0 linhas, contagens idênticas. Com 24 consultas reais acrescentadas e repetir: 0 linhas, nenhuma vaga ativa com consulta marcada.
- `-Remover` com acesso de equipe numa UBS fictícia: ela fica com aviso; sem o acesso e com uma leitura registrada: sai tudo, contagens idênticas às iniciais.
- Oferta real recusada numa vaga fictícia: a vaga e o que ela segura ficam, com aviso; depois de tirar a oferta, `-Remover` volta às contagens iniciais.
- API real (porta 55466): 36 rotas do OBS, do Painel e do app com HTTP 200, 0 erros no log; responsável fictício recusado com 401. Repetir e remover depois: só `nora.sessions` e `nora.unit_access_events` diferentes (gravadas pela API).
- Confirmação errada: "Nada foi apagado.", saída 1.
- Suíte da API (`tests/validar-postgres-descartavel.ps1 -Port 55477 -IncludeUnitDatabases`): 285 PASS, 0 FAIL, saída 0.
- Banco e API encerrados; nenhuma das portas 55466, 55476 e 55477 ficou ocupada.

Fica para o usuário: P-081 (rodar no Neon), P-085 (decidir se o `-Remover` pode apagar acesso e histórico ligados só a UBS fictícias).
