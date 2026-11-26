# Projeto-NORA

Plataforma NORA para acompanhamento do paciente, operação de unidades de saúde e gestão municipal. Implementa acesso do paciente por CPF ou e-mail (a equipe, só por e-mail) com validação de formato e dígitos, redefinição de senha por código enviado ao e-mail, cadastro de pacientes pelo OBS com consulta opcional de endereço por CEP, fila por entrada, Vaga Relâmpago de10 minutos e totais agregados, além dos temas/configurações dos três aplicativos.

- **APPNORA:** aplicativo Android em Kotlin e Jetpack Compose.
- **OBS-NORA:** aplicativo WPF para operação da unidade.
- **Painel-Municipal:** aplicativo WPF para leitura gerencial.
- **Nora.Desktop:** biblioteca compartilhada de interface, temas e preferências dos desktops.
- **Nora.Api:** ASP.NET Core .NET10 com PostgreSQL18, autenticação/sessões, pacientes, fila/ofertas, agregados e erros HTTP padronizados.

A identidade dos desktops usa roxo; o APPNORA do paciente segue a paleta específica branca e azul-marinho. Cartões e navegação inspirados no protótipo foram reconstruídos nas tecnologias nativas. O React do ZIP serve como referência de design; não é a base técnica nem define as regras de negócio finais.

## Estado em 2026-11-17

Os três clientes (OBS, Secretaria e APPNORA) operam só pelo fluxo por UBS: cada UBS tem banco próprio, criado pela API ao cadastrar a unidade, e o fluxo global antigo foi removido dos clientes. A API calcula o score de comparecimento e os indicadores por UBS e por rede, o OBS e a Secretaria mostram indicadores, histórico, contas e estado da base, e o app mostra o score na Jornada, avisa ofertas e deixa sair da fila, remover UBS e trocar a senha. A Fase 3 acrescentou agenda pelo catálogo (especialidades da rede, profissionais, consultórios, grade e bloqueios), agendamento e confirmação de presença pelo paciente, capacidade, metas e alertas avaliados, comunicados, Jornada com exames e vacinas, Família (o responsável age pelo dependente depois da aprovação da UBS), troca do e-mail da conta por código quando a UBS corrige o e-mail de referência, cancelamento das consultas futuras ao desativar o paciente na UBS e limpeza periódica de sessões e códigos vencidos. Última rodada: API HTTP 30/30; PostgreSQL 18 com 28/28 fluxos, 19/19 plataforma, 17/17 revisão, 16/16 comunicação e Jornada, 20/20 agenda, 21/21 Família, e-mail e manutenção e 11/11 correções da revisão da Família; desktop 140/140; Android 217 testes locais e 48 previews (números da Fase 3; os testes instrumentados do Android passaram a rodar no emulador em 2026-11-16). A rodada final (2026-11-17) acrescentou ativação de computadores do OBS, "Chamar próximo", serviços e horários da UBS, recusa de consulta sobreposta em outra UBS e aprovação opcional das vagas reaproveitadas; últimos números em [Execução e validação](docs/execucao.md) (fechamento de 2026-11-17: desktop 181/181, API com todas as suítes verdes, Android com 295 testes locais e lint sem erros; E2E desktop com banco 184/184 e instrumentados do Android 46 aprovados e 2 pulados por assumption, conferidos em 2026-11-18; mesclado na `main` em 2026-11-18 depois de 8 voltas de revisão, a última sem achados; P-055 resolvida). Entrar com Google e push por servidor estão prontos na API e no app, desligados até o usuário criar as credenciais do Google (P-042; como ligar em [Configurações](docs/configuracoes.md)). Antes de publicar esta versão rode `Nora.Api --migrate` e depois, em outra execução, `Nora.Api --migrate-all-units` (central até a migração 022, UBS até a 017; uma UBS sem a 017 responde 503 `unit-database-outdated`). Detalhes em [Integração back x front](docs/integracao-back-front.md), [Execução e validação](docs/execucao.md) e [Pendências](docs/PENDENCIAS.md).

## Escopo da primeira etapa

Temas Clara, Escura e Sistema, escolha persistida, navegação inicial e Configurações. Os desktops compartilham preferências da interface e atalhos; o Android possui preferências locais da interface e de notificações. A interface exibe estados vazios quando não há integração de dados.

