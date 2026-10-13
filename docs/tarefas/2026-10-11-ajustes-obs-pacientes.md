# Ajustes de NORA OBS — Pacientes e seleção

## Skills e comandos invocados

- `skills-aura:aura`: protocolo-tarefa-complexa, diagnosing-bugs e modo-loop-verificacao.
- `my-skills:retomar-execucao`.
- Solicitado commit e push na `main` após conferir integralmente as alterações.

## Pedido, item por item

1. Na tela “NORA OBS · Pacientes e seleção · Clara · topo”, melhorar a tabela e aplicar arredondamento discreto aos inputs, menor que o arredondamento dos elementos superiores.
2. Remover “conta conectada”, “dados atualizados pela API”, o nome OBS e a conta conectada no topo direito, os botões atualizar e sair da conta dessa região.
3. Melhorar tipografia e cores da tabela conforme a referência enviada pelo usuário; distribuir nome completo, CPF e e-mail com mais espaço para nome/e-mail e menos para CPF; centralizar os dados em relação aos respectivos títulos.
4. Remover “incluir sessão na fila”, “preferência salvar nesse computador” e a frase inferior “seu espaço de cuidado, uma jornada organizada com acesso seguro”. Não colocar descrição substituta da tela.
5. Explicar ações por tooltip ao passar o mouse, conforme a imagem de referência.
6. Mostrar a tela/ação de cadastrar paciente se ainda não tiver sido mostrada.
7. Arredondar discretamente as bordas da janela restaurada do OBS e do painel ADM, conforme a terceira referência; distinguir janela restaurada de maximizada.
8. Listagem em zebra com tons branco/escuro, sem separadores marcados, com rolagem vertical e filtros acima; manter apenas o título “Pacientes” no topo esquerdo e remover descrição e “operador fixed story”.
9. Exibir indicador de paciente ativo/inativo no topo esquerdo de cada paciente; se não existir, adicionar suporte no banco. Esclarecer o significado solicitado de “ativo no nosso sistema, no outro sistema”, “utilizando ele no momento”.
10. Remover ponto azul associado ao nome; mostrar e-mail, CPF e telefone abaixo do nome; permitir informar telefone e persistir no banco.
11. Explicar a função das três barras no canto inferior esquerdo e mostrar configurações.
12. Mover “NORA Operação da Unidade” do topo da navegação para o canto inferior esquerdo como conta. Ao clicar, oferecer somente configurações e sair da conta.
13. Conferir todos os itens implementados e corrigir problemas antes de commit e push na `main`.

## Objetivo

Entregar a tela de pacientes do OBS e os ajustes compartilhados autorizados, preservando funcionamento, segurança e dados existentes, com evidência visual/funcional e publicação verificada na main.

## Restrições

- Preservar identidade visual, funcionalidade existente fora das remoções explícitas e trabalho válido local/remoto.
- Não inventar a aparência das imagens ausentes ou o significado de atividade do paciente.
- Não usar dados reais nem alterar banco de produção para validar.
- Manter solicitações seguintes separadas e em ordem; esta tarefa contém os itens ditados nesta mensagem.
- Commit/push somente depois da conferência final solicitada.

## Checklist

- [x] Ler documentação, pendências e estado real do Git; validar estado anterior.
- [x] Receber/localizar referências visuais e esclarecer atividade e alcance dos inputs.
- [x] Ajustar inputs e janela restaurada OBS/ADM (raio 5 px dos inputs e preferência DWM nativa verificados; DPI/monitores físicos em P-007).
- [x] Remover textos/botões solicitados.
- [x] Melhorar distribuição, tipografia, zebra, filtros e rolagem de pacientes.
- [x] Implementar/verificar status e telefone de ponta a ponta.
- [x] Mover conta e validar menu configurações/sair e tooltips.
- [x] Mostrar cadastro/configurações atuais por prévias e explicar três barras.
- [x] Revisar pedido completo por implementação, UX, técnica e testes.
- [x] Executar build, testes pertinentes, fluxos de API/banco descartável e verificação visual possível.
- [x] Atualizar documentação e registro único de pendências (estado parcial real; atualizar resultados ao final).
- [x] Revisar diff, criar commit, push main e verificar hash remoto.

## Estado

