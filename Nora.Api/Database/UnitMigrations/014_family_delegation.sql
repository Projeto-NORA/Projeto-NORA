-- Ações do responsável em nome de um dependente (Família). guardian_id é a conta do responsável quando o evento foi gravado em nome do
-- dependente; nulo nas ações do próprio paciente, da equipe e do sistema. O autor (actor_id) continua sendo a conta que agiu, e a origem
-- continua 'patient'. Colunas aditivas e sem chave estrangeira: contas vivem no banco central.
ALTER TABLE nora_unit.appointment_events ADD COLUMN IF NOT EXISTS guardian_id uuid;
ALTER TABLE nora_unit.waiting_list_events ADD COLUMN IF NOT EXISTS guardian_id uuid;
-- Quem confirmou a presença (o próprio paciente ou o responsável); a confirmação não gera evento.
ALTER TABLE nora_unit.appointments ADD COLUMN IF NOT EXISTS confirmed_by uuid;
