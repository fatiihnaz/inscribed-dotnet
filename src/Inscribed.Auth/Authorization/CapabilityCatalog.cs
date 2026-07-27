namespace Inscribed.Auth.Authorization;

public static class CapabilityCatalog
{
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
}
