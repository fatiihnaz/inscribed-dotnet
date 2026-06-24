using System.Security.Claims;
using System.Text.Json.Nodes;
using Inscribed.Application.Contracts.Schemas;
using Inscribed.Domain.Enums;

namespace Inscribed.Application.Contracts.Policies;

public interface ICollectionPolicy
{
    CollectionKey Key { get; }

    CollectionSchema Schema { get; }

    SlugSource SlugSource { get; }

    bool AllowAnonymousRead => false;

    bool CanEdit(ClaimsPrincipal user, string slug);

    bool CanCreate(ClaimsPrincipal user) => true;

    string? GetSlugSourceValue(JsonNode data) => null;

    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}
