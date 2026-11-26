-- Agenda fictícia de uma UBS: horários da grade -> consultas (passado, hoje e próximos dias) -> Vaga Relâmpago -> gravação.
-- Continuação de ubs.sql (as tabelas temporárias _p, _pat, _patx, _cat, _prof continuam valendo; tudo na mesma transação).
--
-- Regra de ouro da repetição: a escolha de quais horários viram consulta, de quem é a consulta e do que aconteceu com ela é uma função
-- PURA da semente (pg_temp.u) e do que NÃO é fictício no banco. A consulta fictícia nunca "ocupa" o horário na hora de escolher; por isso
-- rodar de novo escolhe exatamente as mesmas e o ON CONFLICT DO NOTHING não grava nada de novo.

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 4. Todos os atendimentos que a grade oferece no período (os mesmos que o SlotEngine da API calcularia)
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Calculando os horários da grade (12 meses)
CREATE TEMP TABLE _slot AS
SELECT f.prof_id, f.doc_name AS prof_name, f.spec_id, f.spec_name, f.weight, f.ages, f.sex, f.idx, t.room_id, d.day,
       (d.day + t.start_time + g.k * t.slot_minutes * interval '1 minute') AT TIME ZONE p.zone AS s,
       (d.day + t.start_time + (g.k + 1) * t.slot_minutes * interval '1 minute') AT TIME ZONE p.zone AS e,
       false AS blocked, false AS taken
FROM _p p
CROSS JOIN LATERAL (SELECT (p.today - p.hist_days + i) AS day FROM generate_series(0, p.hist_days + p.fut_days) i) d
JOIN nora_unit.schedule_templates t ON t.weekday = extract(dow FROM d.day)::int AND (t.valid_from IS NULL OR t.valid_from <= d.day) AND (t.valid_until IS NULL OR t.valid_until >= d.day)
JOIN _prof f ON f.prof_id = t.professional_id AND f.spec_id = t.specialty_id
JOIN nora_unit.rooms r ON r.id = t.room_id AND r.is_active
CROSS JOIN LATERAL generate_series(0, (extract(epoch FROM (t.end_time - t.start_time))::int / 60 / t.slot_minutes) - 1) g(k);
CREATE INDEX ON _slot (prof_id, s);

-- Bloqueado (UBS ou profissional) e ocupado por algo que NÃO é fictício (consulta ativa ou vaga relâmpago aberta/ofertada).
UPDATE _slot s SET
    blocked = EXISTS (SELECT 1 FROM nora_unit.schedule_blocks b WHERE (b.professional_id IS NULL OR b.professional_id = s.prof_id) AND b.starts_at < s.e AND b.ends_at > s.s),
    taken = EXISTS (SELECT 1 FROM nora_unit.appointments a WHERE NOT pg_temp.is_demo(a.id) AND a.status IN ('scheduled', 'called', 'completed', 'missed')
                      AND lower(btrim(a.professional)) = lower(btrim(s.prof_name)) AND a.starts_at < s.e AND a.ends_at > s.s)
         OR EXISTS (SELECT 1 FROM nora_unit.lightning_slots l WHERE NOT pg_temp.is_demo(l.id) AND l.status IN ('open', 'offered')
                      AND lower(btrim(l.professional)) = lower(btrim(s.prof_name)) AND l.starts_at < s.e AND l.ends_at > s.s);

-- Oferta e ocupação real por especialidade e dia (base da regra "no mínimo 60% dos horários livres nos dias futuros").
CREATE TEMP TABLE _cap AS
SELECT day, spec_id, count(*) FILTER (WHERE NOT blocked) AS offered, count(*) FILTER (WHERE NOT blocked AND taken) AS taken, count(*) FILTER (WHERE NOT blocked AND NOT taken) AS free
FROM _slot GROUP BY day, spec_id;

