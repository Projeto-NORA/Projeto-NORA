CREATE TABLE IF NOT EXISTS nora.patient_registration_challenges (
    id uuid PRIMARY KEY,
    cpf varchar(11) NOT NULL CHECK (cpf ~ '^[0-9]{11}$'),
    full_name text NOT NULL,
    password_hash text NOT NULL,
    unit_ids uuid[] NOT NULL,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    selected_email_hash varchar(64),
    code_hash varchar(64),
    code_expires_at timestamptz,
    sends smallint NOT NULL DEFAULT 0 CHECK (sends BETWEEN 0 AND 3),
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 5),
    CHECK (expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS registration_cpf_created_idx ON nora.patient_registration_challenges(cpf, created_at);
