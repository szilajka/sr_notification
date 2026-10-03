# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/SrNotification.Data/SrNotification.Data.csproj src/SrNotification.Data/
COPY src/SrNotification.Infrastructure/SrNotification.Infrastructure.csproj src/SrNotification.Infrastructure/
COPY src/SrNotification.RssReader/SrNotification.RssReader.csproj src/SrNotification.RssReader/
RUN dotnet restore src/SrNotification.RssReader/SrNotification.RssReader.csproj

COPY src/SrNotification.Data/ src/SrNotification.Data/
COPY src/SrNotification.Infrastructure/ src/SrNotification.Infrastructure/
COPY src/SrNotification.RssReader/ src/SrNotification.RssReader/
RUN dotnet publish src/SrNotification.RssReader/SrNotification.RssReader.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
USER $APP_UID
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SrNotification.RssReader.dll"]
