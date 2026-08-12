using System.Security.Claims;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Helpers;

public static class ClaimSlugDeriver
{
    public static IReadOnlySet<string> Derive(ClaimSlugRule rule, ClaimsPrincipal user, string? locale)
    {
        var slugs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var claim in user.FindAll(rule.Claim))
        {
            if (Match(rule, claim.Value) is not { } matched)
                continue;

            var slug = SlugGenerator.Slugify(matched);

            if (slug.Length == 0)
                continue;

            slugs.Add(string.IsNullOrWhiteSpace(locale) ? slug : $"{slug}-{locale}");
        }

        return slugs;
    }

    private static string? Match(ClaimSlugRule rule, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (rule.EndsWith is { } suffix)
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? value[..^suffix.Length] : null;

        if (rule.StartsWith is { } prefix)
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value[prefix.Length..] : null;

        if (rule.Pattern is { } pattern)
            return pattern.Match(value) is { Success: true } match ? match.Groups[1].Value : null;

        return value;
    }
}
