-- Vínculos explícitos: cadastros existentes não recebem acesso automático a nenhuma UBS.
CREATE TABLE IF NOT EXISTS nora.patient_units (
    patient_id uuid NOT NULL REFERENCES nora.accounts(id),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (patient_id, unit_id)
);
CREATE INDEX IF NOT EXISTS patient_units_unit_idx ON nora.patient_units(unit_id, patient_id);
