-- Família: o responsável (conta de paciente) pede o vínculo com um dependente, identificado por CPF, numa UBS onde o dependente tem cadastro de
-- referência; a equipe da UBS decide depois de conferir os documentos. O dependente não precisa de conta. O CPF do dependente nunca vai em URL:
-- as rotas usam o id do vínculo. Um vínculo é de uso único (cancelled, rejected e ended são finais); um novo pedido cria outra linha.
CREATE TABLE IF NOT EXISTS nora.family_links (
    id uuid PRIMARY KEY,
    guardian_id uuid NOT NULL REFERENCES nora.accounts(id),
    dependent_cpf varchar(11) NOT NULL CHECK (dependent_cpf ~ '^[0-9]{11}$'),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    status text NOT NULL CHECK (status IN ('pending', 'approved', 'rejected', 'cancelled', 'ended')),
    requested_at timestamptz NOT NULL,
    decided_by uuid REFERENCES nora.accounts(id),
    decided_at timestamptz,
    decision_reason text CHECK (decision_reason IS NULL OR char_length(decision_reason) BETWEEN 1 AND 280),
    closed_by uuid REFERENCES nora.accounts(id),
    closed_at timestamptz,
    CHECK ((decided_by IS NULL) = (decided_at IS NULL)),
    CHECK ((closed_by IS NULL) = (closed_at IS NULL)),
    CHECK (status NOT IN ('approved', 'rejected', 'ended') OR decided_by IS NOT NULL),
    CHECK (status NOT IN ('cancelled', 'ended') OR closed_by IS NOT NULL)
);
-- No máximo um pedido ou vínculo ativo por responsável, dependente e UBS.
CREATE UNIQUE INDEX IF NOT EXISTS family_links_active_uidx ON nora.family_links(guardian_id, dependent_cpf, unit_id) WHERE status IN ('pending', 'approved');
CREATE INDEX IF NOT EXISTS family_links_guardian_idx ON nora.family_links(guardian_id, requested_at DESC);
CREATE INDEX IF NOT EXISTS family_links_unit_status_idx ON nora.family_links(unit_id, status, requested_at);
CREATE INDEX IF NOT EXISTS family_links_dependent_idx ON nora.family_links(dependent_cpf) WHERE status = 'approved';

-- Sugestão de troca do e-mail da conta: nasce quando a UBS corrige o e-mail de referência de um paciente que tem conta vinculada à UBS. A conta só troca
-- depois de o código (HMAC, 10 minutos, uso único, 5 tentativas) enviado ao endereço novo ser confirmado. No máximo uma sugestão pendente por conta.
CREATE TABLE IF NOT EXISTS nora.email_change_suggestions (
    id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES nora.accounts(id),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    suggested_email varchar(254) NOT NULL,
    status text NOT NULL CHECK (status IN ('pending', 'confirmed', 'dismissed', 'superseded')),
    created_at timestamptz NOT NULL,
    closed_at timestamptz,
    code_hash varchar(64),
    code_expires_at timestamptz,
    sends smallint NOT NULL DEFAULT 0 CHECK (sends BETWEEN 0 AND 3),
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 5),
    CHECK ((status = 'pending') = (closed_at IS NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS email_change_pending_uidx ON nora.email_change_suggestions(account_id) WHERE status = 'pending';

-- Limpeza periódica (MaintenanceService): índices para apagar em lotes sem varrer as tabelas inteiras.
CREATE INDEX IF NOT EXISTS sessions_expires_idx ON nora.sessions(expires_at);
CREATE INDEX IF NOT EXISTS sessions_revoked_idx ON nora.sessions(revoked_at) WHERE revoked_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS registration_challenges_expires_idx ON nora.patient_registration_challenges(expires_at);
CREATE INDEX IF NOT EXISTS patient_password_reset_expires_idx ON nora.patient_password_reset_codes(expires_at);
