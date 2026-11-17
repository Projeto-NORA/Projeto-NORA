# Validação final antes da apresentação (2026-11-17)

## Skills e comandos invocados

- `/skills-aura:aura` (protocolo-tarefa-complexa, diagnosing-bugs, modo-loop-verificacao e graphify).
- Pedidas pelo nome no texto: `refutar-projeto` e `otimizar-projeto` (plugin `my-skills`).

## O que foi pedido

1. Validação final de tudo: testar, achar erros e bugs, e refutar decisões já tomadas, usando as skills refutar-projeto, otimizar-projeto e aura.
2. Queixa sobre só conseguir colocar uma skill por mensagem e pedido para falar com a Anthropic.
3. Corrigir todas as pendências abertas e as que ficariam abertas, perguntando as decisões durante o trabalho.
4. No fim, sobrar só o que o usuário precisa fazer, num roteiro breve e explicado como para uma criança, acrescentado ao `roteiro-demonstracao.md`.
5. Garantir que tudo esteja finalizado e validado, para a apresentação de hoje não ter falha.

## Decisões do usuário (2026-11-17)

| Assunto | Decisão |
| --- | --- |
| P-048, APK de release | Configurar agora. |
| P-012, PostgreSQL de uso | Encerrar: substituído pelo banco da demo e pelo Neon. |
| P-003 e P-007 | A IA faz a parte automatizável; o que é físico vira checklist do usuário. |
| Skill que junta várias | Não precisa. |
| R-030, recusa ou vencimento da oferta | A pessoa volta para a fila na mesma posição. |
| R-032, outra UBS fora do ar | Manter recusando (503). |
| Otimização (R-040, G1 a G3) | Agora. |
| R-031, mesmo usuário em todos os bancos de UBS | Aceitar no piloto, como decisão consciente. |

## Checklist

- [x] Refutação do projeto: 33 refutações. As que ameaçavam a demo foram corrigidas no roteiro e no `demo-local.ps1`: R-001 a R-011, mais R-020 (o Render bloqueia SMTP; agora vai pelo Brevo na porta 2525) e R-025.
- [x] Otimização: análise com tabela de gastos (`docs/desempenho.md`). P1 (passe ocioso do distribuidor) e P2 (`FOR SHARE` nas leituras) foram aplicados e medidos.
- [x] Revisão volta 2 (P-050): 5 achados, todos corrigidos.
- [x] Revisão volta 3: 2 achados baixos e um texto, corrigidos. Volta 4, revisão das quatro correções: sem achados.
- [x] R-030 implementado com regressão (`QueueReturnChecks` 5/5); textos do OBS e do app atualizados.
- [x] P-046, P-048 e a parte automatizável da P-003, com o defeito do botão "Configurar" em fonte 130 % corrigido.
- [x] P-012 e P-047 encerradas, com motivo.
- [x] Validação final: `NORA.slnx` com 0 erros e 0 avisos, desktop 173/173, E2E 176/176, API com todas as suítes verdes, Android com 285 testes, 54 prévias e 45 instrumentados.
- [x] Roteiro corrigido, com a seção "O que você precisa fazer" (itens A a H).
- [x] Commit, merge e push na `main`.

## Estado

- 16:20: `5d0723f` publicado, com o roteiro e o `demo-local.ps1` corrigidos.
- 16:44: `31922c8` publicado, com a parte do app (P-048, P-046 e fonte ampliada).
- 17:00: Android com 285 testes locais, 54 prévias e 45 instrumentados (43 passaram e 2 pulados por assumption), já com os textos novos da recusa. A validação completa do .NET e a revisão volta 3 estão rodando.
- 17:28: validação final verde (números em `execucao.md`). Uma falha do E2E por falta de memória foi diagnosticada: ela se repetiu com 1,9 GB livres e não se repetiu com 18 GB livres. Ficaram abertas só a P-042, a P-049, a P-003 e a P-007 (tarefas do usuário, itens C, D e F do roteiro), a P-051 (decisão do usuário, item G) e a P-052 (otimizações futuras).
