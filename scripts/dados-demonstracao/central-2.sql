-- Banco CENTRAL, parte 2 (depois do banco da UBS): contas de responsáveis (inativas e sem senha), pedidos e vínculos de Família, comunicados da
-- Secretaria, metas e o atendimento ao público da UBS (só se ainda estiver vazio).
-- Tabelas temporárias de entrada:
--   _c(now_ts, unit_id, gerente, decisor, regiao, aviso_paciente, meta_ubs, servicos)   gerente = conta da Secretaria que "assina"; decisor = conta que decidiu os pedidos
--   _g(cpf, nome, email, phone)                                                          responsáveis fictícios
--   _fl(resp_cpf, dep_cpf, status, motivo, dias)                                         pedidos e vínculos de Família
CREATE TEMP TABLE _antes AS SELECT * FROM pg_temp.contar();

\echo E|Responsáveis, Família e comunicados
-- Responsáveis: contas de paciente INATIVAS e com um hash que nenhuma senha produz (a API nunca deixa entrar; o login recusa antes mesmo de olhar o hash).
INSERT INTO nora.accounts(id, full_name, cpf, email, password_hash, scope, role, created_at, is_active, phone)
SELECT pg_temp.did('resp:' || g.cpf), g.nome, g.cpf, g.email, '!conta-ficticia-sem-senha', 'patient', 'patient', c.now_ts - interval '200 days', false, g.phone
FROM _g g CROSS JOIN _c c ON CONFLICT DO NOTHING;

-- Família: o pedido nasce pendente; aprovação e recusa carregam quem decidiu e quando (as restrições da tabela exigem os dois).
INSERT INTO nora.family_links(id, guardian_id, dependent_cpf, unit_id, status, requested_at, decided_by, decided_at, decision_reason)
SELECT pg_temp.did('fam:' || f.resp_cpf || ':' || f.dep_cpf), pg_temp.did('resp:' || f.resp_cpf), f.dep_cpf, c.unit_id, f.status,
       c.now_ts - f.dias * interval '1 day' - interval '3 hours',
       CASE WHEN f.status IN ('approved', 'rejected') THEN c.decisor END,
       CASE WHEN f.status IN ('approved', 'rejected') THEN c.now_ts - f.dias * interval '1 day' + interval '20 hours' END,
       CASE WHEN f.status = 'rejected' THEN f.motivo END
FROM _fl f CROSS JOIN _c c ON CONFLICT DO NOTHING;

-- Comunicados da Secretaria (a ordem de seq é a de commit: todo publicador toma este lock antes de gravar, como a API).
SELECT pg_advisory_xact_lock(62419830);
CREATE TEMP TABLE _ann AS
SELECT n, titulo, corpo, publico, alcance, atras, validade, encerrado
FROM (VALUES
    (1, 'Campanha de vacinação contra a gripe', 'A campanha segue até o fim do mês em todas as UBS. Traga a caderneta de vacinação e um documento com foto. Têm prioridade idosos, gestantes, puérperas, trabalhadores da saúde e crianças de 6 meses a menores de 6 anos.', 'both', 'all', interval '6 days', interval '25 days', NULL::interval),
    (2, 'Nova rotina da Vaga Relâmpago', E'Quando uma consulta é cancelada, o horário vira uma Vaga Relâmpago automaticamente e é oferecido a quem tem consulta marcada depois, na mesma especialidade. A pessoa tem 10 minutos para responder. A equipe não precisa aprovar nem montar fila.', 'staff', 'all', interval '3 days', NULL, NULL),
    (3, 'Conferência dos horários da grade', 'Confira em Informações da UBS se os horários dos profissionais estão corretos para a próxima semana. Ajustes feitos até quinta-feira entram na agenda pública no mesmo dia.', 'staff', 'units', interval '1 day', interval '12 days', NULL),
    (4, 'Horário especial no feriado de 15 de novembro', 'As UBS ficaram fechadas no feriado da Proclamação da República. O atendimento voltou ao horário normal no dia seguinte.', 'both', 'all', interval '25 days', interval '-11 days', NULL),
    (5, 'Atualização do cadastro dos pacientes', 'Pedimos que a equipe confira telefone e endereço dos pacientes no atendimento. Cadastros completos agilizam o contato quando uma vaga aparece.', 'staff', 'region', interval '45 days', NULL, interval '20 days'),
    (6, 'Campanha de multivacinação', 'Crianças e adolescentes com a caderneta incompleta podem atualizar as vacinas sem agendamento, de segunda a sexta, no horário de atendimento da UBS.', 'patients', 'region', interval '70 days', interval '-40 days', NULL)
) v(n, titulo, corpo, publico, alcance, atras, validade, encerrado);

