using System.Globalization;

namespace Inscribed.Cli;

internal sealed class UsageException : Exception
{
    public UsageException(string message) : base(message) { }
}

internal sealed class CommandOptions
{
    private readonly Dictionary<string, string> _values;

    private CommandOptions(Dictionary<string, string> values)
    {
        _values = values;
    }

    public static CommandOptions Parse(string[] args, int offset)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = offset; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"Unexpected argument '{arg}'.");
            }

            var separator = arg.IndexOf('=');
            if (separator >= 0)
            {
                values[arg[2..separator]] = arg[(separator + 1)..];
                continue;
            }

            var name = arg[2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[name] = bool.TrueString;
                continue;
            }

            values[name] = args[++i];
        }

        return new CommandOptions(values);
    }

    public string Require(string name) =>
        _values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new UsageException($"--{name} is required.");

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public string[]? GetList(string name) =>
        Get(name)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool? GetBool(string name)
    {
        var value = Get(name);
        if (value is null)
        {
            return null;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : throw new UsageException($"--{name} must be true or false.");
    }

    public DateTime? GetUtcDateTime(string name)
    {
        var value = Get(name);
        if (value is null)
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : throw new UsageException($"--{name} must be a date such as 2027-01-31 or 2027-01-31T12:00:00Z.");
    }

    public Guid RequireGuid(string name) =>
        Guid.TryParse(Require(name), out var parsed)
            ? parsed
            : throw new UsageException($"--{name} must be a GUID.");
}
