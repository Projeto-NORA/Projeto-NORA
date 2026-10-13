ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS phone varchar(16)
    CHECK (phone IS NULL OR phone ~ '^\+?[0-9]{8,15}$');
ALTER TABLE nora.sessions ADD COLUMN IF NOT EXISTS presence_seen_at timestamptz;
