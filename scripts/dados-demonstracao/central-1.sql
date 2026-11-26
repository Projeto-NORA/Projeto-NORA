-- Banco CENTRAL, partes 1 e 1b. Chamado duas vezes pelo PowerShell:
--   1.  antes dos bancos das UBS: especialidades que faltam e, com -UbsExtras, as UBS fictícias (_spec e _units preenchidas, _idx vazia);
--   1b. depois dos bancos das UBS: o índice central de CPFs (nora.unit_registry_index) dos pacientes fictícios que de fato ficaram gravados (_idx preenchida).
-- O índice é o que a API consulta para recusar duas consultas da mesma pessoa no mesmo horário em UBS diferentes. A API o grava ANTES do cadastro; aqui ele
-- vem depois de propósito: só entram CPFs fictícios (ninguém marca consulta com eles) e assim uma execução interrompida nunca deixa índice sem cadastro.
-- Tabelas temporárias de entrada: _spec(spec_key, nome, spec_id, criar), _idx(cpf, unit_id), _units(id, nome, regiao, endereco, lat, lon).
CREATE TEMP TABLE _antes AS SELECT * FROM pg_temp.contar();

INSERT INTO nora.specialties(id, name, name_key)
SELECT spec_id, nome, spec_key FROM _spec WHERE criar ON CONFLICT DO NOTHING;

INSERT INTO nora.health_units(id, name, region, address, latitude, longitude)
SELECT id, nome, regiao, endereco, lat, lon FROM _units ON CONFLICT DO NOTHING;

\echo E|Especialidades, UBS fictícias e índice central de CPFs
INSERT INTO nora.unit_registry_index(cpf, unit_id)
SELECT cpf, unit_id FROM _idx ON CONFLICT DO NOTHING;

SELECT pg_temp.conferir();
SELECT 'T|' || d.t || '|' || COALESCE(a.n, 0) || '|' || d.n FROM _depois d LEFT JOIN _antes a ON a.t = d.t WHERE d.n <> COALESCE(a.n, 0) ORDER BY d.t;
