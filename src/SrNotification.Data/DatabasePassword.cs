using Npgsql;

namespace SrNotification.Data;

/// <summary>
/// Keeps the database password out of appsettings.json: connection strings are configured
/// without a password and it is added at startup from the POSTGRES_PASSWORD variable
/// (the .env file locally, docker compose in containers).
/// </summary>
public static class DatabasePassword
{
    public const string VariableName = "POSTGRES_PASSWORD";

    /// <summary>
    /// Returns <paramref name="connectionString"/> with <paramref name="password"/> added.
    /// A connection string that already contains a password is returned unchanged.
    /// </summary>
    public static string AddTo(string connectionString, string? password)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        if (!string.IsNullOrEmpty(builder.Password))
        {
            return builder.ConnectionString;
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                $"The database password is not set. Create a .env file in the repository root with " +
                $"{VariableName}=<password> (see .env.example), or set the {VariableName} environment variable.");
        }

        builder.Password = password;
        return builder.ConnectionString;
    }
}
