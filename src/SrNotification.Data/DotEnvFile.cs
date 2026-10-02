namespace SrNotification.Data;

/// <summary>
/// Minimal .env loader for local development. Copies KEY=VALUE lines into the process
/// environment so they reach IConfiguration; variables that are already set win.
/// </summary>
public static class DotEnvFile
{
    private const string FileName = ".env";
    private const string SolutionFileName = "SrNotification.slnx";

    /// <summary>
    /// Looks for .env in the current directory and its parents, up to the repository root
    /// (the folder holding SrNotification.slnx). Does nothing if there is no .env file.
    /// </summary>
    public static void Load()
    {
        var path = Find();
        if (path is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? Find()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // Don't wander above the repository and pick up some unrelated .env file.
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return null;
            }
        }

        return null;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0], value[^1]) is ('"', '"') or ('\'', '\'')
            ? value[1..^1]
            : value;
}
