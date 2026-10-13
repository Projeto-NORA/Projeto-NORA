# Listagens OBS/painel — itens, ações e detalhes

## Skills e comandos invocados

- `skills-aura:aura`: protocolo-tarefa-complexa, diagnosing-bugs e modo-loop-verificacao.
- `my-skills:list-generator`: padrão de itens, barra flutuante e detalhes; pedido explícito mantém duplo clique rápido nativo e altera só os pontos ditados, sem acrescentar paginação/seleção múltipla nesta tarefa.
- Continuação do padrão visual publicado em 2026-10-11; esta solicitação é uma tarefa nova e separada.

## Pedido, item por item

1. Ajustar a listagem atual e aplicar o mesmo padrão a todas as telas com listagem do OBS e do painel Windows.
2. Cada item deve ser um retângulo com borda discretamente arredondada e espaçamento muito pequeno entre itens, mantendo a listagem já aprovada.
3. O nome da linha da listagem não deve ser copiável.
4. Manter a indicação online/offline no APPNORA à direita dos itens aplicáveis.
5. Limitar o e-mail a no máximo metade da largura da linha; CPF e telefone vêm depois. O nome longo continua com reticências. Confirmar a expressão ditada sobre CPF/telefone sem “listas” antes de decidir a exibição do CPF.
6. Um clique seleciona o item e mostra uma barra flutuante de ações na parte inferior, conforme a segunda imagem; as ações devem corresponder ao que aquela listagem realmente permite.
7. Informar quais ações podem ser oferecidas. Desativar paciente foi sugerido como possibilidade, não como regra já definida; esclarecer antes de implementar bloqueio de conta.
8. Dois cliques rápidos, no comportamento nativo do Windows Explorer, abrem os detalhes do item.
9. Detalhes em popup com bordas mais arredondadas, hierarquia semelhante à primeira imagem e todas as informações pertinentes disponíveis daquele item, sem inventar funcionalidades do sistema AEV.
10. No popup de paciente, nome, e-mail, CPF e telefone devem ter ações de copiar com ícone que indique a função. A linha não ganha ações de copiar.
11. Preservar temas, identidade NORA, autorização dos perfis e funcionamento anterior. Não substituir o online/offline por estado de conta ativa/desativada.

## Objetivo

Entregar um padrão compartilhado para as listagens existentes dos dois programas Windows, com linhas compactas arredondadas, ações por seleção e detalhes no duplo clique, incluindo cópia explícita dos dados no popup.

## Restrições

- Preservar funcionalidades e trabalho válido local/remoto; alterar somente o necessário.
- Não expor identificação de pacientes ao painel que atualmente só recebe totais agregados.
- Não inventar ações clínicas, histórico ou cadastro de instrutor apenas por aparecerem na referência AEV.
- Não adicionar desativação antes de definir sua regra de negócio.
- Não copiar dados da linha; copiar no popup somente por ação explícita.
- Validar com dados sintéticos e PostgreSQL descartável quando pertinente; não alterar banco de produção.

## Checklist

- [x] Ler documentação, pendências, Git e recursos existentes.
- [x] Conferir as duas imagens e esclarecer CPF/ações.
- [x] Mapear todas as listagens OBS/painel e capacidades reais de cada uma.
- [x] Aplicar itens arredondados e espaçamento discreto pelo padrão compartilhado.
- [x] Ajustar nome/e-mail/CPF/telefone e indicador de presença.
- [x] Implementar barra flutuante de ações para seleção, sem ocupar espaço fixo vazio.
- [x] Implementar duplo clique nativo e popup de detalhes por tipo de item.
- [x] Implementar cópia explícita/feedback de nome, e-mail, CPF e telefone no popup.
- [x] Validar temas, conteúdo longo, teclado/seleção, janela mínima, atualização e erros.
- [x] Revisar por implementação, UX, técnica e testes; passar a varredura Aura.
- [x] Executar build, checks relevantes e prévias reais; atualizar documentação e pendências.

## Estado

