# Preparar o banco da demo e entrar no app (2026-11-17, durante a apresentação)

## Skills e comandos invocados

- `/aura-retomar-execucao:aura` (protocolo, diagnóstico, loop e graphify).

## O que foi pedido

1. "qual login eu utilizo?" no app do paciente que está aberto no emulador.
2. "não consigo fazer isso agora, faça tudo isso para mim e logue no aplicativo no emulador que está aberto".
3. Antes, no pedido anterior: parar de abrir e fechar o emulador, porque a apresentação do app é agora.

"Tudo isso" são os passos que a IA tinha listado: UBS Escola Centro, especialidade Clínica geral, conceder a UBS ao administrador, cadastrar o paciente no OBS, criar a conta do paciente e entrar no app.

## Objetivo

Banco da demo com a UBS, a especialidade e um paciente com conta, e o app do emulador logado com esse paciente, sem tocar na demonstração em andamento.

## Restrições

- Não abrir, fechar nem reinstalar o emulador (só `adb input` e capturas).
- Não rodar Gradle nem mexer no checkout principal (`C:\Users\Pichau\Documents\GitHub\Projeto-NORA`): a demonstração do usuário roda dele.
- Senhas geradas pela IA, gravadas só em `%LOCALAPPDATA%\NORA\demo\acesso-demo.txt` (fora do Git) e nunca escritas no chat.
- Dados só no banco da demo (`nora_demo`, porta 55432), nunca no PostgreSQL de uso.

## Checklist

- [ ] Comando `--demo-bootstrap` em `tests/Nora.Api.Checks` (idempotente, pelos serviços e rotas reais).
- [ ] Rodar contra o banco da demo e conferir o resultado.
- [ ] Entrar no app do emulador com o paciente criado.
- [ ] Registrar o que foi feito e o que falta.

## Estado

- 2026-11-17: tarefa registrada; implementação começando.
