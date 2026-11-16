-- Entrar com Google (P-042): o "sub" de uma conta Google vinculado a uma conta de paciente. Só existe vínculo feito com a sessão já aberta (CPF e senha);
-- o Google sozinho nunca cria conta. Um sub vale para uma conta só, e uma conta tem no máximo um sub. O e-mail é o do Google, só para mostrar no app.
CREATE TABLE IF NOT EXISTS nora.google_identities (
    account_id uuid PRIMARY KEY REFERENCES nora.accounts(id) ON DELETE CASCADE,
    subject varchar(255) NOT NULL UNIQUE CHECK (char_length(subject) BETWEEN 1 AND 255),
    email varchar(254),
    linked_at timestamptz NOT NULL
);

-- Aparelhos que recebem push (token do Firebase Cloud Messaging). Um token pertence à última conta que o registrou; cada conta guarda até 10 aparelhos.
-- O token nunca vai em URL; removê-lo apaga também o que estava na fila para ele.
CREATE TABLE IF NOT EXISTS nora.push_devices (
    id uuid PRIMARY KEY,
    token text NOT NULL UNIQUE CHECK (char_length(token) BETWEEN 1 AND 4096),
    account_id uuid NOT NULL REFERENCES nora.accounts(id) ON DELETE CASCADE,
    platform text NOT NULL CHECK (platform IN ('android')),
    registered_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS push_devices_account_idx ON nora.push_devices(account_id, updated_at DESC);

-- Fila de saída do push (outbox). Cada linha é um aviso de um evento para um aparelho; a chave única (event_key, device_id) é a marca de "já avisado":
-- o worker pode reencontrar o mesmo evento em vários passes sem repetir o envio. Sem dado pessoal: só o tipo e ids opacos para o app abrir a tela certa.
CREATE TABLE IF NOT EXISTS nora.push_outbox (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_key text NOT NULL CHECK (char_length(event_key) BETWEEN 1 AND 200),
    event_type text NOT NULL CHECK (event_type IN ('lightning-offer', 'announcement', 'presence-window', 'family-decision')),
    device_id uuid NOT NULL REFERENCES nora.push_devices(id) ON DELETE CASCADE,
    unit_id uuid,
    dependent_id uuid,
    appointment_id uuid,
    created_at timestamptz NOT NULL,
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    next_attempt_at timestamptz NOT NULL,
    sent_at timestamptz,
    abandoned_at timestamptz,
    UNIQUE (event_key, device_id)
);
CREATE INDEX IF NOT EXISTS push_outbox_due_idx ON nora.push_outbox(next_attempt_at, id) WHERE sent_at IS NULL AND abandoned_at IS NULL;
-- O worker procura as decisões de família recentes (aprovado ou recusado) para avisar o responsável.
CREATE INDEX IF NOT EXISTS family_links_decided_idx ON nora.family_links(decided_at) WHERE decided_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS push_outbox_done_idx ON nora.push_outbox(created_at) WHERE sent_at IS NOT NULL OR abandoned_at IS NOT NULL;

-- Posição de leitura do worker em fontes que só crescem (hoje: comunicados, pelo seq). Evita reavaliar o alcance dos comunicados antigos a cada passe.
CREATE TABLE IF NOT EXISTS nora.push_cursors (
    name text PRIMARY KEY CHECK (char_length(name) BETWEEN 1 AND 60),
    position bigint NOT NULL
);
