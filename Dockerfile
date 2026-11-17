# Imagem da Nora.Api para hospedagem (Render, VPS com Docker ou qualquer host de contêiner).
# Guia completo em docs/hospedagem.md. Nenhum segredo entra na imagem: conexão, chaves HMAC e SMTP
# vêm de variáveis de ambiente do serviço (ConnectionStrings__Nora, Nora__Registration__HashKey...).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restaura só com o .csproj primeiro, para o cache de camadas não refazer o restore a cada mudança de código.
COPY Nora.Api/Nora.Api.csproj Nora.Api/
RUN dotnet restore Nora.Api/Nora.Api.csproj
COPY Nora.Api/ Nora.Api/
RUN dotnet publish Nora.Api/Nora.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
# O Render informa a porta em PORT; fora dele vale 8080. HTTPS fica com o proxy do host.
# O appsettings.json da imagem fixa Urls=http://127.0.0.1:5080 e AllowedHosts=localhost. O Kestrel ignora ASPNETCORE_HTTP_PORTS quando "urls" está configurado, e a ASPNETCORE_URLS
# também perde para o arquivo (no host mínimo ela é configuração de host, de prioridade menor que o appsettings.json). Só a variável SEM prefixo, "Urls", vence o arquivo (testado):
# por isso a porta vai nela, escutando em todas as interfaces do contêiner (o proxy do host chega por fora).
# AllowedHosts vira o domínio público: ALLOWED_HOSTS (vários separados por ";"), senão o RENDER_EXTERNAL_HOSTNAME que o Render define sozinho, senão só localhost.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "export Urls=http://0.0.0.0:${PORT:-8080}; export AllowedHosts=\"${ALLOWED_HOSTS:-${RENDER_EXTERNAL_HOSTNAME:-localhost}}\"; exec dotnet Nora.Api.dll"]
