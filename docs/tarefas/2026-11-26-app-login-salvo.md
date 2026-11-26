# App do paciente: login salvo por 1 dia ou 30 dias (26/11/2026)

## Skills e comandos invocados

Nenhum comando ou skill do usuário. Tarefa recebida de um fluxo de trabalho (workflow) que repassa o pedido do usuário; branch própria `worktree-wf_5640a03a-b07-2`, pasta isolada. Escopo: só `APPNORA/` e a documentação. Não mexer em `Nora.Desktop`, `OBS-NORA`, `Painel-Municipal` nem `tests/Nora.Desktop.Checks`.

## O que foi pedido

Pedido do usuário (26/11/2026): "quero que o login seja salvo: a pessoa fez login um dia, fica salvo pelos dias que ela selecionou; se não selecionar nada, 1 dia; se selecionar manter por 30 dias, 30 dias". Vale para o app do paciente. Esta decisão **substitui** a regra anterior do app de nunca gravar token em disco (`PatientController.kt`, comentários de `startSession` e `AddressStore`); registrar isso na documentação com a data.

A API está sendo alterada por outra equipe: sessão de paciente de 24 h por padrão e 30 dias com `keepSignedIn=true` no corpo de `POST /api/auth/login`; `POST /api/auth/google` passa a aceitar o campo; a resposta traz `token`, `expiresAt` e `keepSignedIn`. O app deve funcionar também contra a API antiga (ignora o campo e devolve 8 h): obedece sempre ao `expiresAt` recebido.

Itens:

- **A.** Tela de entrada: caixa "Manter conectado por 30 dias" (desmarcada por padrão; apoio curto: sem marcar, fica conectado por 1 dia), acessível, nos dois temas, com fonte ampliada. O login com Google usa a mesma escolha.
- **B.** Guardar a sessão em disco com segurança: token, vencimento e a identidade mínima, cifrados com chave do Android Keystore (AES-GCM, sem dependência nova), em arquivo de preferências próprio, fora do backup automático (conferir `allowBackup`, `dataExtractionRules`, `fullBackupContent`). Chave ou dado corrompido: apagar e pedir login, sem cair.
- **C.** Ao abrir: sessão guardada e não vencida entra direto e confere no servidor pela rota de sessão já usada (`GET /api/auth/me`); 401 apaga tudo e volta à entrada com mensagem clara; falha de rede NÃO apaga (mostra o estado de conexão do app e tenta de novo). Vencida pelo relógio: apaga sem chamar a rede. Mostrar o trabalho ("Restaurando a sua sessão…", indicador na cor do app).
- **D.** "Sair da conta", troca de senha que encerra a sessão, conta desativada e qualquer 401 definitivo apagam a sessão guardada. Serviço de conexão/presença e notificações coerentes depois de restaurar (`PatientPresenceService`, ciclo de 30 s).
- **E.** Nada de token ou identidade em log, Intent, estado salvo (SavedState) ou captura.
- **F.** Testes unitários novos (guardar/ler/apagar, vencido, corrompido, 401 x falha de rede, logout) e adaptação dos existentes; rodar em `APPNORA`: `.\gradlew.bat :app:testDebugUnitTest :app:lintDebug :app:assembleDebug`. Tela de entrada mudou: `:app:updateDebugScreenshotTest` e depois `:app:validateDebugScreenshotTest`, conferindo as imagens novas. Sem emulador e sem `connectedDebugAndroidTest`; deixar os instrumentados que precisarem de ajuste atualizados e dizer quais rodar depois.
- **G.** `docs/app-paciente.md` atualizado (sessão, segurança, o que fica em disco e por quanto tempo).

## Objetivo

Quem entra no app continua conectado ao reabri-lo, pelo prazo que o servidor concedeu (1 dia sem marcar, 30 dias marcando), sem guardar nada legível em disco e sem nunca ficar preso numa sessão inválida.

## Restrições

- Mudança mínima e completa; sem dependência nova; sem TODO; sem token, senha ou identidade em log, Intent, estado salvo ou captura.
- Não ligar emulador, não rodar `connectedDebugAndroidTest`, não rodar `tests/desktop-startup.ps1`, não bloquear/suspender o computador.
- Commit na branch própria, sem `Co-Authored-By`, sem push. Pendência nova a partir de P-078.

## Decisões de desenho

