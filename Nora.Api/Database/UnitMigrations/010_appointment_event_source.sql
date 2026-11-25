-- Origem de cada mudança de consulta: base do score de comparecimento (apenas cancelamentos 'patient' pesam).
-- Eventos anteriores permanecem 'staff' e não pesam no score.
ALTER TABLE nora_unit.appointment_events ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT 'staff';
-- A lista final (com 'rescheduled-lightning' da 018) fica também aqui: as migrações são reaplicadas em ordem e uma lista menor recriaria a restrição sobre linhas que já usam o valor novo.
SELECT nora_unit.ensure_check('nora_unit.appointment_events', 'appointment_events_source_check', 'source IN (''staff'', ''system'', ''patient'', ''rescheduled-lightning'')', ARRAY['staff', 'system', 'patient', 'rescheduled-lightning']);
CREATE INDEX IF NOT EXISTS appointment_events_appointment_idx ON nora_unit.appointment_events(appointment_id, status);
CREATE INDEX IF NOT EXISTS waiting_list_events_entry_idx ON nora_unit.waiting_list_events(entry_id, status);
CREATE INDEX IF NOT EXISTS lightning_offers_entry_idx ON nora_unit.lightning_offers(entry_id, status);
CREATE INDEX IF NOT EXISTS lightning_offers_slot_idx ON nora_unit.lightning_offers(slot_id, status);
CREATE INDEX IF NOT EXISTS lightning_slots_created_idx ON nora_unit.lightning_slots(created_at);
