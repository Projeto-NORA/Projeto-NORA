-- Vaga Relâmpago automática (2026-11-24): os candidatos deixam de vir da fila manual (waiting_list) e passam a ser quem tem consulta marcada
-- na mesma especialidade com início posterior ao da vaga. A oferta guarda o CPF e a consulta que será antecipada; o aceite cancela essa consulta
-- (origem 'rescheduled-lightning', que não pesa no score) e o horário antigo vira uma vaga nova (origem 'rescheduled', efeito cascata).
-- Reaplicável: todas as instruções são idempotentes.
ALTER TABLE nora_unit.lightning_offers ADD COLUMN IF NOT EXISTS cpf varchar(11);
ALTER TABLE nora_unit.lightning_offers ADD COLUMN IF NOT EXISTS appointment_id uuid REFERENCES nora_unit.appointments(id);
ALTER TABLE nora_unit.lightning_offers ALTER COLUMN entry_id DROP NOT NULL;
UPDATE nora_unit.lightning_offers o SET cpf = q.cpf FROM nora_unit.waiting_list q WHERE q.id = o.entry_id AND o.cpf IS NULL;
ALTER TABLE nora_unit.lightning_offers ALTER COLUMN cpf SET NOT NULL;
SELECT nora_unit.ensure_check('nora_unit.appointment_events', 'appointment_events_source_check', 'source IN (''staff'', ''system'', ''patient'', ''rescheduled-lightning'')', ARRAY['staff', 'system', 'patient', 'rescheduled-lightning']);
SELECT nora_unit.ensure_check('nora_unit.lightning_slots', 'lightning_slots_origin_check', 'origin IN (''staff'', ''cancellation'', ''call-next'', ''rescheduled'')', ARRAY['staff', 'cancellation', 'call-next', 'rescheduled']);
-- Fim do caminho manual: ofertas pendentes da fila antiga são canceladas (as vagas voltam a 'open' e seguem a distribuição nova) e as entradas ativas da fila são encerradas.
UPDATE nora_unit.lightning_slots SET status = 'open' WHERE status = 'offered' AND id IN (SELECT slot_id FROM nora_unit.lightning_offers WHERE status = 'pending' AND entry_id IS NOT NULL);
UPDATE nora_unit.lightning_offers SET status = 'cancelled' WHERE status = 'pending' AND entry_id IS NOT NULL;
WITH changed AS (UPDATE nora_unit.waiting_list SET status = 'cancelled' WHERE status IN ('waiting', 'offered') RETURNING id)
INSERT INTO nora_unit.waiting_list_events(entry_id, actor_id, status, occurred_at, source) SELECT id, NULL, 'cancelled', CURRENT_TIMESTAMP, 'system' FROM changed;
-- Fim da aprovação das vagas: a que aguardava vira 'open' se o horário continua livre; senão fecha ('conflict'). O dispatcher fecha as que ficaram perto demais.
UPDATE nora_unit.lightning_slots a SET status = 'closed', closed_reason = 'conflict' WHERE a.status = 'awaiting_approval' AND (
    EXISTS (SELECT 1 FROM nora_unit.appointments b WHERE lower(btrim(b.professional)) = lower(btrim(a.professional)) AND b.status IN ('scheduled', 'called', 'completed', 'missed') AND b.starts_at < a.ends_at AND b.ends_at > a.starts_at)
    OR EXISTS (SELECT 1 FROM nora_unit.lightning_slots y WHERE y.id <> a.id AND lower(btrim(y.professional)) = lower(btrim(a.professional)) AND y.status IN ('open', 'offered') AND y.starts_at < a.ends_at AND y.ends_at > a.starts_at));
UPDATE nora_unit.lightning_slots SET status = 'open' WHERE status = 'awaiting_approval';
CREATE UNIQUE INDEX IF NOT EXISTS lightning_pending_cpf_idx ON nora_unit.lightning_offers(cpf) WHERE status = 'pending';
CREATE INDEX IF NOT EXISTS lightning_offers_cpf_idx ON nora_unit.lightning_offers(cpf, status);
-- Candidatos: consultas marcadas por especialidade e início.
CREATE INDEX IF NOT EXISTS appointments_scheduled_specialty_idx ON nora_unit.appointments(lower(btrim(specialty)), starts_at) WHERE status = 'scheduled';
