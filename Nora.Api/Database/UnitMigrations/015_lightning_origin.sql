-- A vaga relâmpago passa a ter dados próprios (especialidade, profissional, consultório e horário): a do "chamar próximo" não vem de consulta cancelada
-- (appointment_id nulo) e a da aprovação (origin 'cancellation') nasce de uma consulta cancelada, mas não segura o horário até ser aprovada.
-- Toda leitura do horário, da especialidade e do profissional de uma vaga usa estas colunas; a consulta de origem só serve para mostrar de onde ela veio.
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS origin text NOT NULL DEFAULT 'staff';
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS specialty text;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS specialty_id uuid;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS professional text;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS professional_id uuid;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS room_id uuid;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS starts_at timestamptz;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS ends_at timestamptz;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS decided_by uuid;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS decided_at timestamptz;
ALTER TABLE nora_unit.lightning_slots ADD COLUMN IF NOT EXISTS closed_reason text;
UPDATE nora_unit.lightning_slots s SET specialty = a.specialty, specialty_id = a.specialty_id, professional = a.professional, professional_id = a.professional_id, room_id = a.room_id, starts_at = a.starts_at, ends_at = a.ends_at
  FROM nora_unit.appointments a WHERE a.id = s.appointment_id AND s.starts_at IS NULL;
ALTER TABLE nora_unit.lightning_slots ALTER COLUMN specialty SET NOT NULL;
ALTER TABLE nora_unit.lightning_slots ALTER COLUMN professional SET NOT NULL;
ALTER TABLE nora_unit.lightning_slots ALTER COLUMN starts_at SET NOT NULL;
ALTER TABLE nora_unit.lightning_slots ALTER COLUMN ends_at SET NOT NULL;
-- A vaga do "chamar próximo" não tem consulta de origem; a proposta automática da aprovação não tem autor. appointment_id continua único.
ALTER TABLE nora_unit.lightning_slots ALTER COLUMN appointment_id DROP NOT NULL;
ALTER TABLE nora_unit.lightning_slots ALTER COLUMN created_by DROP NOT NULL;
SELECT nora_unit.ensure_check('nora_unit.lightning_slots', 'lightning_slots_status_check', 'status IN (''awaiting_approval'', ''open'', ''offered'', ''accepted'', ''closed'')', ARRAY['awaiting_approval', 'open', 'offered', 'accepted', 'closed']);
SELECT nora_unit.ensure_check('nora_unit.lightning_slots', 'lightning_slots_origin_check', 'origin IN (''staff'', ''cancellation'', ''call-next'')', ARRAY['staff', 'cancellation', 'call-next']);
SELECT nora_unit.ensure_check('nora_unit.lightning_slots', 'lightning_slots_closed_reason_check', 'closed_reason IS NULL OR closed_reason IN (''too-late'', ''approval-timeout'', ''discarded'', ''conflict'')', ARRAY['too-late', 'approval-timeout', 'discarded', 'conflict']);
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'nora_unit.lightning_slots'::regclass AND conname = 'lightning_slots_origin_appointment_check') THEN
        ALTER TABLE nora_unit.lightning_slots ADD CONSTRAINT lightning_slots_origin_appointment_check CHECK ((origin = 'call-next') = (appointment_id IS NULL));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'nora_unit.lightning_slots'::regclass AND conname = 'lightning_slots_interval_check') THEN
        ALTER TABLE nora_unit.lightning_slots ADD CONSTRAINT lightning_slots_interval_check CHECK (ends_at > starts_at);
    END IF;
END
$$;
-- Vagas que seguram o horário de um profissional (grade, marcação e aprovação) e propostas aguardando aprovação (fechamento por prazo).
CREATE INDEX IF NOT EXISTS lightning_slots_professional_time_idx ON nora_unit.lightning_slots(lower(btrim(professional)), starts_at) WHERE status IN ('open', 'offered');
CREATE INDEX IF NOT EXISTS lightning_slots_awaiting_idx ON nora_unit.lightning_slots(starts_at) WHERE status = 'awaiting_approval';
-- A proposta automática procura as consultas canceladas no futuro.
CREATE INDEX IF NOT EXISTS appointments_cancelled_future_idx ON nora_unit.appointments(starts_at) WHERE status = 'cancelled';
CREATE INDEX IF NOT EXISTS appointment_events_appointment_idx ON nora_unit.appointment_events(appointment_id, status);
