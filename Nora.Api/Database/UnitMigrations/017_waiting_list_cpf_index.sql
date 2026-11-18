-- P5 (docs/desempenho.md): as leituras da fila por CPF (o app do paciente, a cada 30 s, e a ordem das ofertas) filtravam por waiting_list.cpf sem índice próprio:
-- os índices da fila eram parciais (só status waiting/offered) e a lista do paciente traz também as encerradas. Agora há índice por CPF e, para a paginação do histórico
-- da Fila do OBS (encerradas das mais recentes para as mais antigas, cursor por data de entrada e id), um índice por data de entrada.
CREATE INDEX IF NOT EXISTS waiting_list_cpf_idx ON nora_unit.waiting_list(cpf, entered_at, id);
CREATE INDEX IF NOT EXISTS waiting_list_entered_idx ON nora_unit.waiting_list(entered_at DESC, id DESC);
