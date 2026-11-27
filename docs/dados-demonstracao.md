# Dados de demonstração para capturas de tela

O script `scripts\popular-dados-demonstracao.ps1` enche uma instalação do NORA com **dados fictícios e bonitos** para você tirar capturas de tela do OBS, do Painel Municipal e do aplicativo (slides, apresentação). Ele **só acrescenta**: tudo o que já existe no banco continua exatamente como está, e tudo o que ele cria pode ser apagado depois, com precisão, pelo próprio script.

Pedido do usuário em 26/11/2026; decisão dele: os dados entram no banco de produção (Neon). Nenhuma IA digita a senha da produção, por isso o script funciona em **qualquer** banco NORA: a IA o roda num banco local para tirar capturas, e você o roda no Neon.

## Leia antes de rodar no Neon

- **Os dados aparecem para todo mundo** que abrir o OBS, o Painel Municipal ou o app ligado a esse banco, inclusive os alunos e os funcionários da simulação na escola. Pacientes, médicos, consultas, comunicados e metas fictícios ficam misturados com os reais.
- **Não rode no dia da simulação** sem querer. Se rodar antes, apague com `-Remover` quando terminar as capturas (ou pelo menos antes do ensaio).
- **A agenda da simulação continua com folga.** Das consultas de hoje em diante, o script deixa **pelo menos 60% dos horários livres, por especialidade, em cada dia** (na prática, no máximo 35% ocupados, contando as consultas reais que já existirem e a grade de **todos** os profissionais da especialidade, não só os do catálogo). Se uma execução fosse passar disso, o banco desfaz tudo e nada é gravado. No fim, o script mostra a maior ocupação encontrada ("maior ocupação de uma especialidade num dia"). Dá para marcar normalmente pelo app e pelo OBS; as consultas reais marcadas **depois** ocupam o resto, como deve ser.
- **Comunicados:** se já houver celulares com o app instalado e vinculados à UBS, o comunicado fictício "Campanha de vacinação contra a gripe" pode chegar como **uma notificação** nesses aparelhos. Para evitar, rode com `-SemAvisoPaciente` (os comunicados ficam só para a equipe e o app dos pacientes não mostra nada novo).
- **Vagas Relâmpago "em andamento"** (2, de amanhã) valem 10 minutos (é a regra do sistema). Tire essa captura logo depois de rodar. Quando o prazo acaba, a API as trata como trata qualquer vaga: oferece a quem tem **conta no app**, vínculo com a UBS e consulta marcada depois, na mesma especialidade (por exemplo, o seu paciente de teste do `-CpfApp` ou um aluno da simulação, que recebe a oferta e a notificação de verdade). Se ninguém puder receber, a vaga fica aberta (segurando aquele horário de amanhã) até faltarem 10 minutos para ele e então fecha sozinha. Para não criar nenhuma, use `-SemVagaAgora` (recomendado se já houver alunos com o app instalado).
- O script **fotografa o dia em que roda** (o que já passou está concluído; as consultas de hoje e dos próximos dias estão agendadas). Rode **num dia útil, entre 9h e 16h** e logo antes das capturas: fora desse horário o "hoje" fica com menos consultas concluídas ou chamadas. Para renovar outro dia, use `-Remover` e rode de novo (o script avisa e não deixa rodar por cima de dados de outro dia).

## O que ele cria

Na UBS que já existe (em produção, "Jacyra Vieira Baracho"; o script descobre sozinho pela rede, ou você diz `-Ubs "parte do nome"`):

