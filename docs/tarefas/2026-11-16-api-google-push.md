# API: testes que faltam na Família (P-043) e Entrar com Google e push (P-042) — 2026-11-16

## Skills e comandos invocados

Nenhum. Tarefa delegada pelo agente principal ao agente da API (único agente em `Nora.Api`, `tests/Nora.Api.Checks`,
`docs/api.md`, `docs/arquitetura.md`, `docs/configuracoes.md` e `docs/execucao.md`).

## O que foi pedido, item por item

Parte 1 — P-043:

1. Escrever os testes que faltam na Família: limite 429 nas escritas delegadas; corrida "encerrar o vínculo espera a operação
   delegada em andamento" (determinística, no estilo do teste com gatilho `DEFERRABLE` e `pg_locks` de `FamilyChecks.Review.cs`);
   recusa de oferta pelo responsável.
2. Teste vermelho é defeito: corrigir com a menor mudança.
3. Conferir se a frase sobre a não atomicidade da sugestão de e-mail ainda é verdade e deixar `docs/api.md` exato.

Parte 2 — P-042 na API, seguindo exatamente os contratos de `contratos-p042.md` (G1–G4, P1, P2, `GET /api/patient/devices/push`,
envio FCM HTTP v1, quatro eventos, outbox, privacidade):

4. Nenhum pacote NuGet novo (RS256 com `System.Security.Cryptography`; `HttpClient` via `IHttpClientFactory`).
5. Tudo desligado e inofensivo sem configuração; nenhuma credencial no repositório.
6. Migração central nova 018 (UBS 015 só se precisar de marca por UBS), aditiva e reaplicável.
7. Worker de push no padrão do `MaintenanceWorker`/`UnitLightningWorker` (intervalo configurável, desligável, primeiro passe
   atrasado), outbox com marca de "já avisado", tentativas limitadas e remoção do token `UNREGISTERED`.
8. Corpo do push genérico (sem nome, CPF, especialidade ou dado de saúde).
9. Testes do Google (chave RSA gerada no teste, JWKS falso): válido, `alg` none/HS256, `kid` desconhecido, `aud` errado, `iss`
   errado, expirado, e-mail não verificado, `sub` de outra conta (409), login sem vínculo (401 genérico), conta inativa (401),
   desligado (503), unlink idempotente.
10. Testes do push (FCM falso): cada um dos 4 eventos gera exatamente um envio para os tokens certos (inclusive responsável de
    dependente sem conta), não repete no passe seguinte, `UNREGISTERED` removido, falha transitória repete até o teto, sem
    configuração não envia nada, corpo sem nome/CPF.
11. Teste vermelho antes de cada implementação.
12. Validação: build Release de `Nora.Api` e `tests/Nora.Api.Checks` (`-m:1`, 0/0) e
    `tests/validar-postgres-descartavel.ps1 -IncludeUnitDatabases` nas portas 55750 e 55751, todas as suítes verdes.
13. Documentar em `docs/api.md`, `docs/configuracoes.md`, `docs/arquitetura.md`, `docs/execucao.md`; este documento.
14. `docs/PENDENCIAS.md`: só mover P-043 e atualizar P-042 no fim.
15. Devolver: tabela item → teste (vermelho antes?) → arquivos; números; o que o usuário precisa criar no Google e onde pôr.

## Objetivo

Fechar P-043 com testes e a documentação exata da sugestão de e-mail, e entregar na API o Entrar com Google e o push pelo
servidor prontos e desligados até o usuário criar as credenciais.

## Restrições

- Não tocar em `APPNORA` nem em `docs/app-paciente.md` (outro agente).
- Não fazer commit, stash, reset nem troca de branch.
- Nenhum pacote NuGet novo; nenhuma credencial versionada.
- `docs/PENDENCIAS.md`: só mover P-043 e atualizar P-042; nunca apagar entradas.

## Checklist

- [x] P-043: 429 nas escritas delegadas (teste)
- [x] P-043: corrida encerrar × operação delegada (teste determinístico)
- [x] P-043: recusa de oferta pelo responsável (teste)
- [x] P-043: frase da sugestão de e-mail conferida e `api.md` exato
- [x] P-042: migração 018
- [x] P-042: validação do ID token Google (RS256, JWKS com cache, iss, aud, exp, email_verified)
- [x] P-042: G1–G4
- [x] P-042: P1, P2 e `GET /api/patient/devices/push`
- [x] P-042: envio FCM HTTP v1 com OAuth2 de conta de serviço
- [x] P-042: worker e outbox dos quatro eventos
- [x] Testes Google (vermelhos antes)
- [x] Testes push (vermelhos antes)
- [x] Build Release 0/0
- [x] Postgres descartável porta 55750 verde
- [x] Postgres descartável porta 55751 verde
- [x] Documentação (api, configuracoes, arquitetura, execucao)
- [x] PENDENCIAS (P-043 movida, P-042 atualizada)