-- Meta de consultas de cada dia: sazonalidade semanal (segunda cheia, sexta mais vazia), leve crescimento no ano, inverno mais movimentado,
-- ruído diário e, nos dias futuros, menos agendamentos quanto mais longe.
CREATE TEMP TABLE _dt AS
SELECT d.day,
       p.base_per_day * p.scale
       * CASE extract(dow FROM d.day)::int WHEN 1 THEN 1.25 WHEN 2 THEN 1.10 WHEN 3 THEN 1.0 WHEN 4 THEN 0.95 WHEN 5 THEN 0.70 ELSE 0.4 END
       * (0.80 + 0.40 * LEAST(1.0, GREATEST(0.0, (d.day - (p.today - p.hist_days))::numeric / p.hist_days)))
       * CASE extract(month FROM d.day)::int WHEN 6 THEN 1.15 WHEN 7 THEN 1.20 WHEN 8 THEN 1.08 WHEN 12 THEN 0.85 WHEN 1 THEN 0.88 WHEN 2 THEN 0.94 ELSE 1.0 END
       * (0.85 + 0.30 * pg_temp.u('dn:' || d.day::text || ':' || p.unit_id::text))
       * CASE WHEN d.day <= p.today THEN 1.0 WHEN d.day - p.today <= 3 THEN 0.95 WHEN d.day - p.today <= 7 THEN 0.80 ELSE 0.55 END AS target
FROM (SELECT DISTINCT day FROM _slot) d CROSS JOIN _p p;

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 5. Quais horários viram consulta e quem é o paciente
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Escolhendo as consultas e os pacientes
CREATE TEMP TABLE _sel AS
SELECT s.*
FROM _slot s JOIN _cap c ON c.day = s.day AND c.spec_id = s.spec_id JOIN _dt t ON t.day = s.day
WHERE NOT s.blocked AND NOT s.taken
  AND pg_temp.u('sel:' || s.prof_id || ':' || extract(epoch FROM s.s)) < LEAST(0.9, t.target * (s.weight / 100.0) * CASE s.idx WHEN 1 THEN 1.08 ELSE 0.92 END / GREATEST(c.free, 1));

-- Sorteio ponderado (corrida exponencial): cada paciente elegível "corre" com tempo -ln(u)/peso e o menor tempo vence.
CREATE TEMP TABLE _apt0 AS
SELECT DISTINCT ON (x.prof_id, x.s) x.prof_id, x.s, x.cpf
FROM (
    SELECT s.prof_id, s.s, p.cpf, -ln(greatest(pg_temp.u('pat:' || s.prof_id || ':' || extract(epoch FROM s.s) || ':' || p.cpf), 1e-9)) / p.weight AS score
    FROM _sel s
    JOIN _patx p ON p.reg_at < s.s - interval '1 day' AND (p.deact_at IS NULL OR p.deact_at > s.s) AND position(p.age IN s.ages) > 0 AND (s.sex = '' OR p.sex = s.sex)
) x
ORDER BY x.prof_id, x.s, x.score;

CREATE TEMP TABLE _a1 AS
SELECT s.*, a.cpf FROM _sel s JOIN _apt0 a ON a.prof_id = s.prof_id AND a.s = s.s;

-- Paciente do app (-CpfApp): garante duas consultas marcadas à frente (uma nas próximas 48 h, para aparecer a confirmação de presença, e outra uns dias depois)
-- em horários livres de especialidades sem restrição de idade ou sexo, para as telas do aplicativo nunca ficarem sem "próxima consulta".
INSERT INTO _a1
SELECT s.*, q.cpf
FROM (SELECT cpf, n.i FROM _patx CROSS JOIN generate_series(1, 2) AS n(i) WHERE app) q
CROSS JOIN _p p
CROSS JOIN LATERAL (
    SELECT x.* FROM _slot x
    WHERE NOT x.blocked AND NOT x.taken AND x.sex = '' AND x.weight >= 4 AND position('a' IN x.ages) > 0 AND x.s > p.now_ts + interval '3 hours'
      AND x.day BETWEEN p.today + CASE q.i WHEN 1 THEN 1 ELSE 4 END AND p.today + CASE q.i WHEN 1 THEN 2 ELSE 10 END
      AND NOT EXISTS (SELECT 1 FROM _a1 b WHERE b.prof_id = x.prof_id AND b.s = x.s)
    ORDER BY pg_temp.u('app:' || q.cpf || ':' || q.i || ':' || x.prof_id || ':' || extract(epoch FROM x.s)) LIMIT 1
) s;
DELETE FROM _a1 a USING _a1 b WHERE a.prof_id = b.prof_id AND a.s = b.s AND a.cpf > b.cpf;

