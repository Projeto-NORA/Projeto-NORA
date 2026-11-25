ALTER TABLE nora_unit.rooms ADD COLUMN IF NOT EXISTS inactive_since timestamptz;
ALTER TABLE nora_unit.rooms ADD COLUMN IF NOT EXISTS retention_at timestamptz;
ALTER TABLE nora_unit.rooms ADD COLUMN IF NOT EXISTS retention_cancelled boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS nora_unit.room_retention_policy (
    singleton boolean PRIMARY KEY CHECK (singleton),
    days integer CHECK (days BETWEEN 1 AND 36500),
    effective_at timestamptz,
    CHECK ((days IS NULL) = (effective_at IS NULL))
);
INSERT INTO nora_unit.room_retention_policy(singleton) VALUES (true) ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS nora_unit.room_retention_events (
    item_id uuid PRIMARY KEY,
    removed_at timestamptz NOT NULL
);

CREATE OR REPLACE FUNCTION nora_unit.update_room_retention_state() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.is_active IS DISTINCT FROM OLD.is_active THEN
        NEW.inactive_since := CASE WHEN NEW.is_active THEN NULL ELSE statement_timestamp() END;
        NEW.retention_at := NULL;
        NEW.retention_cancelled := false;
    END IF;
    RETURN NEW;
END
$$;
DROP TRIGGER IF EXISTS room_retention_state ON nora_unit.rooms;
CREATE TRIGGER room_retention_state BEFORE UPDATE OF is_active ON nora_unit.rooms
FOR EACH ROW EXECUTE FUNCTION nora_unit.update_room_retention_state();

-- Protege contra um vínculo criado simultaneamente à exclusão, preservando dados legados.
ALTER TABLE nora_unit.appointments DROP CONSTRAINT IF EXISTS appointments_catalog_room_fkey;
ALTER TABLE nora_unit.appointments ADD CONSTRAINT appointments_catalog_room_fkey FOREIGN KEY (room_id) REFERENCES nora_unit.rooms(id) NOT VALID;
ALTER TABLE nora_unit.lightning_slots DROP CONSTRAINT IF EXISTS lightning_catalog_room_fkey;
ALTER TABLE nora_unit.lightning_slots ADD CONSTRAINT lightning_catalog_room_fkey FOREIGN KEY (room_id) REFERENCES nora_unit.rooms(id) NOT VALID;
