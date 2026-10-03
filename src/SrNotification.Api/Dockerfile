# Build context: repository root (see docker-compose.yml)

# 1. React app
FROM node:22-alpine AS web
WORKDIR /web
COPY src/web/package.json src/web/package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm ci; else npm install; fi
COPY src/web/ ./
RUN npm run build

# 2. API, with the React build in wwwroot
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/SrNotification.Data/SrNotification.Data.csproj src/SrNotification.Data/
COPY src/SrNotification.Infrastructure/SrNotification.Infrastructure.csproj src/SrNotification.Infrastructure/
COPY src/SrNotification.Api/SrNotification.Api.csproj src/SrNotification.Api/
RUN dotnet restore src/SrNotification.Api/SrNotification.Api.csproj

COPY src/SrNotification.Data/ src/SrNotification.Data/
COPY src/SrNotification.Infrastructure/ src/SrNotification.Infrastructure/
COPY src/SrNotification.Api/ src/SrNotification.Api/
COPY --from=web /web/dist/ src/SrNotification.Api/wwwroot/
RUN dotnet publish src/SrNotification.Api/SrNotification.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
USER $APP_UID
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SrNotification.Api.dll"]