-- O mesmo paciente não fica em dois lugares ao mesmo tempo (o mais cedo vence), nem em cima de uma consulta real dele.
DELETE FROM _a1 a USING _a1 b WHERE a.cpf = b.cpf AND (b.s, b.prof_id) < (a.s, a.prof_id) AND b.s < a.e AND b.e > a.s;
DELETE FROM _a1 a WHERE EXISTS (SELECT 1 FROM nora_unit.appointments x WHERE x.cpf = a.cpf AND NOT pg_temp.is_demo(x.id) AND x.status IN ('scheduled', 'called', 'completed', 'missed') AND x.starts_at < a.e AND x.ends_at > a.s);

-- Hoje e os próximos dias: a agenda real da simulação precisa de folga. As consultas fictícias, somadas às reais, ficam em no máximo _p.teto da
-- oferta de cada especialidade em cada dia (teto 0,35 = pelo menos 65% dos horários livres; a promessa é 60%).
DELETE FROM _a1 a USING (
    SELECT a2.prof_id, a2.s, row_number() OVER (PARTITION BY a2.day, a2.spec_id ORDER BY pg_temp.u('cap:' || a2.prof_id || ':' || extract(epoch FROM a2.s))) AS rn, c.offered, c.taken
    FROM _a1 a2 JOIN _cap c ON c.day = a2.day AND c.spec_id = a2.spec_id WHERE a2.day >= (SELECT today FROM _p)
) z
WHERE a.prof_id = z.prof_id AND a.s = z.s AND z.rn > GREATEST(0, floor((SELECT teto FROM _p) * z.offered) - z.taken);

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 6. O que aconteceu com cada consulta (situação, quem marcou, quando, confirmação) e os instantes dos eventos
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Definindo a situação de cada consulta
CREATE TEMP TABLE _apt AS
WITH b AS (
    SELECT a.prof_id, a.prof_name, a.spec_id, a.spec_name, a.room_id, a.s, a.e, a.day, a.cpf, pt.f, pt.boost_days, pt.boost_mult, pt.app, pt.reg_at,
           p.today, p.now_ts, p.ref_ts, p.miss_mult, p.hist_days,
           pg_temp.did('ap:' || a.prof_id || ':' || extract(epoch FROM a.s)) AS id
    FROM _a1 a JOIN _patx pt ON pt.cpf = a.cpf CROSS JOIN _p p
), c AS (
    SELECT b.*,
           LEAST(0.65, 0.085 * b.miss_mult * b.f * CASE WHEN b.today - b.day <= b.boost_days THEN b.boost_mult ELSE 1 END) AS pm,
           0.075 * b.f * CASE WHEN b.today - b.day <= b.boost_days THEN b.boost_mult * 1.3 ELSE 1 END AS pc0,
           (b.boost_days > 0 AND b.today - b.day <= b.boost_days) AS em_alta
    FROM b
), d AS (
    SELECT c.*, LEAST(c.pc0, 0.45, 0.94 - c.pm) AS pc,
           pg_temp.u('out:' || c.id) AS r,
           -- Quando a consulta foi marcada (de 1 a 20 dias antes, a maioria perto); nunca antes do cadastro do paciente nem depois do início.
           LEAST(GREATEST(c.s - interval '1 hour' * (6 + 24 * (1 + 19 * power(pg_temp.u('cb:' || c.id), 2))), c.reg_at + interval '10 minutes'), c.s - interval '30 minutes') AS created0
    FROM c
), e AS (
    SELECT d.*,
           CASE
               WHEN d.day > d.today THEN 'scheduled'
               WHEN d.day = d.today AND d.e > d.ref_ts - interval '10 minutes' THEN CASE WHEN d.s <= d.ref_ts THEN 'called' ELSE 'scheduled' END
               WHEN d.r < d.pm THEN 'missed'
               WHEN d.r < d.pm + d.pc THEN 'cancelled_p'
               WHEN d.r < d.pm + d.pc + 0.05 THEN 'cancelled_s'
               ELSE 'completed'
           END AS outcome
    FROM d
), f AS (
    SELECT e.*,
           GREATEST(CASE WHEN e.outcome IN ('scheduled', 'called') AND e.day >= e.today THEN LEAST(e.created0, e.now_ts - interval '15 minutes') ELSE e.created0 END, e.reg_at + interval '5 minutes') AS created_at,
           pg_temp.u('ld:' || e.id) * CASE WHEN e.em_alta THEN 0.3 ELSE 1.0 END AS r3, pg_temp.u('lq:' || e.id) AS r4
    FROM e
), g AS (
    SELECT f.*,
           CASE f.outcome
               WHEN 'cancelled_p' THEN CASE WHEN f.r3 < 0.03 THEN 0.3 + 1.6 * f.r4 WHEN f.r3 < 0.08 THEN 2 + 4 * f.r4 WHEN f.r3 < 0.15 THEN 6 + 6 * f.r4 WHEN f.r3 < 0.25 THEN 12 + 12 * f.r4
                                           WHEN f.r3 < 0.45 THEN 24 + 24 * f.r4 WHEN f.r3 < 0.65 THEN 48 + 24 * f.r4 WHEN f.r3 < 0.80 THEN 72 + 24 * f.r4 ELSE 96 + 192 * f.r4 END
               WHEN 'cancelled_s' THEN 1 + 71 * f.r4
           END AS lead0
    FROM f
)
SELECT g.id, g.prof_id, g.prof_name, g.spec_id, g.spec_name, g.room_id, g.s, g.e, g.day, g.cpf, g.outcome, g.created_at,
       CASE WHEN g.app AND g.day >= g.today THEN 'staff'
            WHEN pg_temp.u('bk:' || g.id) < 0.12 + 0.38 * LEAST(1.0, GREATEST(0.0, (g.day - (g.today - g.hist_days))::numeric / g.hist_days)) THEN 'patient' ELSE 'staff' END AS booked_by,
       CASE WHEN g.lead0 IS NOT NULL THEN LEAST(g.lead0, GREATEST(0.2, extract(epoch FROM (g.s - g.created_at)) / 3600.0 - 0.2)) END AS lead_h,
       g.now_ts, g.ref_ts, g.today, g.r, g.reg_at, g.app