- Pedido registrado antes de investigar código.
- Estado inicial: main acompanhando origin/main, árvore limpa em `git status --short --branch`.
- Skills invocadas e suas três dependências lidas.
- Referências mencionadas (três imagens) não vieram anexadas à mensagem; sem confirmação de que estejam no projeto.
- Sem alterações de implementação, build, testes, commit ou push nesta tarefa até agora.
- Confirmações do usuário: “ativo” significa usando o APPNORA naquele momento (online); inputs arredondados somente OBS e painel ADM Windows.
- Documentação relevante e histórico atual lidos: HEAD e origin/main locais em `be00522`; ativação OBS e novo ADM continuam pendentes em P-014/P-015. Esta tarefa visual não autoriza resolver políticas de ativação/financeiro anteriores.
- Baseline atual: `dotnet build NORA.slnx --configuration Release --nologo -m:1 --disable-build-servers` passou com zero erros e zero avisos.
- Pergunta sobre formato final (colunas centralizadas versus nome com contatos abaixo) enviada; referências visuais ainda ausentes.
- Formato confirmado: linhas com nome e e-mail/CPF/telefone abaixo, conforme a imagem citada; descartar a alternativa de três colunas na implementação final.
- Telefone opcional, preservando cadastros antigos sem telefone.
- Presença: usuário deseja manter online também em segundo plano (tela inicial/outro app), até fechar nos aplicativos recentes/“Fechar tudo”. A implementação Android/servidor ainda deve tratar suspensão de processo, perda de conexão e limite de detecção sem afirmar encerramento instantâneo garantido.
- Baseline WPF executado: 33/33 checks passaram, com fixtures HTTP sintéticas e render nativo dos três temas. E2E PostgreSQL não foi solicitado nesta execução; nenhum banco de uso acessado.
- Revisões de intake por implementação/contratos, UX, técnica e validação confirmam cadastro existente em expansor, três barras = recolher/expandir sidebar (`Ctrl+B`), ausência de telefone/presença no banco/contratos atuais e necessidade de adaptar testes às remoções.
- Acrescentada captura reproduzível do cadastro expandido ao harness existente; nenhuma tela de produção foi alterada ainda. Capturas atuais em `.build/previews-20261011-baseline` incluem configurações e cadastro com dados sintéticos.
- Implementação visual aguarda os três anexos necessários para copiar tabela/listagem, tooltip e raio da janela. Commit e push ainda não realizados.
- Verificação do acréscimo de prévia: harness recompilado/reexecutado, 33/33 passaram e `git diff --check` sem erros. Inspeção do PNG confirmou campos do cadastro; captura é do viewport, com botão inferior parcialmente cortado, e não prova layout integral do formulário ou moldura nativa. Ajustes de produção continuam pendentes em P-016.
- Loop da prévia: duas revisões de render; solicitado trazer o botão de cadastro à vista, sem corrigir o layout de produção antes das referências. Não houve erro funcional reproduzido; diagnosing-bugs permaneceu de prontidão.

### Retomada com referências — 2026-10-11

