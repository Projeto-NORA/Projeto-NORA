ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS cep varchar(8);
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS street varchar(160);
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS address_number varchar(20);
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS complement varchar(160);
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS neighborhood varchar(120);
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS city varchar(120);
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS state varchar(2);
ALTER TABLE nora.accounts ADD COLUMN IF NOT EXISTS ibge varchar(10);
