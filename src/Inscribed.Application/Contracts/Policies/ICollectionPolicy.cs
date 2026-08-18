using System.Security.Claims;
using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Contracts.Policies;

public interface ICollectionPolicy
{
    string Key { get; }

    CollectionSchema Schema { get; }

    SlugSource SlugSource { get; }

    bool AllowAnonymousRead { get; }

    IReadOnlyList<string> Locales { get; }

    bool AppliesTo(string? tenant) => true;

    bool CanRead(ClaimsPrincipal user) => true;

    bool CanEdit(ClaimsPrincipal user, string slug);

    bool CanCreate(ClaimsPrincipal user);

    IReadOnlyCollection<string> GetVirtualSlugs(ClaimsPrincipal user, string? locale) => [];

    bool OwnsVirtualSlug(ClaimsPrincipal user, string slug) => false;

    string? GetSlugSourceValue(JsonNode data);

    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}
