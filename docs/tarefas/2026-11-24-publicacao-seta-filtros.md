# Publicação da seta de filtros

## Skills e comandos invocados
Nenhum comando ou skill invocado explicitamente; aplicada `merge-preservando-alteracoes`.

## Pedido e objetivo
Fazer commit e push para a `main` somente do trabalho anterior: seta de filtros e seus registros.

## Restrições
Preservar o histórico e as alterações de outras sessões.

## Checklist
- [x] Conferir alterações e remoto; preparar cópia isolada a partir da `origin/main`.
- [x] Preservar os registros mais recentes de pendências presentes na `main`.
- [x] Validar o trabalho anterior: build Release sem erros e 8/8 checks filtrados; os checks não reproduzem o clique real.
- [x] Criar commit e publicar na `main` sem forçar; conferir resultado.

## Estado
A conferência remota de 24/11/2026 confirmou o commit `5481d14`, que adicionou a seta, e o commit `7e0e814`, que corrigiu a reabertura no segundo clique, no histórico da `main`. A P-065 foi resolvida. Os resultados do teste com mouse e das verificações desktop estão no [registro da correção](2026-11-24-correcao-botao-filtros.md).
