# Pendências da plataforma NORA

## Abertas

- **P-001** · 2026-10-07 · Requisitos funcionais da aplicação final/API: o anexo define design e UX, mas não contratos, autenticação, banco ou regras de fila/agendamento/Vaga Relâmpago. Usuário confirmou começar pela base visual e configurações; a etapa seguinte precisa de especificação própria, sem adotar regras do protótipo como regras finais.
- **P-002** · 2026-10-07 · Higiene do Git: os dois projetos WPF já versionam arquivos de `obj/`, `.vs/` e `*.csproj.user`. Esta etapa adicionou exclusões e separou novos outputs em `.build/`, preservando os arquivos previamente rastreados. Retirar os artefatos antigos do índice deve ser uma tarefa específica que preserve o ambiente local.
- **P-003** · 2026-10-07 · Validação Android em dispositivo: SDK e AVD `Medium_Phone_API_37.0` existem e o APK/testes instrumentados compilaram. O emulador falhou em duas tentativas porque o Android Emulator hypervisor driver não está instalado. Testes de toque, tema e persistência após encerramento real precisam de dispositivo físico ou emulador com virtualização configurada; não instalar driver do Windows como parte desta etapa visual.
- **P-007** · 2026-10-07 · Conferência desktop em ambiente real: verificar atalhos com teclado, diálogo de fechamento, foco, fonte/DPI ampliados e tela cheia com múltiplos monitores. Os 21 checks automatizados validam a estrutura nativa, contraste, seleção, rolagem, tema, persistência e restauração de tela cheia, mas não substituem essa avaliação manual de dispositivos.

## Resolvidas

- **P-004** · aberta em 2026-10-07 · resolvida em 2026-10-07 · Teste de DynamicResource em controle avulso não representava controle da janela → cenário corrigido para árvore da janela e processamento do Dispatcher. A troca passou e os 20 checks integrados também passaram, incluindo render dos dois aplicativos nos três temas. Não foi necessário alterar a troca de tema por esse resultado.
- **P-005** · aberta em 2026-10-07 · resolvida em 2026-10-07 · Home e Configurações Android compartilhavam rolagem → separados os estados e adicionado retorno ao topo ao abrir Configurações. Regressão instrumentada escrita antes da correção e compilada; build, testes locais e lint passaram. Execução da regressão no dispositivo permanece em P-003.
- **P-006** · aberta em 2026-10-07 · resolvida em 2026-10-07 · Limite de largura Android não se aplicava em telas largas → adicionado contêiner centralizado com limite de 640 dp antes do preenchimento. Código revisado e build/lint passaram; conferência em dispositivo permanece em P-003.
