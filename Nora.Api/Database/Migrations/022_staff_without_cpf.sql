-- P-053: as contas de equipe (OBS e Secretaria) deixam de ter CPF: entram só por e-mail e senha. O CPF existente delas é apagado; o do paciente continua obrigatório
-- (o login, a recuperação de senha, o cadastro na UBS e o vínculo dependem dele). UNIQUE(scope, cpf) continua valendo: nulos não colidem entre si.
ALTER TABLE nora.accounts ALTER COLUMN cpf DROP NOT NULL;
UPDATE nora.accounts SET cpf = NULL WHERE scope IN ('obs', 'municipal') AND cpf IS NOT NULL;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'nora.accounts'::regclass AND conname = 'accounts_patient_cpf_required') THEN
        ALTER TABLE nora.accounts ADD CONSTRAINT accounts_patient_cpf_required CHECK (scope <> 'patient' OR cpf IS NOT NULL);
    END IF;
END
$$;