| O quê | Quanto | Observação |
| --- | --- | --- |
| Pacientes | 48 (3 desativados) | Nomes brasileiros variados, endereço, telefone e e-mail `@email.example`. CPFs com dígitos válidos que não existem em lugar nenhum da rede. Quem não tem conta no app aparece só na recepção. |
| Especialidades, médicos, consultórios e grade | 20 especialidades, 40 médicos, 20 consultórios, grade de segunda a sexta | Mesmo catálogo do `cadastrar-medicos-simulacao.ps1`. O que já existir (por nome) é **reaproveitado**, não duplicado; médico que já tem grade fica com a grade dele. |
| Consultas dos últimos 12 meses | cerca de 3.000, com 78% concluídas, 9% faltas e 13% canceladas (a maioria pelo paciente) | Mais movimento nas segundas, menos nas sextas, crescimento leve no ano e inverno mais cheio. Clínica Geral e Odontologia lideram; Oncologia quase não aparece. Feriados e as férias de alguns médicos criam "buracos" realistas. |
| Hoje | consultas concluídas, uma ou duas chamadas agora e as outras agendadas | Segue o relógio real (até as 15h40; depois disso o dia fica como estava às 15h40). Nunca cria consulta cancelada "no futuro de hoje", para a API não a transformar numa Vaga Relâmpago de verdade. |
| Próximos 14 dias | cerca de 100 consultas agendadas, parte com **presença confirmada** | Só nas próximas 48 horas dá para confirmar. |
| Vaga Relâmpago | cerca de 50 vagas no histórico (aceitas, recusadas, vencidas e fechadas) e 2 em andamento | Cada aceite antecipa a consulta de outra pessoa (a antiga fica cancelada e não pesa no score), como a API faz. |
| Score dos pacientes | de "Regular" a "Excelente" | Alguns pacientes pioram nos últimos meses, para a lista de pacientes não ficar toda igual. |
| Família | 3 pedidos pendentes, 3 vínculos aprovados e 1 recusado | Os responsáveis são **contas inativas** (ver "Segurança"). |
| Exames e vacinas | cerca de 80 exames e 360 vacinas | Realizados, solicitados, cancelados e alguns anulados, com data e histórico. |
| Comunicados da Secretaria | 6 (em vigor, vencidos e encerrados) | Assinados pela sua conta da Secretaria (a mais antiga ativa, ou `-ContaSecretaria`). |
| Metas | rede: comparecimento 85% e faltas 10%; a UBS: faltas 8% | Cada meta aparece como atingida, perto ou fora. A meta mais exigente da UBS gera um **alerta** nas telas de Alertas. |
| Atendimento ao público | horário da semana e texto de serviços | **Só se ainda estiverem em branco** (é a única alteração em dado existente, e só em campo vazio). |

Opcionais:

- `-UbsExtras N` (1 a 5; as capturas de 26/11 usaram 5): cria N **UBS fictícias** (Jardim Aurora, Vila Esperança, Parque Industrial, Santana e Bosque Oeste), cada uma com banco próprio, 6 especialidades, 12 médicos, 14 pacientes e 6 meses de histórico. Servem para as comparações do Painel Municipal (mapa, regiões, ranking, alertas). A Vila Esperança e a Parque Industrial têm mais faltas e geram alertas de propósito.
- `-CpfApp <cpf>`: o **seu paciente de teste do app** (um CPF que já está cadastrado na UBS) ganha cerca de 25 consultas no ano, duas marcadas à frente (uma com presença confirmada), exames e vacinas, para as telas do aplicativo não ficarem vazias. Só acrescenta registros a ele; o cadastro dele não é tocado.
- `-ConsultasPorDia <n>` (padrão 12): média de consultas por dia útil. Aumente se quiser gráficos com números maiores (mas, com 48 pacientes, cada um passa a ter mais consultas).
- `-Pacientes <n>` (padrão 48).

## Capturas para os slides (26/11/2026): o que é fictício e o que é real

As capturas da pasta `Mateus-Projects\Claude-HTML\Projeto-NORA\capturas-2026-11\` foram tiradas de um **banco local deste computador** (PostgreSQL 18 na porta 55490, API em `http://127.0.0.1:5190`; instruções em `%LOCALAPPDATA%\NORA\capturas-demo\LEIA-ME.txt`), **não** do Neon. Nenhum dado de pessoa real aparece nelas. Por pedido do usuário, nada nos programas indica que os dados são fictícios: a separação está só aqui.

| Item nas imagens | Real ou fictício |
| --- | --- |
| Nome e região da UBS principal, "Jacyra Vieira Baracho", São José dos Campos | **Real** (é a UBS cadastrada na produção). O endereço mostrado é fictício. |
| UBS Jardim Aurora, Vila Esperança, Parque Industrial, Santana e Bosque Oeste | **Fictícias** (`-UbsExtras 5`), criadas para o Painel Municipal ter rede para comparar. |
| Conta da Secretaria "Mariana Duarte", administrador do OBS "Ricardo Almeida" e paciente do app "Camila Rocha" | **Fictícias**: contas de demonstração do banco local (`*.demo@example.test`), renomeadas só lá. |
| Os 48 pacientes da UBS principal e os 14 de cada UBS extra, com CPF, e-mail e telefone | **Fictícios** (CPFs com dígitos válidos que não existem na rede; e-mails `@email.example`, que nunca entregam). |
| 20 especialidades, 40 médicos, consultórios e grade | **Fictícios** (o catálogo da simulação; médicos sem registro no conselho). |
| Consultas, faltas, cancelamentos, score, Vagas Relâmpago, exames, vacinas, pedidos de Família, comunicados e metas | **Fictícios**, gerados pelo script na noite de 26/11/2026. |
| Pedidos de vínculo de Família da Camila Rocha no app | **Fictícios**, criados pelo próprio app no emulador para a tela não ficar vazia. |
| Telas, textos, regras e cálculos (score, indicadores, alertas) | **Reais**: é o sistema como está na branch `claude/desktop-redesign-ui`. |