- Usuário pediu continuação do mesmo pedido e enviou três imagens: tooltip azul claro, listagem AEV em linhas zebra com nome/contatos e conta inferior, e janela AEV restaurada com cantos discretos.
- Estado preservado: alterações anteriores apenas em documentação e captura de cadastro do harness; nenhuma implementação de produção concluída.
- As confirmações já registradas (online APPNORA em segundo plano, telefone opcional e inputs apenas Windows) continuam válidas.
- Próxima etapa: ler documentação e código existente, validar baseline atual e implementar os itens pendentes com revisões por papéis e validação integrada; commit/push main seguem autorizados após a conferência.
- Baseline reexecutado nesta retomada: Release zero erros/avisos, 33/33 checks desktop e abertura 2/2 dos executáveis.
- As três imagens foram inspecionadas. As referências estão disponíveis; o formato de linhas nome/contatos já estava confirmado.
- Em implementação: API/banco/Android para telefone opcional e presença por sessão, Views/Themes WPF e ViewModel/transporte desktop. Revisões por papéis Aura em andamento.
- Pacientes perde a ação de inclusão na fila como pedido; a operação permanece disponível na própria tela Fila, preservando o fluxo operacional.
- Para presença em segundo plano, pergunta enviada sobre notificação fixa “NORA · conexão ativa”; não implementar etapa dependente antes da resposta. API/banco e restante continuam independentemente.
- API implementada: migração 003 aditiva, telefone opcional normalizado, presença por sessão autenticada recente (90 segundos), logout/expiração e ordenação por cadastro recente. Build API/checks zero erros/avisos; PostgreSQL em validação.
- Primeira tentativa PostgreSQL descartável limitada pelo sandbox em socket loopback (SocketException 10013); cluster próprio encerrado. Reexecução com acesso local fora do sandbox solicitada, sem usar banco de produção.
- PostgreSQL descartável reexecutado com acesso loopback autorizado: HTTP 20/20 e fluxos PostgreSQL 18/18 passaram; cluster próprio encerrado, evidência `.build/postgres-validation-59e0c47d0a054bf9bc452e1201d33182`.
- Loop integrado reproduziu dois bugs em 37/39 checks desktop: atualização perdia seleção na Fila quando filtro Pacientes a excluía; recuperação após erro inicial mantinha aviso. Causas locais: escolha de FilteredPatients na Fila e ausência de marcação refreshFailed no carregamento inicial. Correções aplicadas e revalidação pendente.
- Hipóteses discriminadas pelos testes e bindings: filtro/seleção (confirmada), rebind DataGrid (em conferência) e identidade do registro (mantida); recuperação estava executando, porém flag de erro não era marcada (confirmada), descartando timer parado ou dados ainda indisponíveis.
- Rodada agrupada 38/42 desktop: as duas correções anteriores passaram. Novas reproduções: polling desabilitava inputs do rascunho; texto gerado do botão primário não herdava contraste; TextBox/PasswordBox mediam 61 px por padding duplicado. CanEditDraft separa edição de rascunhos da disponibilidade de mutações; Views/Themes em correção após testes vermelhos.
- Teste de rolagem nativa da lista longa após polling passou; não foi reproduzido retorno ao topo, portanto não houve refatoração especulativa da coleção.
- Revisão independente técnica concluída: API/banco, contratos, recursos, segurança e checklist Aura sem bloqueio adicional. Android ainda tinha somente transporte; iniciado heartbeat enquanto app visível (independente da escolha sobre notificação/segundo plano), com decisão pendente preservada.
- Os quatro papéis foram executados com responsáveis distintos: implementação/contratos, UI/UX, validação iterativa e revisão técnica independente; principal consolidou ViewModel/HTTP e documentação.
- Rodada integrada desktop 43/43 passou; Release zero erros/avisos. Cinco causas corrigidas com seis verificações que falharam antes: seleção Fila, recuperação do aviso, rascunho desabilitado durante polling, herança de contraste Primary e padding duplicado TextBox/PasswordBox. Varredura final sem achados adicionais nesta etapa WPF/API.
- Capturas de 1280×820 mostravam recorte/fundo preto por limite nativo do ambiente de teste (janela solicitada maior que área disponível). Harness será ajustado para representar os limites reais do cliente; não alterar produção para mascarar esse limite.
- Android transporte compilou APK/app/tests, 7/7 testes locais e lint zero erros/um aviso de versão; heartbeat enquanto tela visível em implementação. Não há dispositivo conectado nesta rodada.
- Rodada final em área cliente real 1280×740 revelou duas falhas de layout/atualização antes ocultas pela captura maior: rolagem externa adicional de 18 px em 980×640, corrigida pela reserva de altura da listagem; depois reset da rolagem durante polling sem mudança de dados (43/44). A coleção era limpa/reinserida e FilteredPatients notificado em toda atualização. Correção aplicada para comparar os registros e só substituir/notificar coleções e seleção quando mudam; revalidação pendente.
- Pixel_4 iniciado sem apagar dados, boot concluído e conectado nesta rodada. Android agora envia presença a cada 30 segundos enquanto a Activity está visível, encerra ao sair desse estado e retém o controller em AndroidViewModel. Instrumentação atual e fluxo HTTP real em conferência; presença em segundo plano continua aguardando a decisão sobre notificação fixa.
- Usuário confirmou “Sim, manter online com a notificação” para “NORA · conexão ativa”. Serviço de presença em segundo plano em implementação, com encerramento ao sair da conta/fechar nos recentes; não persistir token nem reiniciar uma sessão após morte do processo.
- Correção da rolagem revalidada nesta retomada: solução Release zero erros/avisos, 43/43 checks desktop sem E2E e abertura real OBS/Municipal 2/2 passaram. Nova captura inclui o teste da ausência de rolagem externa e preservação do offset em 980×640/1280×740 nos três temas. Revisão técnica independente não apontou bloqueios no diff. Rodada com E2E acrescenta um check (44 no total) e ainda será executada com o Android atualizado.
- Prévias finais em `.build/previews-2026-10-11-final`: listagem zebra, menu de duas ações, tooltip e configurações via conta inspecionados. Cadastro final com PostgreSQL aguardando a execução integrada; moldura nativa não aparece no render do cliente e seu pedido DWM foi lido de volta nos testes.
- Serviço Android foreground implementado após autorização: notificação fixa, heartbeat de 30 segundos, sem encerrar no Home/outro app, encerramento no logout/remoção da tarefa e tentativa final de offline com timeout. Sem persistência de token, serviço privado e START_NOT_STICKY. Revisão identificou dois problemas objetivos antes da execução final: PendingIntent podia recriar Activity/descartar controller; offline tardio de geração antiga podia apagar presença nova. Flags de reutilização da Activity e guard de geração corrigem as causas. Testes instrumentados acrescentam próximo heartbeat real em segundo plano, toque no PendingIntent preservando Activity/ViewModel, remoção da tarefa e guarda de offline antigo. Primeira compilação app/APK testes/locais/lint passou; após essas correções, recompilação/execução integrada pendentes.
- Git remoto atualizado novamente: main/origin/main continuam iguais em be00522, sem avanço local/remoto. Nenhum commit/push desta tarefa até agora. Sessões geradas `.kotlin/` passaram a ser ignoradas; arquivos locais preservados.
- Segunda compilação Android após flags/geração passou com APK app/testes, testes locais e lint (zero erros; apenas aviso já conhecido de versão disponível). A rodada integrada WPF/PostgreSQL passou 44/44 e iniciou os instrumentados Android.
- Inspeção do cadastro E2E em tema Escuro reproduziu rótulos pretos no conteúdo do Expander, apesar da lista/menu usarem o contraste correto. Acrescentada regressão dos rótulos nos três temas antes da correção; validação vermelha em andamento. A captura clara do cadastro mostra todos os campos/telefone/botão; a captura após cadastro com mais pacientes exige rolagem externa normal do formulário.
- Regressão dos rótulos confirmou vermelho 43/44 sem E2E; causa era Foreground padrão preto do Expander. Recurso compartilhado agora usa TextBrush e o formulário claro/escuro/sistema passou. Rodada final WPF/PostgreSQL **45/45**; Release zero erros/avisos. Android integrado **14/14** instrumentados sem falhas/erros/skips, **7/7** locais e lint zero erros/um aviso de versão disponível. Serviço em segundo plano, próximo heartbeat, toque na notificação, remoção da tarefa e geração/401 comprovados. Clusters próprios encerrados, dados sintéticos preservados como evidência.

