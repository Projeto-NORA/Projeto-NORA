# Integração da simulação à main e preparação do Neon

## Skills e comandos invocados

- O usuário pediu continuar em conjunto com o chat de Render e Neon.
- Skill `merge-preservando-alteracoes` para integrar os commits.

## Pedido e objetivo

1. Enviar os commits desta tarefa para a `main` em conjunto com o outro chat.
2. Entregar o procedimento e o script para preparar no Neon o banco e o catálogo da simulação, esclarecendo o que pode ser feito pelo SQL Editor.
3. Explicar se as contas OBS e Municipal criadas no PowerShell já estão no Neon e se o mesmo e-mail e senha funcionam em outros computadores.

## Restrições

Preservar os commits dos dois chats e o trabalho local. A integração à `main` foi autorizada. O usuário quer rodar a preparação do Neon. Usar a arquitetura do NORA, manter segredos fora do chat e do Git e considerar o ambiente PostgreSQL 18.

## Checklist

- [x] Conferir o estado do repositório e o andamento da integração no outro chat.
- [x] Validar o conteúdo da integração com a `main` antes do envio.
- [x] Conferir migrações, provisionamento, catálogo e estado verificável do Neon.
- [x] Documentar os comandos e a conferência de onde as contas foram criadas.

## Estado

- O [PR #1 da conexão do Neon](https://github.com/Projeto-NORA/Projeto-NORA/pull/1) já estava integrado à `origin/main` em `eaf837c`. A branch da simulação incorporou esse histórico sem conflitos em `75b7b5e`. O clone principal continuou na branch do outro chat, `codex/conexao-url-neon`.
- `scripts/conferir-neon-nora.sql` consulta o banco central pelo SQL Editor. Foi validado em PostgreSQL 18.7 descartável antes da migração, depois da migração central e com registros sintéticos ativos e inativos. O processo foi encerrado e o cluster removido.
- `scripts/implantar-neon.ps1` localiza também o `psql` instalado em `C:\Tools\postgresql-18`. A descoberta do executável e a sintaxe dos dois scripts PowerShell foram verificadas. A compilação em Release de `tests/Nora.Api.Checks` concluiu com zero avisos e erros.
- A suíte da API integrada em PostgreSQL 18.7, com bancos separados por UBS, concluiu com código zero e 290 verificações aprovadas. O PostgreSQL descartável foi encerrado. O PR da simulação registra a publicação no GitHub e preserva os commits individuais da branch.
- O Render respondeu HTTP 200 e `{"status":"ready","scope":"database"}` após o PR #1, o que confirma acesso à tabela central de contas. Ainda é preciso conferir as contas e a UBS Escola no Neon. A lista pública de UBS retornou um desafio de acesso para a consulta feita deste ambiente. O estado foi registrado na P-049.
- O roteiro de hospedagem explica onde o provisionamento grava os logins e como usar a mesma API em outros computadores. Os dois guias da simulação passaram a apontar para a `main`.

## Comandos para o usuário

1. No Neon, abrir o SQL Editor na branch e no banco da conexão do Render. Rodar `scripts/conferir-neon-nora.sql` para conferir o estado.
2. Na raiz da `main` atualizada, executar `pwsh -File scripts/implantar-neon.ps1` se faltar migração ou algum dos acessos. Usar a URL direta do Neon e aceitar o cadastro apenas para os acessos que faltam.
3. Com o Render pronto, usar o Painel Municipal para cadastrar a UBS Escola se precisar e conceder acesso à conta de administrador do OBS.
4. Executar `pwsh -File scripts/cadastrar-medicos-simulacao.ps1 -Ubs 'UBS Escola'`. Esse script grava pela API os profissionais, os consultórios e a grade no banco da UBS, além das especialidades da rede no banco central.