FROM g;
ALTER TABLE _apt ADD COLUMN booker uuid, ADD COLUMN cancel_ts timestamptz, ADD COLUMN cancel_source text, ADD COLUMN called_ts timestamptz, ADD COLUMN done_ts timestamptz,
    ADD COLUMN confirmed_at timestamptz, ADD COLUMN slot_ts timestamptz;
UPDATE _apt a SET
    booker = CASE WHEN a.booked_by = 'patient' THEN pg_temp.did('pt:' || a.cpf) ELSE pg_temp.demo_actor() END,
    cancel_ts = CASE WHEN a.outcome IN ('cancelled_p', 'cancelled_s') THEN a.s - a.lead_h * interval '1 hour' END,
    cancel_source = CASE a.outcome WHEN 'cancelled_p' THEN 'patient' WHEN 'cancelled_s' THEN 'staff' END,
    called_ts = CASE a.outcome WHEN 'completed' THEN a.s + pg_temp.u('ct:' || a.id) * 7 * interval '1 minute' WHEN 'called' THEN LEAST(a.s + interval '2 minutes', a.ref_ts) END;
UPDATE _apt a SET
    done_ts = CASE a.outcome WHEN 'completed' THEN a.called_ts + (8 + 10 * pg_temp.u('dt:' || a.id)) * interval '1 minute' WHEN 'missed' THEN a.e + (3 + 22 * pg_temp.u('dm:' || a.id)) * interval '1 minute' END,
    confirmed_at = CASE
        WHEN a.outcome = 'scheduled' AND a.s - interval '2 hours' > a.now_ts AND a.s - interval '48 hours' < a.now_ts AND (pg_temp.u('cf:' || a.id) < 0.6 OR a.app)
             AND LEAST(a.s - interval '2 hours', a.now_ts - interval '5 minutes') > GREATEST(a.s - interval '48 hours', a.created_at + interval '10 minutes')
            THEN GREATEST(a.s - interval '48 hours', a.created_at + interval '10 minutes')
                 + pg_temp.u('cu:' || a.id) * (LEAST(a.s - interval '2 hours', a.now_ts - interval '5 minutes') - GREATEST(a.s - interval '48 hours', a.created_at + interval '10 minutes'))
        WHEN a.outcome = 'completed' AND a.booked_by = 'patient' AND pg_temp.u('cf:' || a.id) < 0.7 THEN GREATEST(a.s - (2.5 + 40 * pg_temp.u('cu:' || a.id)) * interval '1 hour', a.created_at + interval '5 minutes')
    END;
