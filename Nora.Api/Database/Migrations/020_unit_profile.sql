-- Atendimento ao público da UBS (horário de funcionamento por dia da semana e texto de serviços) e a chave da aprovação das vagas reaproveitadas.
-- O banco central guarda porque o app já lista as UBS por ele e o texto não é dado de paciente. weekday: 0 = domingo (DayOfWeek); horário no fuso Nora:TimeZone.
CREATE TABLE IF NOT EXISTS nora.unit_opening_hours (
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    weekday smallint NOT NULL CHECK (weekday BETWEEN 0 AND 6),
    opens_at time NOT NULL,
    closes_at time NOT NULL,
    CHECK (closes_at > opens_at),
    PRIMARY KEY (unit_id, weekday, opens_at)
);
ALTER TABLE nora.health_units ADD COLUMN IF NOT EXISTS services_text text;
-- profile_updated_at nulo = "não informado" (diferente de "fechado").
ALTER TABLE nora.health_units ADD COLUMN IF NOT EXISTS profile_updated_at timestamptz;
ALTER TABLE nora.health_units ADD COLUMN IF NOT EXISTS profile_updated_by uuid REFERENCES nora.accounts(id);
-- Aprovação das vagas reaproveitadas: nulo = desligada; ligada, vale para os cancelamentos feitos a partir deste instante.
ALTER TABLE nora.health_units ADD COLUMN IF NOT EXISTS slot_approval_since timestamptz;
ALTER TABLE nora.health_units ADD COLUMN IF NOT EXISTS slot_approval_updated_by uuid REFERENCES nora.accounts(id);
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'nora.health_units'::regclass AND conname = 'health_units_services_text_check') THEN
        ALTER TABLE nora.health_units ADD CONSTRAINT health_units_services_text_check CHECK (services_text IS NULL OR length(services_text) <= 1000);
    END IF;
END
$$;
