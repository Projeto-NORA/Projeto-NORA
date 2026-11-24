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

- [ ] Conferir o remoto antes do push e juntar o que vier dele sem perder nada.
- [ ] P-049: diagnosticar o deploy que falhou, corrigir a variável e publicar. Validar `/health` e `/health/ready`.
- [ ] P-049: aplicar as migrações no Neon e conferir que nenhuma tabela perdeu dados.
- [ ] P-049: enviar um e-mail real pelo Brevo, com dados fictícios.
- [ ] P-058: rodar a suíte instrumentada completa do APPNORA no emulador.
- [ ] P-042: mover para `docs/IDEIAS.md` por decisão do usuário.
- [ ] Recuperação de senha: confirmar no código e nos testes que funciona no APPNORA e no OBS. Confirmar que o Painel Municipal está documentado como adiado.
- [ ] Build .NET Release, suítes da API e do WPF e testes unitários do Android depois das mudanças.
- [ ] Loop de verificação até uma volta sem achados.
- [ ] Atualizar a documentação, `PENDENCIAS.md` e este documento. Fazer os commits por assunto e o push.

## Estado

Tarefa aberta em 24/11/2026. O remoto conferido no início não tinha commit novo (`main` = `origin/main` = `492f7c3`). O trabalho do Codex está copiado para o worktree.
