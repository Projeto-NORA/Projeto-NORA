CREATE TABLE IF NOT EXISTS nora.unit_access_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    actor_id uuid NOT NULL REFERENCES nora.accounts(id),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    action text NOT NULL CHECK (action IN ('registry_read')),
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS unit_access_events_unit_idx ON nora.unit_access_events(unit_id, occurred_at);
