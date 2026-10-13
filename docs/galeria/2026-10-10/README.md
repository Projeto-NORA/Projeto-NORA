# Galeria visual NORA — 10/10/2026

[Abrir galeria completa](index.html). O HTML funciona offline: filtros por aplicativo/aparência, busca, ampliação, navegação por setas e PNG original. As **83 imagens** são cópias sem edição das renderizações nativas; origem, dimensões e SHA256 estão em [manifest.json](manifest.json).

## Telas principais

- [OBS: pacientes e seleção, claro](desktop/visual-obs-pacientes-selecao-light-topo.png).
- [OBS: cadastro de paciente, claro](desktop/visual-obs-cadastro-paciente-light-fim.png).
- [OBS: fila e ação selecionada, escuro](desktop/visual-obs-fila-selecionada-acao-dark-topo.png).
- [OBS: Vaga Relâmpago, claro](desktop/visual-obs-vagas-abertas-light-topo.png).
- [Municipal: totais agregados, escuro](desktop/visual-municipal-totais-agregados-dark-topo.png).
- [Android: acesso, claro](android/02-login-claro.png).
- [Android: fila e oferta pendente, claro](android/05-paciente-oferta-pendente-claro.png).
- [Android: oferta aceita, escuro](android/06-paciente-oferta-aceita-escuro.png).
- [Android: configurações, escuro](android/08-aparencia-escura.png).

## Cobertura e origem

**68 desktop:** OBS e Municipal, acesso, visão geral, pacientes/cadastro, inclusão/cancelamento de fila, abertura de vagas, totais/indicadores, configurações no início/meio/fim, conta conectada, sidebar recolhida e janela mínima. WPF `RenderTargetBitmap` captura a árvore real de controles. 38 imagens desconectadas, 28 com respostas HTTP simuladas exclusivamente no harness visual e duas da base contextual de atendimentos ainda indisponíveis. A fila implementada possui botão de cancelamento abaixo da tabela; a barra contextual do scaffold não representa essa operação integrada. Tema Sistema acompanha o Windows deste computador, claro na captura.

**15 Android:** início/acesso, erro, fila aguardando, oferta pendente/aceita, configurações completas, claro/escuro/Sistema em ambos os modos, fonte 130% e tablet de 1000 dp. Compose Preview Screenshot Testing/Layoutlib renderiza os componentes de produção. Os estados de conta/oferta são fixtures exclusivas de `screenshotTest`; nenhuma API foi chamada. Algumas imagens usam canvas vertical ampliado para mostrar todo o conteúdo: não equivalem à altura de um celular.

Sem imagens geradas por IA, dados pessoais reais, senhas preenchidas, banco de uso ou alterações das preferências pessoais. A galeria apresenta o estado disponível, sem incluir chave OBS ou painel financeiro, ainda em P-014/P-015.

## Revisão e validação

A revisão UX inspecionou 12 amostras iniciais e nove finais. As amostras finais apresentam títulos, cartões, ações e navegação legíveis, sem cortes funcionais observados; fonte 130% e tablet mantêm a organização. A inspeção não é uma auditoria completa de acessibilidade.

O texto dos botões principais WPF estava com contraste inadequado: 2,30:1 no claro e 2,48:1 no escuro. Regressões sobre o `TextBlock` efetivo falharam antes da correção; o template agora respeita o `Foreground` do botão, com 7,10:1 e 6,72:1. Todas as fotos desktop foram regeneradas. A medição da sidebar no harness também foi ajustada para refletir os 80 px recolhidos, sem alterar a aplicação.

- Desktop: **39/39 checks**, build do harness Release sem erros/avisos.
- Android: **15/15 renderizações e comparações**, **7/7 testes locais**, APK e APK de testes compilados; lint zero erros e um aviso de versão Kotlin. Comparações conferem consistência com a referência recém-gerada; não comprovam sozinhas qualidade visual.
- Solução integrada Release: zero erros; quatro avisos NU1900 sobre consulta de vulnerabilidades NuGet indisponível nesta rede.
- Cópias dos 83 PNG conferidas por SHA256. Galeria verificada no navegador com filtros, busca e ampliação.

Não foram reexecutados E2E PostgreSQL nem testes instrumentados em dispositivo nesta rodada. O computador atual não tem PostgreSQL 18 nem aceleração do emulador disponível. Execução em celular, recriação de sessão, DPI e teclado físico continuam em P-003/P-007.

## Reproduzir

Comandos e requisitos em [execucao.md](../../execucao.md#galeria-visual-de-10102026). `tests/exportar-capturas-android.ps1` exporta os15 previews por mapeamento explícito, recusando arquivos ausentes/ambíguos e conferindo dimensões/hashes. `tests/gerar-galeria-visual.ps1` reúne os dois aplicativos desktop e Android. Preferências e fixtures dos testes são isoladas; os scripts copiam PNG sem editar e preservam os originais.
