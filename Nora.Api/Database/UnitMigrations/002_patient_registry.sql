CREATE TABLE IF NOT EXISTS nora_unit.patient_registry (
    cpf varchar(11) PRIMARY KEY CHECK (cpf ~ '^[0-9]{11}$'),
    full_name text NOT NULL CHECK (length(full_name) BETWEEN 2 AND 160),
    email varchar(254) NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS nora_unit.patient_registry_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    cpf varchar(11) NOT NULL REFERENCES nora_unit.patient_registry(cpf),
    actor_id uuid NOT NULL,
    action text NOT NULL CHECK (action IN ('created')),
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