CREATE UNIQUE INDEX ON _apt (id);
CREATE INDEX ON _apt (cpf, s);

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 7. Vaga Relâmpago do passado: consulta cancelada pelo paciente -> vaga -> ofertas (recusa, vencimento ou aceite) -> consulta antecipada
--    (a consulta antiga do aceitante é cancelada com origem 'rescheduled-lightning', que não pesa no score; a nova nasce com booked_by='lightning')
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Vaga Relâmpago do histórico
CREATE TEMP TABLE _lx_x AS
SELECT a.id, a.cpf, a.spec_id, a.spec_name, a.prof_id, a.prof_name, a.room_id, a.s, a.e, a.cancel_ts, a.cancel_ts + interval '20 seconds' AS slot_ts
FROM _apt a CROSS JOIN _p p
WHERE a.outcome = 'cancelled_p' AND a.day < p.today AND a.lead_h >= 1.0 AND pg_temp.u('lx:' || a.id) < p.lightning_rate;

CREATE TEMP TABLE _lx_c AS
SELECT x.id AS x_id, y.id AS y_id, y.cpf AS y_cpf, pg_temp.u('ly:' || x.id || ':' || y.id) AS h
FROM _lx_x x
JOIN _apt y ON y.spec_id = x.spec_id AND y.cpf <> x.cpf AND y.outcome = 'completed' AND y.s >= x.s + interval '2 hours' AND y.s <= x.s + interval '21 days'
           AND y.created_at < x.cancel_ts - interval '5 minutes' AND y.day < (SELECT today FROM _p)
WHERE NOT EXISTS (SELECT 1 FROM _apt z WHERE z.cpf = y.cpf AND z.id <> y.id AND z.outcome IN ('completed', 'missed', 'scheduled', 'called') AND z.s < x.e AND z.e > x.s);

-- Quem aceitou: uma consulta por vaga e uma vaga por consulta (cerca de 80% das vagas acham quem aceite).
CREATE TEMP TABLE _lx_f AS
SELECT DISTINCT ON (q.y_id) q.x_id, q.y_id, q.y_cpf
FROM (SELECT DISTINCT ON (c.x_id) c.* FROM _lx_c c ORDER BY c.x_id, c.h) q
WHERE pg_temp.u('acc:' || q.x_id) < 0.8
ORDER BY q.y_id, pg_temp.u('lyy:' || q.x_id || ':' || q.y_id);

-- Quantas ofertas anteriores (recusadas ou vencidas) cada vaga teve antes do aceite (ou antes de fechar sem ninguém).
CREATE TEMP TABLE _lx_n AS
SELECT x.id AS x_id, EXISTS (SELECT 1 FROM _lx_f f WHERE f.x_id = x.id) AS aceita,
       CASE WHEN EXISTS (SELECT 1 FROM _lx_f f WHERE f.x_id = x.id)
            THEN CASE WHEN pg_temp.u('np:' || x.id) < 0.55 THEN 0 WHEN pg_temp.u('np:' || x.id) < 0.85 THEN 1 ELSE 2 END
            ELSE CASE WHEN pg_temp.u('np:' || x.id) < 0.20 THEN 0 WHEN pg_temp.u('np:' || x.id) < 0.60 THEN 1 WHEN pg_temp.u('np:' || x.id) < 0.85 THEN 2 ELSE 3 END END AS n_prior
