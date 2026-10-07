# API NORA

## Fundação técnica

`Nora.Api` é uma API ASP.NET Core .NET 10. Usa apenas o framework compartilhado, sem pacotes externos. `NoraApiHost` constrói o host e o pipeline; `ApiProblems` centraliza os erros HTTP. A solução `NORA.slnx` inclui a API e seu harness de verificações.

O usuário confirmou PostgreSQL para a persistência futura. Banco, schema, autenticação, autorização, contas e operações clínicas ainda não foram implementados. O serviço atual verifica somente sua própria capacidade de responder HTTP; não afirma que PostgreSQL, notificações ou aplicativos estejam prontos. As interfaces existentes continuam com os estados vazios documentados.

## Execução local

Requer .NET SDK 10. A configuração versionada liga o serviço somente ao loopback IPv4, em `http://127.0.0.1:5080`, e limita os hosts aceitos a endereços locais. Não há perfil de lançamento com portas aleatórias nem abertura automática de navegador.

```powershell
dotnet build Nora.Api/Nora.Api.csproj --configuration Release --nologo -m:1 --disable-build-servers
dotnet run --project Nora.Api/Nora.Api.csproj --configuration Release --no-build
Invoke-RestMethod http://127.0.0.1:5080/health/live
```

O ASP.NET Core permite sobrescrever a URL por `--urls` ou `ASPNETCORE_URLS`; uma porta local alternativa pode ser usada assim:

```powershell
dotnet run --project Nora.Api/Nora.Api.csproj --configuration Release --no-build -- --urls http://127.0.0.1:5081
```

Essa configuração se destina à validação local. Publicação externa depende das decisões restantes de autenticação, autorização, HTTPS, hosts e persistência. Não há CORS liberado, credenciais versionadas, migração ou acesso a banco.

## Contratos HTTP

`GET /health/live` retorna HTTP 200, `application/json` e `Cache-Control: no-store`:

```json
{"status":"alive","scope":"process"}
```

`HEAD /health/live` retorna HTTP 200 sem corpo. Outros métodos nessa rota retornam HTTP 405 com `Allow: GET, HEAD`. Endereços sem rota retornam HTTP 404, incluindo `/health/ready`; não existe endpoint de readiness nesta fundação.

Erros sem corpo gerados pelo pipeline usam `application/problem+json`, `Cache-Control: no-store` e campos `type`, `title`, `status`, `detail`, `code` e `traceId`. Os códigos são `not-found` para 404, `method-not-allowed` para 405 e `internal-error` para 500. `type` é `urn:nora:problem:<code>`. O `traceId` identifica a solicitação no servidor. O corpo não ecoa endereço, query string, payload, stack trace ou mensagem da exceção.

O contrato JSON de erro é mantido mesmo quando o cliente pede `text/html`. Exceções são tratadas de forma igual em Development e Production; detalhes técnicos permanecem no log do servidor, não na resposta. O cabeçalho de identificação `Server` está desativado. Respostas que já tenham começado a ser enviadas não podem ser convertidas em outro corpo de erro pelo pipeline.

## Verificação executada em 2026-10-07

```powershell
dotnet build tests/Nora.Api.Checks/Nora.Api.Checks.csproj --configuration Release --nologo -m:1 --disable-build-servers
dotnet run --configuration Release --project tests/Nora.Api.Checks/Nora.Api.Checks.csproj --no-build
```

Build Release passou com zero erros e avisos. O harness inicia Kestrel em porta dinâmica no loopback e faz requisições HTTP reais com `HttpClient`, sem servidor simulado. Passaram 20/20 verificações, em Development e Production: liveness GET/HEAD, ausência de cache e identificação do servidor, 404 sem eco de entrada, POST inexistente, 405/Allow, ausência de CORS, Accept alternativo, exceção 500 sanitizada, ausência de endpoints não implementados e 32 solicitações concorrentes por ambiente com identificadores independentes. A rota que provoca exceção é mapeada somente no processo do harness e não existe no executável da API.

O sandbox da sessão bloqueou conexões de saída ao loopback, embora Kestrel tenha iniciado. A execução autorizada fora do sandbox passou; a falha de socket não foi tratada como defeito da API nem contornada com um teste simulado. Também foi iniciado o executável real em Production com a configuração padrão: requisições por `Invoke-WebRequest` confirmaram HTTP 200 e o JSON esperado em `/health/live`, além de HTTP 404 em `/health/ready`. O processo foi encerrado após a conferência.

As escolhas seguem as referências oficiais de [tratamento de erros ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0) e [distinção entre liveness e readiness](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0).
