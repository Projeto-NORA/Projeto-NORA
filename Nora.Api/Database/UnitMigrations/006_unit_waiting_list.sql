CREATE TABLE IF NOT EXISTS nora_unit.waiting_list (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    cpf varchar(11) NOT NULL REFERENCES nora_unit.patient_registry(cpf),
    specialty text NOT NULL CHECK (length(specialty) BETWEEN 1 AND 120),
    entered_at timestamptz NOT NULL,
    status text NOT NULL CHECK (status IN ('waiting', 'offered', 'completed', 'cancelled')),
    created_by uuid NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS waiting_list_active_patient_specialty_idx
    ON nora_unit.waiting_list(cpf, lower(specialty)) WHERE status IN ('waiting', 'offered');
CREATE INDEX IF NOT EXISTS waiting_list_fifo_idx ON nora_unit.waiting_list(lower(specialty), entered_at, id) WHERE status = 'waiting';
CREATE TABLE IF NOT EXISTS nora_unit.waiting_list_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    entry_id bigint NOT NULL REFERENCES nora_unit.waiting_list(id),
    actor_id uuid NOT NULL,
    status text NOT NULL CHECK (status IN ('waiting', 'offered', 'completed', 'cancelled')),
    occurred_at timestamptz NOT NULL
);