- `PatientController` ganha um `SessionStorage` (por padrão `NoSessionStorage`; o construtor com `Context` usa `SecureSessionStorage`, AES-GCM com chave do Keystore). A sessão guardada tem token, `expiresAt`, id e nome do usuário e o endereço do serviço (só id e nome: CPF mascarado e e-mail vêm de novo do `GET /api/auth/me` da conferência).
- A sessão é presa ao endereço do serviço: se o endereço do build mudou, a sessão é descartada (o token não vai a outro servidor).
- `signedOut` (todo 401, logout, conta desativada) apaga o disco; `closeLocalSession` (app removido dos recentes) NÃO apaga: é o caso em que a pessoa quer continuar conectada ao reabrir.
- Restauração em dois tempos: leitura do disco fora da thread principal (Início mostra "Restaurando a sua sessão…" no lugar do formulário) e conferência no servidor com a sessão já aberta (linha de progresso no Início). Falha de rede deixa `unconfirmed` e o ciclo de 30 s do serviço de conexão repete a conferência.
- `login` e `loginWithGoogle` da API ganham uma sobrecarga com `keepSignedIn` (a antiga continua, para servidor e dublês antigos).

## Checklist

- [x] A. Caixa "Manter conectado por 30 dias" no login (e Google): feito, conferido nas prévias 55, 56 e 57 (claro, escuro, Google, fonte 1.3) e no teste instrumentado (compilado, não executado).
- [x] B. Armazenamento cifrado (Keystore, AES-GCM), fora do backup: feito; cifra testada no JVM com AES-GCM real; Keystore real só no instrumentado (P-078).
- [x] C. Restauração e conferência no servidor: feito e testado no JVM (vencida sem rede, 401, falha de rede, ciclo de reconfirmação).
- [x] D. Todos os caminhos de fim de sessão apagam o disco: `signedOut` (todo 401), logout (antes da rede), vencimento; remover dos recentes mantém de propósito.
- [x] E. Nada sensível em log, Intent, SavedState ou captura: nenhum `Log` novo com dado de sessão, `StoredSession.toString()` oculto, `keepSignedIn` fica no controlador (não em `rememberSaveable`).
- [x] F. Testes novos e adaptados, Gradle e prévias rodados (números abaixo). Instrumentados só compilados (P-078).
- [x] G. `docs/app-paciente.md`, `docs/PENDENCIAS.md` (P-078 a P-080), `docs/api.md`, `docs/arquitetura.md`, `README.md`.

## Estado e resultados reais (26/11/2026)

Comando, em `APPNORA` com JDK 21 do Adoptium: `.\gradlew.bat :app:testDebugUnitTest :app:lintDebug :app:assembleDebug :app:assembleDebugAndroidTest :app:validateDebugScreenshotTest --rerun`.

- BUILD SUCCESSFUL. **341 testes locais, 0 falhas, 0 erros, 0 pulados** (novos: `SessionStorageTest` e `SessionControllerTest`, mais 2 em `NoraApiHttpTest`). Uma primeira rodada falhou em 1 teste meu (comparação de JSON por ordem de chaves do `org.json`); corrigido no teste.
- `lintDebug`: 0 erros, 17 avisos (os de sempre; os 3 `UseKtx` apontados em `NoraApp.kt` e `LoginExtras.kt` já existiam no código antigo de `apply()`).
- `assembleDebug` e `assembleDebugAndroidTest`: compilaram.
- Prévias: `updateDebugScreenshotTest` e depois `validateDebugScreenshotTest` passaram, 58 imagens de referência (as referências são ignoradas pelo Git e esta pasta nova não tinha nenhuma; todas foram geradas do zero). Cinco previews novas (55 a 59) e lidas na imagem: entrada com a caixa marcada (clara), com Google (escura), fonte ampliada 1.3 (escura, texto e apoio quebram sem cortar), "Restaurando a sua sessão…" no lugar do formulário e a linha no Início aberto (escura). As previews de entrada existentes (02, 03, 04, 13, 52) mudaram de propósito (a caixa nova).
- **Não rodado:** nenhum emulador ligado e nenhum `connectedDebugAndroidTest` (pedido da tarefa). Instrumentados novos ou alterados a rodar depois (P-078): `SecureSessionStorageTest` (novo), `PatientInteractionTest` (3 testes novos), `PatientControllerTest`, `PatientPresenceServiceTest` e `VisualCaptureTest` (agora com `NoSessionStorage`, para não gravar sessão de teste no aparelho).
- **Não verificado:** contra a API nova (P-079) e em celular real (P-080).

## Revisão final (loop de verificação)

Conferido item a item contra o pedido (A a G) e varrido: compilação, nulos, concorrência (restauração no `viewModelScope`, retomada após cancelamento, `presenceMutex` na reconfirmação), tarefa removida dos recentes (mantém), toque em notificação durante a restauração (espera, não descarta), `keepSignedIn` preservado após senha errada, endereço do serviço preso à sessão, lixo (nenhum arquivo temporário). Ajuste feito na volta: o `NoraApp` com controlador próprio também restaura a sessão (antes só o `PatientViewModel` o fazia). Última volta sem achados.

## Sugestão do próximo passo

Mostrar no Perfil "Conectado até dd/mm/aaaa" (o `expiresAt` já está na sessão) para a pessoa ver quando o login salvo vence.
