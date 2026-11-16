-- Lista única de especialidades da rede (mantida pela Secretaria; nunca excluída, só desativada) e metas dos indicadores.
-- name_key é o nome sem acentos, em minúsculas e sem espaços sobrando (calculado pela API): garante nome único sem diferenciar maiúsculas/acentos.
CREATE TABLE IF NOT EXISTS nora.specialties (
    id uuid PRIMARY KEY,
    name text NOT NULL CHECK (length(name) BETWEEN 2 AND 120),
    name_key text NOT NULL CHECK (length(name_key) BETWEEN 2 AND 120),
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by uuid REFERENCES nora.accounts(id)
);
CREATE UNIQUE INDEX IF NOT EXISTS specialties_name_key_idx ON nora.specialties(name_key);
-- Meta da rede (unit_id nulo) ou sobrescrita de uma UBS. attendance_score e missed_rate: 0 a 1; average_wait_minutes: positivo.
CREATE TABLE IF NOT EXISTS nora.goals (
    id uuid PRIMARY KEY,
    metric text NOT NULL CHECK (metric IN ('attendance_score', 'missed_rate', 'average_wait_minutes')),
    unit_id uuid REFERENCES nora.health_units(id),
    target numeric(12, 4) NOT NULL CHECK (target >= 0),
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by uuid REFERENCES nora.accounts(id)
);
CREATE UNIQUE INDEX IF NOT EXISTS goals_network_idx ON nora.goals(metric) WHERE unit_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS goals_unit_idx ON nora.goals(metric, unit_id) WHERE unit_id IS NOT NULL;
