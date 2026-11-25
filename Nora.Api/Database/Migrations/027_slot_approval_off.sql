-- Vaga Relâmpago automática (2026-11-24): a chave de aprovação das vagas foi removida. Desliga para todas as UBS (a coluna fica, sempre nula).
UPDATE nora.health_units SET slot_approval_since = NULL WHERE slot_approval_since IS NOT NULL;
