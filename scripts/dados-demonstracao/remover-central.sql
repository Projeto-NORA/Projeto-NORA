-- Remove do banco CENTRAL o que o script de demonstração criou (e só isso). Roda numa transação REPEATABLE READ depois de os bancos das UBS terem sido limpos.
-- Entradas temporárias: _rm_idx(cpf, unit_id) = CPFs fictícios que de fato saíram dos cadastros; _rm_spec(id) = especialidades fictícias que nenhuma UBS usa mais;
-- _rm_units(id) = UBS fictícias extras (o banco delas é apagado depois, pelo PowerShell); _c(unit_id, servicos) = a UBS real onde o atendimento foi preenchido.
\echo E|Contando as linhas antes
CREATE TEMP TABLE _antes AS SELECT * FROM pg_temp.contar();

\echo E|Comunicados, leituras e Família
DELETE FROM nora.announcement_reads WHERE pg_temp.is_demo(announcement_id);
DELETE FROM nora.announcement_regions WHERE pg_temp.is_demo(announcement_id);
DELETE FROM nora.announcement_units WHERE pg_temp.is_demo(announcement_id);
DELETE FROM nora.announcements WHERE pg_temp.is_demo(id);
DELETE FROM nora.family_links WHERE pg_temp.is_demo(id);
DELETE FROM nora.goals WHERE pg_temp.is_demo(id);

\echo E|Contas de responsáveis (inativas)
-- Só se nenhuma coisa real tiver passado a apontar para a conta (não deveria: ela nunca entra).
DELETE FROM nora.accounts a WHERE pg_temp.is_demo(a.id) AND a.scope = 'patient' AND a.email LIKE '%@email.example'
  AND NOT EXISTS (SELECT 1 FROM nora.family_links l WHERE l.guardian_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM nora.sessions s WHERE s.account_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM nora.announcement_reads r WHERE r.account_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM nora.patient_units pu WHERE pu.patient_id = a.id);

\echo E|Índice central de CPFs e atendimento ao público
DELETE FROM nora.unit_registry_index i USING _rm_idx r WHERE i.cpf = r.cpf AND i.unit_id = r.unit_id;
-- Desfaz o preenchimento do atendimento ao público SÓ se o texto ainda for o que o script gravou (quem editou depois fica com o que editou).
CREATE TEMP TABLE _desfazer AS SELECT u.id FROM nora.health_units u JOIN _c c ON c.unit_id = u.id WHERE u.services_text = c.servicos;
DELETE FROM nora.unit_opening_hours WHERE unit_id IN (SELECT id FROM _desfazer);
UPDATE nora.health_units SET services_text = NULL, profile_updated_at = NULL WHERE id IN (SELECT id FROM _desfazer);

\echo E|UBS fictícias e especialidades
DELETE FROM nora.unit_database_events WHERE unit_id IN (SELECT id FROM _rm_units);
DELETE FROM nora.unit_registry_index WHERE unit_id IN (SELECT id FROM _rm_units);
DELETE FROM nora.health_units WHERE id IN (SELECT id FROM _rm_units) AND pg_temp.is_demo(id);
DELETE FROM nora.specialties WHERE id IN (SELECT id FROM _rm_spec) AND pg_temp.is_demo(id);

SELECT 'K|comunicados|' || count(*) FROM nora.announcements WHERE pg_temp.is_demo(id) HAVING count(*) > 0;
SELECT 'K|especialidades|' || count(*) FROM nora.specialties WHERE pg_temp.is_demo(id) HAVING count(*) > 0;
SELECT 'K|contas|' || count(*) FROM nora.accounts WHERE pg_temp.is_demo(id) HAVING count(*) > 0;

SELECT pg_temp.conferir(ARRAY['nora.announcement_reads', 'nora.announcement_regions', 'nora.announcement_units', 'nora.announcements', 'nora.family_links', 'nora.goals', 'nora.accounts',
    'nora.unit_registry_index', 'nora.unit_opening_hours', 'nora.unit_database_events', 'nora.health_units', 'nora.specialties']);
SELECT 'T|' || a.t || '|' || a.n || '|' || COALESCE(d.n, 0) FROM _antes a LEFT JOIN _depois d ON d.t = a.t WHERE COALESCE(d.n, 0) <> a.n ORDER BY a.t;
