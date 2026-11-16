CREATE SCHEMA IF NOT EXISTS nora_unit;
CREATE TABLE IF NOT EXISTS nora_unit.identity (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    unit_id uuid NOT NULL UNIQUE
);
-- Garante uma restrição CHECK pelo nome sem trabalho à toa quando as migrações são reaplicadas: se ela já existe com os mesmos
-- valores literais e está validada, não faz nada; se difere, é recriada; sobre tabela com dados é adicionada NOT VALID e depois
-- validada (VALIDATE CONSTRAINT não bloqueia leituras nem escritas). Os literais são comparados, e não o texto de
-- pg_get_constraintdef, que muda de formatação entre versões do PostgreSQL.
CREATE OR REPLACE FUNCTION nora_unit.ensure_check(target regclass, constraint_name text, expression text, literals text[]) RETURNS void LANGUAGE plpgsql AS $fn$
DECLARE
    existing oid;
    is_valid boolean;
    current_literals text[];
BEGIN
    SELECT c.oid, c.convalidated INTO existing, is_valid FROM pg_constraint c WHERE c.conrelid = target AND c.conname = constraint_name;
    IF existing IS NOT NULL THEN
        SELECT COALESCE(array_agg(t.m[1] ORDER BY t.m[1]), ARRAY[]::text[]) INTO current_literals FROM regexp_matches(pg_get_constraintdef(existing), '''([^'']*)''', 'g') AS t(m);
        IF current_literals = (SELECT COALESCE(array_agg(l ORDER BY l), ARRAY[]::text[]) FROM unnest(literals) AS l) THEN
            IF NOT is_valid THEN
                EXECUTE format('ALTER TABLE %s VALIDATE CONSTRAINT %I', target, constraint_name);
            END IF;
            RETURN;
        END IF;
        EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', target, constraint_name);
    END IF;
    EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I CHECK (%s) NOT VALID', target, constraint_name, expression);
    EXECUTE format('ALTER TABLE %s VALIDATE CONSTRAINT %I', target, constraint_name);
END
$fn$;
