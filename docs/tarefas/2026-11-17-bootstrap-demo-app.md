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

- [x] Comando `--demo-bootstrap` em `tests/Nora.Api.Checks` (idempotente, pelos serviços e rotas reais).
- [x] Rodar contra o banco da demo e conferir o resultado.
- [x] Entrar no app do emulador com o paciente criado.
- [x] Registrar o que foi feito e o que falta.

## Estado

- 2026-11-17: tarefa registrada.
- `tests/Nora.Api.Checks/DemoBootstrap.cs` (`--demo-bootstrap`) criou, pelo host da API em processo contra `nora_demo`: a especialidade Clínica geral, a UBS Escola Centro, contas de demonstração (Secretaria, administrador da UBS e o paciente Maria Souza), o cadastro de referência, o vínculo do paciente e o acesso à UBS para os administradores OBS que já existiam, inclusive o do usuário. Senhas aleatórias em `%LOCALAPPDATA%\NORA\demo\acesso-demo.txt`, fora do Git. Idempotente.
- O app do emulador estava conectado a uma API de teste de outra sessão (`--client-fixture`, porta 62489, pasta `nora-app-emulator-setup-f5c52d`). Combinado com essa sessão por mensagem, saí da conta de teste, troquei o endereço para `http://10.0.2.2:5080` e entrei como a paciente da demo (tela "Olá, Maria"), só por `adb input`, sem abrir, fechar nem reinstalar o emulador. A fixture e o PostgreSQL dela não foram tocados.
- Ainda não existem profissional, consultório e grade na UBS Escola Centro, então o paciente ainda não vê horários para agendar. O administrador monta isso no OBS, como no roteiro.
