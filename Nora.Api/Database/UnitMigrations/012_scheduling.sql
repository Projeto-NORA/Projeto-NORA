-- Agenda da UBS: consultórios, profissionais, grade semanal, bloqueios e novos campos da consulta.
-- specialty_id aponta para a lista central (nora.specialties); não há chave estrangeira porque os bancos são separados.
CREATE TABLE IF NOT EXISTS nora_unit.rooms (
    id uuid PRIMARY KEY,
    name text NOT NULL CHECK (length(name) BETWEEN 1 AND 80),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS rooms_name_idx ON nora_unit.rooms(lower(btrim(name)));
CREATE TABLE IF NOT EXISTS nora_unit.professionals (
    id uuid PRIMARY KEY,
    full_name text NOT NULL CHECK (length(full_name) BETWEEN 2 AND 160),
    council_acronym text,
    council_number text,
    council_state text,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK ((council_acronym IS NULL AND council_number IS NULL AND council_state IS NULL) OR (council_acronym IS NOT NULL AND council_number IS NOT NULL AND council_state IS NOT NULL))
);
-- O nome identifica o profissional nas consultas antigas e nas vagas relâmpago (texto): por isso é único na UBS.
CREATE UNIQUE INDEX IF NOT EXISTS professionals_name_idx ON nora_unit.professionals(lower(btrim(full_name)));
CREATE TABLE IF NOT EXISTS nora_unit.professional_specialties (
    professional_id uuid NOT NULL REFERENCES nora_unit.professionals(id),
    specialty_id uuid NOT NULL,
    PRIMARY KEY (professional_id, specialty_id)
);
-- weekday: 0 = domingo ... 6 = sábado (DayOfWeek). Horários no fuso Nora:TimeZone.
CREATE TABLE IF NOT EXISTS nora_unit.schedule_templates (
    id uuid PRIMARY KEY,
    professional_id uuid NOT NULL REFERENCES nora_unit.professionals(id),
    weekday smallint NOT NULL CHECK (weekday BETWEEN 0 AND 6),
    start_time time NOT NULL,
    end_time time NOT NULL,
    slot_minutes integer NOT NULL CHECK (slot_minutes BETWEEN 5 AND 240),
    room_id uuid NOT NULL REFERENCES nora_unit.rooms(id),
    specialty_id uuid NOT NULL,
    valid_from date,
    valid_until date,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (end_time > start_time),
    CHECK (valid_from IS NULL OR valid_until IS NULL OR valid_until >= valid_from)
);
CREATE INDEX IF NOT EXISTS schedule_templates_professional_idx ON nora_unit.schedule_templates(professional_id, weekday);
CREATE INDEX IF NOT EXISTS schedule_templates_room_idx ON nora_unit.schedule_templates(room_id, weekday);
-- professional_id nulo = bloqueio da UBS inteira (feriado, por exemplo).
CREATE TABLE IF NOT EXISTS nora_unit.schedule_blocks (
    id uuid PRIMARY KEY,
    professional_id uuid REFERENCES nora_unit.professionals(id),
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NOT NULL,
    reason text NOT NULL CHECK (length(reason) BETWEEN 1 AND 120),
    created_by uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (ends_at > starts_at)
);
CREATE INDEX IF NOT EXISTS schedule_blocks_time_idx ON nora_unit.schedule_blocks(starts_at, ends_at);
-- Consulta: quem marcou (staff, patient, lightning), confirmação do paciente e vínculo opcional com o catálogo.
ALTER TABLE nora_unit.appointments ADD COLUMN IF NOT EXISTS professional_id uuid;
ALTER TABLE nora_unit.appointments ADD COLUMN IF NOT EXISTS specialty_id uuid;
ALTER TABLE nora_unit.appointments ADD COLUMN IF NOT EXISTS room_id uuid;
ALTER TABLE nora_unit.appointments ADD COLUMN IF NOT EXISTS booked_by text NOT NULL DEFAULT 'staff';
ALTER TABLE nora_unit.appointments ADD COLUMN IF NOT EXISTS confirmed_at timestamptz;
SELECT nora_unit.ensure_check('nora_unit.appointments', 'appointments_booked_by_check', 'booked_by IN (''staff'', ''patient'', ''lightning'')', ARRAY['staff', 'patient', 'lightning']);
-- Limite de uma consulta futura por especialidade marcada pelo app: a conta olha só as marcadas pelo paciente.
CREATE INDEX IF NOT EXISTS appointments_patient_booking_idx ON nora_unit.appointments(cpf, specialty_id, starts_at) WHERE booked_by = 'patient';
-- Consultas ativas de um profissional (grade, disponibilidade, capacidade).
CREATE INDEX IF NOT EXISTS appointments_professional_time_idx ON nora_unit.appointments(lower(professional), starts_at);
