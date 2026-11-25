# Finalização das pendências do NORA (continuação do Codex)

## Skills e comandos invocados

- `/aura-retomar-execucao:aura` (protocolo de tarefa complexa, diagnóstico de bugs, loop de verificação e graphify).

## Pedido, item por item

1. Continuar o que o Codex fazia em `2026-11-23-validacao-pendencias-nora.md` e fechar as pendências abertas do sistema.
2. Continuar as validações que ficaram sem resultado.
3. Perguntar ao usuário em qualquer decisão e garantir que tudo foi feito exatamente como ele pediu.
4. Confirmar que a recuperação de senha funciona no APPNORA ("PP") e no OBS.
5. Painel Municipal: a recuperação de senha fica para depois. Por enquanto, a TI olha o banco de dados.

## Decisões do usuário em 24/11/2026

- Trabalhar na `main`, com commit e push no fim. A namorada do usuário pode ter feito um commit. Se precisar de merge, juntar os dois lados sem perder nada e perguntar se houver dúvida.
- O workspace `PanelAPI` do Render é o do NORA. Pode ler os logs, corrigir `ConnectionStrings__Nora` e disparar o deploy.
- Pode aplicar as migrações novas no banco do piloto no Neon e mandar um e-mail real de teste pelo Brevo, com dados fictícios.
- Login com Google e push (P-042) ficam para depois do piloto: a pendência vira ideia.
- A senha do Neon apareceu nos logs do Render (a versão `492f7c3` imprimia a URI inteira ao falhar). O usuário decidiu trocá-la no Neon antes de corrigir a variável.
- E-mail do teste real pelo Brevo: `noraprojeto@gmail.com` (confirmado duas vezes pelo usuário).
- Exclusão programada: quando o prazo geral diminui, a contagem recomeça na data da mudança. Nada é apagado antes de passar o novo prazo inteiro.
- OBS "só login": a funcionária entra com e-mail e senha, escolhe a UBS entre as que tem permissão e pode manter a sessão por 30 dias. O OBS deixa de registrar o computador, e somem a seção Computadores, o código de ativação e a revogação. O código dos computadores fica guardado e desligado por configuração, para ligar no futuro. Nas palavras do usuário: "se ela tem login, ela já pode fazer todas as coisas que a OBS permite. O computador ativado seria uma coisa a mais, mas por enquanto não precisa disso."

## Como o trabalho foi organizado

O trabalho do Codex estava sem commit no checkout principal (139 arquivos: 102 alterados e 37 novos). O app não deixa esta sessão editar o checkout principal, porque ela roda no worktree `claude/project-completion-validations-1966c4`, que partiu de `492f7c3`, o mesmo commit da `main`. Os arquivos foram copiados para o worktree e conferidos por hash: 139 de 139 idênticos. No fim, a `main` avança até os commits deste worktree. Antes disso, guarda-se uma cópia de segurança do que estiver sem commit no checkout principal.

## Objetivo

Deixar o NORA com as pendências fechadas ou com o motivo registrado, a API publicada no Render com o banco do Neon atualizado e todo o trabalho validado, commitado e enviado.

## Restrições

- Não perder o trabalho do Codex nem o commit da namorada do usuário.
- A recuperação de senha do Painel Municipal não é implementada agora.
- Nenhum segredo (string de conexão, senha, chave do Brevo) vai para o Git, o chat ou os documentos.
- As migrações não podem apagar dados.

## Checklist

- [x] Conferir o remoto antes do push e juntar o que vier dele sem perder nada. Não havia commit novo da namorada do usuário: `origin/main` estava em `492f7c3` no início e foi para `3c7e454` só com os commits desta tarefa.
- [x] P-049: diagnosticar o deploy que falhou. Nos logs do Render, `ConnectionStrings__Nora` guardava a URI do Neon e a API caía na subida imprimindo a senha. A senha foi trocada no Neon e o pooling desligado.
- [ ] P-049: publicar e validar `/health/ready`. O usuário está colando a conexão nova no Render (ver Estado).
- [x] P-049: aplicar as migrações no Neon e conferir que nenhuma tabela perdeu dados. Primeira rodada pelo script desta sessão chegou ao `PRONTO`; o usuário roda `scripts/implantar-neon.ps1` para chegar à 025 e cadastrar o município e o OBS.
- [ ] P-049: enviar um e-mail real pelo Brevo, com dados fictícios, para `noraprojeto@gmail.com`. Depende da API no ar.
- [x] P-058: rodar a suíte instrumentada completa do APPNORA no emulador (resultado no Estado; a única falha virou a P-059).
- [x] P-042: movida para `docs/IDEIAS.md` como I-001.
- [x] Recuperação de senha: APPNORA por código no e-mail (2/2 na API com PostgreSQL 18: destinatário certo, código vencido, uso único, sessão antiga encerrada); OBS pela Secretaria, com senha provisória e troca obrigatória; Painel Municipal adiado, com a TI usando `--reset-password` no terminal.
- [x] Build .NET Release (0 avisos, 0 erros), API no PostgreSQL 18 (287/287) e WPF sem API (191/191) depois das mudanças.
- [x] Loop de verificação: a revisão independente achou dois riscos médios, decididos pelo usuário e corrigidos (prazo menor recomeça a contagem; OBS só com login). O risco baixo da lista limitada a 200 virou a P-060.
- [x] Atualizar a documentação, `PENDENCIAS.md` e este documento. Fazer os commits por assunto e o push.

## Estado

Atualizado em 24/11/2026.

**Na `main` e no GitHub (`3c7e454`):**

- Exclusão programada. Reduzir o prazo geral recomeça a contagem na data da mudança; aumentar mantém o início. Teste novo em `RetentionChecks.cs`.
- Computadores do OBS desligados por padrão (`Nora:ObsDevices:Enabled=false` na API, `ObsDeviceStore(computersEnabled: false)` no OBS). O login vale sozinho e pode ser mantido por 30 dias. Os testes antigos dos computadores ligam o recurso de propósito, e há testes novos do padrão na API e no WPF.
- Perfil da equipe, sessão de 30 dias, filtros, pesquisa e preferências (trabalho do Codex de 23/11).
- `--reset-password` (migração central 025) e `scripts/implantar-neon.ps1`, feitos por um fork desta conversa entre 03:00 e 06:40.

**Validações:**

- API no PostgreSQL 18.6 descartável: 283/283 antes das mudanças desta sessão e 287/287 depois. A segunda rodada foi do fork.
- WPF sem API: 190/190 e depois 191/191, com o teste do login só por senha.
- Android: duas rodadas completas sem a API real, 50 testes com 48 passando e 2 pulados por assumption.
- Rodada final com a API real (`validar-clientes-descartavel.ps1 -Android -Port 55530`): WPF 194/194. Android: 46 passando, inclusive o teste da API real, 3 pulados por assumption e 1 falha do teste de presença (P-059: 2 falhas em 2 rodadas com a API real e 0 em 7 sem ela). A P-058 foi resolvida, e a P-059 segue aberta com a hipótese e o próximo passo.

**O que falta:**

- Implantação: colar a conexão no Render, conferir `/health/ready` e testar o Brevo. Fica com o usuário nesta rodada.
- A P-059, o teste de presença intermitente.

**Processo:** esta sessão e um fork trabalharam ao mesmo tempo no mesmo worktree. O fork o alinhou a `3c7e454` com `git reset --hard`; os 149 arquivos eram idênticos aos commits, nada se perdeu.
