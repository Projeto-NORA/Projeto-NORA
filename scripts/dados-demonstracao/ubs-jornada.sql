-- Jornada do paciente (exames e vacinas) e fechamento de uma UBS. Continuação de ubs-agenda.sql, na mesma transação.
-- Os registros nascem como a API os gravaria: exame solicitado/realizado/cancelado/anulado e vacina aplicada/anulada, cada um com o evento de auditoria.

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 10. Vacinas (idade: c = criança, t = adolescente, a = adulto, i = idoso; p = chance de a pessoa ter a vacina; dias = de quantos dias atrás a 1ª dose pode ser)
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Vacinas
CREATE TEMP TABLE _vac (nome text, idades text, doses text[], p numeric, dias_de int, dias_ate int, intervalo int);
INSERT INTO _vac VALUES
    ('BCG', 'ct', ARRAY['single'], 0.95, 1500, 4500, 0),
    ('Hepatite B', 'ctai', ARRAY['1', '2', '3'], 0.80, 150, 1800, 45),
    ('Pentavalente', 'c', ARRAY['1', '2', '3'], 0.90, 200, 1400, 60),
    ('Poliomielite (VIP)', 'c', ARRAY['1', '2', '3'], 0.90, 200, 1400, 60),
    ('Tríplice viral (SCR)', 'ct', ARRAY['1', '2'], 0.85, 200, 2500, 120),
    ('Meningocócica C', 'ct', ARRAY['1', '2'], 0.60, 200, 2500, 90),
    ('HPV', 't', ARRAY['1', '2'], 0.70, 100, 900, 180),
    ('Febre amarela', 'ctai', ARRAY['single'], 0.60, 60, 1800, 0),
    ('Influenza (campanha 2026)', 'ctai', ARRAY['single'], 0.75, 20, 200, 0),
    ('COVID-19', 'tai', ARRAY['1', '2', 'booster'], 0.65, 120, 700, 75),
    ('Tétano e difteria (dT)', 'ai', ARRAY['booster'], 0.55, 60, 2500, 0),
    ('Pneumocócica 23-valente', 'i', ARRAY['single'], 0.70, 60, 1200, 0);

CREATE TEMP TABLE _vacrow AS
SELECT pg_temp.did('vac:' || t.cpf || ':' || v.nome || ':' || d.dose) AS id, t.cpf, v.nome, d.dose,
       (p.today - (floor(v.dias_de + (v.dias_ate - v.dias_de) * pg_temp.u('va:' || t.cpf || ':' || v.nome))::int) + (d.ord - 1)::int * v.intervalo) AS aplicada,
       pg_temp.u('vv:' || t.cpf || ':' || v.nome || ':' || d.dose) < 0.02 AS anulada,
       p.zone
FROM _pat t CROSS JOIN _vac v CROSS JOIN _p p
CROSS JOIN LATERAL unnest(v.doses) WITH ORDINALITY AS d(dose, ord)
WHERE position(t.age IN v.idades) > 0 AND pg_temp.u('vp:' || t.cpf || ':' || v.nome) < v.p;
DELETE FROM _vacrow v USING _p p WHERE v.aplicada >= p.today;

CREATE TEMP TABLE _vins AS
WITH i AS (
    INSERT INTO nora_unit.health_vaccines(id, cpf, name, dose, applied_on, status, note, void_reason, voided_at, created_by, created_at)
    SELECT v.id, v.cpf, v.nome, v.dose, v.aplicada, CASE WHEN v.anulada THEN 'voided' ELSE 'applied' END,
           CASE WHEN pg_temp.u('vn:' || v.id) < 0.2 THEN 'Sem reações após a aplicação.' END,
           CASE WHEN v.anulada THEN 'Registro lançado no paciente errado.' END,
           CASE WHEN v.anulada THEN (v.aplicada + 1)::timestamp AT TIME ZONE v.zone + interval '10 hours' END,
           pg_temp.demo_actor(), (v.aplicada::timestamp AT TIME ZONE v.zone) + interval '9 hours 30 minutes'
    FROM _vacrow v
    ON CONFLICT DO NOTHING RETURNING id, status)
SELECT id, status FROM i;
INSERT INTO nora_unit.health_record_events(kind, record_id, actor_id, action, occurred_at)
SELECT 'vaccine', v.id, pg_temp.demo_actor(), 'created', (r.aplicada::timestamp AT TIME ZONE r.zone) + interval '9 hours 30 minutes' FROM _vins v JOIN _vacrow r ON r.id = v.id ORDER BY 5, 2;
INSERT INTO nora_unit.health_record_events(kind, record_id, actor_id, action, occurred_at)
SELECT 'vaccine', v.id, pg_temp.demo_actor(), 'voided', ((r.aplicada + 1)::timestamp AT TIME ZONE r.zone) + interval '10 hours' FROM _vins v JOIN _vacrow r ON r.id = v.id WHERE v.status = 'voided';

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 11. Exames (solicitados pela equipe; a maioria já realizada)
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Exames
CREATE TEMP TABLE _exm (nome text, idades text, sexo text, p numeric, nota text);
INSERT INTO _exm VALUES
    ('Hemograma completo', 'tai', '', 0.30, NULL),
    ('Glicemia de jejum', 'tai', '', 0.30, 'Jejum de 8 horas.'),
    ('Colesterol total e frações', 'ai', '', 0.25, 'Jejum de 12 horas.'),
    ('TSH e T4 livre', 'tai', 'F', 0.12, NULL),
    ('Creatinina e ureia', 'ai', '', 0.15, NULL),
    ('Urina tipo I (EAS)', 'ctai', '', 0.18, NULL),
    ('Eletrocardiograma', 'ai', '', 0.18, NULL),
    ('Raio-X de tórax', 'ctai', '', 0.12, NULL),
    ('Ultrassonografia abdominal total', 'tai', '', 0.10, 'Jejum de 8 horas e bexiga cheia.'),
    ('Mamografia bilateral', 'ai', 'F', 0.22, NULL),
    ('Papanicolau (citopatológico)', 'ta', 'F', 0.25, NULL),
    ('PSA total', 'ai', 'M', 0.20, NULL),
    ('Parasitológico de fezes', 'c', '', 0.35, NULL),
    ('Audiometria', 'ai', '', 0.06, NULL);

