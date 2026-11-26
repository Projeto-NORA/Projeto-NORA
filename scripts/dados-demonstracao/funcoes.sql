-- Funções auxiliares dos dados de demonstração (temporárias: somem quando a conexão fecha; nada fica gravado no banco).
-- Carregado pelos outros arquivos desta pasta com \i, sempre dentro da transação do script.

-- Número "aleatório" estável entre 0 e 1 a partir de um texto: a mesma semente dá sempre o mesmo valor (é isso que torna a repetição idempotente).
CREATE OR REPLACE FUNCTION pg_temp.u(seed text) RETURNS double precision LANGUAGE sql IMMUTABLE AS
$$ SELECT (('x' || substr(md5(seed), 1, 8))::bit(32)::bigint)::double precision / 4294967296.0 $$;

-- Identificador fictício estável: todo id criado pelo script começa com de0de0de- (é por isso que o -Remover o reconhece).
CREATE OR REPLACE FUNCTION pg_temp.did(seed text) RETURNS uuid LANGUAGE sql IMMUTABLE AS
$$ SELECT ('de0de0de-' || substr(h, 1, 4) || '-4' || substr(h, 5, 3) || '-8' || substr(h, 8, 3) || '-' || substr(h, 11, 12))::uuid FROM (SELECT md5(seed) AS h) x $$;

-- Este id foi criado pelo script?
CREATE OR REPLACE FUNCTION pg_temp.is_demo(id uuid) RETURNS boolean LANGUAGE sql IMMUTABLE AS
$$ SELECT id IS NOT NULL AND id::text LIKE 'de0de0de-%' $$;

-- Contagem de linhas de todas as tabelas do NORA no banco (central: schema nora; UBS: schema nora_unit).
CREATE OR REPLACE FUNCTION pg_temp.contar() RETURNS TABLE(t text, n bigint) LANGUAGE sql AS
$$ SELECT table_schema || '.' || table_name,
          (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM %I.%I', table_schema, table_name), false, true, '')))[1]::text::bigint
   FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema IN ('nora', 'nora_unit') $$;

-- Ator fixo dos registros feitos "pela equipe" fictícia (não é conta de ninguém).
CREATE OR REPLACE FUNCTION pg_temp.demo_actor() RETURNS uuid LANGUAGE sql IMMUTABLE AS
$$ SELECT 'de0de0de-0000-4000-8000-000000000001'::uuid $$;

-- Fecha uma execução: confere que nenhuma tabela perdeu linhas (compara com _antes, criada no começo). Quem lê o resultado
-- é o PowerShell, pelas linhas "T|tabela|antes|depois" do SELECT que vem depois (ver ubs.sql). Se alguma tabela perdeu linhas,
-- a transação inteira é desfeita. As tabelas listadas em permitir_perda são as que o -Remover apaga de propósito.
CREATE OR REPLACE FUNCTION pg_temp.conferir(permitir_perda text[] DEFAULT ARRAY[]::text[]) RETURNS void LANGUAGE plpgsql AS
$$
DECLARE r record;
BEGIN
    CREATE TEMP TABLE _depois ON COMMIT DROP AS SELECT * FROM pg_temp.contar();
    FOR r IN SELECT a.t, a.n AS antes, COALESCE(d.n, -1) AS depois FROM _antes a LEFT JOIN _depois d ON d.t = a.t WHERE COALESCE(d.n, -1) < a.n AND NOT (a.t = ANY (permitir_perda)) LOOP
        RAISE EXCEPTION 'A tabela % perdeu linhas (% antes, % depois). Nada foi gravado.', r.t, r.antes, r.depois;
    END LOOP;
END
$$;
