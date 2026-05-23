using System.Security.Claims;
using System.Text.Json.Nodes;
using Skylab.Cms.Application.Contracts.Schemas;
using Skylab.Cms.Domain.Enums;

namespace Skylab.Cms.Application.Contracts.Policies;

public interface ICollectionPolicy
{
    CollectionKey Key { get; }

    CollectionSchema Schema { get; }

    bool CanEdit(ClaimsPrincipal user, string slug);

    Task<JsonNode> EnrichAsync(string slug, JsonNode data, CancellationToken cancellationToken = default);
}
