# Publicar a main e as demais branches

## Skills e comandos invocados

O usuário solicitou commit e push pelo Git, com prioridade para a `main`. Foi aplicada a skill `merge-preservando-alteracoes`.

## Pedido e objetivo

1. Comitar na `main` todas as alterações pendentes do usuário neste clone.
2. Fazer push da `main` no remoto GitHub.
3. Fazer push também das demais branches do usuário.

O resultado esperado é a `main` remota com as alterações pendentes e as outras branches publicadas com seus commits locais.

## Restrições

Priorizar a `main`. Preservar o histórico das branches e usar a identidade configurada no Git.

## Checklist

- [x] Conferir o estado local, o remoto, a identidade Git e os históricos.
- [x] Revisar o conteúdo pendente e avançar a `main` sem reescrever histórico.
- [x] Validar as alterações antes do push.
- [x] Preparar o commit da `main` com o registro da validação e a atualização da publicação dos filtros.
- [x] Conferir os destinos do push da `main` e das demais branches de trabalho.

## Estado

A cópia de trabalho estava em `codex/conexao-url-neon`, três commits à frente da referência remota. Os arquivos locais de Enter, localização da UBS e filtros foram conferidos com a versão já publicada na `main`. Os ajustes locais de nitidez e ícones do Municipal estavam na branch `codex/legibilidade-icones-municipal`, no commit `d86e3a4`, que também preserva a versão atualizada dos filtros e dos registros de pendências. A `main` local avançou de `d566e58` a `351c284` e depois a `d86e3a4`. As cópias locais anteriores foram preservadas no stash `ba485ee`.

O build Release de `NORA.slnx` passou com zero erros e avisos. A suíte desktop passou em 209/209 verificações, incluindo os fluxos com API e PostgreSQL 18 descartáveis. O teste de abertura e resposta das duas janelas WPF passou em 2/2. A conferência de whitespace do diff passou. A `main` não tem workflows do GitHub Actions versionados.

## Conferência da publicação

O ensaio de push confirmou o acesso a `origin` e os destinos da `main` e das nove outras branches de trabalho. Quatro referências automáticas `worktree-agent-*` pertencem a worktrees temporárias de subagentes e ficam locais. O estado final é conferido comparando os hashes locais com `git ls-remote --heads origin`. O resultado dessa comparação é registrado em `.build/publicacao-main-e-branches-2026-11-24.log`.