CREATE TEMP TABLE _exrow AS
SELECT x.*, CASE x.status WHEN 'done' THEN x.pedido + 1 + floor(pg_temp.u('ep:' || x.id) * 14)::int END AS realizado
FROM (
    SELECT pg_temp.did('exm:' || t.cpf || ':' || e.nome) AS id, t.cpf, e.nome, e.nota, p.zone,
           CASE WHEN r < 0.60 THEN 'done' WHEN r < 0.84 THEN 'requested' WHEN r < 0.94 THEN 'cancelled' ELSE 'voided' END AS status,
           CASE WHEN r < 0.60 THEN p.today - 30 - floor(pg_temp.u('ed:' || t.cpf || ':' || e.nome) * 290)::int
                WHEN r < 0.84 THEN p.today - 1 - floor(pg_temp.u('ed:' || t.cpf || ':' || e.nome) * 17)::int
                WHEN r < 0.94 THEN p.today - 20 - floor(pg_temp.u('ed:' || t.cpf || ':' || e.nome) * 100)::int
                ELSE p.today - 60 - floor(pg_temp.u('ed:' || t.cpf || ':' || e.nome) * 140)::int END AS pedido
    FROM _pat t CROSS JOIN _exm e CROSS JOIN _p p
    CROSS JOIN LATERAL (SELECT pg_temp.u('es:' || t.cpf || ':' || e.nome) AS r) q
    WHERE position(t.age IN e.idades) > 0 AND (e.sexo = '' OR e.sexo = t.sex) AND pg_temp.u('ex:' || t.cpf || ':' || e.nome) < e.p
) x;

CREATE TEMP TABLE _eins AS
WITH i AS (
    INSERT INTO nora_unit.health_exams(id, cpf, name, status, requested_at, performed_on, note, void_reason, voided_at, created_by)
    SELECT x.id, x.cpf, x.nome, x.status, (x.pedido::timestamp AT TIME ZONE x.zone) + interval '8 hours 30 minutes', x.realizado, x.nota,
           CASE WHEN x.status = 'voided' THEN 'Solicitado para o paciente errado.' END,
           CASE WHEN x.status = 'voided' THEN ((x.pedido + 1)::timestamp AT TIME ZONE x.zone) + interval '11 hours' END,
           pg_temp.demo_actor()
    FROM _exrow x
    ON CONFLICT DO NOTHING RETURNING id)
SELECT id FROM i;
INSERT INTO nora_unit.health_record_events(kind, record_id, actor_id, action, occurred_at)
SELECT 'exam', x.id, pg_temp.demo_actor(), 'created', (x.pedido::timestamp AT TIME ZONE x.zone) + interval '8 hours 30 minutes' FROM _exrow x JOIN _eins i ON i.id = x.id ORDER BY 5, 2;
INSERT INTO nora_unit.health_record_events(kind, record_id, actor_id, action, occurred_at)
SELECT 'exam', x.id, pg_temp.demo_actor(), CASE x.status WHEN 'done' THEN 'done' WHEN 'cancelled' THEN 'cancelled' ELSE 'voided' END,
       CASE x.status WHEN 'done' THEN (x.realizado::timestamp AT TIME ZONE x.zone) + interval '15 hours'
                     WHEN 'cancelled' THEN ((x.pedido + 3)::timestamp AT TIME ZONE x.zone) + interval '9 hours'
                     ELSE ((x.pedido + 1)::timestamp AT TIME ZONE x.zone) + interval '11 hours' END
FROM _exrow x JOIN _eins i ON i.id = x.id WHERE x.status <> 'requested';

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 12. Fechamento: nenhuma tabela pode ter perdido linhas; o que foi criado sai em linhas "T|tabela|antes|depois" para o PowerShell.
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Conferindo que nenhuma tabela perdeu linhas
SELECT pg_temp.conferir();
SELECT 'T|' || d.t || '|' || COALESCE(a.n, 0) || '|' || d.n FROM _depois d LEFT JOIN _antes a ON a.t = d.t WHERE d.n <> COALESCE(a.n, 0) ORDER BY d.t;
SELECT 'R|consultas|' || status || '|' || count(*) FROM nora_unit.appointments WHERE pg_temp.is_demo(id) GROUP BY status ORDER BY status;
SELECT 'R|pacientes|fictícios|' || count(*) FROM nora_unit.patient_registry WHERE created_by = pg_temp.demo_actor();
