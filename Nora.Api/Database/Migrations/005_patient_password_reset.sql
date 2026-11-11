CREATE TABLE IF NOT EXISTS nora.patient_password_reset_codes (
    challenge_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES nora.accounts(id) ON DELETE CASCADE,
    code_hash varchar(64) NOT NULL,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 5),
    consumed_at timestamptz,
    CHECK (expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS patient_password_reset_account_created_idx
    ON nora.patient_password_reset_codes(account_id, created_at DESC);
