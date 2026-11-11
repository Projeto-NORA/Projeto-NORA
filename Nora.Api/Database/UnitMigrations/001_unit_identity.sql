CREATE SCHEMA IF NOT EXISTS nora_unit;
CREATE TABLE IF NOT EXISTS nora_unit.identity (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    unit_id uuid NOT NULL UNIQUE
);
