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
-- Só se nenhuma coisa real tiver passado a apontar para a conta (não deveria: ela nunca entra). A conferência olha TODA tabela com chave estrangeira
-- para nora.accounts (sessões, vínculos com UBS, histórico de contas, Família de verdade, foto, aviso no celular...), inclusive as com ON DELETE: apagar
-- a conta mexeria nessas linhas. Conta que alguém reativou também fica.
CREATE TEMP TABLE _rm_acc AS
SELECT a.id FROM nora.accounts a WHERE pg_temp.is_demo(a.id) AND a.scope = 'patient' AND a.email LIKE '%@email.example' AND NOT a.is_active;
DO $$
DECLARE r record;
BEGIN
    FOR r IN SELECT c.conrelid::regclass::text AS t, a.attname::text AS col
             FROM pg_constraint c JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
             WHERE c.contype = 'f' AND c.confrelid = 'nora.accounts'::regclass AND array_length(c.conkey, 1) = 1 LOOP
        EXECUTE format('DELETE FROM _rm_acc x WHERE EXISTS (SELECT 1 FROM %s y WHERE y.%I = x.id)', r.t, r.col);
    END LOOP;
END
$$;
DELETE FROM nora.accounts WHERE id IN (SELECT id FROM _rm_acc);

\echo E|Índice central de CPFs e atendimento ao público
DELETE FROM nora.unit_registry_index i USING _rm_idx r WHERE i.cpf = r.cpf AND i.unit_id = r.unit_id;
-- Desfaz o preenchimento do atendimento ao público SÓ se o texto E o horário ainda forem exatamente os que o script gravou (quem editou depois um ou
-- outro fica com tudo como está).
CREATE TEMP TABLE _desfazer AS
SELECT u.id FROM nora.health_units u JOIN _c c ON c.unit_id = u.id
WHERE u.services_text = c.servicos
  AND NOT EXISTS (SELECT h.weekday, h.opens_at, h.closes_at FROM nora.unit_opening_hours h WHERE h.unit_id = u.id EXCEPT SELECT * FROM pg_temp.horario_demo())
  AND NOT EXISTS (SELECT * FROM pg_temp.horario_demo() EXCEPT SELECT h.weekday, h.opens_at, h.closes_at FROM nora.unit_opening_hours h WHERE h.unit_id = u.id);
DELETE FROM nora.unit_opening_hours WHERE unit_id IN (SELECT id FROM _desfazer);
UPDATE nora.health_units SET services_text = NULL, profile_updated_at = NULL WHERE id IN (SELECT id FROM _desfazer);

\echo E|UBS fictícias e especialidades
-- _rm_units só tem UBS fictícias SEM ligação real (o PowerShell tirou as outras com pg_temp.ligacoes_reais). Da UBS fictícia saem também os registros de
-- leitura (quem da equipe abriu o cadastro ou a agenda dela): eles só registram que alguém olhou dados fictícios, como as leituras dos comunicados fictícios.
DELETE FROM nora.unit_access_events WHERE unit_id IN (SELECT id FROM _rm_units);
DELETE FROM nora.unit_database_events WHERE unit_id IN (SELECT id FROM _rm_units);
DELETE FROM nora.unit_registry_index WHERE unit_id IN (SELECT id FROM _rm_units);
DELETE FROM nora.health_units WHERE id IN (SELECT id FROM _rm_units) AND pg_temp.is_demo(id);
DELETE FROM nora.specialties WHERE id IN (SELECT id FROM _rm_spec) AND pg_temp.is_demo(id);

SELECT 'K|comunicados|' || count(*) FROM nora.announcements WHERE pg_temp.is_demo(id) HAVING count(*) > 0;
SELECT 'K|especialidades|' || count(*) FROM nora.specialties WHERE pg_temp.is_demo(id) HAVING count(*) > 0;
SELECT 'K|contas|' || count(*) FROM nora.accounts WHERE pg_temp.is_demo(id) HAVING count(*) > 0;

SELECT pg_temp.conferir(ARRAY['nora.announcement_reads', 'nora.announcement_regions', 'nora.announcement_units', 'nora.announcements', 'nora.family_links', 'nora.goals', 'nora.accounts',
    'nora.unit_registry_index', 'nora.unit_opening_hours', 'nora.unit_database_events', 'nora.unit_access_events', 'nora.health_units', 'nora.specialties']);
SELECT 'T|' || a.t || '|' || a.n || '|' || COALESCE(d.n, 0) FROM _antes a LEFT JOIN _depois d ON d.t = a.t WHERE COALESCE(d.n, 0) <> a.n ORDER BY a.t;
