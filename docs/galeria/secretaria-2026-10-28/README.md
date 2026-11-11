# Capturas da Secretaria NORA

[Abrir galeria](index.html). São 42 imagens de 13 telas, incluindo login e configurações, em modo claro e escuro. Telas longas são mostradas em partes para cobrir toda a rolagem.

As imagens são renders dos controles WPF em 1280 × 740, gerados com `--secretaria-gallery`; não incluem moldura nativa do Windows. O login está desconectado, e as telas conectadas usam exclusivamente dados sintéticos de uma API simulada no harness. Sem credenciais reais, unidades reais ou dados de pacientes. Áreas sem integração aparecem como indisponíveis.

Para reproduzir: `dotnet run --project tests/Nora.Desktop.Checks/Nora.Desktop.Checks.csproj --configuration Release -- --secretaria-gallery --check-filter "Secretaria: capturas" --preview-dir docs/galeria/secretaria-2026-10-28/imagens`. O manifesto lista tela, tema, posição da rolagem e origem de cada captura.
