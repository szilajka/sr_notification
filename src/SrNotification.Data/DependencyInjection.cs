using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SrNotification.Data;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="IDbContextFactory{SrNotificationDbContext}"/> (and the context itself, scoped)
    /// against PostgreSQL. Shared by the RSS reader, the Web API and the notification sender.
    /// </summary>
    public static IServiceCollection AddSrNotificationData(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<SrNotificationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        return services;
    }
}
