-- Senha provisória com troca obrigatória, estado do banco de cada UBS e histórico de ações de conta.
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT false;
ALTER TABLE nora.health_units ADD COLUMN IF NOT EXISTS database_ready_at timestamptz;
CREATE TABLE IF NOT EXISTS nora.staff_account_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES nora.accounts(id),
    actor_id uuid NOT NULL REFERENCES nora.accounts(id),
    action text NOT NULL CHECK (action IN ('created', 'deactivated', 'reactivated', 'password_reset')),
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS staff_account_events_time_idx ON nora.staff_account_events(occurred_at DESC);
-- actor_id é nulo quando o banco foi marcado como pronto pelo comando de migração, sem sessão de usuário.
CREATE TABLE IF NOT EXISTS nora.unit_database_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    actor_id uuid REFERENCES nora.accounts(id),
    occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS unit_database_events_time_idx ON nora.unit_database_events(occurred_at DESC);
CREATE INDEX IF NOT EXISTS staff_unit_events_time_idx ON nora.staff_unit_events(occurred_at DESC);
CREATE INDEX IF NOT EXISTS unit_access_events_time_idx ON nora.unit_access_events(occurred_at DESC);
-- Eventos anteriores são vínculos; a remoção voluntária pelo paciente passa a registrar 'unlinked'.
ALTER TABLE nora.patient_unit_link_events ADD COLUMN IF NOT EXISTS action text NOT NULL DEFAULT 'linked';
ALTER TABLE nora.patient_unit_link_events DROP CONSTRAINT IF EXISTS patient_unit_link_events_action_check;
ALTER TABLE nora.patient_unit_link_events ADD CONSTRAINT patient_unit_link_events_action_check CHECK (action IN ('linked', 'unlinked'));
