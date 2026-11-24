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
- [x] Analisar documentação, formulários e estado Git.
- [x] Implementar navegação e ação nos dois aplicativos Windows.
- [x] Validar navegação, ações, build e regressões pertinentes.
- [x] Atualizar documentação pertinente e registrar resultados.
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

## Continuação solicitada
O usuário pediu implementar totalmente o Enter nos dois softwares e autorizou commit e push. Manter a decisão de acionar o botão principal quando houver várias opções. Falta cobrir os formulários, testar especificamente a navegação e publicar o resultado completo.

## Implementação completa — 24/11/2026
- Navegação compartilhada por formulário, em 28 telas desktop, com ações principais explícitas; campos desabilitados e somente de leitura são ignorados. Texto multilinha mantém quebra de linha, e Enter em seletor aberto confirma a seleção. Na agenda, o horário é escolhido antes de Agendar.
- Auditoria do XAML: 129 campos de texto/seleção vinculados a grupos, sem campo não marcado; ações e listas de horários vinculadas aos formulários correspondentes. Pesquisa automática sem botão continua automática.
- Build Release da solução passou com 0 erros e 0 avisos. Regressão desktop 196/196 passou antes da última correção localizada para campos somente de leitura e da associação das listas de horários; após essas mudanças, build Release passou e verificações filtradas de Enter passaram 7/7. E2E com PostgreSQL não foi executado.
- A P-064 foi resolvida. A pedido do usuário para incluir tudo, os perfis locais do Visual Studio com o endereço público da API também acompanham a entrega; não contêm credenciais.
- Falta conferir diff, criar commit e enviar à main; depois executar a solicitação separada para retirar latitude e longitude.
