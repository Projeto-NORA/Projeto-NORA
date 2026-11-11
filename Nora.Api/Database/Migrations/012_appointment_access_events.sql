ALTER TABLE nora.unit_access_events DROP CONSTRAINT unit_access_events_action_check;
ALTER TABLE nora.unit_access_events ADD CONSTRAINT unit_access_events_action_check CHECK (action IN ('registry_read', 'appointment_read'));
