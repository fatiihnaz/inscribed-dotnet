namespace Inscribed.Application.Services.Policies;

public static class CollectionClaims
{
    public static string[] Required(FileCollectionDefinition definition)
    {
        var claims = new SortedSet<string>(StringComparer.Ordinal);

        if (definition.ClaimSlug is { } slug)
        {
            claims.Add(slug.Claim);
        }

        foreach (var rule in new[] { definition.Access?.Read, definition.Access?.Create, definition.Access?.Write })
        {
            foreach (var leaf in rule?.Leaves ?? [])
            {
                claims.Add(leaf.Claim);
            }
        }

        return [.. claims];
    }
}
