# HTTP 403 no cadastro de médicos pelo PowerShell

## Skills e comandos invocados

- Skill `aura-retomar-execucao:diagnosing-bugs`.
- Reprodução de `pwsh -NoProfile -File .\scripts\cadastrar-medicos-simulacao.ps1 -Ubs 'UBS Escola'`.

## Pedido e objetivo

Explicar por que a API mostra `ready` no navegador e o script recebe HTTP 403. Restabelecer uma forma de executar o cadastro e orientar o usuário a partir da pasta já indicada.

## Restrições

Preservar o fluxo de autenticação, o catálogo e a seleção da UBS. Usar somente sondas públicas sem credenciais no Render e contas sintéticas para validar operações de escrita localmente. Preservar as alterações dos outros chats.

## Checklist

- [x] Reproduzir a diferença entre o PowerShell e outro cliente.
- [x] Corrigir a chamada HTTP e a mensagem de erro.
- [x] Validar sintaxe, chamadas públicas e o fluxo local descartável.
- [x] Registrar a pendência de validação real e documentar o comando.

## Estado

O PowerShell recebeu HTTP 403 com `cf-mitigated: challenge` e HTML em `/health/ready` e `/api/registration/units`, sem identificador de requisição do Render. O `curl.exe` retornou HTTP 200 e JSON nas duas rotas. A proteção do Cloudflare explica por que a resposta do script difere da exibida no navegador.

- O script se identifica como `Projeto-NORA-Simulacao/1.0`. Um desafio retorna mensagem com `CF-Ray`. Na prontidão, o script encerra imediatamente para HTTP 403 e informa o código retornado.
- O script corrigido recebeu HTTP 200 na prontidão real. O login com entrada sintética chegou ao NORA e recebeu HTTP 400. Duas simulações de desafio, na prontidão e no login, confirmaram a mensagem sem imprimir a página HTML.
- O cadastro local descartável terminou com 20 especialidades, 40 médicos, 20 consultórios e 200 faixas. A segunda execução concluiu com zero registros novos. A API e o PostgreSQL de teste foram desligados. A sintaxe PowerShell e o diff foram conferidos.
- O endpoint público respondeu com zero UBS disponíveis. O cadastro real depende de criar ou concluir a base da UBS Escola, de conceder acesso à conta OBS e de usar as credenciais reais. A P-049 reúne esse estado.
- Na mesma pasta do worktree, rodar `pwsh -NoProfile -File .\scripts\cadastrar-medicos-simulacao.ps1 -Ubs 'UBS Escola'`.
