-- Rode no SQL Editor do Neon, no banco central selecionado para o Render.
-- As contagens ficam nulas enquanto as tabelas correspondentes não existirem.
SELECT
    current_database() AS banco,
    to_regclass('nora.accounts') IS NOT NULL AS tabela_de_contas,
    CASE WHEN to_regclass('nora.accounts') IS NOT NULL THEN
        (xpath('/row/total/text()', query_to_xml(
            'SELECT count(*) AS total FROM nora.accounts WHERE scope = ''municipal'' AND is_active',
            false, true, ''
        )))[1]::text::bigint
    END AS contas_municipais_ativas,
    CASE WHEN to_regclass('nora.accounts') IS NOT NULL THEN
        (xpath('/row/total/text()', query_to_xml(
            'SELECT count(*) AS total FROM nora.accounts WHERE scope = ''obs'' AND is_active AND role = ''administrator''',
            false, true, ''
        )))[1]::text::bigint
    END AS administradores_obs_ativos,
    CASE WHEN to_regclass('nora.health_units') IS NOT NULL THEN
        (xpath('/row/total/text()', query_to_xml(
            'SELECT count(*) AS total FROM nora.health_units',
            false, true, ''
        )))[1]::text::bigint
    END AS unidades,
    CASE WHEN to_regclass('nora.specialties') IS NOT NULL THEN
        (xpath('/row/total/text()', query_to_xml(
            'SELECT count(*) AS total FROM nora.specialties WHERE is_active',
            false, true, ''
        )))[1]::text::bigint
    END AS especialidades_ativas;
