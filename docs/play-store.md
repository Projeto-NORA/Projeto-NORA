# Como publicar o app NORA na Google Play Store

Guia passo a passo para quem nunca publicou um app. Calcule de **1 a 3 semanas** do começo até o app aparecer na loja; a maior parte é espera pela análise do Google.

---

## Visão geral (o caminho todo em 8 etapas)

1. Criar a conta de desenvolvedor no Google Play Console (pagamento único de **US$ 25**).
2. Verificar a identidade (documento) e, se for conta pessoal, o telefone.
3. Preparar o app: nome do pacote definitivo, chave de assinatura e o arquivo **AAB**.
4. Criar o app no Play Console.
5. Preencher a "ficha da loja" (textos, ícone, prints).
6. Responder aos questionários obrigatórios (privacidade, classificação etária, segurança dos dados, permissões).
7. Fazer o **teste fechado** obrigatório (12 testadores por 14 dias, para contas pessoais novas).
8. Enviar para **produção** e aguardar a análise.

---

## Etapa 1: Criar a conta de desenvolvedor

1. Acesse **https://play.google.com/console** com uma conta Google. O ideal é um e-mail do projeto, não pessoal, porque ele será o "dono" do app para sempre.
2. Escolha o tipo de conta:
   - **Pessoal:** mais simples, mas exige o teste fechado com 12 pessoas (Etapa 7).
   - **Organização:** exige CNPJ e um número **D-U-N-S** (gratuito, solicitado no site da Dun & Bradstreet; leva alguns dias). Não exige o teste com 12 pessoas.
   - Para um app de saúde ligado a UBS/prefeitura, o Google pode exigir conta de **organização**. Veja o aviso na Etapa 6.
3. Pague a taxa única de US$ 25 com cartão internacional.
4. Envie a foto de um documento e confirme o telefone. A verificação leva de 1 a 7 dias.

## Etapa 2: Ajustes no projeto antes de gerar o arquivo

1. **Nome do pacote (applicationId):** hoje é `com.example.app_nora`. A Play Store **não aceita** pacotes começando com `com.example`, e o nome **não pode mudar depois de publicado**. Troque em `APPNORA/app/build.gradle.kts` por algo como `br.com.nora.paciente`. Peça para o Claude fazer isso, porque o código também precisa acompanhar.
2. **Versão:** em `build.gradle.kts`, `versionCode` (número inteiro que **sobe a cada envio**: 1, 2, 3...) e `versionName` (texto visível, ex.: "1.0.0").
3. **Chave de assinatura:** já existe o script `scripts/criar-chave-android.ps1` (veja `docs/distribuicao.md`, seção 6).
   - **Guarde a chave e as senhas em 2 lugares seguros**, como um pendrive e um cofre de senhas. Sem elas você não consegue atualizar o app.
   - Ao publicar, ative o **"Assinatura de apps do Google Play"** (Play App Signing). O Google guarda a chave final, e a sua vira só a "chave de upload". Se você perder a sua, o Google consegue trocá-la.
4. **Gerar o AAB** (formato exigido pela loja, no lugar do APK). Na pasta `APPNORA`:

   ```powershell
   .\gradlew.bat :app:bundleRelease
   ```

   O arquivo sai em `APPNORA\app\build\outputs\bundle\release\app-release.aab`.
5. **API em produção:** o app precisa apontar para o endereço definitivo da API (HTTPS), e não para um servidor de teste.

## Etapa 3: Criar o app no Play Console

1. No Play Console, clique em **Criar app**.
2. Preencha:
   - Nome: "NORA";
   - idioma padrão: Português (Brasil);
   - tipo: App;
   - gratuito.
3. Aceite as declarações.

## Etapa 4: Ficha da loja ("Presença na loja" → "Ficha principal da loja")

| Item | O que colocar |
|---|---|
| Nome do app | NORA (até 30 caracteres) |
| Descrição curta | até 80 caracteres. Ex.: "Suas consultas na UBS, vagas relâmpago e histórico de saúde num só lugar." |
| Descrição completa | até 4000 caracteres, explicando as funções |
| Ícone | 512×512 PNG, já pronto em `assets/marca/play_store_icon_512.png` |
| Imagem de destaque | 1024×500 PNG/JPG, um banner com a logo e uma frase |
| Capturas de tela | de 2 a 8 prints do celular, no mínimo 1080 px no lado maior. Use o emulador e capture as telas principais |
| Categoria | Medicina, ou Saúde e fitness |
| E-mail de contato | e-mail público de suporte do projeto |
| Política de privacidade | **obrigatória**: um link público (pode ser uma página no GitHub Pages ou no Google Sites) explicando quais dados são coletados (nome, CPF, e-mail, consultas, foto), para que servem e como pedir a exclusão |