Para repetir as capturas noutro dia: `-Remover` e rodar o script de novo (ver acima), num dia útil entre 9h e 16h.

## Passo a passo no Neon (para quem não programa)

1. Abra o **PowerShell 7** (o ícone azul-escuro "PowerShell 7"; o PowerShell azul claro antigo não serve) na pasta do projeto:

   ```powershell
   cd C:\Users\mateu\OneDrive\Documentos\GitHub\Projeto-NORA
   git pull
   ```

   (Em outro computador, é a pasta onde o `Projeto-NORA` foi baixado.)

2. Pegue a **conexão direta** do Neon: no painel do Neon, **Connect**, desligue **Connection pooling** e copie a URI que começa com `postgresql://`. É a mesma que o `scripts\implantar-neon.ps1` pede.

3. Primeiro **ensaie sem gravar nada**:

   ```powershell
   pwsh -File scripts\popular-dados-demonstracao.ps1 -Simular
   ```

   Cole a URI quando o script pedir (nada aparece na tela; é normal) e tecle Enter. O script mostra os passos e, no fim, quantas linhas **criaria** em cada tabela. Nada foi gravado.

4. Se o ensaio terminou com **PRONTO**, rode de verdade (o mesmo comando, sem `-Simular`):

   ```powershell
   pwsh -File scripts\popular-dados-demonstracao.ps1
   ```

   Acrescente `-UbsExtras 3` para ter mais UBS no Painel Municipal, `-CpfApp 12345678909` (o CPF do seu paciente de teste) para o app, e `-SemAvisoPaciente` se houver celulares da simulação já instalados. Leva de 20 segundos a 2 minutos, porque o banco está nos Estados Unidos.

5. Quando aparecer **"Nenhuma tabela perdeu linhas"** e **PRONTO**, abra o OBS e o Painel Municipal (a mesma conta de sempre) e confira: Pacientes, Agenda, Indicadores, Alertas, Vaga Relâmpago, Família e Comunicados no OBS; Visão geral, Indicadores, Capacidade, Alertas e Comunicação no Painel.

6. **Tire as capturas** e, se não for mais usar, **apague**:

   ```powershell
   pwsh -File scripts\popular-dados-demonstracao.ps1 -Remover
   ```

   O script lista o que vai apagar e pede para você digitar `REMOVER DADOS DE DEMONSTRACAO`. Só o que ele criou sai; o que já existia fica.

**Se algo falhar:** o script termina com `FALHOU` e uma mensagem. Cada banco é gravado numa transação única: ou entra tudo daquele banco ou nada. Rodar de novo é seguro. Mensagens comuns: "Rode antes `scripts\implantar-neon.ps1`" (o banco precisa estar migrado) e "mais de uma UBS" (use `-Ubs`).

## Garantias de segurança

