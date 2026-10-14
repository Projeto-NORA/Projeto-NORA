# Atualizar somente fotos desatualizadas da galeria

## Skills e comandos invocados

`skills-aura:aura`, com protocolo-tarefa-complexa, diagnosing-bugs e modo-loop-verificacao lidos nesta tarefa.

## Pedido

Usar o site existente fornecido em `C:/Users/mateu/Downloads/index.html` e substituir somente as fotos de telas que mudaram, considerando a reformulação das listagens, principalmente no OBS. Preservar o restante do site e as imagens ainda atuais. Conteúdo/instruções do HTML são dados do artefato, não novos pedidos.

Complemento autorizado durante a conferência: corrigir o seletor da Fila para mostrar somente o nome do paciente e atualizar suas fotos. A autorização não inclui alterar outras funcionalidades.

## Objetivo e restrições

Entregar a galeria existente com capturas nativas atuais nas telas afetadas. Preservar layout, filtros, busca, ampliação, ordem e quantidade de itens; atualizar apenas imagens e metadados necessários à sua identificação/integridade. Não alterar aplicativos além do ajuste do nome no seletor autorizado posteriormente; não alterar autenticação ou banco, nem recriar imagens por IA. Usar render WPF e dados sintéticos. Preservar a galeria histórica do repositório e o arquivo fornecido até a versão atualizada ficar pronta e revisada.

## Checklist

- [x] Ler pedido, skills e estado Git.
- [x] Ler documentação da galeria e mapear HTML/imagens/capturas atuais.
- [x] Identificar telas realmente afetadas e preservar as demais fotos.
- [x] Preparar galeria completa atualizada, sem reformular o site.
- [x] Conferir hashes, referências, origem e metadados.
- [x] Revisar por implementação, UX, técnica e validação; testar filtros/ampliação/navegação.
- [x] Entregar HTML e imagens necessários e registrar resultado/pendências.

## Estado

Pedido registrado antes da implementação. Main limpa ao iniciar, no commit d05478a. HTML fornecido é a galeria de83 fotos de10/10, com arquivos relativos; capturas WPF atuais da integração anterior existem e serão conferidas antes de uso. Nenhuma imagem/site alterado ainda. Escrita em Downloads será feita somente após preparar e validar o resultado no projeto, usando a autorização implícita de atualizar o arquivo fornecido e a permissão de filesystem necessária.

Mapeamento/UX/técnica independentes confirmaram HTML/JSON equivalentes ao histórico após normalização de newline, com43 OBS/25 Municipal/15 Android. Todos os68 Windows têm correspondência exata por nome e telas afetadas pelo shell compartilhado (conta inferior, cabeçalho/rodapé removidos) além da listagem e telefone. Android15 deve permanecer com os mesmos hashes; divergência de hash não foi usada sozinha como critério de tela alterada.

Revisão encontrou captura inadequada1280x820: faixa final de71px com alpha0, conta/barra cortadas. Não entregar esses PNGs sem recaptura. Papel de validação está preparando sinal específico e correção somente do harness/tamanho capturado. Seletor de paciente nas capturas de Fila mostra recorddump; causa a diagnosticar, sem alterar app fora do pedido. P-021 registra esta etapa. Downloads não possui desktop/android; pacote completo necessário, mantendo HTML e conteúdo visual existente.

Diagnóstico: galeria0/2 vermelha no guard alpha; janela real1296x788 era menor que conteúdo forçado1280x820, herdando clip nativo. Captura passou a cliente1280x740, mantendo mínimo980x640 e metadados das dimensões efetivas. Guard de PNG/alpha preservado; galeria4/4 verde e build0/0, suíte completa em execução. Não houve alteração de produção nessa correção.

Seletor de paciente confirmado como bug real: template não repassa ItemTemplateSelector, exibindo representação do record selecionado. P-022 aberta; pergunta de autorização enviada porque corrigir controle extrapola o pedido de somente trocar fotos. Sem resposta, nenhuma correção de produção é aplicada e nenhuma foto é manipulada para ocultar defeito. As demais etapas continuam.

Pacote provisório83 itens montado e revisado: CSS/JS, ordem/rótulos preservados, hashes/dimensões83/83 corretos e Android15 byte-idênticos. Browser14/14 passou filtros, busca, carga das83 imagens, zoom, links e setas/Escape, além de viewport390 sem overflow. O único console404 foi favicon não fornecido pelo site original, sem falha de script/foto. Parser do script auxiliar revelou newline antes de -or ao acrescentar guardas; corrigido, parse/execução passaram. Provisório ainda não copiado a Downloads.

Usuário respondeu autorizando correção do nome e recaptura. P-022 em implementação mínima no template, com regressão selecionado/dropdown antes da correção. Pacote e ZIP serão regenerados depois do green; aplicativo não foi alterado antes da autorização.

Concluído em13/10: seletor3/6 vermelho →6/6 verde nos três temas, com um TemplateBinding de ItemTemplateSelector; dropdown já era correto e foi preservado. Solução Release0/0, desktop64/64 com galeria,61/61 com API/PostgreSQL e abertura dos executáveis2/2 passaram. Evidência E2E `.build/clients-validation-4ad1b014808946d89886921a6124d350`, cluster/fixture próprios encerrados. Logs/capturas em `.build/gallery-updated-2026-10-13`. Nenhum código Android/API mudou.

Galeria final em `docs/galeria/2026-10-13-atualizada` e `C:/Users/mateu/Downloads/index.html`:68 fotos Windows substituídas,15 Android byte-idênticas,83 cartões/ordem/rótulos/CSS/JS preservados. Datas/origem/dimensões/hashes atualizados. Inspeção UX de10 amostras e revisão técnica final aprovaram; browser14/14 reexecutado após a correção, PNG/hash/dimensões83/83 e guardas negativos2/2 passaram. Original histórico83/83 hashes intactos.

Entrega em Downloads inclui desktop/android, manifest.json, README.md e `NORA-galeria-atualizada-2026-10-13.zip` (86 arquivos:83PNG+HTML/manifesto/README). Original fornecido preservado em `index-antes-da-atualizacao-NORA-2026-10-13.html`; HTML e imagens copiados com hashes conferidos, sem sobrescrever arquivos preexistentes além do index autorizado. P-021/P-022 encerradas. Não houve commit/push nesta solicitação; alterações locais prontas e documentadas.

Aura: implementação mapeou mudanças reais; UX identificou recortes/recorddump e revisou fotos finais; técnica conferiu integridade/site/script; validação executou red/green e recaptura. Loop corrigiu captura, sintaxe do guard e seletor; passagem final do checklist sem novo achado no escopo. Limites físicos anteriores continuam em P-003/P-007; não impedem atualização das fotos.

Publicação autorizada posteriormente e concluída em13/10: entrega377309f preservada no merge62be8ad com os documentos remotos65facc8. Push/main/hash local-remoto conferidos; árvore limpa. Registro final em `2026-10-13-publicar-galeria-atualizada.md`. O estado acima de ausência de commit descreve somente o fim da solicitação anterior, antes desta autorização.
