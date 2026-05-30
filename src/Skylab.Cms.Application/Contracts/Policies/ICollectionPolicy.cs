using System.Security.Claims;
using System.Text.Json.Nodes;
using Skylab.Cms.Application.Contracts.Schemas;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Contracts.Policies;

public interface ICollectionPolicy
{
    CollectionKey Key { get; }

    CollectionSchema Schema { get; }

    SlugSource SlugSource { get; }

    bool AllowAnonymousRead => false;

    bool CanEdit(ClaimsPrincipal user, string slug);

    bool CanCreate(ClaimsPrincipal user) => true;

    IReadOnlyCollection<string> GetVirtualSlugs(ClaimsPrincipal user) => Array.Empty<string>();

    string? GetSlugSourceValue(JsonNode data) => null;

    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}