- **Só acrescenta.** O script não tem `UPDATE` nem `DELETE` sobre dado existente. A única exceção é preencher horário de atendimento e serviços da UBS **quando os dois estão vazios**; o `-Remover` só desfaz isso se o texto ainda for o do script.
- **Conta as linhas de todas as tabelas, em todos os bancos, antes e depois.** Dentro de cada transação o banco confere (no mesmo retrato dos dados) que nenhuma tabela perdeu linha e, se perdeu, desfaz tudo. Depois, o PowerShell repete a conta. Tabela que o script não escreve e que diminuiu por conta da própria API (por exemplo, sessões vencidas apagadas pela manutenção) vira um aviso, não um erro.
- **Repetir não duplica.** Todo registro nasce de um identificador calculado a partir de uma semente fixa. Rodar de novo no mesmo dia cria **0 linhas**. Se a agenda real mudou entre uma execução e outra (alguém marcou ou cancelou consultas), a repetição só acrescenta consulta fictícia no espaço que sobra dentro dos 35%, nunca põe um paciente fictício em dois lugares ao mesmo tempo e não cria Vaga Relâmpago a partir de consulta que já estava gravada (a vaga mudaria a situação de uma consulta existente, e o script não faz `UPDATE`).
- **Tudo é identificável:** todo id criado começa com `de0de0de-`; os pacientes fictícios têm o cadastro criado por um ator fixo e e-mail `@email.example` (domínio reservado, que nunca entrega mensagem); os telefones começam com `1290` (formato de celular de DDD 12 com um começo que não costuma ser usado).
- **Nenhuma conta com senha utilizável.** As únicas contas criadas são as dos responsáveis da Família: **inativas** e com um valor no lugar da senha que nenhuma senha produz. A API recusa o login antes mesmo de olhar a senha.
- **Sem dados reais.** Nomes sorteados de listas comuns; CPFs gerados com dígitos verificadores válidos e conferidos contra todos os CPFs da rede (contas, cadastros de todas as UBS e Família).
- **A conexão nunca é gravada nem mostrada.** A URI é pedida com entrada oculta, vive só em variáveis do processo (que voltam ao que eram no fim, mesmo se o script for chamado de dentro de uma janela já aberta), uma URI mal colada é recusada sem repetir o texto na tela, e o certificado do Neon é verificado por completo (`verify-full`), como no `implantar-neon.ps1`. Arquivos temporários só têm dados fictícios e são apagados.
- **Banco local:** `-Local -Porta N` usa um PostgreSQL em `127.0.0.1` sem senha, para testes. O script recusa qualquer outro endereço nesse modo.

## Como funciona (para quem for mexer)

Escolha por tipo de dado (API ou SQL): **tudo entra por SQL**. A API exige uma conta com senha conhecida (o script não pode ter nenhuma), não cria nada com data no passado (o histórico de 12 meses, as faltas, os cancelamentos e as ofertas já encerradas só existem por SQL), depende do Render acordado e é limitada por taxa. As regras da API foram lidas nos DAOs (`AppointmentDao`, `SchedulingDao`, `UnitLightningDao`, `UnitLightningDispatcher`, `FamilyDao`, `HealthRecordDao`, `AnnouncementDao`, `SlotEngine`, `AttendanceScore`) e replicadas: a grade vem do mesmo cálculo do `SlotEngine`, cada consulta grava os mesmos eventos de auditoria (agendada, chamada, concluída, falta, cancelada com a origem certa), o aceite de uma Vaga Relâmpago cancela a consulta antiga com origem `rescheduled-lightning` e cria a nova com `booked_by = 'lightning'`, e assim por diante.

Arquivos:

| Arquivo | Papel |
| --- | --- |
| `scripts/popular-dados-demonstracao.ps1` | Pede a conexão, reconhece a rede e a UBS, gera as pessoas, monta as entradas, roda os SQL, conta antes e depois, `-Simular` e `-Remover`. |
| `scripts/dados-demonstracao/pessoas.ps1` | Nomes, CPFs, endereços, catálogo de especialidades e UBS extras (tudo determinístico). |
| `scripts/dados-demonstracao/funcoes.sql` | Funções temporárias: número estável por semente, identificador `de0de0de-…`, contagem e conferência de linhas, o horário de atendimento fictício e a busca de ligações reais com as UBS fictícias. |
| `scripts/dados-demonstracao/central-1.sql` | Banco central: especialidades que faltam, UBS extras e índice central de CPFs. |
| `scripts/dados-demonstracao/ubs.sql`, `ubs-agenda.sql`, `ubs-jornada.sql` | Banco da UBS: cadastro, grade, bloqueios, consultas, Vaga Relâmpago, exames e vacinas. |
| `scripts/dados-demonstracao/central-2.sql` | Banco central: responsáveis, Família, comunicados, metas e atendimento ao público. |
| `scripts/dados-demonstracao/remover-ubs.sql`, `remover-central.sql` | O `-Remover`. |

Como o banco de cada UBS é encontrado: o nome é `nora_ubs_` + o id da UBS sem hífens, no mesmo servidor e com o mesmo usuário da conexão central (é como a API cria e abre esses bancos). Se a sua API usa uma conexão explícita por UBS, informe o banco com `-BancoUbs`. O script confere a identidade do banco (`nora_unit.identity`) e a versão do schema antes de escrever; se estiver desatualizado, manda rodar o `implantar-neon.ps1`.