A API persiste contas, sessões, fila e ofertas. OBS aceita atendente/administrador; Android usa conta individual; Municipal recebe totais e pode consultar, por solicitação, os cadastros de referência da UBS selecionada. Recusa/expiração passa ao próximo, com prazo de10 minutos para todos e controle concorrente no servidor. As preferências são locais e separadas por app; credenciais/token não são persistidos nos clientes.

A conexão privada e o provisionamento definitivo (P-012) foram encerrados em 2026-11-17: a demonstração usa o banco próprio de `scripts/demo-local.ps1` e o piloto usa o Neon (ver [hospedagem.md](docs/hospedagem.md) e [configuracoes.md](docs/configuracoes.md)). Na primeira etapa, consultas/agendamento, notificações externas e séries/filtros analíticos ficaram para definição posterior (P-013); o agendamento e os filtros dos indicadores vieram depois (ver o estado acima), e o push por servidor segue sem definição (P-042). Não há dados fictícios de produção; testes de integração usam banco descartável, e capturas visuais usam fixtures isoladas somente nos harnesses/previews.

## Documentação

- [Galeria de10/10: 83 capturas dos três aplicativos](../../Claude-HTML/Projeto-NORA/galeria/2026-10-10/index.html).
- [Cobertura visual, imagens principais e resultados](../../Claude-HTML/Projeto-NORA/galeria/2026-10-10/README.md).

- [Briefing visual original](docs/briefing-visual.md).
- [Referência visual e padrões de design](docs/design.md).
- [Arquitetura](docs/arquitetura.md).
- [Configurações e atalhos](docs/configuracoes.md).
- [Execução e validação](docs/execucao.md).
- [Painel operacional da UBS](docs/obs-painel-operacional.md).
- [Gestão / Secretaria](docs/secretaria.md).
- [Documento da tarefa e validações](docs/tarefas/2026-10-07-inicio-plataforma-nora.md).
- [Pendências](docs/PENDENCIAS.md).
- [Dados de demonstração para capturas de tela](docs/dados-demonstracao.md) (`scripts/popular-dados-demonstracao.ps1`: só acrescenta, repetível, com `-Simular` e `-Remover`).
- [Integração back x front por UBS](docs/integracao-back-front.md).
- [App do paciente](docs/app-paciente.md).
- [Hospedagem (Render + Neon, plano B na Hostinger)](docs/hospedagem.md).
- [Distribuição dos três aplicativos](docs/distribuicao.md).
- [Simulação na escola (instalação e roteiro)](docs/simulacao-escola.md); cronograma em Word: [cronograma-simulacao-escola.docx](../Mateus-Projects/Claude-PLANS/Projeto-NORA/cronograma-simulacao-escola.docx).
- [Documento da tarefa das pendências finais](docs/tarefas/2026-11-16-pendencias-finais.md).
- [Roteiro da demonstração (17/11/2026)](../Mateus-Projects/Claude-PLANS/Projeto-NORA/roteiro-demonstracao.md).
- [Documento da tarefa da integração back x front](docs/tarefas/2026-11-11-integracao-back-front.md).
- [Requisitos funcionais confirmados](docs/requisitos-funcionais.md).
- [API e contratos HTTP](docs/api.md).
- [Emulador Android e passo a passo](docs/android-emulador.md).
- [Retomada atual](docs/tarefas/2026-10-07-continuacao-funcionalidades-testes-api.md).
- [Conferência do prompt e pendências do usuário](docs/tarefas/2026-10-07-conferencia-prompt-visual.md).
- [Retomada e publicação atual](docs/tarefas/2026-10-07-retomada-implementacao-publicacao.md).
- [Retomada e fila de solicitações de 08/10](docs/tarefas/2026-10-08-retomada-e-fila-solicitacoes.md).

A Secretaria exibe contagens agregadas dos estados das ofertas de Vaga Relâmpago e permite exportar o resumo carregado em CSV. O que ainda não tem dado na API, como as vagas reaproveitadas com confirmação clínica, continua identificado na interface.

## Desenvolvimento

Os desktops usam .NET 10 para Windows. A solução integrada é `NORA.slnx`. Os outputs .NET ficam em `.build/`, preservando os artefatos antigos que já estavam rastreados.

