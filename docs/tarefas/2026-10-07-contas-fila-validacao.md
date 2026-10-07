# Contas iniciais, prazo da fila e validações

## Skills e comandos invocados

- `skills-aura:aura`: protocolo de tarefa complexa, diagnóstico e loop de verificação.
- `my-skills:refatoracao-limpeza-codigo`: limpeza do projeto preservando o comportamento, em etapa separada da implementação.

## Pedido, item por item

1. Confirmar o identificador de login e o provisionamento das contas iniciais, usando os dados fornecidos privadamente pelo usuário para os três sistemas.
2. Criar um login por sistema; deixar contas previamente cadastradas no OBS e no Painel Municipal.
3. Cadastrar a conta do aplicativo Android utilizando o OBS.
4. Aplicar prazo de **10 minutos para todos**, independentemente da posição, passando ao próximo após recusa ou expiração.
5. Conferir Android em outro AVD ou celular, preservando o AVD existente com falhas de System UI.
6. Conferir teclado, DPI ampliado e múltiplos monitores no desktop.
7. Resolver a escolha de prioridade para respeitar a ordem das tarefas.
8. Aplicar a limpeza de código invocada, mantendo sua execução e validação separadas das mudanças funcionais.

## Objetivo

Disponibilizar autenticação e contas reais nos três clientes, com persistência PostgreSQL e cadastro do paciente pelo OBS; registrar e aplicar o prazo confirmado da Vaga Relâmpago, executar as validações reais e a limpeza na ordem escolhida.

## Restrições

- Nenhuma senha, token ou credencial em arquivos versionados, documentação, comandos exibidos ou logs. Os dados de acesso recebidos permanecem privados; a senha será armazenada somente por hash apropriado.
- Preservar trabalho existente, dados reais, AVD pessoal, stack Kotlin/Compose, WPF e ASP.NET Core e a identidade visual.
- Manter separação das solicitações na fila; aguardar resposta às dúvidas reais de identificador/prioridade antes de executar etapas dependentes.
- Sem simular autenticação, persistência, cadastro de pacientes ou sucesso de testes.
- Autorização de push anterior foi cumprida; este pedido não solicita outra publicação.

## Checklist

- [x] Ler o pedido e as skills invocadas e criar este documento antes da investigação.
- [x] Ler documentação, pendências e regras locais; conferir o estado real do Git.
- [ ] Confirmar identificador de login e prioridade com o usuário.
- [ ] Identificar PostgreSQL/configuração disponível e preservar bases existentes.
- [ ] Implementar provisionamento seguro e autenticação com contas separadas por sistema.
- [ ] Integrar login real no OBS e Municipal.
- [ ] Implementar e usar cadastro de paciente pelo OBS; integrar login Android.
- [ ] Registrar/aplicar prazo de 10 minutos, recusa, expiração e próxima oferta; validar com tempo controlado.
- [ ] Executar builds, testes HTTP/persistência e fluxos dos clientes.
- [ ] Conferir Android em ambiente estável e desktop com teclado/DPI/monitores; registrar limitações reais.
- [ ] Executar limpeza separadamente conforme prioridade, com busca de referências e validação de equivalência.
- [ ] Consolidar quatro revisões Aura, passar o checklist de bugs e atualizar documentação/pendências.

## Estado

- Pedido registrado sem copiar dados pessoais ou senha para o Git.
- Perguntas enviadas sobre CPF/e-mail/ambos e ordem de execução; sem resposta ainda.
- Nenhuma implementação funcional ou limpeza iniciada nesta tarefa.
- Git inicial: `main` em `d6e4038`, sincronizada com `origin/main`, sem alterações preexistentes; somente este documento e a atualização dos requisitos/pendências foram feitos nesta etapa.
- Quatro papéis Aura em leitura: implementação/API/persistência, interface WPF/UX, Android/técnica e testes/validação. Revisões confirmaram ausência de autenticação, cliente HTTP e persistência clínica; o cadastro exige implementar o fluxo real e não inserir credenciais na interface.
- Menor solução proposta: PostgreSQL via Npgsql já disponível no cache; hashes de senha do framework; sessões opacas revogáveis com hash do token persistido; três contas totais separadas por escopo, uma para OBS, uma para Municipal e uma para paciente. Proposta ainda não implementada.
- Fora do sandbox, serviços PostgreSQL 17 e 18 confirmados `Running`, aceitando conexões em `127.0.0.1:5432` e `127.0.0.1:5433`. Não foi acessado nem alterado qualquer banco; falta escolha da instância, usuário e configuração privada da conexão.
- Perguntas adicionais enviadas sobre nome completo das contas, instância/usuário PostgreSQL e critério de escolha do próximo paciente. Prazo de 10 minutos está confirmado; ordenação não foi inferida.
- Android: somente AVD `Pixel_4` listado; imagens locais API31 e API36.1 disponíveis, sem `avdmanager`. Outro AVD pode ser criado pelo Device Manager com a imagem já instalada. Nenhum AVD foi criado, iniciado ou apagado; nenhum novo teste Android executado.
- Computer Use: runtime `@oai/sky` inicializado e `list_apps` executado com sucesso. Isso não comprova teclado, foco, DPI ou múltiplos monitores; nenhum cliente NORA foi controlado nesta etapa.
- Regressões a preservar: 24 checks desktop e startup; checks HTTP reais. O check HTTP de ausência de login e o check Android de logout desabilitado precisam de atualização ao implementar os novos fluxos, sem enfraquecer segurança ou testes anteriores.
- Limpeza: apenas análise preparatória; nenhuma remoção de código ou mudança de comportamento. A etapa permanece separada e aguarda a prioridade escolhida.
- Resultado deste checkpoint: documentação atualizada e análise consolidada; implementação e validações de ponta a ponta pendentes das respostas, registradas em P-001/P-003/P-007/P-009/P-011. Nenhum build/teste de código novo foi executado porque não houve mudança de código.