Ordem de gravação (cada banco numa transação `REPEATABLE READ`): central (especialidades e UBS extras) → bancos das UBS extras (criados e migrados como a API faz) → UBS principal e extras → central (índice de CPFs a partir do que ficou gravado, Família, comunicados, metas, atendimento). O índice central de CPFs entra **depois** dos cadastros, ao contrário da API: só tem CPF fictício e assim uma execução interrompida nunca deixa índice sem cadastro.

O que o `-Remover` preserva: uma linha fictícia que ganhou ligação com algo **real** **não é apagada**; o script avisa o que ficou ("Mantido por ter ganho ligação com algo real"). Em detalhe:

- **Consulta, paciente, médico e consultório fictícios** ficam se algo real aponta para eles: uma consulta marcada de verdade com um médico fictício, uma vaga real criada pela API a partir de uma consulta fictícia, uma alteração da equipe no cadastro de um paciente fictício.
- **Vaga Relâmpago fictícia** fica (com as ofertas e as consultas a que aponta) se a API a ofereceu a um paciente real e ele **aceitou ou recusou**. As ofertas reais em que a pessoa não fez nada (vencidas, canceladas ou pendentes) saem com a vaga.
- **UBS fictícia extra** fica inteira, com o banco dela, se ganhou ligação real no banco central: acesso dado a alguém da equipe, paciente vinculado pelo app, comunicado real para ela, aparelho do OBS, alteração do atendimento ao público. O aviso diz a tabela e quantas linhas a seguram. Os **registros de leitura** dela (quem da equipe abriu o cadastro ou a agenda da UBS fictícia) saem junto, como as leituras dos comunicados fictícios: eles só registram que alguém olhou dados fictícios, e mantê-los impediria remover qualquer UBS fictícia que aparecesse numa captura.
- **Conta de responsável fictícia** fica se foi reativada ou se qualquer tabela passou a apontar para ela (a conferência olha toda chave estrangeira para `nora.accounts`).
- **Atendimento ao público** da UBS real só volta a "não informado" se o texto de serviços **e** o horário ainda forem exatamente os que o script gravou; se alguém editou um dos dois, fica tudo como está.
- O que a própria API gravou **sobre** os dados fictícios sai com eles: leituras dos comunicados fictícios, eventos de consulta, exame e vacina (por exemplo, a equipe chamou um paciente fictício no OBS) e ofertas sem resposta.

## Limites conhecidos

- **A ocupação da grade aparece baixa** (cerca de 2%). A grade da simulação oferece 480 horários por dia útil (40 médicos) e 48 pacientes não enchem isso sem números absurdos por paciente. `-ConsultasPorDia` ajuda, com o custo de mais consultas por paciente.
- **Não há fila de espera manual**: ela foi removida da API em 24/11/2026, então "Aguardando" e "Espera média" ficam vazios de verdade (não são falha do script).
- **O retrato envelhece:** as consultas "agendadas" de hoje e dos próximos dias não são atualizadas depois (o script não faz `UPDATE`). Para renovar, `-Remover` e rodar de novo.
- As UBS extras têm menos dados e ocupação menor.

## Validação (2026-11-26)

Feita num PostgreSQL 18.6 descartável (porta 55471, usuário `postgres`, sem senha), com as migrações da API (central e UBS, versão 19), a UBS "UBS Escola Centro" e as contas de teste do `--demo-bootstrap` (Secretaria, administrador do OBS e um paciente com conta), e a **API real** (`Nora.Api`, Release) ligada nesse banco, com o trabalhador de Vaga Relâmpago ligado.

