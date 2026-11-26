# Tarefa: dados fictícios para capturas de tela (2026-11-26)

## Skills e comandos invocados

Nenhuma skill nem comando do usuário. Tarefa recebida de um fluxo automatizado (harness) a partir de um pedido do usuário.

## O que foi pedido (palavras do usuário, 26/11/2026)

> "quero que você faça um script de insert nos meus dados, onde ainda vai estar mantendo todos os dados que já estão agora, só que vão estar dando inserts para ficar muito boas prints (…) dados fictícios, priorizando muito que fique com uma beleza extrema do software."

Contexto: ele vai tirar capturas de tela do OBS, do Painel Municipal e do app para slides. Os dados entram no banco de PRODUÇÃO (Neon). Nenhuma IA digita senha de produção; por isso o script funciona em QUALQUER banco NORA: uma IA o roda num banco local para tirar capturas e o usuário (que não programa) o roda no Neon.

Itens entregues:

1. `scripts/popular-dados-demonstracao.ps1` (PowerShell 7, estilo do `scripts/implantar-neon.ps1`: passos numerados, marcados ao terminar; conexão pedida com entrada oculta e nunca gravada; verificação de certificado igual à do `implantar-neon`; conexão local sem senha por parâmetro para testes) e arquivos SQL em `scripts/dados-demonstracao/`. Escolher, por tipo de dado, entre API e SQL; histórico no passado só por SQL, respeitando todas as tabelas e eventos que a API gravaria.
2. Regras inegociáveis: só acrescenta; pode rodar de novo sem duplicar; conta as linhas de todas as tabelas antes e depois e recusa o resultado se alguma perdeu linha; tudo identificável; modo `-Remover` (confirmação digitada, apaga SÓ o que o script criou); modo `-Simular`; nenhuma conta nova com senha conhecida/utilizável; e-mails em domínio reservado; CPFs válidos que não colidem; telefones fictícios; nenhum dado de pessoa real.
3. Conteúdo: na UBS existente (descobrir pelas UBS da rede; aceitar `-Ubs`): cerca de 48 pacientes; especialidades, dois profissionais por especialidade, consultórios e grade (catálogo de `cadastrar-medicos-simulacao.ps1`, sem duplicar); horário e serviços da UBS se ainda não informados; ~12 meses de consultas com sazonalidade semanal e leve crescimento (78% concluídas, 9% faltas, 13% canceladas, parte pelo paciente), desiguais entre especialidades; o dia de hoje com várias situações; próximos 14 dias agendados (algumas confirmadas); histórico de Vaga Relâmpago e 1 ou 2 em andamento; pedidos de Família pendentes e vínculos aprovados; exames e vacinas; comunicados; metas; o que mais as telas pedirem. Opcional `-UbsExtras N` (padrão 0).
4. A simulação na escola usa a UBS real: no mínimo 60% dos horários livres por especialidade em cada dia futuro; documentar.
5. Validar de verdade num PostgreSQL 18 descartável na porta 55471 (migrações, UBS e contas de teste via `--demo-bootstrap`, rodar o script, ligar a API, ler pelas rotas das telas, rodar de novo e conferir que nada duplicou, `-Remover` volta às contagens, suíte da API com `-Port 55472`). Relatar números reais.
6. `docs/dados-demonstracao.md`: para que serve, o que cria, passo a passo no Neon em linguagem simples, como remover, aviso de que os dados aparecem para todos e durante a simulação.

## Objetivo

Um script seguro, idempotente e reversível que enche uma instalação NORA com dados fictícios bonitos e críveis, sem tocar no que já existe, e a documentação para o usuário rodá-lo no Neon.

## Restrições

- Não alterar `Nora.Desktop`, `OBS-NORA`, `Painel-Municipal` nem `tests/Nora.Desktop.Checks` (outra equipe).
- Migrações só aditivas; sem senha, token ou conexão em arquivo versionado nem na saída.
- Commits na branch própria, em português, sem `Co-Authored-By`; sem push.
- Pendências novas em `docs/PENDENCIAS.md` a partir de P-081.
- Não rodar `tests/desktop-startup.ps1`; nunca bloquear/suspender/desligar o computador.

## Decisões de projeto (registradas durante o trabalho)

- Tudo por SQL, em uma transação por banco (REPEATABLE READ): a API exige contas com senha conhecida (o script não pode ter nenhuma), não cria nada com data no passado, depende do Render acordado e é limitada por taxa. As regras da API foram lidas nos DAOs e replicadas.
- Marca de identificação: todo id criado pelo script começa com `de0de0de-` (UUID determinístico a partir de uma semente, o que também torna a repetição idempotente); pacientes têm `created_by` = ator fictício fixo e e-mail `@email.example`.
- Contas de responsáveis (Família) são inativas e têm hash inválido: ninguém consegue entrar.
- Único UPDATE: preencher horário/serviços da UBS só se ainda estiverem vazios; `-Remover` desfaz só se o texto ainda for o do script.

## Checklist

- [x] Script PowerShell com passos, `-Simular`, `-Remover`, `-Local`, `-Ubs`, `-UbsExtras`, `-CpfApp` (`scripts/popular-dados-demonstracao.ps1` + `scripts/dados-demonstracao/pessoas.ps1`)
- [x] SQL central (`central-1.sql`, `central-2.sql`), SQL da UBS (`ubs.sql`, `ubs-agenda.sql`, `ubs-jornada.sql`) e de remoção (`remover-ubs.sql`, `remover-central.sql`)
- [x] Contagem antes/depois com recusa se alguma tabela perdeu linha (dentro de cada transação e, de novo, em todos os bancos pelo PowerShell); guarda testada com contagem inflada de propósito
- [x] Idempotência: segunda execução criou 0 linhas (com e sem UBS extras)
- [x] `-Remover` devolve exatamente as contagens iniciais (só `nora.sessions` e `nora.unit_access_events` mudaram, por causa das leituras da API)
- [x] No mínimo 60% de horários livres nos dias futuros (pior caso medido: 20,8% ocupados)
- [x] Validação num PostgreSQL 18.6 descartável (porta 55471) com a API real lendo as rotas das telas do OBS, do Painel Municipal e do app: 0 rotas com erro
- [x] Suíte da API (porta 55472, `-IncludeUnitDatabases`): 285 verificações, 0 falhas
- [x] `docs/dados-demonstracao.md`, `docs/PENDENCIAS.md` (P-081 e P-082), links no README, em `docs/hospedagem.md` e em `docs/simulacao-escola.md`
- [x] Commit na branch própria (sem push)

## Estado

Concluído e conferido. Números reais em `docs/dados-demonstracao.md`, seção "Validação". O que ficou para o usuário: rodar no Neon de verdade (P-081, nunca testado contra o Neon, só contra PostgreSQL 18 local); limites conhecidos em P-082 (ocupação da grade baixa, retrato do dia em que roda, índice central gravado depois dos cadastros).

Achados durante a validação, já corrigidos: (1) a primeira versão gravava o índice central de CPFs antes dos cadastros e uma execução interrompida deixava linhas órfãs que o `-Remover` não reconhecia; (2) o "agora" da demonstração podia ficar à frente do relógio real, e a consulta cancelada de hoje que ainda estava no futuro virava Vaga Relâmpago de verdade pela API (agora o "agora" nunca passa do relógio real); (3) a API mexendo numa vaga em andamento durante uma re-execução fazia o PostgreSQL recusar a transação (agora o script repete sozinho até 4 vezes); (4) a simulação com `-UbsExtras` falhava na chave estrangeira do índice.
