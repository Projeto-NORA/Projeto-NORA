-- A exclusão automática começa desligada. Contas antigas podem ter histórico anterior aos registros disponíveis.
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS retention_protected boolean NOT NULL DEFAULT true;
ALTER TABLE nora.accounts ALTER COLUMN retention_protected SET DEFAULT false;
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS inactive_since timestamptz;
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS retention_at timestamptz;
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS retention_cancelled boolean NOT NULL DEFAULT false;
ALTER TABLE nora.staff_account_events ALTER COLUMN account_id DROP NOT NULL;
ALTER TABLE nora.staff_account_events DROP CONSTRAINT IF EXISTS staff_account_events_account_id_fkey;
ALTER TABLE nora.staff_account_events ADD CONSTRAINT staff_account_events_account_id_fkey FOREIGN KEY (account_id) REFERENCES nora.accounts(id) ON DELETE SET NULL;

CREATE TABLE IF NOT EXISTS nora.staff_retention_policy (
    singleton boolean PRIMARY KEY CHECK (singleton),
    days integer CHECK (days BETWEEN 1 AND 36500),
    effective_at timestamptz,
    CHECK ((days IS NULL) = (effective_at IS NULL))
);
INSERT INTO nora.staff_retention_policy(singleton) VALUES (true) ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS nora.staff_retention_events (
    item_id uuid PRIMARY KEY,
    removed_at timestamptz NOT NULL
);

CREATE OR REPLACE FUNCTION nora.update_staff_retention_state() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.scope = 'obs' AND NEW.is_active IS DISTINCT FROM OLD.is_active THEN
        NEW.inactive_since := CASE WHEN NEW.is_active THEN NULL ELSE statement_timestamp() END;
        NEW.retention_at := NULL;
        NEW.retention_cancelled := false;
    END IF;
    RETURN NEW;
END
$$;
DROP TRIGGER IF EXISTS staff_retention_state ON nora.accounts;
CREATE TRIGGER staff_retention_state BEFORE UPDATE OF is_active ON nora.accounts
FOR EACH ROW EXECUTE FUNCTION nora.update_staff_retention_state();

CREATE OR REPLACE FUNCTION nora.protect_staff_with_login() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    UPDATE nora.accounts SET retention_protected=true
    WHERE id=NEW.account_id AND scope='obs' AND NOT retention_protected;
    RETURN NEW;
END
$$;
DROP TRIGGER IF EXISTS protect_staff_with_login ON nora.sessions;
CREATE TRIGGER protect_staff_with_login AFTER INSERT ON nora.sessions
FOR EACH ROW EXECUTE FUNCTION nora.protect_staff_with_login();