```powershell
dotnet build NORA.slnx --configuration Release -m:1
dotnet run --project OBS-NORA/OBS-NORA.csproj
dotnet run --project Painel-Municipal/Painel-Municipal.csproj
dotnet run --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj
dotnet run --project Nora.Api/Nora.Api.csproj
dotnet run --configuration Release --project tests/Nora.Api.Checks/Nora.Api.Checks.csproj
# Depois do build Release, testa a abertura dos executáveis WPF:
.\tests\desktop-startup.ps1
```

O Android é aberto pela pasta `APPNORA` no Android Studio. Use o wrapper do próprio projeto para compilar; os comandos e requisitos de ambiente estão em `docs/execucao.md`. O APK de depuração está em `APPNORA/app/build/outputs/apk/debug/app-debug.apk` após o build.

Para iniciar PostgreSQL18/API local e provisionar contas privadamente, após build use `scripts/iniciar-local.ps1 -ProvisionAccounts` (senha oculta). Cadastre o paciente pela interface OBS. Nas próximas aberturas use o script sem a opção de provisionamento; configure endpoint nos clientes. Veja `docs/api.md`.

## 2026-10-07 — Fundação da plataforma

- **Alteração:** reconstrução nativa da base visual e das configurações dos três aplicativos.
- **Motivo:** iniciar a plataforma preservando a experiência da referência e uma identidade coerente.
- **Validação:** solução WPF em Release com zero erros/avisos; 21 checks desktop passaram. Android compilou app e testes, passou nos três testes locais e no lint (zero erros, um aviso de versão disponível).
- **Estado:** primeira etapa implementada e revisada. Validação Android em dispositivo pendente por falta de hypervisor/dispositivo conectado (P-003); quatro casos instrumentados apenas compilados. API e fluxos clínicos dependem da próxima especificação (P-001).

## 2026-10-07 — Retomada, API técnica e testes reais

- **Alteração:** fundação ASP.NET Core da API; correção do crash de abertura dos dois executáveis WPF (P-008); testes de botões/fechamento e regressão dos executáveis.
- **Validação:** solução Release com zero erros/avisos, 23/23 checks WPF, abertura real dos dois clientes, 20/20 checks HTTP; Android com 3/3 testes locais e 4/4 instrumentados no Pixel_4 API31, lint zero erros/um aviso. WHPX já funcional, sem instalação de driver.
- **Estado:** implementação clínica, banco e logins ainda pendentes de decisões/implementação (P-001). Inspeção Android extra bloqueada por falhas do System UI do AVD (P-003); teclado/DPI/monitores WPF em P-007. Limpeza solicitada depois registrada na fila (P-009). Evidências e continuidade no documento da retomada.

Conferência complementar: corrigida navegação WPF que herdava a rolagem da tela anterior (P-010). **24/24 checks desktop**, build Release zero erros/avisos e abertura dos dois executáveis atualizados passaram. O prompt reenviado mantém o mesmo escopo visual; análise e limites da implementação estão no documento de conferência, sem apresentar os módulos clínicos como prontos.

## 2026-10-08 — Acesso, fila e persistência integrados

Login por CPF/e-mail (na época; hoje a equipe entra só por e-mail), cadastro OBS, fila por entrada, ofertas de dez minutos e totais isolados estão implementados nos clientes/API/PostgreSQL 18. Passaram HTTP 20/20, persistência/fluxos 15/15, WPF 34/34 incluindo controles nativos com banco real e Android 10/10 instrumentados sem skips. Os dois executáveis abriram responsivos; build Release zero erros/avisos. Android 7/7 locais reexecutados, lint zero erros/um aviso de versão disponível. Corrigidas mensagem de atualização WPF, inicialização da API em pasta diferente e manutenção da decisão Android confirmada após falha de recarga. Harnesses reproduzíveis em docs/execucao.md.

Limpeza retirou 37 artefatos de IDE/build somente do Git, preservando arquivos locais e hashes. P-001/P-002/P-009/P-011 resolvidas; na época, o provisionamento definitivo P-012 e as definições clínicas P-013 permaneciam e as solicitações de chave OBS e painel administrativo estavam na fila P-014/P-015; hoje P-012, P-014 e P-015 estão encerradas (ver `docs/PENDENCIAS.md`).

## 2026-10-10 — Galeria e revisão visual

