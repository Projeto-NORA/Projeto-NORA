-- Carimbo da versão do schema da UBS (maior número de migração aplicado). Quem grava é UnitMigrationDao ao fim de cada migração;
-- a API compara com o esperado para recusar bancos desatualizados com 503 unit-database-outdated em vez de erro de SQL.
CREATE TABLE IF NOT EXISTS nora_unit.schema_version (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    version integer NOT NULL
);
-- Espera média: parte dos eventos de conclusão da fila dentro do período, em vez de varrer a fila inteira.
CREATE INDEX IF NOT EXISTS waiting_list_events_status_time_idx ON nora_unit.waiting_list_events(status, occurred_at);