### Conferência final do pedido

1. Inputs OBS/painel: raio 5 px, altura 42 px; recursos compartilhados e contrastes nos três temas conferidos.
2. Textos conectada/API, nome/conta do topo e botões Atualizar/Sair da região removidos; testes nativos verificam ausência em Pacientes.
3. Formato final confirmado pelo usuário: nome acima e contatos abaixo; alternativa de colunas centralizadas substituída por essa confirmação. Tipografia/cores, nome longo e contato longo conferidos.
4. Inclusão na fila removida de Pacientes e preservada na própria Fila; mensagens inferiores de preferências/motivação removidas, sem descrição substituta. Erros reais continuam informados.
5. Tooltips claros com raio 6 px e texto descritivo aplicados; menu, hamburger, filtros e ações conferidos.
6. Cadastro completo mostrado em prévias finais clara/escura, com telefone opcional e botão; persistência e login da conta nova conferidos com PostgreSQL descartável.
7. OBS/painel restaurados solicitam DWM de cantos pequenos; maximização/tela cheia usam bordas retas. Preferência nativa lida de volta nas transições; conferência física DPI/monitores continua em P-007.
8. Título Pacientes, filtros acima, zebra sem divisões fortes e rolagem vertical; nenhuma rolagem externa adicional com cadastro recolhido em 980×640/1280×740; polling sem alterações mantém offset.
9. Indicador online/offline significa presença no APPNORA, inclusive em segundo plano como confirmado. Heartbeat30s, timeout90s, logout/múltiplas sessões e finalização nos recentes conferidos.
10. Sem marcador azul do nome; e-mail/CPF/telefone abaixo, cadastros antigos sem telefone preservados; normalização/validação e migração aditiva reaplicável conferidas.
11. Três barras recolhem/expandem a navegação (`Ctrl+B`); ação e tooltip conferidos. Configurações abertas pelo menu e captura final disponível.
12. Conta NORA/Operação da Unidade no canto inferior esquerdo; menu contém somente Configurações e Sair da conta, com navegação/logout conferidos.
13. Revisões de implementação, UX, técnica e validação executadas com quatro responsáveis; causas corrigidas e rodada final sem novos achados. Implementação publicada na main e hash remoto confirmado; P-003/P-007/P-012/P-013/P-014/P-015 são limites ou tarefas anteriores preservadas.

- Abertura final após o ajuste do Expander reexecutada: OBS e painel **2/2** com janelas responsivas. Clusters de todas as rodadas desta tarefa encerrados pelo próprio harness; emulador Pixel_4 iniciado para os testes encerrado sem apagar seus dados. Nenhum banco de produção foi acessado.

### Publicação verificada

- Commit de implementação `7dff75b` criado na main, com 43 arquivos revisados; staging explícito, sem artefatos gerados nem credenciais reais.
- Novo fetch antes do push confirmou somente um commit local e nenhum avanço remoto; push normal `be00522..7dff75b` concluído.
- `git rev-parse HEAD` e `git ls-remote origin refs/heads/main` retornaram o mesmo hash `7dff75b6ad68d1c923a0e74d4d6a5d94799ec2ee`.
- P-016 movida para Resolvidas. Este registro documental sucede a publicação já comprovada; os limites e tarefas anteriores permanecem abertos.
