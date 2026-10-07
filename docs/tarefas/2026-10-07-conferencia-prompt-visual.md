# Conferência e correções do prompt visual

## Skills e comandos invocados

- `skills-aura:aura`, com protocolo, diagnóstico e loop de verificação já lidos nesta conversa.

## Pedido, item por item

1. Considerar o prompt anexado em `C:/Users/mateu/Downloads/Texto colado.txt`.
2. Arrumar o que falta em relação a esse prompt.
3. Informar quais pendências ficam para o usuário executar ou decidir.

## Objetivo

Conferir os três clientes nativos contra o briefing visual, corrigir lacunas verificáveis, executar validações reais e apresentar somente as decisões ou verificações externas que continuarem necessárias.

## Restrições

- Preservar todas as alterações da retomada anterior e a ordem da fila.
- Usar o protótipo como referência visual/estrutural, sem copiar React ou adotar regras fictícias de negócio.
- Manter Kotlin/Compose, WPF e ASP.NET Core; identidade roxa e temas Clara/Escura/Sistema.
- Não fabricar pacientes, indicadores, gráficos de produção, autenticação ou dados de banco para preencher telas.
- Perguntar sobre dúvidas relevantes; não presumir resposta às confirmações de login/oferta já enviadas.

## Checklist

- [x] Ler o anexo inteiro e registrar pedido antes da investigação de código.
- [x] Conferir documentação e diff real, preservando trabalho anterior.
- [ ] Definir ordem desta solicitação em relação à continuação e limpeza na fila.
- [x] Conferir identidade, layouts, configurações, temas, sidebar/cards e contexto da seleção existentes; limites abaixo.
- [ ] Corrigir gaps verificáveis de controles, filtros, modais, estados e acessibilidade.
- [x] Executar build e suite nativa/render WPF; Android permanece com evidência anterior e conferência complementar em P-003.
- [x] Revisar os quatro papéis Aura e o checklist de bugs do código alterado, diretamente por indisponibilidade dos auxiliares.
- [x] Atualizar documentação e registro único de pendências.
- [x] Listar o que resta para o usuário, distinguindo decisões de produto de limitações de ambiente.

## Estado

Anexo lido integralmente: conteúdo visual equivalente ao briefing já existente. Git mantém alterações não commitadas da etapa anterior (API técnica, correção do bootstrap WPF, checks e documentação); nenhum trabalho descartado. Documento criado antes da investigação desta solicitação. Continuação clínica depende de respostas já solicitadas e limpeza está na fila; pergunta de prioridade enviada, sem resposta até este checkpoint (P-011). Nenhuma nova implementação distinta foi iniciada fora da ordem. A conferência complementar e a correção P-010 abaixo integram a retomada de testes já autorizada.

## Correspondência com o prompt

| Item | Estado real e evidência |
| --- | --- |
| Kotlin/Compose, dois WPF e ASP.NET Core | Stack preservada; nenhum React do protótipo copiado. API atual é somente fundação HTTP, sem domínio/banco/login. |
| Identidade roxa, cards, navegação, tema dos três clientes | Implementados; temas/persistência WPF conferidos pela suite nativa. Android teve testes locais/instrumentados na etapa anterior; inspeção extra ainda limitada pelo AVD. |
| Clara/Escura/Sistema e seleção persistida | Presentes nos três. WPF testa tokens, contraste, seleção acessível, aplicação em controles existentes e resolução do Windows. Android usa tema Compose e SharedPreferences. Conferência extra de Sistema no dispositivo em P-003. |
| Configurações e preferências desktop | Dicas, confirmação ao fechar, navegação, animação, sidebar, tela cheia e restauração presentes. Atalhos atuais exibidos, permitido pelo prompt; edição completa não é requisito desta POC. |
| Configurações Android | Aparência, quatro preferências, restauração confirmada e versão presentes. Preferências de avisos ainda não entregam notificações; sair indisponível sem autenticação. P-001 cobre a integração. |
| OBS operacional e barra contextual | Tabela e barra por seleção presentes e testadas com registro sintético exclusivo do harness. Não há cadastro, agenda, fila ou ações clínicas reais ainda (P-001). |
| Municipal executivo, gráficos, filtros e indicadores | Estrutura visual/estados vazios presentes; séries, filtros e métricas reais ainda não implementados (P-001). Dados fictícios do ZIP não foram adotados. |
| Acabamento, foco, estados de componentes e acessibilidade | Tokens/styles para textos, inputs, tabelas, botões, tooltip e rolagem; contrastes e seleção acessível verificados. Estados vazios são reais. Loading/erros de serviços clínicos dependem da integração; teclado físico, DPI e múltiplos monitores permanecem em P-007. |
| Navegação com tela rolada | Bug reproduzido e corrigido na retomada (P-010): ambas as telas desktop abrem pelo topo. |