- Pedido registrado antes de investigar código. Árvore limpa em `main...origin/main` ao iniciar.
- Aura e suas três skills dependentes lidas. Duas referências locais disponíveis; inspeção e leitura da documentação são as próximas etapas.
- Decisões pendentes: sentido de CPF/telefone sem “listas”; ações de pacientes além de detalhes. A sugestão de desativar não autoriza por si só uma política de conta.
- Nenhuma alteração de implementação ou validação desta nova tarefa ainda.
- Confirmações: CPF completo e telefone visível; implementar desativação agora. Administrador e atendente OBS podem desativar/reativar. Desativar bloqueia novos logins, revoga sessões e cancela entradas/ofertas pendentes, oferecendo as vagas ao próximo FIFO; mantém cadastro e histórico. Reativar permite novo login e não restaura sessões/fila anteriores. A ação terá confirmação explícita na interface.
- Baseline real reexecutado: solução Release zero erros/avisos, desktop44/44 sem E2E e abertura2/2. Estado inicial anterior preservado; saída em `.build/previews-2026-10-12-baseline`.
- Mapeadas quatro listagens WPF: Pacientes, Fila, Vagas e tabela legada da Home operacional (atualmente vazia em produção). Municipal possui indicadores agregados e nenhum endpoint de listagem individual; estilo compartilhado vale nas duas aplicações sem criar dados/telas não aprovados.
- Modelos/API em implementação para isActive e data de cadastro existentes; Models/VM/cópia e Views/Themes em lotes coordenados. Backend da desativação segue política confirmada, com transações/autorização/concorrência em revisão e testes descartáveis pendentes.
- API implementada com migração 004 aditiva, PATCH de decisão explícita, transação compartilhada com a fila e proteção contra sessão antiga/login concorrente. Revisão técnica independente não apontou bloqueios no backend; cinco checks novos previstos, total PostgreSQL23. Build/fluxos descartáveis atuais ainda em andamento.
- Primeira rodada desktop48/50: itens, CPF completo, e-mail limitado, clipboard bloqueado na linha e VM de cópia passaram. Dois testes do harness estavam inadequados: popup sem autenticação após novo guard e escopo errado para Escape; serão corrigidos sem enfraquecer os requisitos.
- Conferência visual/revisão encontrou dois problemas de produção em validação vermelha: nome selecionado ilegível no tema Escuro (distinguir cor herdada da célula/regra da seleção/recurso do tema) e confirmação de desativação habilitada durante polling, perdendo a ação em RunAsync ocupado (distinguir busy, falha API e mudança da seleção). Regressões focadas antes das correções; próxima rodada em andamento.
- Consulta complementar enviada: Fila mostra presença do paciente; nas Vagas, que ainda não identificam o paciente no contrato, proposta de mostrar o estado da vaga à direita. A resposta sobre presença também nas Vagas ainda é necessária para fechar esse item.
- Dois testes focados reproduziram 0/1 antes das correções: Foreground da célula selecionada não seguia TextBrush; botão de confirmação não acompanhava CanOperate enquanto GET estava pausado. Célula passou a usar explicitamente a cor do tema; confirmação recebeu binding de disponibilidade e cor dinâmica. Green integrado e conferência do popup em andamento.
- Usuário confirmou Fila com presença do paciente e Vagas com estado da oportunidade. Não há decisão funcional aberta nesta tarefa. Referências anteriores a perguntas pendentes acima registram apenas a sequência histórica.
- Itens compartilhados com raio8/intervalo3, linha sem cópia, nome/e-mail abreviados e contatos completos implementados. Barra sobreposta oferece Detalhes, Desativar/Reativar em Pacientes e Cancelar entrada quando permitido na Fila. Ficha com raio18/grupos12, dados completos e quatro botões de cópia disponíveis; dados ausentes não ganham cópia fictícia. Prévia clara/escura inspecionada, sem cortes na borda; título extremo abreviado e corpo preservado.
- Documentação de design, API, arquitetura, requisitos, configurações e README atualizada. Build final integrado Release zero erros/avisos; checks52 e fluxos PostgreSQL/E2E ainda em execução.
- Retomada em 2026-10-13: todos os itens acima estão concluídos e verificados. Correção do pump do harness em SystemIdle conferida: regressão focada1/1 e integrada52/52 passaram. O timeout anterior era do harness, sem nova alteração de produção nesta retomada. Release zero erros/avisos; HTTP20/20; PostgreSQL23/23; WPF com API/PostgreSQL53/53; abertura real OBS/Municipal2/2.
- Evidências: `.build/postgres-validation-f45660f6faf04ce4bcaf8e6c253ebb27`, `.build/clients-validation-b4ce79e39f9b4b7bab651fb422a04d0f`; prévias nativas em `.build/previews-2026-10-13-final` e `-e2e`, incluindo ficha usual e lista zebra com barra. Clusters próprios encerrados e metadata removida pelo harness. Clipboard simulado nos checks preservou o clipboard pessoal.
- Papéis Aura realizados: contratos/backend, Views/UX, validação e revisão técnica independentes; principal integrou Models/VM/documentação e retomou os checks finais. Revisão técnica final aceitou correções da confirmação/cores sem novos achados. Passagem completa do checklist compartilhado sem novo bug objetivo; handlers async são eventos de UI/timer existentes, recursos/eventos têm fechamento e queries são parametrizadas.
- Limites anteriores permanecem P-007 (DPI/monitores/teclado físicos), P-003 (Android físico/recriação) e P-012 (base definitiva). Android não foi reexecutado nesta etapa Windows; contratos aditivos e regras HTTP do paciente foram conferidos. Publicação/integracão do outro computador seguem na tarefa separada de 2026-10-13.

- Publicação concluída em13/10: implementação f3ec187 preservada no merge34b7877 com a galeria remota. Push/hash local-remoto conferidos; evidência final integrada e registro em `2026-10-13-integracao-main.md`. P-019/P-020 encerradas. A nova escolha de acesso da secretária está documentada separadamente e não alterou login nesta entrega.
