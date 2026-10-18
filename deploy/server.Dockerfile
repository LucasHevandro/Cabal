# Imagem do servidor do jogo (jogo + API de login no mesmo processo).
# Construir na raiz do projeto:  docker build -f deploy/server.Dockerfile -t game-server .
# Portas: 7777 (jogo, TCP) e 7778 (API de login e /api/health; em produção fica atrás do Caddy com HTTPS).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Primeiro só os projetos: o restore fica em cache enquanto os pacotes não mudam.
COPY Directory.Build.props ./
COPY Shared/Game.Shared/Game.Shared.csproj Shared/Game.Shared/
COPY Server/Game.Server/Game.Server.csproj Server/Game.Server/
RUN dotnet restore Server/Game.Server/Game.Server.csproj
COPY Shared/Game.Shared/ Shared/Game.Shared/
COPY Server/Game.Server/ Server/Game.Server/
RUN dotnet publish Server/Game.Server/Game.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# O Npgsql procura a biblioteca do Kerberos ao conectar (sem ela, só um aviso feio no log).
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
# Versão que está rodando (o GitHub Actions passa o commit).
ARG GAME_VERSION=local
ENV GAME_VERSION=$GAME_VERSION \
    DOTNET_gcServer=0 \
    TZ=America/Sao_Paulo
EXPOSE 7777 7778
# Usuário sem privilégios (já existe na imagem da Microsoft).
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 CMD ["dotnet", "Game.Server.dll", "--check-health"]
ENTRYPOINT ["dotnet", "Game.Server.dll", "--port", "7777"]
