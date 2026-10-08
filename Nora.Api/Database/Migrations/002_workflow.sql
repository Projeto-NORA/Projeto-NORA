CREATE TABLE IF NOT EXISTS nora.queue_entries (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    patient_id uuid NOT NULL REFERENCES nora.accounts(id),
    entered_at timestamptz NOT NULL,
    status text NOT NULL CHECK (status IN ('waiting','offered','completed','cancelled'))
);
CREATE UNIQUE INDEX IF NOT EXISTS queue_active_patient_idx ON nora.queue_entries(patient_id) WHERE status IN ('waiting','offered');
CREATE INDEX IF NOT EXISTS queue_order_idx ON nora.queue_entries(entered_at,id) WHERE status='waiting';
CREATE TABLE IF NOT EXISTS nora.slots (
    id uuid PRIMARY KEY,
    label text NOT NULL CHECK (length(label) BETWEEN 1 AND 160),
    status text NOT NULL CHECK (status IN ('open','offered','accepted')),
    created_at timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS nora.offers (
    id uuid PRIMARY KEY,
    slot_id uuid NOT NULL REFERENCES nora.slots(id),
    queue_id bigint NOT NULL REFERENCES nora.queue_entries(id),
    patient_id uuid NOT NULL REFERENCES nora.accounts(id),
    expires_at timestamptz NOT NULL,
    status text NOT NULL CHECK (status IN ('pending','accepted','declined','expired','cancelled'))
);
CREATE UNIQUE INDEX IF NOT EXISTS offer_pending_slot_idx ON nora.offers(slot_id) WHERE status='pending';
CREATE UNIQUE INDEX IF NOT EXISTS offer_pending_patient_idx ON nora.offers(patient_id) WHERE status='pending';
CREATE INDEX IF NOT EXISTS offers_expiration_idx ON nora.offers(expires_at) WHERE status='pending';
