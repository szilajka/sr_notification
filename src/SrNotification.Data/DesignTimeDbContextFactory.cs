using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SrNotification.Data;

/// <summary>
/// Used only by the `dotnet ef` tools (migrations). Reads the same settings as the app: the
/// connection string from the ConnectionStrings__SrNotification environment variable (falling back
/// to the local docker-compose database) and the password from POSTGRES_PASSWORD, which can come
/// from the .env file at the repository root.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SrNotificationDbContext>
{
    public SrNotificationDbContext CreateDbContext(string[] args)
    {
        DotEnvFile.Load();

        var connectionString = DatabasePassword.AddTo(
            Environment.GetEnvironmentVariable("ConnectionStrings__SrNotification")
                ?? "Host=localhost;Port=5432;Database=sr_notification;Username=sr_notification",
            Environment.GetEnvironmentVariable(DatabasePassword.VariableName));

        var options = new DbContextOptionsBuilder<SrNotificationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SrNotificationDbContext(options);
    }
}
