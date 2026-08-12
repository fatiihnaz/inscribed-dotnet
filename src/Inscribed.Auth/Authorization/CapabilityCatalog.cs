using Inscribed.Domain.Exceptions;

namespace Inscribed.Auth.Authorization;

public enum GrantTarget
{
    Membership,
    ServiceKey,
}

public static class CapabilityCatalog
{
    public const string RolesClaim = "roles";

    public const string ContentRead = "content:read";
    public const string ContentWrite = "content:write";
    public const string SchemaSync = "schema:sync";
    public const string TenantAdmin = "tenant:admin";

    public static readonly string[] All = [ContentRead, ContentWrite, SchemaSync, TenantAdmin];

    public static readonly string[] HumanOnly = [TenantAdmin];

    public static readonly IReadOnlyDictionary<string, string[]> Presets =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["editor"] = [ContentRead, ContentWrite],
            ["render"] = [ContentRead],
            ["deploy"] = [SchemaSync],
            ["admin"] = [TenantAdmin],
        };

    public static string Usage =>
        $"Capabilities: {string.Join(", ", All)}.{Environment.NewLine}"
        + $"Presets: {string.Join(", ", Presets.Select(preset => $"{preset.Key} = {string.Join(" + ", preset.Value)}"))}.";

    public static string[] Resolve(IEnumerable<string>? requested, GrantTarget target)
    {
        var resolved = new SortedSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();

        foreach (var token in requested ?? [])
        {
            var normalized = token.Trim().ToLowerInvariant();
            if (normalized.Length == 0)
            {
                continue;
            }

            if (Presets.TryGetValue(normalized, out var preset))
            {
                resolved.UnionWith(preset);
            }
            else if (All.Contains(normalized, StringComparer.Ordinal))
            {
                resolved.Add(normalized);
            }
            else
            {
                unknown.Add(token.Trim());
            }
        }

        if (unknown.Count > 0)
        {
            throw new ValidationException([$"Unknown capabilities: {string.Join(", ", unknown)}. {Usage.Replace(Environment.NewLine, " ")}"]);
        }

        if (target is GrantTarget.ServiceKey)
        {
            var refused = resolved.Intersect(HumanOnly, StringComparer.Ordinal).ToArray();
            if (refused.Length > 0)
            {
                throw new ValidationException([$"Service keys cannot hold {string.Join(", ", refused)}: administration is reserved for human principals."]);
            }

            if (resolved.Count == 0)
            {
                throw new ValidationException([$"A service key needs at least one capability. {Usage.Replace(Environment.NewLine, " ")}"]);
            }
        }

        return [.. resolved];
    }
}
