-- A concessão explícita nunca cria acesso automático às contas existentes.
CREATE TABLE IF NOT EXISTS nora.staff_units (
    account_id uuid NOT NULL REFERENCES nora.accounts(id),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    granted_by uuid NOT NULL REFERENCES nora.accounts(id),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (account_id, unit_id)
);
CREATE INDEX IF NOT EXISTS staff_units_unit_idx ON nora.staff_units(unit_id, account_id);
CREATE TABLE IF NOT EXISTS nora.staff_unit_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES nora.accounts(id),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    actor_id uuid NOT NULL REFERENCES nora.accounts(id),
    action text NOT NULL CHECK (action IN ('granted','revoked')),
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