## Etapa 5: Questionários obrigatórios ("Política e programas" → "Conteúdo do app")

Responda com sinceridade. Respostas erradas são o principal motivo de reprovação.

1. **Política de privacidade:** o link da Etapa 4.
2. **Acesso ao app:** o app exige login, então forneça uma **conta de teste** (CPF/e-mail e senha de um paciente fictício numa UBS de demonstração) para o revisor do Google entrar.
3. **Anúncios:** "Não, o app não contém anúncios".
4. **Classificação de conteúdo:** questionário rápido. A resposta típica dá "Livre".
5. **Público-alvo:** maiores de 18 anos, ou "13+". Evite marcar crianças, porque isso traz regras extras.
6. **Segurança dos dados (Data safety):** declare que o app coleta:
   - nome, e-mail, CPF (identificadores);
   - informações de saúde (consultas, exames, vacinas);
   - fotos (foto de perfil);
   - dados transmitidos com criptografia (HTTPS);
   - a forma de o usuário pedir a exclusão da conta.
7. **Apps de saúde:** preencha a declaração de "Health apps". O Google pode pedir comprovação de vínculo com a instituição de saúde (UBS/prefeitura). Tenha um documento ou autorização da Secretaria de Saúde.
8. **Permissões sensíveis:**
   - **Full-screen intent (alerta da vaga relâmpago em tela cheia):** desde 2024 o Google só libera essa permissão automaticamente para apps de **chamadas e alarmes**. Na declaração, explique que é um alerta urgente, com prazo de 10 minutos, de vaga de consulta médica. Se o Google negar, o app continua funcionando com uma notificação de alta prioridade, com som e vibração, só que sem abrir sozinho sobre a tela de bloqueio.
   - **Serviço em primeiro plano** (a conexão "NORA · conexão ativa"): declare o tipo e grave um vídeo curto mostrando o uso. O Play Console pede o link do vídeo (pode ser não listado no YouTube).
   - **Notificações:** padrão, sem declaração especial.

## Etapa 6: Teste fechado (obrigatório para conta pessoal nova)

1. Em **Testar → Teste fechado**, crie uma faixa e envie o arquivo `.aab`.
2. Adicione **pelo menos 12 testadores** (e-mails de contas Google). Eles aceitam o convite pelo link e instalam pela Play Store.
3. Os 12 precisam ficar com o app instalado por **14 dias seguidos**.
4. Depois disso, aparece o botão **"Solicitar acesso à produção"**. Responda às perguntas sobre o teste. A aprovação leva até 7 dias.

> Conta de organização pula essa etapa e pode ir direto para produção (ou para o teste interno, de até 100 pessoas, sem análise).

## Etapa 7: Publicar em produção

1. **Produção → Criar nova versão**, envie o `.aab` e escreva as "notas da versão" (o que há de novo).
2. Escolha os países (Brasil).
3. Clique em **Revisar versão → Iniciar lançamento**.
4. A análise leva de algumas horas a 7 dias, ou mais na primeira vez. Se reprovar, o e-mail diz o motivo: corrija e envie de novo.

## Etapa 8: Atualizações futuras

1. Suba o `versionCode` (ex.: de 1 para 2) e o `versionName`.
2. Gere um novo `.aab` com a **mesma chave**.
3. Em Produção, crie uma nova versão e envie. Os usuários recebem a atualização automaticamente.

---

## Checklist rápido

- [ ] Conta do Play Console criada e verificada
- [ ] Pacote trocado de `com.example.app_nora` para o definitivo
- [ ] Chave de upload criada e guardada em 2 lugares
- [ ] `app-release.aab` gerado
- [ ] API de produção em HTTPS configurada no app
- [ ] Política de privacidade publicada
- [ ] Ícone 512, banner 1024×500 e de 2 a 8 prints
- [ ] Conta de teste para o revisor
- [ ] Questionários: conteúdo, público, segurança dos dados, apps de saúde, permissões (full-screen intent e serviço em primeiro plano)
- [ ] Teste fechado com 12 pessoas por 14 dias (se conta pessoal)
- [ ] Versão de produção enviada

## Enquanto o app não está na loja: QR code

Até a publicação, o app é distribuído pelo arquivo `.apk`, com o QR code que aponta para o link de download (veja `docs/distribuicao.md`). Ao escanear, o celular baixa o APK. A pessoa toca no arquivo e autoriza "instalar apps desta fonte". O Android **não permite** instalação 100% automática fora da Play Store por segurança. Depois da publicação, troque o QR code pelo link da Play Store (`https://play.google.com/store/apps/details?id=<pacote>`), e aí a instalação fica a um toque.
