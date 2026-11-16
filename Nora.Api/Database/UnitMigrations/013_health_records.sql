-- Jornada do paciente: exames e vacinas registrados pela equipe da UBS. Nada é apagado: a correção de um registro errado é a anulação
-- ('voided', com motivo). Cada escrita grava autor e instante em health_record_events.
CREATE TABLE IF NOT EXISTS nora_unit.health_exams (
    id uuid PRIMARY KEY,
    cpf varchar(11) NOT NULL REFERENCES nora_unit.patient_registry(cpf),
    name text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 160),
    status text NOT NULL CHECK (status IN ('requested', 'done', 'cancelled', 'voided')),
    requested_at timestamptz NOT NULL,
    performed_on date,
    note text CHECK (note IS NULL OR char_length(note) BETWEEN 1 AND 280),
    void_reason text CHECK (void_reason IS NULL OR char_length(void_reason) BETWEEN 3 AND 280),
    voided_at timestamptz,
    created_by uuid NOT NULL,
    CHECK (status <> 'done' OR performed_on IS NOT NULL),
    CHECK (status <> 'voided' OR (void_reason IS NOT NULL AND voided_at IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS health_exams_patient_idx ON nora_unit.health_exams(cpf, requested_at DESC);
CREATE TABLE IF NOT EXISTS nora_unit.health_vaccines (
    id uuid PRIMARY KEY,
    cpf varchar(11) NOT NULL REFERENCES nora_unit.patient_registry(cpf),
    name text NOT NULL CHECK (char_length(name) BETWEEN 1 AND 160),
    dose text NOT NULL CHECK (dose IN ('1', '2', '3', 'booster', 'single')),
    applied_on date NOT NULL,
    status text NOT NULL CHECK (status IN ('applied', 'voided')),
    note text CHECK (note IS NULL OR char_length(note) BETWEEN 1 AND 280),
    void_reason text CHECK (void_reason IS NULL OR char_length(void_reason) BETWEEN 3 AND 280),
    voided_at timestamptz,
    created_by uuid NOT NULL,
    created_at timestamptz NOT NULL,
    CHECK (status <> 'voided' OR (void_reason IS NOT NULL AND voided_at IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS health_vaccines_patient_idx ON nora_unit.health_vaccines(cpf, applied_on DESC);
CREATE TABLE IF NOT EXISTS nora_unit.health_record_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    kind text NOT NULL CHECK (kind IN ('exam', 'vaccine')),
    record_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    action text NOT NULL CHECK (action IN ('created', 'done', 'cancelled', 'voided')),
    occurred_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS health_record_events_record_idx ON nora_unit.health_record_events(kind, record_id, id);
