-- Dados fictícios de UMA UBS (banco nora_ubs_<id>). Executado por scripts/popular-dados-demonstracao.ps1 dentro de uma transação
-- REPEATABLE READ, depois de o PowerShell criar as tabelas temporárias de entrada:
--   _p    uma linha de parâmetros (UBS, relógio, volume, fuso)
--   _pat  pacientes fictícios (e, com -CpfApp, pacientes que já existem e recebem histórico)
--   _cat  catálogo de especialidades, médicos e consultórios
-- Só acrescenta: todo INSERT é ON CONFLICT DO NOTHING e todo id nasce de pg_temp.did(semente), então repetir não duplica nada.
-- Cada regra aqui replica o que os DAOs da API gravariam (migrações em Nora.Api/Database/UnitMigrations).

DO $$
BEGIN
    IF (SELECT unit_id FROM nora_unit.identity WHERE singleton) IS DISTINCT FROM (SELECT unit_id FROM _p) THEN
        RAISE EXCEPTION 'Este banco não é o da UBS esperada (identidade diferente).';
    END IF;
END
$$;
CREATE TEMP TABLE _antes AS SELECT * FROM pg_temp.contar();

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 1. Cadastro de referência dos pacientes (o que o OBS grava em "Pacientes")
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Cadastro dos pacientes
INSERT INTO nora_unit.patient_registry(cpf, full_name, email, created_by, created_at, phone, cep, street, address_number, complement, neighborhood, city, state, ibge, is_active)
SELECT cpf, full_name, email, pg_temp.demo_actor(), created_ts, phone, cep, street, num, NULLIF(complement, ''), neighborhood, city, state, ibge, deact_ts IS NULL
FROM _pat WHERE NOT existing ON CONFLICT (cpf) DO NOTHING;

-- Defesa: um CPF fictício nunca pode ter caído num cadastro que já era de outra pessoa.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM _pat p JOIN nora_unit.patient_registry r ON r.cpf = p.cpf WHERE NOT p.existing AND r.created_by <> pg_temp.demo_actor()) THEN
        RAISE EXCEPTION 'Um CPF fictício coincide com um cadastro real. Nada foi gravado.';
    END IF;
END
$$;

INSERT INTO nora_unit.patient_registry_events(cpf, actor_id, action, occurred_at)
SELECT p.cpf, pg_temp.demo_actor(), 'created', p.created_ts FROM _pat p
WHERE NOT p.existing AND NOT EXISTS (SELECT 1 FROM nora_unit.patient_registry_events e WHERE e.cpf = p.cpf AND e.action = 'created');
INSERT INTO nora_unit.patient_registry_events(cpf, actor_id, action, occurred_at)
SELECT p.cpf, pg_temp.demo_actor(), 'deactivated', p.deact_ts FROM _pat p
WHERE NOT p.existing AND p.deact_ts IS NOT NULL AND NOT EXISTS (SELECT 1 FROM nora_unit.patient_registry_events e WHERE e.cpf = p.cpf AND e.action = 'deactivated');

-- Pacientes com a data real de cadastro e de desativação (a repetição em outro dia não muda quem era elegível a quê).
CREATE TEMP TABLE _patx AS
SELECT p.*,
       -- Paciente real que recebe histórico (-CpfApp) é tratado como cadastrado há mais de um ano: a linha do cadastro dele não é tocada.
       CASE WHEN p.app THEN LEAST(r.created_at, pr.now_ts - interval '400 days') ELSE r.created_at END AS reg_at,
       (SELECT min(e.occurred_at) FROM nora_unit.patient_registry_events e WHERE e.cpf = p.cpf AND e.action = 'deactivated') AS deact_at
FROM _pat p CROSS JOIN _p pr JOIN nora_unit.patient_registry r ON r.cpf = p.cpf WHERE r.is_active OR NOT p.existing;

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 2. Consultórios, profissionais e grade (a lógica do scripts/cadastrar-medicos-simulacao.ps1; nada que já existe é mudado)
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Consultórios, profissionais e grade
INSERT INTO nora_unit.rooms(id, name)
SELECT pg_temp.did('room:' || p.unit_id || ':' || lower(btrim(c.room_name))), c.room_name
FROM _cat c CROSS JOIN _p p
WHERE NOT EXISTS (SELECT 1 FROM nora_unit.rooms r WHERE lower(btrim(r.name)) = lower(btrim(c.room_name)))
ON CONFLICT DO NOTHING;

CREATE TEMP TABLE _doc AS
SELECT c.spec_key, c.spec_id, c.spec_name, c.room_name, c.weight, c.ages, c.sex, v.name AS doc_name, v.idx
FROM _cat c CROSS JOIN LATERAL (VALUES (c.doc1, 1), (c.doc2, 2)) v(name, idx);

INSERT INTO nora_unit.professionals(id, full_name)
SELECT pg_temp.did('prof:' || p.unit_id || ':' || lower(btrim(d.doc_name))), d.doc_name
FROM _doc d CROSS JOIN _p p
WHERE NOT EXISTS (SELECT 1 FROM nora_unit.professionals x WHERE lower(btrim(x.full_name)) = lower(btrim(d.doc_name)))
ON CONFLICT DO NOTHING;

-- O vínculo com a especialidade só é criado para profissionais que o próprio script criou; os que já existiam ficam como estão.
INSERT INTO nora_unit.professional_specialties(professional_id, specialty_id)
SELECT x.id, d.spec_id FROM _doc d JOIN nora_unit.professionals x ON lower(btrim(x.full_name)) = lower(btrim(d.doc_name))
WHERE pg_temp.is_demo(x.id) ON CONFLICT DO NOTHING;

