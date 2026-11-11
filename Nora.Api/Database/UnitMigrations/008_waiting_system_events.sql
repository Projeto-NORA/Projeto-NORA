ALTER TABLE nora_unit.waiting_list_events ALTER COLUMN actor_id DROP NOT NULL;
ALTER TABLE nora_unit.waiting_list_events ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT 'staff';
ALTER TABLE nora_unit.waiting_list_events DROP CONSTRAINT IF EXISTS waiting_list_events_source_check;
ALTER TABLE nora_unit.waiting_list_events ADD CONSTRAINT waiting_list_events_source_check CHECK (source IN ('staff', 'system', 'patient'));