## Estado

Concluída em 2026-11-16. Nada foi commitado (instrução do agente principal).

| Item | Teste | Vermelho antes? | Arquivos |
| --- | --- | --- | --- |
| P-043: 429 nas escritas delegadas (60 por 10 min somando as quatro escritas; agendamento 20 dividido com o próprio responsável; confirmação 40) | `FamilyChecks.Gaps.cs`, 2 verificações | Não: verde de primeira, sem defeito (a regra já existia) | `tests/Nora.Api.Checks/FamilyChecks.Gaps.cs`, `Program.cs` (`--family-gaps-only`) |
| P-043: corrida encerrar × operação delegada | `FamilyChecks.Gaps.cs` (linha da consulta segura na UBS, espera do encerramento observada em `pg_stat_activity`) | Verde com o código atual; **vermelho** ao tirar o `FOR SHARE` de `UnitAccessService.ExecuteGuardianAsync` (conferido e revertido) | idem |
| P-043: recusa de oferta pelo responsável | `FamilyChecks.Gaps.cs` | Não: verde de primeira, sem defeito | idem |
| P-043: frase da sugestão de e-mail | revisão de `UnitPatientStatusService` | A frase da pendência estava desatualizada (a central confirma **depois** da UBS) e `api.md` não dizia o efeito da janela | `docs/api.md` |
| P-042: migração 018 | `GooglePushChecks` (reaplicada) | Sim (0/14 antes da implementação) | `Nora.Api/Database/Migrations/018_google_push.sql` |
| P-042: Google G1–G4 e validação do ID token | `GooglePushChecks`, 5 verificações + 1 HTTP sem banco | Sim (404 nas rotas) | `ApiEndpoints.Identity.cs`, `GoogleIdTokenValidator.cs`, `GoogleAccountService.cs`, `GoogleIdentityDao.cs`, `AuthService.cs`, `ApiProblems.cs`, `AccountRateLimiter.cs`, `Models/IdentityModels.cs` |
| P-042: aparelhos P1/P2 e estado | `GooglePushChecks`, 1 verificação | Sim | `ApiEndpoints.Identity.cs`, `PushDao.cs` |
| P-042: worker, outbox e os 4 eventos, UNREGISTERED, tentativas, privacidade, sem configuração | `GooglePushChecks`, 8 verificações | Sim | `PushService.cs` (com `PushWorker`), `PushSender.cs`, `PushOptions.cs`, `PushDao.cs`, `NoraApiHost.cs`, `ForwardedHeadersSetup.cs` |
| Infra de teste | — | — | `FamilyChecks.Fixture.cs` (fixture `internal`, argumentos e serviços extras), `FamilyChecks.cs`, `GooglePushChecks.cs`, `GooglePushChecks.Fakes.cs` |

Validações (números reais):

- `dotnet build Nora.Api/Nora.Api.csproj -c Release -m:1` → 0 avisos, 0 erros; `dotnet build tests/Nora.Api.Checks/Nora.Api.Checks.csproj -c Release -m:1` → 0 avisos, 0 erros.
- `validar-postgres-descartavel.ps1 -IncludeUnitDatabases -Port 55750` → saída 0: HTTP 32/32, fluxos 28/28, bancos separados PASS, plataforma 19/19, revisão 17/17, comunicação 16/16, agenda 21/21, Família 21/21, revisão da Família 14/14, decisões 7/7, lacunas 5/5, Google e push 15/15 (`.build/postgres-validation-25b7c4f534ae494c8255d1871f293826`).
- Mesma suíte com `-Port 55751` → saída 0, mesmos números (`.build/postgres-validation-497f5e9d02cb4d0c97e82555b606f93f`).

Documentação: `docs/api.md` (seção "Entrar com Google e push pelo servidor", rota de login na tabela, códigos de erro, limites, migração 018, sugestão de e-mail exata e verificação das lacunas), `docs/configuracoes.md` (chaves e "como ligar"), `docs/arquitetura.md` (worker e outbox), `docs/execucao.md` (números e opções do harness), `README.md` (migração 018 e estado de P-042), `docs/PENDENCIAS.md` (P-043 resolvida, P-042 atualizada).

Pendente (fora desta tarefa): credenciais reais do Google e Firebase, a parte do app e o teste com o Google de verdade (P-042).
