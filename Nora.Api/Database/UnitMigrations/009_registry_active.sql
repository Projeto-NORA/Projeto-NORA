ALTER TABLE nora_unit.patient_registry ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
