# Enter nos formulários dos aplicativos Windows

## Skills e comandos invocados
Nenhum comando ou skill foi invocado pelo usuário. Solicitou commit e push na branch main.

## Pedido
- Nos dois softwares Windows, Enter avança para o próximo campo enquanto houver campos.
- No último campo, Enter aciona o botão correspondente do formulário.
- Não alterar o aplicativo móvel.
- Fazer commit e push na main.

## Objetivo
Permitir preencher e enviar os formulários dos dois aplicativos usando Enter.

## Restrições
Preservar o comportamento dos aplicativos e não alterar o aplicativo móvel.

## Checklist
- [ ] Analisar documentação, formulários e estado Git.
- [ ] Implementar navegação e ação nos dois aplicativos Windows.
- [ ] Validar navegação, ações, build e regressões pertinentes.
- [ ] Atualizar documentação pertinente e registrar resultados.
- [ ] Conferir alterações, fazer commit e push na main.

## Estado
Documento inicial criado antes da investigação. Demais etapas pendentes.

## Progresso em 2026-11-24
- Usuário decidiu que, havendo vários botões, Enter deve acionar o botão principal destacado.
- Implementação parcial local: login compacto avança do e-mail para a senha e, quando visível, à seleção de unidade; troca de senha avança entre os campos. Falta testar diretamente esses atalhos e cobrir os demais formulários das duas aplicações. Campos multilinha e seletores exigem preservar seu comportamento próprio.
- `dotnet build NORA.slnx --configuration Release -m:1`: passou, 0 erros e 13 avisos NU1900 (consulta de vulnerabilidades NuGet indisponível no ambiente).
- Suíte desktop: 191/191 verificações existentes passaram; não são testes específicos dos novos atalhos. E2E PostgreSQL não solicitado e não executado.
- Branch atual `codex/conexao-url-neon`; main local e origin/main estavam em commits diferentes na inspeção inicial. Configurações locais não rastreadas em `OBS-NORA/Properties/` e `Painel-Municipal/Properties/` pertencem ao usuário e devem ser preservadas.
- P-064 registra o escopo não concluído. Nenhum commit ou push efetuado: não publicar implementação parcial como se concluída.

## Publicação parcial solicitada
Em 24/11/2026 o usuário pediu commit e push à main mesmo com o trabalho incompleto. A main já tinha a P-061; a pendência desta tarefa foi renumerada para P-064, preservando a numeração remota. A implementação integral e os testes específicos continuam pendentes.
