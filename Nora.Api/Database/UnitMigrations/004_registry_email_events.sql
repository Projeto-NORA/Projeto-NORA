ALTER TABLE nora_unit.patient_registry_events DROP CONSTRAINT patient_registry_events_action_check;
ALTER TABLE nora_unit.patient_registry_events ADD CONSTRAINT patient_registry_events_action_check CHECK (action IN ('created', 'updated', 'email_updated', 'deactivated', 'reactivated'));
