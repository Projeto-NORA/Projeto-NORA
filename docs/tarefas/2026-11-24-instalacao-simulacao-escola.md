# Instalação e simulação do NORA na escola — 2026-11-24

## Skills e comandos invocados

- `/skills-aura:aura` — o usuário colou o comando junto com o pedido. A skill tem `disable-model-invocation` e não pôde ser carregada pela IA; o pedido foi atendido diretamente. Para rodar o fluxo dela, o usuário digita `/skills-aura:aura` na caixa de mensagem.

## O que foi pedido

1. Terminar a parte de instalação dos dois softwares (desktop e aplicativo) para a simulação do hospital na escola.
2. Os pacientes da simulação são os **funcionários** da escola (os alunos não podem usar celular). Eles baixam os aplicativos pelo **GitHub Releases** do projeto.
3. Analisar se isso é possível e se é a melhor forma; se houver forma melhor, dizer; se não, seguir com ela.
4. Ensinar **passo a passo** como fazer a instalação e como ficar preparado para a mini apresentação, com tudo funcionando.
5. Fazer um **cronograma** para cerca de **1 hora**, com **10 funcionários = 10 pacientes**: o que cada paciente faz e o que o OBS faz. O Painel Municipal não faz nada por enquanto.
6. Entregar o cronograma em **Word (.docx)**.

## Objetivo

O usuário sabe se a entrega pelo GitHub Releases funciona, tem um roteiro de preparação e instalação que pode seguir sozinho e um cronograma em .docx pronto para conduzir a simulação de 1 hora.

## Restrições

- Painel Municipal sem papel na simulação (só a preparação inicial, que é obrigatória: UBS e contas).
- Alunos não usam celular; quem usa o app são os funcionários.
- Sem dados de saúde reais (LGPD, `hospedagem.md`).
- Publicar repositório ou release é ação pública: só com autorização do usuário.

## Achados da análise

- O repositório `Projeto-NORA/Projeto-NORA` é **privado**: o release dele só abre para quem tem conta no GitHub e acesso à organização. Os funcionários veriam "404". Solução proposta: um repositório **público só de instaladores** (`Projeto-NORA/NORA-downloads`, sem código) com o APK no release; o link `.../releases/latest/download/NORA-paciente.apk` sempre baixa a versão mais nova e vira um QR code.
- A API em `https://nora-api-kw8x.onrender.com` **não respondeu** em 24/11/2026 (`/health/live` e `/health/ready` sem resposta em 100 s). É a P-049 ainda aberta. Sem a API nada funciona.
- O serviço da API não aparece no workspace do Render ligado a esta sessão (só serviços da TodaiBusiness), então os logs não puderam ser lidos daqui.
- O DNS do serviço aponta para `gcp-us-west1-1.origin.onrender.com`, o que sugere região Oregon, enquanto `hospedagem.md` recomenda Virginia junto com o Neon em US East. Conferir no painel do Render.
- Não há app para iPhone: funcionário com iPhone precisa de aparelho Android emprestado ou faz dupla.
- O app só cria conta depois do cadastro no OBS e com e-mail real (código), então o SMTP (Brevo) precisa funcionar.

## Checklist

- [x] Ler a documentação (README, distribuicao, hospedagem, app-paciente, obs-painel-operacional, api, PENDENCIAS, IDEIAS)
- [x] Conferir chats recentes (nenhuma decisão sobre release; P-049 seguia aberta)
- [x] Testar a API publicada (sem resposta)
- [x] Análise da distribuição pelo GitHub Releases → `docs/simulacao-escola.md`
- [x] Passo a passo de preparação e instalação → `docs/simulacao-escola.md`
- [x] Cronograma .docx → `Mateus-Projects/Claude-PLANS/Projeto-NORA/cronograma-simulacao-escola.docx`
- [x] Conferir o .docx (validação do arquivo e leitura do texto)
- [x] Atualizar `distribuicao.md` e `PENDENCIAS.md`
- [x] Criar o repositório público e o release (autorizado pelo usuário em 24/11/2026; `NORA-downloads`, release `v1.0-escola`)
- [x] Conferir o link `latest` sem login e o SHA-256 do arquivo baixado

## Estado

- Análise e passo a passo em `docs/simulacao-escola.md`; atalhos em `distribuicao.md` e no `README.md`.
- Cronograma gerado com `docx` (Node), sem pastas no ZIP, convertido para PDF pelo LibreOffice (5 páginas, A4 paisagem) e conferido página a página em imagem. O validador XSD da skill não rodou neste Windows (caminho dos esquemas não encontrado); a conversão do LibreOffice abriu o arquivo sem erro.
- Pendências: P-049 atualizada (API sem resposta em 24/11), P-061 (publicar o APK, espera autorização) e P-062 (ensaio no celular real, teste do usuário).
- Falta, do lado do usuário: concluir a P-049 e autorizar a criação do repositório público `NORA-downloads` com o release.
- Desktops publicados no release `v1.0-escola` (autorizado pelo usuário): `OBS-NORA.zip` (62.187.248 bytes) e `Painel-Municipal.zip` (61.923.361 bytes), gerados por `empacotar-clientes.ps1 -SemAndroid`; endereço da API embutido conferido em `Nora.Desktop.csproj`; download público conferido com SHA-256 igual. Guia atualizado (instalação pelo release e seção "Dados criados em casa, uso na escola"); P-063 aberta para apagar os zips depois da simulação.

## Rodada 2 (24/11/2026, depois do pedido de QR Codes e passo a passo)

- Pedido: consultar o outro chat do NORA, escrever o passo a passo do zero e gerar uma imagem com os 3 QR Codes (App do Paciente, OBS-NORA, Painel Municipal), cada um com o nome em cima.
- Outro chat ("Projeto finalização e validações"): lido pelas pendências e commits na `main` (8734f37), sem mensagem (o chat estava fechado). Nada novo além da P-049; P-059 e P-060 seguem abertas e não afetam a simulação.
- Chat paralelo nesta pasta publicou `OBS-NORA.zip` e `Painel-Municipal.zip` no release (P-063 para apagá-los depois).
- Feito: imagem dos 3 QR Codes (lidos de volta, 3/3 corretos), passo a passo em `Claude-PLANS/Projeto-NORA/passo-a-passo-final-simulacao.md`, downloads dos 3 arquivos conferidos (HTTP 200).
- Não feito: ligar a API (P-049: exige a senha do Neon e a conta do Render do NORA, fora do alcance da IA).
