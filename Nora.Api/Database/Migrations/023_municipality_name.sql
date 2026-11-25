-- Identificação informada no provisionamento de contas municipais. Contas antigas permanecem sem valor.
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS municipality_name text;
