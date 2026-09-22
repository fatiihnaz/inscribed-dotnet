namespace Inscribed.Cli;

internal static class EnvFile
{
    private const int Levels = 6;

    private static readonly (string Variable, string Key)[] Mapped =
    [
        ("AUTH_MODE", "Auth:Mode"),
        ("AUTH_AUDIENCE", "Auth:Audience"),
        ("AUTH_AUTHORITY", "Auth:Authority"),
        ("AUTH_TENANT_CLAIM", "Auth:TenantClaim"),
        ("AUTH_ROLES_CLAIM", "Auth:RolesClaim"),
        ("AUTH_ISSUER", "Auth:Issuer"),
    ];

    public static Dictionary<string, string?>? Read(string directory)
    {
        if (Locate(directory) is not { } path)
        {
            return null;
        }

        var values = Parse(path);
        var settings = new Dictionary<string, string?>();

        foreach (var (variable, key) in Mapped)
        {
            if (values.TryGetValue(variable, out var mapped) && mapped.Length > 0)
            {
                settings[key] = mapped;
            }
        }

        if (values.TryGetValue("ConnectionStrings__Default", out var connection) && connection.Length > 0)
        {
            settings["ConnectionStrings:Default"] = connection;
        }
        else if (values.TryGetValue("DB_PASSWORD", out var password) && password.Length > 0)
        {
            settings["ConnectionStrings:Default"] =
                $"Host={Value(values, "DB_HOST", "localhost")};"
                + $"Port={Value(values, "DB_PORT", "5432")};"
                + $"Database={Value(values, "DB_NAME", "inscribed_cms")};"
                + $"Username={Value(values, "DB_USER", "postgres")};"
                + $"Password={password}";
        }

        return settings.Count == 0 ? null : settings;
    }

    private static string? Locate(string directory)
    {
        var current = new DirectoryInfo(directory);

        for (var level = 0; level < Levels && current is not null; level++)
        {
            var candidate = Path.Combine(current.FullName, ".env");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static Dictionary<string, string> Parse(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                if (trimmed.StartsWith("export ", StringComparison.Ordinal))
                {
                    trimmed = trimmed[7..].TrimStart();
                }

                var separator = trimmed.IndexOf('=');

                if (separator > 0)
                {
                    values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return values;
        }

        return values;
    }

    private static string Value(Dictionary<string, string> values, string variable, string fallback) =>
        values.TryGetValue(variable, out var value) && value.Length > 0 ? value : fallback;
}
