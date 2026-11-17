-- Em quais UBS cada CPF tem cadastro de referência (consulta toda exige cadastro na UBS). Serve para recusar consulta que se sobrepõe em outra UBS
-- sem varrer todos os bancos: a conferência abre só os bancos das UBS listadas aqui. Cadastro nunca é apagado, então a linha nunca precisa sair.
-- Sobrar uma linha é inofensivo (só faz conferir um banco a mais); faltar seria um furo, por isso a linha é gravada e confirmada ANTES do cadastro na UBS.
CREATE TABLE IF NOT EXISTS nora.unit_registry_index (
    cpf varchar(11) NOT NULL CHECK (cpf ~ '^[0-9]{11}$'),
    unit_id uuid NOT NULL REFERENCES nora.health_units(id),
    PRIMARY KEY (cpf, unit_id)
);
-- Origem da sugestão de troca de e-mail: o id da linha da caixa de saída da UBS. Uma sugestão (qualquer situação) por origem: reprocessar a caixa de saída nunca a recria.
ALTER TABLE nora.email_change_suggestions ADD COLUMN IF NOT EXISTS source_key uuid;
CREATE UNIQUE INDEX IF NOT EXISTS email_change_source_uidx ON nora.email_change_suggestions(source_key) WHERE source_key IS NOT NULL;
