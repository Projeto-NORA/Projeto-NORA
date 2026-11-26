# Ferramenta de capturas reais dos programas desktop

Pedido (orquestrador, urgente, entrega de imagens em menos de 2 horas): criar a ferramenta que captura as telas do OBS-NORA e do Painel Municipal ligados à API local com dados reais.

## Pedido, item a item
1. Modo `--capturas-reais --saida <pasta> --perfil obs|municipal|ambos` em `tests/Nora.Desktop.Checks`; URL e credenciais vêm de `NORA_CAPTURA_URL`, `NORA_CAPTURA_OBS_EMAIL/SENHA`, `NORA_CAPTURA_MUN_EMAIL/SENHA`; preferências em pasta temporária; login real e escolha da UBS.
2. Janela 1600x900, tema claro (e passe escuro com prefixo `escuro-` só nas telas principais), 144 DPI (2400x1350).
3. Percorrer o menu lido do ShellViewModel em tempo de execução, esperar os dados e gravar `<perfil>-NN-<rota>.png`.
4. Estados extras: `-graficos`, `-selecao`, `-ficha`, `-novo`, menu do perfil, Trocar perfil (OBS), login dos dois programas; pop-ups compostos sobre a janela escurecida.
5. Não salvar nem alterar nada na API; Mapa da rede (WebView2) pulado com aviso.
6. Testar contra a API local, gravar em `.build/capturas-teste/`, conferir as imagens.
7. Commit na branch; informar `comoRodar` e `pastaSaida`.

## Estado
- Implementado em `tests/Nora.Desktop.Checks/RealCaptures.cs` e gancho em `Program.cs`.
- Resultado dos testes: ver o final deste arquivo (atualizado após a execução).

## Resultado (26/11/2026)
- Teste com o código desta pasta, contra a API local: 70 imagens 2400x1350 em `.build/capturas-teste/` (OBS e Municipal, claro e `escuro-`), login, menu, pop-ups, menu do perfil e Trocar perfil conferidos visualmente.
- Teste das telas novas (ListBox `OpenList`, `ViewModeToggle`, `NewItemButton`, ficha e pop-up de cadastro) com o código de `claude/desktop-redesign-ui` exportado em pasta temporária: `-graficos`, `-selecao`, `-ficha` e `-novo` saíram nítidos e com o pop-up centralizado sobre a janela escurecida. Listas sem ficha (consultórios, grade, vagas) geram só um aviso.
- Mapa da rede (WebView2) é pulado com aviso.
- Rodar: `pwsh -File scripts\capturas-reais.ps1 -Saida <pasta> -Perfil ambos` (lê a senha de `%LOCALAPPDATA%\NORA\capturas-demo\acesso-demo.txt`).
