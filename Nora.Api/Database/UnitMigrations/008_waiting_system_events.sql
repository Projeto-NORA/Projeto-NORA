ALTER TABLE nora_unit.waiting_list_events ALTER COLUMN actor_id DROP NOT NULL;
ALTER TABLE nora_unit.waiting_list_events ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT 'staff';
SELECT nora_unit.ensure_check('nora_unit.waiting_list_events', 'waiting_list_events_source_check', 'source IN (''staff'', ''system'', ''patient'')', ARRAY['staff', 'system', 'patient']);
