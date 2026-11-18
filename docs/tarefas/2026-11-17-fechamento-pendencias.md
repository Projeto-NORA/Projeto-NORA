# Fechamento das pendências do chat (2026-11-17, depois da apresentação)

## Skills e comandos invocados

- `/aura-retomar-execucao:aura` (protocolo, diagnóstico, loop e graphify), duas vezes: "finalize todas as pendências em aberto desse chat, qualquer decisão pendente me faça perguntas" e "continue, finalizando todas as pendências abertas desse chat para eu poder fechar ele".

## O que foi pedido

1. Finalizar todas as pendências abertas deste chat.
2. Perguntar durante o trabalho qualquer decisão pendente.
3. Deixar o chat em condição de ser fechado.

## Decisões do usuário (2026-11-17, à noite)

| Assunto | Decisão |
| --- | --- |
| P-053, CPF das contas de equipe | Tirar. As contas de equipe que já existem perdem o CPF (apagado); a equipe (OBS e Secretaria) entra só por e-mail e senha. Pacientes continuam com CPF. O cadastro inicial continua pelo terminal (`--provision`), sem perguntar CPF. |
| P-051 e score | Não há limite de ofertas sem resposta (P-051 encerrada; deixar a oferta vencer não baixa o score). O usuário quer o **score estilo Serasa**: pontos de **0 a 1000** a partir do cálculo de comparecimento que já existe (faltas e cancelamentos em cima da hora derrubam), com faixas **Baixo (0–299), Regular (300–599), Bom (600–799) e Excelente (800–1000)**. Aparece no **OBS e no app**; o Painel Municipal não precisa. |
| Efeito do score | **Prioridade na Vaga Relâmpago**: entre quem espera a mesma especialidade, quem tem score maior recebe a oferta antes; empate segue a ordem de entrada na fila. |
| P-052, otimizações | Fazer as três: P3 (lista de cadastro do OBS sem a 2ª conexão central), P4 (paginar o histórico das listas de fila e de vagas do OBS) e P5 (índice por CPF na fila, migração de UBS 017). |
| P-054, dados fictícios | Cancelada: a apresentação já passou e não precisa mais. |
| P-049, hospedagem | Fazer agora, no **Render + Neon**, com a IA guiando no navegador do app; o usuário entra nas contas e cola senhas e chaves (a IA não cria conta nem digita segredo). |
| P-055, "marcar a qualquer hora" | Validar por completo e publicar na `main`. |

## Restrições

- Nenhuma credencial no Git; a IA não digita senha, chave nem token em formulário de serviço externo.
- Merge e push na `main` autorizados para este fechamento (pedido do usuário de deixar tudo finalizado).
- Não mexer na fixture de outra sessão (porta 62489, PostgreSQL 55441).

## Checklist

- [ ] Contrato fixo (score em pontos, prioridade, P-053, P4).
- [ ] Servidor: P-053, score em pontos e prioridade, P3, P4, P5, com testes.
- [ ] Desktop: Secretaria sem CPF na equipe, entrada por e-mail, score em pontos no OBS, histórico paginado na Fila e na Vaga Relâmpago.
- [ ] App: score em pontos e faixas, textos da prioridade.
- [ ] Scripts e roteiro sem CPF da equipe.
- [ ] Revisão em loop até uma volta sem achados.
- [ ] Validação completa (API, desktop, E2E, Android) e merge + push na `main`.
- [ ] Hospedagem no Render + Neon, guiada no navegador.
- [ ] PENDENCIAS atualizado (P-051, P-052, P-053, P-054, P-055, P-049).

## Estado

- Decisões recebidas; contrato sendo escrito.
