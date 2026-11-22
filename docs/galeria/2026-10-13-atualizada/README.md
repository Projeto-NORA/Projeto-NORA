# Galeria NORA — fotos atualizadas em 13/10/2026

[Abrir o site](index.html). O layout, filtros, busca, ampliação, navegação e os83 itens mantêm a galeria HTML fornecida pelo usuário. Foram substituídas43 fotos OBS e25 Municipal porque as telas Windows mudaram no shell compartilhado e nas listagens. As15 imagens Android permanecem byte a byte iguais às fotos de10/10.

As fotos Windows são renders WPF atuais com dados sintéticos isolados:64 capturas1280×740 e4 capturas980×640. Foram copiadas sem edição; origem, dimensões e SHA256 constam em [manifest.json](manifest.json). Não são captura de moldura/DPI físicos. As fotos Android históricas são renders Compose/Layoutlib, sem alteração nesta tarefa. O conjunto não representa implantação do acesso por ativação ou painel financeiro.

O HTML original não contém imagens embutidas: distribua `index.html`, `desktop/`, `android/`, `manifest.json` e este README juntos. Reprodução: gerar as capturas pelo harness com `--visual-gallery --preview-dir .build/gallery-updated-2026-10-13/desktop` e executar `pwsh -NoProfile -File tests/atualizar-fotos-galeria.ps1`. A galeria histórica de10/10 foi preservada.