FROM _lx_x x;

CREATE TEMP TABLE _lx_o AS
SELECT o.x_id, o.seq, o.cpf, o.appt_id, o.status,
       CASE o.status WHEN 'declined' THEN 20 + 400 * pg_temp.u('dd:' || o.x_id || ':' || o.seq) WHEN 'expired' THEN 600 ELSE 20 + 300 * pg_temp.u('da:' || o.x_id || ':' || o.seq) END AS dur
FROM (
    SELECT p.x_id, p.rn::int AS seq, p.y_cpf AS cpf, p.y_id AS appt_id, CASE WHEN pg_temp.u('pst:' || p.x_id || ':' || p.rn) < 0.65 THEN 'declined' ELSE 'expired' END AS status
    FROM (SELECT q.*, row_number() OVER (PARTITION BY q.x_id ORDER BY q.h) AS rn
          FROM (SELECT DISTINCT ON (c.x_id, c.y_cpf) c.* FROM _lx_c c
                WHERE NOT EXISTS (SELECT 1 FROM _lx_f f WHERE f.x_id = c.x_id AND (f.y_id = c.y_id OR f.y_cpf = c.y_cpf)) ORDER BY c.x_id, c.y_cpf, c.h) q) p
    JOIN _lx_n n ON n.x_id = p.x_id WHERE p.rn <= n.n_prior
    UNION ALL
    SELECT f.x_id, 100, f.y_cpf, f.y_id, 'accepted' FROM _lx_f f
) o;

