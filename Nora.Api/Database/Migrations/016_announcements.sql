-- Comunicados da Secretaria para equipes (OBS) e pacientes. O alcance é guardado como regra (toda a rede, regiões ou UBS) e avaliado na leitura,
-- então não há dado pessoal no comunicado. seq é o cursor incremental do aplicativo: a publicação toma um lock de transação antes do INSERT,
-- então a ordem de seq é a ordem de commit e um cursor nunca pula um comunicado que ainda ia ser confirmado.
CREATE TABLE IF NOT EXISTS nora.announcements (
    id uuid PRIMARY KEY,
    seq bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 120),
    body text NOT NULL CHECK (char_length(body) BETWEEN 1 AND 2000),
    audience text NOT NULL CHECK (audience IN ('staff', 'patients', 'both')),
    scope text NOT NULL CHECK (scope IN ('all', 'region', 'units')),
    created_by uuid NOT NULL REFERENCES nora.accounts(id),
    created_at timestamptz NOT NULL,
    expires_at timestamptz,
    closed_at timestamptz,
    closed_by uuid REFERENCES nora.accounts(id),
    CHECK ((closed_at IS NULL) = (closed_by IS NULL))
);
-- Comunicados em vigor (não encerrados): é o conjunto que as leituras do OBS e do app percorrem.
CREATE INDEX IF NOT EXISTS announcements_open_seq_idx ON nora.announcements(seq) WHERE closed_at IS NULL;
CREATE INDEX IF NOT EXISTS announcements_creator_idx ON nora.announcements(created_by, seq DESC);
-- Região é texto livre em health_units: a chave é lower(btrim(region)), calculada no próprio banco.
CREATE TABLE IF NOT EXISTS nora.announcement_regions (
    announcement_id uuid NOT NULL REFERENCES nora.announcements(id),
    region_key text NOT NULL CHECK (char_length(region_key) BETWEEN 1 AND 120),
    region_label text NOT NULL CHECK (char_length(region_label) BETWEEN 1 AND 120),
    PRIMARY KEY (announcement_id, region_key)
);
CREATE INDEX IF NOT EXISTS announcement_regions_key_idx ON nora.announcement_regions(region_key, announcement_id);
CREATE TABLE IF NOT EXISTS nora.announcement_units (
    announcement_id uuid NOT NULL REFERENCES nora.announcements(id),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    PRIMARY KEY (announcement_id, unit_id)
);
CREATE INDEX IF NOT EXISTS announcement_units_unit_idx ON nora.announcement_units(unit_id, announcement_id);
CREATE INDEX IF NOT EXISTS health_units_region_key_idx ON nora.health_units(lower(btrim(region)));
-- Confirmação de leitura por conta (equipe ou paciente), com o instante.
CREATE TABLE IF NOT EXISTS nora.announcement_reads (
    announcement_id uuid NOT NULL REFERENCES nora.announcements(id),
    account_id uuid NOT NULL REFERENCES nora.accounts(id),
    read_at timestamptz NOT NULL,
    PRIMARY KEY (announcement_id, account_id)
);
