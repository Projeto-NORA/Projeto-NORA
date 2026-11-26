-- Remove de UMA UBS tudo o que o script de demonstração criou (e só isso). Roda numa transação REPEATABLE READ.
-- O que é "do script": id que começa com de0de0de- (consultas, vagas, ofertas, exames, vacinas, profissionais, consultórios, grade, bloqueios) e paciente cujo
-- cadastro foi criado pelo ator fictício. Uma linha de demonstração que ganhou ligação com algo REAL (uma consulta marcada de verdade com um médico fictício,
-- por exemplo) NÃO é apagada: o item fica e aparece nas linhas "K|" do resultado.
\echo E|Contando as linhas antes
CREATE TEMP TABLE _antes AS SELECT * FROM pg_temp.contar();

\echo E|Vagas relâmpago e ofertas
DELETE FROM nora_unit.lightning_offers WHERE pg_temp.is_demo(id) OR slot_id IN (SELECT id FROM nora_unit.lightning_slots WHERE pg_temp.is_demo(id));
DELETE FROM nora_unit.lightning_slots WHERE pg_temp.is_demo(id);

\echo E|Consultas e eventos
-- Consulta fictícia que ficou ligada a uma oferta ou vaga REAL (ex.: o distribuidor da API ofereceu a vaga a quem tem uma consulta fictícia) é mantida.
CREATE TEMP TABLE _rm_apt AS
SELECT a.id FROM nora_unit.appointments a
WHERE pg_temp.is_demo(a.id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.lightning_offers o WHERE o.appointment_id = a.id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.lightning_slots s WHERE s.appointment_id = a.id);
DELETE FROM nora_unit.appointment_events WHERE appointment_id IN (SELECT id FROM _rm_apt);
DELETE FROM nora_unit.appointments WHERE id IN (SELECT id FROM _rm_apt);

\echo E|Exames e vacinas
DELETE FROM nora_unit.health_record_events WHERE pg_temp.is_demo(record_id);
DELETE FROM nora_unit.health_exams WHERE pg_temp.is_demo(id);
DELETE FROM nora_unit.health_vaccines WHERE pg_temp.is_demo(id);

\echo E|Pacientes fictícios
-- Só sai o paciente que nada de real aponta mais (nenhuma consulta, exame, vacina, fila ou correção de e-mail que não seja da demonstração).
CREATE TEMP TABLE _rm_pat AS
SELECT r.cpf FROM nora_unit.patient_registry r
WHERE r.created_by = pg_temp.demo_actor()
  AND NOT EXISTS (SELECT 1 FROM nora_unit.appointments a WHERE a.cpf = r.cpf)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.health_exams x WHERE x.cpf = r.cpf)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.health_vaccines v WHERE v.cpf = r.cpf)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.waiting_list w WHERE w.cpf = r.cpf)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.email_change_outbox o WHERE o.cpf = r.cpf)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.patient_registry_events e WHERE e.cpf = r.cpf AND e.actor_id <> pg_temp.demo_actor());
DELETE FROM nora_unit.patient_registry_events WHERE cpf IN (SELECT cpf FROM _rm_pat);
DELETE FROM nora_unit.patient_registry WHERE cpf IN (SELECT cpf FROM _rm_pat);

\echo E|Bloqueios, grade, profissionais e consultórios
DELETE FROM nora_unit.schedule_blocks WHERE pg_temp.is_demo(id);
DELETE FROM nora_unit.schedule_templates WHERE pg_temp.is_demo(id);
CREATE TEMP TABLE _rm_prof AS
SELECT p.id FROM nora_unit.professionals p
WHERE pg_temp.is_demo(p.id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.schedule_templates t WHERE t.professional_id = p.id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.schedule_blocks b WHERE b.professional_id = p.id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.appointments a WHERE a.professional_id = p.id OR lower(btrim(a.professional)) = lower(btrim(p.full_name)))
  AND NOT EXISTS (SELECT 1 FROM nora_unit.lightning_slots s WHERE s.professional_id = p.id OR lower(btrim(s.professional)) = lower(btrim(p.full_name)));
DELETE FROM nora_unit.professional_specialties WHERE professional_id IN (SELECT id FROM _rm_prof);
DELETE FROM nora_unit.professionals WHERE id IN (SELECT id FROM _rm_prof);
CREATE TEMP TABLE _rm_room AS
SELECT r.id FROM nora_unit.rooms r
WHERE pg_temp.is_demo(r.id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.schedule_templates t WHERE t.room_id = r.id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.appointments a WHERE a.room_id = r.id)
  AND NOT EXISTS (SELECT 1 FROM nora_unit.lightning_slots s WHERE s.room_id = r.id);
DELETE FROM nora_unit.rooms WHERE id IN (SELECT id FROM _rm_room);

\echo E|Conferindo que só o que é da demonstração saiu
-- O que a demonstração tinha criado e continua aqui por ter ganho ligação real (informado ao usuário).
SELECT 'K|consultas|' || count(*) FROM nora_unit.appointments WHERE pg_temp.is_demo(id) HAVING count(*) > 0;
SELECT 'K|pacientes|' || count(*) FROM nora_unit.patient_registry WHERE created_by = pg_temp.demo_actor() HAVING count(*) > 0;
SELECT 'K|profissionais|' || count(*) FROM nora_unit.professionals WHERE pg_temp.is_demo(id) HAVING count(*) > 0;
SELECT 'K|consultórios|' || count(*) FROM nora_unit.rooms WHERE pg_temp.is_demo(id) HAVING count(*) > 0;

-- Só estas tabelas podem perder linhas; qualquer outra que perca faz a transação inteira ser desfeita.
SELECT pg_temp.conferir(ARRAY['nora_unit.lightning_offers', 'nora_unit.lightning_slots', 'nora_unit.appointment_events', 'nora_unit.appointments', 'nora_unit.health_record_events',
    'nora_unit.health_exams', 'nora_unit.health_vaccines', 'nora_unit.patient_registry_events', 'nora_unit.patient_registry', 'nora_unit.schedule_blocks', 'nora_unit.schedule_templates',
    'nora_unit.professional_specialties', 'nora_unit.professionals', 'nora_unit.rooms']);
SELECT 'T|' || a.t || '|' || a.n || '|' || COALESCE(d.n, 0) FROM _antes a LEFT JOIN _depois d ON d.t = a.t WHERE COALESCE(d.n, 0) <> a.n ORDER BY a.t;
