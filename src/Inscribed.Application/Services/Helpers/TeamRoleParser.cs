using System.Globalization;
using System.Security.Claims;

namespace Inscribed.Application.Services.Helpers;

public static class TeamRoleParser
{
    private const string LeaderSuffix = "_LEADER";

    public static IReadOnlySet<string> GetLeaderTeamSlugs(ClaimsPrincipal user)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var identity in user.Identities)
        {
            var roleClaimType = identity.RoleClaimType;
            foreach (var role in identity.FindAll(roleClaimType))
            {
                var slug = ToTeamSlug(role.Value);
                if (slug is not null)
                    slugs.Add(slug);
            }
        }

        return slugs;
    }

    public static string? ToTeamSlug(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        if (!role.EndsWith(LeaderSuffix, StringComparison.OrdinalIgnoreCase)) return null;

        var stripped = role[..^LeaderSuffix.Length];
        return string.IsNullOrWhiteSpace(stripped) ? null : stripped.ToLower(CultureInfo.InvariantCulture);
    }
}