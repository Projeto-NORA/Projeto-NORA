# Projeto-NORA

Plataforma NORA para a jornada do paciente, operação de unidades de saúde e gestão municipal. Esta primeira entrega, confirmada pelo usuário, implementa a base visual e as configurações locais dos três aplicativos.

- **APPNORA:** aplicativo Android em Kotlin e Jetpack Compose.
- **OBS-NORA:** aplicativo WPF para operação da unidade.
- **Painel-Municipal:** aplicativo WPF para leitura gerencial.
- **Nora.Desktop:** biblioteca compartilhada de interface, temas e preferências dos desktops.

A identidade usa roxo, cartões e navegação inspirados no protótipo fornecido, reconstruídos nas tecnologias nativas. O React do ZIP serve como referência de design; não é a base técnica nem define as regras de negócio finais.

## Escopo da primeira etapa

Temas Clara, Escura e Sistema, escolha persistida, navegação inicial e Configurações. Os desktops compartilham preferências da interface e atalhos; o Android possui preferências locais da interface e de notificações. A interface exibe estados vazios quando não há integração de dados.

API ASP.NET Core, autenticação, banco, cadastros, agendamento, filas, entrega de notificações e regras de Vaga Relâmpago precisam de especificação funcional para a próxima etapa. Não há pacientes, indicadores ou serviços clínicos simulados nesta entrega. Nenhuma regra clínica foi presumida. As preferências são locais ao dispositivo; os perfis desktop usam armazenamento separado por aplicativo.

## Documentação

- [Briefing visual original](docs/briefing-visual.md).
- [Referência visual e padrões de design](docs/design.md).
- [Arquitetura](docs/arquitetura.md).
- [Configurações e atalhos](docs/configuracoes.md).
- [Execução e validação](docs/execucao.md).
- [Documento da tarefa e validações](docs/tarefas/2026-10-07-inicio-plataforma-nora.md).
- [Pendências](docs/PENDENCIAS.md).

## Desenvolvimento

Os desktops usam .NET 10 para Windows. A solução integrada é `NORA.slnx`. Os outputs .NET ficam em `.build/`, preservando os artefatos antigos que já estavam rastreados.

```powershell
dotnet build NORA.slnx --configuration Release -m:1
dotnet run --project OBS-NORA/OBS-NORA.csproj
dotnet run --project Painel-Municipal/Painel-Municipal.csproj
dotnet run --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj
```

O Android é aberto pela pasta `APPNORA` no Android Studio. Use o wrapper do próprio projeto para compilar; os comandos e requisitos de ambiente estão em `docs/execucao.md`. O APK de depuração está em `APPNORA/app/build/outputs/apk/debug/app-debug.apk` após o build.

## 2026-10-07 — Fundação da plataforma

- **Alteração:** reconstrução nativa da base visual e das configurações dos três aplicativos.
- **Motivo:** iniciar a plataforma preservando a experiência da referência e uma identidade coerente.
- **Validação:** solução WPF em Release com zero erros/avisos; 21 checks desktop passaram. Android compilou app e testes, passou nos três testes locais e no lint (zero erros, um aviso de versão disponível).
- **Estado:** primeira etapa implementada e revisada. Validação Android em dispositivo pendente por falta de hypervisor/dispositivo conectado (P-003); quatro casos instrumentados apenas compilados. API e fluxos clínicos dependem da próxima especificação (P-001).
