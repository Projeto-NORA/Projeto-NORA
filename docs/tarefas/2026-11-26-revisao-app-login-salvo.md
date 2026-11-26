# Revisão crítica: login salvo do app do paciente (26/11/2026)

## Skills e comandos invocados

Nenhum comando ou skill do usuário. Tarefa de um fluxo de trabalho (workflow): revisar, com correção, o trabalho da branch
`worktree-wf_5640a03a-b07-2` (commit `63386de`), trazido por fast-forward para a branch própria `worktree-wf_5640a03a-b07-4`.
Pedido do usuário repassado pelo fluxo: "pode escrever você mesmo a função, continue."

## O que foi pedido

1. Trazer o trabalho com `git merge --ff-only worktree-wf_5640a03a-b07-2` (parar se não der fast-forward).
2. Ler o diff inteiro e os documentos do assunto (`app-paciente.md`, `android-emulador.md`, autenticação em `api.md`).
3. Conferir cada exigência do pedido original (itens A a G de `2026-11-26-app-login-salvo.md`): feita, pela metade ou faltando.
4. Procurar defeitos reais (segurança, perda de dado, concorrência, casos extremos, regressão, teste afrouxado).
5. Rodar as validações e comparar com o relato do autor.
6. Corrigir a causa de tudo o que for achado e repetir até uma volta sem achados.
7. Commit na branch própria, sem push e sem `Co-Authored-By`.

## Objetivo

O login salvo entregue funciona como o usuário pediu, sem defeito escondido, com os testes e a documentação refletindo o estado real.

## Restrições

Não mexer em `Nora.Desktop`, `OBS-NORA`, `Painel-Municipal` nem `tests/Nora.Desktop.Checks`. Não ligar emulador nem rodar
`connectedDebugAndroidTest`. Pendência nova a partir de P-078 (o autor já usou P-078 a P-080).

## Checklist

- [x] Fast-forward de `8386b04` para `63386de`.
- [x] Diff e documentos lidos.
- [x] Itens A a G conferidos contra o código: todos feitos (detalhe abaixo).
- [x] Defeitos procurados e corrigidos (4 achados).
- [x] Validações rodadas antes e depois das correções.
- [x] Documentação atualizada (`app-paciente.md`, `execucao.md`, este documento).
- [x] Commit na branch própria.

## Conferência dos itens do pedido

- **A** (caixa "Manter conectado por 30 dias", desmarcada, apoio de 1 dia, acessível, Google com a mesma escolha): feito.
- **B** (AES-GCM com chave do Keystore, arquivo próprio, fora do backup, dado ilegível apagado sem cair): feito; ver achados 1 e 2.
- **C** (restaurar, conferir em `GET /api/auth/me`, 401 apaga, rede não apaga, vencida sem rede, "Restaurando a sua sessão…"): feito; ver achado 3.
- **D** (sair, 401, conta desativada apagam; presença e notificações coerentes): feito.
- **E** (nada sensível em log, Intent, SavedState ou captura): feito; nenhum `Log` com dado, `StoredSession.toString()` oculto.
- **F** (testes e prévias): feito; instrumentados só compilados (P-078).
- **G** (`app-paciente.md`): feito.

## Achados e correções

1. **Gravação que falha era relatada como sucesso.** `PreferencesBlobStore.write` ignorava o resultado do `commit` e devolvia sempre
   `true`; com o disco cheio o app não avisava que a sessão não foi guardada. Agora devolve o resultado real do `commit`, e
   `CipheredSessionStorage.save` trata `false` como falha (apaga o resto e avisa). Uma falha de gravação também deixou de descartar a
   chave do Keystore à toa: só a falha da cifra troca a chave.
2. **Corrida na criação da chave.** Duas leituras simultâneas sem chave podiam gerar duas, e a segunda tornaria ilegível o que a primeira
   cifrou. Criar, ler e apagar a chave agora são serializados (`KeystoreSessionKey`).
3. **Queda em laço na abertura.** `verifyRestored` não tratava `IllegalArgumentException`; uma exceção ali sairia do `viewModelScope` e
   derrubaria o app, e como a sessão continuaria guardada, ele cairia a cada abertura. Agora mantém a sessão e passa ao ciclo de 30 s,
   como as outras falhas sem 401 (teste novo em `SessionControllerTest`).
4. **Testes instrumentados mexiam na sessão do app de verdade.** O `SecureSessionStorageTest` usava a mesma chave do Keystore do app, e o
   teste de dado corrompido a descartava; os testes que abrem a `MainActivity` real esperam o formulário de entrada, mas uma sessão
   guardada de uma rodada anterior seria restaurada. `secureSessionStorage` ganhou arquivo e apelido de chave opcionais (o teste usa os
   seus e apaga a chave de teste no fim), e `PatientPresenceServiceTest` e `LocalNetworkPermissionTest` apagam a sessão guardada antes.

## Estado e resultados reais

Rodada do autor reproduzida antes das correções (`testDebugUnitTest lintDebug assembleDebug assembleDebugAndroidTest`): 341 testes,
0 falhas, 0 erros, 0 pulados; lint 17 avisos, 0 erros. Igual ao relato.

Depois das correções (mesmos comandos, mais `updateDebugScreenshotTest` e `validateDebugScreenshotTest`): BUILD SUCCESSFUL,
**343 testes, 0 falhas, 0 erros, 0 pulados** (2 novos: commit recusado em `SessionStorageTest` e identidade inválida na abertura em
`SessionControllerTest`); lint 0 erros e os mesmos 17 avisos (nenhum em `SessionStorage.kt`); 58 referências de prévia geradas e
validadas, com as imagens 55 (caixa marcada, clara), 57 (fonte 1.3, escura) e 58 ("Restaurando a sua sessão…") conferidas: layout
correto. Segunda volta de verificação sobre as correções sem achados.

Não verificado: os instrumentados no emulador (P-078), a API nova (P-079) e o celular real (P-080), como já registrado pelo autor.