-- Profissionais ativos, com a especialidade vinculada (os únicos que a grade e a marcação enxergam).
CREATE TEMP TABLE _prof AS
SELECT d.*, x.id AS prof_id
FROM _doc d JOIN nora_unit.professionals x ON lower(btrim(x.full_name)) = lower(btrim(d.doc_name)) AND x.is_active
WHERE EXISTS (SELECT 1 FROM nora_unit.professional_specialties s WHERE s.professional_id = x.id AND s.specialty_id = d.spec_id);

-- Grade padrão (segunda a sexta; 1º médico de manhã, 2º à tarde, 20 minutos) só para quem não tem grade nenhuma: grade existente nunca é tocada.
INSERT INTO nora_unit.schedule_templates(id, professional_id, weekday, start_time, end_time, slot_minutes, room_id, specialty_id)
SELECT pg_temp.did('tpl:' || f.prof_id || ':' || wd), f.prof_id, wd, CASE f.idx WHEN 1 THEN time '08:00' ELSE time '13:00' END, CASE f.idx WHEN 1 THEN time '12:00' ELSE time '17:00' END, 20, r.id, f.spec_id
FROM _prof f
CROSS JOIN _p p
CROSS JOIN generate_series(1, 5) wd
JOIN nora_unit.rooms r ON lower(btrim(r.name)) = lower(btrim(f.room_name)) AND r.is_active
-- Grade enxuta (UBS fictícias extras): cada médico atende só alguns dias, para a ocupação não ficar irrisória.
WHERE (NOT p.grade_enxuta OR (f.idx = 1 AND wd IN (1, 3, 5)) OR (f.idx = 2 AND wd IN (2, 4)))
  AND NOT EXISTS (SELECT 1 FROM nora_unit.schedule_templates t WHERE t.professional_id = f.prof_id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.schedule_templates t WHERE t.room_id = r.id AND t.weekday = wd
                    AND t.start_time < CASE f.idx WHEN 1 THEN time '12:00' ELSE time '17:00' END AND t.end_time > CASE f.idx WHEN 1 THEN time '08:00' ELSE time '13:00' END)
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 3. Bloqueios do passado: feriados nacionais (a UBS inteira) e férias de alguns médicos. Só dias já passados, para não tirar horário da agenda de verdade.
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Feriados e férias do passado
INSERT INTO nora_unit.schedule_blocks(id, professional_id, starts_at, ends_at, reason, created_by)
SELECT pg_temp.did('hol:' || p.unit_id || ':' || h.d), NULL, (h.d::timestamp) AT TIME ZONE p.zone, ((h.d + 1)::timestamp) AT TIME ZONE p.zone, h.motivo, pg_temp.demo_actor()
FROM _p p
CROSS JOIN LATERAL (
    SELECT make_date(y, split_part(f.md, '-', 1)::int, split_part(f.md, '-', 2)::int) AS d, f.motivo
    FROM generate_series(extract(year FROM p.today)::int - 1, extract(year FROM p.today)::int) y,
         (VALUES ('01-01', 'Feriado: Confraternização Universal'), ('04-21', 'Feriado: Tiradentes'), ('05-01', 'Feriado: Dia do Trabalho'), ('09-07', 'Feriado: Independência do Brasil'),
                 ('10-12', 'Feriado: Nossa Senhora Aparecida'), ('11-02', 'Feriado: Finados'), ('11-15', 'Feriado: Proclamação da República'), ('12-25', 'Feriado: Natal')) AS f(md, motivo)
    UNION ALL
    -- Datas móveis (Carnaval, Sexta-feira Santa, Corpus Christi) e o recesso de fim de ano.
    SELECT s.d::date, s.motivo FROM (VALUES ('2025-12-24', 'Recesso de fim de ano'), ('2025-12-26', 'Recesso de fim de ano'), ('2026-02-16', 'Ponto facultativo: Carnaval'), ('2026-02-17', 'Ponto facultativo: Carnaval'),
                 ('2026-04-03', 'Feriado: Sexta-feira Santa'), ('2026-06-04', 'Feriado: Corpus Christi'), ('2027-02-08', 'Ponto facultativo: Carnaval'), ('2027-02-09', 'Ponto facultativo: Carnaval'),
                 ('2027-03-26', 'Feriado: Sexta-feira Santa'), ('2027-05-27', 'Feriado: Corpus Christi')) AS s(d, motivo)
) h
WHERE h.d >= p.today - p.hist_days AND h.d < p.today
ON CONFLICT DO NOTHING;

-- Férias de três médicos (quinze dias seguidos, escolhidos pela semente) que NÃO sejam os da simulação na escola (_p.protegidos).
INSERT INTO nora_unit.schedule_blocks(id, professional_id, starts_at, ends_at, reason, created_by)
SELECT pg_temp.did('fer:' || f.prof_id), f.prof_id,
       ((p.today - p.hist_days + 30 + floor(pg_temp.u('fer1:' || f.prof_id) * (p.hist_days - 80))::int)::timestamp) AT TIME ZONE p.zone,
       ((p.today - p.hist_days + 30 + floor(pg_temp.u('fer1:' || f.prof_id) * (p.hist_days - 80))::int + 15)::timestamp) AT TIME ZONE p.zone,
       'Férias', pg_temp.demo_actor()
FROM (SELECT * FROM _prof WHERE doc_name NOT IN (SELECT unnest(protegidos) FROM _p) ORDER BY pg_temp.u('fer0:' || prof_id) LIMIT 3) f CROSS JOIN _p p
WHERE p.scale >= 0.5
ON CONFLICT DO NOTHING;

\ir ubs-agenda.sql
\ir ubs-jornada.sql
