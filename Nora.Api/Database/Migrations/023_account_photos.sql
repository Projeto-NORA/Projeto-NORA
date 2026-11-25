-- Foto de perfil de qualquer conta (paciente, equipe do OBS e Secretaria): uma por conta, no banco central, com o binário em bytea (até 2 MB).
-- etag = SHA-256 do conteúdo em hexadecimal (o cabeçalho ETag o devolve entre aspas). Desativar a conta não apaga a foto (a reativação a recupera);
-- se a linha da conta for apagada, a foto vai junto (ON DELETE CASCADE).
CREATE TABLE IF NOT EXISTS nora.account_photos (
    account_id uuid PRIMARY KEY REFERENCES nora.accounts(id) ON DELETE CASCADE,
    content_type text NOT NULL CHECK (content_type IN ('image/jpeg', 'image/png', 'image/webp')),
    content bytea NOT NULL CHECK (octet_length(content) BETWEEN 1 AND 2097152),
    etag text NOT NULL CHECK (etag ~ '^[0-9a-f]{64}$'),
    updated_at timestamptz NOT NULL
);
