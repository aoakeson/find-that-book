# syntax=docker/dockerfile:1
# Find That Book in one image: the web app built by Node, served by the API from wwwroot on one origin.
#   docker compose up --build        then open http://localhost:8080
#   docker build -t find-that-book . && docker run --rm -p 8080:8080 --env-file .env find-that-book
# Production environment: the local-only Admin page and its /api/admin endpoints are off in this image by design.

# 1. Web app: vite build writes to ../src/FindThatBook.Api/wwwroot (see web/vite.config.ts).
FROM node:24-slim AS web
WORKDIR /repo
COPY package.json package-lock.json ./
COPY web/package.json web/
RUN npm ci --no-audit --no-fund
COPY web/ web/
RUN npm run build -w web

# 2. API: restore first (cached until a project file changes), then publish.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FindThatBook.Core/FindThatBook.Core.csproj src/FindThatBook.Core/
COPY src/FindThatBook.Infrastructure/FindThatBook.Infrastructure.csproj src/FindThatBook.Infrastructure/
COPY src/FindThatBook.Api/FindThatBook.Api.csproj src/FindThatBook.Api/
RUN dotnet restore src/FindThatBook.Api/FindThatBook.Api.csproj
COPY src/ src/
RUN dotnet publish src/FindThatBook.Api/FindThatBook.Api.csproj -c Release --no-restore -o /app -p:UseAppHost=false

# 3. Runtime: ASP.NET Core only, non-root, port 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
COPY --from=api /app ./
COPY --from=web /repo/src/FindThatBook.Api/wwwroot ./wwwroot
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FindThatBook.Api.dll"]
