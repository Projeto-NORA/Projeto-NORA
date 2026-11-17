# P-047 — verificação de troca de senha do OBS falha de forma intermitente

## Pedido (agente coordenador)
- Pendência P-047: no E2E com banco (`tests/validar-clientes-descartavel.ps1`), "NORA OBS: trocar senha em Configurações valida localmente, mostra a senha atual errada no campo e informa o sucesso" falhou uma vez com "O campo da senha atual errada deveria ser limpo" e passou na repetição.
- Ciclo de diagnóstico: primeiro um laço que reproduza com taxa alta, só depois hipóteses.
- Corrigir pela causa raiz (teste frágil ou defeito da tela). Provar com N repetições verdes (30), rodar a suíte `tests/Nora.Desktop.Checks` completa (sem API) e o E2E completo uma vez.
- Se não reproduzir em 30-50 repetições, dizer a taxa e o que foi tentado.
- Mover P-047 para Resolvidas só se reproduziu e corrigiu.

## Restrições
- Não tocar em APPNORA/; não commitar; não usar stash. PostgreSQL 18 em `C:\Users\Pichau\AppData\Local\Programs\PostgreSQL\18\bin`, portas 55780-55789. Limite 50 min.

## Checklist
- [x] Laço de reprodução (`--check-filter "NORA OBS: trocar senha"`)
- [ ] Causa raiz — não reproduzida, sem causa provada
- [ ] Correção — nenhuma (sem reprodução)
- [x] 144 repetições sem falha
- [x] Suíte Desktop.Checks completa 170/170
- [x] E2E completo 173/173
- [x] PENDENCIAS.md: P-047 segue aberta, com a taxa medida

## Estado
Não reproduzido: 0 falhas em 144 execuções (30 sequenciais, 64 em paralelo x16, 48 em paralelo sob 28 queimadores de CPU, 1 suíte completa, 1 E2E completo). Nenhum código alterado; P-047 permanece aberta com a medição registrada.
