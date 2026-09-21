namespace Inscribed.Application.Services.Helpers;

public static class GlobalSlugRule
{
    public const string Prefix = "__";

    public static bool IsGlobal(string normalizedSlug) =>
        normalizedSlug.AsSpan(normalizedSlug.LastIndexOf('/') + 1).StartsWith(Prefix, StringComparison.Ordinal);
}
