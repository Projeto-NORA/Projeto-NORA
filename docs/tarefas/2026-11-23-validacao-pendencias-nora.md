# Validação das pendências do NORA

## Pedido

- Retomar pelo Aura Retomar Execução e concluir as pendências abertas e as verificações que ficaram incompletas.
- Conferir os fluxos de senha do APPNORA e do OBS.
- Adiar a recuperação de senha do Painel Municipal. O suporte fica com a TI.
- Consultar o usuário quando houver decisões relevantes em aberto.

## Objetivo e restrições

Validar a versão integrada do NORA e fechar as pendências com evidência. Preservar o trabalho de outras sessões e a decisão de adiar a recuperação municipal. As validações que criam ou alteram dados começam em ambiente descartável.

## Checklist

- [x] Conferir documentação, Git, pendências e evidências das rodadas anteriores.
- [x] Rodar as migrações e a suíte da API e verificar a exclusão programada (P-057).
- [ ] Confirmar a suíte instrumentada completa do APPNORA (P-058).
- [x] Conferir na API a recuperação do paciente e a redefinição de senha provisória do OBS.
- [ ] Avançar nas pendências de implantação, validação visual e serviços externos conforme o acesso disponível.
- [ ] Registrar resultados, revisar o diff e identificar o que depender de decisão ou acesso.

## Estado

A implementação anterior compilou em Release e teve 190/190 verificações WPF. Esta rodada compilou o conjunto com zero avisos e erros e confirmou 193/193 verificações Windows, incluindo testes com API e PostgreSQL descartáveis. A suíte PostgreSQL 18.6 passou 283/283 nas suítes com resumo, além da conferência de bancos separados. A P-057 foi resolvida com 3/3 de exclusão.

A recuperação do paciente passou 2/2 na API com e-mail simulado. A suíte confirmou destinatário cadastrado, expiração, uso único e revogação de sessão. A redefinição do OBS passou na API e na suíte WPF. Corrigimos a corrida de atualização do hash no login, o diagnóstico que podia mostrar a string de conexão, as asserções presas à versão antiga da UBS e o teste de desempenho que contava transações alheias ao distribuidor. A medição revisada contou 600 comandos em 300 passes e zero comandos no banco central.

O Painel Municipal mantém a troca de senha autenticada em Dados do perfil. O usuário adiou o fluxo de recuperação para gestores. O painel do Render mostrou `Failed deploy`. Foi pedida uma decisão sobre o ambiente do piloto e sobre o projeto Google/Firebase. As verificações físicas P-003 e P-007 foram transferidas para Testes sugeridos ao usuário.
