CREATE TABLE IF NOT EXISTS nora_unit.lightning_slots (
    id uuid PRIMARY KEY,
    appointment_id uuid NOT NULL UNIQUE REFERENCES nora_unit.appointments(id),
    status text NOT NULL CHECK (status IN ('open', 'offered', 'accepted', 'closed')),
    created_at timestamptz NOT NULL,
    created_by uuid NOT NULL
);
CREATE TABLE IF NOT EXISTS nora_unit.lightning_offers (
    id uuid PRIMARY KEY,
    slot_id uuid NOT NULL REFERENCES nora_unit.lightning_slots(id),
    entry_id bigint NOT NULL REFERENCES nora_unit.waiting_list(id),
    expires_at timestamptz NOT NULL,
    status text NOT NULL CHECK (status IN ('pending', 'accepted', 'declined', 'expired', 'cancelled'))
);
CREATE UNIQUE INDEX IF NOT EXISTS lightning_pending_slot_idx ON nora_unit.lightning_offers(slot_id) WHERE status='pending';
CREATE UNIQUE INDEX IF NOT EXISTS lightning_pending_entry_idx ON nora_unit.lightning_offers(entry_id) WHERE status='pending';