-- Instante de início de cada oferta (cada uma começa quando a anterior terminou) e do aceite.
CREATE TEMP TABLE _lx_t AS
SELECT o.*, x.slot_ts + interval '10 seconds'
            + COALESCE(sum(o.dur + 10) OVER (PARTITION BY o.x_id ORDER BY o.seq ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) * interval '1 second' AS t0
FROM _lx_o o JOIN _lx_x x ON x.id = o.x_id;

-- Consulta antecipada (nasce no instante do aceite, no horário da vaga) e consulta antiga cancelada pela antecipação.
UPDATE _apt y SET outcome = 'cancelled_r', cancel_source = 'rescheduled-lightning', cancel_ts = t.t0 + t.dur * interval '1 second', called_ts = NULL, done_ts = NULL, confirmed_at = NULL
FROM _lx_t t WHERE t.status = 'accepted' AND t.appt_id = y.id;

INSERT INTO _apt (id, prof_id, prof_name, spec_id, spec_name, room_id, s, e, day, cpf, outcome, created_at, booked_by, lead_h, now_ts, ref_ts, today, r, reg_at, booker, called_ts, done_ts)
SELECT pg_temp.did('lz:' || x.id), x.prof_id, x.prof_name, x.spec_id, x.spec_name, x.room_id, x.s, x.e, (x.s AT TIME ZONE p.zone)::date, t.cpf, 'completed', t.t0 + t.dur * interval '1 second', 'lightning', NULL, p.now_ts, p.ref_ts, p.today, 0, NULL,
       pg_temp.did('pt:' || t.cpf), x.s + pg_temp.u('lc:' || x.id) * 6 * interval '1 minute', x.s + (9 + 8 * pg_temp.u('ld2:' || x.id)) * interval '1 minute'
FROM _lx_t t JOIN _lx_x x ON x.id = t.x_id CROSS JOIN _p p WHERE t.status = 'accepted';

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 8. Vaga Relâmpago EM ANDAMENTO agora (1 ou 2): a consulta do paciente A, de amanhã, foi cancelada há poucos minutos e a vaga está
--    ofertada ao paciente B (que tem consulta depois, na mesma especialidade), com o prazo de 10 minutos correndo.
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Vaga Relâmpago em andamento
-- Pares possíveis: a consulta cancelada (nos 3 próximos dias) e outra consulta marcada DEPOIS, na mesma especialidade, de outra pessoa (até 14 dias).
CREATE TEMP TABLE _lv_pares AS
SELECT x.id AS x_id, x.cpf AS x_cpf, x.spec_id, y.id AS y_id, y.cpf AS y_cpf, pg_temp.u('lv:' || x.id || ':' || y.id) AS h
FROM _apt x CROSS JOIN _p p
JOIN _apt y ON y.spec_id = x.spec_id AND y.cpf <> x.cpf AND y.outcome = 'scheduled' AND y.s >= x.s + interval '3 hours'
WHERE p.live AND x.outcome = 'scheduled' AND x.day > p.today AND x.day <= p.today + 3
  AND x.cpf NOT IN (SELECT cpf FROM _pat WHERE app) AND y.cpf NOT IN (SELECT cpf FROM _pat WHERE app);
CREATE TEMP TABLE _lv AS
WITH k1 AS (SELECT * FROM _lv_pares ORDER BY h LIMIT 1),
k2 AS (SELECT q.* FROM _lv_pares q, k1 WHERE q.spec_id <> k1.spec_id AND q.x_cpf NOT IN (k1.x_cpf, k1.y_cpf) AND q.y_cpf NOT IN (k1.x_cpf, k1.y_cpf)
                                          AND q.x_id <> k1.y_id AND q.y_id <> k1.x_id ORDER BY q.h LIMIT 1)
SELECT 1 AS k, x_id, y_id, y_cpf FROM k1 UNION ALL SELECT 2, x_id, y_id, y_cpf FROM k2;

UPDATE _apt a SET outcome = 'cancelled_p', cancel_source = 'patient', confirmed_at = NULL,
    cancel_ts = a.now_ts - (CASE v.k WHEN 1 THEN 3 ELSE 6 END) * interval '1 minute',
    lead_h = extract(epoch FROM (a.s - (a.now_ts - (CASE v.k WHEN 1 THEN 3 ELSE 6 END) * interval '1 minute'))) / 3600.0,
    slot_ts = a.now_ts - (CASE v.k WHEN 1 THEN 3 ELSE 6 END) * interval '1 minute' + interval '20 seconds'
FROM _lv v WHERE v.x_id = a.id;

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 9. Gravação das consultas e dos eventos (só das consultas que ainda não existiam: _ins)
-- ---------------------------------------------------------------------------------------------------------------------------------
\echo E|Gravando consultas, eventos e vagas
CREATE TEMP TABLE _ins AS
WITH i AS (
    INSERT INTO nora_unit.appointments(id, cpf, specialty, professional, starts_at, ends_at, status, created_by, created_at, professional_id, specialty_id, room_id, booked_by, confirmed_at, confirmed_by)
    SELECT a.id, a.cpf, a.spec_name, a.prof_name, a.s, a.e,
           CASE a.outcome WHEN 'completed' THEN 'completed' WHEN 'missed' THEN 'missed' WHEN 'called' THEN 'called' WHEN 'scheduled' THEN 'scheduled' ELSE 'cancelled' END,
           a.booker, a.created_at, a.prof_id, a.spec_id, a.room_id, a.booked_by, a.confirmed_at, CASE WHEN a.confirmed_at IS NOT NULL THEN pg_temp.did('pt:' || a.cpf) END
    FROM _apt a ORDER BY a.created_at, a.id
    ON CONFLICT DO NOTHING RETURNING id)
SELECT id FROM i;

INSERT INTO nora_unit.appointment_events(appointment_id, actor_id, status, occurred_at, source)
SELECT a.id, a.booker, 'scheduled', a.created_at, CASE WHEN a.booked_by IN ('patient', 'lightning') THEN 'patient' ELSE 'staff' END
FROM _apt a JOIN _ins i ON i.id = a.id ORDER BY a.created_at, a.id;
INSERT INTO nora_unit.appointment_events(appointment_id, actor_id, status, occurred_at, source)
SELECT a.id, pg_temp.demo_actor(), 'called', a.called_ts, 'staff' FROM _apt a JOIN _ins i ON i.id = a.id WHERE a.outcome IN ('completed', 'called') ORDER BY a.called_ts, a.id;
INSERT INTO nora_unit.appointment_events(appointment_id, actor_id, status, occurred_at, source)
SELECT a.id, pg_temp.demo_actor(), a.outcome, a.done_ts, 'staff' FROM _apt a JOIN _ins i ON i.id = a.id WHERE a.outcome IN ('completed', 'missed') ORDER BY a.done_ts, a.id;
INSERT INTO nora_unit.appointment_events(appointment_id, actor_id, status, occurred_at, source)
SELECT a.id, CASE WHEN a.cancel_source = 'staff' THEN pg_temp.demo_actor() ELSE pg_temp.did('pt:' || a.cpf) END, 'cancelled', a.cancel_ts, a.cancel_source
FROM _apt a JOIN _ins i ON i.id = a.id WHERE a.outcome LIKE 'cancelled%' AND a.cancel_source IS NOT NULL ORDER BY a.cancel_ts, a.id;

-- Vagas e ofertas do histórico.
INSERT INTO nora_unit.lightning_slots(id, appointment_id, status, created_at, created_by, origin, specialty, specialty_id, professional, professional_id, room_id, starts_at, ends_at, closed_reason)
SELECT pg_temp.did('ls:' || x.id), x.id, CASE WHEN f.x_id IS NOT NULL THEN 'accepted' ELSE 'closed' END, x.slot_ts, NULL, 'cancellation', x.spec_name, x.spec_id, x.prof_name, x.prof_id, x.room_id, x.s, x.e,
       CASE WHEN f.x_id IS NULL THEN 'too-late' END
FROM _lx_x x LEFT JOIN _lx_f f ON f.x_id = x.id ON CONFLICT DO NOTHING;
-- A consulta que foi antecipada também libera o horário antigo: metade dessas vagas "em cascata" ninguém aceita e fecham perto da hora.
INSERT INTO nora_unit.lightning_slots(id, appointment_id, status, created_at, created_by, origin, specialty, specialty_id, professional, professional_id, room_id, starts_at, ends_at, closed_reason)
SELECT pg_temp.did('ls2:' || y.id), y.id, 'closed', t.t0 + t.dur * interval '1 second' + interval '20 seconds', NULL, 'rescheduled', y.spec_name, y.spec_id, y.prof_name, y.prof_id, y.room_id, y.s, y.e, 'too-late'
FROM _lx_t t JOIN _apt y ON y.id = t.appt_id WHERE t.status = 'accepted' AND pg_temp.u('rs:' || y.id) < 0.5 ON CONFLICT DO NOTHING;
INSERT INTO nora_unit.lightning_offers(id, slot_id, entry_id, expires_at, status, cpf, appointment_id)
SELECT pg_temp.did('lo:' || t.x_id || ':' || t.seq), pg_temp.did('ls:' || t.x_id), NULL, t.t0 + interval '10 minutes', t.status, t.cpf, t.appt_id FROM _lx_t t ON CONFLICT DO NOTHING;

-- Em andamento agora: vaga ofertada com prazo correndo (o distribuidor da API a vence sozinho quando o prazo acabar).
INSERT INTO nora_unit.lightning_slots(id, appointment_id, status, created_at, created_by, origin, specialty, specialty_id, professional, professional_id, room_id, starts_at, ends_at)
SELECT pg_temp.did('ls:' || a.id), a.id, 'offered', a.slot_ts, NULL, 'cancellation', a.spec_name, a.spec_id, a.prof_name, a.prof_id, a.room_id, a.s, a.e FROM _apt a JOIN _lv v ON v.x_id = a.id ON CONFLICT DO NOTHING;
INSERT INTO nora_unit.lightning_offers(id, slot_id, entry_id, expires_at, status, cpf, appointment_id)
SELECT pg_temp.did('lo:' || v.x_id || ':1'), pg_temp.did('ls:' || v.x_id), NULL, p.now_ts + (CASE v.k WHEN 1 THEN 8 ELSE 5 END) * interval '1 minute', 'pending', v.y_cpf, v.y_id FROM _lv v CROSS JOIN _p p ON CONFLICT DO NOTHING;
