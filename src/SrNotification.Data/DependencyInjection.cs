using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SrNotification.Data;

public static class DependencyInjection
{
    /// <summary>Application name shared by every app that reads data protected by another one.</summary>
    public const string DataProtectionApplicationName = "SrNotification";

    /// <summary>
    /// Registers <see cref="IDbContextFactory{SrNotificationDbContext}"/> and a scoped
    /// <see cref="SrNotificationDbContext"/> against PostgreSQL. Shared by the RSS reader, the Web API
    /// and the notification sender.
    /// </summary>
    public static IServiceCollection AddSrNotificationData(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<SrNotificationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        // Data Protection's key store resolves the context itself; make sure it is resolvable.
        services.TryAddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<SrNotificationDbContext>>().CreateDbContext());

        return services;
    }

    /// <summary>
    /// Data Protection with its keys in the database, plus <see cref="SecretProtector"/>.
    /// Call <see cref="AddSrNotificationData"/> first.
    /// </summary>
    public static IServiceCollection AddSrNotificationDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<SrNotificationDbContext>();

        services.TryAddSingleton<SecretProtector>();
        return services;
    }
}
