-- Computadores do OBS (P-014). O administrador da UBS ativa e revoga computadores; só a atendente precisa de um computador ativado.
-- O segredo do computador fica só no próprio computador; aqui só o hash (SHA-256 hex). O código de ativação (uso único, 15 minutos) também só existe como hash.
CREATE TABLE IF NOT EXISTS nora.obs_devices (
    id uuid PRIMARY KEY,
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    name text NOT NULL CHECK (length(name) BETWEEN 1 AND 60),
    status text NOT NULL CHECK (status IN ('pending', 'active', 'revoked')),
    secret_hash varchar(64),
    code_hash varchar(64),
    code_expires_at timestamptz,
    created_by uuid NOT NULL REFERENCES nora.accounts(id),
    created_at timestamptz NOT NULL,
    activated_at timestamptz,
    last_login_at timestamptz,
    revoked_by uuid REFERENCES nora.accounts(id),
    revoked_at timestamptz,
    CHECK ((status = 'active') = (secret_hash IS NOT NULL)),
    CHECK ((code_hash IS NULL) = (code_expires_at IS NULL)),
    CHECK (status <> 'revoked' OR (revoked_at IS NOT NULL AND code_hash IS NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS obs_devices_secret_uidx ON nora.obs_devices(secret_hash) WHERE secret_hash IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS obs_devices_code_uidx ON nora.obs_devices(code_hash) WHERE code_hash IS NOT NULL;
CREATE INDEX IF NOT EXISTS obs_devices_unit_idx ON nora.obs_devices(unit_id, created_at);
-- Códigos vencidos são limpos em lotes pela manutenção.
CREATE INDEX IF NOT EXISTS obs_devices_code_expires_idx ON nora.obs_devices(code_expires_at) WHERE code_expires_at IS NOT NULL;
ALTER TABLE nora.sessions ADD COLUMN IF NOT EXISTS device_id uuid REFERENCES nora.obs_devices(id);
CREATE INDEX IF NOT EXISTS sessions_device_open_idx ON nora.sessions(device_id) WHERE device_id IS NOT NULL AND revoked_at IS NULL;
-- actor_id é nulo na ativação feita pelo próprio computador com o código.
CREATE TABLE IF NOT EXISTS nora.obs_device_events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    device_id uuid NOT NULL REFERENCES nora.obs_devices(id),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    actor_id uuid REFERENCES nora.accounts(id),
    action text NOT NULL CHECK (action IN ('created', 'code-issued', 'activated', 'revoked')),
    occurred_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS obs_device_events_unit_time_idx ON nora.obs_device_events(unit_id, occurred_at);
