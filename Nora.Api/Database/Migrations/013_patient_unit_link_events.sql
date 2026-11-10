CREATE TABLE IF NOT EXISTS nora.patient_unit_link_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    patient_id uuid NOT NULL REFERENCES nora.accounts(id),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
