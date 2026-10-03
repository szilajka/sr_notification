# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/SrNotification.Data/SrNotification.Data.csproj src/SrNotification.Data/
COPY src/SrNotification.Infrastructure/SrNotification.Infrastructure.csproj src/SrNotification.Infrastructure/
COPY src/SrNotification.NotificationSender/SrNotification.NotificationSender.csproj src/SrNotification.NotificationSender/
RUN dotnet restore src/SrNotification.NotificationSender/SrNotification.NotificationSender.csproj

COPY src/SrNotification.Data/ src/SrNotification.Data/
COPY src/SrNotification.Infrastructure/ src/SrNotification.Infrastructure/
COPY src/SrNotification.NotificationSender/ src/SrNotification.NotificationSender/
RUN dotnet publish src/SrNotification.NotificationSender/SrNotification.NotificationSender.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
USER $APP_UID
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SrNotification.NotificationSender.dll"]
