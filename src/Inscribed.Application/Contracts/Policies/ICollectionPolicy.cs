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

    bool CanEdit(ClaimsPrincipal user, string slug);

    bool CanCreate(ClaimsPrincipal user);

    string? GetSlugSourceValue(JsonNode data);

    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}
