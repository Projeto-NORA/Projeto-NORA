# Projeto-NORA

Plataforma NORA para a jornada do paciente, operação de unidades de saúde e gestão municipal. Esta primeira entrega, confirmada pelo usuário, implementa a base visual e as configurações locais dos três aplicativos.

- **APPNORA:** aplicativo Android em Kotlin e Jetpack Compose.
- **OBS-NORA:** aplicativo WPF para operação da unidade.
- **Painel-Municipal:** aplicativo WPF para leitura gerencial.
- **Nora.Desktop:** biblioteca compartilhada de interface, temas e preferências dos desktops.
- **Nora.Api:** fundação ASP.NET Core .NET 10 com liveness e erros HTTP padronizados, validada com requisições reais.

A identidade usa roxo, cartões e navegação inspirados no protótipo fornecido, reconstruídos nas tecnologias nativas. O React do ZIP serve como referência de design; não é a base técnica nem define as regras de negócio finais.

## Escopo da primeira etapa

Temas Clara, Escura e Sistema, escolha persistida, navegação inicial e Configurações. Os desktops compartilham preferências da interface e atalhos; o Android possui preferências locais da interface e de notificações. A interface exibe estados vazios quando não há integração de dados.

A fundação HTTP da API está implementada. O usuário confirmou PostgreSQL, perfis de atendente/administrador no OBS, contas individuais no Android e acesso Municipal somente a indicadores agregados. Autenticação, banco, cadastros, agendamento, filas, entrega de notificações e regras de Vaga Relâmpago ainda não estão implementados; decisões restantes constam em P-001. Não há pacientes, indicadores ou serviços clínicos simulados. As preferências são locais ao dispositivo; os perfis desktop usam armazenamento separado por aplicativo.

## Documentação

- [Briefing visual original](docs/briefing-visual.md).
- [Referência visual e padrões de design](docs/design.md).
- [Arquitetura](docs/arquitetura.md).
- [Configurações e atalhos](docs/configuracoes.md).
- [Execução e validação](docs/execucao.md).
- [Documento da tarefa e validações](docs/tarefas/2026-10-07-inicio-plataforma-nora.md).
- [Pendências](docs/PENDENCIAS.md).
- [Requisitos funcionais confirmados](docs/requisitos-funcionais.md).
- [API e contratos HTTP](docs/api.md).
- [Emulador Android e passo a passo](docs/android-emulador.md).
- [Retomada atual](docs/tarefas/2026-10-07-continuacao-funcionalidades-testes-api.md).
- [Conferência do prompt e pendências do usuário](docs/tarefas/2026-10-07-conferencia-prompt-visual.md).

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
