# Acesso OBS por chave

## Skills e comandos invocados
- Usuário: retomada, Aura e refatoração/limpeza.
- Aplicada `productivity:research` para o estudo solicitado, com fontes primárias e registro em docs.

## Pedido, item por item
1. OBS terá somente administrador da clínica, sem login de administrador ou atendente.
2. Acesso mediante chave gerada no painel do administrador e informada no OBS.
3. Comparar com o sistema existente `C:/Users/mateu/source/repos/OliverImportsBusinnes`.
4. Estudar se o modelo é adequado e como aprimorá-lo; implementar as decisões confirmadas.

## Objetivo
Definir e implementar ativação segura e simples do OBS, sem presumir políticas de clínica/dispositivo, revogação, validade ou acesso offline que não foram confirmadas.

## Restrições
- Etapa começa após publicação da entrega anterior, confirmada em `7f32528` na main.
- Ler documentação da referência antes do código; referência somente leitura, sem alterar outro projeto.
- Preservar dados, stack, trabalho e segredos; chave e token não entram em Git/logs.
- Não confundir ativação de software com identidade de quem opera dados de pacientes.
- Painel administrativo/financeiro é a solicitação seguinte, registrada separadamente em P-015.

## Checklist
- [x] Registrar tarefa antes de investigar código.
- [x] Ler documentação/arquitetura/licenciamento da referência Oliver Imports.
- [x] Pesquisar fontes oficiais, escrever estudo e proposta concreta.
- [ ] Confirmar alcance da chave e políticas necessárias com usuário.
- [ ] Implementar API, persistência, geração/revogação no painel e ativação OBS.
- [ ] Validar autorização, expiração/revogação, isolamento, clientes e persistência.
- [ ] Limpar/revisar, atualizar documentação e P-014; commit/push main e conferir remoto.

## Estado
Iniciada em 2026-10-08 após entrega anterior implementada, validada e publicada. Caminho da referência confirmado pelo usuário; inventário identificou os repositórios Panel-Desktop e Sistema-Gerenciamento-Estoque e seus remotos. Documentação e código de ativação consultados somente para leitura, sem executar os sistemas da referência.

Estudo concluído em `docs/estudo-acesso-obs.md`, com fontes oficiais OWASP, Microsoft e RFCs. O Oliver Imports mantém ativação e identidade de funcionário; a ativação não substitui o login humano nessa referência. Proposta NORA apresentada: convite de uso único por computador/perfil Windows, validade inicial de 24 horas, credencial local protegida, abertura posterior sem login e revogação central, com operação online. As ações seriam atribuídas à instalação, sem inventar identidade individual do operador.

Perguntas enviadas ao usuário: confirmar a proposta e definir uma clínica por API ou várias clínicas na mesma API. O caminho foi respondido; alcance e isolamento ainda aguardam resposta. A implementação de autorização, migrações e interface depende dessas respostas para não criar um contrato incorreto ou misturar dados de clínicas. Nenhum código de acesso por chave foi implementado nesta etapa. P-014 registra a decisão pendente; a etapa seguinte do painel permanece em P-015.