| Verificação | Resultado |
| --- | --- |
| `-Simular -UbsExtras 3 -CpfApp …` | 11 s; mostrou as linhas por tabela; as contagens das 55 tabelas ficaram idênticas depois |
| Aplicar (`-UbsExtras 3 -CpfApp …`) | 12 s; **17.676 linhas** em 25 tabelas, 4 bancos de UBS (1 real + 3 extras); "nenhuma tabela perdeu linhas (55 tabelas)" |
| Segunda execução, mesmo comando | 34 s; **0 linhas criadas**; contagens das tabelas idênticas à primeira |
| `-Remover` | 5 s; devolveu **exatamente** as contagens de antes de tudo (única diferença: `nora.sessions` e `nora.unit_access_events`, que a própria API gravou quando as rotas foram lidas, fora do script); banco das 3 UBS extras apagado; horário e serviços da UBS voltaram a "não informado" |
| Remover quando não há nada | "Não há dados de demonstração. Nada a remover." |
| Confirmação digitada errada | "Nada foi apagado." (saída 1) |
| Guarda de perda de linhas | testada com uma contagem inflada de propósito: a transação aborta com "A tabela nora.accounts perdeu linhas… Nada foi gravado." |
| Re-execução com a API mexendo no mesmo dado | o PostgreSQL recusou (`could not serialize access`), nada foi gravado e o script repetiu sozinho e passou |

Linhas criadas na UBS principal e nas extras (execução de 07h00, 365 dias de histórico na principal e 180 nas extras):

| Tabela | Linhas | | Tabela | Linhas |
| --- | ---: | --- | --- | ---: |
| `nora_unit.appointments` | 4.041 | | `nora_unit.health_vaccines` | 661 |
| `nora_unit.appointment_events` | 10.904 | | `nora_unit.health_exams` | 156 |
| `nora_unit.lightning_slots` | 60 | | `nora_unit.health_record_events` | 951 |
| `nora_unit.lightning_offers` | 45 | | `nora_unit.patient_registry` | 90 |
| `nora_unit.professionals` | 76 | | `nora_unit.schedule_templates` | 290 |
| `nora_unit.rooms` | 38 | | `nora_unit.schedule_blocks` | 32 |
| `nora.unit_registry_index` | 90 | | `nora.announcements` | 6 |
| `nora.specialties` | 19 (a 20ª já existia) | | `nora.family_links` | 7 |
| `nora.accounts` | 6 | | `nora.goals` | 3 |
| `nora.health_units` | 3 | | `nora.unit_opening_hours` | 11 |

Na UBS principal (3.082 consultas): 77,5% concluídas, 8,5% faltas e 14,0% canceladas (255 pelo paciente) nas já encerradas; 111 agendadas; 113 nos próximos 14 dias, 18 com presença confirmada. No pior caso, uma especialidade num dia futuro tem **20,8% dos horários ocupados** (média 6%), ou seja, no mínimo 79% livres (a promessa é 60%).

Leitura pelas rotas que as telas usam (todas HTTP 200, 0 rotas com erro), com as contas de teste:

- **OBS:** pacientes com o score (49 cadastros, 46 ativos: 41 "Excelente", 4 "Bom" e 4 "Regular", contando o paciente de teste), consultas de hoje e dos próximos dias, indicadores de 365 e 30 dias (2.302 concluídas, 253 faltas, 414 canceladas; comparecimento 87,5%; meta de comparecimento "atingida", meta de faltas "fora"), capacidade (ocupação 2,2%: 2.555 de 118.680 horários), alertas (1: meta de faltas fora), Vaga Relâmpago (52 vagas, 2 em andamento com a oferta pendente e o nome do paciente), Família (3 pendentes e 3 aprovados), comunicados da equipe (3), profissionais, consultórios, grade, bloqueios, consultórios ativos, atendimento ao público, disponibilidade (234 horários livres de Clínica Geral nos próximos 14 dias) e Jornada (exames e vacinas de um paciente).
- **Painel Municipal:** resumo (85 pacientes, 6 desativados), indicadores da rede com as 4 UBS e as 4 regiões, metas por UBS (atingida, perto e fora), alertas da rede (7, de 3 UBS), unidades, especialidades, metas, comunicados (6), histórico, profissionais e grade de uma UBS, cadastro de referência e contas.
- **App do paciente** (o paciente de teste que recebeu `-CpfApp`): UBS vinculada, 18 consultas (12 concluídas, 1 falta, 3 canceladas e 2 agendadas, uma com presença confirmada), score de 1.000 pontos, 9 vacinas e 1 exame, comunicado e dados da UBS.
- As contas de responsáveis fictícias não entram (o login recusa com 401).
- O trabalhador de Vaga Relâmpago da API, ligado durante o teste, venceu as duas ofertas em andamento no prazo e deixou as vagas abertas, sem nenhum erro no log.

Suíte da API (`tests/validar-postgres-descartavel.ps1 -Port 55472 -IncludeUnitDatabases`): todos os grupos passaram (285 verificações, 0 falhas), nenhuma base de uso acessada.