83 imagens atuais: 68 WPF nativas e 15 Compose/Layoutlib. Corrigido contraste do texto efetivo dos botões principais desktop; fotos regeneradas, checks39/39 e Android15/15 renders/comparações e7/7 locais. Galeria com filtros/busca/ampliação, fontes sintéticas identificadas e hashes das cópias. Solução Release zero erros, quatro avisos de consulta NuGet indisponível nesta rede. Sem E2E PostgreSQL ou execução Android em dispositivo nesta rodada; P-003/P-007 mantêm os limites físicos, P-014/P-015 continuam pendentes. Detalhes e reprodução na documentação da galeria.

## 2026-10-11 — Pacientes OBS e acabamento Windows

A listagem passa a usar linhas zebra com nome, contatos e presença recente no APPNORA; o telefone é opcional no cadastro e no PostgreSQL. Filtros ficam acima da lista, inputs têm arredondamento discreto e as dicas aparecem pelo mouse. A conta no canto inferior da navegação abre Configurações e Sair; inclusão na fila fica na própria tela Fila. OBS/Municipal usam cantos nativos pequenos na janela restaurada do Windows 11.

Conferência e estado de publicação em [tarefa dos ajustes](docs/tarefas/2026-10-11-ajustes-obs-pacientes.md). A API de presença usa sessões e prazo de 90 segundos; o Android mantém a conexão em segundo plano com a notificação “NORA · conexão ativa”, autorizada pelo usuário, e encerra ao sair ou fechar nos recentes. Passaram WPF45/45 com PostgreSQL e Android14/14 instrumentados sem skips, incluindo o serviço de presença. As políticas de ativação OBS/novo painel financeiro continuam em P-014/P-015.

As listagens Windows agora compartilham itens com raio de 8 px e intervalo de 3 px, barra flutuante por seleção e ficha por duplo clique rápido/Enter. CPF completo e telefone ficam visíveis; a cópia de nome/contatos fica na ficha. Administrador e atendente podem desativar/reativar pacientes com confirmação: desativar revoga acesso e cancela pendências com avanço FIFO; reativar permite novo login. Contrato/migração em [API](docs/api.md) e evidência desta etapa na [tarefa das listagens](docs/tarefas/2026-10-12-listagens-acoes-detalhes.md).

Integração de13/10 preserva a galeria do outro computador e as listagens: desktop58/58 com galeria,55/55 com PostgreSQL, Android7/7 locais e15/15 renders/comparações passaram. [Prévias atuais de pacientes/detalhes](docs/galeria/2026-10-13/README.md) e [registro da integração](docs/tarefas/2026-10-13-integracao-main.md). Usuário escolheu ativação do computador + login da secretária com manter conectado; implementação futura e detalhes de isolamento permanecem em P-014.

A [galeria com fotos atualizadas](../../Claude-HTML/Projeto-NORA/galeria/2026-10-13-atualizada/index.html) mantém os83 itens e o site existente:68 fotos Windows substituídas e15 Android preservadas. Correções de captura e do nome no seletor da Fila conferidas em desktop64/64,61/61 com PostgreSQL e navegador14/14; [registro da atualização](docs/tarefas/2026-10-13-atualizar-fotos-galeria.md).

## 2026-11-15 — Integração back x front por UBS

- **Alteração:** remoção do fluxo global dos clientes; OBS completo por UBS (faixa única de retorno, carga da UBS no login, agenda por mês, fila com posição e espera, indicadores, participação na ficha, ofertas de uma vaga); Secretaria com indicadores da rede com filtros reais, contas OBS, histórico paginado e estado da base; APPNORA com ofertas com prazo, aviso local a cada 30 s, score na Jornada, sair da fila, remover UBS e trocar senha. Na API: banco por UBS criado automaticamente, score de comparecimento, indicadores, histórico, contas e ações do paciente, mais 15 correções da revisão crítica (pool por UBS, revalidação de sessão, versão do schema, limites por conta).
- **Validação:** build Release 0/0; API HTTP 30/30; PostgreSQL 18 com 28/28 fluxos, bancos separados, 19/19 plataforma e 17/17 revisão; desktop 103/103; E2E com banco 104/104; Android 84/84 locais, 28 previews, lint 0 erros e 6 avisos antigos.
- **Estado:** pendências P-023 a P-029, P-031 a P-035 e P-039 resolvidas. Abertas: P-030 (avaliação dos alertas), P-036, P-037 (Fase 3 em andamento) e P-040 (instrumentados do Android só compilam). Reprodução em `docs/execucao.md`.