## Correção complementar e validação

- Teste escrito antes da correção reproduziu Configurações abrindo na rolagem herdada da Home: 23/24 checks verdes, regressão vermelha.
- `ShellWindow` agora chama `PageScroll.ScrollToTop()` ao trocar a página, independentemente da preferência de animações; sem alteração do armazenamento ou regras clínicas.
- Suite final **24/24**: nova regressão invoca botões nativos em Home→Configurações e retorno, para OBS/Municipal, animações ligadas/desligadas e 980×640. Medição do diálogo de fechamento confirmou que os botões cabem; nenhum ajuste desse modal necessário.
- Solução Release **zero erros/avisos** após liberar a DLL usada pela instância OBS de teste da etapa anterior. Caminho/PID conferidos antes de encerrá-la; nenhum outro processo encerrado.
- `tests/desktop-startup.ps1`: **2/2** executáveis atualizados com janela real e responsiva. Prévias `.build/previews-conferencia` geradas; imagem de Configurações no tamanho mínimo inspecionada.
- Não houve mudança em Android/API; suas suites anteriores não foram repetidas. Mantêm resultados históricos de 3 locais/4 instrumentados Android e 20 checks HTTP, sem implicar funcionamento clínico.
- Auxiliares existentes retornaram limite de uso; revisão dos papéis produto, implementação, técnica e UX feita diretamente pelo root. Checklist aplicado: execução, lógica, limites, eventos concorrentes, recursos, regressão, persistência, segurança, resíduos e inconsistências de documentação. Não alegar revisão independente que não ocorreu.

## O que fica para o usuário

1. Responder às confirmações já enviadas de acesso: e-mail/senha ou CPF/senha para pacientes; quem provisiona atendentes e contas iniciais de administrador/Municipal. Atendente/administrador criando pacientes e Municipal somente agregado já estão definidos e não precisam ser respondidos novamente.
2. Confirmar a interpretação já enviada da Vaga Relâmpago: primeiro notificado com 30 minutos, seguintes com 10; sequência por entrada com recusa/expiração passando ao próximo. O anexo visual não resolve essa ambiguidade.
3. Escolher a prioridade na pergunta enviada, se quiser executar este pedido visual antes da implementação/limpeza já na fila. Sem resposta, preservar a ordem do AGENTS.md.
4. Para a conferência Android complementar, executar em outro AVD estável ou dispositivo físico conforme `../android-emulador.md`: reabertura, Clara/Escura/Sistema, fonte ampliada e tela larga. WHPX já funcional; não é necessário instalar driver no computador atual. AVD pessoal preservado.
5. Conferência manual desktop ainda necessária: atalhos/foco com teclado físico, escala/DPI ampliados e tela cheia entre monitores (P-007), pois o helper de interação não ficou disponível.

## O que continua como implementação do agente

PostgreSQL, autenticação/contas, cadastros, agenda, fila/Vaga Relâmpago, entrega de notificações, logout autenticado, ações contextuais funcionais, filtros e gráficos com dados agregados reais (P-001); limpeza ampla e higiene dos artefatos rastreados (P-009/P-002). Não transferir esses itens técnicos ao usuário nem apresentar a plataforma como concluída.

Resultado: **parcial**, com correção verificável e pendências explícitas. Pedido 1 atendido (anexo lido/comparado), pedido 2 parcialmente atendido na retomada (P-010 resolvida, integração ainda pendente), pedido 3 atendido (lista acima). Documentação preserva o ponto exato para continuar; sem commit/push/reset.