INSERT INTO nora.announcements(id, title, body, audience, scope, created_by, created_at, expires_at, closed_at, closed_by)
SELECT pg_temp.did('ann:' || a.n || ':' || c.unit_id), a.titulo, a.corpo,
       CASE WHEN NOT c.aviso_paciente AND a.publico = 'both' THEN 'staff' WHEN NOT c.aviso_paciente AND a.publico = 'patients' THEN 'staff' ELSE a.publico END,
       a.alcance, c.gerente, c.now_ts - a.atras, c.now_ts + a.validade,
       CASE WHEN a.encerrado IS NOT NULL THEN c.now_ts - a.atras + a.encerrado END, CASE WHEN a.encerrado IS NOT NULL THEN c.gerente END
FROM _ann a CROSS JOIN _c c
ORDER BY a.atras DESC
ON CONFLICT DO NOTHING;

INSERT INTO nora.announcement_regions(announcement_id, region_key, region_label)
SELECT pg_temp.did('ann:' || a.n || ':' || c.unit_id), lower(btrim(c.regiao)), btrim(c.regiao) FROM _ann a CROSS JOIN _c c WHERE a.alcance = 'region' ON CONFLICT DO NOTHING;
INSERT INTO nora.announcement_units(announcement_id, unit_id)
SELECT pg_temp.did('ann:' || a.n || ':' || c.unit_id), c.unit_id FROM _ann a CROSS JOIN _c c WHERE a.alcance = 'units' ON CONFLICT DO NOTHING;

-- Quem leu: só os responsáveis fictícios (pacientes) e só os comunicados que eles podem ver; a equipe real fica sem leitura marcada.
INSERT INTO nora.announcement_reads(announcement_id, account_id, read_at)
SELECT pg_temp.did('ann:' || a.n || ':' || c.unit_id), pg_temp.did('resp:' || g.cpf), c.now_ts - a.atras + (2 + 70 * pg_temp.u('rd:' || a.n || ':' || g.cpf)) * interval '1 hour'
FROM _ann a CROSS JOIN _c c CROSS JOIN _g g
WHERE a.publico IN ('both', 'patients') AND c.aviso_paciente AND pg_temp.u('rd0:' || a.n || ':' || g.cpf) < 0.65 ON CONFLICT DO NOTHING;

\echo E|Metas e atendimento ao público
-- Metas: da rede (só se a Secretaria ainda não definiu) e, se pedido, uma mais exigente só para esta UBS (a UBS fica "fora da meta" em faltas: gera um alerta).
INSERT INTO nora.goals(id, metric, unit_id, target)
SELECT pg_temp.did('goal:' || m.metric || ':rede'), m.metric, NULL, m.alvo FROM (VALUES ('attendance_score', 0.85), ('missed_rate', 0.10)) m(metric, alvo) ON CONFLICT DO NOTHING;
INSERT INTO nora.goals(id, metric, unit_id, target)
SELECT pg_temp.did('goal:missed_rate:' || c.unit_id), 'missed_rate', c.unit_id, 0.08 FROM _c c WHERE c.meta_ubs ON CONFLICT DO NOTHING;

-- Atendimento ao público da UBS: só preenche se NADA foi informado ainda (é o único UPDATE do script, e só em campos vazios).
CREATE TEMP TABLE _preencher AS
SELECT u.id FROM nora.health_units u JOIN _c c ON c.unit_id = u.id
WHERE u.profile_updated_at IS NULL AND u.services_text IS NULL AND NOT EXISTS (SELECT 1 FROM nora.unit_opening_hours h WHERE h.unit_id = u.id);
INSERT INTO nora.unit_opening_hours(unit_id, weekday, opens_at, closes_at)
SELECT p.id, h.d, h.a::time, h.f::time FROM _preencher p
CROSS JOIN (VALUES (1, '07:00', '12:00'), (1, '13:00', '17:00'), (2, '07:00', '12:00'), (2, '13:00', '17:00'), (3, '07:00', '12:00'), (3, '13:00', '17:00'),
                   (4, '07:00', '12:00'), (4, '13:00', '17:00'), (5, '07:00', '12:00'), (5, '13:00', '16:00'), (6, '08:00', '12:00')) h(d, a, f) ON CONFLICT DO NOTHING;
UPDATE nora.health_units u SET services_text = c.servicos, profile_updated_at = c.now_ts
FROM _c c WHERE u.id IN (SELECT id FROM _preencher) AND u.id = c.unit_id AND u.profile_updated_at IS NULL AND u.services_text IS NULL;

SELECT pg_temp.conferir();
SELECT 'T|' || d.t || '|' || COALESCE(a.n, 0) || '|' || d.n FROM _depois d LEFT JOIN _antes a ON a.t = d.t WHERE d.n <> COALESCE(a.n, 0) ORDER BY d.t;
SELECT 'R|perfil|preenchido|' || count(*) FROM _preencher;