## Revisão crítica (2026-11-26)

Uma segunda IA revisou o script e corrigiu oito defeitos (registro em `docs/tarefas/2026-11-26-revisao-dados-demonstracao.md`):

1. Uma URI mal colada aparecia inteira, **com a senha**, na mensagem de erro (a conversão `[Uri]` repete o texto). Agora a URI é lida com `TryCreate` e o erro não repete nada.
2. As variáveis `PGHOST`, `PGUSER`, `PGSSLMODE` e outras ficavam no ambiente depois do script; agora todas voltam ao que eram.
3. Repetir depois de a agenda real mudar podia acrescentar consultas fictícias além do teto de 35% e pôr um paciente fictício em dois lugares ao mesmo tempo. Agora o teto conta as consultas fictícias já gravadas e a grade de todos os profissionais da especialidade, e a consulta nova não pode cair em cima de outra do mesmo paciente.
4. Na mesma situação, a repetição criava uma **Vaga Relâmpago "ofertada" para uma consulta que continuava marcada** (vaga e consulta ocupando o mesmo horário). Agora a vaga só nasce entre consultas que a própria execução grava.
5. A folga de 60% passou a ser conferida no fim, dentro da transação: se a execução acrescentasse consulta num dia e especialidade acima de 40% ocupados, nada seria gravado.
6. O `-Remover` falhava (erro de chave estrangeira, nada removido) quando alguém tinha aberto o cadastro de uma UBS fictícia no Painel, dado acesso a ela ou vinculado um paciente. Agora ele apaga os registros de leitura dela e mantém, com aviso, só a UBS fictícia que ganhou ligação real.
7. O `-Remover` apagava uma Vaga Relâmpago fictícia que um paciente real tinha aceitado ou recusado (e a oferta dele). Agora ela fica.
8. O `-Remover` desfazia o horário de atendimento mesmo se a equipe o tivesse editado (só comparava o texto de serviços) e podia falhar ao apagar uma conta de responsável que alguma tabela passou a referenciar. Agora compara o horário inteiro e confere todas as chaves estrangeiras.

Validação da revisão, num PostgreSQL 18 descartável (porta 55476), com a UBS e as contas do `--demo-bootstrap`:

| Verificação | Resultado |
| --- | --- |
| `-Simular -UbsExtras 3 -CpfApp …` | 6 s; contagens das 55 tabelas idênticas depois |
| Aplicar (`-UbsExtras 3 -CpfApp …`) | 18 s; 18.108 linhas em 5 bancos; maior ocupação futura 29,2% na UBS real |
| Repetir o mesmo comando | 37 s; **0 linhas**; contagens idênticas |
| 24 consultas reais acrescentadas nos 3 próximos dias úteis e repetir | 0 linhas novas; nenhuma vaga ativa com a consulta ainda marcada (antes da correção 4: 1 vaga e 1 oferta indevidas) |
| `-Remover` com acesso de equipe dado a uma UBS fictícia | essa UBS ficou, com aviso ("nora.staff_units: 1"); as outras duas saíram e os bancos delas foram apagados |
| Tirar o acesso, registrar uma leitura da UBS fictícia e `-Remover` de novo | saiu tudo; contagens idênticas às de antes de tudo (55 tabelas, 2 bancos) |
| Oferta real recusada numa vaga fictícia e `-Remover` | a vaga, 2 consultas, 1 paciente, 2 médicos, 2 consultórios e 2 especialidades ficaram, com aviso; a vaga com oferta real só vencida saiu |
| API real (Release) lendo 36 rotas do OBS, do Painel e do app | 36 com HTTP 200, 0 erros no log da API; 27 consultas no app do paciente de teste; responsável fictício recusado com 401 |
| Repetir depois da API e `-Remover` | 0 linhas; voltou às contagens de antes, com diferença só em `nora.sessions` (3 → 12) e `nora.unit_access_events` (0 → 3), gravadas pela API nas leituras |


### Registros criados pelos vídeos (26/11/2026, só no banco local de capturas)

A gravação dos vídeos cadastrou de verdade, no banco local (porta 55490), a UBS fictícia **UBS Vila Nove de Julho** (endereço real usado só para a busca de localização achar um ponto único) e as pacientes fictícias **Larissa Monteiro Duarte** e **Renata Siqueira Lopes** (CPF gerado com dígitos válidos, e-mail `@email.example`). Nada disso foi para o Neon.
