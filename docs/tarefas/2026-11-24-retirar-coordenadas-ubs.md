# Retirar latitude e longitude do cadastro de UBS

## Skills e comandos invocados
Nenhuma skill ou comando foi invocado pelo usuário. Pediu commit e push na main.

## Pedido
- Retirar os campos Latitude e Longitude do cadastro da UBS, conforme a imagem fornecida.
- Fazer commit e push na main.

## Objetivo
Simplificar o formulário de cadastro da UBS retirando os dois campos indicados.

## Restrições
Preservar as demais informações e o comportamento do sistema.

## Checklist
- [x] Ler a documentação relevante e verificar o fluxo de cadastro e edição.
- [x] Retirar os campos indicados e preservar os dados existentes.
- [x] Validar o cadastro, a edição, a navegação por Enter e a compilação.
- [x] Atualizar a documentação e registrar os resultados.
- [ ] Fazer commit e push na main.

## Estado
Documento inicial criado antes de investigar esta solicitação.

## Estado da investigação — 24/11/2026
- Os dois campos e suas mensagens foram retirados do formulário localmente. A alteração ainda não deve ser publicada: o código atual exige coordenadas para cadastrar, e escondê-las sozinho impediria o salvamento.
- A API e o banco exigem números; o aplicativo Android instalado também espera números. O mapa e a sugestão de UBS próxima usam as coordenadas. Na edição, os valores existentes são carregados e precisam ser preservados.
- Foi perguntado ao usuário se deseja obter a localização automaticamente pelo endereço ou cadastrar sem localização, com mapa e proximidade indisponíveis para essas unidades. Aguardando decisão; consultar chats e documentação não encontrou decisão anterior.
- Outra sessão alterou `Nora.Desktop/Views/ShellWindow.xaml` e criou `docs/tarefas/2026-11-24-correcao-botao-filtros.md`; preservar ambos e não incluí-los nesta tarefa.

## Decisão do usuário
Em 24/11/2026 o usuário escolheu obter a localização automaticamente a partir do endereço. A solução deve preservar as coordenadas existentes quando endereço/região não mudarem, informar erro de busca sem inventar coordenadas e manter compatibilidade com a API e o aplicativo já distribuído.

## Implementação e verificações em 24/11/2026
- Campos removidos do cadastro/edição; nome, região e endereço permanecem. A busca usa o endereço da UBS no serviço Photon/OSM, confere rua, região e número quando informado e recusa resultados ambíguos, ausentes ou fora do Brasil. Ao alterar apenas o nome, preserva a posição existente; ao alterar endereço ou região, busca novamente.
- A tela acumula as etapas de localização e salvamento, com falha visível; o fechamento durante a busca não deve gravar a unidade. A API e o formato usado pelo app não foram alterados.
- A consulta real ao Photon com o endereço público da escola retornou a rua, o número e a cidade correspondentes. Não houve gravação no banco real.
- Build Release da solução: 0 erros e 0 avisos (restauração executada com auditoria NuGet desativada porque a consulta estava indisponível). Regressão desktop anterior passou 200/200; os testes específicos de endereço e fechamento foram ampliados depois e precisam ser repetidos após a liberação da janela pela outra tarefa.
- Restam a validação final, revisão do diff e publicação. Arquivos de filtro da outra tarefa não pertencem a esta alteração e devem ficar fora do commit.

Os testes finais sem janela passaram: localização exata e sem vírgula, recusa de localização ambígua, serviço indisponível, cadastro/edição e fechamento durante a busca. A repetição do teste visual com Enter aguarda a liberação da janela que outra tarefa está usando, por coordenação autorizada pelo usuário.

## Validação final
- A main mais recente foi usada em uma cópia isolada para não interferir em outras tarefas. Compilação Release passou com 0 erros e 0 avisos; teste do formulário real 1/1 e seleção UBS 9/9, incluindo busca automática, ambiguidades, falha do serviço, edição, janela fechada e Enter no endereço.
- A regressão completa anterior passou 200/200 antes das últimas proteções de fechamento e dos testes adicionais; após elas, as verificações específicas passaram na cópia isolada. Não houve E2E com PostgreSQL nem gravação no banco real.
- A primeira repetição de interface no diretório compartilhado teve stack overflow enquanto outra tarefa alterava componentes da janela; a mesma verificação passou na main isolada. As outras alterações locais não entram no commit desta tarefa.
- Falta criar o commit e enviar à main.
