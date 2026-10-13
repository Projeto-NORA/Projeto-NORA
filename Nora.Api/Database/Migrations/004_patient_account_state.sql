ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
