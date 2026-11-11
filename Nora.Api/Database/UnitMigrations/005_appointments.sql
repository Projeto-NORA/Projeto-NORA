CREATE TABLE IF NOT EXISTS nora_unit.appointments (
    id uuid PRIMARY KEY,
    cpf varchar(11) NOT NULL REFERENCES nora_unit.patient_registry(cpf),
    specialty text NOT NULL,
    professional text NOT NULL,
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NOT NULL CHECK (ends_at > starts_at),
    status text NOT NULL CHECK (status IN ('scheduled', 'called', 'completed', 'missed', 'cancelled')),
    created_by uuid NOT NULL,
    created_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS appointments_time_idx ON nora_unit.appointments(starts_at, id);
CREATE INDEX IF NOT EXISTS appointments_patient_idx ON nora_unit.appointments(cpf, starts_at);
CREATE TABLE IF NOT EXISTS nora_unit.appointment_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    appointment_id uuid NOT NULL REFERENCES nora_unit.appointments(id),
    actor_id uuid NOT NULL,
    status text NOT NULL CHECK (status IN ('scheduled', 'called', 'completed', 'missed', 'cancelled')),
    occurred_at timestamptz NOT NULL
);
