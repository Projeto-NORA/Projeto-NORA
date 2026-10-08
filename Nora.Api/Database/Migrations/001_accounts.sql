CREATE SCHEMA IF NOT EXISTS nora;
CREATE TABLE IF NOT EXISTS nora.accounts (
    id uuid PRIMARY KEY,
    full_name text NOT NULL CHECK (length(full_name) BETWEEN 2 AND 160),
    cpf varchar(11) NOT NULL CHECK (cpf ~ '^[0-9]{11}$'),
    email varchar(254) NOT NULL,
    password_hash text NOT NULL,
    scope text NOT NULL CHECK (scope IN ('obs','municipal','patient')),
    role text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK ((scope='obs' AND role IN ('administrator','attendant')) OR (scope='municipal' AND role='manager') OR (scope='patient' AND role='patient')),
    UNIQUE(scope, cpf),
    UNIQUE(scope, email)
);
CREATE TABLE IF NOT EXISTS nora.sessions (
    token_hash varchar(64) PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES nora.accounts(id),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS sessions_account_idx ON nora.sessions(account_id);
