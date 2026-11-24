# Ideias da plataforma NORA

Ideias para o projeto que ainda não viraram trabalho a fazer. Quando uma ideia for aceita, ela vai para Encerradas com o número da pendência (`P-…`) que nasceu dela. Quando for descartada, vai para Encerradas com o motivo.

## Em aberto

- **I-001** · 2026-11-24 · Ligar o "Entrar com Google" e as notificações push depois do piloto. Veio da P-042. O código já existe e fica desligado sem credenciais: na API (`09fdd87`) e no app (`dfadb0e`). O botão "Continuar com Google" só aparece com `NORA_GOOGLE_WEB_CLIENT_ID`, e o `NoraMessagingService` registra o token e abre a tela pelo tipo. O plugin `google-services` só entra com `app/google-services.json`, que fica fora do Git. Para ativar: criar no Google Cloud os client IDs OAuth web e Android (com o SHA-1 da chave do app), criar o app Android e a chave da conta de serviço no Firebase e configurar `Nora:Google:ClientIds`, `Nora:Push:ProjectId` e `Nora:Push:ServiceAccountPath` ou `Json` no servidor. O passo a passo está em [configuracoes.md](configuracoes.md) e [app-paciente.md](app-paciente.md). Depois, testar com o Google de verdade o login, o vínculo da conta e a entrega do push num aparelho com Google Play. Os testes com servidor falso não substituem esse teste. Por decisão do usuário em 24/11/2026, o piloto usa só e-mail e senha.

## Encerradas
