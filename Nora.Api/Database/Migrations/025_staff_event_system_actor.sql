-- A redefinição de senha pelo terminal (--reset-password) não tem usuário agindo: o histórico a mostra como "Sistema". Só relaxa a obrigatoriedade; nada é apagado.
ALTER TABLE nora.staff_account_events ALTER COLUMN actor_id DROP NOT NULL;
