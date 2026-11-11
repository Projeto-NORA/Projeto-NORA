# Telas atualizadas da Secretaria

[Abrir galeria](index.html). A galeria mostra Unidades, Mapa da rede, Vaga Relâmpago e Relatórios e metas nos modos claro e escuro: 18 imagens da interface WPF e duas capturas da janela com o mapa carregado. Telas longas foram divididas em partes.

As capturas usam conta, UBS, endereço e dados sintéticos, com API simulada. O marcador representa uma coordenada de demonstração e não uma UBS real. As imagens da interface são renders WPF em 1280 × 740. O mapa foi capturado da janela real porque o navegador incorporado é uma superfície separada; os créditos do OpenStreetMap permanecem visíveis.

Renders reproduzíveis com `dotnet run --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj --configuration Release -- --changed-secretaria --check-filter "Secretaria: capturas das telas alteradas" --preview-dir docs/galeria/secretaria-atualizada-2026-10-28/imagens`. Os arquivos `mapa-carregado-*.jpg` foram capturados de uma prévia isolada usando a habilidade de captura do Windows.
