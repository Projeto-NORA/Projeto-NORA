-- Caixa de saída da sugestão de troca de e-mail da conta. É gravada na mesma transação da UBS que muda o e-mail de referência, e só sai depois de a sugestão
-- ser registrada no banco central; se o commit central falhar depois do da UBS, a linha sobra e a repetição da correção ou o reconciliador recria a sugestão.
-- Uma linha por CPF (a última mudança vale); id é a origem da sugestão (nora.email_change_suggestions.source_key), então reprocessar nunca a duplica.
CREATE TABLE IF NOT EXISTS nora_unit.email_change_outbox (
    cpf varchar(11) PRIMARY KEY REFERENCES nora_unit.patient_registry(cpf),
    id uuid NOT NULL UNIQUE,
    email varchar(254) NOT NULL,
    created_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS email_change_outbox_time_idx ON nora_unit.email_change_outbox(created_at);
